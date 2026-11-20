using System.Security;
using System.Text;

using KScript.Documents.Export;

namespace KScript.Documents.Maths
{
    /// <summary>Writes a maths tree as LaTeX again (for .tex and Markdown files), with characters typed directly turned into commands.</summary>
    public static class MathLatex
    {
        public static string Write(MathNode node)
        {
            var sb = new StringBuilder();
            Write(node, sb);
            return sb.ToString().Trim();
        }

        private static void Group(MathNode node, StringBuilder sb)
        {
            sb.Append('{');
            Write(node, sb);
            sb.Append('}');
        }

        private static void Write(MathNode node, StringBuilder sb)
        {
            switch (node)
            {
                case MathRow row:
                    foreach (MathNode item in row.Items) Write(item, sb);
                    break;
                case MathAtom a:
                {
                    string t = a.Command ?? (MathSymbols.CommandFor.TryGetValue(a.Text, out string cmd) ? cmd : a.Text);
                    if (a.Command == "\\prime") t = new string('\'', a.Text.Length);
                    else if (t.Length == 1 && "{}%$#&_".IndexOf(t[0]) >= 0) t = "\\" + t;
                    Space(sb, t);
                    sb.Append(t);
                    break;
                }
                case MathText text:
                {
                    string command = text.Font switch { MathFont.Bold => "\\textbf", MathFont.Italic => "\\textit", MathFont.Mono => "\\texttt", MathFont.Sans => "\\textsf", _ => "\\text" };
                    sb.Append(command).Append('{').Append(LatexText.Escape(text.Text)).Append('}');
                    break;
                }
                case MathStyled styled:
                    sb.Append('\\').Append(styled.Command);
                    Group(styled.Body, sb);
                    break;
                case MathFrac f:
                    sb.Append('\\').Append(f.Command);
                    Group(f.Numerator, sb);
                    Group(f.Denominator, sb);
                    break;
                case MathSqrt s:
                    sb.Append("\\sqrt");
                    if (s.Index != null) sb.Append('[').Append(Write(s.Index)).Append(']');
                    Group(s.Body, sb);
                    break;
                case MathScripts sc:
                    if (sc.Base is MathScripts || sc.Base is MathFrac) Group(sc.Base, sb);
                    else Write(sc.Base, sb);
                    if (sc.Sub != null)
                    {
                        sb.Append('_');
                        Group(sc.Sub, sb);
                    }
                    if (sc.Sup != null)
                    {
                        sb.Append('^');
                        Group(sc.Sup, sb);
                    }
                    break;
                case MathBigOp op:
                    if (op.Command == "operatorname") sb.Append("\\operatorname").Append(op.Limits ? "*" : string.Empty).Append('{').Append(op.Text).Append('}');
                    else sb.Append('\\').Append(op.Command);
                    break;
                case MathFenced fenced:
                    sb.Append("\\left").Append(Delimiter(fenced.Left)).Append(' ');
                    Write(fenced.Body, sb);
                    sb.Append(" \\right").Append(Delimiter(fenced.Right));
                    break;
                case MathMatrix m:
                {
                    string env = m.Environment == "align" || m.Environment == "align*" ? "aligned" : m.Environment;
                    sb.Append("\\begin{").Append(env).Append('}');
                    if (m.Columns != null) sb.Append('{').Append(new string(m.Columns)).Append('}');
                    for (int r = 0; r < m.Rows.Count; r++)
                    {
                        sb.Append(r == 0 ? " " : " \\\\ ");
                        for (int c = 0; c < m.Rows[r].Count; c++)
                        {
                            if (c > 0) sb.Append(" & ");
                            Write(m.Rows[r][c], sb);
                        }
                    }
                    sb.Append(" \\end{").Append(env).Append('}');
                    break;
                }
                case MathAccent accent:
                    sb.Append('\\').Append(accent.Accent);
                    Group(accent.Body, sb);
                    break;
                case MathSpace space:
                    sb.Append(space.Command == "~" ? "~" : space.Command + (char.IsLetter(space.Command[^1]) ? " " : string.Empty));
                    break;
            }
        }

        /// <summary>A space after a command that ends in a letter, so \alpha x doesn't become \alphax.</summary>
        private static void Space(StringBuilder sb, string next)
        {
            if (sb.Length == 0 || next.Length == 0) return;
            int i = sb.Length - 1;
            if (!char.IsLetter(sb[i]) || !char.IsLetter(next[0])) return;
            while (i >= 0 && char.IsLetter(sb[i])) i--;
            if (i >= 0 && sb[i] == '\\') sb.Append(' ');
        }

