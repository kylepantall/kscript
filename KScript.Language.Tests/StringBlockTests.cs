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
        public void OnlyKnownLanguagesCanBeNamed()
        {
            StringBlock typo = StringBlock.Read("```textc\nx\n```", 0);
            Assert.StartsWith("'textc' isn't a language a ``` block can name. Did you mean text?", typo.Error);
            Assert.Equal(3, typo.ErrorSpan.Start);
            Assert.Equal(5, typo.ErrorSpan.Length);
            Assert.Contains("Did you mean yaml?", StringBlock.Read("```yaml2\nx\n```", 0).Error);
            Assert.Contains("Did you mean html?", StringBlock.Read("```htnl\nx\n```", 0).Error);
            string other = StringBlock.Read("```c++\nx\n```", 0).Error;
            Assert.DoesNotContain("Did you mean", other);
            Assert.Contains("Use text, sql, html", other);
            foreach (string bad in new[] { "<>", "<1x>", "<a b>", "<a", "a b", "a!" })
            {
                Assert.NotNull(StringBlock.Read("```" + bad + "\nx\n```", 0).Error);
            }
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
    }
}
