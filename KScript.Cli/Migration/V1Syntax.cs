using System;
using System.Collections.Generic;
using System.Text;

namespace KScript.Migration
{
    // The KScript 1 expression syntax, as the v1 interpreter read it. A v1 value was text with commands, variables and
    // array lookups substituted into it, so the tree keeps text and expressions side by side.

    internal abstract class V1Node
    {
    }

    /// <summary>Plain text. Inside a command argument the parser keeps it raw; the converter trims it.</summary>
    internal sealed class V1Text : V1Node
    {
        public V1Text(string text) => Text = text;
        public string Text { get; }
    }

    /// <summary><c>$name</c>. v1 names could contain dots and dashes.</summary>
    internal sealed class V1Var : V1Node
    {
        public V1Var(string name) => Name = name;
        public string Name { get; }
    }

    /// <summary><c>~name[0]['key'][$i]</c>: a lookup in an array made with &lt;marray&gt; or &lt;array&gt;.</summary>
    internal sealed class V1ArrayRef : V1Node
    {
        public V1ArrayRef(string name, List<string> indexes)
        {
            Name = name;
            Indexes = indexes;
        }

        public string Name { get; }
        public List<string> Indexes { get; }
    }

    /// <summary>A quoted string (its text is still substituted) or a number.</summary>
    internal sealed class V1Literal : V1Node
    {
        public V1Literal(string value, bool quoted)
        {
            Value = value;
            Quoted = quoted;
        }

        public string Value { get; }
        public bool Quoted { get; }
    }

    internal sealed class V1Command : V1Node
    {
        public V1Command(string name, List<V1Arg> args)
        {
            Name = name;
            Args = args;
        }

        public string Name { get; }
        public List<V1Arg> Args { get; }
    }

    internal sealed class V1Method : V1Node
    {
        public V1Method(V1Node target, string name, List<V1Arg> args)
        {
            Target = target;
            Name = name;
            Args = args;
        }

        /// <summary>The value the method is called on, or null for a bare method on the right of a pipe.</summary>
        public V1Node Target { get; }
        public string Name { get; }
        public List<V1Arg> Args { get; }
    }

    internal sealed class V1Pipe : V1Node
    {
        public V1Pipe(V1Node left, V1Node right)
        {
            Left = left;
            Right = right;
        }

        public V1Node Left { get; }
        public V1Node Right { get; }
    }

    internal sealed class V1Lambda : V1Node
    {
        public V1Lambda(List<string> parameters, V1Node body)
        {
            Parameters = parameters;
            Body = body;
        }

        public List<string> Parameters { get; }
        public V1Node Body { get; }
    }

    /// <summary>One argument of a call: its parts are joined together.</summary>
    internal sealed class V1Arg
    {
        public List<V1Node> Parts { get; } = new List<V1Node>();
    }

    internal sealed class V1SyntaxException : Exception
    {
        public V1SyntaxException(string message) : base(message) { }
    }

    /// <summary>
    /// Reads KScript 1 values. This follows the v1 interpreter's own parser: in running text only <c>@name(</c>,
    /// <c>fn(...) =&gt;</c> and values followed by <c>-&gt;</c> or <c>|&gt;</c> are expressions; <c>$name</c> and
    /// <c>~array[...]</c> are found in the text afterwards, the way v1 substituted them.
    /// </summary>
    internal sealed class V1Parser
    {
        private const char Quote = '\'';

        private readonly string input;
        private int pos;

        private V1Parser(string input) => this.input = input;

        /// <summary>Splits a v1 value into text and expressions, with variables and array lookups found in the text.</summary>
        public static List<V1Node> ParseTemplate(string input)
        {
            var result = new List<V1Node>();
            if (string.IsNullOrEmpty(input)) return result;
            var parser = new V1Parser(input);
            var text = new StringBuilder();

            void Flush()
            {
                if (text.Length > 0)
                {
                    result.AddRange(SplitText(text.ToString()));
                    text.Clear();
                }
            }

            while (parser.pos < input.Length)
            {
                V1Node array = parser.TryParseArrayRef();
                if (array != null)
                {
                    Flush();
                    result.Add(array);
                    continue;
                }

                V1Node expr = parser.TryParseTopLevelExpression();
                if (expr != null)
                {
                    Flush();
                    result.Add(expr);
                    continue;
                }

                text.Append(input[parser.pos]);
                parser.pos++;
            }

            Flush();
            return result;
        }