        private static string Delimiter(string d) => d switch
        {
            "" => ".",
            "{" => "\\{",
            "}" => "\\}",
            "⟨" => "\\langle",
            "⟩" => "\\rangle",
            "⌊" => "\\lfloor",
            "⌋" => "\\rfloor",
            "⌈" => "\\lceil",
            "⌉" => "\\rceil",
            "‖" => "\\|",
            _ => d,
        };
    }

    /// <summary>Writes maths as MathML, which browsers show without scripts.</summary>
    public static class MathMl
    {
        public static string Write(MathNode node, bool display)
        {
            var sb = new StringBuilder();
            sb.Append("<math xmlns=\"http://www.w3.org/1998/Math/MathML\"").Append(display ? " display=\"block\"" : string.Empty).Append('>');
            Write(node, sb);
            sb.Append("</math>");
            return sb.ToString();
        }

        private static string E(string s) => SecurityElement.Escape(s);

        private static void Row(MathNode node, StringBuilder sb)
        {
            sb.Append("<mrow>");
            Write(node, sb);
            sb.Append("</mrow>");
        }

        private static void Write(MathNode node, StringBuilder sb)
        {
            switch (node)
            {
                case MathRow row:
                    foreach (MathNode item in row.Items) Write(item, sb);
                    break;
                case MathAtom a:
                    Atom(a, sb);
                    break;
                case MathText t:
                    sb.Append("<mtext>").Append(E(t.Text)).Append("</mtext>");
                    break;
                case MathStyled styled:
                    Write(styled.Body, sb);
                    break;
                case MathFrac f:
                    if (!f.Bar) sb.Append("<mrow><mo>(</mo>");
                    sb.Append(f.Bar ? "<mfrac>" : "<mfrac linethickness=\"0\">");
                    Row(f.Numerator, sb);
                    Row(f.Denominator, sb);
                    sb.Append("</mfrac>");
                    if (!f.Bar) sb.Append("<mo>)</mo></mrow>");
                    break;
                case MathSqrt s:
                    if (s.Index == null)
                    {
                        sb.Append("<msqrt>");
                        Write(s.Body, sb);
                        sb.Append("</msqrt>");
                    }
                    else
                    {
                        sb.Append("<mroot>");
                        Row(s.Body, sb);
                        Row(s.Index, sb);
                        sb.Append("</mroot>");
                    }
                    break;
                case MathScripts sc:
                {
                    bool under = sc.Base is MathBigOp op && op.Limits;
                    string tag = sc.Sub != null && sc.Sup != null ? (under ? "munderover" : "msubsup") : sc.Sub != null ? (under ? "munder" : "msub") : (under ? "mover" : "msup");
                    sb.Append('<').Append(tag).Append('>');
                    Row(sc.Base, sb);
                    if (sc.Sub != null) Row(sc.Sub, sb);
                    if (sc.Sup != null) Row(sc.Sup, sb);
                    sb.Append("</").Append(tag).Append('>');
                    break;
                }
                case MathBigOp op:
                    if (op.Named) sb.Append("<mi>").Append(E(op.Text)).Append("</mi><mo>&#x2061;</mo>");
                    else sb.Append("<mo largeop=\"true\">").Append(op.Text).Append("</mo>");
                    break;
                case MathFenced fenced:
                    sb.Append("<mrow><mo stretchy=\"true\">").Append(E(fenced.Left)).Append("</mo>");
                    Write(fenced.Body, sb);
                    sb.Append("<mo stretchy=\"true\">").Append(E(fenced.Right)).Append("</mo></mrow>");
                    break;
                case MathMatrix m:
                    sb.Append("<mrow>");
                    if (m.Left.Length > 0) sb.Append("<mo stretchy=\"true\">").Append(E(m.Left)).Append("</mo>");
                    sb.Append("<mtable");
                    int columns = m.ColumnCount;
                    sb.Append(" columnalign=\"").Append(string.Join(" ", Enumerable.Range(0, columns).Select(c => m.AlignOf(c) switch { 'l' => "left", 'r' => "right", _ => "center" }))).Append('"');
                    if (m.Environment == "aligned" || m.Environment == "align" || m.Environment == "align*" || m.Environment == "split") sb.Append(" columnspacing=\"0\"");
                    sb.Append('>');
                    foreach (List<MathNode> r in m.Rows)
                    {
                        sb.Append("<mtr>");
                        foreach (MathNode cell in r)
                        {
                            sb.Append("<mtd>");
                            Write(cell, sb);
                            sb.Append("</mtd>");
                        }
                        sb.Append("</mtr>");
                    }
                    sb.Append("</mtable>");
                    if (m.Right.Length > 0) sb.Append("<mo stretchy=\"true\">").Append(E(m.Right)).Append("</mo>");
                    sb.Append("</mrow>");
                    break;
                case MathAccent accent:
                {
                    string tag = accent.Under ? "munder" : "mover";
                    sb.Append('<').Append(tag).Append(accent.Under ? " accentunder=\"true\">" : " accent=\"true\">");
                    Row(accent.Body, sb);
                    sb.Append("<mo stretchy=\"").Append(accent.Accent.StartsWith("wide", StringComparison.Ordinal) || accent.Accent.EndsWith("line", StringComparison.Ordinal) ? "true" : "false").Append("\">")
                        .Append(E(accent.Character)).Append("</mo></").Append(tag).Append('>');
                    break;
                }
                case MathSpace space:
                    sb.Append("<mspace width=\"").Append(Units.N(space.Em)).Append("em\"/>");
                    break;
            }
        }

