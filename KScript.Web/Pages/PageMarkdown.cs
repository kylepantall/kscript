using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Markdig;

namespace KScript.Pages
{
    /// <summary>Markdown support for pages: rendering, front matter and Markdown documents as values.</summary>
    public static class PageMarkdown
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
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

        /// <summary>A Markdown document as a map: <c>html</c>, <c>meta</c> (front matter), <c>title</c> and <c>text</c>.</summary>
        public static Dictionary<string, object> Document(string text)
        {
            var (meta, body, _) = SplitFrontMatter(text);
            string title = meta.TryGetValue("title", out var t) && t != null ? PageValues.ToStr(t) : FirstHeading(body);
            return new Dictionary<string, object>
            {
                ["html"] = ToHtml(body),
                ["meta"] = meta,
                ["title"] = title,
                ["text"] = body,
            };
        }

        private static string FirstHeading(string body)
        {
            var m = Regex.Match(body ?? string.Empty, @"^#{1,6}\s+(.+?)\s*#*\s*$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : null;
        }

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
