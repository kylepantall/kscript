namespace KScript.Syntax.Text
{
    /// <summary>A range of a document: a start offset and a length, in UTF-16 code units.</summary>
    public readonly struct TextSpan : IEquatable<TextSpan>
    {
        public TextSpan(int start, int length)
        {
            Start = start;
            Length = Math.Max(0, length);
        }

        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;
        public bool IsEmpty => Length == 0;

        public static TextSpan FromBounds(int start, int end) => new TextSpan(start, end - start);

        /// <summary>True if the offset is inside the span or touches its end (the usual rule for a cursor).</summary>
        public bool Contains(int offset) => offset >= Start && offset <= End;

        public bool ContainsStrict(int offset) => offset >= Start && offset < End;

        public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length;
        public override bool Equals(object obj) => obj is TextSpan other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Start, Length);
        public override string ToString() => $"[{Start}..{End})";
    }

    /// <summary>A zero-based line and character (UTF-16) position, as used by the Language Server Protocol.</summary>
    public readonly struct LinePosition
    {
        public LinePosition(int line, int character)
        {
            Line = line;
            Character = character;
        }

        public int Line { get; }
        public int Character { get; }
        public override string ToString() => $"{Line + 1}:{Character + 1}";
    }

    /// <summary>The text of a document with a line index, so offsets and line/character positions convert both ways.</summary>
    public sealed class SourceText
    {
        private readonly int[] lineStarts;

        public SourceText(string text)
        {
            Text = text ?? string.Empty;
            List<int> starts = new List<int> { 0 };
            for (int i = 0; i < Text.Length; i++)
            {
                char c = Text[i];
                if (c == '\r')
                {
                    if (i + 1 < Text.Length && Text[i + 1] == '\n')
                    {
                        i++;
                    }
                    starts.Add(i + 1);
                }
                else if (c == '\n')
                {
                    starts.Add(i + 1);
                }
            }
            lineStarts = starts.ToArray();
        }

        public string Text { get; }
        public int Length => Text.Length;
        public int LineCount => lineStarts.Length;

        public string this[TextSpan span] => Text.Substring(span.Start, Math.Min(span.Length, Text.Length - span.Start));

        public LinePosition GetPosition(int offset)
        {
            offset = Math.Clamp(offset, 0, Text.Length);
            int line = Array.BinarySearch(lineStarts, offset);
            if (line < 0)
            {
                line = ~line - 1;
            }
            return new LinePosition(line, offset - lineStarts[line]);
        }

        public int GetOffset(LinePosition position) => GetOffset(position.Line, position.Character);

        public int GetOffset(int line, int character)
        {
            if (line < 0)
            {
                return 0;
            }
            if (line >= lineStarts.Length)
            {
                return Text.Length;
            }
            int start = lineStarts[line];
            int end = line + 1 < lineStarts.Length ? lineStarts[line + 1] : Text.Length;
            return Math.Min(start + Math.Max(0, character), end);
        }
    }
}
