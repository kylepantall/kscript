using System.Text;

using KScript.Runtime;

namespace KScript.Documents.Maths
{
    /// <summary>
    /// Parses LaTeX maths, such as <c>\frac{a}{b} + \sum_{i=1}^{n} x_i^2</c>, into a <see cref="MathNode"/> tree.
    /// Inside environments (matrix, cases, aligned) a new line ends a row as \\ does, so maths written in .kscript
    /// strings needs no doubled backslashes.
    /// </summary>
    public sealed class MathParser
    {
        private readonly string text;
        private int pos;
        private int environmentDepth;

        private MathParser(string text) => this.text = text ?? string.Empty;

        public static MathNode Parse(string latex)
        {
            var parser = new MathParser(latex);
            MathRow row = parser.ParseRow(stopAtClose: false);
            if (parser.pos < parser.text.Length) throw parser.Error($"'{parser.text[parser.pos]}' has nothing to close.");
            return Simplify(row);
        }

        /// <summary>Checks LaTeX maths without keeping it: null if it parses, else the error.</summary>
        public static string Check(string latex)
        {
            try
            {
                Parse(latex);
                return null;
            }
            catch (KScriptException e)
            {
                return e.Message;
            }
        }

        private KScriptException Error(string message) =>
            new KScriptException($"In the maths '{Clip(text)}': {message}", "Math");

        private static string Clip(string s) => s.Length > 60 ? s.Substring(0, 57) + "..." : s;

        private static MathNode Simplify(MathRow row) => row.Items.Count == 1 ? row.Items[0] : row;

        private bool AtEnd => pos >= text.Length;

        private char Peek => pos < text.Length ? text[pos] : '\0';

        private void SkipSpace(bool newlines = true)
        {
            while (pos < text.Length && char.IsWhiteSpace(text[pos]) && (newlines || text[pos] != '\n')) pos++;
        }

        /// <summary>True at a row end inside an environment: \\ or a new line.</summary>
        private bool AtRowEnd()
        {
            if (environmentDepth == 0) return false;
            if (text[pos] == '\n') return true;
            return text[pos] == '\\' && pos + 1 < text.Length && text[pos + 1] == '\\';
        }

        private MathRow ParseRow(bool stopAtClose)
        {
            var row = new MathRow();
            while (true)
            {
                SkipSpace(newlines: environmentDepth == 0);
                if (AtEnd) break;
                char c = text[pos];
                if (c == '}')
                {
                    if (stopAtClose) break;
                    throw Error("there is a } without a { before it.");
                }
                if (c == '&' && environmentDepth > 0) break;
                if (AtRowEnd()) break;
                if (c == '\\' && (LooksAt("\\right") || LooksAt("\\end"))) break;
                MathNode atom = ParseAtom();
                if (atom == null) continue;
                atom = ParseScripts(atom);
                row.Items.Add(atom);
            }
            return row;
        }

        private bool LooksAt(string command)
        {
            if (string.CompareOrdinal(text, pos, command, 0, command.Length) != 0) return false;
            int end = pos + command.Length;
            return end >= text.Length || !char.IsLetter(text[end]);
        }

        private MathNode ParseScripts(MathNode atom)
        {
            MathNode sub = null, sup = null;
            while (true)
            {
                SkipSpace(newlines: environmentDepth == 0);
                if (AtEnd) break;
                char c = text[pos];
                if (c == '\'')
                {
                    // x' is x^{\prime}; x'' has two.
                    var primes = new StringBuilder();
                    while (Peek == '\'')
                    {
                        primes.Append('′');
                        pos++;
                    }
                    sup = Append(sup, new MathAtom(primes.ToString(), AtomKind.Ord, "\\prime"));
                    continue;
                }
                if (c == '\\' && (LooksAt("\\limits") || LooksAt("\\nolimits")))
                {
                    bool limits = LooksAt("\\limits");
                    pos += limits ? 7 : 9;
                    if (atom is MathBigOp op) op.Limits = limits;
                    continue;
                }
                if (c != '^' && c != '_') break;
                pos++;
                MathNode arg = ParseArgument(c == '^' ? "a superscript" : "a subscript");
                if (c == '^')
                {
                    if (sup != null && !(sup is MathAtom a && a.Command == "\\prime")) throw Error("x^a^b has two superscripts. Group them: x^{ab}.");
                    sup = Append(sup, arg);
                }
                else
                {
                    if (sub != null) throw Error("x_a_b has two subscripts. Group them: x_{ab}.");
                    sub = arg;
                }
            }
            if (sub == null && sup == null) return atom;
            return new MathScripts { Base = atom, Sub = sub, Sup = sup };
        }

