using KScript.Metadata;
using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// One old spelling in a script: where it is, its code, what to write instead, and the change that writes it.
    /// </summary>
    public sealed class OldSpelling
    {
        public OldSpelling(TextSpan span, string code, string message, string fixTitle, params TextChange[] changes)
        {
            Span = span;
            Code = code;
            Message = message;
            Fix = fixTitle == null || changes == null || changes.Length == 0 ? null : new QuickFix(fixTitle, changes);
        }

        public TextSpan Span { get; }
        public string Code { get; }
        public string Message { get; }

        /// <summary>The change that writes the current spelling, or null when there is no safe one.</summary>
        public QuickFix Fix { get; }
    }

    /// <summary>
    /// The spellings a <c>.kscript</c> file no longer has: text as a bare word, a list as one string, yes/no, a
    /// bare function name, a comparison word, a name declared with <c>$</c>, and a model's options as lines of its
    /// block. Each is found here once, so the checker (which reports it, with its fix) and the interpreter (which
    /// refuses to run it) say the same thing. In a .kscript file they are errors; XML keeps the forms that are its own.
    /// </summary>
    public static class OldSpellings
    {
        /// <summary>
        /// True for the codes of an old spelling. In a .kscript file they are errors that no setting turns off;
        /// the other KS6xxx codes are style, which a standard sets.
        /// </summary>
        public static bool IsOldSpelling(string code) =>
            code is DiagnosticCodes.PreferArrayList or DiagnosticCodes.QuoteText or DiagnosticCodes.BareDeclaration
                or DiagnosticCodes.BoolWords or DiagnosticCodes.OneSpelling or DiagnosticCodes.TypeForm;

        /// <summary>The severity an old spelling is reported with: an error in a .kscript file, a warning in XML.</summary>
        public static DiagnosticSeverity Severity(bool codeFile) => codeFile ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;

        #region Expressions

        /// <summary>
        /// Old spellings in parsed expressions. KS6010: a name is declared bare, so a lambda is <c>fn(n) =&gt; $n * 2</c>
        /// and an assignment is <c>total += 1</c>. KS6012: in a .kscript file comparisons are symbols. Spans are
        /// positions in <paramref name="text"/>, the text the nodes were parsed from.
        /// </summary>
        public static IEnumerable<OldSpelling> InExpression(IEnumerable<ExprNode> roots, string text, bool codeFile)
        {
            if (roots == null || text == null) yield break;
            foreach (ExprNode node in roots.Where(n => n != null).SelectMany(n => n.DescendantsAndSelf()))
            {
                switch (node)
                {
                    case BinaryNode binary when codeFile && binary.OperatorText != null && CodeSpellings.ComparisonSymbols.TryGetValue(binary.OperatorText, out string symbol)
                        && SourceIs(text, binary.OperatorSpan, binary.OperatorText):
                        yield return new OldSpelling(binary.OperatorSpan, DiagnosticCodes.OneSpelling,
                            $"Write {symbol}: in a .kscript file comparisons are symbols ('{binary.OperatorText}' is the XML spelling).",
                            $"Change to {symbol}", new TextChange(binary.OperatorSpan, symbol));
                        break;

                    case LambdaNode lambda:
                        foreach (ParameterNode p in lambda.Parameters.Where(p => StartsWithSigil(text, p.Span)))
                        {
                            yield return new OldSpelling(p.Span, DiagnosticCodes.BareDeclaration,
                                $"Declare the parameter as {p.Name}: a name is declared without $, and read with it (fn({p.Name}) => ${p.Name}).",
                                $"Change to {p.Name}", new TextChange(new TextSpan(p.Span.Start, 1), string.Empty));
                        }
                        break;

                    case AssignmentNode assignment when AssignmentRoot(assignment.Target) is VariableNode target && StartsWithSigil(text, target.Span):
                        yield return new OldSpelling(target.Span, DiagnosticCodes.BareDeclaration,
                            $"Assign to {target.Name}, without $: {target.Name} {assignment.Operator} ... ($ reads a value).",
                            $"Change to {target.Name}", new TextChange(new TextSpan(target.Span.Start, 1), string.Empty));
                        break;
                }
            }
        }

        private static ExprNode AssignmentRoot(ExprNode target)
        {
            while (target is MemberNode || target is IndexNode)
            {
                target = target is MemberNode m ? m.Target : ((IndexNode)target).Target;
            }
            return target;
        }

        private static bool StartsWithSigil(string text, TextSpan span) =>
            span.Length > 1 && span.Start < text.Length && text[span.Start] == '$';

        private static bool SourceIs(string source, TextSpan span, string text) =>
            span.End <= source.Length && string.CompareOrdinal(source, span.Start, text, 0, text.Length) == 0 && span.Length == text.Length;

        #endregion

        #region Arguments

        /// <summary>
        /// Old spellings of one argument of a .kscript statement, after <see cref="CodeArguments.Normalise(MarkupElement, ElementInfo)"/>.
        /// Quotes are strings and everything else is code, so: KS6009 text given as a bare word (<c>title: Report</c>)
        /// goes in quotes; KS6006 a list given as one string (<c>use: "log, cors"</c>) is an array; KS6011 yes/no is
        /// true/false; KS6012 a function is named <c>@name</c>. Spans are positions in <paramref name="source"/>, the file's text.
        /// </summary>
        public static IEnumerable<OldSpelling> InArgument(MarkupElement e, MarkupAttribute attr, AttributeInfo a, string source)
        {
            if (e == null || attr == null || a == null || source == null || !attr.IsArgument || attr.Value == null || attr.FormError != null)
            {
                yield break;
            }
            string text = attr.ValueText.Trim();
            if (text.Length == 0)
            {
                yield break;
            }

            if (NameListString(e, attr, a, source) is OldSpelling list)
            {
                yield return list;
                yield break;
            }

            TextSpan written = ValueSpan(attr, source);
            if (attr.Quote == '\0' && CodeArguments.TakesFunctions(ElementInfo.BareName(e.Name), a))
            {
                // A function is @name wherever one is named, as it is in an expression: fn: @send, tools: [@lookup].
                foreach ((string name, int at) in NameList.Split(attr.ValueText))
                {
                    if (!Lexer.IsIdentStart(name[0]) || !name.All(c => Lexer.IsIdentChar(c) || c == '.'))
                    {
                        continue;
                    }
                    var span = new TextSpan(attr.Value.ToSource(at), name.Length);
                    yield return new OldSpelling(span, DiagnosticCodes.OneSpelling,
                        $"Write @{name}: a function is referred to as @name wherever one is named.",
                        $"Write @{name}", new TextChange(new TextSpan(span.Start, 0), "@"));
                }
                yield break;
            }
            if (attr.Quote == '\0' && a.IsNameList)
            {
                // Text in a list is quoted like any other text: to: ['paper.pdf', 'paper.docx']. Names stay bare.
                if (a.Kind is AttributeKind.Template or AttributeKind.Path && text[0] != '$')
                {
                    foreach ((string name, int at) in NameList.Split(attr.ValueText))
                    {
                        int offset = attr.Value.ToSource(at);
                        if (offset > 0 && source[offset - 1] is '\'' or '"') continue;
                        var span = new TextSpan(offset, name.Length);
                        string quotedName = "'" + name + "'";
                        yield return new OldSpelling(span, DiagnosticCodes.QuoteText,
                            $"Write text in quotes: {quotedName}. A bare word names a variable, a function or a type.",
                            $"Write {quotedName}", new TextChange(span, quotedName));
                    }
                }
                yield break;
            }
            if (attr.Quote == '\0' && a.Type == "slash_path" && Lexer.SlashPathEnd(text, 0, text.Length) == text.Length)
            {
                // A path is a value of its own, quoted or not: path: /people/{id}.
                yield break;
            }
            if (attr.Quote == '\0' && CodeArguments.IsText(a.Kind) && !a.IsName && !attr.CodeValue && !attr.QuotedValue && !IsPlainValue(text))
            {
                string quoted = ScriptConverter.Quote(text);
                yield return new OldSpelling(written, DiagnosticCodes.QuoteText,
                    $"Write text in quotes: {attr.Name}: {quoted}. A bare word names a variable, a function or a type.",
                    $"Write {quoted}", new TextChange(written, quoted));
                yield break;
            }
            if (attr.Quote == '\0' && a.Kind == AttributeKind.Bool && text is "yes" or "no" or "on" or "off" or "y" or "n" or "1" or "0")
            {
                string word = text is "yes" or "on" or "y" or "1" ? "true" : "false";
                yield return new OldSpelling(written, DiagnosticCodes.BoolWords,
                    $"Write {word}: booleans are true and false everywhere in .kscript.",
                    $"Write {word}", new TextChange(written, word));
                yield break;
            }
            if (attr.Quote == '"' && !a.IsNameList && a.Kind != AttributeKind.ExpressionList && CodeArguments.IsList(a.Kind, a) && e.Name != "import")
            {
                string array = "[" + text + "]";
                yield return new OldSpelling(written, DiagnosticCodes.PreferArrayList,
                    $"Write the list as an array: {attr.Name}: {array}.",
                    $"Write {array}", new TextChange(written, array));
            }
        }

        /// <summary>KS6006: a list of names written as one string, <c>use: "log, cors"</c>, is an array: <c>use: [log, cors]</c>.</summary>
        private static OldSpelling NameListString(MarkupElement e, MarkupAttribute attr, AttributeInfo a, string source)
        {
            if (!a.IsNameList || e.Name == "import" || attr.Quote != '"' || !attr.ValueText.Contains(','))
            {
                return null;
            }
            List<string> names = NameList.Names(attr.ValueText);
            if (names.Count < 2 || names.Any(n => !n.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or ':' or '-' or '$' or '@' or '/')))
            {
                return null;
            }
            // Names stay bare (use: [log, cors]); text is quoted (to: ['paper.pdf', 'paper.md']).
            bool isText = a.Kind is AttributeKind.Template or AttributeKind.Path;
            string array = "[" + string.Join(", ", names.Select(n => isText && n[0] != '$' ? "'" + n + "'" : n)) + "]";
            int open = attr.Value.SourceSpan.Start - 1;
            bool fixable = open >= 0 && source[open] == '"' && attr.Span.End > attr.Value.SourceSpan.End;
            return new OldSpelling(TrimmedSpan(attr.Value), DiagnosticCodes.PreferArrayList,
                $"Write the list as an array: {attr.Name}: {array}.",
                fixable ? $"Write {array}" : null, fixable ? new[] { new TextChange(TextSpan.FromBounds(open, attr.Span.End), array) } : null);
        }

        /// <summary>A number, a list, a map, true, false or null: a value as it stands, never mistaken for a name.</summary>
        private static bool IsPlainValue(string text) =>
            text is "true" or "false" or "null" || text[0] is '[' or '{' or '(' or '$' or '@' or '\'' or '`'
            || double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

        /// <summary>The value of an argument as written, quotes included.</summary>
        public static TextSpan ValueSpan(MarkupAttribute attr, string text)
        {
            int start = attr.NameSpan.End;
            while (start < attr.Span.End && (char.IsWhiteSpace(text[start]) || text[start] is ':' or '='))
            {
                // Only the first : or = separates the name from the value.
                bool separator = text[start] is ':' or '=';
                start++;
                if (separator)
                {
                    while (start < attr.Span.End && char.IsWhiteSpace(text[start])) start++;
                    break;
                }
            }
            return TextSpan.FromBounds(start, Math.Max(start, attr.Span.End));
        }

        private static TextSpan TrimmedSpan(DecodedText value)
        {
            string v = value.Value;
            int start = 0;
            int end = v.Length;
            while (start < end && char.IsWhiteSpace(v[start])) start++;
            while (end > start && char.IsWhiteSpace(v[end - 1])) end--;
            return TextSpan.FromBounds(value.ToSource(start), value.ToSource(end));
        }

        #endregion

        #region Types

        /// <summary>KS6014: <c>$Repo</c> for a declared type. A type is named bare.</summary>
        public static string TypeIsBare(string name) => $"Write {name}, without $: a type is named bare. $ reads a variable.";

        /// <summary>
        /// KS6014: <c>age: int?</c> as a field. An optional field is marked on its name (<c>age?: int</c>). Null when
        /// the type isn't written that way. <paramref name="name"/> is the field's name as written, with its ? if it has one.
        /// </summary>
        public static string OptionalIsOnName(string name, string type, bool codeFile)
        {
            if (type == null || !type.EndsWith('?') || type.Length < 2)
            {
                return null;
            }
            string bare = name.TrimEnd('?').TrimEnd();
            string plain = type.Substring(0, type.Length - 1).TrimEnd();
            return codeFile ? $"Mark an optional field on its name: {bare}?: {plain}." : $"Mark an optional field on its name: name=\"{bare}?\" type=\"{plain}\".";
        }

        /// <summary>
        /// KS6014: a model's options written as lines of its block (<c>timestamps: true</c>, <c>unique: [email]</c>)
        /// in a .kscript file: the lines, and each option as it is written in the brackets. Empty when there are none.
        /// </summary>
        public static (List<MarkupAttribute> Lines, List<string> Options) ModelOptionLines(MarkupElement e, string text, Func<string, AttributeInfo> attributeOf)
        {
            var options = new List<string>();
            if (e.ContentSpan.Length == 0)
            {
                return (new List<MarkupAttribute>(), options);
            }
            List<MarkupAttribute> lines = e.Attributes
                .Where(a => CodeParser.ModelSettings.Contains(a.Name) && a.NameSpan.Length > 0 && a.NameSpan.Start >= e.ContentSpan.Start && a.Span.End <= e.ContentSpan.End)
                .OrderBy(a => a.NameSpan.Start).ToList();
            foreach (MarkupAttribute line in lines)
            {
                string value = text.Substring(line.Span.Start, line.Span.Length).Trim();
                bool? flag = attributeOf(line.Name)?.Kind != AttributeKind.Bool ? null
                    : value.ToLowerInvariant() switch
                    {
                        "yes" or "y" or "true" or "1" or "on" => true,
                        "no" or "n" or "false" or "0" or "off" => false,
                        _ => null,
                    };
                options.Add(flag == true ? line.Name : flag == false ? line.Name + ": false" : line.Name + ": " + value);
            }
            return (lines, options);
        }

        /// <summary>What a model's option line is told: the model as it is written with its options in the brackets.</summary>
        public static string ModelOptionsInBrackets(string modelName, IEnumerable<string> options) =>
            $"Write a model's options in its brackets: model {modelName}({string.Join(", ", options)}). The block holds its fields, rules and fn.";

        #endregion
    }
}
