using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Tests;

/// <summary>
/// Long strings in .kscript: a \ at the end of a line joins it to the next, and ``` blocks hold text over several
/// lines (with a language, or a &lt;Type&gt; that parses them).
/// </summary>
public class MultilineStringTests
{
    private static string Run(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "test.kscript"));

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static IReadOnlyList<Diagnostic> Errors(string code) =>
        CodeParser.Parse(code).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    [Fact]
    public void ABackslashAtTheEndOfALineJoinsText()
    {
        Assert.Equal("Hello, Kyle, and welcome.\n", Run(Lines(
            "name = 'Kyle'",
            "echo \"Hello, \\",
            "      { $name }, and \\",
            "\twelcome.\"")));
    }

    [Fact]
    public void ABackslashAtTheEndOfALineJoinsExpressionStrings()
    {
        Assert.Equal("abc def!\n", Run(Lines(
            "x = 'abc \\",
            "     def' + '!'",
            "echo $x")));
        // A Windows line ending joins too.
        Assert.Equal("ab\n", Run("x = 'a\\\r\n   b'\r\necho $x\r\n"));
    }

    [Fact]
    public void OtherBackslashesAreKept()
    {
        // \\ is a backslash of your own, so the new line after it stays; \( and a \ before a space are text.
        Assert.Equal("a\\\nb \\( x \\) c\\ d\n", Run(Lines(
            "echo \"a\\\\",
            "b \\( x \\) c\\ d\"")));
        // A \ followed by spaces before the new line isn't a join.
        Assert.Equal("a\\ \nb\n", Run("x = 'a\\ \nb'\necho $x\n"));
    }

    [Fact]
    public void ABlockIsTextWithTheClosingFencesIndentationRemoved()
    {
        Assert.Equal("Hi Kyle,\n  indented\n\nshipped.\n", Run(Lines(
            "name = 'Kyle'",
            "if true {",
            "  echo ```",
            "      Hi { $name },",
            "        indented",
            "",
            "      shipped.",
            "      ```",
            "}")));
    }

    [Fact]
    public void ALanguageTagKeepsTheBlockAsAString()
    {
        Assert.Equal("SELECT *\n  FROM users|\n", Run(Lines(
            "query = ```sql",
            "  SELECT *",
            "    FROM users",
            "  ```",
            "echo $query + '|'")));
    }

    [Fact]
    public void FourBackticksHoldThree()
    {
        Assert.Equal("```\ncode\n```\n", Run(Lines(
            "x = ````md",
            "  ```",
            "  code",
            "  ```",
            "  ````",
            "echo $x")));
    }

    [Fact]
    public void CodeCanCarryOnAfterTheClosingFence()
    {
        Assert.Equal("INSIDE\n", Run(Lines(
            "msg = (```",
            "    inside",
            "    ```)->upper()",
            "echo $msg")));
    }

    [Fact]
    public void AJsonTagParsesTheBlock()
    {
        Assert.Equal("3 2\n", Run(Lines(
            "data = ```<json>",
            "  { \"items\": [1, 2, 3],",
            "    \"name\": \"two\" }",
            "  ```",
            "echo \"{ $data.items->count() } { $data.items[1] }\"")));
    }

    [Fact]
    public void ATypeTagMapsTheBlockOntoTheType()
    {
        string type = Lines("type Repo {", "  name: string", "  stars: int", "}");
        Assert.Equal("kscript 12\n", Run(type + Lines(
            "repo = ```<Repo>",
            "    { \"name\": \"kscript\", \"stars\": 12 }",
            "    ```",
            "echo \"{ $repo.name } { $repo.stars }\"")));

        var error = Assert.Throws<KScriptException>(() => Run(type + Lines(
            "repo = ```<Repo>",
            "    { \"name\": \"kscript\" }",
            "    ```")));
        Assert.Contains("stars", error.Message);
    }

    [Fact]
    public void ATextOrStringTagIsJustTheText()
    {
        Assert.Equal("{ \"a\": 1 }\n", Run(Lines("x = ```<text>", "  { \"a\": 1 }", "  ```", "echo $x")));
    }

    [Fact]
    public void MistakesInBlocksAreReported()
    {
        // As an element's text, the parser reports them.
        Assert.Contains(Errors(Lines("echo ```", "  a")), d => d.Message.Contains("missing its closing ```"));
        string shallow = Lines("echo ```", "    a", "  b", "    ```");
        Diagnostic less = Assert.Single(Errors(shallow));
        Assert.Contains("indented less than the closing fence", less.Message);
        Assert.Equal(2, CodeParser.Parse(shallow).Source.GetPosition(less.Span.Start).Line);

        // In an expression, the expression parser does.
        Assert.Contains("missing its closing ```", Fails(Lines("x = ```", "  a")));
        Assert.Contains("indented less than the closing fence", Fails(Lines("x = ```", "    a", "  b", "    ```")));
        Assert.Contains("isn't a language or a <Type>", Fails(Lines("x = ```sql query", "  a", "  ```")));
        Assert.Contains("closing fence on a line of its own", Fails("x = ```a```\n"));
    }

    private static string Fails(string code) => Assert.ThrowsAny<Exception>(() => Run(code)).Message;

    [Fact]
    public void TheEditorSeesTheTextOfABlock()
    {
        MarkupDocument doc = CodeParser.Parse(Lines("echo ```", "  Hi $name", "  ```"));
        MarkupElement echo = doc.DescendantElements().Single(e => e.Name == "echo");
        MarkupText text = echo.Children.OfType<MarkupText>().Single();
        Assert.Equal("Hi $name", text.Value);
        // Each character of the value maps back to where it is written, so completion and hover line up.
        Assert.Equal(doc.Source.Text.IndexOf("$name", StringComparison.Ordinal), text.Content.ToSource(3));
    }

    [Fact]
    public void ConvertingToXmlAndBackKeepsWhatAScriptDoes()
    {
        string code = Lines(
            "name = 'Kyle'",
            "echo \"Hello, \\",
            "  { $name }.\"",
            "q = ```sql",
            "    SELECT 1",
            "      FROM t",
            "    ```",
            "echo $q",
            "echo ```",
            "  Bye { $name }",
            "  ```");
        string xml = ScriptConverter.Convert(code, "x.kscript").Text;
        Assert.Equal(Run(code), ScriptRunner.Run(xml));
        Assert.Equal(Run(code), Run(ScriptConverter.Convert(xml, "x.ks.xml").Text));
    }
}