        private static MathNode Append(MathNode first, MathNode second)
        {
            if (first == null) return second;
            var row = new MathRow();
            row.Items.Add(first);
            row.Items.Add(second);
            return row;
        }

        /// <summary>One argument: a {group}, a command, or a single character.</summary>
        private MathNode ParseArgument(string what)
        {
            SkipSpace();
            if (AtEnd || Peek == '}' || Peek == '&') throw Error($"{what} is missing.");
            if (Peek == '{') return ParseGroup();
            if (Peek == '\\') return ParseAtom() ?? new MathRow();
            if (char.IsDigit(Peek))
            {
                // x^23 is x^{2}3, as in TeX.
                return new MathAtom(text[pos++].ToString(), AtomKind.Ord);
            }
            return ParseAtom();
        }

        /// <summary>A {group}. New lines and &amp; inside it belong to it, not to an environment around it.</summary>
        private MathNode ParseGroup()
        {
            pos++;
            int depth = environmentDepth;
            environmentDepth = 0;
            MathRow row = ParseRow(stopAtClose: true);
            environmentDepth = depth;
            Expect('}');
            return Simplify(row);
        }

        private void Expect(char c)
        {
            if (Peek != c) throw Error(c == '}' ? "a { isn't closed with }." : $"'{c}' is missing.");
            pos++;
        }

        /// <summary>The text of a {group} without parsing it: \text{...}, \begin{...}.</summary>
        private string ReadGroupText(string what)
        {
            SkipSpace();
            if (Peek != '{') throw Error($"{what} needs {{...}} after it.");
            int depth = 0;
            int start = pos + 1;
            for (; pos < text.Length; pos++)
            {
                if (text[pos] == '\\')
                {
                    pos++;
                    continue;
                }
                if (text[pos] == '{') depth++;
                else if (text[pos] == '}' && --depth == 0)
                {
                    string s = text.Substring(start, pos - start);
                    pos++;
                    return s;
                }
            }
            throw Error("a { isn't closed with }.");
        }

        private string ReadCommandName()
        {
            pos++; // the backslash
            if (AtEnd) throw Error("it ends with a \\.");
            int start = pos;
            if (!char.IsLetter(text[pos]))
            {
                pos++;
                return text.Substring(start, 1);
            }
            while (pos < text.Length && char.IsLetter(text[pos])) pos++;
            // \operatorname*, \align*: a star belongs to the name.
            if (pos < text.Length && text[pos] == '*' && (text.Substring(start, pos - start) == "operatorname")) pos++;
            return text.Substring(start, pos - start);
        }

        private MathNode ParseAtom()
        {
            char c = text[pos];
            if (c == '{') return ParseGroup();
            if (c == '^' || c == '_') throw Error($"'{c}' needs something before it, such as x{c}2.");
            if (c == '&') throw Error("& only works inside an environment such as aligned or matrix.");
            if (c == '\\') return ParseCommand();
            if (char.IsDigit(c) || (c == '.' && pos + 1 < text.Length && char.IsDigit(text[pos + 1])))
            {
                int start = pos;
                while (pos < text.Length && (char.IsDigit(text[pos]) || (text[pos] == '.' && pos + 1 < text.Length && char.IsDigit(text[pos + 1])))) pos++;
                return new MathAtom(text.Substring(start, pos - start), AtomKind.Ord);
            }
            if (c == '~')
            {
                pos++;
                return new MathSpace(1 / 3.0, "~");
            }
            pos++;
            if (char.IsHighSurrogate(c) && pos < text.Length) return new MathAtom(c.ToString() + text[pos++], AtomKind.Ord);
            if (c == '-') return new MathAtom("−", AtomKind.Bin, "-");
            string typed = c.ToString();
            AtomKind kind = MathSymbols.KindOf(c);
            if (MathSymbols.CommandFor.TryGetValue(typed, out string command))
            {
                // α typed directly: the same as \alpha.
                string name = command.Substring(1);
                if (MathSymbols.Symbols.TryGetValue(name, out var s)) kind = s.Kind;
                else if (MathSymbols.BigOps.TryGetValue(name, out var big)) return new MathBigOp(big.Text, name, big.Limits, named: false);
                return new MathAtom(typed, kind, command);
            }
            return new MathAtom(typed, kind);
        }

