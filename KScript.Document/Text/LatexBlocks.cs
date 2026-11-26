using System.Text;
using System.Text.RegularExpressions;

using KScript.Documents.Maths;
using KScript.Runtime;

namespace KScript.Documents.Text
{
    /// <summary>
    /// Reads the LaTeX a document part is commonly written in into blocks: sections, paragraphs, lists, maths,
    /// quotes, code, figures and the usual text commands. A command it doesn't read is an error that names it,
    /// so nothing is dropped without a word.
    /// </summary>
    public static class LatexBlocks
    {
        private static readonly Dictionary<string, int> Sections = new(StringComparer.Ordinal)
        {
            ["section"] = 1, ["subsection"] = 2, ["subsubsection"] = 3,
        };

        private static readonly Dictionary<string, double> Skips = new(StringComparer.Ordinal)
        {
            ["smallskip"] = 3, ["medskip"] = 6, ["bigskip"] = 12,
        };

        private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
        {
            ["LaTeX"] = "LaTeX", ["TeX"] = "TeX", ["ldots"] = "\u2026", ["dots"] = "\u2026", ["textbackslash"] = "\\",
            ["quad"] = " ", ["qquad"] = " ", ["noindent"] = string.Empty, ["centering"] = string.Empty,
        };

        private static readonly Regex BlankLine = new Regex(@"\G\n[ \t]*\n");

        private static readonly string[] MathEnvironments = { "equation", "equation*", "displaymath", "align", "align*" };

        public static List<Block> Parse(string text) => new Reader((text ?? string.Empty).Replace("\r\n", "\n")).Blocks(null, inList: false);

        private static KScriptException Error(string message) => new KScriptException(message, "Syntax");

        private static KScriptException Unknown(string what) =>
            Error($"{what} isn't LaTeX a document part reads. Write that part with document elements, or leave it out.");

        private sealed class Reader
        {
            private readonly string text;
            private int pos;
            private string label;

            public Reader(string text) => this.text = text;

            private bool End => pos >= text.Length;

            private bool Starts(string s) => string.CompareOrdinal(text, pos, s, 0, s.Length) == 0;

            /// <summary>The command at the cursor (<c>section*</c> for <c>\section*</c>), or null. Doesn't move.</summary>
            private string Command(out int after)
            {
                after = pos;
                if (End || text[pos] != '\\') return null;
                int i = pos + 1;
                while (i < text.Length && char.IsLetter(text[i])) i++;
                if (i == pos + 1) return null;
                if (i < text.Length && text[i] == '*') i++;
                after = i;
                return text.Substring(pos + 1, i - pos - 1);
            }

            private void SkipSpace(bool lines = true)
            {
                while (!End && (text[pos] == ' ' || text[pos] == '\t' || (lines && text[pos] == '\n'))) pos++;
            }

            /// <summary>The text of a <c>{group}</c> at the cursor, as written.</summary>
            private string Group(string command)
            {
                SkipSpace(lines: false);
                if (End || text[pos] != '{') throw Error($"\\{command} needs its text in braces: \\{command}{{...}}.");
                int depth = 0;
                int start = pos + 1;
                for (; pos < text.Length; pos++)
                {
                    if (text[pos] == '\\') pos++;
                    else if (text[pos] == '{') depth++;
                    else if (text[pos] == '}' && --depth == 0) return text.Substring(start, pos++ - start);
                }
                throw Error($"\\{command}{{ is never closed with }}.");
            }

            /// <summary>An <c>[option]</c> at the cursor, or null.</summary>
            private string Option()
            {
                if (End || text[pos] != '[') return null;
                int close = text.IndexOf(']', pos);
                if (close < 0) return null;
                string option = text.Substring(pos + 1, close - pos - 1);
                pos = close + 1;
                return option;
            }

            private static List<Inline> Inlines(string source)
            {
                var reader = new Reader(source);
                var inlines = new List<Inline>();
                while (!reader.End) reader.Inline(inlines);
                return inlines;
            }

