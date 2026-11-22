using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>What kind of file a document is, which decides how it is parsed.</summary>
    public enum DocumentKind
    {
        /// <summary>A plain KScript script (<c>.xml</c>, <c>.ks.xml</c>, <c>.ks</c>): strict XML.</summary>
        Script,

        /// <summary>An HTML page served by <c>kscript serve</c>: forgiving HTML5 with KScript control elements inline.</summary>
        Page,

        /// <summary>A Markdown page: optional front matter, then Markdown with KScript elements and <c>{ }</c> inline.</summary>
        Markdown,
    }

    /// <summary>How a script is written: as XML (<c>.ks.xml</c>), or in the brace-and-newline format (<c>.kscript</c>).</summary>
    public enum SourceSyntax
    {
        Xml,
        Code,
    }

    /// <summary>
    /// Text with XML/HTML entities decoded, and a map from each decoded character back to its offset in the document,
    /// so positions inside an attribute or a text node can be reported exactly.
    /// </summary>
    public sealed class DecodedText
    {
        private readonly int[] map;

        public DecodedText(string value, int[] map)
        {
            Value = value;
            this.map = map;
        }

        public string Value { get; }

        /// <summary>The span of the raw (undecoded) text in the document.</summary>
        public TextSpan SourceSpan => TextSpan.FromBounds(map[0], map[map.Length - 1]);

        /// <summary>The document offset of a decoded index; an index equal to the length maps to the end.</summary>
        public int ToSource(int index) => map[Math.Clamp(index, 0, map.Length - 1)];

        public TextSpan ToSource(TextSpan span) => TextSpan.FromBounds(ToSource(span.Start), ToSource(span.End));

        /// <summary>The decoded index of a document offset, or -1 if the offset is outside this text.</summary>
        public int FromSource(int offset)
        {
            if (offset < map[0] || offset > map[map.Length - 1])
            {
                return -1;
            }
            for (int i = 0; i < map.Length - 1; i++)
            {
                if (map[i] >= offset)
                {
                    return i;
                }
            }
            return map.Length - 1;
        }

        public static DecodedText Plain(string value, int sourceStart)
        {
            int[] m = new int[value.Length + 1];
            for (int i = 0; i < m.Length; i++)
            {
                m[i] = sourceStart + i;
            }
            return new DecodedText(value, m);
        }

        public override string ToString() => Value;
    }

    public abstract class MarkupNode
    {
        public TextSpan Span { get; internal set; }
        public MarkupElement Parent { get; internal set; }
    }

    public sealed class MarkupText : MarkupNode
    {
        public DecodedText Content { get; internal set; }
        public bool IsCData { get; internal set; }
        public string Value => Content.Value;
        public bool IsWhitespace => string.IsNullOrWhiteSpace(Content.Value);
    }

    public sealed class MarkupComment : MarkupNode
    {
        /// <summary>The text between &lt;!-- and --&gt;.</summary>
        public string Text { get; internal set; } = string.Empty;

        /// <summary>A doc comment, &lt;!--- ... --&gt;: documents the element after it, like /// in a .kscript file.</summary>
        public bool IsDoc => !Plain && Text.StartsWith("-", StringComparison.Ordinal) && !Text.StartsWith("--", StringComparison.Ordinal);

        /// <summary>A // or /* */ comment from a .kscript file: never a doc comment, even when it starts with '-'.</summary>
        internal bool Plain { get; init; }

        /// <summary>A /* */ comment from a .kscript file, so converting back writes it the same way.</summary>
        public bool IsBlock { get; init; }

        /// <summary>The doc comment's text with the leading '-' and the common indentation removed, or null.</summary>
        public string DocText
        {
            get
            {
                if (!IsDoc)
                {
                    return null;
                }
                string[] lines = Text.Substring(1).Replace("\r\n", "\n").Split('\n');
                IEnumerable<string> rest = lines.Skip(1).Select(l => l.TrimEnd());
                int indent = rest.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
                return string.Join("\n", new[] { lines[0].Trim() }.Concat(rest.Select(l => l.Length >= indent ? l.Substring(indent) : l.TrimStart()))).Trim();
            }
        }
    }

    public sealed class MarkupAttribute
    {
        public string Name { get; internal set; }
        public TextSpan NameSpan { get; internal set; }

        /// <summary>The value, or null for an HTML boolean attribute written without <c>=</c>.</summary>
        public DecodedText Value { get; internal set; }

        /// <summary>The whole attribute, from its name to its closing quote.</summary>
        public TextSpan Span { get; internal set; }

        /// <summary>The quote character, or '\0' when the value was unquoted.</summary>
        public char Quote { get; internal set; }

        public MarkupElement Owner { get; internal set; }

        /// <summary>
        /// True for the expression of a shorthand start tag such as <c>&lt;if $ready&gt;</c>, which has no name or
        /// quotes in the source (<see cref="NameSpan"/> is empty).
        /// </summary>
        public bool IsShorthand { get; internal set; }

        /// <summary>True for a value written in the argument list of a .kscript statement: <c>name(key: value)</c>.</summary>
        public bool IsArgument { get; internal set; }

        /// <summary>True once <see cref="CodeArguments"/> has turned what was written into the text the element reads.</summary>
        public bool Normalised { get; internal set; }

        /// <summary>True for an argument written as a string in quotes.</summary>
        public bool QuotedValue { get; internal set; }

        /// <summary>True for an argument that gives text as a value (<c>title: $name</c>).</summary>
        public bool CodeValue { get; internal set; }

        /// <summary>What is wrong with the way the argument is written, or null.</summary>
        public string FormError { get; internal set; }

        public string ValueText => Value?.Value;
    }

    public sealed class MarkupElement : MarkupNode
    {
        public string Name { get; internal set; }
        public TextSpan NameSpan { get; internal set; }
        public List<MarkupAttribute> Attributes { get; } = new List<MarkupAttribute>();
        public List<MarkupNode> Children { get; } = new List<MarkupNode>();

        /// <summary>The start tag, from '&lt;' to '&gt;' (or as far as it got if unfinished).</summary>
        public TextSpan StartTagSpan { get; internal set; }

        /// <summary>False while the start tag is still being typed (no closing '&gt;').</summary>
        public bool StartTagClosed { get; internal set; }

        public bool SelfClosing { get; internal set; }

        /// <summary>The name in the end tag, if there is one.</summary>
        public TextSpan? EndNameSpan { get; internal set; }

        /// <summary>The span between the start and end tags.</summary>
        public TextSpan ContentSpan { get; internal set; }

        public MarkupAttribute GetAttribute(string name) =>
            Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        public string GetAttributeValue(string name) => GetAttribute(name)?.ValueText;

        public IEnumerable<MarkupElement> ChildElements => Children.OfType<MarkupElement>();

        /// <summary>True if the element has no child elements, only text (or nothing).</summary>
        public bool HasOnlyText => Children.All(c => !(c is MarkupElement));

        /// <summary>The element's text content when it is a single run of text (ignoring comments).</summary>
        public MarkupText SingleText
        {
            get
            {
                List<MarkupText> texts = Children.OfType<MarkupText>().ToList();
                return HasOnlyText && texts.Count == 1 ? texts[0] : null;
            }
        }

        /// <summary>
        /// The element's documentation: its description attribute, or a &lt;!--- doc comment --&gt; just before it.
        /// </summary>
        public string Documentation
        {
            get
            {
                string description = GetAttributeValue("description");
                if (description != null)
                {
                    return description;
                }
                List<MarkupNode> siblings = Parent?.Children;
                int index = siblings?.IndexOf(this) ?? -1;
                for (int i = index - 1; i >= 0; i--)
                {
                    switch (siblings[i])
                    {
                        case MarkupText t when t.IsWhitespace:
                            continue;
                        case MarkupComment c when c.IsDoc:
                            return c.DocText;
                    }
                    break;
                }
                return null;
            }
        }

        public IEnumerable<MarkupElement> Ancestors()
        {
            for (MarkupElement e = Parent; e != null; e = e.Parent)
            {
                yield return e;
            }
        }

        public override string ToString() => "<" + Name + ">";
    }

    /// <summary>A <c>key: value</c> line of Markdown front matter.</summary>
    public sealed class FrontMatterEntry
    {
        public string Key { get; internal set; }
        public TextSpan KeySpan { get; internal set; }
        public DecodedText Value { get; internal set; }
    }

    public sealed class FrontMatter
    {
        public TextSpan Span { get; internal set; }
        public List<FrontMatterEntry> Entries { get; } = new List<FrontMatterEntry>();
    }

    /// <summary>A parsed document: top-level nodes, front matter for Markdown, and markup errors.</summary>
    public sealed class MarkupDocument
    {
        public SourceText Source { get; internal set; }
        public DocumentKind Kind { get; internal set; }

        /// <summary>Xml for markup files; Code for <c>.kscript</c> files, which <see cref="CodeParser"/> turns into the same tree.</summary>
        public SourceSyntax Syntax { get; internal set; }

        public List<MarkupNode> Children { get; } = new List<MarkupNode>();
        public FrontMatter FrontMatter { get; internal set; }
        public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();

        public IEnumerable<MarkupElement> RootElements => Children.OfType<MarkupElement>();

        public IEnumerable<MarkupNode> DescendantNodes()
        {
            Stack<IEnumerator<MarkupNode>> stack = new Stack<IEnumerator<MarkupNode>>();
            stack.Push(Children.GetEnumerator());
            while (stack.Count > 0)
            {
                IEnumerator<MarkupNode> it = stack.Peek();
                if (!it.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                yield return it.Current;
                if (it.Current is MarkupElement e)
                {
                    stack.Push(e.Children.GetEnumerator());
                }
            }
        }

        public IEnumerable<MarkupElement> DescendantElements() => DescendantNodes().OfType<MarkupElement>();

        /// <summary>The innermost element whose span contains the offset.</summary>
        public MarkupElement FindElementAt(int offset)
        {
            MarkupElement found = null;
            IEnumerable<MarkupNode> level = Children;
            while (true)
            {
                MarkupElement next = level.OfType<MarkupElement>().FirstOrDefault(e => offset > e.Span.Start && (offset < e.Span.End || (e.Span.End == Source.Length && offset == Source.Length)));
                if (next == null)
                {
                    return found;
                }
                found = next;
                level = next.Children;
            }
        }
    }
}