        private MathNode ParseCommand()
        {
            string name = ReadCommandName();
            if (name == "\\") throw Error("\\\\ starts a new line, which only works inside an environment such as aligned or matrix.");
            if (MathSymbols.Spaces.TryGetValue(name, out double em)) return new MathSpace(em, "\\" + name);
            if (MathSymbols.Symbols.TryGetValue(name, out var symbol)) return new MathAtom(symbol.Text, symbol.Kind, "\\" + name);
            if (MathSymbols.BigOps.TryGetValue(name, out var op)) return new MathBigOp(op.Text, name, op.Limits, named: false);
            if (MathSymbols.Functions.TryGetValue(name, out bool limits)) return new MathBigOp(name, name, limits, named: true);
            if (MathSymbols.Ignored.Contains(name))
            {
                if (name == "label") ReadGroupText("\\label");
                return null;
            }
            if (MathSymbols.Accents.Contains(name)) return new MathAccent { Accent = name, Body = ParseArgument("\\" + name + "'s argument") };
            if (MathSymbols.TextCommands.Contains(name))
            {
                MathFont font = name switch { "textbf" => MathFont.Bold, "textit" => MathFont.Italic, "texttt" => MathFont.Mono, "textsf" => MathFont.Sans, _ => MathFont.Roman };
                return new MathText(Unescape(ReadGroupText("\\" + name)), font);
            }
            if (MathSymbols.Fonts.TryGetValue(name, out MathFont mathFont))
            {
                MathNode body = ParseArgument("\\" + name + "'s argument");
                SetFont(body, mathFont);
                return Wrap(body, name);
            }
            switch (name)
            {
                case "frac":
                case "dfrac":
                case "tfrac":
                case "cfrac":
                    return new MathFrac { Numerator = ParseArgument("\\frac's numerator"), Denominator = ParseArgument("\\frac's denominator"), Command = name };
                case "binom":
                case "dbinom":
                case "tbinom":
                    return new MathFrac { Numerator = ParseArgument("\\binom's top"), Denominator = ParseArgument("\\binom's bottom"), Bar = false, Command = name };
                case "sqrt":
                {
                    MathNode index = null;
                    SkipSpace();
                    if (Peek == '[')
                    {
                        int close = text.IndexOf(']', pos);
                        if (close < 0) throw Error("\\sqrt[ isn't closed with ].");
                        index = Parse(text.Substring(pos + 1, close - pos - 1));
                        pos = close + 1;
                    }
                    return new MathSqrt { Body = ParseArgument("\\sqrt's argument"), Index = index };
                }
                case "left":
                    return ParseFenced();
                case "begin":
                    return ParseEnvironment();
                case "operatorname":
                case "operatorname*":
                {
                    string opName = ReadGroupText("\\operatorname");
                    return new MathBigOp(opName, "operatorname", limits: name.EndsWith("*", StringComparison.Ordinal), named: true);
                }
                case "right":
                    throw Error("\\right has no \\left before it.");
                case "end":
                    throw Error("\\end has no \\begin before it.");
            }
            throw Error($"\\{name} isn't a maths command KScript knows. See the list in the docs (Documents, Maths).");
        }

        /// <summary>Records the font command so LaTeX output writes \mathbf{...} again.</summary>
        private static MathNode Wrap(MathNode body, string command) => new MathStyled { Body = body, Command = command };

