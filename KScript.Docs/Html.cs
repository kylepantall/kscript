using System.Net;
using System.Text;
using System.Text.RegularExpressions;

using Markdig;

namespace KScript.Docs
{
    /// <summary>Small HTML helpers for the docs pages: escaping, Markdown text, code blocks and "try it" boxes.</summary>
    internal static class Html
    {
        /// <summary>HTML-escapes text, leaving other characters (… · →) as they are.</summary>
        public static string H(string text) =>
            (text ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

        /// <summary>Markdown with GitHub's heading ids, so links written for GitHub (#settings-kscriptjson) work here too.</summary>
        public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
            .UseAdvancedExtensions()
            .Build();

        /// <summary>Markdown for one paragraph or less (descriptions in the catalog), without the &lt;p&gt; around it.</summary>
        public static string Inline(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
            // Descriptions name elements as <elif>, which Markdown would pass through as an HTML tag: outside `code`,
            // angle brackets are text.
            string[] parts = markdown.Trim().Split('`');
            for (int i = 0; i < parts.Length; i += 2) parts[i] = parts[i].Replace("<", "&lt;").Replace(">", "&gt;");
            string html = Markdig.Markdown.ToHtml(string.Join("`", parts), Pipeline).Trim();
            if (html.StartsWith("<p>", StringComparison.Ordinal) && html.EndsWith("</p>", StringComparison.Ordinal) && html.IndexOf("<p>", 3, StringComparison.Ordinal) < 0)
                html = html.Substring(3, html.Length - 7);
            return html;
        }

        /// <summary>The words of some HTML, for search.</summary>
        public static string Text(string html)
        {
            string text = Regex.Replace(html ?? string.Empty, "<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
        }

        /// <summary>
        /// The same thing as XML and as .kscript (<c>&lt;break&gt;</c> and <c>break</c>): the page shows the one in the
        /// reader's format, and the other when they switch.
        /// </summary>
        public static string Dual(string xml, string kscript) =>
            xml == kscript ? xml : $"<span class=\"f-xml\">{xml}</span><span class=\"f-ks\">{kscript}</span>";

        /// <summary>A heading id: "Early returns instead of else" becomes early-returns-instead-of-else.</summary>
        public static string Slug(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in (text ?? string.Empty).ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if ((c == ' ' || c == '-' || c == '_' || c == '.') && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        private static readonly Dictionary<string, string> LanguageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["xml"] = "XML", ["kscript"] = ".kscript", ["html"] = "HTML page", ["sh"] = "Shell", ["powershell"] = "PowerShell",
            ["json"] = "JSON", ["jsonc"] = "JSON", ["yaml"] = "YAML", ["css"] = "CSS", ["text"] = "Text",
        };

        /// <summary>
        /// Command line examples as a table: each command, highlighted as shell, beside what it does.
        /// </summary>
        public static string Commands(IEnumerable<(string Command, string Does)> examples)
        {
            var sb = new StringBuilder("<div class=\"table commands\"><table><thead><tr><th>Command</th><th>What it does</th></tr></thead><tbody>");
            foreach (var (command, does) in examples)
                sb.Append("<tr><td><code class=\"sh\">").Append(H(command)).Append("</code></td><td>").Append(Inline(does)).Append("</td></tr>");
            return sb.Append("</tbody></table></div>").ToString();
        }

        private static readonly Regex CommentedCommand = new Regex(@"^((?:kscript|claude)\s\S.*?)\s+#\s+(\S.*)$", RegexOptions.Compiled);

        /// <summary>
        /// A shell block in a guide whose every line is a kscript command with a # comment, as a <see cref="Commands"/>
        /// table (the comment says what it does), or null for any other block.
        /// </summary>
        public static string CommandTable(string code)
        {
            var rows = new List<(string, string)>();
            foreach (string line in code.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                Match m = CommentedCommand.Match(line);
                if (!m.Success) return null;
                string does = m.Groups[2].Value;
                rows.Add((m.Groups[1].Value, char.ToUpperInvariant(does[0]) + does.Substring(1) + (does.EndsWith('.') || does.EndsWith(')') ? "" : ".")));
            }
            return rows.Count > 0 ? Commands(rows) : null;
        }

        /// <summary>
        /// A read-only code block with a copy button. An XML or .kscript script is given in both formats when it converts,
        /// and the page shows the reader's format (<see cref="DocsSite.Format"/> until they pick the other).
        /// </summary>
        public static string Code(string code, string language)
        {
            string lang = (language ?? string.Empty).ToLowerInvariant();
            if (lang == "xml" || lang == "kscript")
            {
                string other = lang == "xml" ? "kscript" : "xml";
                string converted = Playground.Convert(code.Replace("\r\n", "\n").TrimEnd('\n') + "\n", other);
                if (converted != null)
                {
                    string xml = lang == "xml" ? code : converted;
                    string kscript = lang == "xml" ? converted : code;
                    return "<div class=\"code-pair\">" + Single(xml, "xml", paired: true) + Single(kscript, "kscript", paired: true) + "</div>";
                }
            }
            return Single(code, language);
        }

        /// <summary>One code block, in one language.</summary>
        internal static string Single(string code, string language, bool paired = false)
        {
            language = string.IsNullOrEmpty(language) ? "text" : language.ToLowerInvariant();
            string label = LanguageNames.TryGetValue(language, out string name) ? name : language;
            bool script = language == "xml" || language == "kscript";
            var sb = new StringBuilder();
            sb.Append("<div class=\"code\" data-lang=\"").Append(H(language)).Append("\">");
            sb.Append("<div class=\"code-bar\">");
            if (paired)
            {
                // Both blocks of a pair carry the same switch; the page shows one of them.
                sb.Append("<span class=\"fmt-tabs\" role=\"group\" aria-label=\"Format\">");
                foreach (string f in new[] { "kscript", "xml" })
                    sb.Append($"<button type=\"button\" data-format=\"{f}\" aria-pressed=\"{(f == language ? "true" : "false")}\">{(f == "xml" ? "XML" : ".kscript")}</button>");
                sb.Append("</span>");
            }
            else sb.Append("<span class=\"code-lang\">").Append(H(label)).Append("</span>");
            sb.Append("<span class=\"code-actions\">");
            if (script) sb.Append("<button type=\"button\" class=\"link open-playground\" title=\"Edit and run this in the playground\">Open in playground</button>");
            sb.Append("<button type=\"button\" class=\"link copy\" title=\"Copy to the clipboard\">Copy</button></span></div>");
            sb.Append("<pre><code class=\"hl lang-").Append(H(language)).Append("\">").Append(H(code.TrimEnd('\n', '\r'))).Append("</code></pre></div>");
            return sb.ToString();
        }

        /// <summary>
        /// An example the reader can edit and run. The page shows it as XML or .kscript (both are given when the code
        /// converts); without JavaScript it is a plain code block.
        /// </summary>
        public static string Try(string code, string format = "xml", string title = null, string input = null)
        {
            format = format == "kscript" ? "kscript" : "xml";
            code = code.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
            string xml = format == "xml" ? code : Playground.Convert(code, "xml");
            string kscript = format == "kscript" ? code : Playground.Convert(code, "kscript");
            var sb = new StringBuilder();
            sb.Append("<div class=\"try\" data-format=\"").Append(format).Append('"');
            if (xml != null) sb.Append(" data-xml=\"").Append(H(xml)).Append('"');
            if (kscript != null) sb.Append(" data-kscript=\"").Append(H(kscript)).Append('"');
            if (title != null) sb.Append(" data-title=\"").Append(H(title)).Append('"');
            if (input != null) sb.Append(" data-input=\"").Append(H(input)).Append('"');
            sb.Append('>');
            sb.Append(Single(code, format));
            sb.Append("</div>");
            return sb.ToString();
        }
    }
}
