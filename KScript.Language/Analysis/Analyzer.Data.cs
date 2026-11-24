using KScript.Data;
using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>KScript.Data in the editor: the columns a query, create or update names are the model's.</summary>
    public sealed partial class Analyzer
    {
        private void CheckModelArgument(ExprNode value, ExpressionRegion region, bool acceptsTableName)
        {
            // A compound expression's inferred shape is a hint for completion. For example, $value ?? 42
            // may still be a model. Diagnose concrete values and declared variables only.
            if (value is not (LiteralNode or ListNode or MapNode or LambdaNode or IdentifierNode or VariableNode))
            {
                return;
            }
            TypeShape shape = Infer(value, region);
            // Both model declarations and their records carry ModelInfo at run time.
            if (shape.Kind is KType.Model or KType.Record || shape.Type is KType.Any or KType.Record
                || (value is VariableNode && !shape.Declared))
            {
                return;
            }
            if (acceptsTableName && shape.Type == KType.String)
            {
                if (value is LiteralNode { Kind: LiteralKind.String, Template: null } text && string.IsNullOrWhiteSpace(text.Value))
                {
                    Report(region.Source.ToSource(value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch, "A table name cannot be blank.");
                }
                return;
            }
            // A map or type returned by an opaque call can still carry a model at run time.
            if (shape.Name == null && (shape.Type == KType.Type || (shape.Type == KType.Map && value is not MapNode)))
            {
                return;
            }
            string expected = acceptsTableName ? "a model or a table name" : "a model";
            Report(region.Source.ToSource(value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                $"This argument needs {expected}, but its type is {shape.Display()}.");
        }

        private static bool IsModelMixin(MarkupElement model) => IsYes(model.GetAttributeValue("mixin"));

        private readonly Dictionary<MarkupElement, IReadOnlyDictionary<string, string>> effectiveModelOptions = new();

        private IReadOnlyDictionary<string, string> ModelOptions(MarkupElement model)
        {
            if (effectiveModelOptions.TryGetValue(model, out var options)) return options;
            // A cycle is diagnosed when checking the base list. Stop here to keep field completion usable.
            effectiveModelOptions[model] = new Dictionary<string, string>();
            ModelComposition.TryReadBases(model.GetAttributeValue("with"), out var parents, out _);
            options = ModelComposition.Merge(parents.Select(parent =>
            {
                MarkupElement declaration = TypeElementNamed(parent.Name);
                return (declaration != null && IsModelElement(declaration) ? ModelOptions(declaration) : null, parent);
            }), model);
            effectiveModelOptions[model] = options;
            return options;
        }

        private void CheckModelBases(MarkupElement model, MarkupAttribute attribute)
        {
            if (!ModelComposition.TryReadBases(attribute.ValueText, out var bases, out string problem))
            {
                Report(TrimmedSpan(attribute.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, problem);
                return;
            }

            bool ReachesModel(MarkupElement parent, HashSet<MarkupElement> seen) =>
                parent == model || seen.Add(parent) && WithNames(parent)
                    .Select(TypeElementNamed).Any(e => e != null && ReachesModel(e, seen));

            foreach (var parent in bases)
            {
                string name = parent.Name;
                MarkupElement declaration = TypeElementNamed(name);
                if (declaration == null || RefinedRoot(declaration) != null || IsEnumName(name))
                    Report(TrimmedSpan(attribute.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"'{name}' must name an imported or declared model, model mixin or record type.");
                else if (ReachesModel(declaration, new HashSet<MarkupElement>()))
                    Report(TrimmedSpan(attribute.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"Extending '{name}' creates a cycle.");
                else
                {
                    Symbol symbol = result.RootScope.Lookup(name, model.Span.Start);
                    int index = attribute.ValueText.IndexOf(name, StringComparison.Ordinal);
                    if (symbol != null && index >= 0)
                        result.References.Add(new SymbolReference(symbol, attribute.Value.ToSource(new TextSpan(index, name.Length)), false, false));
                }
            }
        }

        /// <summary>Each field named in a model option must exist when the model is declared.</summary>
        private void CheckModelOptions(MarkupElement model, ElementInfo element, string modelName)
        {
            if (IsModelMixin(model))
            {
                foreach (MarkupAttribute attribute in model.Attributes.Where(a => a.Name is "table" or "connection" or "auto_create"))
                    Report(attribute.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"A model mixin cannot set {attribute.Name}.");
                return;
            }
            if (model.GetAttribute("with") is MarkupAttribute inherited)
            {
                foreach (var option in ModelOptions(model).Where(o => model.GetAttribute(o.Key) == null))
                {
                    ModelFieldKind kind = element.GetAttribute(option.Key)?.ModelFields ?? ModelFieldKind.None;
                    string[] known = result.ModelOptionFields(model, option.Key).Select(f => f.Name).ToArray();
                    if (kind == ModelFieldKind.Single)
                        CheckColumnName(modelName, option.Value.Trim(), TrimmedSpan(inherited.Value), "field", known, DiagnosticSeverity.Error);
                    else if (kind is ModelFieldKind.Groups or ModelFieldKind.List)
                        foreach ((string name, _) in NameList.Split(option.Value))
                            CheckColumnName(modelName, name, TrimmedSpan(inherited.Value), "field", known, DiagnosticSeverity.Error);
                }
            }
            foreach (MarkupAttribute option in model.Attributes)
            {
                ModelFieldKind kind = element.GetAttribute(option.Name)?.ModelFields ?? ModelFieldKind.None;
                if (kind == ModelFieldKind.None || option.Value == null)
                {
                    continue;
                }

                string[] known = result.ModelOptionFields(model, option.Name).Select(f => f.Name).ToArray();
                if (kind == ModelFieldKind.Single)
                {
                    string key = option.ValueText.Trim();
                    if (key.Length > 0)
                    {
                        CheckColumnName(modelName, key, TrimmedSpan(option.Value), "field", known, DiagnosticSeverity.Error);
                    }
                    continue;
                }
                foreach ((string name, int start) in NameList.Split(option.ValueText))
                {
                    TextSpan span = option.Value.ToSource(new TextSpan(start, name.Length));
                    CheckColumnName(modelName, name, span, "field", known, DiagnosticSeverity.Error);
                }
            }
        }

        /// <summary>
        /// Checks the column names a KScript.Data method is given against the model's fields:
        /// <c>User->where(nmae: 'Joan')</c> and <c>->order_by(agee)</c> are warnings that suggest the closest one.
        /// </summary>
        private void CheckColumns(string method, TypeShape receiver, IReadOnlyList<ArgumentNode> args, ExpressionRegion region)
        {
            TypeShape record = DataColumns.RecordOf(receiver);
            if (record == null || args == null || region == null)
            {
                return;
            }
            bool named = DataColumns.Named.Contains(method);
            bool bare = DataColumns.Bare.Contains(method);
            bool relations = DataColumns.Relations.Contains(method);
            foreach (ArgumentNode a in args)
            {
                if (a.Name != null && named)
                {
                    // fake_many(count: 3, ...) and update's values: count is the method's own.
                    if (method == "fake_many" && a.Name == "count")
                    {
                        continue;
                    }
                    Column(record, a.Name, region.Source.ToSource(a.NameSpan), relationsOnly: false);
                    continue;
                }
                if (a.Name != null || !(bare || relations))
                {
                    continue;
                }
                if (relations && (BareWord(a)?.Word ?? (a.Value as MemberNode)?.DottedName) is string path)
                {
                    string first = path.Split('.')[0];
                    TextSpan whole = region.Source.ToSource(a.Value.Span);
                    Column(record, first, new TextSpan(whole.Start, first.Length), relationsOnly: true);
                    continue;
                }
                if (bare && BareWord(a) is var (word, _, node))
                {
                    Column(record, word, region.Source.ToSource(node.Span), relationsOnly: false);
                }
            }
        }

        /// <summary>
        /// KS5014: a model in a project with migrations whose table the migrations don't make as it is now. Only for a
        /// file on disk, since the saved models and migrations are what kscript db diff reads.
        /// </summary>
        private void CheckDrift(MarkupElement e)
        {
            string name = e.GetAttributeValue("name")?.Trim();
            if (string.IsNullOrEmpty(name) || directory == null || files is EmptyFileSystem || result.Path == null || !File.Exists(result.Path))
            {
                return;
            }
            if (!KScript.Data.Migrations.Drift.Of(directory).TryGetValue(name, out string change))
            {
                return;
            }
            MarkupAttribute attribute = e.GetAttribute("name");
            TextSpan span = attribute?.Value != null ? TrimmedSpan(attribute.Value) : e.NameSpan;
            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.ModelDrift,
                $"The migrations don't make {name}'s table as it is now: it needs a migration to {change}. Run kscript db diff to write it.");
        }

        private void Column(TypeShape record, string name, TextSpan span, bool relationsOnly)
        {
            string[] known = record.Fields.Values.Where(f => !relationsOnly || DataColumns.IsRelation(f)).Select(f => f.Name).ToArray();
            CheckColumnName(record.Name, name, span, relationsOnly ? "relation" : "field", known, DiagnosticSeverity.Warning);
        }

        private void CheckColumnName(string modelName, string name, TextSpan span, string what, string[] known, DiagnosticSeverity severity)
        {
            if (known.Contains(name, StringComparer.Ordinal))
            {
                return;
            }
            string close = Suggestions.Closest(name, known);
            Report(span, severity, DiagnosticCodes.UnknownColumn,
                $"{modelName} has no {what} '{name}'." + (close != null ? $" Did you mean {close}?" : known.Length > 0 ? $" Its {what}s are: {string.Join(", ", known)}." : $" {modelName} has no {what}s."),
                close == null ? null : new[] { new QuickFix($"Change to {close}", new TextChange(span, close)) });
        }
    }
}
