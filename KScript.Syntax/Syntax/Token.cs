using KScript.Syntax.Text;

namespace KScript.Syntax
{
    public enum TokenKind
    {
        EndOfInput,
        Error,
        Number,
        String,

        /// <summary><c>$name</c>.</summary>
        Variable,

        /// <summary><c>@name</c>.</summary>
        Command,

        /// <summary>A bare word, including word operators and keywords (and, or, not, eq, lt, fn, match, true...).</summary>
        Identifier,

        /// <summary>Operators and punctuation; the text says which.</summary>
        Punctuation,

        /// <summary>A regular expression: <c>/^[a-z]+$/i</c>. The text is the whole literal, slashes and flags included.</summary>
        Regex,

        /// <summary>An email address written without quotes: <c>kyle@example.com</c>.</summary>
        Email,

        /// <summary>A UUID written without quotes: <c>3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f</c>.</summary>
        Uuid,

        /// <summary>A URL written without quotes: <c>https://example.com/docs</c>.</summary>
        Url,

        /// <summary>A length of time written without quotes: <c>30s</c>, <c>500ms</c>, <c>1h30m</c>.</summary>
        Duration,
    }

    public readonly struct Token
    {
        public Token(TokenKind kind, string text, TextSpan span, string error = null, string tag = null, TextSpan? errorSpan = null)
        {
            Kind = kind;
            Text = text;
            Span = span;
            Error = error;
            Tag = tag;
            ErrorSpan = errorSpan ?? span;
        }

        public TokenKind Kind { get; }

        /// <summary>The token's text. For strings this is the unquoted value; for variables and commands, the name without the sigil.</summary>
        public string Text { get; }

        public TextSpan Span { get; }

        /// <summary>A lexer error, for <see cref="TokenKind.Error"/> and unterminated strings.</summary>
        public string Error { get; }

        /// <summary>Where <see cref="Error"/> applies: the token, or a line inside a ``` block.</summary>
        public TextSpan ErrorSpan { get; }

        /// <summary>For a ``` block, what follows the opening fence (<c>sql</c>, <c>&lt;Repo&gt;</c>); otherwise null.</summary>
        public string Tag { get; }

        public bool Is(string punctuation) => Kind == TokenKind.Punctuation && Text == punctuation;

        public bool IsWord(string word) => Kind == TokenKind.Identifier && Text == word;

        public override string ToString() => $"{Kind} '{Text}' {Span}";
    }

    /// <summary>
    /// Turns expression text into tokens on demand, so the parser can stop in the middle of running text
    /// (for <c>Hello $name, it's late</c>) without the rest of the text having to be valid.
    /// </summary>
    public sealed class Lexer
    {
        // Longest first.
        private static readonly string[] Operators =
        {
            "===", "!==", "??=", "**", "==", "!=", "->", "|>", "=>", "??", "?.", "..", "+=", "-=", "*=", "/=", ">=", "<=", "&&", "||",
            "(", ")", "[", "]", "{", "}", ",", ":", ";", ".", "?", "+", "-", "*", "/", "%", "=", ">", "<", "!", "|", "&",
        };

        public static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "and", "or", "not", "eq", "ne", "lt", "le", "gt", "ge", "in", "step", "true", "false", "null", "fn", "match",
        };

        private readonly string text;
        private readonly int end;

        public Lexer(string text, int start = 0, int end = -1)
        {
            this.text = text ?? string.Empty;
            Position = start;
            this.end = end < 0 ? this.text.Length : end;
        }

        public int Position { get; private set; }

        public string Text => text;

        /// <summary>The last token returned: a / after a value is division, anywhere else it starts a regex.</summary>
        private Token previous;

        public Token Next()
        {
            Token token = NextToken();
            previous = token;
            return token;
        }

        /// <summary>True where a value can start, so a / there opens a regex rather than dividing.</summary>
        private bool ValueCanStart()
        {
            switch (previous.Kind)
            {
                case TokenKind.EndOfInput:
                    return true;
                case TokenKind.Punctuation:
                    return previous.Text is not (")" or "]" or "}");
                case TokenKind.Identifier:
                    return Keywords.Contains(previous.Text) && previous.Text is not ("true" or "false" or "null");
                default:
                    return false;
            }
        }

