using System.Text;

using KScript.Metadata;
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
        public static Template Parse(string text, bool allowBareReferences) => Parse(text, allowBareReferences, TextLiterals.None);

        /// <summary>As <see cref="Parse(string, bool)"/>, keeping LaTeX maths (or all of the text) as written.</summary>
        public static Template Parse(string text, bool allowBareReferences, TextLiterals literals)
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

            if (literals == TextLiterals.All)
            {
                if (text.Length > 0) template.Segments.Add(new TemplateSegment { Kind = TemplateSegmentKind.Text, Text = text, Span = TextSpan.FromBounds(0, text.Length) });
                return template;
            }

            while (i < text.Length)
            {
                char c = text[i];

                if (literals == TextLiterals.Maths && c == '\\' && i + 1 < text.Length && (text[i + 1] == '(' || text[i + 1] == '['))
                {
                    // \( x^{2} \): LaTeX maths, braces and all, is kept for the document package to read.
                    string close = text[i + 1] == '(' ? "\\)" : "\\]";
                    int end = text.IndexOf(close, i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? text.Length : end + 2;
                    literal.Append(text, i, stop - i);
                    i = stop;
                    continue;
                }

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

        /// <summary>
        /// Calls whose text is theirs to read, so "double quotes" given to them are not filled in first:
        /// <c>@sql("... where id = {$id}")</c> sends each <c>{ }</c> as a parameter instead of pasting it into the SQL.
        /// </summary>
        public static bool KeepsText(string callName) => callName == "sql";

        /// <summary>
        /// The parts of text in "double quotes" inside an expression: <paramref name="text"/> from
        /// <paramref name="start"/> to <paramref name="end"/> (the quotes left out). <c>$name</c>, <c>$name.field</c>,
        /// <c>@command(...)</c> and every brace are filled in: a brace always holds an expression
        /// (<c>{ $n * 2 }</c>, <c>{ User->count() }</c>, <c>{ 1 + 2 }</c>), as it does in a statement's text. <c>{{</c> and
        /// <c>}}</c> are single braces, and 'single quotes' keep braces as written (<c>'/users/{id}'</c>). Spans are positions in <paramref name="text"/>. Null when nothing is filled in.
        /// </summary>
        private const string LiteralBraceHint = " In double quotes, { } holds an expression: write {{ and }} for braces, or use 'single quotes', which keep the text as written.";

        public static Template ParseQuoted(string text, int start, int end)
        {
            Template template = new Template();
            StringBuilder literal = new StringBuilder();
            int literalStart = start;
            bool any = false;

            void Flush(int at)
            {
                if (literal.Length > 0)
                {
                    template.Segments.Add(new TemplateSegment { Kind = TemplateSegmentKind.Text, Text = LineJoins.Join(literal.ToString()), Span = TextSpan.FromBounds(literalStart, at) });
                    literal.Clear();
                }
                literalStart = at;
            }

            int i = start;
            while (i < end)
            {
                char c = text[i];
                if (Quotes.IsEscape(text, i, end, '"'))
                {
                    // \" and \\ are the two escapes; the value is used when something else is filled in too.
                    literal.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if ((c == '{' || c == '}') && i + 1 < end && text[i + 1] == c)
                {
                    literal.Append(c);
                    i += 2;
                    any = true;
                    continue;
                }

                if (c == '{')
                {
                    // A brace always holds an expression, whatever it opens with: { $n * 2 }, { User->count() }, { 1 + 2 }.
                    ParseResult result = ExpressionParser.ParseEmbedded(text, i + 1, out int close);
                    if (close < 0 || close >= end)
                    {
                        // Not closed inside the string: read it again as far as the string goes, so the error says so.
                        result = ExpressionParser.ParseEmbedded(text.Substring(0, end), i + 1, out close);
                        if (close < 0) close = end - 1;
                    }
                    if (result.Errors.Count > 0)
                    {
                        SyntaxError first = result.Errors[0];
                        result.Errors[0] = new SyntaxError(first.Span, first.Words + Wording.Final(LiteralBraceHint), first.Code);
                    }
                    Flush(i);
                    template.Segments.Add(new TemplateSegment { Kind = TemplateSegmentKind.Interpolation, Span = TextSpan.FromBounds(i, close + 1), Expression = result });
                    i = close + 1;
                    literalStart = i;
                    any = true;
                    continue;
                }
                if (i + 1 < end && IsBareReferenceStart(text, i))
                {
                    ParseResult result = ExpressionParser.ParseBareReference(text, i, out int stop);
                    if (stop > i && stop <= end)
                    {
                        Flush(i);
                        template.Segments.Add(new TemplateSegment { Kind = TemplateSegmentKind.Reference, Span = TextSpan.FromBounds(i, stop), Expression = result });
                        i = stop;
                        literalStart = i;
                        any = true;
                        continue;
                    }
                }

                literal.Append(c);
                i++;
            }

            Flush(end);
            return any ? template : null;
        }

        private static bool IsBareReferenceStart(string text, int i)
        {
            char c = text[i];
            if (c != '$' && c != '@')
            {
                return false;
            }
            int nameStart = i + (c == '$' && i + 1 < text.Length && text[i + 1] == '.' ? 2 : 1);
            if (nameStart >= text.Length || !Lexer.IsIdentStart(text[nameStart]))
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