            private static void Add(List<Inline> into, string s)
            {
                if (s.Length == 0) return;
                if (into.Count > 0 && into[^1] is TextInline last) last.Text += s;
                else into.Add(new TextInline(s));
            }

            /// <summary>Reads blocks up to the <c>\end</c> of <paramref name="environment"/> (or an <c>\item</c> in a list), which is left for the caller.</summary>
            public List<Block> Blocks(string environment, bool inList)
            {
                var root = new List<Block>();
                var stack = new List<(int Level, List<Block> Blocks)> { (0, root) };
                var paragraph = new List<Inline>();
                List<Block> Target() => stack[^1].Blocks;
                void Flush()
                {
                    if (paragraph.Count > 0 && paragraph[^1] is TextInline tail) tail.Text = tail.Text.TrimEnd();
                    if (paragraph.Any(i => i is not TextInline t || t.Text.Length > 0)) Target().Add(new Paragraph { Inlines = paragraph, Id = label });
                    paragraph = new List<Inline>();
                    label = null;
                }

                while (true)
                {
                    if (paragraph.Count == 0) SkipSpace();
                    if (End)
                    {
                        if (environment != null) throw Error($"\\begin{{{environment}}} has no \\end{{{environment}}}.");
                        Flush();
                        return root;
                    }
                    if (text[pos] == '\n' && BlankLine.Match(text, pos).Success)
                    {
                        Flush();
                        continue;
                    }
                    if (Starts("\\[") || Starts("$$"))
                    {
                        string close = Starts("$$") ? "$$" : "\\]";
                        int end = text.IndexOf(close, pos + 2, StringComparison.Ordinal);
                        if (end < 0) throw Error($"The maths opened with {text.Substring(pos, 2)} is never closed with {close}.");
                        Flush();
                        Target().Add(Maths(text.Substring(pos + 2, end - pos - 2), numbered: false));
                        pos = end + 2;
                        continue;
                    }

                    string command = Command(out int after);
                    string name = command?.TrimEnd('*');
                    if (command == "end" || (command == "item" && inList))
                    {
                        Flush();
                        return root;
                    }
                    if (name != null && Sections.TryGetValue(name, out int level))
                    {
                        Flush();
                        pos = after;
                        while (stack.Count > 1 && stack[^1].Level >= level) stack.RemoveAt(stack.Count - 1);
                        var section = new Section { Title = Inlines(Group(command)), Numbered = command == name };
                        SkipSpace();
                        if (Command(out int afterLabel) == "label")
                        {
                            pos = afterLabel;
                            section.Id = Group("label").Trim();
                        }
                        Target().Add(section);
                        stack.Add((level, section.Blocks));
                        continue;
                    }
                    switch (command)
                    {
                        case "begin":
                            Flush();
                            pos = after;
                            Environment(Group("begin").Trim(), Target());
                            continue;
                        case "par":
                            Flush();
                            pos = after;
                            continue;
                        case "newpage" or "clearpage" or "pagebreak":
                            Flush();
                            pos = after;
                            Target().Add(new PageBreak());
                            continue;
                        case "tableofcontents":
                            Flush();
                            pos = after;
                            Target().Add(new Contents());
                            continue;
                        case "hrule":
                            Flush();
                            pos = after;
                            Target().Add(new Rule());
                            continue;
                        case "documentclass" or "usepackage":
                            // A whole file pasted in: its settings belong to the document the part is drawn in.
                            pos = after;
                            Option();
                            Group(command);
                            continue;
                        case "item":
                            throw Error("\\item only works inside \\begin{itemize} or \\begin{enumerate}.");
                    }
                    if (command != null && Skips.TryGetValue(command, out double height))
                    {
                        Flush();
                        pos = after;
                        Target().Add(new Spacer { Height = height });
                        continue;
                    }
                    Inline(paragraph);
                }
            }

