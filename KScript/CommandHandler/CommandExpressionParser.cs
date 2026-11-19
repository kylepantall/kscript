using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using KScript.KScriptExceptions;

namespace KScript.CommandHandler
{
    #region Syntax tree

    /// <summary>
    /// A node of a parsed KScript expression.
    /// </summary>
    public abstract class Expr
    {
        /// <summary>The original text of this expression.</summary>
        public string Source { get; internal set; }

        public override string ToString() => Source;
    }

    /// <summary>
    /// Plain text. At the top level of a string it is copied to the output (after variable substitution);
    /// inside a command argument it is unquoted text whose surrounding whitespace is trimmed.
    /// </summary>
    public sealed class TextExpr : Expr
    {
        public TextExpr(string text) { Text = text; Source = text; }
        public string Text { get; }
    }

    /// <summary>A quoted string <c>'text'</c> or a number literal such as <c>42</c>.</summary>
    public sealed class LiteralExpr : Expr
    {
        public LiteralExpr(string value, string source) { Value = value; Source = source; }
        public string Value { get; }
    }

    /// <summary>A variable reference <c>$name</c>.</summary>
    public sealed class VariableExpr : Expr
    {
        public VariableExpr(string name) { Name = name; Source = "$" + name; }
        public string Name { get; }
    }

    /// <summary>A command call <c>@name(arguments)</c>.</summary>
    public sealed class CommandExpr : Expr
    {
        public CommandExpr(string name, List<ArgumentExpr> arguments, string source)
        {
            Name = name;
            Arguments = arguments;
            Source = source;
        }

        public string Name { get; }
        public List<ArgumentExpr> Arguments { get; }
    }

    /// <summary>A method call on a value: <c>target->name(arguments)</c>.</summary>
    public sealed class MethodExpr : Expr
    {
        public MethodExpr(Expr target, string name, List<ArgumentExpr> arguments, string source)
        {
            Target = target;
            Name = name;
            Arguments = arguments;
            Source = source;
        }

        public Expr Target { get; }
        public string Name { get; }
        public List<ArgumentExpr> Arguments { get; }
    }

    /// <summary>
    /// A bare method call on the right of a pipe: in <c>$x |> upper()</c>, <c>upper()</c> is applied to <c>$x</c>.
    /// </summary>
    public sealed class MethodRefExpr : Expr
    {
        public MethodRefExpr(string name, List<ArgumentExpr> arguments, string source)
        {
            Name = name;
            Arguments = arguments;
            Source = source;
        }

        public string Name { get; }
        public List<ArgumentExpr> Arguments { get; }
    }

    /// <summary>
    /// A pipe <c>left |> right</c>. The right side is a method, a command (the value becomes its first argument),
    /// a lambda, or a variable holding a lambda.
    /// </summary>
    public sealed class PipeExpr : Expr
    {
        public PipeExpr(Expr left, Expr right, string source)
        {
            Left = left;
            Right = right;
            Source = source;
        }

        public Expr Left { get; }
        public Expr Right { get; }
    }

    /// <summary>A lambda <c>fn($a, $b) => body</c>. Evaluated on its own, a lambda's value is its source text.</summary>
    public sealed class LambdaExpr : Expr
    {
        public LambdaExpr(List<string> parameters, Expr body, string source)
        {
            Parameters = parameters;
            Body = body;
            Source = source;
        }

        public List<string> Parameters { get; }
        public Expr Body { get; }
    }

    /// <summary>
    /// One argument of a command or method call: a sequence of parts that are concatenated.
    /// </summary>
    public sealed class ArgumentExpr
    {
        public List<Expr> Parts { get; } = new List<Expr>();

