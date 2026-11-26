using System.Text.RegularExpressions;
using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// <c>@pending('crumple', for: 'delete')</c> on an HTML tag: classes the browser adds to the tag while one of the
    /// component's actions runs, and takes off again when it finishes or fails. The first argument is a class or a list
    /// of classes; <c>for:</c> names the method (or a list of methods) whose actions count. Without <c>for:</c>, any
    /// action of the component counts. Read by the renderer and the checker alike.
    /// </summary>
    public static class PendingDirective
    {
        /// <summary>The attribute a <c>@pending(...)</c> directive becomes.</summary>
        public const string Attribute = "pending";

        /// <summary>The parts of a directive, as spans of its text, and what is wrong with it.</summary>
        public sealed class Reading
        {
            /// <summary>The classes: a quoted name, or an expression giving a name or a list of names.</summary>
            public TextSpan? Classes { get; internal set; }

            /// <summary>The <c>for:</c> value: a quoted method name or a list of them, or null when every action counts.</summary>
            public TextSpan? For { get; internal set; }

            public List<(TextSpan Span, string Message)> Problems { get; } = new List<(TextSpan, string)>();
        }

        /// <summary>Reads a <c>@pending(...)</c> directive's text.</summary>
        public static Reading Read(string text)
        {
            text ??= string.Empty;
            var reading = new Reading();
            var parts = Split(text);
            if (parts.Count == 0)
            {
                reading.Problems.Add((new TextSpan(0, text.Length), "@pending needs a class, such as @pending('saving') or @pending('crumple', for: 'delete')."));
                return reading;
            }
            foreach ((TextSpan whole, string name, TextSpan value) in parts)
            {
                if (name == null)
                {
                    if (reading.Classes != null)
                        reading.Problems.Add((whole, "@pending takes one class or a list of classes, then for: with the method: @pending(['busy', 'dim'], for: 'save')."));
                    else if (reading.For != null)
                        reading.Problems.Add((whole, "Put the class first: @pending('crumple', for: 'delete')."));
                    else reading.Classes = whole;
                    continue;
                }
                if (name != "for")
                {
                    reading.Problems.Add((whole, $"@pending has no {name}: option. Name the method whose action counts with for: 'delete'."));
                    continue;
                }
                if (reading.For != null)
                {
                    reading.Problems.Add((whole, "Give for: once, with a list for several methods: for: ['save', 'delete']."));
                    continue;
                }
                if (value.Length == 0) reading.Problems.Add((whole, "for: needs a method name, such as for: 'delete'."));
                else reading.For = value;
            }
            if (reading.Classes == null && reading.Problems.Count == 0)
                reading.Problems.Add((new TextSpan(0, text.Length), "@pending needs a class before for:, such as @pending('crumple', for: 'delete')."));
            return reading;
        }

        /// <summary>
        /// True when a handler runs the component's method of this name: <c>$delete</c>, <c>call: $delete</c>
        /// (with or without <c>with:</c>) or a statement that calls <c>$this-&gt;delete(...)</c>.
        /// </summary>
        public static bool HandlerCalls(string handler, string method)
        {
            if (string.IsNullOrEmpty(handler) || string.IsNullOrEmpty(method)) return false;
            string name = Regex.Escape(method);
            return Regex.IsMatch(handler, @"^\s*\$" + name + @"\s*$") ||
                Regex.IsMatch(handler, @"(^|,)\s*call\s*:\s*\$" + name + @"\s*(,|$)") ||
                Regex.IsMatch(handler, @"\$this\s*->\s*" + name + @"\s*\(");
        }

        /// <summary>The top-level arguments: each one's whole span, its name (or null) and its value's span.</summary>
        private static List<(TextSpan Whole, string Name, TextSpan Value)> Split(string text)
        {
            var parts = new List<(TextSpan, string, TextSpan)>();
            var lexer = new Lexer(text, 0, text.Length);
            int depth = 0, start = 0;
            void Close(int at)
            {
                TextSpan whole = Trim(text, start, at);
                if (whole.Length == 0) return;
                string segment = text.Substring(whole.Start, whole.Length);
                var named = Regex.Match(segment, @"^([A-Za-z_][A-Za-z0-9_]*)\s*:(?!:)");
                if (named.Success)
                {
                    parts.Add((whole, named.Groups[1].Value, Trim(text, whole.Start + named.Length, whole.End)));
                    return;
                }
                parts.Add((whole, null, whole));
            }
            for (Token token = lexer.Next(); token.Kind != TokenKind.EndOfInput; token = lexer.Next())
            {
                if (token.Is("(") || token.Is("[") || token.Is("{")) depth++;
                else if (token.Is(")") || token.Is("]") || token.Is("}")) depth--;
                else if (depth == 0 && token.Is(","))
                {
                    Close(token.Span.Start);
                    start = token.Span.End;
                }
            }
            Close(text.Length);
            return parts;
        }

        private static TextSpan Trim(string text, int start, int end)
        {
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            return TextSpan.FromBounds(start, end);
        }
    }
}