        private static void Atom(MathAtom a, StringBuilder sb)
        {
            string variant = a.Font switch { MathFont.Bold => " mathvariant=\"bold\"", MathFont.Roman => " mathvariant=\"normal\"", MathFont.Sans => " mathvariant=\"sans-serif\"", MathFont.Mono => " mathvariant=\"monospace\"", _ => string.Empty };
            if (a.IsNumber)
            {
                sb.Append("<mn").Append(variant).Append('>').Append(E(a.Text)).Append("</mn>");
            }
            else if (a.Kind == AtomKind.Ord && (char.IsLetter(a.Text[0]) || a.Text[0] > 0x370))
            {
                sb.Append("<mi").Append(variant).Append('>').Append(E(a.Text)).Append("</mi>");
            }
            else
            {
                sb.Append("<mo").Append(a.Kind == AtomKind.Open || a.Kind == AtomKind.Close ? " stretchy=\"false\"" : string.Empty).Append('>').Append(E(a.Text)).Append("</mo>");
            }
        }
    }

    /// <summary>Writes maths as Office Math (OMML), which Word shows and edits as equations.</summary>
    public static class MathOmml
    {
        public static string Write(MathNode node)
        {
            var sb = new StringBuilder();
            sb.Append("<m:oMath>");
            WriteRow(Flatten(node), sb);
            sb.Append("</m:oMath>");
            return sb.ToString();
        }

        private static List<MathNode> Flatten(MathNode node) => node is MathRow row ? row.Items : new List<MathNode> { node };

        private static string E(string s) => SecurityElement.Escape(s);

        /// <param name="normal">Ordinary text (\\text), set in the paragraph's font rather than as maths.</param>
        private static void Run(string text, StringBuilder sb, bool upright = false, bool bold = false, bool normal = false)
        {
            sb.Append("<m:r>");
            if (normal) sb.Append("<m:rPr><m:nor/></m:rPr>");
            else if (upright || bold) sb.Append("<m:rPr><m:sty m:val=\"").Append(bold ? (upright ? "b" : "bi") : "p").Append("\"/></m:rPr>");
            if (normal) sb.Append(bold ? "<w:rPr><w:b/></w:rPr>" : string.Empty);
            else sb.Append("<w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/></w:rPr>");
            sb.Append("<m:t xml:space=\"preserve\">").Append(E(text)).Append("</m:t></m:r>");
        }

        private static void Arg(string tag, MathNode node, StringBuilder sb)
        {
            sb.Append("<m:").Append(tag).Append('>');
            if (node != null) WriteRow(Flatten(node), sb);
            sb.Append("</m:").Append(tag).Append('>');
        }

