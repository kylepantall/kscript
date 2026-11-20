using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// &lt;type&gt; declarations: the shape of some data, such as an API response. Each one is a constant (<c>$Repo</c>)
    /// whose records have the fields it lists, so <c>->json(as: Repo)</c>, <c>&lt;let type="Repo"&gt;</c> and
    /// <c>repo: Repo</c> parameters complete <c>$repo.</c> with them.
    /// </summary>
    public sealed partial class Analyzer
    {
        private static readonly string[] BuiltInTypes = { "string", "int", "float", "number", "bool", "list", "map", "fn", "date", "any" };

        /// <summary>The records each &lt;type&gt; element makes, by element.</summary>
        private readonly Dictionary<MarkupElement, TypeShape> declaredTypes = new Dictionary<MarkupElement, TypeShape>();

        /// <summary>
        /// Declares every &lt;type&gt; in the root scope before the walk (the runtime hoists them, and types refer to each
        /// other in any order), then fills in their fields.
        /// </summary>
        private void HoistTypes(Scope root)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<MarkupElement> elements = result.Markup.DescendantElements().Where(e => IsKScriptElementName(e, "type") && catalog.GetElement("type")?.Package == "KScript.Core").ToList();
            foreach (MarkupElement e in elements)
            {
                MarkupAttribute nameAttr = e.GetAttribute("name");
                if (nameAttr?.Value == null || !CheckIdentifier(nameAttr.Value))
                {
                    continue;
                }
                string name = nameAttr.ValueText.Trim().TrimStart('$');
                TextSpan span = TrimmedSpan(nameAttr.Value);
                if (BuiltInTypes.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{name}' is a built-in type, so a <type> can't use it as its name.");
                    continue;
                }
                if (!seen.Add(name))
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"A type named '{name}' is already declared.");
                    continue;
                }
                TypeShape instance = new TypeShape(KType.Map) { Name = name, Closed = e.GetAttributeValue("extra")?.Trim() != "keep" };
                Symbol symbol = NewVariable(name, span, e, SymbolKind.Constant, new TypeShape(KType.Type) { Name = name, Instance = instance });
                root.Declare(symbol, 0);
                declaredTypes[e] = instance;
            }

            foreach (KeyValuePair<MarkupElement, TypeShape> type in declaredTypes)
            {
                FillFields(type.Key, type.Value);
                Symbol symbol = result.Symbols.First(s => s.DeclaringElement == type.Key && s.Shape.Instance == type.Value);
                string description = type.Key.GetAttributeValue("description");
                symbol.Description = (description != null ? description + "\n\n" : string.Empty)
                    + "type " + type.Value.Name + " {\n" + string.Concat(type.Value.Fields.Values.Select(f => "  " + f.Name + ": " + f.Shape.Display() + "\n")) + "}";
            }
        }

        /// <summary>The fields of a type: from its sample file, then its fields attribute, then its &lt;field&gt; elements.</summary>
        private void FillFields(MarkupElement e, TypeShape shape)
        {
            string sample = e.GetAttributeValue("sample")?.Trim();
            if (!string.IsNullOrEmpty(sample) && result.JsonFileShape(sample) is TypeShape json)
            {
                TypeShape map = json.Type == KType.List ? json.Element : json;
                foreach (FieldInfo f in map?.Fields.Values ?? Enumerable.Empty<FieldInfo>())
                {
                    shape.Fields[FieldName(f.Name)] = new FieldInfo(FieldName(f.Name), f.Shape, null, "From the sample file.");
                }
            }

            MarkupAttribute fields = e.GetAttribute("fields");
            if (fields?.Value != null)
            {
                foreach ((string name, string type, TextSpan span, string description) in InlineFields(fields.Value))
                {
                    shape.Fields[name] = new FieldInfo(name, TypeNamed(type, span.Start) ?? TypeShape.Unknown, span, description);
                }
            }

            foreach (MarkupElement field in e.ChildElements.Where(c => IsKScriptElementName(c, "field")))
            {
                MarkupAttribute nameAttr = field.GetAttribute("name");
                string name = nameAttr?.ValueText?.Trim().TrimEnd('?').TrimEnd();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                string type = field.GetAttributeValue("type");
                string from = field.GetAttributeValue("from")?.Trim();
                string description = field.GetAttributeValue("description");
                string detail = from != null ? $"Read from {from}." : field.GetAttribute("value") != null ? "Worked out from the other fields." : null;
                description = description != null && detail != null ? description + " " + detail : description ?? detail;
                shape.Fields[name] = new FieldInfo(name, type != null ? TypeNamed(type, field.Span.Start) ?? TypeShape.Unknown : TypeShape.Unknown, TrimmedSpan(nameAttr.Value), description);
            }
        }

        /// <summary>A JSON key as the field name the runtime gives it: 'avatar-url' is avatar_url.</summary>
        private static string FieldName(string key)
        {
            string name = new string(key.Select(c => Lexer.IsIdentChar(c) ? c : '_').ToArray());
            return name.Length > 0 && !Lexer.IsIdentStart(name[0]) ? "_" + name : name;
        }

        /// <summary>The parts of <c>fields="login: string, id?: int = 0"</c>: name, type, the name's span and a description.</summary>
        private static IEnumerable<(string Name, string Type, TextSpan Span, string Description)> InlineFields(DecodedText value)
        {
            foreach ((string part, int start) in SplitTopLevel(value.Value))
            {
                string head = part;
                int eq = part.IndexOf('=');
                if (eq >= 0)
                {
                    head = part.Substring(0, eq);
                }
                int colon = head.IndexOf(':');
                string namePart = colon >= 0 ? head.Substring(0, colon) : head;
                string type = colon >= 0 ? head.Substring(colon + 1).Trim() : null;
                string name = namePart.Trim();
                bool optional = name.EndsWith("?", StringComparison.Ordinal);
                name = name.TrimEnd('?').TrimEnd();
                int at = start + (namePart.Length - namePart.TrimStart().Length);
                if (name.Length == 0)
                {
                    continue;
                }
                yield return (name, type, value.ToSource(new TextSpan(at, name.Length)), optional || eq >= 0 ? "Optional." : null);
            }
        }

        /// <summary>Splits on commas outside brackets and quotes, with each part's start.</summary>
        private static IEnumerable<(string Part, int Start)> SplitTopLevel(string text)
        {
            int depth = 0, start = 0;
            char quote = '\0';
            for (int i = 0; i <= text.Length; i++)
            {
                char c = i < text.Length ? text[i] : ',';
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    quote = c;
                }
                else if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                }
                else if (c == ',' && (depth <= 0 || i == text.Length))
                {
                    if (text.Substring(start, i - start).Trim().Length > 0)
                    {
                        yield return (text.Substring(start, i - start), start);
                    }
                    start = i + 1;
                }
            }
        }

        /// <summary>Walks a &lt;type&gt;: checks its fields attribute and its children (which must be &lt;field&gt;s).</summary>
        private void AnalyzeType(MarkupElement e, ElementInfo info, Scope scope, Scope inner, bool markupMode)
        {
            AnalyzeAttributes(e, info, scope);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            MarkupAttribute fields = e.GetAttribute("fields");
            if (fields?.Value != null)
            {
                foreach ((string part, int start) in SplitTopLevel(fields.Value.Value))
                {
                    CheckInlineField(part, start, fields.Value, seen);
                }
            }
            foreach (MarkupElement child in e.ChildElements)
            {
                if (!IsKScriptElementName(child, "field"))
                {
                    Report(child.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, $"<type> can only hold <field> elements, not <{child.Name}>.");
                    continue;
                }
                MarkupAttribute nameAttr = child.GetAttribute("name");
                string name = nameAttr?.ValueText?.Trim().TrimEnd('?').TrimEnd();
                if (!string.IsNullOrEmpty(name) && !seen.Add(name))
                {
                    Report(TrimmedSpan(nameAttr.Value), DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"The field '{name}' is listed twice.");
                }
            }
            bool hasFields = seen.Count > 0 || e.GetAttribute("sample") != null;
            if (!hasFields)
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, "<type> needs fields: fields=\"name: string, age?: int\", <field> elements, or a sample file.");
            }
            string sample = e.GetAttributeValue("sample")?.Trim();
            if (!string.IsNullOrEmpty(sample) && ResolvePath(sample) is string full && files.FileExists(full) && result.JsonFileShape(sample) == null)
            {
                Report(TrimmedSpan(e.GetAttribute("sample").Value), DiagnosticSeverity.Warning, DiagnosticCodes.InvalidAttributeValue, $"'{sample}' isn't a JSON file the editor can read, so the type's fields from it aren't known.");
            }
            WalkChildren(e.Children, inner, markupMode);
        }

        private void CheckInlineField(string part, int start, DecodedText value, HashSet<string> seen)
        {
            string head = part;
            int eq = part.IndexOf('=');
            if (eq >= 0)
            {
                head = part.Substring(0, eq);
                ParseResult parsed = ExpressionParser.ParseExpression(part.Substring(eq + 1));
                if (!parsed.Success)
                {
                    Report(value.ToSource(new TextSpan(start, part.Length)), DiagnosticSeverity.Error, DiagnosticCodes.ExpressionSyntax,
                        $"The default of '{head.Trim()}' isn't a valid expression: {parsed.Errors[0].Message}");
                }
            }
            int colon = head.IndexOf(':');
            string namePart = colon >= 0 ? head.Substring(0, colon) : head;
            string name = namePart.Trim().TrimEnd('?').TrimEnd();
            int at = start + (namePart.Length - namePart.TrimStart().Length);
            TextSpan nameSpan = value.ToSource(new TextSpan(at, Math.Max(name.Length, 1)));
            if (name.Length == 0 || !Lexer.IsIdentStart(name[0]) || !name.All(Lexer.IsIdentChar))
            {
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{part.Trim()}' isn't a field. Write name, name: type or name?: type.");
                return;
            }
            if (!seen.Add(name))
            {
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"The field '{name}' is listed twice.");
            }
            if (colon >= 0)
            {
                string type = head.Substring(colon + 1);
                int typeStart = start + colon + 1 + (type.Length - type.TrimStart().Length);
                CheckTypeText(type.Trim(), value.ToSource(new TextSpan(typeStart, Math.Max(type.Trim().Length, 1))), value.ToSource(typeStart));
            }
        }

        /// <summary>
        /// A &lt;field&gt; inside a &lt;type&gt;. Its value="..." is worked out from the other fields, so they are in scope
        /// there as variables; default="..." is not.
        /// </summary>
        private void AnalyzeField(MarkupElement e, ElementInfo info, Scope scope)
        {
            AnalyzeAttributes(e, info, scope, skip: "value");
            MarkupAttribute name = e.GetAttribute("name");
            if (name?.Value != null)
            {
                string n = name.ValueText.Trim().TrimEnd('?').TrimEnd();
                if (n.Length == 0 || !Lexer.IsIdentStart(n[0]) || !n.All(Lexer.IsIdentChar))
                {
                    Report(TrimmedSpan(name.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"'{n}' can't be a field name: use letters, digits and _, starting with a letter. To read a key like '{n}', use from=\"{n}\".");
                }
            }
            MarkupAttribute from = e.GetAttribute("from");
            if (from?.Value != null && !IsFieldPath(from.ValueText))
            {
                Report(TrimmedSpan(from.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                    $"'{from.ValueText.Trim()}' isn't a path. Write keys joined by dots, with [0] for a list item: owner.login, items[0].name.");
            }
            MarkupAttribute value = e.GetAttribute("value");
            if (value?.Value == null)
            {
                return;
            }
            if (from != null || e.GetAttribute("default") != null)
            {
                Report(value.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttribute, "A field with a value is worked out from the others, so it can't also have from or default.");
            }

            Scope fieldScope = new Scope(scope, e.Span);
            TypeShape record = declaredTypes.TryGetValue(e.Parent as MarkupElement ?? e, out TypeShape t) ? t : null;
            foreach (FieldInfo f in record?.Fields.Values ?? Enumerable.Empty<FieldInfo>())
            {
                Symbol symbol = new Symbol { Name = f.Name, Kind = SymbolKind.Parameter, Shape = f.Shape, Description = f.Description ?? $"The {f.Name} field.", DeclaringElement = e, DeclarationSpan = f.Declaration ?? default };
                result.Symbols.Add(symbol);
                fieldScope.Declare(symbol, e.Span.Start);
            }
            ExpressionRegion region = AnalyzeExpressionRegion(value.Value, RegionKind.Expression, fieldScope, e, value);
            string n2 = name?.ValueText?.Trim().TrimEnd('?').TrimEnd();
            if (record != null && n2 != null && record.Fields.TryGetValue(n2, out FieldInfo computed) && computed.Shape.IsVague && region?.Parse?.Root != null)
            {
                record.Fields[n2] = new FieldInfo(computed.Name, Infer(region.Parse.Root, region), computed.Declaration, computed.Description);
            }
        }

        private static bool IsFieldPath(string text)
        {
            string s = text.Trim();
            if (s.Length == 0 || s[0] == '.' || s[0] == '[' || s.EndsWith(".", StringComparison.Ordinal) || s.Contains("..", StringComparison.Ordinal))
            {
                return false;
            }
            int i = 0;
            while (i < s.Length)
            {
                int open = s.IndexOf('[', i);
                if (open < 0)
                {
                    return s.IndexOf(']', i) < 0;
                }
                int close = s.IndexOf(']', open);
                if (close < 0 || !int.TryParse(s.AsSpan(open + 1, close - open - 1), out int index) || index < 0)
                {
                    return false;
                }
                i = close + 1;
                if (i < s.Length && s[i] != '.' && s[i] != '[')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>A type="..." attribute (on let, const, set, field): the type must be one KScript knows.</summary>
        private void CheckTypeAttribute(MarkupAttribute attr)
        {
            string text = attr.ValueText.Trim();
            CheckTypeText(text, TrimmedSpan(attr.Value), attr.Value.ToSource(0));
        }

        private void CheckTypeText(string text, TextSpan span, int offset)
        {
            string name = text.TrimEnd('?').TrimEnd();
            while (name.EndsWith("[]", StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - 2).TrimEnd();
            }
            if (name.Length == 0 || !Lexer.IsIdentStart(name[0]) || !name.All(Lexer.IsIdentChar))
            {
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{text}' isn't a type. Write a name such as string, int or Repo, with [] for a list and ? to allow null.");
                return;
            }
            if (TypeNamed(text, offset) == null)
            {
                IEnumerable<string> known = BuiltInTypes.Concat(TypeNames());
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                    $"Unknown type '{name}'. Use one of: {string.Join(", ", BuiltInTypes)}, or declare it with <type name=\"{name}\" fields=\"...\" /> or <enum name=\"{name}\" values=\"...\" />.",
                    SuggestFix(span, name, known, n => n));
            }
        }

        /// <summary>The names of the &lt;type&gt;s and &lt;enum&gt;s visible from the root of the document.</summary>
        private IEnumerable<string> TypeNames() =>
            result.Symbols.Where(s => s.EnumValues != null || s.Shape.Type == KType.Type).Select(s => s.Name).Concat(DeclaredEnums()).Distinct();

        private TypeShape TypeNamed(string text, int offset) => (types ??= new TypeInference(catalog, result)).TypeNamed(text, offset);

        /// <summary>A bare type name in a call (<c>as: Repo</c>) is the type, not a word: a reference to it.</summary>
        private bool ReferenceBareType(IdentifierNode node, ExpressionRegion region, Scope scope)
        {
            TextSpan span = region.Source.ToSource(node.Span);
            Symbol symbol = scope.Lookup(node.Name, span.Start);
            if (symbol?.Shape.Type != KType.Type)
            {
                return false;
            }
            result.References.Add(new SymbolReference(symbol, span, false, false));
            return true;
        }

        /// <summary>A field a record's type doesn't have: <c>$repo.stras</c>.</summary>
        private void CheckRecordField(TypeShape target, MemberNode member, ExpressionRegion region)
        {
            if (target.Type != KType.Map || target.Name == null || !target.Closed || member.Name.Length == 0 || target.Fields.ContainsKey(member.Name))
            {
                return;
            }
            TextSpan span = region.Source.ToSource(member.NameSpan);
            string close = Suggestions.Closest(member.Name, target.Fields.Keys);
            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownField,
                $"{target.Name} has no field '{member.Name}'." + (close != null ? $" Did you mean {close}?" : $" Its fields are: {string.Join(", ", target.Fields.Keys)}."),
                SuggestFix(span, member.Name, target.Fields.Keys, n => n));
        }
    }
}