        public bool IsEmpty
        {
            get
            {
                foreach (Expr part in Parts)
                {
                    if (!(part is TextExpr text) || !string.IsNullOrWhiteSpace(text.Text))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }

    #endregion

    /// <summary>
    /// Recursive-descent parser for KScript expressions embedded in strings.
    ///
    /// <code>
    ///   template   := ( expression | any-char )*
    ///   expression := primary postfix*
    ///   postfix    := '->' identifier '(' arguments? ')'          method call
    ///               | '|>' pipe-target                             pipe
    ///   pipe-target:= identifier '(' arguments? ')' | command | lambda | variable | '(' expression ')'
    ///   primary    := command | variable | quoted | number | lambda | '(' expression ')'
    ///   command    := '@' identifier '(' arguments? ')'
    ///   lambda     := 'fn' '(' ( param ( ',' param )* )? ')' '=>' expression
    ///   arguments  := argument ( ',' argument )*
    ///   argument   := ( expression | quoted | '(' raw-balanced ')' | raw-char )*
    ///   quoted     := "'" any-char-except-quote* "'"
    /// </code>
    ///
    /// In running text, only unambiguous expressions are recognised: <c>@name(</c>, <c>fn(...) =></c>, and
    /// variables / quoted strings / numbers directly followed by <c>-></c> or <c>|></c>. Everything else
    /// (e-mail addresses, apostrophes, a lone <c>$name</c>) is left as text.
    /// </summary>
    public sealed class CommandExpressionParser
    {
        private const char Quote = '\'';

        // $a%$b->func() - the legacy "tied" variable function syntax, left as text for KScriptVariableHandler.
        private static readonly Regex TiedPointer = new Regex(@"\G\$[A-Za-z0-9_.\-]+%\$[A-Za-z0-9_.\-]+->[A-Za-z0-9_.\-]+\(\)", RegexOptions.Compiled);

        private static readonly Dictionary<string, LambdaExpr> LambdaCache = new Dictionary<string, LambdaExpr>();

        private readonly string input;
        private int pos;

        private CommandExpressionParser(string input)
        {
            this.input = input;
            pos = 0;
        }

        #region Public API

        /// <summary>
        /// Returns true if the string contains anything other than plain text:
        /// a command, a method call, a pipe or a lambda.
        /// </summary>
        public static bool ContainsCommand(string input)
        {
            if (string.IsNullOrEmpty(input) || (input.IndexOf('@') < 0 && input.IndexOf("->", System.StringComparison.Ordinal) < 0
                && input.IndexOf("|>", System.StringComparison.Ordinal) < 0 && input.IndexOf("=>", System.StringComparison.Ordinal) < 0))
            {
                return false;
            }

            try
            {
                foreach (Expr expr in Parse(input))
                {
                    if (!(expr is TextExpr))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (KScriptException)
            {
                // Malformed expressions count as expressions so that evaluation reports the error.
                return true;
            }
        }

        /// <summary>
        /// Splits the input into text and expressions.
        /// </summary>
        /// <exception cref="KScriptException">If an expression is malformed.</exception>
        public static List<Expr> Parse(string input)
        {
            List<Expr> result = new List<Expr>();
            if (string.IsNullOrEmpty(input))
            {
                return result;
            }

            new CommandExpressionParser(input).ParseTemplate(result);
            return result;
        }

        /// <summary>
        /// Parses a comma separated argument list such as <c>'a', @x('b'), 3</c>.
        /// </summary>
        public static List<ArgumentExpr> ParseArgumentList(string arguments)
        {
            CommandExpressionParser parser = new CommandExpressionParser(arguments + ")");
            List<ArgumentExpr> result = parser.ParseArguments("argument list");
            if (parser.pos != parser.input.Length)
            {
                throw parser.Error("Unexpected ')' in argument list.");
            }
            return result;
        }

        /// <summary>
        /// Parses a stored lambda such as <c>fn($s) => $s->upper()</c>. Returns null if the value is not a lambda.
        /// </summary>
        public static LambdaExpr ParseLambda(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed = value.Trim();

            lock (LambdaCache)
            {
                if (LambdaCache.TryGetValue(trimmed, out LambdaExpr cached))
                {
                    return cached;
                }
            }

            CommandExpressionParser parser = new CommandExpressionParser(trimmed);
            if (!parser.IsLambdaStart(0))
            {
                return null;
            }

            LambdaExpr lambda = parser.ParseLambdaExpr();
            parser.SkipWhitespace();
            if (parser.pos != trimmed.Length)
            {
                throw parser.Error("Unexpected text after lambda body.");
            }

            lock (LambdaCache)
            {
                LambdaCache[trimmed] = lambda;
            }

            return lambda;
        }

        #endregion

        #region Top level

        private void ParseTemplate(List<Expr> output)
        {
            StringBuilder text = new StringBuilder();

            void Flush()
            {
                if (text.Length > 0)
                {
                    output.Add(new TextExpr(text.ToString()));
                    text.Clear();
                }
            }

            while (pos < input.Length)
            {
                if (TryCopyVerbatim(text))
                {
                    continue;
                }

                Expr expr = TryParseTopLevelExpression();
                if (expr != null)
                {
                    Flush();
                    output.Add(expr);
                    continue;
                }

                text.Append(input[pos]);
                pos++;
            }

            Flush();
        }

        /// <summary>
        /// Copies constructs that must not be parsed (multi-dimensional array lookups and tied variable functions)
        /// straight into the text buffer.
        /// </summary>
        private bool TryCopyVerbatim(StringBuilder buffer)
        {
            int end = ArrayReferenceEnd(pos);
            if (end < 0)
            {
                Match tied = input[pos] == '$' ? TiedPointer.Match(input, pos) : Match.Empty;
                end = tied.Success ? pos + tied.Length : -1;
            }

            if (end < 0)
            {
                return false;
            }

            buffer.Append(input, pos, end - pos);
            pos = end;
            return true;
        }

        private Expr TryParseTopLevelExpression()
        {
            char c = input[pos];

            if (c == '@' && IsCommandStart(pos))
            {
                return ParsePostfix(ParseCommand());
            }

            if (c == 'f' && IsLambdaStart(pos))
            {
                return ParseLambdaExpr();
            }

            if (c == '$' || c == Quote || char.IsDigit(c))
            {
                int start = pos;
                Expr primary = TryParseSimplePrimary();
                if (primary != null && IsPostfixAhead(pos))
                {
                    return ParsePostfix(primary);
                }
                pos = start;
            }

            return null;
        }

        #endregion

        #region Expressions

        /// <summary>Parses a full expression (used for lambda bodies and brackets).</summary>
        private Expr ParseExpression()
        {
            SkipWhitespace();

            if (pos >= input.Length)
            {
                throw Error("Expected an expression.");
            }

            Expr primary;
            char c = input[pos];

            if (c == '@' && IsCommandStart(pos))
            {
                primary = ParseCommand();
            }
            else if (c == 'f' && IsLambdaStart(pos))
            {
                return ParseLambdaExpr();
            }
            else if (c == '(')
            {
                primary = ParseGroup();
            }
            else
            {
                primary = TryParseSimplePrimary() ?? throw Error("Expected a value, command or lambda.");
            }

            return ParsePostfix(primary);
        }

        private Expr ParseGroup()
        {
            pos++; // (
            Expr inner = ParseExpression();
            SkipWhitespace();
            Expect(')', "Missing ')' to close the bracketed expression.");
            return inner;
        }

        /// <summary>A variable, quoted string or number literal; null if none starts here.</summary>
        private Expr TryParseSimplePrimary()
        {
            if (pos >= input.Length)
            {
                return null;
            }

            char c = input[pos];
            int start = pos;

            if (c == '$')
            {
                int end = VariableNameEnd(pos + 1);
                if (end == pos + 1)
                {
                    return null;
                }

                string name = input.Substring(pos + 1, end - pos - 1);
                pos = end;
                return new VariableExpr(name);
            }

            if (c == Quote)
            {
                int close = input.IndexOf(Quote, pos + 1);
                if (close < 0)
                {
                    return null;
                }

                pos = close + 1;
                return new LiteralExpr(input.Substring(start + 1, close - start - 1), input.Substring(start, pos - start));
            }

            if (char.IsDigit(c) && (start == 0 || !IsIdentifierPart(input[start - 1]) && input[start - 1] != '.'))
            {
                int end = start;
                while (end < input.Length && char.IsDigit(input[end])) end++;
                if (end + 1 < input.Length && input[end] == '.' && char.IsDigit(input[end + 1]))
                {
                    end++;
                    while (end < input.Length && char.IsDigit(input[end])) end++;
                }

                pos = end;
                string number = input.Substring(start, end - start);
                return new LiteralExpr(number, number);
            }

            return null;
        }

        /// <summary>Applies any '->method()' calls and '|>' pipes that follow an expression.</summary>
        private Expr ParsePostfix(Expr expr)
        {
            int start = SourceStart(expr);

            while (true)
            {
                if (IsMethodStart(pos))
                {
                    pos += 2; // ->
                    string name = ReadIdentifier();
                    pos++; // (
                    List<ArgumentExpr> args = ParseArguments(name);
                    expr = new MethodExpr(expr, name, args, input.Substring(start, pos - start));
                    continue;
                }

                int pipe = PipeOperatorAt(pos);
                if (pipe >= 0)
                {
                    pos = pipe + 2;
                    Expr target = ParsePipeTarget();
                    expr = new PipeExpr(expr, target, input.Substring(start, pos - start));
                    continue;
                }

                return expr;
            }
        }

        private Expr ParsePipeTarget()
        {
            SkipWhitespace();

            if (pos >= input.Length)
            {
                throw Error("Expected a method, command or lambda after '|>'.");
            }

            char c = input[pos];
            int start = pos;

            if (c == '@' && IsCommandStart(pos))
            {
                return ParseCommand();
            }

            if (c == 'f' && IsLambdaStart(pos))
            {
                return ParseLambdaExpr();
            }

            if (c == '$')
            {
                return TryParseSimplePrimary() ?? throw Error("Expected a variable name after '$'.");
            }

            if (c == '(')
            {
                return ParseGroup();
            }

            if (IsIdentifierStart(c))
            {
                string name = ReadIdentifier();
                if (pos < input.Length && input[pos] == '(')
                {
                    pos++;
                    List<ArgumentExpr> args = ParseArguments(name);
                    return new MethodRefExpr(name, args, input.Substring(start, pos - start));
                }
                pos = start;
            }

            throw Error("Expected a method such as upper(), a command, a lambda or a $variable after '|>'.");
        }

        private LambdaExpr ParseLambdaExpr()
        {
            int start = pos;
            pos += 2; // fn
            SkipWhitespace();
            pos++; // (

            List<string> parameters = new List<string>();
            int close = input.IndexOf(')', pos);
            string list = input.Substring(pos, close - pos);

            foreach (string raw in list.Split(','))
            {
                string name = raw.Trim().TrimStart('$');
                if (name.Length == 0)
                {
                    if (list.Trim().Length == 0) break;
                    throw Error("Empty lambda parameter name.");
                }
                if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                {
                    throw Error($"Invalid lambda parameter name '{name}'.");
                }
                parameters.Add(name);
            }

            pos = close + 1;
            SkipWhitespace();
            pos += 2; // =>

            Expr body = ParseExpression();
            return new LambdaExpr(parameters, body, input.Substring(start, pos - start).TrimEnd());
        }

        private CommandExpr ParseCommand()
        {
            int start = pos;
            pos++; // @
            string name = ReadIdentifier();
            pos++; // (
            List<ArgumentExpr> args = ParseArguments(name);
            return new CommandExpr(name, args, input.Substring(start, pos - start));
        }

        /// <summary>
        /// Parses arguments up to and including the closing ')'. The opening '(' has already been consumed.
        /// </summary>
        private List<ArgumentExpr> ParseArguments(string owner)
        {
            List<ArgumentExpr> arguments = new List<ArgumentExpr>();
            ArgumentExpr current = new ArgumentExpr();
            StringBuilder raw = new StringBuilder();
            int depth = 0;

            void FlushRaw()
            {
                if (raw.Length > 0)
                {
                    current.Parts.Add(new TextExpr(raw.ToString()));
                    raw.Clear();
                }
            }

            void AddPart(Expr part)
            {
                FlushRaw();
                current.Parts.Add(part);
            }

            while (pos < input.Length)
            {
                if (TryCopyVerbatim(raw))
                {
                    continue;
                }

                char c = input[pos];

                if (c == Quote)
                {
                    Expr literal = TryParseSimplePrimary();
                    if (literal == null)
                    {
                        throw Error($"Unterminated string in '{owner}': a closing (') is missing.");
                    }
                    AddPart(ParsePostfix(literal));
                    continue;
                }

                if (c == '@' && IsCommandStart(pos))
                {
                    AddPart(ParsePostfix(ParseCommand()));
                    continue;
                }

                if (c == 'f' && IsLambdaStart(pos) && (raw.Length == 0 || !IsIdentifierPart(raw[raw.Length - 1])))
                {
                    AddPart(ParseLambdaExpr());
                    continue;
                }

                if (c == '$' || (char.IsDigit(c) && (raw.Length == 0 || !IsIdentifierPart(raw[raw.Length - 1]) && raw[raw.Length - 1] != '.')))
                {
                    int start = pos;
                    Expr primary = TryParseSimplePrimary();
                    if (primary != null && IsPostfixAhead(pos))
                    {
                        AddPart(ParsePostfix(primary));
                        continue;
                    }
                    pos = start;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth == 0)
                    {
                        FlushRaw();
                        arguments.Add(current);
                        pos++;

                        // "@cmd()" has no arguments rather than one empty argument.
                        if (arguments.Count == 1 && arguments[0].IsEmpty)
                        {
                            arguments.Clear();
                        }

                        return arguments;
                    }
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    FlushRaw();
                    arguments.Add(current);
                    current = new ArgumentExpr();
                    pos++;
                    continue;
                }

                raw.Append(c);
                pos++;
            }

            throw Error($"'{owner}' is missing a closing bracket ')'.");
        }

        #endregion

        #region Lookahead helpers

        private bool IsCommandStart(int index)
        {
            if (index + 1 >= input.Length || input[index] != '@' || !IsIdentifierStart(input[index + 1]))
            {
                return false;
            }

            int j = index + 2;
            while (j < input.Length && IsIdentifierPart(input[j])) j++;
            return j < input.Length && input[j] == '(';
        }

        /// <summary>'->' identifier '('</summary>
        private bool IsMethodStart(int index)
        {
            if (index + 2 >= input.Length || input[index] != '-' || input[index + 1] != '>' || !IsIdentifierStart(input[index + 2]))
            {
                return false;
            }

            int j = index + 3;
            while (j < input.Length && IsIdentifierPart(input[j])) j++;
            return j < input.Length && input[j] == '(';
        }

        /// <summary>Index of a '|>' operator after optional whitespace, or -1.</summary>
        private int PipeOperatorAt(int index)
        {
            int j = index;
            while (j < input.Length && char.IsWhiteSpace(input[j])) j++;
            return j + 1 < input.Length && input[j] == '|' && input[j + 1] == '>' ? j : -1;
        }

        private bool IsPostfixAhead(int index) => IsMethodStart(index) || PipeOperatorAt(index) >= 0;

        /// <summary>'fn' '(' ... ')' '=>'</summary>
        private bool IsLambdaStart(int index)
        {
            if (index + 1 >= input.Length || input[index] != 'f' || input[index + 1] != 'n')
            {
                return false;
            }

            if (index > 0 && (IsIdentifierPart(input[index - 1]) || input[index - 1] == '$' || input[index - 1] == '@'))
            {
                return false;
            }

            int j = index + 2;
            while (j < input.Length && char.IsWhiteSpace(input[j])) j++;
            if (j >= input.Length || input[j] != '(')
            {
                return false;
            }

            int close = input.IndexOf(')', j);
            if (close < 0 || input.IndexOf('(', j + 1, close - j - 1) >= 0)
            {
                return false;
            }

            j = close + 1;
            while (j < input.Length && char.IsWhiteSpace(input[j])) j++;
            return j + 1 < input.Length && input[j] == '=' && input[j + 1] == '>';
        }

        /// <summary>End of a '~array[..][..]' lookup starting at index, or -1.</summary>
        private int ArrayReferenceEnd(int index)
        {
            if (input[index] != '~' || index + 1 >= input.Length || !IsIdentifierStart(input[index + 1]))
            {
                return -1;
            }

            int j = index + 2;
            while (j < input.Length && IsIdentifierPart(input[j])) j++;

            if (j >= input.Length || input[j] != '[')
            {
                return -1;
            }

            while (j < input.Length && input[j] == '[')
            {
                int close = input.IndexOf(']', j);
                if (close < 0)
                {
                    return -1;
                }
                j = close + 1;
            }

            return j;
        }

        /// <summary>
        /// End of a variable name starting at index. Names may contain letters, digits, '_', '.' and '-'
        /// (but stop before '->' and before a trailing '.').
        /// </summary>
        private int VariableNameEnd(int index)
        {
            int j = index;
            while (j < input.Length)
            {
                char c = input[j];
                if (IsIdentifierPart(c) || c == '.' || (c == '-' && !(j + 1 < input.Length && input[j + 1] == '>')))
                {
                    j++;
                    continue;
                }
                break;
            }

            while (j > index && (input[j - 1] == '.' || input[j - 1] == '-'))
            {
                j--;
            }

            return j;
        }

        private string ReadIdentifier()
        {
            int start = pos;
            while (pos < input.Length && IsIdentifierPart(input[pos])) pos++;
            return input.Substring(start, pos - start);
        }

        private void SkipWhitespace()
        {
            while (pos < input.Length && char.IsWhiteSpace(input[pos])) pos++;
        }

        private void Expect(char c, string message)
        {
            if (pos >= input.Length || input[pos] != c)
            {
                throw Error(message);
            }
            pos++;
        }

        private int SourceStart(Expr expr)
        {
            int index = pos - (expr.Source?.Length ?? 0);
            return index < 0 ? 0 : index;
        }

        private KScriptException Error(string message) => new KScriptException("Syntax_Error",
            message + "\nError occurs when parsing string:\n----------------------------------------------------\n" + input);

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        #endregion
    }
}
