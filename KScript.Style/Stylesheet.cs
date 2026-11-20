using System.Runtime.CompilerServices;
using System.Text;

using KScript.Metadata;

using KScript.Syntax.Markup;
using KScript.Runtime;

namespace KScript.Style
{
    /// <summary>
    /// Stylesheets written in KScript: <c>theme.ks.css</c> becomes <c>theme.css</c>, <c>theme.ks.scss</c> becomes
    /// <c>theme.scss</c> and <c>theme.ks.sass</c> becomes <c>theme.sass</c>. The file is CSS (or Sass) with KScript
    /// elements such as <c>&lt;let&gt;</c>, <c>&lt;for&gt;</c> and <c>&lt;if&gt;</c> between the rules, and
    /// <c>{$name}</c> or <c>{ @fn(...) }</c> in the text. A <c>{</c> that doesn't start with $ or @name(, or that follows # (Sass's own #{...}), is CSS.
    /// </summary>
    public static class Stylesheets
    {
        public static readonly string[] Extensions = { ".ks.css", ".ks.scss", ".ks.sass" };

        /// <summary>The packages a stylesheet has without importing them.</summary>
        public static readonly string[] DefaultPackages = { "KScript.Style", "KScript.IO" };

        public static bool IsStylesheet(string path) => path != null && Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

        /// <summary>Where a stylesheet's output goes: theme.ks.css is theme.css, next to it.</summary>
        public static string OutputPath(string path)
        {
            string ext = Extensions.FirstOrDefault(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                ?? throw new KScriptException($"'{Path.GetFileName(path)}' isn't a stylesheet: its name should end in .ks.css, .ks.scss or .ks.sass.", "Argument");
            return path.Substring(0, path.Length - ext.Length) + ext.Substring(3);
        }

        /// <summary>Compiles a stylesheet file. <paramref name="setup"/> can sandbox the host, as kscript serve does.</summary>
        public static string Compile(string path, Dictionary<string, object> vars = null, Action<KScriptHost> setup = null)
        {
            string full = Path.GetFullPath(path);
            return CompileText(File.ReadAllText(full), full, vars, setup);
        }

        /// <summary>Compiles stylesheet text. <paramref name="path"/> names it in errors and is where relative paths start.</summary>
        public static string CompileText(string source, string path, Dictionary<string, object> vars = null, Action<KScriptHost> setup = null)
        {
            var css = new StringWriter(System.Globalization.CultureInfo.InvariantCulture) { NewLine = "\n" };
            var host = new KScriptHost(TextWriter.Null, Console.Error);
            if (path != null) host.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            setup?.Invoke(host);
            host.Items[StylePackage.VarsKey] = vars ?? new Dictionary<string, object>();
            var options = new InterpreterOptions { Timeout = TimeSpan.FromSeconds(30) };
            options.DefaultPackages.AddRange(DefaultPackages);
            using (host.CaptureOutput(css))
            {
                new Interpreter(host, options: options).RunText(ToScript(source), path);
            }
            return css.ToString();
        }

        /// <summary>
        /// Turns stylesheet text into a KScript script: KScript elements stay as they are, and the CSS between them
        /// goes into &lt;css-text&gt; elements. A KScript element alone on its line leaves no blank line behind.
        /// Line numbers stay the same, so errors point at the stylesheet's own lines.
        /// </summary>
        public static string ToScript(string source)
        {
            source = (source ?? string.Empty).Replace("\r\n", "\n");
            var sb = new StringBuilder("<kscript>");
            var text = new StringBuilder();
            // The open KScript elements: CSS is only wrapped where statements go, not inside <echo> or <let>.
            var open = new Stack<bool>();
            bool Wrap() => open.Count == 0 || open.Peek();
            int i = 0;
            while (i < source.Length)
            {
                int tagEnd = source[i] == '<' ? TagEnd(source, i) : -1;
                if (tagEnd < 0)
                {
                    text.Append(source[i++]);
                    continue;
                }

                // A tag alone on its line: its indentation and line break are left out of the CSS.
                int lineStart = LastLineBreak(text) + 1;
                bool aloneBefore = text.ToString(lineStart, text.Length - lineStart).All(c => c == ' ' || c == '\t');
                int after = tagEnd;
                while (after < source.Length && (source[after] == ' ' || source[after] == '\t')) after++;
                bool aloneAfter = after >= source.Length || source[after] == '\n';
                string indent = string.Empty;
                if (aloneBefore && aloneAfter)
                {
                    indent = text.ToString(lineStart, text.Length - lineStart);
                    text.Length = lineStart;
                }
                Flush(sb, text, Wrap());
                string tag = source.Substring(i, tagEnd - i);
                Track(tag, open);
                sb.Append(indent).Append(tag);
                if (aloneBefore && aloneAfter)
                {
                    int end = after < source.Length ? after + 1 : after;
                    sb.Append(source, tagEnd, end - tagEnd);
                    i = end;
                }
                else
                {
                    i = tagEnd;
                }
            }
            Flush(sb, text, Wrap());
            return sb.Append("</kscript>").ToString();
        }

        private static int LastLineBreak(StringBuilder sb)
        {
            for (int i = sb.Length - 1; i >= 0; i--)
            {
                if (sb[i] == '\n') return i;
            }
            return -1;
        }

        private static void Flush(StringBuilder sb, StringBuilder text, bool wrap)
        {
            if (text.Length == 0) return;
            if (wrap) sb.Append("<css-text>");
            foreach (char c in text.ToString())
            {
                sb.Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", _ => c.ToString() });
            }
            if (wrap) sb.Append("</css-text>");
            text.Clear();
        }

        private static readonly Lazy<Dictionary<string, ContentKind>> Contents = new Lazy<Dictionary<string, ContentKind>>(() =>
            DefaultPackages.Prepend("KScript.Core").Select(PackageRegistry.Default.Get).Where(p => p != null)
                .SelectMany(p => p.Elements.Values).GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First().Info.Content));