        /// <summary>A comma separated argument list, such as the args attribute of v1 &lt;call&gt;.</summary>
        public static List<V1Arg> ParseArgumentList(string arguments)
        {
            var parser = new V1Parser(arguments + ")");
            List<V1Arg> result = parser.ParseArguments("argument list");
            if (parser.pos != parser.input.Length) throw new V1SyntaxException("Unexpected ')' in the argument list.");
            return result;
        }

        /// <summary>Finds <c>$name</c> and <c>~array[...]</c> in plain text.</summary>
        public static List<V1Node> SplitText(string text)
        {
            var result = new List<V1Node>();
            var buffer = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '$' && i + 1 < text.Length && IsIdentifierPart(text[i + 1]))
                {
                    int end = VariableNameEnd(text, i + 1);
                    if (end > i + 1 && !char.IsDigit(text[i + 1]))
                    {
                        if (buffer.Length > 0) { result.Add(new V1Text(buffer.ToString())); buffer.Clear(); }
                        result.Add(new V1Var(text.Substring(i + 1, end - i - 1)));
                        i = end;
                        continue;
                    }
                }

                if (c == '~')
                {
                    int end = ArrayReferenceEnd(text, i);
                    if (end > 0)
                    {
                        if (buffer.Length > 0) { result.Add(new V1Text(buffer.ToString())); buffer.Clear(); }
                        result.Add(ParseArrayRef(text.Substring(i, end - i)));
                        i = end;
                        continue;
                    }
                }

                buffer.Append(c);
                i++;
            }

