using KScript.Language.Formatting;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>
    /// The .kscript formatter. Every case is checked three ways: the output is what we expect, formatting it again
    /// changes nothing, and the parser reads the same program from both (so formatting never changes meaning).
    /// </summary>
    public class CodeFormatterTests
    {
        private static string Fmt(string text, FormatOptions options = null)
        {
            FormatResult result = Formatter.Format(text, "test.kscript", options);
            Assert.True(result.Succeeded, result.Error);
            AssertSame(text, result.Text);
            FormatResult again = Formatter.Format(result.Text, "test.kscript", options);
            Assert.True(again.Succeeded, again.Error);
            Assert.Equal(result.Text, again.Text);
            return result.Text;
        }

        private static void AssertSame(string before, string after)
        {
            bool same = CodeMeaning.Same(CodeParser.Parse(before.TrimStart('﻿')), CodeParser.Parse(after), out string difference);
            Assert.True(same, difference);
        }

        private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

        private static FormatOptions Without(Action<FormatOptions> off)
        {
            FormatOptions o = new FormatOptions();
            off(o);
            return o;
        }

        [Fact]
        public void ARegexAtTheEndOfALineDoesntContinueIt()
        {
            string code = Lines("type Sku with [string] {", "  pattern: /^[A-Z]{3}-\\d{4}$/", "  min: 1", "}",
                "r = /a\\//", "x = 1", "half = $x /", "  2", "both = $x / 2 /", "  3", "f = fn() => [/a/, /b/]", "list = [", "  /a/", "]", "fn re() {", "  return /x/", "}", "y = 2");
            Assert.Equal(code, Fmt(code));
            Assert.Equal(code, Fmt(code.Replace("\n  min: 1", "\n      min: 1")));
        }

        [Fact]
        public void IndentsBlocksByNesting()
        {
            string input = "x = 1\nif $x > 0 {\n      for i in 1..3 {\necho $i\n}\n}\n";
            Assert.Equal(Lines(
                "x = 1",
                "if $x > 0 {",
                "  for i in 1..3 {",
                "    echo $i",
                "  }",
                "}"), Fmt(input));
        }

        [Fact]
        public void UsesTabsAndIndentSize()
        {
            string input = "if yes {\necho 1\n}\n";
            Assert.Equal("if yes {\n    echo 1\n}\n", Fmt(input, new FormatOptions { IndentSize = 4 }));
            Assert.Equal("if yes {\n\techo 1\n}\n", Fmt(input, new FormatOptions { UseTabs = true }));
        }

        [Fact]
        public void OpensOneLineBlocksAndSplitsStatements()
        {
            Assert.Equal(Lines(
                "for i in 1..3 {",
                "  echo $i",
                "}",
                "a = 1",
                "b = 2"), Fmt("for i in 1..3 { echo $i }\na = 1; b = 2;\n"));
        }

        [Fact]
        public void StatementPerLineOffKeepsLines()
        {
            string input = "for i in 1..3 { echo $i }\na = 1; b = 2\n";
            Assert.Equal(input, Fmt(input, Without(o => o.StatementPerLine = false)));
        }

        [Fact]
        public void JoinsElseOntoTheClosingBrace()
        {
            string input = "if $a {\necho 1\n}\nelse {\necho 2\n}\ntry {\nx()\n}\ncatch {\necho 3\n}\n";
            Assert.Equal(Lines(
                "if $a {",
                "  echo 1",
                "} else {",
                "  echo 2",
                "}",
                "try {",
                "  x()",
                "} catch {",
                "  echo 3",
                "}"), Fmt(input));
            Assert.Contains("}\nelse {", Fmt(input, Without(o => o.BraceStyle = false)));
        }

        [Fact]
        public void TidiesSpacing()
        {
            Assert.Equal(Lines(
                "x = add(1, 2)",
                "m = { a: 1, b: [1, 2] }",
                "f = (t) => $t",
                "if $x > 1 {",
                "  echo $x // big",
                "}"), Fmt("x=add(1 ,2)\nm = {a:1,b:[1,2]}\nf = (t)=>$t\nif $x > 1{\n  echo $x// big\n}\n"));
        }

        [Fact]
        public void SpacingOffKeepsLines()
        {
            string input = "x=add(1 ,2)\nm = {a:1}\n";
            Assert.Equal(input, Fmt(input, Without(o => o.Spacing = false)));
        }

        [Fact]
        public void KeepsStringsExactly()
        {
            string input = "echo \"a  ,b  {  }   ;  x=1\"\necho 'it''s  :  here'\ns = \"line one\n   line two\"\n";
            string output = Fmt(input);
            Assert.Contains("\"a  ,b  {  }   ;  x=1\"", output);
            Assert.Contains("   line two\"", output);
        }

        [Fact]
        public void KeepsCommentsAndDocComments()
        {
            string input = "/// Says hello.\nfn hi(name) {\n// inside\n/* a\n   block */\necho \"hi $name\"\n}\n";
            Assert.Equal(Lines(
                "/// Says hello.",
                "fn hi(name) {",
                "  // inside",
                "  /* a",
                "     block */",
                "  echo \"hi $name\"",
                "}"), Fmt(input));
        }

        [Fact]
        public void CollapsesBlankLines()
        {
            string input = "a = 1\n\n\n\nb = 2\nif yes {\n\necho 1\n\n}\n\n\n";
            Assert.Equal("a = 1\n\nb = 2\nif yes {\n  echo 1\n}\n", Fmt(input));
            Assert.Equal("a = 1\n\n\nb = 2\nif yes {\n  echo 1\n}\n", Fmt(input, new FormatOptions { MaxBlankLines = 2 }));
        }

        [Fact]
        public void TrimsTrailingWhitespaceAndEndsWithANewline()
        {
            Assert.Equal("a = 1\nb = 2\n", Fmt("a = 1   \nb = 2"));
            Assert.Equal("a = 1\nb = 2", Fmt("a = 1\nb = 2", Without(o => o.FinalNewline = false)));
            Assert.Equal("a = 1   \n", Fmt("a = 1   \n", Without(o => o.TrimTrailingWhitespace = false)));
        }

        [Fact]
        public void IndentsContinuationLines()
        {
            string input = "total = $a +\n$b\nlist = [1, 2]\n|> map((n) => $n * 2)\n";
            Assert.Equal(Lines(
                "total = $a +",
                "  $b",
                "list = [1, 2]",
                "  |> map((n) => $n * 2)"), Fmt(input));
        }

        [Fact]
        public void IndentsArgumentListsAndArrays()
        {
            string input = "x = call(\n1,\n2\n)\nlist = [\n1,\n2,\n]\n";
            Assert.Equal(Lines(
                "x = call(",
                "  1,",
                "  2",
                ")",
                "list = [",
                "  1,",
                "  2,",
                "]"), Fmt(input));
        }

        [Fact]
        public void WrapsLongArgumentLists()
        {
            string input = "x = call(\"" + new string('a', 50) + "\", \"" + new string('b', 50) + "\", \"" + new string('c', 30) + "\")\n";
            string output = Fmt(input);
            Assert.StartsWith("x = call(\n  \"aaa", output);
            Assert.Contains("\n  \"" + new string('c', 30) + "\"\n)\n", output);
            Assert.Equal(input, Fmt(input, Without(o => o.WrapAttributes = false)));
        }

        [Fact]
        public void LeavesMatchValuesOnTheirLine()
        {
            string input = "label = match $n { 1 => \"one\", _ => \"many\" }\n";
            Assert.Equal(input, Fmt(input));
        }

        [Fact]
        public void IndentsTypeAndEnumBlocks()
        {
            string input = "type User {\nname: string\nage: int\n}\nenum Colour { red, green }\n";
            Assert.Equal(Lines(
                "type User {",
                "  name: string",
                "  age: int",
                "}",
                "enum Colour { red, green }"), Fmt(input));
        }

        [Fact]
        public void IndentOffKeepsIndentation()
        {
            string input = "if yes {\n      echo 1\n}\n";
            Assert.Equal(input, Fmt(input, Without(o => o.Indent = false)));
        }

        [Fact]
        public void OffAndOnCommentsKeepARegion()
        {
            string input = "a=1\n// kscript-fmt: off\nb   =   2\nif yes {   echo 1 }\n// kscript-fmt: on\nc=3\n";
            Assert.Equal(Lines(
                "a = 1",
                "// kscript-fmt: off",
                "b   =   2",
                "if yes {   echo 1 }",
                "// kscript-fmt: on",
                "c = 3"), Fmt(input));
        }

        [Fact]
        public void OffRegionEndsWithItsBlock()
        {
            string input = "if yes {\n// kscript-fmt: off\nb   =   2\n}\nc=3\n";
            string output = Fmt(input);
            Assert.Contains("\nb   =   2\n", output);
            Assert.EndsWith("}\nc = 3\n", output);
        }

        [Fact]
        public void IgnoreCommentKeepsTheNextStatement()
        {
            string input = "// kscript-fmt-ignore\ngrid = [1,0,\n        0,1]\nx=1\n";
            Assert.Equal(Lines(
                "// kscript-fmt-ignore",
                "grid = [1,0,",
                "        0,1]",
                "x = 1"), Fmt(input));
        }

        [Fact]
        public void KeepsLineEndings()
        {
            Assert.Equal("if yes {\r\n  echo 1\r\n}\r\n", Fmt("if yes {\r\necho 1\r\n}\r\n"));
            Assert.Equal("a = 1\r\n", Fmt("a = 1\n", new FormatOptions { EndOfLine = EndOfLine.CrLf }));
        }

        [Fact]
        public void FilesWithErrorsAreLeftAlone()
        {
            FormatResult result = Formatter.Format("if yes {\necho 1\n", "test.kscript");
            Assert.False(result.Succeeded);
            Assert.StartsWith("not formatted", result.Error);
        }

        [Fact]
        public void TheMeaningCheckSpotsRealChanges()
        {
            Assert.False(CodeMeaning.Same(CodeParser.Parse("echo \"a b\"\n"), CodeParser.Parse("echo \"a  b\"\n"), out _));
            Assert.False(CodeMeaning.Same(CodeParser.Parse("x = a b\n"), CodeParser.Parse("x = ab\n"), out _));
            Assert.False(CodeMeaning.Same(CodeParser.Parse("a = 1\nb = 2\n"), CodeParser.Parse("a = 1\n"), out _));
            Assert.False(CodeMeaning.Same(CodeParser.Parse("// one\n"), CodeParser.Parse("// two\n"), out _));
            Assert.True(CodeMeaning.Same(CodeParser.Parse("x=add(1 ,2)\n\n"), CodeParser.Parse("x = add(1, 2)\n"), out _));
        }

        [Fact]
        public void CoversTheRarerCases()
        {
            // Escapes in strings, and a string before a comparison (not an assignment) on a block's line.
            Assert.Equal("if 'a\\'b' == $x {\n  echo \"q\\\"q\"\n}\n", Fmt("if 'a\\'b' == $x {\necho \"q\\\"q\"\n}\n"));
            // ??= is an assignment.
            Assert.Equal("x ??= 1\n", Fmt("x??=1\n"));
            // An = after echo is text, not an assignment.
            Assert.Equal("echo x=1\n", Fmt("echo x=1\n"));
            // A comment that opens and closes on one line.
            Assert.Equal("x = 1 /* one */\n/* two\n   three */\n", Fmt("x = 1 /* one */\n/* two\n   three */\n"));
            // With finalNewline off, the file keeps its own ending (shrunk to maxBlankLines).
            Assert.Equal("a = 1\n\n", Fmt("a = 1\n\n\n\n", Without(o => o.FinalNewline = false)));
            // An explicit lf turns CRLF into LF.
            Assert.Equal("a = 1\n", Fmt("a = 1\r\n", new FormatOptions { EndOfLine = EndOfLine.Lf }));
            // Brackets inside a wrapped list stay with their item.
            string input = "x = call(inner(1, 2), \"" + new string('a', 60) + "\", \"" + new string('b', 50) + "\")\n";
            Assert.StartsWith("x = call(\n  inner(1, 2),\n", Fmt(input));
        }

        [Fact]
        public void IgnoreCommentSkipsBlankLinesAndComments()
        {
            string input = "// kscript-fmt-ignore\n\n// why\ngrid = [1,0]\nx=1\n";
            Assert.Equal("// kscript-fmt-ignore\n\n// why\ngrid = [1,0]\nx = 1\n", Fmt(input));
            Assert.Equal("x = 1\n// kscript-fmt-ignore\n", Fmt("x=1\n// kscript-fmt-ignore\n"));
            Assert.Equal("// kscript-fmt-ignore\n\ngrid = [1,0]\n", Fmt("// kscript-fmt-ignore\n   \ngrid = [1,0]\n"));
        }

        [Fact]
        public void AResultThatMeansSomethingElseIsNotUsed()
        {
            CodeFormatter.Tamper = t => t.Replace("1", "2");
            try
            {
                FormatResult result = Formatter.Format("x = 1\n", "test.kscript");
                Assert.False(result.Succeeded);
                Assert.Equal("not formatted: tidying this file would change what it does (text changed at line 1). Please report it.", result.Error);
            }
            finally
            {
                CodeFormatter.Tamper = null;
            }
        }

        [Fact]
        public void TheMeaningCheckNamesWhatChanged()
        {
            static string Diff(string a, string b)
            {
                Assert.False(CodeMeaning.Same(CodeParser.Parse(a), CodeParser.Parse(b), out string d));
                return d;
            }
            Assert.StartsWith("the result has an error", Diff("a = 1\n", "if yes {\n"));
            Assert.Equal("<if> became <while> at line 1", Diff("if yes {\n}\n", "while yes {\n}\n"));
            Assert.StartsWith("the attributes of", Diff("Text(size: 1) \"a\"\n", "Text(size: 1, wrap) \"a\"\n"));
            Assert.EndsWith("size changed at line 1", Diff("Text(size: 1) \"a\"\n", "Text(size: 2) \"a\"\n"));
            Assert.Equal("a statement changed kind at line 1", Diff("// note\n", "x = 1\n"));
        }

        // ---- every .kscript file in the repository ----------------------------------------------------------------

        public static IEnumerable<object[]> RepositoryFiles()
        {
            string root = FormatterTests.RepoRoot();
            foreach (string f in Directory.EnumerateFiles(root, "*.kscript", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("node_modules") || rel.StartsWith(".git/")) continue;
                yield return new object[] { rel };
            }
        }

        [Theory]
        [MemberData(nameof(RepositoryFiles))]
        public void RepositoryFilesFormatSafelyAndStably(string relative)
        {
            string path = Path.Combine(FormatterTests.RepoRoot(), relative);
            string text = File.ReadAllText(path);
            FormatResult result = Formatter.Format(text, path);
            if (!result.Succeeded)
            {
                // `kscript make` templates hold {{ }} placeholders; anything else must format.
                Assert.Contains("{{", text);
                return;
            }
            AssertSame(text, result.Text);
            Assert.Equal(result.Text, Formatter.Format(result.Text, path).Text);
        }
    }
}
