using KScript.Language.Formatting;
using KScript.Syntax.Text;

namespace KScript.Language.Tests
{
    /// <summary>The reader for ``` blocks and \ line joins, which the parser, the lexer and the formatter share.</summary>
    public class StringBlockTests
    {
        [Fact]
        public void ReadsTheTagTheValueAndWhereEachCharacterCameFrom()
        {
            string text = "x = ```sql  \r\n    SELECT 1\r\n\r\n      FROM t\r\n    ```)";
            StringBlock block = StringBlock.Read(text, 4);
            Assert.Null(block.Error);
            Assert.Equal("sql", block.Tag);
            Assert.Equal("sql", block.Language);
            Assert.Null(block.TypeName);
            Assert.Equal("SELECT 1\n\n  FROM t", block.Value);
            Assert.Equal(text.IndexOf("SELECT", StringComparison.Ordinal), block.Map[0]);
            Assert.Equal(text.Length - 1, block.End);
            Assert.Equal(')', text[block.End]);
        }

        [Fact]
        public void TypesAndTags()
        {
            Assert.Equal("Repo", StringBlock.Read("```< Repo >\n```", 0).TypeName);
            Assert.Null(StringBlock.Read("```< Repo >\n```", 0).Language);
            Assert.Equal(string.Empty, StringBlock.Read("```\n```", 0).Value);
            foreach (string language in StringBlock.Languages)
            {
                Assert.Null(StringBlock.Read("```" + language + "\nx\n```", 0).Error);
            }
            Assert.Null(StringBlock.Read("```<text>\n```", 0).Error);
        }

        [Fact]
        public void AFileExtensionCanNameAnOtherwiseUnknownFormat()
        {
            foreach (string tag in new[] { "ps1", "rb", "d.ts", "textc" })
            {
                StringBlock file = StringBlock.Read("```" + tag + "\nx\n```", 0);
                Assert.Null(file.Error);
                Assert.Equal(tag, file.Language);
                Assert.Equal(KScript.Metadata.KType.FormattedFile, StringBlock.ValueType(file.Tag));
            }
            foreach (string bad in new[] { "<>", "<1x>", "<a b>", "<a", "a b", "a!", "a..b", "../ps1", "/ps1" })
                Assert.NotNull(StringBlock.Read("```" + bad + "\nx\n```", 0).Error);
        }

        [Fact]
        public void OnlyThreeOrMoreBackticksStartABlock()
        {
            Assert.Null(StringBlock.Read("``x", 0));
            Assert.Null(StringBlock.Read("```", 0, 2));
            Assert.False(StringBlock.StartsAt("`", 0));
        }

        [Fact]
        public void ABackslashBeforeANewLineJoins()
        {
            Assert.Equal("ab", LineJoins.Join("a\\\n   b"));
            Assert.Equal("ab", LineJoins.Join("a\\\r\n\tb"));
            Assert.Equal("a\\ b", LineJoins.Join("a\\ b"));
            Assert.Equal("a\\", LineJoins.Join("a\\"));
            Assert.False(LineJoins.At("a", 0, 1, out _));
        }

        [Fact]
        public void ABlockMovesLeftWithItsStatement()
        {
            string input = "if true {\n      q = ```\n        a\n\n          b\n        ```\n}\n";
            FormatResult result = Formatter.Format(input, "test.kscript");
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("if true {\n  q = ```\n    a\n\n      b\n    ```\n}\n", result.Text);
        }
    
        [Fact]
        public void TheEditorSuggestsTheLanguagesAfterAFence()
        {
            var service = new KScript.Language.Features.LanguageService(files: KScript.Language.Analysis.EmptyFileSystem.Instance);
            (string text, int offset) = TestHelpers.WithCaret("q = ```te\u2038\n  x\n  ```\necho $q");
            var items = service.GetCompletions(service.Analyze(text, "test.kscript"), offset);
            Assert.Equal(StringBlock.Languages, items.Select(i => i.Label));
            Assert.Equal(offset - 2, items[0].ReplaceSpan.Start);
            Assert.Equal("plain text", items[0].Detail);
            (string xml, int at) = TestHelpers.WithCaret("<kscript><echo>```te\u2038</echo></kscript>");
            Assert.DoesNotContain(service.GetCompletions(service.Analyze(xml, "test.ks.xml"), at), i => i.Label == "javascript");
        }
        [Fact]
        public void LatexReturnTypeFlowsIntoCompletion()
        {
            var service = new KScript.Language.Features.LanguageService(files: KScript.Language.Analysis.EmptyFileSystem.Instance);
            const string code = """
                import KScript.Document

                fn paper() => ```latex
                  \section{Example}
                  ```

                echo @document(@paper())->latex()
                """;
            var analysis = service.Analyze(code, "test.kscript");
            Assert.Empty(analysis.Errors);
            Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == KScript.Syntax.DiagnosticCodes.TypeMismatch);
            Assert.Equal("latex", Assert.Single(analysis.Symbols, s => s.Name == "paper").ReturnShape.Type);
            (string text, int offset) = TestHelpers.WithCaret(code.Replace("@document(@paper())->latex()", "@paper()->‸"));
            var methods = service.GetCompletions(service.Analyze(text, "test.kscript"), offset).Select(item => item.Label).ToList();
            Assert.Contains("save", methods);
            Assert.Contains("replace", methods);
            Assert.DoesNotContain("to_pdf", methods);
        }
    }
}
