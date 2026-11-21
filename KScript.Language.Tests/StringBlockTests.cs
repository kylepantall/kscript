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
            Assert.Null(StringBlock.Read("```c++\n```", 0).Error);
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
    }
}