        /// <summary>A row. A large operator takes the terms after it as its body, up to the next + or relation.</summary>
        private static void WriteRow(List<MathNode> items, StringBuilder sb)
        {
            for (int i = 0; i < items.Count; i++)
            {
                MathNode item = items[i];
                MathBigOp op = item as MathBigOp ?? (item as MathScripts)?.Base as MathBigOp;
                if (op != null && op.Large)
                {
                    int end = i + 1;
                    while (end < items.Count && !(items[end] is MathAtom a && (a.Kind == AtomKind.Rel || (a.Kind == AtomKind.Bin && (a.Text == "+" || a.Text == "−"))))) end++;
                    Nary(op, item as MathScripts, items.GetRange(i + 1, end - i - 1), sb);
                    i = end - 1;
                    continue;
                }
                Write(item, sb);
            }
        }

        private static void Nary(MathBigOp op, MathScripts scripts, List<MathNode> body, StringBuilder sb)
        {
            sb.Append("<m:nary><m:naryPr><m:chr m:val=\"").Append(op.Text).Append("\"/><m:limLoc m:val=\"").Append(op.Limits ? "undOvr" : "subSup").Append("\"/>");
            if (scripts?.Sub == null) sb.Append("<m:subHide m:val=\"1\"/>");
            if (scripts?.Sup == null) sb.Append("<m:supHide m:val=\"1\"/>");
            sb.Append("</m:naryPr>");
            Arg("sub", scripts?.Sub, sb);
            Arg("sup", scripts?.Sup, sb);
            sb.Append("<m:e>");
            WriteRow(body, sb);
            sb.Append("</m:e></m:nary>");
        }

        private static void Write(MathNode node, StringBuilder sb)
        {
            switch (node)
            {
                case MathRow row:
                    WriteRow(row.Items, sb);
                    break;
                case MathAtom a:
                    Run(a.Text, sb, upright: a.Font == MathFont.Roman || a.Font == MathFont.Sans || a.Font == MathFont.Mono, bold: a.Font == MathFont.Bold);
                    break;
                case MathText t:
                    Run(t.Text, sb, bold: t.Font == MathFont.Bold, normal: true);
                    break;
                case MathStyled styled:
                    Write(styled.Body, sb);
                    break;
                case MathFrac f:
                    if (!f.Bar) sb.Append("<m:d><m:e>");
                    sb.Append("<m:f>");
                    if (!f.Bar) sb.Append("<m:fPr><m:type m:val=\"noBar\"/></m:fPr>");
                    Arg("num", f.Numerator, sb);
                    Arg("den", f.Denominator, sb);
                    sb.Append("</m:f>");
                    if (!f.Bar) sb.Append("</m:e></m:d>");
                    break;
                case MathSqrt s:
                    sb.Append("<m:rad>");
                    if (s.Index == null) sb.Append("<m:radPr><m:degHide m:val=\"1\"/></m:radPr>");
                    Arg("deg", s.Index, sb);
                    Arg("e", s.Body, sb);
                    sb.Append("</m:rad>");
                    break;
                case MathScripts sc when sc.Base is MathBigOp op && op.Named && op.Limits:
                    // lim_{x \to 0}: the limit goes under the word.
                    sb.Append("<m:func><m:fName><m:limLow><m:e>");
                    Run(op.Text, sb, upright: true);
                    sb.Append("</m:e>");
                    Arg("lim", sc.Sub ?? sc.Sup, sb);
                    sb.Append("</m:limLow></m:fName><m:e/></m:func>");
                    break;
                case MathScripts sc:
                {
                    string tag = sc.Sub != null && sc.Sup != null ? "sSubSup" : sc.Sub != null ? "sSub" : "sSup";
                    sb.Append("<m:").Append(tag).Append('>');
                    Arg("e", sc.Base, sb);
                    if (sc.Sub != null) Arg("sub", sc.Sub, sb);
                    if (sc.Sup != null) Arg("sup", sc.Sup, sb);
                    sb.Append("</m:").Append(tag).Append('>');
                    break;
                }
                case MathBigOp op when op.Named:
                    Run(op.Text, sb, upright: true);
                    break;
                case MathBigOp op:
                    Nary(op, null, new List<MathNode>(), sb);
                    break;
                case MathFenced fenced:
                    Delimited(fenced.Left, fenced.Right, sb, () => Arg("e", fenced.Body, sb));
                    break;
                case MathMatrix m:
                {
                    void Table()
                    {
                        sb.Append("<m:e><m:m><m:mPr><m:mcs>");
                        for (int c = 0; c < m.ColumnCount; c++)
                            sb.Append("<m:mc><m:mcPr><m:count m:val=\"1\"/><m:mcJc m:val=\"").Append(m.AlignOf(c) switch { 'l' => "left", 'r' => "right", _ => "center" }).Append("\"/></m:mcPr></m:mc>");
                        sb.Append("</m:mcs></m:mPr>");
                        foreach (List<MathNode> r in m.Rows)
                        {
                            sb.Append("<m:mr>");
                            for (int c = 0; c < m.ColumnCount; c++) Arg("e", c < r.Count ? r[c] : null, sb);
                            sb.Append("</m:mr>");
                        }
                        sb.Append("</m:m></m:e>");
                    }
                    if (m.Left.Length == 0 && m.Right.Length == 0)
                    {
                        sb.Append("<m:d><m:dPr><m:begChr m:val=\"\"/><m:endChr m:val=\"\"/></m:dPr>");
                        Table();
                        sb.Append("</m:d>");
                    }
                    else
                    {
                        Delimited(m.Left, m.Right, sb, Table);
                    }
                    break;
                }
                case MathAccent accent when accent.Accent == "overline" || accent.Accent == "underline":
                    sb.Append("<m:bar><m:barPr><m:pos m:val=\"").Append(accent.Under ? "bot" : "top").Append("\"/></m:barPr>");
                    Arg("e", accent.Body, sb);
                    sb.Append("</m:bar>");
                    break;
                case MathAccent accent:
                {
                    string chr = accent.Accent switch { "hat" or "widehat" => "̂", "bar" => "̅", "vec" => "⃗", "dot" => "̇", "ddot" => "̈", _ => "̃" };
                    sb.Append("<m:acc><m:accPr><m:chr m:val=\"").Append(chr).Append("\"/></m:accPr>");
                    Arg("e", accent.Body, sb);
                    sb.Append("</m:acc>");
                    break;
                }
                case MathSpace space:
                    Run(space.Em >= 1 ? " " : space.Em > 0 ? " " : string.Empty, sb);
                    break;
            }
        }

