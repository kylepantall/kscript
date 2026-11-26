using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    // The one way to write a declared type (KS6014): its name is bare (Repo, never $Repo), calling it makes a value
    // (Repo($data), OutOfStock(item: 'tea')), an optional field is marked on its name (age?: int), and a
    // declaration's options go in its brackets (model User(timestamps) { }).
    public sealed partial class Analyzer
    {
        /// <summary>The types, models, enums, errors and jobs other files declare, by file and name.</summary>
        private readonly HashSet<(string Path, string Name)> importedTypes = new HashSet<(string Path, string Name)>();

        /// <summary>True for the name of a declared type: a type, model, enum, error type or job, declared here or imported.</summary>
        private bool IsTypeSymbol(Symbol symbol) =>
            symbol != null && (TypeInference.IsTypeSymbol(symbol) || symbol.IsTypeDeclaration);

        /// <summary>A bare name that is a declared type (<c>as: Repo</c>, <c>User->find(1)</c>, <c>Size.small</c>) is the type: a reference to it.</summary>
        private bool ReferenceDeclaredType(IdentifierNode node, ExpressionRegion region, Scope scope)
        {
            TextSpan span = region.Source.ToSource(node.Span);
            Symbol symbol = scope.Lookup(node.Name, span.Start);
            if (!IsTypeSymbol(symbol))
            {
                return false;
            }
            result.References.Add(new SymbolReference(symbol, span, false, false));
            return true;
        }

        /// <summary>KS6014: <c>$Repo</c> for a declared type. A type is named bare.</summary>
        private void CheckTypeIsBare(Symbol symbol, TextSpan nameSpan)
        {
            if (symbol?.IsTypeDeclaration != true || nameSpan.Start == 0 || source.Text[nameSpan.Start - 1] != '$')
            {
                return;
            }
            TextSpan written = TextSpan.FromBounds(nameSpan.Start - 1, nameSpan.End);
            Report(written, DiagnosticSeverity.Warning, DiagnosticCodes.TypeForm,
                OldSpellings.TypeIsBare(symbol.Name),
                new[] { new QuickFix($"Write {symbol.Name}", new TextChange(new TextSpan(written.Start, 1), string.Empty)) });
        }

        /// <summary>
        /// <c>Repo($data)</c>, <c>Repo(name: 'x')</c>, <c>Size('small')</c>: calling a type makes a value of it. A type
        /// takes the data to check or its fields by name; the names must be fields it has.
        /// </summary>
        private bool AnalyzeTypeCall(CallNode call, IdentifierNode id, ExpressionRegion region, Scope scope, bool piped)
        {
            TextSpan span = region.Source.ToSource(id.Span);
            Symbol symbol = scope.Lookup(id.Name, span.Start);
            if (!IsTypeSymbol(symbol) || symbol.Shape.Type is KType.ErrorType or KType.JobType)
            {
                return false;
            }
            result.References.Add(new SymbolReference(symbol, span, false, false));
            int positional = call.Arguments.Count(a => a.Name == null) + (piped ? 1 : 0);
            bool named = call.Arguments.Any(a => a.Name != null);
            TypeShape instance = symbol.Shape.Type == KType.Type ? symbol.Shape.Instance : null;
            bool record = instance?.Type == KType.Map && instance.RefinementBase == null;
            if (positional > 1 || (positional == 1 && named) || (!record && symbol.SourcePath == null && positional != 1))
            {
                Report(region.Source.ToSource(call.Span), DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount, record
                    ? $"{id.Name}() takes the data to check ({id.Name}($data)) or its fields by name ({id.Name}(field: value))."
                    : $"{id.Name}() takes one value to check: {id.Name}($value).");
                return true;
            }
            foreach (ArgumentNode argument in call.Arguments)
            {
                string expected = symbol.Name;
                if (argument.Name != null)
                {
                    if (instance == null || !instance.Fields.TryGetValue(argument.Name, out FieldInfo field)) continue;
                    if (field.PrivateTo != null)
                    {
                        Report(region.Source.ToSource(argument.NameSpan), DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter,
                            $"'{argument.Name}' is private to {field.PrivateTo} and can't be passed in.");
                        continue;
                    }
                    if (field.OwnState)
                    {
                        Report(region.Source.ToSource(argument.NameSpan), DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter,
                            $"'{argument.Name}' is {id.Name}'s own state and can't be passed in. construct takes: {string.Join(", ", instance.Fields.Values.Where(f => !f.OwnState).Select(f => f.Name))}.");
                        continue;
                    }
                    expected = field.ValueType;
                    if (expected != null && !ExpectsShapeError(region.Element))
                        CheckTypedLiteral(argument.Value, expected, region.Source, scope, span.Start);
                }
                if (expected == null) continue;
                if (argument.Value is IdentifierNode word && pendingBare.Contains(argument) && RawTextKindOf(expected, scope, span.Start) != null)
                {
                    pendingBare.Remove(argument);
                    CheckLiteralFits(word.Name, expected, region.Source.ToSource(word.Span), scope, span.Start);
                }
                ClaimRawListItems(argument, expected, region, scope, span.Start);
            }
            if (record && instance.Closed && instance.Fields.Count > 0)
            {
                foreach (ArgumentNode arg in call.Arguments.Where(a => a.Name != null && !instance.Fields.ContainsKey(a.Name)))
                {
                    TextSpan nameSpan = region.Source.ToSource(arg.NameSpan);
                    string close = Suggestions.Closest(arg.Name, instance.Fields.Keys);
                    Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter,
                        $"{id.Name} has no field '{arg.Name}'." + (close != null ? $" Did you mean {close}?" : $" Its fields are: {string.Join(", ", instance.Fields.Keys)}."),
                        SuggestFix(nameSpan, arg.Name, instance.Fields.Keys, n => n));
                }
            }
            return true;
        }

        private static bool ExpectsShapeError(MarkupElement element)
        {
            for (; element != null; element = element.Parent)
            {
                string filter = element.Name switch
                {
                    "expect" => element.GetAttributeValue("throws"),
                    "expect_error" => element.GetAttributeValue("type") ?? "Error",
                    _ => null,
                };
                if (filter != null && KScript.Runtime.ErrorTypes.BuiltIn["Shape"].IsA(filter.Trim())) return true;
            }
            return false;
        }

        /// <summary>
        /// <c>Repo->from($data)</c> and <c>OutOfStock->new(item: 'tea')</c> are gone: calling the type makes the value.
        /// True when the method was one of them (and reported).
        /// </summary>
        private bool CheckRetiredConstructor(MethodCallNode m, TypeShape receiver, ExpressionRegion region)
        {
            bool from = m.Name == "from" && receiver.Type == KType.Type;
            if (!from && !(m.Name == "new" && receiver.Type == KType.ErrorType))
            {
                return false;
            }
            string name = receiver.Name ?? "Name";
            // From the end of the type's name to the end of the method's: Repo|->from|($data).
            TextSpan arrow = TextSpan.FromBounds(region.Source.ToSource(m.Target.Span).End, region.Source.ToSource(m.NameSpan).End);
            Report(region.Source.ToSource(m.NameSpan), DiagnosticSeverity.Error, DiagnosticCodes.UnknownMethod,
                $"->{m.Name}() is gone: call the type to make a value of it, {name}({(from ? "$data" : "...")}).",
                m.ArgumentList == null ? null : new[] { new QuickFix($"Write {name}(...)", new TextChange(arrow, string.Empty)) });
            return true;
        }

        /// <summary>
        /// KS6014: <c>age: int?</c> as a field. An optional field is marked on its name (<c>age?: int</c>) in a type,
        /// model, error and job alike; <c>T?</c> is for a value that may be null where there is no field name to mark,
        /// such as a parameter or the items of a list.
        /// </summary>
        private void CheckOptionalIsOnName(string name, TextSpan nameSpan, string type, TextSpan typeSpan)
        {
            if (!type.EndsWith('?') || type.Length < 2)
            {
                return;
            }
            string bare = name.TrimEnd('?').TrimEnd();
            string plain = type.Substring(0, type.Length - 1).TrimEnd();
            var changes = new List<TextChange> { new TextChange(TextSpan.FromBounds(typeSpan.Start + plain.Length, typeSpan.End), string.Empty) };
            if (!name.EndsWith('?'))
            {
                changes.Add(new TextChange(new TextSpan(nameSpan.End, 0), "?"));
            }
            Report(typeSpan, DiagnosticSeverity.Warning, DiagnosticCodes.TypeForm,
                OldSpellings.OptionalIsOnName(name, type, IsCodeFile),
                new[] { new QuickFix($"Write {bare}?: {plain}", changes.ToArray()) });
        }

        /// <summary>
        /// KS6014: a model's options written as lines of its block (<c>timestamps: yes</c>, <c>unique: [email]</c>).
        /// Options go in the brackets for every declaration: <c>model User(timestamps, unique: [email]) { }</c>, and the
        /// block holds only fields, rules and fn. The fix moves every option line of the model at once.
        /// </summary>
        private void CheckOptionsAreInBrackets(MarkupElement e)
        {
            if (!IsCodeFile || !IsModelElement(e) || e.ContentSpan.Length == 0)
            {
                return;
            }
            string text = source.Text;
            (List<MarkupAttribute> lines, List<string> options) = OldSpellings.ModelOptionLines(e, text, name => AttributeOf(e, name));
            if (lines.Count == 0)
            {
                return;
            }

            var changes = new List<TextChange>();
            // True while every line so far, from the '{' on, was an option: the blank line after them goes too.
            bool leading = true;
            int previousEnd = e.ContentSpan.Start;
            foreach (MarkupAttribute line in lines)
            {
                // The whole line goes when the option is all it holds; otherwise only the option and its ';'.
                int start = line.NameSpan.Start;
                int end = line.Span.End;
                while (end < text.Length && text[end] is ' ' or '\t') end++;
                if (end < text.Length && text[end] == ';')
                {
                    end++;
                    while (end < text.Length && text[end] is ' ' or '\t') end++;
                }
                int lineStart = start;
                while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t') lineStart--;
                bool ownLine = (lineStart == 0 || text[lineStart - 1] == '\n') && (end >= text.Length || text[end] is '\n' or '\r');
                if (ownLine)
                {
                    start = lineStart;
                    if (end < text.Length && text[end] == '\r') end++;
                    if (end < text.Length && text[end] == '\n') end++;
                }
                leading = leading && ownLine && string.IsNullOrWhiteSpace(text.Substring(previousEnd, start - previousEnd));
                previousEnd = end;
                changes.Add(new TextChange(TextSpan.FromBounds(start, end), string.Empty));
            }
            if (leading)
            {
                int blank = previousEnd;
                while (true)
                {
                    int p = blank;
                    while (p < text.Length && text[p] is ' ' or '\t' or '\r') p++;
                    if (p >= text.Length || text[p] != '\n') break;
                    blank = p + 1;
                }
                if (blank > previousEnd)
                {
                    TextChange last = changes[changes.Count - 1];
                    changes[changes.Count - 1] = new TextChange(TextSpan.FromBounds(last.Span.Start, blank), string.Empty);
                }
            }

            // model User { }: add the brackets after the name; model User(table: 'people') { }: add to them.
            MarkupAttribute nameAttr = e.GetAttribute("name");
            int open = nameAttr != null ? nameAttr.Span.End : -1;
            if (open < 0 || open >= e.ContentSpan.Start)
            {
                return;
            }
            string joined = string.Join(", ", options);
            if (open < text.Length && text[open] == '(')
            {
                int close = MatchingParen(text, open, e.ContentSpan.Start);
                if (close < 0)
                {
                    return;
                }
                bool empty = text.Substring(open + 1, close - open - 1).Trim().Length == 0;
                int at = close;
                while (at > open + 1 && char.IsWhiteSpace(text[at - 1])) at--;
                bool trailingComma = at > open + 1 && text[at - 1] == ',';
                changes.Add(new TextChange(new TextSpan(at, 0), (empty ? string.Empty : trailingComma ? " " : ", ") + joined));
            }
            else
            {
                changes.Add(new TextChange(new TextSpan(open, 0), "(" + joined + ")"));
            }

            foreach (MarkupAttribute line in lines)
            {
                Report(TextSpan.FromBounds(line.NameSpan.Start, line.Span.End), DiagnosticSeverity.Warning, DiagnosticCodes.TypeForm,
                    OldSpellings.ModelOptionsInBrackets(nameAttr.ValueText.Trim(), options),
                    new[] { new QuickFix("Move the options into the brackets", changes.ToArray()) });
            }
        }

        private AttributeInfo AttributeOf(MarkupElement e, string name) => catalog.GetElement(e.Name)?.GetAttribute(name);

        /// <summary>The ')' that closes the '(' at <paramref name="open"/>, skipping strings and nested brackets; -1 when there is none before <paramref name="limit"/>.</summary>
        private static int MatchingParen(string text, int open, int limit)
        {
            int depth = 0;
            for (int i = open; i < limit && i < text.Length; i++)
            {
                char c = text[i];
                if (c is '\'' or '"')
                {
                    int closeQuote = i + 1;
                    while (closeQuote < text.Length && text[closeQuote] != c)
                    {
                        closeQuote += c == '"' && text[closeQuote] == '\\' ? 2 : 1;
                    }
                    i = closeQuote;
                }
                else if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}')
                {
                    depth--;
                    if (depth == 0) return c == ')' ? i : -1;
                }
            }
            return -1;
        }
    }
}