        /// <summary>Follows which elements are open. An element holds CSS when it holds statements (&lt;for&gt;, &lt;if&gt;...).</summary>
        private static void Track(string tag, Stack<bool> open)
        {
            if (tag.StartsWith("<!--", StringComparison.Ordinal) || tag.EndsWith("/>", StringComparison.Ordinal)) return;
            if (tag.StartsWith("</", StringComparison.Ordinal))
            {
                if (open.Count > 0) open.Pop();
                return;
            }
            string name = new string(tag.Skip(1).TakeWhile(c => char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_').ToArray());
            open.Push(!Contents.Value.TryGetValue(name, out ContentKind kind) || kind == ContentKind.Statements);
        }

        /// <summary>
        /// The end of a KScript tag or comment starting at <paramref name="start"/>: &lt;name ...&gt;, &lt;/name&gt;
        /// or &lt;!-- ... --&gt;. CSS never uses &lt; otherwise. -1 when it isn't one.
        /// </summary>
        private static int TagEnd(string s, int start)
        {
            if (string.CompareOrdinal(s, start, "<!--", 0, 4) == 0)
            {
                int close = s.IndexOf("-->", start + 4, StringComparison.Ordinal);
                return close < 0 ? -1 : close + 3;
            }
            int i = start + 1;
            if (i < s.Length && s[i] == '/') i++;
            if (i >= s.Length || !char.IsLetter(s[i])) return -1;
            char quote = '\0';
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c == '"' || c == '\'')
                {
                    quote = c;
                }
                else if (c == '>')
                {
                    return i + 1;
                }
            }
            return -1;
        }

        private static readonly ConditionalWeakTable<DecodedText, Dictionary<int, DecodedText>> Pieces = new ConditionalWeakTable<DecodedText, Dictionary<int, DecodedText>>();

        /// <summary>CSS text with <c>{$name}</c> and <c>{ @fn() }</c> replaced. Other braces are left alone.</summary>
        internal static string Interpolate(DecodedText text, Interpreter interpreter, Scope scope, Module module)
        {
            string s = text.Value;
            if (!s.Contains('{')) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '{' || (i > 0 && s[i - 1] == '#') || !StartsExpression(s, i + 1, out int exprStart))
                {
                    sb.Append(s[i]);
                    continue;
                }
                int close = Closing(s, exprStart);
                if (close < 0) throw new KScriptException($"The {{ at line {LineOf(text, module, i)} isn't closed.", "Syntax") { };
                DecodedText expr = Piece(text, exprStart, close);
                string value = Format(interpreter.EvaluateText(expr, scope, module));
                // A value over several lines keeps the indentation of the line it was put on.
                if (value.Contains('\n')) value = value.Replace("\n", "\n" + Indentation(sb));
                sb.Append(value);
                i = close;
            }
            return sb.ToString();
        }

        private static string Indentation(StringBuilder sb)
        {
            int start = LastLineBreak(sb) + 1;
            int end = start;
            while (end < sb.Length && (sb[end] == ' ' || sb[end] == '\t')) end++;
            return sb.ToString(start, end - start);
        }

        private static int LineOf(DecodedText text, Module module, int index) => module.Source.GetPosition(text.ToSource(index)).Line + 1;

        /// <summary>True when the text after a { is a KScript expression: $name, @name(, or ! (raw) before one of those.</summary>
        private static bool StartsExpression(string s, int i, out int start)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            if (i < s.Length && s[i] == '!') i++;
            start = i;
            if (i >= s.Length) return false;
            if (s[i] == '$') return i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '_');
            if (s[i] != '@') return false;
            int j = i + 1;
            while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_' || s[j] == '.')) j++;
            return j > i + 1 && j < s.Length && s[j] == '(';
        }

        /// <summary>The } that ends an expression, skipping brackets and strings inside it.</summary>
        private static int Closing(string s, int i)
        {
            int depth = 0;
            char quote = '\0';
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                switch (c)
                {
                    case '\'': case '"': quote = c; break;
                    case '(': case '[': case '{': depth++; break;
                    case ')': case ']': depth--; break;
                    case '}':
                        if (depth == 0) return i;
                        depth--;
                        break;
                }
            }
            return -1;
        }

        private static DecodedText Piece(DecodedText text, int start, int end)
        {
            Dictionary<int, DecodedText> pieces = Pieces.GetOrCreateValue(text);
            lock (pieces)
            {
                if (!pieces.TryGetValue(start, out DecodedText piece))
                {
                    int[] map = Enumerable.Range(start, end - start + 1).Select(text.ToSource).ToArray();
                    piece = new DecodedText(text.Value.Substring(start, end - start), map);
                    pieces[start] = piece;
                }
                return piece;
            }
        }

        /// <summary>A value as CSS: lists are joined with spaces, null is nothing.</summary>
        internal static string Format(object value) => value switch
        {
            null => string.Empty,
            List<object> list => string.Join(" ", list.Select(Format)),
            _ => Values.ToStr(value),
        };
    }
}