            private void Environment(string environment, List<Block> target)
            {
                switch (environment)
                {
                    case "document":
                        target.AddRange(Blocks(environment, inList: false));
                        break;
                    case "itemize" or "enumerate":
                        var list = new ListBlock { Style = environment == "enumerate" ? ListStyle.Number : ListStyle.Bullet };
                        SkipSpace();
                        while (Command(out int afterItem) == "item")
                        {
                            pos = afterItem;
                            var item = new ListItem();
                            item.Blocks.AddRange(Blocks(environment, inList: true));
                            list.Items.Add(item);
                        }
                        target.Add(list);
                        break;
                    case "quote" or "quotation":
                        var quote = new Quote();
                        quote.Blocks.AddRange(Blocks(environment, inList: false));
                        target.Add(quote);
                        break;
                    case "center":
                        foreach (Block block in Blocks(environment, inList: false))
                        {
                            if (block is Paragraph centred) centred.Align = PaperAlign.Center;
                            target.Add(block);
                        }
                        break;
                    case "verbatim":
                        target.Add(new CodeBlock { Text = Raw(environment).Trim('\n') });
                        return;
                    case "figure":
                        target.Add(Figure(Raw(environment)));
                        return;
                    default:
                        if (!MathEnvironments.Contains(environment)) throw Unknown($"\\begin{{{environment}}}");
                        string body = Raw(environment);
                        Match id = Regex.Match(body, @"\\label\{([^}]*)\}");
                        if (id.Success) body = body.Remove(id.Index, id.Length);
                        if (environment.StartsWith("align", StringComparison.Ordinal)) body = "\\begin{aligned}" + body + "\\end{aligned}";
                        MathBlock maths = Maths(body, numbered: !environment.EndsWith("*", StringComparison.Ordinal) && environment != "displaymath");
                        maths.Id = id.Success ? id.Groups[1].Value.Trim() : null;
                        target.Add(maths);
                        return;
                }
                SkipSpace();
                if (Command(out int after) != "end") throw Error($"\\begin{{{environment}}} has no \\end{{{environment}}}.");
                pos = after;
                string closed = Group("end").Trim();
                if (closed != environment) throw Error($"\\begin{{{environment}}} is closed by \\end{{{closed}}}.");
            }

            /// <summary>What an environment holds, as written, moving past its <c>\end</c>.</summary>
            private string Raw(string environment)
            {
                string close = "\\end{" + environment + "}";
                int end = text.IndexOf(close, pos, StringComparison.Ordinal);
                if (end < 0) throw Error($"\\begin{{{environment}}} has no {close}.");
                string body = text.Substring(pos, end - pos);
                pos = end + close.Length;
                return body;
            }

            private static MathBlock Maths(string latex, bool numbered) =>
                new MathBlock { Latex = latex.Trim(), Node = MathParser.Parse(latex), Numbered = numbered };

