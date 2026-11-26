namespace KScript.Tests;

/// <summary>Markdown fences have the document outputs; LaTeX fences are saved as .tex and not rendered (#187).</summary>
public sealed class FenceDocumentMethodTests : DocumentFixture
{
    private const string Fences = """
        fn notes() => ```md
          # Notes
          Ship *soon*.
          ```
        fn paper() => ```latex
          \section{Draft}
          ```

        """;

    [Fact]
    public void A_markdown_fence_renders_like_a_document()
    {
        string output = Run(Fences + """
            echo @notes()->html()->contains('<em>soon</em>')
            echo @notes()->latex()->contains('\emph{soon}')
            echo @notes()->words()
            echo @notes()->pages()
            @notes()->save('notes.pdf')
            @notes()->save('notes.docx')
            @notes()->save('notes')
            @notes()->save('page', as: html)
            """);
        Assert.Equal("true\ntrue\n3\n1\n", output);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(PathOf("notes.pdf")), 0, 5));
        Assert.True(File.Exists(PathOf("notes.docx")));
        Assert.Equal("# Notes\nShip *soon*.", Read("notes.md"));
        Assert.Contains("<em>soon</em>", Read("page.html"));
    }

    [Fact]
    public void A_latex_fence_saves_its_source_and_is_not_rendered()
    {
        Run(Fences + "@paper()->replace('Draft', 'Final')->save('paper')\n@paper()->save('copy.tex')");
        Assert.Equal("\\section{Final}", Read("paper.tex"));
        Assert.Equal("\\section{Draft}", Read("copy.tex"));
        Assert.Contains("LaTeX source can be edited or saved as LaTeX", Fails(Fences + "@paper()->save('paper.pdf')").Message);
        Assert.Contains("no method ->html()", Fails(Fences + "@paper()->html()").Message);
    }
}
