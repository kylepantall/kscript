using System;
using System.Collections.Generic;
using System.Text;
using KScript.Metadata;

namespace KScript.Syntax.Text
{
    /// <summary>
    /// A fenced block of text in a .kscript file, written like a Markdown code block:
    /// <code>
    /// query = ```sql
    ///     SELECT * FROM users
    ///     ```
    /// </code>
    /// The block runs from the opening fence (three or more backticks) to a line that starts with as many backticks.
    /// That closing fence's indentation is removed from every line of the block, and the new lines just inside the
    /// fences aren't part of the value. Code can carry on after the closing fence (<c>```)</c>).
    /// </summary>
    public sealed class StringBlock
    {
        /// <summary>The first backtick of the opening fence.</summary>
        public int Start { get; private set; }

        /// <summary>Just after the last backtick of the closing fence (or the end of the text when it isn't closed).</summary>
        public int End { get; private set; }

        /// <summary>What follows the opening fence: empty, a language (<c>sql</c>) or a type (<c>&lt;Repo&gt;</c>).</summary>
        public string Tag { get; private set; } = string.Empty;

        public TextSpan TagSpan { get; private set; }

        /// <summary>The type in a <c>&lt;Type&gt;</c> tag, or null when the tag is a language or empty.</summary>
        public string TypeName =>
            Tag.Length > 2 && Tag[0] == '<' && Tag[Tag.Length - 1] == '>' ? Tag.Substring(1, Tag.Length - 2).Trim() : null;

        /// <summary>The language in a plain tag (<c>sql</c>, <c>html</c>), or null.</summary>
        public string Language => Tag.Length > 0 && Tag[0] != '<' ? Tag : null;

        /// <summary>The text of the block, with the closing fence's indentation removed.</summary>
        public string Value { get; private set; } = string.Empty;

        /// <summary>For each character of <see cref="Value"/> its offset in the source, then one more for the end.</summary>
        public int[] Map { get; private set; } = Array.Empty<int>();

        /// <summary>Where the value's text starts in the source (just after the opening line).</summary>
        public int ContentStart { get; private set; }

        /// <summary>Where the value's text ends in the source (the start of the closing fence's line).</summary>
        public int ContentEnd { get; private set; }

        /// <summary>A problem with the block (not closed, a line less indented than the closing fence), or null.</summary>
        public string Error { get; private set; }

        public TextSpan ErrorSpan { get; private set; }

        /// <summary>True when three or more backticks start at <paramref name="pos"/>.</summary>
        public static bool StartsAt(string text, int pos) =>
            pos + 2 < text.Length && text[pos] == '`' && text[pos + 1] == '`' && text[pos + 2] == '`';