        private Token NextToken()
        {
            while (Position < end)
            {
                if (char.IsWhiteSpace(text[Position]))
                {
                    Position++;
                    continue;
                }
                // /* ... */ is a comment anywhere in an expression ('/' then '*' is never an operator pair).
                if (text[Position] == '/' && Position + 1 < end && text[Position + 1] == '*')
                {
                    int close = text.IndexOf("*/", Position + 2, end - Position - 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        int at = Position;
                        Position = end;
                        return new Token(TokenKind.EndOfInput, string.Empty, TextSpan.FromBounds(at, end), "This comment is missing its closing */.");
                    }
                    Position = close + 2;
                    continue;
                }
                // // to the end of the line, for expressions written over several lines ('//' is never an operator).
                if (text[Position] == '/' && Position + 1 < end && text[Position + 1] == '/' && !AfterUrlScheme(text, Position))
                {
                    while (Position < end && text[Position] != '\n')
                    {
                        Position++;
                    }
                    continue;
                }
                break;
            }

            if (Position >= end)
            {
                return new Token(TokenKind.EndOfInput, string.Empty, new TextSpan(end, 0));
            }

            int start = Position;
            char c = text[Position];

            if (c == '/' && ValueCanStart() && ReadRegex(start) is Token regex)
            {
                return regex;
            }

            // kyle@example.com and 3f2a8c1e-...: never valid code before these were literals, so nothing changes meaning.
            if (char.IsLetterOrDigit(c) && !(previous.Kind == TokenKind.Punctuation && previous.Text is "." or "?." or "->"))
            {
                int literalEnd = UuidEnd(start);
                if (literalEnd > 0)
                {
                    Position = literalEnd;
                    return Make(TokenKind.Uuid, start);
                }
                literalEnd = UrlEnd(text, start, end);
                if (literalEnd > 0)
                {
                    Position = literalEnd;
                    return Make(TokenKind.Url, start);
                }
                literalEnd = EmailEnd(start);
                if (literalEnd > 0)
                {
                    Position = literalEnd;
                    return Make(TokenKind.Email, start);
                }
            }

            if (char.IsDigit(c))
            {
                // 30s, 500ms, 1h30m: a number with a unit was never valid code, so nothing changes meaning.
                int durationEnd = DurationEnd(text, start, end);
                if (durationEnd > 0)
                {
                    Position = durationEnd;
                    return Make(TokenKind.Duration, start);
                }
                while (Position < end && char.IsDigit(text[Position]))
                {
                    Position++;
                }
                if (Position + 1 < end && text[Position] == '.' && char.IsDigit(text[Position + 1]))
                {
                    Position++;
                    while (Position < end && char.IsDigit(text[Position]))
                    {
                        Position++;
                    }
                }
                return Make(TokenKind.Number, start);
            }

            if (c == '\'' || c == '"')
            {
                int close = Quotes.Close(text, start, end);
                if (close < 0)
                {
                    Position = end;
                    return new Token(TokenKind.String, Quotes.Decode(text, start + 1, end, c), TextSpan.FromBounds(start, end),
                        $"This string is missing its closing {c}.");
                }
                Position = close + 1;
                // A backslash at the end of a line joins it to the next one: the break isn't part of the string.
                return new Token(TokenKind.String, Quotes.Decode(text, start + 1, close, c), TextSpan.FromBounds(start, Position));
            }

            if (c == '`' && StringBlock.Read(text, start, end) is StringBlock block)
            {
                Position = block.End;
                return new Token(TokenKind.String, block.Value, TextSpan.FromBounds(start, block.End), block.Error, block.Tag, block.Error != null ? block.ErrorSpan : null);
            }

            if ((c == '$' || c == '@') && Position + 1 < end && IsIdentStart(text[Position + 1]))
            {
                Position++;
                int nameStart = Position;
                while (Position < end && IsIdentChar(text[Position]))
                {
                    Position++;
                }
                // @math.sqrt(2): a command from a package (or script) imported with as="math".
                while (c == '@' && Position + 1 < end && text[Position] == '.' && IsIdentStart(text[Position + 1]))
                {
                    Position++;
                    while (Position < end && IsIdentChar(text[Position]))
                    {
                        Position++;
                    }
                }
                return new Token(c == '$' ? TokenKind.Variable : TokenKind.Command, text.Substring(nameStart, Position - nameStart),
                    TextSpan.FromBounds(start, Position));
            }

            if (c == '~' && Position + 1 < end && IsIdentStart(text[Position + 1]))
            {
                Position++;
                while (Position < end && IsIdentChar(text[Position]))
                {
                    Position++;
                }
                return new Token(TokenKind.Error, text.Substring(start, Position - start), TextSpan.FromBounds(start, Position),
                    $"'~' is no longer used for arrays. Write ${text.Substring(start + 1, Position - start - 1)}.");
            }

            if (IsIdentStart(c))
            {
                while (Position < end && IsIdentChar(text[Position]))
                {
                    Position++;
                }
                return Make(TokenKind.Identifier, start);
            }

            foreach (string op in Operators)
            {
                if (Position + op.Length <= end && string.CompareOrdinal(text, Position, op, 0, op.Length) == 0)
                {
                    Position += op.Length;
                    return Make(TokenKind.Punctuation, start);
                }
            }

            Position++;
            return new Token(TokenKind.Error, c.ToString(), new TextSpan(start, 1), $"Unexpected character '{c}'.");
        }