            private static Figure Figure(string body)
            {
                Match image = Regex.Match(body, @"\\includegraphics\s*(?:\[([^\]]*)\])?\s*\{([^}]*)\}");
                if (!image.Success) throw Error("A figure needs its image: \\includegraphics{file.png}.");
                var figure = new Figure { Source = image.Groups[2].Value.Trim() };
                Match width = Regex.Match(image.Groups[1].Value, @"width\s*=\s*([\d.]*)\s*\\(?:text|line)width");
                if (width.Success)
                    figure.Width = width.Groups[1].Length == 0 ? 1 : double.Parse(width.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int caption = body.IndexOf("\\caption", StringComparison.Ordinal);
                if (caption >= 0)
                {
                    var reader = new Reader(body) { pos = caption + "\\caption".Length };
                    figure.Caption = Inlines(reader.Group("caption"));
                }
                Match id = Regex.Match(body, @"\\label\{([^}]*)\}");
                if (id.Success) figure.Id = id.Groups[1].Value.Trim();
                return figure;
            }

            /// <summary>Reads one piece of running text at the cursor into <paramref name="into"/>.</summary>
            private void Inline(List<Inline> into)
            {
                char c = text[pos];
                switch (c)
                {
                    case '%':
                        // A comment runs to the end of its line and takes the line break with it.
                        int line = text.IndexOf('\n', pos);
                        pos = line < 0 ? text.Length : line + 1;
                        return;
                    case '~':
                        pos++;
                        Add(into, "\u00A0");
                        return;
                    case '\n':
                        pos++;
                        SkipSpace(lines: false);
                        if (into.Count > 0) Add(into, " ");
                        return;
                    case '{':
                        into.AddRange(Inlines(Group("text")));
                        return;
                    case '}':
                        throw Error("A } has no { before it.");
                    case '$':
                        int close = text.IndexOf('$', pos + 1);
                        if (close < 0) throw Error("The maths opened with $ is never closed with $.");
                        into.Add(InlineMaths(text.Substring(pos + 1, close - pos - 1)));
                        pos = close + 1;
                        return;
                    case '`' when Starts("``"):
                        pos += 2;
                        Add(into, "\u201C");
                        return;
                    case '\'' when Starts("''"):
                        pos += 2;
                        Add(into, "\u201D");
                        return;
                    case '-' when Starts("---"):
                        pos += 3;
                        Add(into, "\u2014");
                        return;
                    case '-' when Starts("--"):
                        pos += 2;
                        Add(into, "\u2013");
                        return;
                    case '\\':
                        Backslash(into);
                        return;
                    default:
                        pos++;
                        Add(into, c.ToString());
                        return;
                }
            }

            private static MathInline InlineMaths(string latex) => new MathInline(latex.Trim(), MathParser.Parse(latex));

            private void Backslash(List<Inline> into)
            {
                string command = Command(out int after);
                if (command == null)
                {
                    char symbol = pos + 1 < text.Length ? text[pos + 1] : ' ';
                    pos = Math.Min(text.Length, pos + 2);
                    switch (symbol)
                    {
                        case '\\':
                            Option();
                            into.Add(new LineBreakInline());
                            return;
                        case '(':
                            int close = text.IndexOf("\\)", pos, StringComparison.Ordinal);
                            if (close < 0) throw Error("The maths opened with \\( is never closed with \\).");
                            into.Add(InlineMaths(text.Substring(pos, close - pos)));
                            pos = close + 2;
                            return;
                        case ' ' or ',' or ';' or '\n':
                            Add(into, " ");
                            return;
                        case '%' or '&' or '#' or '_' or '$' or '{' or '}':
                            Add(into, symbol.ToString());
                            return;
                        default:
                            throw Unknown("\\" + symbol);
                    }
                }

                pos = after;
                switch (command)
                {
                    case "textbf":
                        var strong = new StrongInline();
                        strong.Children.AddRange(Inlines(Group(command)));
                        into.Add(strong);
                        return;
                    case "textit" or "emph":
                        var emphasis = new EmphasisInline();
                        emphasis.Children.AddRange(Inlines(Group(command)));
                        into.Add(emphasis);
                        return;
                    case "texttt":
                        into.Add(new CodeInline(InlineParser.Plain(Inlines(Group(command)))));
                        return;
                    case "underline" or "textrm" or "textsf" or "mbox":
                        into.AddRange(Inlines(Group(command)));
                        return;
                    case "href":
                        var link = new LinkInline { Url = Group(command).Trim() };
                        link.Children.AddRange(Inlines(Group(command)));
                        into.Add(link);
                        return;
                    case "url":
                        string url = Group(command).Trim();
                        var plain = new LinkInline { Url = url };
                        plain.Children.Add(new TextInline(url));
                        into.Add(plain);
                        return;
                    case "cite" or "citep" or "citet":
                        var cite = new CiteInline();
                        cite.Keys.AddRange(Group(command).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        into.Add(cite);
                        return;
                    case "ref" or "eqref" or "autoref":
                        into.Add(new RefInline(Group(command).Trim()));
                        return;
                    case "footnote":
                        var note = new FootnoteInline();
                        note.Children.AddRange(Inlines(Group(command)));
                        into.Add(note);
                        return;
                    case "label":
                        label = Group(command).Trim();
                        return;
                }
                if (!Words.TryGetValue(command, out string word)) throw Unknown("\\" + command);
                // A command word takes the space after it, and an empty group ends it: \LaTeX{} is.
                if (Starts("{}")) pos += 2;
                else SkipSpace(lines: false);
                Add(into, word);
            }
        }
    }
}