        /// <summary>Reads the block that starts at <paramref name="pos"/>, or returns null when no fence starts there.</summary>
        public static StringBlock Read(string text, int pos, int limit = -1)
        {
            if (limit < 0 || limit > text.Length) limit = text.Length;
            if (!StartsAt(text, pos) || pos + 2 >= limit) return null;

            StringBlock block = new StringBlock { Start = pos };
            int ticks = 0;
            int p = pos;
            while (p < limit && text[p] == '`')
            {
                ticks++;
                p++;
            }

            // The tag: the rest of the opening line.
            int lineEnd = p;
            while (lineEnd < limit && text[lineEnd] != '\n') lineEnd++;
            int tagStart = p, tagEnd = lineEnd;
            while (tagStart < tagEnd && (text[tagStart] == ' ' || text[tagStart] == '\t')) tagStart++;
            while (tagEnd > tagStart && char.IsWhiteSpace(text[tagEnd - 1])) tagEnd--;
            block.Tag = text.Substring(tagStart, tagEnd - tagStart);
            block.TagSpan = TextSpan.FromBounds(tagStart, tagEnd);

            if (block.Tag.Contains('`'))
            {
                block.End = lineEnd;
                block.ContentStart = block.ContentEnd = lineEnd;
                block.Fail(new TextSpan(pos, lineEnd - pos), "A ``` block holds several lines: put its text on the lines after the opening fence, and the closing fence on a line of its own.");
                return block;
            }
            if (lineEnd >= limit)
            {
                block.End = limit;
                block.ContentStart = block.ContentEnd = limit;
                block.Fail(new TextSpan(pos, ticks), "Start the text of a ``` block on the line after the fence.");
                return block;
            }
            if (!ValidTag(block.Tag))
            {
                block.Fail(block.TagSpan, $"'{block.Tag}' isn't a language, file extension or <Type>. Write ```sql, ```<Repo> or nothing after the fence.");
            }
            // The lines of the block, up to the closing fence.
            int contentStart = lineEnd + 1;
            block.ContentStart = contentStart;
            List<(int Start, int End)> lines = new List<(int, int)>();
            int lineStart = contentStart;
            int closeLine = -1, indentEnd = -1;
            while (lineStart <= limit)
            {
                int end = lineStart;
                while (end < limit && text[end] != '\n') end++;
                int first = lineStart;
                while (first < end && (text[first] == ' ' || text[first] == '\t')) first++;
                int run = 0;
                while (first + run < end && text[first + run] == '`') run++;
                if (run >= ticks)
                {
                    closeLine = lineStart;
                    indentEnd = first;
                    block.End = first + run;
                    break;
                }
                lines.Add((lineStart, end));
                if (end >= limit) break;
                lineStart = end + 1;
            }

            if (closeLine < 0)
            {
                block.End = limit;
                block.ContentEnd = limit;
                block.Fail(new TextSpan(pos, ticks), $"This ``` block is missing its closing {new string('`', ticks)}.");
                indentEnd = limit;
                closeLine = limit;
            }
            else
            {
                block.ContentEnd = closeLine;
            }

            string indent = closeLine < limit ? text.Substring(closeLine, indentEnd - closeLine) : string.Empty;
            StringBuilder value = new StringBuilder();
            List<int> map = new List<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                (int s, int e) = lines[i];
                int lineContentEnd = e > s && text[e - 1] == '\r' ? e - 1 : e;
                if (i > 0)
                {
                    map.Add(lines[i - 1].End);
                    value.Append('\n');
                }
                int from = s;
                if (string.CompareOrdinal(text, s, indent, 0, indent.Length) == 0 && s + indent.Length <= lineContentEnd)
                {
                    from = s + indent.Length;
                }
                else if (IsBlank(text, s, lineContentEnd))
                {
                    from = lineContentEnd;
                }
                else if (indent.Length > 0)
                {
                    int f = s;
                    while (f < lineContentEnd && f - s < indent.Length && text[f] == indent[f - s]) f++;
                    from = f;
                    block.Fail(TextSpan.FromBounds(s, f), "This line is indented less than the closing fence of its ``` block.");
                }
                for (int c = from; c < lineContentEnd; c++)
                {
                    map.Add(c);
                    value.Append(text[c]);
                }
            }
            map.Add(lines.Count > 0 ? (lines[lines.Count - 1].End > lines[lines.Count - 1].Start && text[lines[lines.Count - 1].End - 1] == '\r' ? lines[lines.Count - 1].End - 1 : lines[lines.Count - 1].End) : contentStart);
            block.Value = value.ToString();
            block.Map = map.ToArray();
            return block;
        }

