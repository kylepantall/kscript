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
    }

    public readonly struct Token
    {
        public Token(TokenKind kind, string text, TextSpan span, string error = null)
        {
            Kind = kind;
            Text = text;
            Span = span;
            Error = error;
        }

        public TokenKind Kind { get; }

        /// <summary>The token's text. For strings this is the unquoted value; for variables and commands, the name without the sigil.</summary>
        public string Text { get; }

        public TextSpan Span { get; }

        /// <summary>A lexer error, for <see cref="TokenKind.Error"/> and unterminated strings.</summary>
        public string Error { get; }

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

        public Token Next()
        {
            while (Position < end && char.IsWhiteSpace(text[Position]))
            {
                Position++;
            }

            if (Position >= end)
            {
                return new Token(TokenKind.EndOfInput, string.Empty, new TextSpan(end, 0));
            }

            int start = Position;
            char c = text[Position];

            if (char.IsDigit(c))
            {
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
                int close = text.IndexOf(c, start + 1, end - start - 1);
                if (close < 0)
                {
                    Position = end;
                    return new Token(TokenKind.String, text.Substring(start + 1, end - start - 1), TextSpan.FromBounds(start, end),
                        $"This string is missing its closing {c}.");
                }
                Position = close + 1;
                return new Token(TokenKind.String, text.Substring(start + 1, close - start - 1), TextSpan.FromBounds(start, Position));
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

        private Token Make(TokenKind kind, int start) => new Token(kind, text.Substring(start, Position - start), TextSpan.FromBounds(start, Position));

        public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

        public static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
