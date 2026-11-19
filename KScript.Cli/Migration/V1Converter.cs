using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KScript.Migration
{
    /// <summary>What a converted value is used for, which decides how bare text such as yes or 42 is read.</summary>
    internal enum ValueHint
    {
        /// <summary>A value: numbers become numbers, true/false/yes/no become bools, everything else is text.</summary>
        Any,

        /// <summary>A condition: yes, y, 1 and true are true.</summary>
        Bool,

        /// <summary>Always text, such as a file path or a prompt.</summary>
        Text,
    }

    /// <summary>Converted KScript 2 expression code. Atomic code can take a method call or be an operand without brackets.</summary>
    internal readonly struct Code
    {
        public Code(string text, bool atomic)
        {
            Text = text;
            Atomic = atomic;
        }

        public string Text { get; }
        public bool Atomic { get; }

        /// <summary>The code, bracketed unless it is atomic.</summary>
        public string Operand => Atomic ? Text : "(" + Text + ")";

        public override string ToString() => Text;
    }

    /// <summary>
    /// Converts KScript 1 values (text with @commands, $variables and ~array lookups substituted in) to KScript 2
    /// expressions and templates. Anything it can't convert is reported through <see cref="Problem"/>.
    /// </summary>
    internal sealed class V1Converter
    {
        private static readonly Regex Number = new Regex(@"^-?\d+(\.\d+)?$", RegexOptions.Compiled);
        private static readonly Regex Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private static readonly HashSet<string> MathFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sqrt", "cbrt", "abs", "sign", "exp", "sin", "cos", "tan", "asin", "acos", "atan", "floor", "ceil", "round", "log", "log10", "log2", "pow", "min", "max",
        };

        /// <summary>Methods that KScript 2 has under the same name, for any receiver.</summary>
        private static readonly HashSet<string> SameMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "upper", "lower", "trim", "trim_start", "trim_end", "reverse", "contains", "starts_with", "ends_with", "index_of", "replace", "repeat",
            "pad_left", "pad_right", "length", "round", "floor", "ceil", "abs", "min", "max", "increment", "decrement", "is_empty", "count",
        };

        public Func<string, (string, string)> ResolveVariable { get; set; } = raw => ("$" + raw, "");

        /// <summary>Reports a construct that was kept as it was, or converted with a caveat.</summary>
        public Action<string> Problem { get; set; } = _ => { };

        #region Entry points

        /// <summary>A v1 attribute value or element text, as one KScript 2 expression.</summary>
        public string Expression(string v1, ValueHint hint = ValueHint.Any) => ExpressionCode(v1, hint).Text;

        public Code ExpressionCode(string v1, ValueHint hint = ValueHint.Any)
        {
            v1 ??= string.Empty;
            string trimmed = hint == ValueHint.Text && v1.Trim().Length > 0 && !(v1.Trim().StartsWith("'") && v1.Trim().EndsWith("'")) ? v1 : v1.Trim();
            string value = StripSurroundingQuotes(trimmed);
            bool wasQuoted = value.Length != trimmed.Length;
            List<V1Node> nodes = Parse(value);
            if (nodes == null)
            {
                return new Code(Quote(v1), true);
            }
            List<Piece> pieces = Pieces(nodes, trim: false);
            return Join(pieces, wasQuoted ? ValueHint.Text : hint);
        }

        /// <summary>A v1 value as a KScript 2 echo template: text with $name and { expr } inside.</summary>
        public string Template(string v1)
        {
            if (string.IsNullOrEmpty(v1)) return v1 ?? string.Empty;

            // v1 printed 'quoted' as quoted when the quotes surround the whole text.
            string trimmed = v1.Trim();
            string unquoted = StripSurroundingQuotes(trimmed);
            if (unquoted.Length != trimmed.Length) v1 = v1.Replace(trimmed, unquoted);
            List<V1Node> nodes = Parse(v1);
            if (nodes == null) return EscapeTemplateText(v1);

            var sb = new StringBuilder();
            List<Piece> pieces = Pieces(nodes, trim: false);
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece p = pieces[i];
                if (!p.IsCode)
                {
                    sb.Append(EscapeTemplateText(p.Text));
                    continue;
                }

                string next = i + 1 < pieces.Count && !pieces[i + 1].IsCode ? pieces[i + 1].Text : "";
                string previous = i > 0 && !pieces[i - 1].IsCode ? pieces[i - 1].Text : "";
                bool safeBefore = previous.Length == 0 || !(char.IsLetterOrDigit(previous[previous.Length - 1]) || previous[previous.Length - 1] == '_');
                bool simpleVariable = Regex.IsMatch(p.Code.Text, @"^\$[A-Za-z_][A-Za-z0-9_]*$");
                bool safeAfter = next.Length == 0 || !(char.IsLetterOrDigit(next[0]) || next[0] == '_' || next[0] == '.' || next[0] == '-' || next[0] == '[' || next[0] == '(');
                if (simpleVariable && safeAfter && safeBefore && !(i + 1 < pieces.Count && pieces[i + 1].IsCode))
                {
                    sb.Append(p.Code.Text);
                }
                else
                {
                    sb.Append("{ ").Append(p.Code.Text).Append(" }");
                }
            }
            return sb.ToString();
        }

        /// <summary>A v1 arithmetic expression, such as the text of &lt;math&gt; or the argument of @math().</summary>
        public Code Arithmetic(string v1)
        {
            string value = StripSurroundingQuotes((v1 ?? string.Empty).Trim());
            List<V1Node> nodes = Parse(value);
            if (nodes == null) return new Code(Quote(v1), true);
            return ArithmeticOf(nodes);
        }

        /// <summary>The arguments of a v1 &lt;call args="..."&gt;, each as an expression.</summary>
        public List<string> ArgumentList(string v1)
        {
            try
            {
                return V1Parser.ParseArgumentList(v1).Select(a => Arg(a, ValueHint.Any).Text).ToList();
            }
            catch (V1SyntaxException ex)
            {
                Problem($"Couldn't read the arguments '{v1}': {ex.Message}");
                return new List<string> { Quote(v1) };
            }
        }

        /// <summary>The variable that holds an array named the v1 way: files, ~people or '$name'.</summary>
        public Code ArrayVariable(string v1Name)
        {
            string name = StripSurroundingQuotes((v1Name ?? string.Empty).Trim()).TrimStart('~');
            if (name.StartsWith("$", StringComparison.Ordinal))
            {
                name = name.Substring(1);
            }
            (string code, string rest) = ResolveVariable(name);
            if (rest.Length > 0)
            {
                Problem($"'{v1Name}' isn't a simple array name.");
            }
            return new Code(code, true);
        }

        #endregion

        #region Pieces

        private readonly struct Piece
        {
            public Piece(string text)
            {
                Text = text;
                IsCode = false;
                Code = default;
            }

            public Piece(Code code)
            {
                Code = code;
                IsCode = true;
                Text = null;
            }

            public string Text { get; }
            public bool IsCode { get; }
            public Code Code { get; }
        }

        private List<V1Node> Parse(string value)
        {
            try
            {
                return V1Parser.ParseTemplate(value);
            }
            catch (V1SyntaxException ex)
            {
                Problem($"Couldn't read '{Shorten(value)}': {ex.Message}");
                return null;
            }
        }

        private List<Piece> Pieces(IEnumerable<V1Node> nodes, bool trim)
        {
            var pieces = new List<Piece>();
            foreach (V1Node node in nodes)
            {
                switch (node)
                {
                    case V1Text t:
                        pieces.Add(new Piece(t.Text.Replace("\\n", "\n")));
                        break;
                    case V1Literal { Quoted: true } q:
                        pieces.AddRange(Pieces(V1Parser.SplitText(q.Value), trim: false));
                        break;
                    case V1Literal n:
                        pieces.Add(new Piece(n.Value));
                        break;
                    case V1Var v:
                        (string code, string rest) = ResolveVariable(v.Name);
                        pieces.Add(new Piece(new Code(code, true)));
                        if (rest.Length > 0) pieces.Add(new Piece(rest));
                        break;
                    default:
                        pieces.Add(new Piece(Node(node)));
                        break;
                }
            }

            // Merge neighbouring text.
            var merged = new List<Piece>();
            foreach (Piece p in pieces)
            {
                if (!p.IsCode && merged.Count > 0 && !merged[merged.Count - 1].IsCode)
                {
                    merged[merged.Count - 1] = new Piece(merged[merged.Count - 1].Text + p.Text);
                }
                else
                {
                    merged.Add(p);
                }
            }

            if (trim && merged.Count > 0)
            {
                if (!merged[0].IsCode) merged[0] = new Piece(merged[0].Text.TrimStart());
                int last = merged.Count - 1;
                if (!merged[last].IsCode) merged[last] = new Piece(merged[last].Text.TrimEnd());
                merged.RemoveAll(p => !p.IsCode && p.Text.Length == 0);
            }

            return merged;
        }

        private Code Join(List<Piece> pieces, ValueHint hint)
        {
            if (pieces.Count == 0) return new Code(hint == ValueHint.Bool ? "false" : "''", true);
            if (pieces.Count == 1)
            {
                return pieces[0].IsCode ? pieces[0].Code : Literal(pieces[0].Text, hint);
            }

            var parts = new List<string>();
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece p = pieces[i];
                parts.Add(p.IsCode ? p.Code.Operand : Quote(p.Text));
            }

            // Two values side by side join as text, not add as numbers.
            if (pieces[0].IsCode && pieces[1].IsCode) parts.Insert(0, "''");
            return new Code(string.Join(" + ", parts), false);
        }

        private Code Literal(string text, ValueHint hint)
        {
            string trimmed = text.Trim();
            if (hint != ValueHint.Text)
            {
                if (Number.IsMatch(trimmed)) return new Code(trimmed, !trimmed.StartsWith("-", StringComparison.Ordinal));
                switch (trimmed.ToLowerInvariant())
                {
                    case "true":
                    case "yes":
                        return new Code("true", true);
                    case "false":
                    case "no":
                        return new Code("false", true);
                    case "y":
                    case "t":
                    case "1":
                        if (hint == ValueHint.Bool) return new Code("true", true);
                        break;
                    case "n":
                    case "f":
                    case "0":
                    case "":
                        if (hint == ValueHint.Bool) return new Code("false", true);
                        break;
                    case "null":
                        return new Code("null", true);
                }
            }
            return new Code(Quote(text), true);
        }

        private Code Arg(V1Arg arg, ValueHint hint) => Join(Pieces(arg.Parts, trim: true), hint);

        private Code Arg(List<V1Arg> args, int index, ValueHint hint = ValueHint.Any) =>
            index < args.Count ? Arg(args[index], hint) : new Code("null", true);

        /// <summary>An argument's raw text, for flags such as 'lt' or 'filename'.</summary>
        private static string RawArg(List<V1Arg> args, int index)
        {
            if (index >= args.Count) return null;
            var sb = new StringBuilder();
            foreach (V1Node part in args[index].Parts)
            {
                switch (part)
                {
                    case V1Text t: sb.Append(t.Text); break;
                    case V1Literal l when !(l.Quoted && (l.Value.Contains('$') || l.Value.Contains('~'))): sb.Append(l.Value); break;
                    default: return null;
                }
            }
            return sb.ToString().Trim();
        }

        #endregion

        #region Nodes

        private Code Node(V1Node node)
        {
            switch (node)
            {
                case CodeNode code:
                    return code.Code;
                case V1Command c:
                    return Command(c);
                case V1Method m:
                    return Method(m.Target == null ? null : (Code?)Node(m.Target), m);
                case V1Pipe p:
                    return Pipe(p);
                case V1Lambda l:
                    return new Code("fn(" + string.Join(", ", l.Parameters.Select(x => "$" + x)) + ") => " + LambdaBody(l), false);
                case V1ArrayRef a:
                    return ArrayRef(a);
                case V1Var v:
                    {
                        (string code, string rest) = ResolveVariable(v.Name);
                        return rest.Length == 0 ? new Code(code, true) : new Code(code + " + " + Quote(rest), false);
                    }
                case V1Literal { Quoted: true } q:
                    // v1 values were text until used: 'yes'->not() is a bool, but '7'->pad_left(3) stays text.
                    return q.Value.Trim().ToLowerInvariant() is "yes" or "no" or "true" or "false"
                        ? Literal(q.Value, ValueHint.Bool)
                        : Join(Pieces(V1Parser.SplitText(q.Value), trim: false), ValueHint.Text);
                case V1Literal n:
                    return Literal(n.Value, ValueHint.Any);
                case V1Text t:
                    return Literal(t.Text, ValueHint.Any);
                default:
                    return new Code("null", true);
            }
        }

        private string LambdaBody(V1Lambda lambda)
        {
            // Lambda parameters are written $x in v1 too, and must keep their own names.
            Func<string, (string, string)> outer = ResolveVariable;
            ResolveVariable = raw =>
            {
                string head = raw.Split('.', '-')[0];
                return lambda.Parameters.Contains(head) ? ("$" + head, raw.Substring(head.Length)) : outer(raw);
            };
            try
            {
                return Node(lambda.Body).Text;
            }
            finally
            {
                ResolveVariable = outer;
            }
        }

        private Code ArrayRef(V1ArrayRef a)
        {
            (string code, _) = ResolveVariable(a.Name);
            var sb = new StringBuilder(code);
            foreach (string raw in a.Indexes)
            {
                string index = raw.Trim();
                if (index.StartsWith("~", StringComparison.Ordinal))
                {
                    Problem($"The deep lookup [{index}] in ~{a.Name} has no KScript 2 equivalent; it was converted to a plain key.");
                    index = index.Substring(1);
                }

                string unquoted = StripSurroundingQuotes(index);
                if (unquoted.Length != index.Length)
                {
                    if (Identifier.IsMatch(unquoted)) sb.Append('.').Append(unquoted);
                    else sb.Append('[').Append(Quote(unquoted)).Append(']');
                }
                else if (Number.IsMatch(index))
                {
                    sb.Append('[').Append(index).Append(']');
                }
                else
                {
                    sb.Append('[').Append(Expression(index)).Append(']');
                }
            }
            return new Code(sb.ToString(), true);
        }

        private Code Pipe(V1Pipe p)
        {
            Code left = Node(p.Left);
            switch (p.Right)
            {
                case V1Method { Target: null } m:
                    return Method(left, m);
                case V1Command c:
                    {
                        var args = new List<V1Arg>();
                        var first = new V1Arg();
                        first.Parts.Add(new CodeNode(left));
                        args.Add(first);
                        args.AddRange(c.Args);
                        return Command(new V1Command(c.Name, args));
                    }
                case V1Var v:
                    (string code, _) = ResolveVariable(v.Name);
                    return new Code(code + "->call(" + left.Text + ")", true);
                default:
                    return new Code(left.Operand + " |> " + Node(p.Right).Text, false);
            }
        }

        /// <summary>Already converted code, used when a pipe passes a value on to a command.</summary>
        private sealed class CodeNode : V1Node
        {
            public CodeNode(Code code) => Code = code;
            public Code Code { get; }
        }

        #endregion

        #region Methods

        private Code Method(Code? target, V1Method m)
        {
            string t = target?.Operand ?? "$it";
            List<V1Arg> args = m.Args;
            string A(int i) => Arg(args, i).Operand;
            string all = string.Join(", ", args.Select(a => Arg(a, ValueHint.Any).Text));

            switch (m.Name)
            {
                case "add": return new Code($"{t} + {A(0)}", false);
                case "sub": return new Code($"{t} - {A(0)}", false);
                case "mul": return new Code($"{t} * {A(0)}", false);
                case "div": return new Code($"{t} / {A(0)}", false);
                case "mod": return new Code($"{t} % {A(0)}", false);
                case "pow": return new Code($"{t} ** {A(0)}", false);
                case "lt": return new Code($"{t} lt {A(0)}", false);
                case "lte": return new Code($"{t} le {A(0)}", false);
                case "mt": return new Code($"{t} gt {A(0)}", false);
                case "mte": return new Code($"{t} ge {A(0)}", false);
                case "between": return new Code($"{t} ge {A(0)} and {t} le {A(1)}", false);
                case "is_zero": return new Code($"{t} == 0", false);
                case "not_zero": return new Code($"{t} != 0", false);
                case "positive": return new Code($"{t} gt 0", false);
                case "negative": return new Code($"{t} lt 0", false);
                case "not": return new Code($"not {t}", false);
                case "and": return new Code($"{t} and {A(0)}", false);
                case "or": return new Code($"{t} or {A(0)}", false);
                case "xor": return new Code($"{t} != {A(0)}", false);
                case "equals":
                    return RawArg(args, 1) is string ic && Literal(ic, ValueHint.Bool).Text == "true"
                        ? new Code($"{t}->lower() == {A(0)}->lower()", false)
                        : new Code($"{t} == {A(0)}", false);
                case "default": return new Code($"{t} ?? {A(0)}", false);
                case "has_value": return new Code($"({t} ?? '') != ''", false);
                case "type": return new Code($"@type({target?.Text ?? "$it"})", true);
                case "is_number": return new Code($"@type({t}->to_number() ?? '') == 'number'", false);
                case "is_bool": return new Code($"{t} in ['true', 'false', 'yes', 'no', true, false]", false);
                case "is_url": return new Code($"{t}->matches('^https?://')", true);
                case "call": return new Code($"{t}->call({all})", true);
                case "split":
                    return args.Count > 1 ? new Code($"{t}->split({Arg(args, 0).Text})[{Arg(args, 1).Text}]", true) : new Code($"{t}->split({all})", true);
                case "substring":
                    return args.Count > 1
                        ? new Code($"{t}->slice({Arg(args, 0).Text}, {A(0)} + {A(1)})", true)
                        : new Code($"{t}->slice({all})", true);
                case "char_at": return new Code($"{t}->chars()[{Arg(args, 0).Text}]", true);
                case "to_array": return new Code($"{t}->split(',')", true);
                case "paragraph":
                    Problem("->paragraph() has no KScript 2 equivalent; the text is printed as it is.");
                    return target ?? new Code("$it", true);
                case "increment":
                case "decrement":
                    if (args.Count == 0) return new Code($"{t}->{m.Name}()", true);
                    return new Code($"{t} {(m.Name == "increment" ? "+" : "-")} {A(0)}", false);
            }

            if (!SameMethods.Contains(m.Name))
            {
                Problem($"->{m.Name}() was kept as it is; check that KScript 2 has it.");
            }
            return new Code($"{t}->{m.Name}({all})", true);
        }

        #endregion

        #region Commands

        private Code Command(V1Command c)
        {
            List<V1Arg> args = c.Args;
            Code A(int i, ValueHint hint = ValueHint.Any) => ArgOf(args, i, hint);
            string all = string.Join(", ", args.Select((_, i) => A(i).Text));

            switch (c.Name)
            {
                case "math":
                    return args.Count == 0 ? new Code("null", true) : ArithmeticOfArg(args[0]);

                case "more_less":
                    {
                        string[] ops = { "mt", "mte", "lt", "lte" };
                        int opIndex = args.Count >= 3 && ops.Contains(RawArg(args, 1)?.ToLowerInvariant()) ? 1 : 2;
                        int bIndex = opIndex == 1 ? 2 : 1;
                        string op = (args.Count >= 3 ? RawArg(args, opIndex) : "lt")?.ToLowerInvariant() switch
                        {
                            "mt" => "gt",
                            "mte" => "ge",
                            "lte" => "le",
                            _ => "lt",
                        };
                        return new Code($"{A(0).Operand} {op} {A(bIndex).Operand}", false);
                    }

                case "equals":
                    return args.Count > 2 && Literal(RawArg(args, 2) ?? "", ValueHint.Bool).Text == "true"
                        ? new Code($"{A(0).Operand}->lower() == {A(1).Operand}->lower()", false)
                        : new Code($"{A(0).Operand} == {A(1).Operand}", false);

                case "and":
                case "any":
                    {
                        string op = c.Name == "and" ? " and " : " or ";
                        return args.Count == 1 ? A(0, ValueHint.Bool) : new Code(string.Join(op, args.Select((_, i) => A(i, ValueHint.Bool).Operand)), false);
                    }

                case "not":
                    return new Code("not " + A(0, ValueHint.Bool).Operand, false);

                case "iftrue":
                    return new Code($"{A(0, ValueHint.Bool).Operand} ? {A(1).Operand} : ''", false);

                case "if":
                    return args.Count >= 2
                        ? new Code($"{A(0, ValueHint.Bool).Operand} ? {A(1).Operand} : ''", false)
                        : new Code($"({A(0).Operand} ?? '') != ''", false);

                case "ifnull":
                    return new Code($"{A(0).Operand} ?? {A(1).Operand}", false);

                case "null":
                    return args.Count == 0 ? new Code("null", true) : new Code($"{A(0).Operand} == null", false);

                case "acount":
                    return new Code(ArrayVariableOfArg(args, 0).Text + "->count()", true);

                case "array":
                    return new Code($"{ArrayVariableOfArg(args, 0).Text}[{A(1).Text}]", true);

                case "atindex":
                    return new Code($"{A(0).Operand}->chars()[{A(1).Text}]", true);

                case "tie":
                    return new Code("[" + all + "]", true);

                case "range":
                    return new Code($"{A(0).Operand} ge {A(1).Operand} and {A(0).Operand} le {A(2).Operand}", false);

                case "length":
                    return args.Count >= 2
                        ? new Code($"{A(0).Operand}->length() == {A(1).Operand}", false)
                        : new Code($"{A(0, ValueHint.Text).Operand}->length()", true);

                case "contains":
                    return new Code($"{A(0, ValueHint.Text).Operand}->contains({A(1, ValueHint.Text).Text})", true);

                case "split":
                    return new Code($"{A(0, ValueHint.Text).Operand}->split({A(1, ValueHint.Text).Text})[{A(2).Text}]", true);

                case "to_lower":
                    return new Code($"{A(0, ValueHint.Text).Operand}->lower()", true);

                case "char":
                    return new Code($"@char_of({A(0).Text})", true);

                case "guid":
                    return new Code("@guid()", true);

                case "datetime":
                    return args.Count == 0 ? new Code("@now()", true) : new Code($"@now()->format({A(0, ValueHint.Text).Text})", true);

                case "date":
                    return new Code($"@now()->format({(args.Count == 0 ? "'D'" : A(0, ValueHint.Text).Text)})", true);

                case "time":
                    return new Code($"@now()->format({(args.Count == 0 ? "'HH:mm:ss'" : A(0, ValueHint.Text).Text)})", true);

                case "to_date":
                    if (args.Count > 1) Problem("@to_date() with a calculation such as D1Y2 was converted without it; add ->add_days() and friends.");
                    return new Code($"{A(0, ValueHint.Text).Operand}->to_date()", true);

                case "args":
                    return args.Count == 0 ? new Code("$args->count()", true) : new Code($"$args[{A(0).Text}]", true);

                case "k":
                    return K(RawArg(args, 0) ?? "");

                case "join_path":
                    return new Code($"@join_path({string.Join(", ", args.Select((_, i) => A(i, ValueHint.Text).Text))})", true);

                case "dir_exists":
                    return new Code($"@is_folder({A(0, ValueHint.Text).Text})", true);

                case "read_text":
                    return new Code($"@read_text({A(0, ValueHint.Text).Text})", true);

                case "api":
                    return new Code($"@fetch({A(0, ValueHint.Text).Text})->text()", true);

                case "extension_of":
                    return new Code($"@file({A(0, ValueHint.Text).Text})->extension()", true);

                case "path":
                    return PathCommand(A(0, ValueHint.Text), (RawArg(args, 1) ?? "filename").ToLowerInvariant());

                case "getfiles":
                    return GetFiles(args);

                case "random":
                    return args.Count >= 2 ? new Code($"@random({A(0).Text}, {A(1).Text})", true) : new Code("@random(0, 2147483647)", true);

                case "average":
                    return new Code($"@average({all})", true);

                case "to_base64":
                case "from_base64":
                case "to_md5":
                    Problem($"@{c.Name}() has no KScript 2 equivalent yet and was kept as it is.");
                    return new Code($"@{c.Name}({all})", true);

                case "pretty_print":
                    return new Code($"{A(0).Operand}->to_json(true)", true);
            }

            if (KScript.Runtime.Retired.Commands.TryGetValue(c.Name, out var retired))
            {
                Problem($"@{c.Name}() can't be converted automatically. {retired.Replacement}");
            }
            return new Code($"@{c.Name}({all})", true);
        }

        private Code ArgOf(List<V1Arg> args, int index, ValueHint hint)
        {
            if (index >= args.Count) return new Code("null", true);
            if (args[index].Parts.Count == 1 && args[index].Parts[0] is CodeNode code) return code.Code;
            return Arg(args[index], hint);
        }

        private Code ArrayVariableOfArg(List<V1Arg> args, int index)
        {
            if (index >= args.Count) return new Code("null", true);
            string raw = RawArg(args, index);
            if (raw != null) return ArrayVariable(raw);
            if (args[index].Parts.Count == 1 && args[index].Parts[0] is V1Var v) return ArrayVariable(v.Name);
            if (args[index].Parts.Count == 1 && args[index].Parts[0] is V1Literal { Quoted: true } l) return ArrayVariable(l.Value);
            return Arg(args[index], ValueHint.Any);
        }

        private Code K(string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "os.name":
                case "os.platform": return new Code("$os.name", true);
                case "path.separator": return new Code("$os.separator", true);
                case "newline": return new Code("$os.newline", true);
                case "user.home": return new Code("$os.home", true);
                case "temp.directory": return new Code("$os.temp", true);
                case "machine.name": return new Code("$os.machine", true);
                case "username": return new Code("$os.user", true);
                case "script.path": return new Code("$script.path", true);
                case "script.directory": return new Code("$script.dir", true);
                case "math.pi": return new Code("$pi", true);
                case "math.e": return new Code("$e", true);
                default: return new Code("$script.dir", true);
            }
        }

        private Code PathCommand(Code path, string type)
        {
            switch (type)
            {
                case "1":
                case "directory": return new Code($"@file({path.Text})->directory()", true);
                case "2":
                case "fullpath": return new Code($"@full_path({path.Text})", true);
                case "3":
                case "extension": return new Code($"'.' + @file({path.Text})->extension()", false);
                case "4":
                case "name": return new Code($"@file({path.Text})->stem()", true);
                default: return new Code($"@file({path.Text})->name()", true);
            }
        }

        private Code GetFiles(List<V1Arg> args)
        {
            string folder = ArgOf(args, 0, ValueHint.Text).Text;
            if (args.Count >= 3)
            {
                string format = RawArg(args, 2);
                if (format != null && format.Trim() != "$value")
                {
                    Problem($"@getfiles() with the format '{format}' now returns file values; format them with ->map(fn($f) => ...).");
                }
            }

            string types = args.Count >= 4 ? RawArg(args, 3) : "*";
            if (types == "*" || types == "")
            {
                return new Code($"@files({folder})", true);
            }

            string extensions;
            if (types != null)
            {
                var list = types.Split(',').Select(x => x.Trim().TrimStart('.').ToLowerInvariant()).Where(x => x.Length > 0).Select(Quote);
                extensions = "[" + string.Join(", ", list) + "]";
            }
            else
            {
                extensions = $"{ArgOf(args, 3, ValueHint.Text).Operand}->split(',')->map(fn($t) => $t->trim()->trim_start('.')->lower())";
            }
            return new Code($"@files({folder})->filter(fn($f) => {extensions}->contains($f->extension()->lower()))", true);
        }

        #endregion

        #region Arithmetic

        private Code ArithmeticOfArg(V1Arg arg)
        {
            // @math('$i+1') reads the quoted text as the sum, the same as @math($i+1).
            if (arg.Parts.Count == 1 && arg.Parts[0] is V1Literal { Quoted: true } q)
            {
                return ArithmeticOf(V1Parser.SplitText(q.Value));
            }
            return ArithmeticOf(arg.Parts);
        }

        private Code ArithmeticOf(IEnumerable<V1Node> nodes)
        {
            var sb = new StringBuilder();
            int count = 0;
            Code only = default;
            foreach (V1Node node in nodes)
            {
                if (node is V1Text t)
                {
                    sb.Append(ArithmeticText(t.Text));
                    if (!string.IsNullOrWhiteSpace(t.Text)) count += 2;
                    continue;
                }

                Code code = node is V1Literal { Quoted: true } q ? ArithmeticOf(V1Parser.SplitText(q.Value)) : Node(node);
                sb.Append(code.Operand);
                only = code;
                count++;
            }

            if (count == 1) return only;
            string text = sb.ToString().Trim();
            return new Code(text, Number.IsMatch(text) && !text.StartsWith("-", StringComparison.Ordinal));
        }

        /// <summary>The operators and numbers of a v1 sum, with ^ as ** and functions such as sqrt() as @sqrt().</summary>
        private string ArithmeticText(string text)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    string word = text.Substring(start, i - start);
                    bool call = i < text.Length && text[i] == '(';
                    if (call && MathFunctions.Contains(word)) sb.Append('@').Append(word.ToLowerInvariant());
                    else if (!call && (word == "pi" || word == "e")) sb.Append('$').Append(word);
                    else
                    {
                        Problem($"'{word}' in the sum '{Shorten(text)}' was kept as it is.");
                        sb.Append(word);
                    }
                    continue;
                }

                if (c == '^')
                {
                    sb.Append("**");
                }
                else
                {
                    sb.Append(c);
                }
                i++;
            }
            return sb.ToString();
        }

        #endregion

        #region Text helpers

        /// <summary>A KScript 2 string literal. KScript strings have no escapes, so a value with both quotes is split.</summary>
        public static string Quote(string text)
        {
            text ??= string.Empty;
            if (text.IndexOf('\'') < 0) return "'" + text + "'";
            if (text.IndexOf('"') < 0) return "\"" + text + "\"";
            return string.Join(" + \"'\" + ", text.Split('\'').Select(p => "'" + p + "'"));
        }

        public static string StripSurroundingQuotes(string value)
        {
            if (value != null && value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'' && value.IndexOf('\'', 1, value.Length - 2) < 0)
            {
                return value.Substring(1, value.Length - 2);
            }
            return value;
        }

        /// <summary>Text for a KScript 2 template, with { and } doubled.</summary>
        public static string EscapeTemplateText(string text) => text.Replace("\\n", "\n").Replace("{", "{{").Replace("}", "}}");

        private static string Shorten(string text) => text.Length > 60 ? text.Substring(0, 57) + "..." : text;

        #endregion
    }
}
