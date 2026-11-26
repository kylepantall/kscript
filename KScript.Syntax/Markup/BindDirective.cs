using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// <c>@bind($query, on: blur, debounce: 300ms, as: int, trim: true)</c> on a form control: the field it binds, and
    /// optional named arguments for when the value is sent and how it is read. <c>@bind('query')</c> and
    /// <c>bind="query"</c> are the same binding with no options. One reader for the renderer and the checker.
    /// </summary>
    public static class BindDirective
    {
        /// <summary>One option a binding takes.</summary>
        public sealed record Option(string Name, string Description, IReadOnlyList<string> Choices);

        /// <summary>Every option, in the order completion lists them.</summary>
        public static IReadOnlyList<Option> Options { get; } = new[]
        {
            new Option("on", "When the value is sent: input (as you type, the default for text), change, blur (when focus leaves) or action (with the next action).", new[] { "input", "change", "blur", "action" }),
            new Option("debounce", "Sends the value once typing has stopped for this long: debounce: 300ms.", null),
            new Option("as", "Reads the value as this type before it reaches the field, and refuses one that isn't: as: int.", new[] { "string", "int", "number", "bool" }),
            new Option("trim", "Trims spaces from both ends on the way in: trim: true.", null),
        };

        /// <summary>A binding as written: the field's path (query, note.title), where it is, and its options.</summary>
        public sealed class Reading
        {
            public string Path { get; internal set; } = string.Empty;
            public TextSpan PathSpan { get; internal set; }
            public List<ArgumentNode> Options { get; } = new List<ArgumentNode>();
            public List<(TextSpan Span, string Message)> Problems { get; } = new List<(TextSpan, string)>();

            /// <summary>The option of this name as written, or null.</summary>
            public ExprNode Get(string name) => Options.FirstOrDefault(o => o.Name == name)?.Value;

            /// <summary>A word option's value (on: blur, as: int), or null.</summary>
            public string Word(string name) => Get(name) switch
            {
                IdentifierNode id => id.Name,
                LiteralNode { Kind: LiteralKind.String, Template: null } text => text.Value,
                _ => null,
            };

            /// <summary>True when trim: true is given.</summary>
            public bool Trim => Get("trim") is LiteralNode { Kind: LiteralKind.Bool, Value: "true" };
        }

        /// <summary>Reads a binding's text: <c>query</c>, <c>$query</c>, <c>'query'</c>, or <c>$query, on: blur</c>.</summary>
        public static Reading Read(string text)
        {
            var reading = new Reading();
            text ??= string.Empty;
            int comma = TopLevelComma(text);
            int start = 0, end = comma < 0 ? text.Length : comma;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (end - start >= 2 && text[start] is '\'' or '"' && text[end - 1] == text[start])
            {
                start++;
                end--;
            }
            if (start < end && text[start] == '$') start++;
            reading.PathSpan = TextSpan.FromBounds(start, end);
            reading.Path = text[start..end];
            if (comma < 0) return reading;

            string rest = text[(comma + 1)..];
            ParseResult parse = ExpressionParser.ParseNamedArguments(rest);
            foreach (SyntaxError error in parse.Errors)
                reading.Problems.Add((new TextSpan(comma + 1 + error.Span.Start, error.Span.Length), "@bind takes the field, then options such as on: blur or trim: true."));
            foreach (ArgumentNode option in parse.Nodes.OfType<ArgumentNode>().Where(a => a.Name != null))
            {
                Shift(option, comma + 1);
                Option known = Options.FirstOrDefault(o => o.Name == option.Name);
                if (known == null)
                {
                    reading.Problems.Add((option.NameSpan, $"@bind has no '{option.Name}:' option. It takes {string.Join(", ", Options.Select(o => o.Name + ":"))}."));
                    continue;
                }
                if (reading.Options.Any(o => o.Name == option.Name))
                {
                    reading.Problems.Add((option.NameSpan, $"{option.Name}: is given twice."));
                    continue;
                }
                reading.Options.Add(option);
                string word = reading.Word(option.Name);
                if (known.Choices != null && (word == null || !known.Choices.Contains(word)))
                    reading.Problems.Add((option.Value.Span, $"{option.Name}: takes {string.Join(", ", known.Choices.Take(known.Choices.Count - 1))} or {known.Choices[^1]}."));
                if (option.Name == "trim" && option.Value is not LiteralNode { Kind: LiteralKind.Bool })
                    reading.Problems.Add((option.Value.Span, "trim: takes true or false."));
            }
            return reading;
        }

        /// <summary>Where a binding's text has options (a comma outside brackets and quotes), or -1.</summary>
        private static int TopLevelComma(string text)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c is '\'' or '"') quote = c;
                else if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == ',' && depth == 0) return i;
            }
            return -1;
        }

        /// <summary>Moves a parsed option's spans from the options' text to the whole binding's.</summary>
        private static void Shift(ExprNode node, int by)
        {
            node.Span = new TextSpan(node.Span.Start + by, node.Span.Length);
            if (node is ArgumentNode argument) argument.NameSpan = new TextSpan(argument.NameSpan.Start + by, argument.NameSpan.Length);
            foreach (ExprNode child in node.Children.Where(c => c != null)) Shift(child, by);
        }
    }
}