            if (buffer.Length > 0) result.Add(new V1Text(buffer.ToString()));
            return result;
        }

        #region Expressions

        private V1Node TryParseArrayRef()
        {
            int end = ArrayReferenceEnd(input, pos);
            if (end < 0) return null;
            V1Node node = ParseArrayRef(input.Substring(pos, end - pos));
            pos = end;
            return IsPostfixAhead(pos) ? ParsePostfix(node) : node;
        }

        private static V1ArrayRef ParseArrayRef(string source)
        {
            int open = source.IndexOf('[');
            string name = source.Substring(1, open - 1);
            var indexes = new List<string>();
            int j = open;
            while (j < source.Length && source[j] == '[')
            {
                int close = source.IndexOf(']', j);
                indexes.Add(source.Substring(j + 1, close - j - 1));
                j = close + 1;
            }
            return new V1ArrayRef(name, indexes);
        }

        private V1Node TryParseTopLevelExpression()
        {
            char c = input[pos];
            if (c == '@' && IsCommandStart(pos)) return ParsePostfix(ParseCommand());
            if (c == 'f' && IsLambdaStart(pos)) return ParseLambda();
            if (c == '$' || c == Quote || char.IsDigit(c))
            {
                int start = pos;
                V1Node primary = TryParseSimplePrimary();
                if (primary != null && IsPostfixAhead(pos)) return ParsePostfix(primary);
                pos = start;
            }
            return null;
        }

        private V1Node ParseExpression()
        {
            SkipWhitespace();
            if (pos >= input.Length) throw new V1SyntaxException("Expected an expression.");
            char c = input[pos];
            V1Node primary;
            if (c == '@' && IsCommandStart(pos)) primary = ParseCommand();
            else if (c == 'f' && IsLambdaStart(pos)) return ParseLambda();
            else if (c == '(') primary = ParseGroup();
            else primary = TryParseArrayRef() ?? TryParseSimplePrimary() ?? throw new V1SyntaxException("Expected a value, command or lambda.");
            return ParsePostfix(primary);
        }

        private V1Node ParseGroup()
        {
            pos++;
            V1Node inner = ParseExpression();
            SkipWhitespace();
            if (pos >= input.Length || input[pos] != ')') throw new V1SyntaxException("Missing ')'.");
            pos++;
            return inner;
        }

        private V1Node TryParseSimplePrimary()
        {
            if (pos >= input.Length) return null;
            char c = input[pos];
            int start = pos;

            if (c == '$')
            {
                int end = VariableNameEnd(input, pos + 1);
                if (end == pos + 1) return null;
                pos = end;
                return new V1Var(input.Substring(start + 1, end - start - 1));
            }

            if (c == Quote)
            {
                int close = input.IndexOf(Quote, pos + 1);
                if (close < 0) return null;
                pos = close + 1;
                return new V1Literal(input.Substring(start + 1, close - start - 1), quoted: true);
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
                return new V1Literal(input.Substring(start, end - start), quoted: false);
            }

            return null;
        }

        private V1Node ParsePostfix(V1Node expr)
        {
            while (true)
            {
                if (IsMethodStart(pos))
                {
                    pos += 2;
                    string name = ReadIdentifier();
                    pos++;
                    expr = new V1Method(expr, name, ParseArguments(name));
                    continue;
                }

                int pipe = PipeOperatorAt(pos);
                if (pipe >= 0)
                {
                    pos = pipe + 2;
                    expr = new V1Pipe(expr, ParsePipeTarget());
                    continue;
                }

                return expr;
            }
        }

        private V1Node ParsePipeTarget()
        {
            SkipWhitespace();
            if (pos >= input.Length) throw new V1SyntaxException("Expected a method, command or lambda after '|>'.");
            char c = input[pos];
            int start = pos;
            if (c == '@' && IsCommandStart(pos)) return ParseCommand();
            if (c == 'f' && IsLambdaStart(pos)) return ParseLambda();
            if (c == '$') return TryParseSimplePrimary() ?? throw new V1SyntaxException("Expected a variable name after '$'.");
            if (c == '(') return ParseGroup();
            if (IsIdentifierStart(c))
            {
                string name = ReadIdentifier();
                if (pos < input.Length && input[pos] == '(')
                {
                    pos++;
                    return new V1Method(null, name, ParseArguments(name));
                }
                pos = start;
            }
            throw new V1SyntaxException("Expected a method, a command, a lambda or a $variable after '|>'.");
        }

        private V1Lambda ParseLambda()
        {
            pos += 2;
            SkipWhitespace();
            pos++;
            int close = input.IndexOf(')', pos);
            var parameters = new List<string>();
            foreach (string raw in input.Substring(pos, close - pos).Split(','))
            {
                string name = raw.Trim().TrimStart('$');
                if (name.Length > 0) parameters.Add(name);
            }
            pos = close + 1;
            SkipWhitespace();
            pos += 2;
            return new V1Lambda(parameters, ParseExpression());
        }

        private V1Command ParseCommand()
        {
            pos++;
            string name = ReadIdentifier();
            pos++;
            return new V1Command(name, ParseArguments(name));
        }

        private List<V1Arg> ParseArguments(string owner)
        {
            var arguments = new List<V1Arg>();
            var current = new V1Arg();
            var raw = new StringBuilder();
            int depth = 0;

            void FlushRaw()
            {
                if (raw.Length > 0)
                {
                    current.Parts.AddRange(SplitText(raw.ToString()));
                    raw.Clear();
                }
            }

            void Add(V1Node part)
            {
                FlushRaw();
                current.Parts.Add(part);
            }

            while (pos < input.Length)
            {
                V1Node array = TryParseArrayRef();
                if (array != null)
                {
                    Add(array);
                    continue;
                }

                char c = input[pos];
                if (c == Quote)
                {
                    V1Node literal = TryParseSimplePrimary() ?? throw new V1SyntaxException($"A string in @{owner}() is missing its closing quote.");
                    Add(ParsePostfix(literal));
                    continue;
                }

                if (c == '@' && IsCommandStart(pos))
                {
                    Add(ParsePostfix(ParseCommand()));
                    continue;
                }

                if (c == 'f' && IsLambdaStart(pos) && (raw.Length == 0 || !IsIdentifierPart(raw[raw.Length - 1])))
                {
                    Add(ParseLambda());
                    continue;
                }

                if (c == '$' || (char.IsDigit(c) && (raw.Length == 0 || !IsIdentifierPart(raw[raw.Length - 1]) && raw[raw.Length - 1] != '.')))
                {
                    int start = pos;
                    V1Node primary = TryParseSimplePrimary();
                    if (primary != null && IsPostfixAhead(pos))
                    {
                        Add(ParsePostfix(primary));
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
                        if (arguments.Count == 1 && IsBlank(arguments[0])) arguments.Clear();
                        return arguments;
                    }
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    FlushRaw();
                    arguments.Add(current);
                    current = new V1Arg();
                    pos++;
                    continue;
                }

                raw.Append(c);
                pos++;
            }

            throw new V1SyntaxException($"@{owner}( is missing its closing ')'.");
        }

        private static bool IsBlank(V1Arg arg)
        {
            foreach (V1Node part in arg.Parts)
            {
                if (!(part is V1Text t) || !string.IsNullOrWhiteSpace(t.Text)) return false;
            }
            return true;
        }

        #endregion

        #region Lookahead

        private bool IsCommandStart(int index)
        {
            if (index + 1 >= input.Length || input[index] != '@' || !IsIdentifierStart(input[index + 1])) return false;
            int j = index + 2;
            while (j < input.Length && IsIdentifierPart(input[j])) j++;
            return j < input.Length && input[j] == '(';
        }

        private bool IsMethodStart(int index)
        {
            if (index + 2 >= input.Length || input[index] != '-' || input[index + 1] != '>' || !IsIdentifierStart(input[index + 2])) return false;
            int j = index + 3;
            while (j < input.Length && IsIdentifierPart(input[j])) j++;
            return j < input.Length && input[j] == '(';
        }

        private int PipeOperatorAt(int index)
        {
            int j = index;
            while (j < input.Length && char.IsWhiteSpace(input[j])) j++;
            return j + 1 < input.Length && input[j] == '|' && input[j + 1] == '>' ? j : -1;
        }

        private bool IsPostfixAhead(int index) => IsMethodStart(index) || PipeOperatorAt(index) >= 0;

        private bool IsLambdaStart(int index)
        {
            if (index + 1 >= input.Length || input[index] != 'f' || input[index + 1] != 'n') return false;
            if (index > 0 && (IsIdentifierPart(input[index - 1]) || input[index - 1] == '$' || input[index - 1] == '@')) return false;
            int j = index + 2;
            while (j < input.Length && char.IsWhiteSpace(input[j])) j++;
            if (j >= input.Length || input[j] != '(') return false;
            int close = input.IndexOf(')', j);
            if (close < 0 || input.IndexOf('(', j + 1, close - j - 1) >= 0) return false;
            j = close + 1;
            while (j < input.Length && char.IsWhiteSpace(input[j])) j++;
            return j + 1 < input.Length && input[j] == '=' && input[j + 1] == '>';
        }

        private static int ArrayReferenceEnd(string text, int index)
        {
            if (index >= text.Length || text[index] != '~' || index + 1 >= text.Length || !IsIdentifierStart(text[index + 1])) return -1;
            int j = index + 2;
            while (j < text.Length && IsIdentifierPart(text[j])) j++;
            if (j >= text.Length || text[j] != '[') return -1;
            while (j < text.Length && text[j] == '[')
            {
                int close = text.IndexOf(']', j);
                if (close < 0) return -1;
                j = close + 1;
            }
            return j;
        }

        private static int VariableNameEnd(string text, int index)
        {
            int j = index;
            while (j < text.Length)
            {
                char c = text[j];
                if (IsIdentifierPart(c) || c == '.' || (c == '-' && !(j + 1 < text.Length && text[j + 1] == '>')))
                {
                    j++;
                    continue;
                }
                break;
            }
            while (j > index && (text[j - 1] == '.' || text[j - 1] == '-')) j--;
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

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        #endregion
    }
}