/// <summary>A paper written with joined lines and ```latex blocks, cut down from a real one.</summary>
public sealed class MultilineDocumentTests : DocumentFixture
{
    [Fact]
    public void JoinedLinesAndLatexBlocksBuildAPaper()
    {
        Run("""
            import KScript.Document

            Document(template: "paper", title: "Reaction Rates", to: ['rates.tex', 'rates.md']) {
              Abstract "Most chemical reactions speed up as they get warmer. \
                This paper reviews the rate law, \( \mathrm{A} \rightarrow \text{products} \), \
                and the Arrhenius equation."

              Section(title: "The Arrhenius equation", id: "arrhenius") {
                Paragraph "\( R = 8.314\ \mathrm{J\,mol^{-1}\,K^{-1}} \) is the gas constant, and \
                  Equation [#eq-ratio] follows from it."

                Math(id: "eq-ratio") ```latex
                  \ln\frac{k_2}{k_1}
                    = \frac{E_\mathrm{a}}{R}
                      \left(\frac{1}{T_1} - \frac{1}{T_2}\right)
                  ```
              }
            }
            """);
        string tex = Read("rates.tex");
        // Joined lines are one paragraph, with LaTeX's own backslashes kept.
        Assert.Contains("Most chemical reactions speed up as they get warmer. This paper reviews the rate law, $\\mathrm{A}\\rightarrow\\text{products}$, and the Arrhenius equation.", tex);
        Assert.Contains("$R=8.314\\ \\mathrm{J\\,mol^{-1}\\,K^{-1}}$ is the gas constant, and Equation \\eqref{eq-ratio} follows from it.", tex);
        // Math keeps its braces as LaTeX instead of reading { } as KScript.
        Assert.Contains("\\ln\\frac{k_{2}}{k_{1}}=\\frac{E_{\\mathrm{a}}}{R}\\left( \\frac{1}{T_{1}}-\\frac{1}{T_{2}} \\right)", tex);
    }
}
