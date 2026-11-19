using System.Text;

using KScript.Syntax.Text;

namespace KScript.Syntax
{
    public enum TemplateSegmentKind
    {
        /// <summary>Literal text (with <c>{{</c> and <c>}}</c> already unescaped).</summary>
        Text,

        /// <summary><c>{ expr }</c>: the value is inserted, HTML-escaped in pages.</summary>
        Interpolation,

        /// <summary><c>{! expr }</c>: the value is inserted unescaped.</summary>
        RawInterpolation,

        /// <summary>A bare <c>$name</c>, <c>$name-&gt;method()</c> or <c>@cmd(...)</c> in running text.</summary>
        Reference,
    }

    public sealed class TemplateSegment
    {
        public TemplateSegmentKind Kind { get; internal set; }

        /// <summary>The whole segment, including braces.</summary>
        public TextSpan Span { get; internal set; }

        /// <summary>For text segments, the literal text.</summary>
        public string Text { get; internal set; }

        /// <summary>For expression segments, the parsed expression.</summary>
        public ParseResult Expression { get; internal set; }

        public ExprNode Node => Expression?.Root;
    }

    /// <summary>A string with embedded expressions: element text or an attribute value.</summary>
    public sealed class Template
    {
        public List<TemplateSegment> Segments { get; } = new List<TemplateSegment>();

        public IEnumerable<TemplateSegment> Expressions => Segments.Where(s => s.Kind != TemplateSegmentKind.Text);

        public IEnumerable<SyntaxError> Errors => Expressions.SelectMany(s => s.Expression.Errors);

        /// <summary>True if the whole string is one <c>{ expr }</c> (an HTML boolean attribute, for example).</summary>
        public bool IsSingleInterpolation =>
            Segments.Count(s => !(s.Kind == TemplateSegmentKind.Text && string.IsNullOrWhiteSpace(s.Text))) == 1
            && Segments.Any(s => s.Kind == TemplateSegmentKind.Interpolation);
    }

    /// <summary>
    /// Splits text into literal runs and expressions. <c>{ expr }</c> and <c>{! expr }</c> are always recognised;
    /// <c>{{</c> and <c>}}</c> are literal braces. With <c>allowBareReferences</c> (element text), <c>$name</c> and
    /// <c>@cmd(</c> are recognised too, so <c>Hello, $name!</c> keeps working.
    /// </summary>
    public static class TemplateParser
    {
        public static Template Parse(string text, bool allowBareReferences)
        {
            Template template = new Template();
            StringBuilder literal = new StringBuilder();
            int literalStart = 0;
            int i = 0;
            text ??= string.Empty;

            void Flush(int end)
            {
                if (literal.Length > 0)
                {
                    template.Segments.Add(new TemplateSegment { Kind = TemplateSegmentKind.Text, Text = literal.ToString(), Span = TextSpan.FromBounds(literalStart, end) });
                    literal.Clear();
                }
                literalStart = end;
            }

            while (i < text.Length)
            {
                char c = text[i];

                if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
                {
                    literal.Append(c);
                    i += 2;
                    continue;
                }

                if (c == '{')
                {
                    Flush(i);
                    bool raw = i + 1 < text.Length && text[i + 1] == '!';
                    int exprStart = i + (raw ? 2 : 1);
                    ParseResult result = ExpressionParser.ParseEmbedded(text, exprStart, out int close);
                    int end = close < 0 ? text.Length : close + 1;
                    template.Segments.Add(new TemplateSegment
                    {
                        Kind = raw ? TemplateSegmentKind.RawInterpolation : TemplateSegmentKind.Interpolation,
                        Span = TextSpan.FromBounds(i, end),
                        Expression = result,
                    });
                    i = end;
                    literalStart = i;
                    continue;
                }

                if (allowBareReferences && IsBareReferenceStart(text, i))
                {
                    Flush(i);
                    ParseResult result = ExpressionParser.ParseBareReference(text, i, out int end);
                    template.Segments.Add(new TemplateSegment { Kind = TemplateSegmentKind.Reference, Span = TextSpan.FromBounds(i, end), Expression = result });
                    i = Math.Max(end, i + 1);
                    literalStart = i;
                    continue;
                }

                literal.Append(c);
                i++;
            }

            Flush(text.Length);
            return template;
        }

        private static bool IsBareReferenceStart(string text, int i)
        {
            char c = text[i];
            if (c != '$' && c != '@')
            {
                return false;
            }
            if (i + 1 >= text.Length || !Lexer.IsIdentStart(text[i + 1]))
            {
                return false;
            }
            if (i > 0 && (Lexer.IsIdentChar(text[i - 1]) || text[i - 1] == '.'))
            {
                // user@example.com, or a$b inside a word: not a reference.
                return false;
            }
            if (c == '$')
            {
                return true;
            }

            int j = i + 1;
            while (j < text.Length && Lexer.IsIdentChar(text[j]))
            {
                j++;
            }
            return j < text.Length && text[j] == '(';
        }
    }
}
