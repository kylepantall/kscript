namespace KScript.Documents.Maths
{
    /// <summary>How an atom is spaced, as in TeX: x + y = z has ordinary x, binary +, relation =.</summary>
    public enum AtomKind
    {
        Ord,
        Op,
        Bin,
        Rel,
        Open,
        Close,
        Punct,
    }

    public enum MathFont
    {
        /// <summary>Letters italic, digits and symbols upright: the default for maths.</summary>
        Normal,
        Roman,
        Bold,
        Italic,
        Sans,
        Mono,
    }

    /// <summary>A parsed LaTeX maths expression. The writers turn it into PDF drawing, MathML, Word maths or LaTeX again.</summary>
    public abstract class MathNode
    {
    }

    public sealed class MathRow : MathNode
    {
        public List<MathNode> Items { get; } = new List<MathNode>();

        public MathRow()
        {
        }

        public MathRow(IEnumerable<MathNode> items) => Items.AddRange(items);
    }

    /// <summary>A letter, number, operator or symbol.</summary>
    public sealed class MathAtom : MathNode
    {
        public MathAtom(string text, AtomKind kind, string command = null, MathFont font = MathFont.Normal)
        {
            Text = text;
            Kind = kind;
            Command = command;
            Font = font;
        }

        /// <summary>The character(s), such as α or 3.14.</summary>
        public string Text { get; }

        public AtomKind Kind { get; set; }

        /// <summary>The command it was written as, such as \alpha, so LaTeX output can use it again.</summary>
        public string Command { get; }

        public MathFont Font { get; set; }

        public bool IsNumber => Text.Length > 0 && (char.IsDigit(Text[0]) || (Text[0] == '.' && Text.Length > 1));
    }

    /// <summary>\text{...}: words inside maths.</summary>
    public sealed class MathText : MathNode
    {
        public MathText(string text, MathFont font = MathFont.Roman)
        {
            Text = text;
            Font = font;
        }

        public string Text { get; }
        public MathFont Font { get; }
    }

    /// <summary>\frac{a}{b}, or \binom{n}{k} (no bar, in parentheses).</summary>
    public sealed class MathFrac : MathNode
    {
        public MathNode Numerator { get; set; }
        public MathNode Denominator { get; set; }
        public bool Bar { get; set; } = true;
        public string Command { get; set; } = "frac";
    }

    public sealed class MathSqrt : MathNode
    {
        public MathNode Body { get; set; }

        /// <summary>The n of an nth root, or null for a square root.</summary>
        public MathNode Index { get; set; }
    }

    /// <summary>A base with a subscript, a superscript or both: x_i^2.</summary>
    public sealed class MathScripts : MathNode
    {
        public MathNode Base { get; set; }
        public MathNode Sub { get; set; }
        public MathNode Sup { get; set; }
    }

    /// <summary>A large operator (∑, ∫) or a named one (lim, max, sin).</summary>
    public sealed class MathBigOp : MathNode
    {
        public MathBigOp(string text, string command, bool limits, bool named)
        {
            Text = text;
            Command = command;
            Limits = limits;
            Named = named;
        }

        public string Text { get; }
        public string Command { get; }

        /// <summary>In displayed maths, scripts go above and below (∑, lim) rather than to the side (∫, sin).</summary>
        public bool Limits { get; set; }

        /// <summary>A word such as lim or sin, set upright, rather than a symbol.</summary>
        public bool Named { get; }

        /// <summary>∑ ∏ ∫: drawn larger than the text.</summary>
        public bool Large => !Named;
    }

    /// <summary>\left( ... \right): delimiters that grow to fit.</summary>
    public sealed class MathFenced : MathNode
    {
        public string Left { get; set; }
        public string Right { get; set; }
        public MathNode Body { get; set; }
    }

    /// <summary>matrix, pmatrix, bmatrix, vmatrix, cases, aligned and array environments.</summary>
    public sealed class MathMatrix : MathNode
    {
        public string Environment { get; set; }
        public List<List<MathNode>> Rows { get; } = new List<List<MathNode>>();

        /// <summary>The delimiters around it: ( ) for pmatrix, { for cases.</summary>
        public string Left => Environment switch
        {
            "pmatrix" => "(",
            "bmatrix" => "[",
            "Bmatrix" or "cases" => "{",
            "vmatrix" => "|",
            "Vmatrix" => "‖",
            _ => string.Empty,
        };

        public string Right => Environment switch
        {
            "pmatrix" => ")",
            "bmatrix" => "]",
            "Bmatrix" => "}",
            "vmatrix" => "|",
            "Vmatrix" => "‖",
            _ => string.Empty,
        };

        /// <summary>The alignment of a column: aligned alternates right and left at &amp;; cases is left; matrices centre.</summary>
        public char AlignOf(int column)
        {
            if (Columns != null && column < Columns.Length) return Columns[column];
            return Environment switch
            {
                "aligned" or "align" or "align*" or "split" => column % 2 == 0 ? 'r' : 'l',
                "cases" or "gathered" => 'l',
                _ => 'c',
            };
        }

        /// <summary>An array's column spec, such as "lcr".</summary>
        public char[] Columns { get; set; }

        public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);
    }

    /// <summary>\hat, \bar, \vec, \dot, \ddot, \tilde, \overline and \underline.</summary>
    public sealed class MathAccent : MathNode
    {
        public string Accent { get; set; }
        public MathNode Body { get; set; }

        public bool Under => Accent == "underline";

        /// <summary>The accent character, for MathML and Word.</summary>
        public string Character => Accent switch
        {
            "hat" or "widehat" => "^",
            "bar" or "overline" or "underline" => "¯",
            "vec" => "→",
            "dot" => "˙",
            "ddot" => "¨",
            "tilde" or "widetilde" => "~",
            _ => "¯",
        };
    }

    /// <summary>\, \; \quad and friends.</summary>
    public sealed class MathSpace : MathNode
    {
        public MathSpace(double em, string command)
        {
            Em = em;
            Command = command;
        }

        /// <summary>Width in ems; negative for \!.</summary>
        public double Em { get; }

        public string Command { get; }
    }
}
