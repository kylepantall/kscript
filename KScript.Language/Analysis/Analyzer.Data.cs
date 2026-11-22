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
            IEnumerable<string> known = record.Fields.Values.Where(f => !relationsOnly || DataColumns.IsRelation(f)).Select(f => f.Name).ToList();
            if (known.Contains(name, StringComparer.Ordinal))
            {
                return;
            }
            string close = Suggestions.Closest(name, known);
            string what = relationsOnly ? "relation" : "field";
            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownColumn,
                $"{record.Name} has no {what} '{name}'." + (close != null ? $" Did you mean {close}?" : known.Any() ? $" Its {what}s are: {string.Join(", ", known)}." : $" {record.Name} has no {what}s."),
                SuggestFix(span, name, known, n => n));
        }
    }
}
