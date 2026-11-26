using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using KScript.Runtime;

using Markdig;

namespace KScript.Pages
{
    /// <summary>Markdown support for pages: rendering, front matter and Markdown documents as values.</summary>
    public static class PageMarkdown
    {
        // The advanced extensions without generic attributes: `{onclick=...}` after a block would let a value
        // filled into the page add attributes to the element around it. Pages write their own HTML for that.
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAbbreviations()
            .UseAutoIdentifiers()
            .UseCitations()
            .UseCustomContainers()
            .UseDefinitionLists()
            .UseEmphasisExtras()
            .UseFigures()
            .UseFooters()
            .UseFootnotes()
            .UseGridTables()
            .UseMathematics()
            .UseMediaLinks()
            .UsePipeTables()
            .UseListExtras()
            .UseTaskLists()
            .UseDiagrams()
            .UseAutoLinks()
            .Build();

        public static string ToHtml(string markdown) => Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline);

        /// <summary>
        /// Splits a leading <c>---</c> front matter block off a document. Lines are <c>key: value</c>; values
        /// that look like numbers or true/false become those types, and quotes around strings are removed.
        /// </summary>
        public static (Dictionary<string, object> Meta, string Body, int BodyLine) SplitFrontMatter(string text)
        {
            var meta = new Dictionary<string, object>();
            text ??= string.Empty;
            string normalised = text.Replace("\r\n", "\n");
            if (!normalised.StartsWith("---\n", StringComparison.Ordinal)) return (meta, text, 1);

            int end = normalised.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end < 0) return (meta, text, 1);

            string block = normalised.Substring(4, end - 4);
            int bodyStart = normalised.IndexOf('\n', end + 1);
            string body = bodyStart < 0 ? string.Empty : normalised.Substring(bodyStart + 1);
            int bodyLine = normalised.Substring(0, bodyStart < 0 ? normalised.Length : bodyStart + 1).Count(c => c == '\n') + 1;

            foreach (string raw in block.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) throw new PageException($"Front matter lines must look like 'key: value', but got: {line}");
                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                meta[key] = ParseScalar(value);
            }
            return (meta, body, bodyLine);
        }

        private static object ParseScalar(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
                return value.Substring(1, value.Length - 2);
            if (value == "true") return true;
            if (value == "false") return false;
            if (value == "null" || value == "~" || value.Length == 0) return null;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                return value.Substring(1, value.Length - 2).Split(',').Select(v => ParseScalar(v.Trim())).Where(v => v != null).ToList();
            return value;
        }

        /// <summary>
        /// Escapes a value inserted into Markdown, so it shows as written instead of becoming formatting, structure or
        /// HTML. Every character Markdown (or one of its extensions) gives a meaning becomes a character reference,
        /// which Markdown reads as plain text and which also reads correctly inside an HTML block. Line breaks and a
        /// leading space are written the same way, so a value can't start a new block (a list, a heading, code).
        /// </summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '&') sb.Append("&amp;");
                else if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n') continue;
                else if (c is '\n' or '\r' or '\t' || (c == ' ' && i == 0) || EscapedPunctuation.IndexOf(c) >= 0)
                    sb.Append("&#").Append(((int)c).ToString(CultureInfo.InvariantCulture)).Append(';');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private const string EscapedPunctuation = "\\`*_[]#|~{}=^:!+-.()\"$@";

        /// <summary>Removes the common leading indentation, so Markdown nested inside indented HTML still works.</summary>
        public static string Dedent(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int indent = lines.Where(l => l.Trim().Length > 0)
                .Select(l => l.Length - l.TrimStart(' ', '\t').Length)
                .DefaultIfEmpty(0).Min();
            return string.Join("\n", lines.Select(l => l.Length >= indent ? l.Substring(indent) : l.TrimStart())).Trim('\n');
        }
    }
}
