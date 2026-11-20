using System.Globalization;
using System.Text.RegularExpressions;

using KScript.Metadata;
using KScript.Runtime;

using Markdig;

namespace KScript.Markdown
{
    /// <summary>Markdown rendering shared by KScript.Markdown, KScript.Terminal and KScript.Web.</summary>
    public static class MarkdownText
    {
        public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        public static string ToHtml(string markdown) => Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline);

        /// <summary>
        /// Splits a leading <c>---</c> front matter block off a document. Lines are <c>key: value</c>; values that
        /// look like numbers or true/false become those types, and quotes around strings are removed.
        /// </summary>
        public static (Dictionary<string, object> Meta, string Body) SplitFrontMatter(string text)
        {
            var meta = new Dictionary<string, object>();
            text ??= string.Empty;
            string normalised = text.Replace("\r\n", "\n");
            if (!normalised.StartsWith("---\n", StringComparison.Ordinal)) return (meta, text);
            int end = normalised.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end < 0) return (meta, text);

            string block = normalised.Substring(4, end - 4);
            int bodyStart = normalised.IndexOf('\n', end + 1);
            string body = bodyStart < 0 ? string.Empty : normalised.Substring(bodyStart + 1);
            foreach (string raw in block.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) throw new KScriptException($"Front matter lines must look like 'key: value', but got: {line}", "Format");
                meta[line.Substring(0, colon).Trim()] = ParseScalar(line.Substring(colon + 1).Trim());
            }
            return (meta, body);
        }

        private static object ParseScalar(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')) return value.Substring(1, value.Length - 2);
            if (value == "true") return true;
            if (value == "false") return false;
            if (value == "null" || value == "~" || value.Length == 0) return null;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                return value.Substring(1, value.Length - 2).Split(',').Select(v => ParseScalar(v.Trim())).Where(v => v != null).ToList();
            return value;
        }

        /// <summary>A Markdown document as a map: <c>html</c>, <c>meta</c> (front matter), <c>title</c> and <c>text</c>.</summary>
        public static Dictionary<string, object> Document(string text)
        {
            var (meta, body) = SplitFrontMatter(text);
            string title = meta.TryGetValue("title", out object t) && t != null ? Values.ToStr(t) : FirstHeading(body);
            return new Dictionary<string, object> { ["html"] = ToHtml(body), ["meta"] = meta, ["title"] = title, ["text"] = body };
        }

        private static string FirstHeading(string body)
        {
            Match m = Regex.Match(body ?? string.Empty, @"^#{1,6}\s+(.+?)\s*#*\s*$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Escapes a value placed in Markdown so it is shown literally, not read as Markdown or HTML.</summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if ("\\`*_[]#|~".IndexOf(c) >= 0) sb.Append('\\').Append(c);
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '&') sb.Append("&amp;");
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>KScript.Markdown: <c>@markdown(text)</c>, <c>@read_markdown(path)</c> and <c>->markdown()</c>.</summary>
    public sealed class MarkdownPackage : KScriptPackage
    {
        public override string Name => "KScript.Markdown";

        public override string Description => "Markdown to HTML, with front matter.";

        protected override void Register(PackageBuilder p)
        {
            p.Command("markdown", "text", "Renders Markdown: $doc.html, $doc.title, $doc.meta (front matter) and $doc.text.", KType.Map,
                c => MarkdownText.Document(c.Str(0)), "<let name=\"post\" value=\"@read_text('hello.md') |> @markdown()\" />");
            p.Command("read_markdown", "path", "Reads a Markdown file: $doc.html, $doc.title, $doc.meta (front matter) and $doc.text. Relative paths are from the script's folder.", KType.Map,
                c => MarkdownText.Document(File.ReadAllText(c.Host.ResolvePath(c.Str(0), c.Module?.Directory ?? c.Host.WorkingDirectory))),
                "<let name=\"post\" value=\"@read_markdown('posts/hello.md')\" />");
            p.Method(KType.String, "markdown", "", "The text rendered from Markdown to HTML.", KType.String, (t, c) => MarkdownText.ToHtml((string)t));
            p.Method(KType.String, "markdown_escape", "", "The text escaped so Markdown shows it as written.", KType.String, (t, c) => MarkdownText.Escape((string)t));
        }
    }
}
