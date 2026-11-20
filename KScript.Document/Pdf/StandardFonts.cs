namespace KScript.Documents.Pdf
{
    /// <summary>A font of the PDF base 14: every PDF reader has them, so nothing is embedded and no font licence applies.</summary>
    public readonly record struct Face(FontFamily Family, bool Bold, bool Italic)
    {
        public string BaseFont => Family switch
        {
            FontFamily.Sans => Bold ? (Italic ? "Helvetica-BoldOblique" : "Helvetica-Bold") : (Italic ? "Helvetica-Oblique" : "Helvetica"),
            FontFamily.Mono => Bold ? (Italic ? "Courier-BoldOblique" : "Courier-Bold") : (Italic ? "Courier-Oblique" : "Courier"),
            _ => Bold ? (Italic ? "Times-BoldItalic" : "Times-Bold") : (Italic ? "Times-Italic" : "Times-Roman"),
        };

        public Face WithBold(bool bold = true) => this with { Bold = bold };

        public Face WithItalic(bool italic = true) => this with { Italic = italic };
    }

    /// <summary>Widths and encodings of the base 14 fonts. Text uses WinAnsi; Greek and maths symbols fall back to Symbol.</summary>
    internal static partial class StandardFonts
    {
        public const string Symbol = "Symbol";

        private static readonly Dictionary<char, byte> WinAnsiHigh = new Dictionary<char, byte>
        {
            ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86,
            ['‡'] = 0x87, ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C,
            ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95,
            ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B,
            ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
        };

        // Built on first use: the glyph table is in the other half of this partial class, whose fields may start later.
        private static Dictionary<int, (byte Code, short Width)> symbolMap;

        private static Dictionary<int, (byte Code, short Width)> SymbolMap => symbolMap ??= SymbolGlyphs.ToDictionary(g => g.Unicode, g => (g.Code, g.Width));


        /// <summary>Characters neither font has, drawn as a near equivalent.</summary>
        private static readonly Dictionary<char, string> Substitutes = new Dictionary<char, string>
        {
            ['ℓ'] = "l", ['ℏ'] = "h", ['≪'] = "«", ['≫'] = "»", ['⋯'] = "···",
            ['⋮'] = ":", ['⋱'] = "··", ['↦'] = "→", ['⊨'] = "|=", ['⊢'] = "|-",
            ['∓'] = "±", ['⋆'] = "*", ['∘'] = "°", ['∙'] = "•", ['∖'] = "\\",
            ['≃'] = "≅", ['≔'] = ":=", ['‖'] = "||", ['⌊'] = "[", ['⌋'] = "]", ['⌈'] = "[",
            ['⌉'] = "]", ['⟨'] = "〈", ['⟩'] = "〉", ['ℵ'] = "ℵ", ['⊤'] = "T",
            ['△'] = "∆", ['∵'] = ":.", ['✓'] = "v", ['′'] = "′", [' '] = " ",
            ['∬'] = "∫∫", ['∭'] = "∫∫∫", ['∮'] = "∫", ['∐'] = "∏",
            ['⨁'] = "⊕", ['⨂'] = "⊗", ['∣'] = "|", ['∥'] = "||", [' '] = " ", [' '] = " ",
            [' '] = " ", ['‑'] = "-", ['‐'] = "-", ['−'] = "−",
        };

        /// <summary>The WinAnsi code of a character, or -1.</summary>
        public static int WinAnsi(char c)
        {
            if (c >= 0x20 && c <= 0x7E) return c;
            if (c >= 0xA0 && c <= 0xFF) return c;
            return WinAnsiHigh.TryGetValue(c, out byte b) ? b : -1;
        }

        /// <summary>The width of a WinAnsi code in a font, in 1/1000 em.</summary>
        public static double Width(string baseFont, int code)
        {
            short[] widths = WinAnsiWidths[baseFont];
            int i = code - 32;
            return i >= 0 && i < widths.Length ? widths[i] : 0;
        }

        /// <summary>
        /// Splits text into runs that one font can draw: the face's own font for Latin text, Symbol for Greek and
        /// maths. Each run has its bytes and width in points at <paramref name="size"/>.
        /// </summary>
        public static List<(string Font, byte[] Bytes, double Width)> Runs(string text, Face face, double size)
        {
            var runs = new List<(string, byte[], double)>();
            string current = null;
            var bytes = new List<byte>();
            double width = 0;
            void Flush()
            {
                if (bytes.Count > 0) runs.Add((current, bytes.ToArray(), width * size / 1000));
                bytes.Clear();
                width = 0;
            }
            void Add(string font, byte code, double w)
            {
                if (font != current)
                {
                    Flush();
                    current = font;
                }
                bytes.Add(code);
                width += w;
            }
            string baseFont = face.BaseFont;
            foreach (char ch in Expand(text))
            {
                int code = WinAnsi(ch);
                if (code >= 0 && Width(baseFont, code) > 0)
                {
                    Add(baseFont, (byte)code, Width(baseFont, code));
                    continue;
                }
                if (SymbolMap.TryGetValue(ch, out var sym))
                {
                    Add(Symbol, sym.Code, sym.Width);
                    continue;
                }
                // Nothing draws it: a question mark, as PDF readers do for missing glyphs.
                Add(baseFont, (byte)'?', Width(baseFont, '?'));
            }
            Flush();
            return runs;
        }

        /// <summary>Text with characters neither font has replaced by near equivalents.</summary>
        private static IEnumerable<char> Expand(string text)
        {
            foreach (char c in text)
            {
                if (WinAnsi(c) < 0 && !SymbolMap.ContainsKey(c) && Substitutes.TryGetValue(c, out string sub))
                {
                    foreach (char s in sub) yield return s;
                    continue;
                }
                yield return c;
            }
        }

        public static double Measure(string text, Face face, double size) => Runs(text, face, size).Sum(r => r.Width);
    }
}