        private static void Delimited(string left, string right, StringBuilder sb, Action body)
        {
            sb.Append("<m:d><m:dPr><m:begChr m:val=\"").Append(E(left)).Append("\"/><m:endChr m:val=\"").Append(E(right)).Append("\"/></m:dPr>");
            body();
            sb.Append("</m:d>");
        }
    }

    /// <summary>Maths as plain text, for PDF bookmarks and alt text: x^2 + y^2.</summary>
    public static class MathPlain
    {
        public static string Write(MathNode node)
        {
            var sb = new StringBuilder();
            Write(node, sb);
            return sb.ToString().Trim();
        }

        private static void Write(MathNode node, StringBuilder sb)
        {
            switch (node)
            {
                case MathRow row:
                    foreach (MathNode n in row.Items) Write(n, sb);
                    break;
                case MathAtom a:
                    if (a.Kind == AtomKind.Bin || a.Kind == AtomKind.Rel) sb.Append(' ').Append(a.Text).Append(' ');
                    else sb.Append(a.Text);
                    break;
                case MathText t:
                    sb.Append(t.Text);
                    break;
                case MathStyled s:
                    Write(s.Body, sb);
                    break;
                case MathFrac f:
                    sb.Append('(');
                    Write(f.Numerator, sb);
                    sb.Append(")/(");
                    Write(f.Denominator, sb);
                    sb.Append(')');
                    break;
                case MathSqrt sq:
                    sb.Append("√(");
                    Write(sq.Body, sb);
                    sb.Append(')');
                    break;
                case MathScripts sc:
                    Write(sc.Base, sb);
                    if (sc.Sub != null)
                    {
                        sb.Append('_');
                        Write(sc.Sub, sb);
                    }
                    if (sc.Sup != null)
                    {
                        sb.Append('^');
                        Write(sc.Sup, sb);
                    }
                    break;
                case MathBigOp op:
                    sb.Append(op.Text).Append(op.Named ? " " : string.Empty);
                    break;
                case MathFenced fe:
                    sb.Append(fe.Left);
                    Write(fe.Body, sb);
                    sb.Append(fe.Right);
                    break;
                case MathMatrix m:
                    sb.Append(m.Left).Append(string.Join("; ", m.Rows.Select(r => string.Join(", ", r.Select(Write))))).Append(m.Right);
                    break;
                case MathAccent ac:
                    Write(ac.Body, sb);
                    break;
                case MathSpace sp:
                    if (sp.Em > 0) sb.Append(' ');
                    break;
            }
        }
    }
}