        /// <summary>
        /// <c>/pattern/flags</c> starting at <paramref name="start"/>: a / inside [...] or after \ doesn't end it, and it
        /// must close on the same line. Null when it doesn't close, so the / is read as an operator.
        /// </summary>
        private Token? ReadRegex(int start)
        {
            int i = start + 1;
            bool inClass = false;
            for (; i < end && text[i] != '\n'; i++)
            {
                char ch = text[i];
                if (ch == '\\')
                {
                    i++;
                    continue;
                }
                if (ch == '[') inClass = true;
                else if (ch == ']') inClass = false;
                else if (ch == '/' && !inClass) break;
            }
            if (i >= end || text[i] != '/' || i == start + 1)
            {
                return null;
            }
            i++;
            while (i < end && char.IsAsciiLetterLower(text[i]))
            {
                i++;
            }
            Position = i;
            return Make(TokenKind.Regex, start);
        }

        /// <summary>The end of a UUID (8-4-4-4-12 hex digits) at <paramref name="start"/>, or -1.</summary>
        private int UuidEnd(int start)
        {
            int[] groups = { 8, 4, 4, 4, 12 };
            int i = start;
            for (int g = 0; g < groups.Length; g++)
            {
                if (g > 0)
                {
                    if (i >= end || text[i] != '-') return -1;
                    i++;
                }
                for (int n = 0; n < groups[g]; n++, i++)
                {
                    if (i >= end || !char.IsAsciiHexDigit(text[i])) return -1;
                }
            }
            // A method call may follow: 3f2a...->to_text().
            bool arrow = i + 1 < end && text[i] == '-' && text[i + 1] == '>';
            return i < end && (IsIdentChar(text[i]) || (text[i] == '-' && !arrow)) ? -1 : i;
        }

        /// <summary>The schemes a URL written without quotes can start with.</summary>
        public static readonly string[] UrlSchemes = { "https", "http", "wss", "ws", "ftp" };

        /// <summary>
        /// The end of a URL written without quotes (https://example.com/docs?page=2) at <paramref name="start"/>, or -1. It
        /// runs to a space, a quote, a comma, a bracket, ;, -> (a method call) or the end. A . ! or ? at the very end is left out, so a
        /// URL can end a sentence.
        /// </summary>
        public static int UrlEnd(string text, int start, int end)
        {
            if (start > 0 && (IsIdentChar(text[start - 1]) || text[start - 1] is '$' or '@' or '.')) return -1;
            foreach (string scheme in UrlSchemes)
            {
                int after = start + scheme.Length;
                if (after + 3 >= end || string.Compare(text, start, scheme, 0, scheme.Length, StringComparison.OrdinalIgnoreCase) != 0
                    || string.CompareOrdinal(text, after, "://", 0, 3) != 0 || !UrlChar(text[after + 3]) || text[after + 3] == '/') continue;
                int i = after + 3;
                while (i < end && UrlChar(text[i]) && !(text[i] == '-' && i + 1 < end && text[i + 1] == '>'))
                {
                    i++;
                }
                while (i > after + 3 && text[i - 1] is '.' or '!' or '?' or ':')
                {
                    i--;
                }
                return i;
            }
            return -1;
        }

