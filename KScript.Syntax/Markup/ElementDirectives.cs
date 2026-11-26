using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// <c>@if($note)</c> and <c>@each($tasks, as: task, at: i)</c> on a tag: the tag is drawn only while the condition
    /// holds, or once for each item. They stand for <c>&lt;if test="$note"&gt;</c> and
    /// <c>&lt;for each="task" in="$tasks" at="i"&gt;</c> around the tag, and both parsers (the editor's and the page
    /// runtime's) wrap the tag that way, so the checker and the renderer see the same tree. With both on one tag,
    /// the loop is outside and the condition is checked for each item.
    /// </summary>
    public static class ElementDirectives
    {
        public const string If = "if";
        public const string Each = "each";

        /// <summary>An @each as written: where the list, the item's name and the position's name are.</summary>
        public sealed class EachReading
        {
            public TextSpan List { get; internal set; }
            public TextSpan? As { get; internal set; }
            public TextSpan? At { get; internal set; }
            public List<(TextSpan Span, string Message)> Problems { get; } = new List<(TextSpan, string)>();
        }

        /// <summary>Reads <c>$tasks, as: task, at: i</c>.</summary>
        public static EachReading ReadEach(string text)
        {
            var reading = new EachReading();
            text ??= string.Empty;
            List<(int Start, int End)> parts = Split(text);
            (int ls, int le) = Trim(text, parts[0]);
            reading.List = TextSpan.FromBounds(ls, le);
            if (le == ls) reading.Problems.Add((new TextSpan(0, text.Length), "@each needs a list and a name for each item, such as @each($tasks, as: task)."));
            foreach ((int start, int end) in parts.Skip(1))
            {
                (int s, int e) = Trim(text, (start, end));
                int colon = text.IndexOf(':', s, e - s);
                string name = colon < 0 ? string.Empty : text[s..colon].Trim();
                (int vs, int ve) = colon < 0 ? (e, e) : Trim(text, (colon + 1, e));
                string value = text[vs..ve].TrimStart('$');
                if (value.Length > 0 && text[vs] == '$') vs++;
                var span = TextSpan.FromBounds(vs, ve);
                if (name is not ("as" or "at"))
                {
                    reading.Problems.Add((TextSpan.FromBounds(s, e), "@each takes the list, then as: for each item's name and at: for its position, such as @each($tasks, as: task, at: i)."));
                    continue;
                }
                if (value.Length == 0 || !value.All(c => Lexer.IsIdentChar(c)) || !Lexer.IsIdentStart(value[0]))
                {
                    reading.Problems.Add((TextSpan.FromBounds(s, e), $"{name}: takes a name, such as {name}: {(name == "as" ? "task" : "i")}."));
                    continue;
                }
                if ((name == "as" ? reading.As : reading.At) != null)
                {
                    reading.Problems.Add((TextSpan.FromBounds(s, e), $"{name}: is given twice."));
                    continue;
                }
                if (name == "as") reading.As = span;
                else reading.At = span;
            }
            if (reading.As == null && le > ls)
                reading.Problems.Add((TextSpan.FromBounds(ls, le), "@each needs as: with a name for each item, such as @each($tasks, as: task)."));
            return reading;
        }

        /// <summary>The text split at commas outside brackets and quotes.</summary>
        private static List<(int, int)> Split(string text)
        {
            var parts = new List<(int, int)>();
            int depth = 0, start = 0;
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
                else if (c == ',' && depth == 0)
                {
                    parts.Add((start, i));
                    start = i + 1;
                }
            }
            parts.Add((start, text.Length));
            return parts;
        }

        private static (int, int) Trim(string text, (int Start, int End) part)
        {
            (int s, int e) = part;
            while (s < e && char.IsWhiteSpace(text[s])) s++;
            while (e > s && char.IsWhiteSpace(text[e - 1])) e--;
            return (s, e);
        }

        /// <summary>Part of a decoded text, keeping each character's place in the source.</summary>
        private static DecodedText Slice(DecodedText text, TextSpan span) =>
            new DecodedText(text.Value.Substring(span.Start, span.Length), Enumerable.Range(span.Start, span.Length + 1).Select(text.ToSource).ToArray());

        /// <summary>Wraps every tag with @if or @each in the &lt;if&gt; or &lt;for&gt; it stands for. Problems go to <paramref name="report"/>.</summary>
        public static void Wrap(List<MarkupNode> nodes, MarkupElement parent, Action<TextSpan, string> report)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is not MarkupElement element) continue;
                Wrap(element.Children, element, report);
                MarkupNode replacement = element;
                MarkupAttribute condition = element.Attributes.FirstOrDefault(a => a.Name == If && a.IsDirective && a.Value != null);
                MarkupAttribute loop = element.Attributes.FirstOrDefault(a => a.Name == Each && a.IsDirective && a.Value != null);
                if (condition == null && loop == null) continue;
                if (condition != null)
                {
                    element.Attributes.Remove(condition);
                    var wrapper = Wrapper(If, element, condition);
                    wrapper.Attributes.Add(Attribute(wrapper, "test", condition.Value, condition.Span, condition.NameSpan));
                    replacement = Adopt(wrapper, replacement);
                }
                if (loop != null)
                {
                    element.Attributes.Remove(loop);
                    var wrapper = Wrapper("for", element, loop);
                    EachReading reading = ReadEach(loop.Value.Value);
                    foreach ((TextSpan span, string message) in reading.Problems) report(loop.Value.ToSource(span), message);
                    if (reading.As is TextSpan asSpan) wrapper.Attributes.Add(Attribute(wrapper, "each", Slice(loop.Value, asSpan), loop.Span, loop.NameSpan));
                    wrapper.Attributes.Add(Attribute(wrapper, "in", Slice(loop.Value, reading.List), loop.Span, loop.NameSpan));
                    if (reading.At is TextSpan atSpan) wrapper.Attributes.Add(Attribute(wrapper, "at", Slice(loop.Value, atSpan), loop.Span, loop.NameSpan));
                    replacement = Adopt(wrapper, replacement);
                }
                replacement.Parent = parent;
                nodes[i] = replacement;
            }
        }

        private static MarkupElement Wrapper(string name, MarkupElement element, MarkupAttribute directive) => new MarkupElement
        {
            Name = name,
            NameSpan = directive.NameSpan,
            Span = element.Span,
            StartTagSpan = directive.Span,
            StartTagClosed = true,
            ContentSpan = element.Span,
            IsDirectiveWrapper = true,
        };

        private static MarkupElement Adopt(MarkupElement wrapper, MarkupNode child)
        {
            child.Parent = wrapper;
            wrapper.Children.Add(child);
            return wrapper;
        }

        private static MarkupAttribute Attribute(MarkupElement owner, string name, DecodedText value, TextSpan span, TextSpan nameSpan) => new MarkupAttribute
        {
            Name = name,
            NameSpan = nameSpan,
            Span = span,
            Value = value,
            Quote = '"',
            Owner = owner,
        };
    }
}