        private static void SetFont(MathNode node, MathFont font)
        {
            switch (node)
            {
                case MathAtom a:
                    a.Font = font;
                    break;
                case MathRow r:
                    foreach (MathNode n in r.Items) SetFont(n, font);
                    break;
                case MathScripts s:
                    SetFont(s.Base, font);
                    break;
                case MathStyled st:
                    SetFont(st.Body, font);
                    break;
            }
        }

        private static string Unescape(string s) => s.Replace("\\{", "{").Replace("\\}", "}").Replace("\\%", "%").Replace("\\$", "$").Replace("\\&", "&").Replace("\\_", "_");

        private string ReadDelimiter(string after)
        {
            SkipSpace();
            if (AtEnd) throw Error($"\\{after} needs a delimiter after it, such as ( or . for none.");
            if (Peek == '\\')
            {
                string name = ReadCommandName();
                if (MathSymbols.Symbols.TryGetValue(name, out var s) && (s.Kind == AtomKind.Open || s.Kind == AtomKind.Close || name == "|" || name == "vert" || name == "Vert")) return s.Text;
                throw Error($"\\{name} can't follow \\{after}. Use ( ) [ ] \\{{ \\}} | \\| \\langle \\rangle, or . for none.");
            }
            char c = text[pos++];
            if (c == '.') return string.Empty;
            if ("()[]|/".IndexOf(c) < 0) throw Error($"'{c}' can't follow \\{after}. Use ( ) [ ] \\{{ \\}} | \\| \\langle \\rangle, or . for none.");
            return c.ToString();
        }

        private MathNode ParseFenced()
        {
            string left = ReadDelimiter("left");
            MathRow body = ParseRow(stopAtClose: false);
            if (!LooksAt("\\right")) throw Error("\\left has no \\right after it.");
            pos += 6;
            string right = ReadDelimiter("right");
            return new MathFenced { Left = left, Right = right, Body = Simplify(body) };
        }

        private MathNode ParseEnvironment()
        {
            string env = ReadGroupText("\\begin").Trim();
            if (!MathSymbols.Environments.Contains(env))
                throw Error($"\\begin{{{env}}} isn't an environment KScript knows. Use matrix, pmatrix, bmatrix, vmatrix, cases, aligned or array.");
            var matrix = new MathMatrix { Environment = env };
            if (env == "array") matrix.Columns = ReadGroupText("\\begin{array}").Where(ch => ch == 'l' || ch == 'c' || ch == 'r').ToArray();
            environmentDepth++;
            var row = new List<MathNode>();
            while (true)
            {
                MathRow cell = ParseRow(stopAtClose: false);
                row.Add(Simplify(cell));
                if (AtEnd) throw Error($"\\begin{{{env}}} has no \\end{{{env}}}.");
                if (Peek == '&')
                {
                    pos++;
                    continue;
                }
                if (Peek == '\n' || (Peek == '\\' && pos + 1 < text.Length && text[pos + 1] == '\\'))
                {
                    pos += Peek == '\n' ? 1 : 2;
                    AddRow(matrix, row);
                    row = new List<MathNode>();
                    continue;
                }
                if (Peek == '}') throw Error("there is a } without a { before it.");
                // \end (\right inside an environment cell is an error too)
                if (!LooksAt("\\end")) throw Error("\\right has no \\left before it.");
                pos += 4;
                string end = ReadGroupText("\\end").Trim();
                if (end != env) throw Error($"\\begin{{{env}}} is closed with \\end{{{end}}}.");
                AddRow(matrix, row);
                break;
            }
            environmentDepth--;
            return matrix;
        }

        /// <summary>Adds a row, skipping empty ones (a trailing \\, or blank lines in .kscript text).</summary>
        private static void AddRow(MathMatrix matrix, List<MathNode> row)
        {
            bool empty = row.All(c => c is MathRow r && r.Items.Count == 0);
            if (!empty) matrix.Rows.Add(row);
        }
    }

    /// <summary>\mathbf{...} and other font commands, kept so LaTeX output uses them again.</summary>
    public sealed class MathStyled : MathNode
    {
        public MathNode Body { get; set; }
        public string Command { get; set; }
    }
}
