using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KScript.Pages
{
    /// <summary>An error in a page: bad syntax, a missing variable, a wrong type, and so on.</summary>
    public class PageException : Exception
    {
        public PageException(string message) : base(message) { }
        public PageException(string message, Exception inner) : base(message, inner) { }

        /// <summary>File and line, filled in by the renderer as the error passes through an element.</summary>
        public string Location { get; set; }
    }

    /// <summary>Variables for one page render. Each element body, loop iteration and lambda call gets a child scope.</summary>
    public sealed class PageScope
    {
        private readonly Dictionary<string, object> _vars = new Dictionary<string, object>(StringComparer.Ordinal);

        public PageScope(PageScope parent = null) { Parent = parent; }

        public PageScope Parent { get; }

        public PageScope Child() => new PageScope(this);

        public bool TryGet(string name, out object value)
        {
            for (var s = this; s != null; s = s.Parent)
            {
                if (s._vars.TryGetValue(name, out value)) return true;
            }
            value = null;
            return false;
        }

        public bool Has(string name) => TryGet(name, out _);

        /// <summary>Declares (or redeclares) a variable in this scope.</summary>
        public void Declare(string name, object value) => _vars[name] = value;

        /// <summary>Updates an existing variable wherever it was declared.</summary>
        public void Assign(string name, object value)
        {
            for (var s = this; s != null; s = s.Parent)
            {
                if (s._vars.ContainsKey(name)) { s._vars[name] = value; return; }
            }
            throw new PageException($"${name} is not defined, so it can't be set. Declare it first with <let name=\"{name}\" ...>.");
        }

        public IEnumerable<string> Names()
        {
            var seen = new HashSet<string>();
            for (var s = this; s != null; s = s.Parent)
                foreach (var k in s._vars.Keys) if (seen.Add(k)) yield return k;
        }
    }

    #region Syntax tree

    internal abstract class PageExpr
    {
        public string Source { get; set; }
    }

    internal sealed class LiteralNode : PageExpr { public object Value; }
    internal sealed class VarNode : PageExpr { public string Name; }
    internal sealed class ListNode : PageExpr { public List<PageExpr> Items; }
    internal sealed class MapNode : PageExpr { public List<KeyValuePair<string, PageExpr>> Entries; }
    internal sealed class MemberNode : PageExpr { public PageExpr Target; public string Name; public bool Safe; }
    internal sealed class IndexNode : PageExpr { public PageExpr Target; public PageExpr Index; }
    internal sealed class MethodNode : PageExpr { public PageExpr Target; public string Name; public List<PageExpr> Args; public bool Safe; }
    internal sealed class CommandNode : PageExpr { public string Name; public List<PageExpr> Args; }
    internal sealed class UnaryNode : PageExpr { public string Op; public PageExpr Operand; }
    internal sealed class BinaryNode : PageExpr { public string Op; public PageExpr Left, Right; }
    internal sealed class TernaryNode : PageExpr { public PageExpr Condition, Then, Else; }
    internal sealed class LambdaNode : PageExpr { public List<string> Parameters; public PageExpr Body; }

    /// <summary><c>left |> name(args)</c>, <c>left |> @cmd(args)</c> or <c>left |> fn($x) => ...</c>.</summary>
    internal sealed class PipeNode : PageExpr { public PageExpr Left; public PageExpr Target; public string MethodName; public List<PageExpr> MethodArgs; }

    #endregion

    #region Lexer

    internal enum TokKind { Number, String, Word, Var, Command, Symbol, End }

    internal readonly struct Tok
    {
        public Tok(TokKind kind, string text, int pos) { Kind = kind; Text = text; Pos = pos; }
        public TokKind Kind { get; }
        public string Text { get; }
        public int Pos { get; }
        public bool Is(string symbolOrWord) => (Kind == TokKind.Symbol || Kind == TokKind.Word) && Text == symbolOrWord;
        public override string ToString() => Kind == TokKind.End ? "the end of the expression" : $"'{Text}'";
    }

    internal static class PageLexer
    {
        private static readonly string[] Symbols =
        {
            "===", "!==", "==", "!=", ">=", "<=", "??", "?.", "->", "|>", "=>",
            "(", ")", "[", "]", "{", "}", ",", ":", ".", "?", ">", "<", "+", "-", "*", "/", "%", "!", "=",
        };

        public static List<Tok> Lex(string src)
        {
            var toks = new List<Tok>();
            int i = 0;
            while (i < src.Length)
            {
                char c = src[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1]) && (toks.Count == 0 || toks[^1].Kind == TokKind.Symbol && toks[^1].Text != ")" && toks[^1].Text != "]")))
                {
                    int start = i;
                    while (i < src.Length && char.IsDigit(src[i])) i++;
                    if (i + 1 < src.Length && src[i] == '.' && char.IsDigit(src[i + 1]))
                    {
                        i++;
                        while (i < src.Length && char.IsDigit(src[i])) i++;
                    }
                    toks.Add(new Tok(TokKind.Number, src.Substring(start, i - start), start));
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    int start = i++;
                    var sb = new StringBuilder();
                    while (true)
                    {
                        if (i >= src.Length) throw new PageException($"Unclosed string starting at column {start + 1}: {src.Substring(start)}");
                        char ch = src[i];
                        if (ch == '\\' && i + 1 < src.Length)
                        {
                            char n = src[i + 1];
                            sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => n });
                            i += 2;
                            continue;
                        }
                        if (ch == c) { i++; break; }
                        sb.Append(ch);
                        i++;
                    }
                    toks.Add(new Tok(TokKind.String, sb.ToString(), start));
                    continue;
                }

                if ((c == '$' || c == '@') && i + 1 < src.Length && IsIdentStart(src[i + 1]))
                {
                    int start = i++;
                    while (i < src.Length && IsIdentPart(src[i])) i++;
                    toks.Add(new Tok(c == '$' ? TokKind.Var : TokKind.Command, src.Substring(start + 1, i - start - 1), start));
                    continue;
                }

                if (IsIdentStart(c))
                {
                    int start = i;
                    while (i < src.Length && IsIdentPart(src[i])) i++;
                    toks.Add(new Tok(TokKind.Word, src.Substring(start, i - start), start));
                    continue;
                }

                string sym = Symbols.FirstOrDefault(s => string.CompareOrdinal(src, i, s, 0, s.Length) == 0);
                if (sym == null)
                {
                    if (c == '&' || c == '|')
                        throw new PageException($"Unexpected '{c}' at column {i + 1}. Use the words 'and' / 'or' (XML attributes can't hold '&&').");
                    throw new PageException($"Unexpected character '{c}' at column {i + 1} in: {src}");
                }
                toks.Add(new Tok(TokKind.Symbol, sym, i));
                i += sym.Length;
            }
            toks.Add(new Tok(TokKind.End, string.Empty, src.Length));
            return toks;
        }

        public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
        public static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    #endregion

    #region Parser

    internal sealed class PageParser
    {
        private readonly List<Tok> _toks;
        private readonly string _src;
        private int _pos;

        private static readonly Dictionary<string, PageExpr> Cache = new Dictionary<string, PageExpr>();

        private PageParser(string src)
        {
            _src = src;
            _toks = PageLexer.Lex(src);
        }

        public static PageExpr Parse(string src)
        {
            if (src == null) throw new PageException("Missing expression.");
            lock (Cache)
            {
                if (Cache.TryGetValue(src, out var cached)) return cached;
            }

            var p = new PageParser(src);
            if (p.Peek.Kind == TokKind.End) throw new PageException("Empty expression.");
            var expr = p.ParseExpression();
            if (p.Peek.Kind != TokKind.End)
            {
                if (p.Peek.Is("="))
                    throw new PageException($"'=' assigns a value and can't be used here. To compare, use '==' (or '===' for type and value): {src}");
                throw new PageException($"Unexpected {p.Peek} at column {p.Peek.Pos + 1} in: {src}");
            }

            lock (Cache)
            {
                if (Cache.Count > 5000) Cache.Clear();
                Cache[src] = expr;
            }
            return expr;
        }

        private Tok Peek => _toks[_pos];
        private Tok PeekAt(int offset) => _toks[Math.Min(_pos + offset, _toks.Count - 1)];
        private Tok Next() => _toks[_pos++];

        private bool Accept(string text)
        {
            if (Peek.Is(text)) { _pos++; return true; }
            return false;
        }

        private void Expect(string text)
        {
            if (!Accept(text)) throw new PageException($"Expected '{text}' but found {Peek} at column {Peek.Pos + 1} in: {_src}");
        }

        private T Mark<T>(T node, int startPos) where T : PageExpr
        {
            int end = Math.Max(startPos, Peek.Pos);
            node.Source = _src.Substring(startPos, end - startPos).Trim();
            return node;
        }

        public PageExpr ParseExpression() => ParsePipe();

        private PageExpr ParsePipe()
        {
            int start = Peek.Pos;
            var left = ParseTernary();
            while (Accept("|>"))
            {
                var pipe = new PipeNode { Left = left };
                if (Peek.Kind == TokKind.Word && Peek.Text != "fn" && PeekAt(1).Is("("))
                {
                    pipe.MethodName = Next().Text;
                    pipe.MethodArgs = ParseArgs();
                }
                else
                {
                    pipe.Target = ParseTernary();
                }
                left = Mark(pipe, start);
            }
            return left;
        }

        private PageExpr ParseTernary()
        {
            int start = Peek.Pos;
            var cond = ParseCoalesce();
            if (Accept("?"))
            {
                var then = ParseTernary();
                Expect(":");
                var other = ParseTernary();
                return Mark(new TernaryNode { Condition = cond, Then = then, Else = other }, start);
            }
            return cond;
        }

        private PageExpr ParseCoalesce() => ParseLeftAssoc(ParseOr, "??");
        private PageExpr ParseOr() => ParseLeftAssoc(ParseAnd, "or");
        private PageExpr ParseAnd() => ParseLeftAssoc(ParseNot, "and");

        private PageExpr ParseNot()
        {
            int start = Peek.Pos;
            if (Accept("not") || Accept("!"))
                return Mark(new UnaryNode { Op = "not", Operand = ParseNot() }, start);
            return ParseEquality();
        }

        private PageExpr ParseEquality() => ParseLeftAssoc(ParseComparison, "==", "!=", "===", "!==", "eq", "ne");
        private PageExpr ParseComparison() => ParseLeftAssoc(ParseIn, "lt", "le", "gt", "ge", "<", "<=", ">", ">=");
        private PageExpr ParseIn() => ParseLeftAssoc(ParseAdditive, "in");
        private PageExpr ParseAdditive() => ParseLeftAssoc(ParseMultiplicative, "+", "-");
        private PageExpr ParseMultiplicative() => ParseLeftAssoc(ParseUnary, "*", "/", "%");

        private PageExpr ParseLeftAssoc(Func<PageExpr> next, params string[] ops)
        {
            int start = Peek.Pos;
            var left = next();
            while (true)
            {
                string op = ops.FirstOrDefault(o => Peek.Is(o));
                if (op == null) return left;
                _pos++;
                var right = next();
                left = Mark(new BinaryNode { Op = Normalise(op), Left = left, Right = right }, start);
            }
        }

        private static string Normalise(string op) => op switch
        {
            "eq" => "==", "ne" => "!=", "lt" => "<", "le" => "<=", "gt" => ">", "ge" => ">=",
            _ => op,
        };

        private PageExpr ParseUnary()
        {
            int start = Peek.Pos;
            if (Accept("-")) return Mark(new UnaryNode { Op = "-", Operand = ParseUnary() }, start);
            if (Accept("+")) return ParseUnary();
            return ParsePostfix();
        }

        private PageExpr ParsePostfix()
        {
            int start = Peek.Pos;
            var expr = ParsePrimary();
            while (true)
            {
                if (Peek.Is(".") || Peek.Is("?."))
                {
                    bool safe = Next().Text == "?.";
                    var name = Next();
                    if (name.Kind != TokKind.Word && name.Kind != TokKind.Number)
                        throw new PageException($"Expected a property name after '.' at column {name.Pos + 1} in: {_src}");
                    expr = Mark(new MemberNode { Target = expr, Name = name.Text, Safe = safe }, start);
                }
                else if (Accept("["))
                {
                    var index = ParseExpression();
                    Expect("]");
                    expr = Mark(new IndexNode { Target = expr, Index = index }, start);
                }
                else if (Peek.Is("->"))
                {
                    _pos++;
                    var name = Next();
                    if (name.Kind != TokKind.Word) throw new PageException($"Expected a method name after '->' at column {name.Pos + 1} in: {_src}");
                    var args = Peek.Is("(") ? ParseArgs() : new List<PageExpr>();
                    expr = Mark(new MethodNode { Target = expr, Name = name.Text, Args = args }, start);
                }
                else
                {
                    return expr;
                }
            }
        }

        private List<PageExpr> ParseArgs()
        {
            Expect("(");
            var args = new List<PageExpr>();
            if (Accept(")")) return args;
            do { args.Add(ParseExpression()); } while (Accept(","));
            Expect(")");
            return args;
        }

        private PageExpr ParsePrimary()
        {
            var t = Peek;
            int start = t.Pos;
            switch (t.Kind)
            {
                case TokKind.Number:
                    _pos++;
                    return Mark(new LiteralNode { Value = double.Parse(t.Text, CultureInfo.InvariantCulture) }, start);
                case TokKind.String:
                    _pos++;
                    return Mark(new LiteralNode { Value = t.Text }, start);
                case TokKind.Var:
                    _pos++;
                    return Mark(new VarNode { Name = t.Text }, start);
                case TokKind.Command:
                    _pos++;
                    return Mark(new CommandNode { Name = t.Text, Args = Peek.Is("(") ? ParseArgs() : new List<PageExpr>() }, start);
                case TokKind.Word:
                    switch (t.Text)
                    {
                        case "true": _pos++; return Mark(new LiteralNode { Value = true }, start);
                        case "false": _pos++; return Mark(new LiteralNode { Value = false }, start);
                        case "null": _pos++; return Mark(new LiteralNode { Value = null }, start);
                        case "fn": return ParseLambda();
                    }
                    throw new PageException($"Unknown name '{t.Text}' at column {t.Pos + 1}. Variables start with $ (${t.Text}) and commands with @ (@{t.Text}(...)). In: {_src}");
                case TokKind.Symbol:
                    if (Accept("("))
                    {
                        var inner = ParseExpression();
                        Expect(")");
                        return inner;
                    }
                    if (Accept("["))
                    {
                        var items = new List<PageExpr>();
                        if (!Accept("]"))
                        {
                            do
                            {
                                if (Peek.Is("]")) break; // trailing comma
                                items.Add(ParseExpression());
                            } while (Accept(","));
                            Expect("]");
                        }
                        return Mark(new ListNode { Items = items }, start);
                    }
                    if (Accept("{"))
                    {
                        var entries = new List<KeyValuePair<string, PageExpr>>();
                        if (!Accept("}"))
                        {
                            do
                            {
                                if (Peek.Is("}")) break;
                                var key = Next();
                                if (key.Kind != TokKind.Word && key.Kind != TokKind.String && key.Kind != TokKind.Number)
                                    throw new PageException($"Expected a key in map literal at column {key.Pos + 1} in: {_src}");
                                Expect(":");
                                entries.Add(new KeyValuePair<string, PageExpr>(key.Text, ParseExpression()));
                            } while (Accept(","));
                            Expect("}");
                        }
                        return Mark(new MapNode { Entries = entries }, start);
                    }
                    break;
            }
            throw new PageException($"Unexpected {t} at column {t.Pos + 1} in: {_src}");
        }

        private PageExpr ParseLambda()
        {
            int start = Peek.Pos;
            Expect("fn");
            Expect("(");
            var ps = new List<string>();
            if (!Accept(")"))
            {
                do
                {
                    var p = Next();
                    if (p.Kind != TokKind.Var && p.Kind != TokKind.Word)
                        throw new PageException($"Expected a parameter name like $x at column {p.Pos + 1} in: {_src}");
                    ps.Add(p.Text);
                } while (Accept(","));
                Expect(")");
            }
            Expect("=>");
            var body = ParseTernary();
            return Mark(new LambdaNode { Parameters = ps, Body = body }, start);
        }
    }

    #endregion

    /// <summary>Evaluates page expressions against a scope.</summary>
    public static class PageEvaluator
    {
        public static object Evaluate(string expression, PageScope scope, PageContext ctx) =>
            Eval(PageParser.Parse(expression), scope, ctx);

        internal static object Eval(PageExpr e, PageScope scope, PageContext ctx)
        {
            ctx.Step();
            switch (e)
            {
                case LiteralNode lit:
                    return lit.Value;

                case VarNode v:
                    if (scope.TryGet(v.Name, out var value)) return value;
                    throw new PageException(UndefinedMessage(v.Name, scope));

                case ListNode list:
                    return list.Items.Select(i => Eval(i, scope, ctx)).ToList();

                case MapNode map:
                    var dict = new Dictionary<string, object>();
                    foreach (var kv in map.Entries) dict[kv.Key] = Eval(kv.Value, scope, ctx);
                    return dict;

                case MemberNode member:
                {
                    var target = Eval(member.Target, scope, ctx);
                    if (target == null)
                    {
                        if (member.Safe) return null;
                        throw new PageException($"Can't read '.{member.Name}' of null in '{member.Source}'. Use '?.' if it may be missing.");
                    }
                    return GetMember(target, member.Name, member.Source);
                }

                case IndexNode index:
                {
                    var target = Eval(index.Target, scope, ctx);
                    var key = Eval(index.Index, scope, ctx);
                    return GetIndex(target, key, index.Source);
                }

                case MethodNode method:
                {
                    var target = Eval(method.Target, scope, ctx);
                    var args = method.Args.Select(a => Eval(a, scope, ctx)).ToList();
                    return PageMethods.Invoke(method.Name, target, args, ctx, method.Source);
                }

                case CommandNode cmd:
                {
                    var args = cmd.Args.Select(a => Eval(a, scope, ctx)).ToList();
                    return CallCommand(cmd.Name, args, scope, ctx, cmd.Source);
                }

                case PipeNode pipe:
                {
                    var left = Eval(pipe.Left, scope, ctx);
                    if (pipe.MethodName != null)
                    {
                        var args = pipe.MethodArgs.Select(a => Eval(a, scope, ctx)).ToList();
                        return PageMethods.Invoke(pipe.MethodName, left, args, ctx, pipe.Source);
                    }
                    if (pipe.Target is CommandNode pc)
                    {
                        var args = new List<object> { left };
                        args.AddRange(pc.Args.Select(a => Eval(a, scope, ctx)));
                        return CallCommand(pc.Name, args, scope, ctx, pipe.Source);
                    }
                    var fn = Eval(pipe.Target, scope, ctx) as PageLambda
                        ?? throw new PageException($"The right side of '|>' must be a method like filter(...), a command like @json(), or a lambda, in '{pipe.Source}'.");
                    return Call(fn, new List<object> { left }, ctx);
                }

                case UnaryNode un:
                {
                    var operand = Eval(un.Operand, scope, ctx);
                    if (un.Op == "not") return !PageValues.Truthy(operand);
                    return -PageValues.ToNumber(operand, $"'-' in '{un.Source}'");
                }

                case TernaryNode t:
                    return PageValues.Truthy(Eval(t.Condition, scope, ctx)) ? Eval(t.Then, scope, ctx) : Eval(t.Else, scope, ctx);

                case LambdaNode lam:
                    return new PageLambda(lam.Parameters, lam.Body, scope, lam.Source);

                case BinaryNode bin:
                    return EvalBinary(bin, scope, ctx);
            }
            throw new PageException($"Can't evaluate '{e.Source}'.");
        }

        private static string UndefinedMessage(string name, PageScope scope)
        {
            string close = scope.Names().OrderBy(n => Distance(n, name)).FirstOrDefault();
            string hint = close != null && Distance(close, name) <= 2 ? $" Did you mean ${close}?" : string.Empty;
            return $"${name} is not defined.{hint}";
        }

        internal static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        private static object EvalBinary(BinaryNode bin, PageScope scope, PageContext ctx)
        {
            switch (bin.Op)
            {
                case "and":
                {
                    var l = Eval(bin.Left, scope, ctx);
                    return PageValues.Truthy(l) && PageValues.Truthy(Eval(bin.Right, scope, ctx));
                }
                case "or":
                {
                    var l = Eval(bin.Left, scope, ctx);
                    return PageValues.Truthy(l) || PageValues.Truthy(Eval(bin.Right, scope, ctx));
                }
                case "??":
                {
                    // A missing variable on the left counts as null, so `$maybe ?? 'default'` works.
                    object l;
                    try { l = Eval(bin.Left, scope, ctx); }
                    catch (PageException) when (bin.Left is VarNode || bin.Left is MemberNode) { l = null; }
                    return l ?? Eval(bin.Right, scope, ctx);
                }
            }

            var a = Eval(bin.Left, scope, ctx);
            var b = Eval(bin.Right, scope, ctx);
            switch (bin.Op)
            {
                case "==": return PageValues.LooseEquals(a, b);
                case "!=": return !PageValues.LooseEquals(a, b);
                case "===": return PageValues.StrictEquals(a, b);
                case "!==": return !PageValues.StrictEquals(a, b);
                case "<": return PageValues.Compare(a, b, "lt") < 0;
                case "<=": return PageValues.Compare(a, b, "le") <= 0;
                case ">": return PageValues.Compare(a, b, "gt") > 0;
                case ">=": return PageValues.Compare(a, b, "ge") >= 0;
                case "in":
                    return b switch
                    {
                        List<object> list => list.Any(x => PageValues.LooseEquals(x, a)),
                        Dictionary<string, object> map => map.ContainsKey(PageValues.ToStr(a)),
                        string s => s.Contains(PageValues.ToStr(a), StringComparison.Ordinal),
                        null => false,
                        _ => throw new PageException($"'in' needs a list, map or string on the right, but got {PageValues.Describe(b)} in '{bin.Source}'."),
                    };
                case "+":
                    if (a is double x && b is double y) return x + y;
                    if (a is List<object> la && b is List<object> lb) return la.Concat(lb).ToList();
                    if (a is Dictionary<string, object> ma && b is Dictionary<string, object> mb)
                    {
                        var merged = new Dictionary<string, object>(ma);
                        foreach (var kv in mb) merged[kv.Key] = kv.Value;
                        return merged;
                    }
                    if (a is string || b is string) return PageValues.ToStr(a) + PageValues.ToStr(b);
                    return PageValues.ToNumber(a, $"'+' in '{bin.Source}'") + PageValues.ToNumber(b, $"'+' in '{bin.Source}'");
                case "-": return Num(a, bin) - Num(b, bin);
                case "*": return Num(a, bin) * Num(b, bin);
                case "/":
                {
                    double d = Num(b, bin);
                    if (d == 0) throw new PageException($"Division by zero in '{bin.Source}'.");
                    return Num(a, bin) / d;
                }
                case "%":
                {
                    double d = Num(b, bin);
                    if (d == 0) throw new PageException($"Division by zero in '{bin.Source}'.");
                    return Num(a, bin) % d;
                }
            }
            throw new PageException($"Unknown operator '{bin.Op}'.");
        }

        private static double Num(object v, BinaryNode bin) => PageValues.ToNumber(v, $"'{bin.Op}' in '{bin.Source}'");

        internal static object GetMember(object target, string name, string source)
        {
            switch (target)
            {
                case Dictionary<string, object> map:
                    return map.TryGetValue(name, out var v) ? v : null;
                case List<object> list when int.TryParse(name, out int i):
                    return i >= 0 && i < list.Count ? list[i] : null;
                case List<object> list when name == "count" || name == "length":
                    return (double)list.Count;
                case string s when name == "length":
                    return (double)s.Length;
            }
            throw new PageException($"{PageValues.Describe(target)} has no property '{name}' (in '{source}').");
        }

        internal static object GetIndex(object target, object key, string source)
        {
            switch (target)
            {
                case null:
                    throw new PageException($"Can't index into null in '{source}'.");
                case List<object> list:
                {
                    int i = (int)PageValues.ToNumber(key, $"A list index in '{source}'");
                    if (i < 0) i += list.Count;
                    return i >= 0 && i < list.Count ? list[i] : null;
                }
                case Dictionary<string, object> map:
                    return map.TryGetValue(PageValues.ToStr(key), out var v) ? v : null;
                case string s:
                {
                    int i = (int)PageValues.ToNumber(key, $"A string index in '{source}'");
                    if (i < 0) i += s.Length;
                    return i >= 0 && i < s.Length ? s[i].ToString() : null;
                }
            }
            throw new PageException($"Can't index into {PageValues.Describe(target)} in '{source}'.");
        }

        public static object Call(PageLambda fn, IList<object> args, PageContext ctx)
        {
            if (++ctx.CallDepth > 200)
                throw new PageException("Too much recursion (more than 200 nested lambda calls).");
            try
            {
                var scope = fn.Closure.Child();
                for (int i = 0; i < fn.Parameters.Count; i++)
                    scope.Declare(fn.Parameters[i], i < args.Count ? args[i] : null);
                return Eval(fn.Body, scope, ctx);
            }
            finally
            {
                ctx.CallDepth--;
            }
        }

        private static object CallCommand(string name, List<object> args, PageScope scope, PageContext ctx, string source)
        {
            // A lambda stored in a variable can be called like a command: <let name="double" value="fn($x) => $x * 2"/> then @double(4)
            if (scope.TryGet(name, out var stored) && stored is PageLambda fn)
                return Call(fn, args, ctx);
            return PageCommands.Invoke(name, args, ctx, source);
        }
    }
}
