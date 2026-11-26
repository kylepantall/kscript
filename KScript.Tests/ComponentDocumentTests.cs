using KScript.Documents;
using KScript.Documents.Text;
using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>Components drawn in a document: by document elements, or by the Markdown or LaTeX their render returns.</summary>
public class ComponentDocumentTests
{
    private static string Code(string code) => Run(code, path: Path.Combine(Path.GetTempPath(), "component-document.kscript"));

    private static string Markdown(string components, string body) => Code($$"""
        import KScript.Document

        {{components}}

        Document(into: doc) {
        {{body}}
        }
        echo $doc->markdown()
        """).Replace("\r\n", "\n").Trim();

    [Fact]
    public void ElementsInARenderAddToTheDocumentAndSlotDrawsWhatTheCallerWrote()
    {
        string text = Markdown("""
            type Panel extends Component {
              construct({ heading: string })

              fn render() {
                Section(title: "{$heading}") {
                  slot
                }
              }
            }
            """, """
              Panel(heading: "Results") {
                Paragraph "Inside."
              }
            """);
        Assert.Equal("# 1 Results\n\nInside.", text);
    }

    [Fact]
    public void AMarkdownBlockIsReadIntoTheDocumentWithItsValues()
    {
        string text = Markdown("""
            type Finding extends Component {
              construct({ title: string, count: int })

              fn render() => ```md
                # {$title}

                We saw **{$count}** of them, and \( x^{2} \) holds.
                ```
            }
            """, """
              Section(title: "Results") {
                Finding(title: "Gliders", count: 3)
              }
            """);
        Assert.Equal("# 1 Results\n\n## 1.1 Gliders\n\nWe saw **3** of them, and $x^{2}$ holds.", text);
    }

    [Fact]
    public void ALatexBlockIsReadIntoTheDocumentAndItsValuesAreEscaped()
    {
        string text = Markdown("""
            type Method extends Component {
              construct({ name: string })

              fn render() {
                note = "50% & more"
                return ```latex
                  \section{Method: {$name}}\label{m}
                  We used \textbf{$name} with { $note }, see \ref{m} and $x^{2}$ {as written}.
                  ```
              }
            }
            """, """
              Method(name: "counting")
            """);
        Assert.Equal("# 1 Method: counting {#m}\n\nWe used **counting** with 50% & more, see [1](#m) and $x^{2}$ as written.", text);
    }

    [Fact]
    public void ARenderOfOnlyComponentsNamesTheDocument()
    {
        string text = Markdown("""
            type Line extends Component {
              construct({ text: string })

              // A block may name the place too, beside a render for another one.
              fn render(document) => ```md
                {$text}
                ```
            }

            type Pair extends Component {
              fn render(document) {
                Line(text: "One")
                Line(text: "Two")
              }
            }
            """, """
              Pair()
            """);
        Assert.Equal("One\n\nTwo", text);
    }

    [Fact]
    public void TheXmlFormDrawsTheSameDocument()
    {
        string output = Run("""
            <kscript>
              <import package="KScript.Document" />
              <type name="Line" extends="Component">
                <construct>
                  <field name="text" type="string" />
                </construct>
                <fn name="render">
                  <return value="```latex
                    \emph{$text}
                    ```" />
                </fn>
              </type>
              <Document into="doc">
                <Line text="Hello" />
              </Document>
              <echo>{ $doc->markdown() }</echo>
            </kscript>
            """);
        Assert.Equal("*Hello*", output.Trim());
    }

    [Fact]
    public void ADocumentBlockOnlyDrawsInADocument()
    {
        const string line = """
            import KScript.Document

            type Line extends Component {
              fn render() => ```md
                Hello
                ```
            }

            """;
        var outside = Assert.Throws<KScriptException>(() => Code(line + "Line()"));
        Assert.Contains("Line has no render for the terminal", outside.Message);

        var unknown = Assert.Throws<KScriptException>(() => Code("""
            import KScript.Document

            type Odd extends Component {
              fn render() => ```latex
                \frobnicate{x}
                ```
            }

            Document(into: doc) {
              Odd()
            }
            """));
        Assert.Contains("\\frobnicate isn't LaTeX a document part reads", unknown.Message);
        Assert.Equal(10, unknown.Line);
    }

