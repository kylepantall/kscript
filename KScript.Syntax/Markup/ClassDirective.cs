using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// <c>@class(...)</c> on an HTML tag: classes switched on by conditions, added to the tag's own <c>class</c>.
    /// Two forms, read by the renderer and the checker alike:
    /// class first, <c>@class(done: $todo.done, 'is-open': $open)</c>, and condition first,
    /// <c>@class({ $todo.done: ['done', 'struck'], [$todo.done, $open]: 'expanded' })</c>, where a list of
    /// conditions needs all of them true and the value is a class or a list of classes.
    /// </summary>
    public static class ClassDirective
    {
        /// <summary>The attribute a <c>@class(...)</c> directive becomes.</summary>
        public const string Attribute = "class-if";

        /// <summary>One entry: the condition and the classes, each as a span of the directive's text.</summary>
        /// <param name="Condition">The condition's text: an expression giving a bool, or a list of them.</param>
        /// <param name="Classes">The classes: a name written as text (class first), or an expression giving a name or a list of names.</param>
        /// <param name="ClassName">The class name when written as text (class first), else null.</param>
        public sealed record Entry(TextSpan Condition, TextSpan Classes, string ClassName);

        /// <summary>The entries of a directive, and what is wrong with it.</summary>
        public sealed class Reading
        {
            public bool ConditionFirst { get; internal set; }
            public List<Entry> Entries { get; } = new List<Entry>();
            public List<(TextSpan Span, string Message)> Problems { get; } = new List<(TextSpan, string)>();
        }

        /// <summary>Reads a <c>@class(...)</c> directive's text into its entries.</summary>
        public static Reading Read(string text)
        {
            var reading = new Reading();
            text ??= string.Empty;
            int start = 0, end = text.Length;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (start == end)
            {
                reading.Problems.Add((new TextSpan(0, text.Length), "@class needs classes and conditions, such as @class(done: $todo.done)."));
                return reading;
            }
            if (text[start] == '{')
            {
                if (text[end - 1] != '}')
                {
                    reading.Problems.Add((TextSpan.FromBounds(start, end), "This { is never closed."));
                    return reading;
                }
                reading.ConditionFirst = true;
                start++;
                end--;
            }
            foreach ((TextSpan key, TextSpan value) in Pairs(text, start, end, reading))
            {
                if (reading.ConditionFirst)
                {
                    reading.Entries.Add(new Entry(key, value, null));
                    continue;
                }
                string name = text.Substring(key.Start, key.Length);
                if (name.Length >= 2 && name[0] is '\'' or '"' && name[^1] == name[0]) name = name[1..^1];
                if (!IsClassName(name))
                {
                    reading.Problems.Add((key, $"'{name}' isn't a class name. Quote a name with other characters ('is-open': $open), or put conditions first: @class({{ $open: 'open' }})."));
                    continue;
                }
                reading.Entries.Add(new Entry(value, key, name));
            }
            return reading;
        }

        /// <summary>True for text that can be a CSS class name: letters, digits, '-' and '_'.</summary>
        public static bool IsClassName(string name) =>
            name.Length > 0 && !char.IsDigit(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');

        /// <summary>The top-level <c>key: value</c> pairs between start and end, split at commas outside brackets and strings.</summary>
        private static IEnumerable<(TextSpan Key, TextSpan Value)> Pairs(string text, int start, int end, Reading reading)
        {
            var lexer = new Lexer(text, start, end);
            int depth = 0, entryStart = start, colon = -1;
            var pairs = new List<(TextSpan, TextSpan)>();
            void Close(int at)
            {
                TextSpan whole = Trim(text, entryStart, at);
                if (whole.Length == 0) return; // a trailing comma
                if (colon < 0)
                {
                    reading.Problems.Add((whole, "Each entry is a class and a condition, such as done: $todo.done."));
                    return;
                }
                TextSpan key = Trim(text, entryStart, colon), value = Trim(text, colon + 1, at);
                if (key.Length == 0 || value.Length == 0)
                {
                    reading.Problems.Add((whole, "Each entry is a class and a condition, such as done: $todo.done."));
                    return;
                }
                pairs.Add((key, value));
            }
            int ternaries = 0;
            for (Token token = lexer.Next(); token.Kind != TokenKind.EndOfInput && token.Span.Start < end; token = lexer.Next())
            {
                if (token.Is("(") || token.Is("[") || token.Is("{")) depth++;
                else if (token.Is(")") || token.Is("]") || token.Is("}")) depth--;
                else if (depth == 0 && token.Is("?")) ternaries++;
                else if (depth == 0 && token.Is(":"))
                {
                    if (ternaries > 0) ternaries--;
                    else if (colon < 0) colon = token.Span.Start;
                }
                else if (depth == 0 && token.Is(","))
                {
                    Close(token.Span.Start);
                    entryStart = token.Span.End;
                    colon = -1;
                    ternaries = 0;
                }
            }
            Close(end);
            return pairs;
        }

        private static TextSpan Trim(string text, int start, int end)
        {
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            return TextSpan.FromBounds(start, end);
        }
    }
}