        private static bool UrlChar(char c) => !char.IsWhiteSpace(c) && c is not ('\'' or '"' or ',' or ')' or ']' or '}' or ';' or '<' or '>' or '`' or '(' or '[' or '{');

        /// <summary>True when the // at <paramref name="slashes"/> follows a URL's scheme (https://), so it isn't a comment.</summary>
        public static bool AfterUrlScheme(string text, int slashes)
        {
            if (slashes < 1 || text[slashes - 1] != ':') return false;
            foreach (string scheme in UrlSchemes)
            {
                int start = slashes - 1 - scheme.Length;
                if (start >= 0 && UrlEnd(text, start, text.Length) > 0) return true;
            }
            return false;
        }

        /// <summary>The end of an email address (local@domain.tld) at <paramref name="start"/>, or -1.</summary>
        private int EmailEnd(int start)
        {
            int i = start;
            while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '_' or '%' or '+' or '-'))
            {
                i++;
            }
            if (i == start || i >= end || text[i] != '@' || text[i - 1] == '.') return -1;
            i++;
            int labels = 0;
            while (true)
            {
                int label = i;
                while (i < end && (char.IsLetterOrDigit(text[i]) || (text[i] == '-' && !(i + 1 < end && text[i + 1] == '>'))))
                {
                    i++;
                }
                if (i == label || text[i - 1] == '-') return -1;
                labels++;
                if (i + 1 < end && text[i] == '.' && char.IsLetterOrDigit(text[i + 1]))
                {
                    i++;
                    continue;
                }
                break;
            }
            return labels >= 2 ? i : -1;
        }

        /// <summary>Tokenizes the whole input (used for highlighting and completion).</summary>
        public static List<Token> Tokenize(string text, int start = 0, int end = -1)
        {
            Lexer lexer = new Lexer(text, start, end);
            List<Token> tokens = new List<Token>();
            while (true)
            {
                Token t = lexer.Next();
                if (t.Kind == TokenKind.EndOfInput)
                {
                    return tokens;
                }
                tokens.Add(t);
            }
        }

        /// <summary>The units a duration literal may use, as the duration type reads them.</summary>
        private static readonly string[] DurationUnits = { "ms", "min", "w", "d", "h", "m", "s" };

        /// <summary>
        /// Where a duration literal starting at <paramref name="start"/> ends, or -1: one or more numbers, each with a
        /// unit (w, d, h, m or min, s, ms), with nothing but the end of a word after the last: <c>30s</c>, <c>1.5h</c>,
        /// <c>1h30m</c>.
        /// </summary>
        public static int DurationEnd(string text, int start, int end)
        {
            int i = start;
            bool any = false;
            while (i < end && char.IsDigit(text[i]))
            {
                while (i < end && char.IsDigit(text[i])) i++;
                if (i + 1 < end && text[i] == '.' && char.IsDigit(text[i + 1]))
                {
                    i++;
                    while (i < end && char.IsDigit(text[i])) i++;
                }
                int unitStart = i;
                while (i < end && text[i] >= 'a' && text[i] <= 'z') i++;
                if (Array.IndexOf(DurationUnits, text.Substring(unitStart, i - unitStart)) < 0) return -1;
                any = true;
            }
            return any && (i >= end || !IsIdentChar(text[i])) ? i : -1;
        }

        private Token Make(TokenKind kind, int start) => new Token(kind, text.Substring(start, Position - start), TextSpan.FromBounds(start, Position));

        public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

        public static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