    private static string Read(string latex) =>
        KScript.Documents.Export.MarkdownWriter.Write(Document(LatexBlocks.Parse(latex)), null).Replace("\r\n", "\n").Trim();

    private static Document Document(List<Block> blocks)
    {
        var document = new Document();
        document.Blocks.AddRange(blocks);
        return document;
    }

    private static string Fails(string latex) => Assert.Throws<KScriptException>(() => LatexBlocks.Parse(latex)).Message;

    [Fact]
    public void LatexSectionsNestAndStarredOnesHaveNoNumber()
    {
        var blocks = LatexBlocks.Parse("\\section{One}\nText.\n\\subsection{Inner}\n\\subsubsection{Deep}\n\\section*{Two}\n\\label{two}\nMore.");
        var one = Assert.IsType<Section>(blocks[0]);
        var inner = Assert.IsType<Section>(one.Blocks[1]);
        Assert.IsType<Section>(Assert.Single(inner.Blocks));
        var two = Assert.IsType<Section>(blocks[1]);
        Assert.True(one.Numbered);
        Assert.False(two.Numbered);
        Assert.Equal("two", two.Id);
        Assert.Empty(LatexBlocks.Parse(null));
    }

    [Fact]
    public void LatexParagraphsBreakAtBlankLinesParAndComments()
    {
        var blocks = LatexBlocks.Parse("First line\ncarries on. % not shown\nStill first.\n\n  Second\\par Third \\label{p}\r\n\n% only a comment");
        Assert.Equal(3, blocks.Count);
        Assert.Equal("First line carries on. Still first.", InlineParser.Plain(((Paragraph)blocks[0]).Inlines));
        Assert.Equal("Second", InlineParser.Plain(((Paragraph)blocks[1]).Inlines));
        Assert.Equal("p", blocks[2].Id);
    }

    [Fact]
    public void LatexTextCommandsBecomeInlineText()
    {
        Assert.Equal("**bold** *it* *em* `code` plain sans [site](https://example.org) [https://example.org](https://example.org)",
            Read("\\textbf{bold} \\textit{it} \\emph{em} \\texttt{code} \\underline{plain} \\textsf{sans} \\href{https://example.org}{site} \\url{https://example.org}"));
        var inlines = ((Paragraph)LatexBlocks.Parse("A~b ``q'' 1--2 x---y 50\\% \\& \\# \\_ \\$ \\{ \\} a\\ b\\,c \\LaTeX{} and \\TeX is \\ldots \\noindent{}here\\\\[2pt]next \\( a+b \\) $c$")[0]).Inlines;
        string plain = string.Concat(inlines.Select(i => i switch { TextInline t => t.Text, LineBreakInline => "|", MathInline m => "<" + m.Latex + ">", _ => "?" }));
        Assert.Equal("A\u00A0b \u201Cq\u201D 1\u20132 x\u2014y 50% & # _ $ { } a b c LaTeX and TeXis \u2026here|next <a+b> <c>", plain);

        var notes = ((Paragraph)LatexBlocks.Parse("See \\cite{a, b} and \\ref{fig}\\footnote{A \\emph{note}.}")[0]).Inlines;
        Assert.Equal(new[] { "a", "b" }, notes.OfType<CiteInline>().Single().Keys);
        Assert.Equal("fig", notes.OfType<RefInline>().Single().Id);
        Assert.Single(notes.OfType<FootnoteInline>().Single().Children.OfType<EmphasisInline>());
    }

