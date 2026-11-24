namespace KScript.Syntax.Markup
{
    /// <summary>Identifies literal Markdown code before KScript parses tags and expressions.</summary>
    public static class MarkdownCode
    {
        /// <returns>The end of a code span or fence, or the unchanged position.</returns>
        public static int EndAt(string text, int start)
        {
            char marker = text[start];
            if (marker is not ('`' or '~') || start > 0 && text[start - 1] == marker) return start;
            int after = RunEnd(text, start, marker);
            int width = after - start;

            if (width >= 3 && IsFencePosition(text, start))
            {
                int openEnd = LineEnd(text, after);
                if (marker == '~' || !text.AsSpan(after, openEnd - after).Contains('`'))
                {
                    for (int line = openEnd + 1; line < text.Length;)
                    {
                        int end = LineEnd(text, line);
                        int fence = line;
                        while (fence < end && fence - line < 4 && text[fence] == ' ') fence++;
                        if (fence - line < 4 && fence + width <= end && text[fence] == marker)
                        {
                            int close = RunEnd(text, fence, marker);
                            if (close - fence >= width && text.AsSpan(close, end - close).Trim().IsEmpty)
                                return end;
                        }
                        line = end + 1;
                    }
                    return text.Length;
                }
            }

            if (marker != '`' || Escaped(text, start)) return start;
            for (int p = after; p < text.Length;)
            {
                if (text[p] == '\n')
                {
                    int lineEnd = LineEnd(text, p + 1);
                    if (text.AsSpan(p + 1, lineEnd - p - 1).Trim().IsEmpty) break;
                }
                if (text[p] == '`')
                {
                    int end = RunEnd(text, p, marker);
                    if (end - p == width) return end;
                    p = end;
                }
                else p++;
            }
            return start;
        }

        private static int RunEnd(string text, int start, char marker)
        {
            int end = start;
            while (end < text.Length && text[end] == marker) end++;
            return end;
        }

        private static int LineEnd(string text, int start)
        {
            int end = text.IndexOf('\n', start);
            return end < 0 ? text.Length : end;
        }

        private static bool IsFencePosition(string text, int pos)
        {
            int start = pos;
            while (start > 0 && pos - start < 4 && text[start - 1] == ' ') start--;
            return pos - start < 4 && (start == 0 || text[start - 1] == '\n');
        }

        private static bool Escaped(string text, int pos)
        {
            int start = pos;
            while (start > 0 && text[start - 1] == '\\') start--;
            return (pos - start) % 2 != 0;
        }
    }
}
