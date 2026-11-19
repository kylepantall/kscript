using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KScript.Scaffolding
{
    /// <summary>
    /// The small template language used by <c>kscript make</c> templates.
    /// <list type="bullet">
    /// <item><c>{{ name }}</c> inserts a value, <c>{{ name | kebab }}</c> transforms it, <c>{{ name ?? text }}</c> gives a fallback.</item>
    /// <item><c>{{#if name}} … {{else}} … {{/if}}</c> keeps a section when the value is set (not empty and not "false").</item>
    /// <item><c>{{#each name}} … {{ item }} … {{/each}}</c> repeats a section for each comma-separated value.</item>
    /// </list>
    /// Single braces are left alone, so KScript's own <c>{$value}</c> interpolation can be written as is.
    /// A block tag on a line of its own removes that whole line from the output.
    /// </summary>
    public static class StubTemplate
    {
        public static readonly IReadOnlyDictionary<string, Func<string, string>> Filters = new Dictionary<string, Func<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["kebab"] = StubNames.Kebab,
            ["snake"] = StubNames.Snake,
            ["pascal"] = StubNames.Pascal,
            ["camel"] = StubNames.Camel,
            ["title"] = StubNames.Title,
            ["var"] = StubNames.Var,
            ["lower"] = s => s.ToLowerInvariant(),
            ["upper"] = s => s.ToUpperInvariant(),
            ["trim"] = s => s.Trim(),
        };

        public static string Render(string template, IReadOnlyDictionary<string, string> values)
        {
            var tokens = Tokenize(template ?? string.Empty);
            int pos = 0;
            var nodes = ParseNodes(tokens, ref pos, null);
            var sb = new StringBuilder();
            RenderNodes(nodes, new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase), sb);
            return sb.ToString();
        }

        // ---- tokens ----------------------------------------------------------------------------------------------

        private enum TokenKind { Text, Value, Open, Else, Close }

        private sealed record Token(TokenKind Kind, string Text, string Block = null, int Line = 0);

        private static List<Token> Tokenize(string src)
        {
            var tokens = new List<Token>();
            int i = 0;
            var text = new StringBuilder();

            while (i < src.Length)
            {
                int start = src.IndexOf("{{", i, StringComparison.Ordinal);
                if (start < 0) { text.Append(src, i, src.Length - i); break; }
                int end = src.IndexOf("}}", start + 2, StringComparison.Ordinal);
                if (end < 0) { text.Append(src, i, src.Length - i); break; }

                string inner = src.Substring(start + 2, end - start - 2).Trim();
                int tagEnd = end + 2;
                int line = LineOf(src, start);
                Token token;

                if (inner.StartsWith('#'))
                {
                    string rest = inner.Substring(1).Trim();
                    int space = rest.IndexOf(' ');
                    string block = space < 0 ? rest : rest.Substring(0, space);
                    string arg = space < 0 ? "" : rest.Substring(space + 1).Trim();
                    if (block != "if" && block != "unless" && block != "each")
                        throw new StubException($"Unknown block {{{{#{block}}}}} on line {line}. Use #if, #unless or #each.");
                    if (arg.Length == 0)
                        throw new StubException($"{{{{#{block}}}}} on line {line} needs a value name, e.g. {{{{#{block} props}}}}.");
                    token = new Token(TokenKind.Open, arg, block, line);
                }
                else if (inner.StartsWith('/'))
                {
                    token = new Token(TokenKind.Close, "", inner.Substring(1).Trim(), line);
                }
                else if (inner == "else")
                {
                    token = new Token(TokenKind.Else, "", null, line);
                }
                else
                {
                    text.Append(src, i, start - i);
                    Flush();
                    tokens.Add(new Token(TokenKind.Value, inner, null, line));
                    i = tagEnd;
                    continue;
                }

                // A block tag alone on its line takes the whole line (indent and newline) with it.
                int lineStart = start;
                while (lineStart > 0 && (src[lineStart - 1] == ' ' || src[lineStart - 1] == '\t')) lineStart--;
                int lineEnd = tagEnd;
                while (lineEnd < src.Length && (src[lineEnd] == ' ' || src[lineEnd] == '\t' || src[lineEnd] == '\r')) lineEnd++;
                bool standalone = (lineStart == 0 || src[lineStart - 1] == '\n') && (lineEnd == src.Length || src[lineEnd] == '\n');
                standalone &= lineStart >= i;

                if (standalone)
                {
                    text.Append(src, i, lineStart - i);
                    i = lineEnd < src.Length ? lineEnd + 1 : lineEnd;
                }
                else
                {
                    text.Append(src, i, start - i);
                    i = tagEnd;
                }
                Flush();
                tokens.Add(token);
            }
            Flush();
            return tokens;

            void Flush()
            {
                if (text.Length == 0) return;
                tokens.Add(new Token(TokenKind.Text, text.ToString()));
                text.Clear();
            }
        }

        private static int LineOf(string src, int index)
        {
            int line = 1;
            for (int i = 0; i < index; i++) if (src[i] == '\n') line++;
            return line;
        }

        // ---- tree ------------------------------------------------------------------------------------------------

        private abstract record Node;
        private sealed record TextNode(string Text) : Node;
        private sealed record ValueNode(string Expr) : Node;
        private sealed record BlockNode(string Kind, string Name, List<Node> Body, List<Node> Else) : Node;

        private static List<Node> ParseNodes(List<Token> tokens, ref int pos, Token open)
        {
            var nodes = new List<Node>();
            while (pos < tokens.Count)
            {
                var t = tokens[pos++];
                switch (t.Kind)
                {
                    case TokenKind.Text: nodes.Add(new TextNode(t.Text)); break;
                    case TokenKind.Value: nodes.Add(new ValueNode(t.Text)); break;
                    case TokenKind.Open:
                        {
                            var body = ParseNodes(tokens, ref pos, t);
                            List<Node> elseBody = null;
                            if (pos > 0 && tokens[pos - 1].Kind == TokenKind.Else)
                            {
                                elseBody = ParseNodes(tokens, ref pos, t);
                                if (tokens[pos - 1].Kind == TokenKind.Else)
                                    throw new StubException($"{{{{#{t.Block} {t.Text}}}}} on line {t.Line} has more than one {{{{else}}}}.");
                            }
                            nodes.Add(new BlockNode(t.Block, t.Text, body, elseBody ?? new List<Node>()));
                            break;
                        }
                    case TokenKind.Else:
                        if (open == null) throw new StubException($"{{{{else}}}} on line {t.Line} is not inside {{{{#if}}}} or {{{{#each}}}}.");
                        return nodes;
                    case TokenKind.Close:
                        if (open == null) throw new StubException($"{{{{/{t.Block}}}}} on line {t.Line} has no matching {{{{#{t.Block} …}}}}.");
                        if (!string.IsNullOrEmpty(t.Block) && t.Block != open.Block)
                            throw new StubException($"{{{{/{t.Block}}}}} on line {t.Line} closes {{{{#{open.Block}}}}} from line {open.Line}; use {{{{/{open.Block}}}}}.");
                        return nodes;
                }
            }
            if (open != null) throw new StubException($"{{{{#{open.Block} {open.Text}}}}} on line {open.Line} is never closed with {{{{/{open.Block}}}}}.");
            return nodes;
        }

        // ---- rendering -------------------------------------------------------------------------------------------

        private static void RenderNodes(List<Node> nodes, Dictionary<string, string> values, StringBuilder sb)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case TextNode t: sb.Append(t.Text); break;
                    case ValueNode v: sb.Append(Evaluate(v.Expr, values)); break;
                    case BlockNode b when b.Kind == "each":
                        {
                            var items = List(Lookup(b.Name, values));
                            if (items.Count == 0) { RenderNodes(b.Else, values, sb); break; }
                            for (int i = 0; i < items.Count; i++)
                            {
                                var inner = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
                                {
                                    ["item"] = items[i],
                                    ["index"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    ["first"] = i == 0 ? "true" : "",
                                    ["last"] = i == items.Count - 1 ? "true" : "",
                                };
                                RenderNodes(b.Body, inner, sb);
                            }
                            break;
                        }
                    case BlockNode b:
                        {
                            bool truthy = IsSet(Lookup(b.Name, values));
                            if (b.Kind == "unless") truthy = !truthy;
                            RenderNodes(truthy ? b.Body : b.Else, values, sb);
                            break;
                        }
                }
            }
        }

        /// <summary>Evaluates <c>name | filter | filter ?? fallback</c>.</summary>
        private static string Evaluate(string expr, Dictionary<string, string> values)
        {
            string fallback = null;
            int q = expr.IndexOf("??", StringComparison.Ordinal);
            if (q >= 0)
            {
                fallback = expr.Substring(q + 2).Trim();
                expr = expr.Substring(0, q);
            }

            var parts = expr.Split('|').Select(p => p.Trim()).ToArray();
            string value = Lookup(parts[0], values);
            if (!IsSet(value)) return fallback ?? "";

            foreach (var filter in parts.Skip(1))
            {
                if (!Filters.TryGetValue(filter, out var fn))
                    throw new StubException($"Unknown filter '{filter}' in {{{{ {expr.Trim()} }}}}. Filters: {string.Join(", ", Filters.Keys)}.");
                value = fn(value);
            }
            return value;
        }

        private static string Lookup(string name, Dictionary<string, string> values) =>
            values.TryGetValue(name.Trim(), out var v) ? v : null;

        private static bool IsSet(string value) =>
            !string.IsNullOrEmpty(value) && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

        private static List<string> List(string value) =>
            !IsSet(value) || value == "true"
                ? new List<string>()
                : value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    public class StubException : Exception
    {
        public StubException(string message) : base(message) { }
    }
}