    [Fact]
    public void LatexEnvironmentsBecomeBlocks()
    {
        var blocks = LatexBlocks.Parse("""
            \documentclass[11pt]{article}
            \usepackage{amsmath}
            \begin{document}
            \tableofcontents
            \begin{enumerate}
              \item One
              \begin{itemize}
                \item Nested
              \end{itemize}
              \item Two

              With more.
            \end{enumerate}
            \begin{quote}
              Said.
            \end{quote}
            \begin{center}
              Middle.
              \hrule
            \end{center}
            \begin{verbatim}
            x = {1}
            \end{verbatim}
            \begin{figure}
              \includegraphics[width=0.5\textwidth]{glider.png}
              \caption{A \textbf{glider}}\label{fig}
            \end{figure}
            \begin{figure}\includegraphics[width=\linewidth]{wide.png}\end{figure}
            \begin{figure}\includegraphics{plain.png}\end{figure}
            \newpage
            \bigskip
            \end{document}
            """);
        Assert.IsType<Contents>(blocks[0]);
        var list = Assert.IsType<ListBlock>(blocks[1]);
        Assert.Equal(ListStyle.Number, list.Style);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(ListStyle.Bullet, Assert.IsType<ListBlock>(list.Items[0].Blocks[1]).Style);
        Assert.Equal(2, list.Items[1].Blocks.Count);
        Assert.Single(Assert.IsType<Quote>(blocks[2]).Blocks);
        Assert.Equal(PaperAlign.Center, Assert.IsType<Paragraph>(blocks[3]).Align);
        Assert.IsType<Rule>(blocks[4]);
        Assert.Equal("x = {1}", Assert.IsType<CodeBlock>(blocks[5]).Text);
        var figure = Assert.IsType<Figure>(blocks[6]);
        Assert.Equal(("glider.png", 0.5, "fig"), (figure.Source, figure.Width, figure.Id));
        Assert.Equal("A glider", InlineParser.Plain(figure.Caption));
        Assert.Equal(1, Assert.IsType<Figure>(blocks[7]).Width);
        Assert.Equal(0, Assert.IsType<Figure>(blocks[8]).Width);
        Assert.IsType<PageBreak>(blocks[9]);
        Assert.Equal(12, Assert.IsType<Spacer>(blocks[10]).Height);
    }

    [Fact]
    public void LatexMathsBecomesEquations()
    {
        var blocks = LatexBlocks.Parse("""
            Before \[ a = b \] after
            $$ c = d $$
            \begin{equation}\label{eq}
              N = 1
            \end{equation}
            \begin{equation*}
              M = 2
            \end{equation*}
            \begin{align}
              x &= 1 \\
              y &= 2
            \end{align}
            """);
        var maths = blocks.OfType<MathBlock>().ToList();
        Assert.Equal(5, maths.Count);
        Assert.Equal(new[] { false, false, true, false, true }, maths.Select(m => m.Numbered));
        Assert.Equal("eq", maths[2].Id);
        Assert.Equal("N = 1", maths[2].Latex);
        Assert.StartsWith("\\begin{aligned}", maths[4].Latex);
        Assert.Equal(2, blocks.OfType<Paragraph>().Count());
    }

    [Fact]
    public void LatexThatIsNotReadIsAnErrorThatNamesIt()
    {
        Assert.Contains("\\begin{tabular} isn't LaTeX a document part reads", Fails("\\begin{tabular}{ll}\\end{tabular}"));
        Assert.Contains("\\^ isn't LaTeX", Fails("caf\\^e"));
        Assert.Contains("\\item only works inside", Fails("\\item Alone"));
        Assert.Contains("\\begin{quote} has no \\end{quote}", Fails("\\begin{quote} Said."));
        Assert.Contains("\\begin{itemize} has no \\end{itemize}", Fails("\\begin{itemize} \\item One"));
        Assert.Contains("\\begin{itemize} has no \\end{itemize}", Fails("\\begin{itemize} Loose"));
        Assert.Contains("\\begin{quote} is closed by \\end{center}", Fails("\\begin{quote} Said. \\end{center}"));
        Assert.Contains("\\begin{verbatim} has no \\end{verbatim}", Fails("\\begin{verbatim} x"));
        Assert.Contains("needs its image", Fails("\\begin{figure}\\caption{None}\\end{figure}"));
        Assert.Contains("\\textbf needs its text in braces", Fails("\\textbf bold"));
        Assert.Contains("\\textbf{ is never closed", Fails("\\textbf{bold \\} "));
        Assert.Contains("A } has no { before it", Fails("loose }"));
        Assert.Contains("opened with $ is never closed", Fails("costs $5"));
        Assert.Contains("opened with \\( is never closed", Fails("\\( a"));
        Assert.Contains("opened with \\[ is never closed", Fails("\\[ a"));
        Assert.Contains("opened with $$ is never closed", Fails("$$ a"));
    }
}