        private static bool IsBlank(string text, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (text[i] != ' ' && text[i] != '\t') return false;
            }
            return true;
        }

        /// <summary>Well-known language names suggested at the fence header.</summary>
        public static IReadOnlyList<string> Languages { get; } = new[]
        {
            "text", "sql", "html", "css", "js", "javascript", "json", "md", "markdown", "latex", "tex", "yaml", "yml", "xml",
            "string", "kscript", "scss", "ks.css", "ks.scss", "ts", "typescript", "jsonc", "ps1",
        };

        /// <summary>The type of a fence in an expression, independent of its use as markup text.</summary>
        public static string ValueType(string tag) => tag?.ToLowerInvariant() switch
        {
            "" or "md" or "markdown" => KType.Markdown,
            "latex" or "tex" => KType.Latex,
            "kscript" => KType.FormattedFile,
            null => KType.String,
            string language when Languages.Contains(language, StringComparer.Ordinal) && language != "ps1" => KType.String,
            _ => KType.FormattedFile,
        };

        /// <summary>The filename extension for a document fence.</summary>
        public static string FileExtension(string tag) => tag.ToLowerInvariant() switch
        {
            "" or "md" or "markdown" => "md",
            "latex" or "tex" => "tex",
            _ => tag.ToLowerInvariant(),
        };

        private static bool ValidTag(string tag)
        {
            if (tag.Length == 0) return true;
            if (tag[0] == '<')
            {
                if (tag.Length < 3 || tag[tag.Length - 1] != '>') return false;
                string type = tag.Substring(1, tag.Length - 2).Trim();
                if (type.Length == 0 || !(char.IsLetter(type[0]) || type[0] == '_')) return false;
                foreach (char c in type)
                {
                    if (!(char.IsLetterOrDigit(c) || c is '_' or '.' or '-')) return false;
                }
                return true;
            }
            return tag.Length <= 64 && tag.Split('.').All(part => part.Length > 0
                && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'));
        }

        private void Fail(TextSpan span, string message)
        {
            if (Error != null) return;
            Error = message;
            ErrorSpan = span;
        }
    }

    /// <summary>Line joins inside quoted strings: a backslash at the end of a line.</summary>
    public static class LineJoins
    {
        /// <summary>
        /// True when the backslash at <paramref name="pos"/> ends its line, so the string carries on after the next
        /// line's indentation. <paramref name="after"/> is where it carries on from.
        /// </summary>
        public static bool At(string text, int pos, int limit, out int after)
        {
            after = pos;
            if (pos >= limit || text[pos] != '\\') return false;
            int p = pos + 1;
            if (p < limit && text[p] == '\r') p++;
            if (p >= limit || text[p] != '\n') return false;
            p++;
            while (p < limit && (text[p] == ' ' || text[p] == '\t')) p++;
            after = p;
            return true;
        }

        /// <summary>The string with each backslash-new-line (and the next line's indentation) removed.</summary>
        public static string Join(string text)
        {
            if (text.IndexOf('\\') < 0) return text;
            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (At(text, i, text.Length, out int after))
                {
                    i = after - 1;
                    continue;
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// The one rule for quoted strings, shared by everything that reads them. In "double quotes" <c>\"</c> is a quote
    /// and <c>\\</c> is a backslash; any other backslash is kept, so <c>"\d+"</c> is a pattern as written. 'Single
    /// quotes' have no escapes at all: the string ends at the next <c>'</c>. In both, a backslash at the end of a line
    /// joins it to the next line.
    /// </summary>
    public static class Quotes
    {
        /// <summary>True when the backslash at <paramref name="pos"/> escapes the character after it in a string opened with <paramref name="quote"/>.</summary>
        public static bool IsEscape(string text, int pos, int limit, char quote) =>
            quote == '"' && pos + 1 < limit && text[pos] == '\\' && (text[pos + 1] == '"' || text[pos + 1] == '\\');

        /// <summary>The offset of the quote that closes the string opened at <paramref name="open"/>, or -1.</summary>
        public static int Close(string text, int open, int limit)
        {
            char quote = text[open];
            for (int i = open + 1; i < limit; i++)
            {
                if (IsEscape(text, i, limit, quote)) i++;
                else if (text[i] == quote) return i;
            }
            return -1;
        }

        /// <summary>The value of the string between <paramref name="start"/> and <paramref name="end"/> (the quotes left out).</summary>
        public static string Decode(string text, int start, int end, char quote)
        {
            string raw = text.Substring(start, end - start);
            if (raw.IndexOf('\\') < 0) return raw;
            if (quote != '"') return LineJoins.Join(raw);
            StringBuilder sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                if (IsEscape(raw, i, raw.Length, quote))
                {
                    sb.Append(raw[++i]);
                }
                else if (LineJoins.At(raw, i, raw.Length, out int after))
                {
                    i = after - 1;
                }
                else
                {
                    sb.Append(raw[i]);
                }
            }
            return sb.ToString();
        }
    }
}
