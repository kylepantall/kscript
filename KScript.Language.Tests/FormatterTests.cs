using System.Text;
using System.Text.RegularExpressions;

using KScript.Language.Formatting;
using KScript.LanguageServer;
using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    public class FormatterTests
    {
        private static string Fmt(string text, string path = "test.ks.xml", FormatOptions options = null)
        {
            FormatResult result = Formatter.Format(text, path, options);
            Assert.True(result.Succeeded, result.Error);
            AssertSameMeaning(text, result.Text, MarkupParser.KindFromPath(path));
            Assert.Equal(result.Text, Formatter.Format(result.Text, path, options).Text);
            return result.Text;
        }

        private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

        // ---- scripts ----------------------------------------------------------------------------------------------

        [Fact]
        public void IndentsStatementsByNesting()
        {
            string input = "<kscript>\n<let name=\"x\" value=\"1\"/>\n      <if test=\"$x gt 0\"><echo>big</echo>\n</if>\n</kscript>";
            Assert.Equal(Lines(
                "<kscript>",
                "  <let name=\"x\" value=\"1\" />",
                "  <if test=\"$x gt 0\">",
                "    <echo>big</echo>",
                "  </if>",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void KeepsOneBlankLineBetweenStatementsAndTrimsTrailingSpaces()
        {
            string input = "<kscript>   \n  <let name=\"a\" value=\"1\" />\n\n\n\n  <let name=\"b\" value=\"2\" />   \n\n</kscript>\n\n\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <let name=\"a\" value=\"1\" />",
                "",
                "  <let name=\"b\" value=\"2\" />",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void HonoursMaxBlankLines()
        {
            string input = "<kscript>\n  <let name=\"a\" value=\"1\" />\n\n\n  <let name=\"b\" value=\"2\" />\n</kscript>\n";
            Assert.Equal(Lines("<kscript>", "  <let name=\"a\" value=\"1\" />", "  <let name=\"b\" value=\"2\" />", "</kscript>"),
                Fmt(input, options: new FormatOptions { MaxBlankLines = 0 }));
        }

        [Fact]
        public void CommentsAreKeptAndMoveAsABlock()
        {
            string input = "<kscript>\n<if test=\"yes\">\n<!-- first\n     second -->\n<echo>x</echo>\n</if>\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <if test=\"yes\">",
                "    <!-- first",
                "         second -->",
                "    <echo>x</echo>",
                "  </if>",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void MultiLineTextIsReindentedAsABlock()
        {
            string input = "<kscript>\n    <echo>\n            Dear $name,\n              indented line\n            Thanks\n        </echo>\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <echo>",
                "    Dear $name,",
                "      indented line",
                "    Thanks",
                "  </echo>",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void TextThatFollowsItsTagMovesWithTheElement()
        {
            string input = "<kscript>\n<let name=\"people\">[\n    { name: 'Joseph' },\n    { name: 'Ellie' }\n  ]</let>\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <let name=\"people\">[",
                "      { name: 'Joseph' },",
                "      { name: 'Ellie' }",
                "    ]</let>",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void LinesNeverMoveLeftOfTheMargin()
        {
            // Moving the element 8 columns left would push the continuation lines past column 0.
            string input = "<kscript>\n        <echo>one\n    two\n      three</echo>\n</kscript>\n";
            string output = Fmt(input);
            Assert.Contains("  <echo>one\n", output);
            Assert.Contains("\ntwo\n  three</echo>", output);
        }

        [Fact]
        public void SelfClosingTagsGetASpaceAndAttributesAreKeptAsWritten()
        {
            string input = "<kscript><let   name='x'    value=\"a  &amp;  b\"/><import package=\"KScript.Math\"/></kscript>";
            Assert.Equal(Lines(
                "<kscript>",
                "  <let name='x' value=\"a  &amp;  b\" />",
                "  <import package=\"KScript.Math\" />",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void LongStartTagsGetOneAttributePerLine()
        {
            string value = new string('a', 100);
            string input = $"<kscript>\n  <let name=\"x\" value=\"'{value}'\" />\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <let",
                "    name=\"x\"",
                $"    value=\"'{value}'\" />",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void StartTagsTheAuthorWrappedStayWrapped()
        {
            string input = "<kscript>\n<test name=\"a\"\n    each=\"c\" in=\"$cases\">\n<echo>x</echo>\n</test>\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <test",
                "    name=\"a\"",
                "    each=\"c\"",
                "    in=\"$cases\">",
                "    <echo>x</echo>",
                "  </test>",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void MultiLineAttributeValuesAreNotTouched()
        {
            string input = "<kscript>\n<let name=\"names\" value=\"$people\n      |> map(fn($p) => $p.name)\n      |> join(', ')\" />\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <let name=\"names\" value=\"$people",
                "      |> map(fn($p) => $p.name)",
                "      |> join(', ')\" />",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void CDataIsKeptExactly()
        {
            string input = "<kscript>\n<echo><![CDATA[\n   a < b   \n     c\n]]></echo>\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <echo><![CDATA[",
                "   a < b   ",
                "     c",
                "]]></echo>",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void TemplateContentKeepsItsSpacingBetweenElements()
        {
            // <echo> content is text, so whitespace between child elements could show: none is added.
            string input = "<kscript><fn name=\"b\" params=\"t\"><return value=\"$t\" /></fn><echo><call fn=\"b\" args=\"'x'\" /><call fn=\"b\" args=\"'y'\" /></echo></kscript>";
            string output = Fmt(input);
            Assert.Contains("<echo><call fn=\"b\" args=\"'x'\" /><call fn=\"b\" args=\"'y'\" /></echo>", output);
        }

        [Fact]
        public void TabsAndIndentSizeAreOptions()
        {
            string input = "<kscript>\n<if test=\"yes\">\n<echo>x</echo>\n</if>\n</kscript>\n";
            Assert.Equal("<kscript>\n\t<if test=\"yes\">\n\t\t<echo>x</echo>\n\t</if>\n</kscript>\n", Fmt(input, options: new FormatOptions { UseTabs = true, IndentSize = 4 }));
            Assert.Equal("<kscript>\n    <if test=\"yes\">\n        <echo>x</echo>\n    </if>\n</kscript>\n", Fmt(input, options: new FormatOptions { IndentSize = 4 }));
        }

        [Fact]
        public void LineEndingsFollowTheFileUnlessSet()
        {
            string input = "<kscript>\r\n<echo>x</echo>\r\n</kscript>";
            Assert.Equal("<kscript>\r\n  <echo>x</echo>\r\n</kscript>\r\n", Fmt(input));
            Assert.Equal("<kscript>\n  <echo>x</echo>\n</kscript>\n", Fmt(input, options: new FormatOptions { EndOfLine = EndOfLine.Lf }));
            Assert.Equal("<kscript>\r\n  <echo>x</echo>\r\n</kscript>\r\n", Fmt("<kscript>\n<echo>x</echo>\n</kscript>", options: new FormatOptions { EndOfLine = EndOfLine.CrLf }));
        }

        [Fact]
        public void DeclarationsAndAByteOrderMarkAreKept()
        {
            Assert.Equal("\uFEFF<?xml version=\"1.0\"?>\n\n<kscript>\n  <echo>x</echo>\n</kscript>\n",
                Fmt("\uFEFF<?xml version=\"1.0\"?>\n\n<kscript><echo>x</echo></kscript>"));
        }

        [Fact]
        public void DocumentsWithErrorsAreLeftAlone()
        {
            FormatResult result = Formatter.Format("<kscript>\n  <if test=\"yes\">\n</kscript>", "bad.ks.xml");
            Assert.False(result.Succeeded);
            Assert.Contains("line 2", result.Error);
            Assert.NotNull(result.ErrorSpan);
        }

        [Fact]
        public void PreservedElementsAreKeptExactly()
        {
            var options = new FormatOptions();
            options.Preserve.Add("echo");
            string input = "<kscript>\n<if test=\"yes\">\n<echo>\n      keep   \n   this</echo>\n</if>\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <if test=\"yes\">",
                "    <echo>",
                "      keep   ",
                "   this</echo>",
                "  </if>",
                "</kscript>"), Fmt(input, options: options));
        }

        // ---- rules and ignore comments ----------------------------------------------------------------------------

        [Fact]
        public void FormatOffRegionsAreKeptExactly()
        {
            string input = "<kscript>\n<!-- kscript-fmt: off -->\n<let   name=\"grid\" value=\"[\n   [1, 0],\n   [0, 1] ]\"/>   \n      <echo>x</echo>\n<!-- kscript-fmt: on -->\n<echo>y</echo>\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <!-- kscript-fmt: off -->",
                "<let   name=\"grid\" value=\"[",
                "   [1, 0],",
                "   [0, 1] ]\"/>   ",
                "      <echo>x</echo>",
                "<!-- kscript-fmt: on -->",
                "  <echo>y</echo>",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void FormatOffRunsToTheEndOfItsElement()
        {
            string input = "<kscript>\n<if test=\"yes\">\n<!-- kscript-fmt: off -->\n<echo>a</echo>\n</if>\n<echo>b</echo>\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <if test=\"yes\">",
                "    <!-- kscript-fmt: off -->",
                "<echo>a</echo>",
                "  </if>",
                "  <echo>b</echo>",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void IgnoreCommentKeepsTheNextElement()
        {
            string input = "<kscript>\n<!-- kscript-fmt-ignore: hand aligned -->\n<for each=\"i\" in=\"1..3\"><echo>$i</echo></for>\n<for each=\"i\" in=\"1..3\"><echo>$i</echo></for>\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                "  <!-- kscript-fmt-ignore: hand aligned -->",
                "  <for each=\"i\" in=\"1..3\"><echo>$i</echo></for>",
                "  <for each=\"i\" in=\"1..3\">",
                "    <echo>$i</echo>",
                "  </for>",
                "</kscript>"), Fmt(input));
        }

        [Fact]
        public void RulesCanBeTurnedOff()
        {
            string input = "<kscript>   \n    <let   name=\"a\"/>\n\n\n\n <if test=\"yes\"><echo>x</echo>\n   </if>\n</kscript>";

            Assert.Equal("<kscript>\n  <let   name=\"a\"/>\n\n  <if test=\"yes\">\n    <echo>x</echo>\n  </if>\n</kscript>\n",
                Fmt(input, options: new FormatOptions { TidyTags = false }));
            Assert.Equal("<kscript>\n  <let name=\"a\" />\n\n  <if test=\"yes\"><echo>x</echo>\n  </if>\n</kscript>\n",
                Fmt(input, options: new FormatOptions { StatementPerLine = false }));
            Assert.Equal("<kscript>\n    <let name=\"a\" />\n\n <if test=\"yes\">\n   <echo>x</echo>\n   </if>\n</kscript>\n",
                Fmt(input, options: new FormatOptions { Indent = false }));
            Assert.Equal("<kscript>\n  <let name=\"a\" />\n\n\n\n  <if test=\"yes\">\n    <echo>x</echo>\n  </if>\n</kscript>\n",
                Fmt(input, options: new FormatOptions { CollapseBlankLines = false }));
            Assert.Equal("<kscript>\n  <let name=\"a\" />\n\n  <if test=\"yes\">\n    <echo>x</echo>\n  </if>\n</kscript>",
                Fmt(input, options: new FormatOptions { FinalNewline = false }));
            Assert.Contains("    a   \n", Fmt("<kscript>\n<echo>\n  a   \n  b\n</echo>\n</kscript>\n", options: new FormatOptions { TrimTrailingWhitespace = false }));
            Assert.Contains("    a\n", Fmt("<kscript>\n<echo>\n  a   \n  b\n</echo>\n</kscript>\n"));
        }

        [Fact]
        public void WrapAttributesOffKeepsTagsAsWritten()
        {
            string value = new string('a', 120);
            string input = $"<kscript>\n<let name=\"x\" value=\"'{value}'\" />\n<test name=\"a\"\n   each=\"c\" in=\"$cases\" />\n</kscript>\n";
            Assert.Equal(Lines(
                "<kscript>",
                $"  <let name=\"x\" value=\"'{value}'\" />",
                "  <test name=\"a\"",
                "   each=\"c\" in=\"$cases\" />",
                "</kscript>"), Fmt(input, options: new FormatOptions { WrapAttributes = false }));
        }

        // ---- pages ------------------------------------------------------------------------------------------------

        [Fact]
        public void PagesAreIndentedAndDoctypeIsKept()
        {
            string input = "<!doctype html>\n<html>\n<head><title>{$title}</title><link rel=\"stylesheet\" href=\"/site.css\"></head>\n<body>\n<main><slot/></main>\n</body>\n</html>\n";
            Assert.Equal(Lines(
                "<!doctype html>",
                "<html>",
                "  <head>",
                "    <title>{$title}</title>",
                "    <link rel=\"stylesheet\" href=\"/site.css\">",
                "  </head>",
                "  <body>",
                "    <main>",
                "      <slot />",
                "    </main>",
                "  </body>",
                "</html>"), Fmt(input, "_layout.html"));
        }

        [Fact]
        public void InlineElementsStayTogetherInPages()
        {
            string input = "<div>\n<span>a</span><b>b</b>\n<a href=\"/\">c</a>\n</div>\n";
            Assert.Equal(Lines("<div>", "  <span>a</span><b>b</b>", "  <a href=\"/\">c</a>", "</div>"), Fmt(input, "page.html"));

            string row = "<nav><a href=\"/\">Home</a> <a href=\"/people\">People</a></nav>\n";
            Assert.Equal(row, Fmt(row, "page.html"));
        }

        [Fact]
        public void PreTextareaScriptAndStyleAreKeptExactly()
        {
            string input = "<body>\n<div>\n<pre>\n  line one   \n      line two\n</pre>\n<textarea>\n a\n</textarea>\n" +
                           "<script>\nif (a) {\n        b();\n}\n</script>\n<style>\n   p { color: red; }\n</style>\n</div>\n</body>\n";
            Assert.Equal(Lines(
                "<body>",
                "  <div>",
                "    <pre>",
                "  line one   ",
                "      line two",
                "</pre>",
                "    <textarea>",
                " a",
                "</textarea>",
                "    <script>",
                "if (a) {",
                "        b();",
                "}",
                "</script>",
                "    <style>",
                "   p { color: red; }",
                "</style>",
                "  </div>",
                "</body>"), Fmt(input, "page.html"));
        }

        [Fact]
        public void ElementsWithoutEndTagsKeepThem()
        {
            string input = "<ul>\n<li>one\n<li>two\n</ul>\n";
            Assert.Equal(Lines("<ul>", "  <li>one", "  <li>two", "</ul>"), Fmt(input, "page.html"));
        }

        [Fact]
        public void KScriptControlElementsInPagesAreIndented()
        {
            string input = "<layout src=\"_layout.html\" title=\"Home\">\n<ul>\n<for each=\"p\" in=\"$people\">\n<li>{$p.name}</li>\n</for>\n</ul>\n</layout>\n";
            Assert.Equal(Lines(
                "<layout src=\"_layout.html\" title=\"Home\">",
                "  <ul>",
                "    <for each=\"p\" in=\"$people\">",
                "      <li>{$p.name}</li>",
                "    </for>",
                "  </ul>",
                "</layout>"), Fmt(input, "index.html"));
        }

        [Fact]
        public void MarkdownElementsKeepTrailingSpaces()
        {
            string input = "<div>\n<markdown>\n      line with a break  \n      next\n</markdown>\n</div>\n";
            Assert.Equal(Lines("<div>", "  <markdown>", "    line with a break  ", "    next", "  </markdown>", "</div>"), Fmt(input, "page.html"));
        }

        [Fact]
        public void MarkdownPagesAreNotFormatted()
        {
            FormatResult result = Formatter.Format("# Title  \n\ntext", "about.md");
            Assert.False(result.Succeeded);
            Assert.Null(result.ErrorSpan);
        }

        // ---- every file in the repository -------------------------------------------------------------------------

        public static IEnumerable<object[]> RepositoryFiles()
        {
            string root = RepoRoot();
            foreach (string f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("node_modules") || rel.StartsWith(".git/")) continue;
                bool script = f.EndsWith(".xml") && File.ReadAllText(f).Contains("<kscript");
                bool page = f.EndsWith(".html") && (rel.StartsWith("Example Sites/") || rel.StartsWith("samples/"));
                if (script || page) yield return new object[] { rel };
            }
        }

        [Theory]
        [MemberData(nameof(RepositoryFiles))]
        public void RepositoryFilesFormatSafelyAndStably(string relative)
        {
            string path = Path.Combine(RepoRoot(), relative);
            string text = File.ReadAllText(path);
            FormatResult result = Formatter.Format(text, path);
            if (!result.Succeeded)
            {
                // `kscript make` templates hold {{ }} placeholders; anything else must format.
                Assert.Contains("Templates", relative);
                return;
            }
            AssertSameMeaning(text, result.Text, MarkupParser.KindFromPath(path));
            Assert.Equal(result.Text, Formatter.Format(result.Text, path).Text);
        }

        internal static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "KScript.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir;
        }

        // ---- the meaning check ------------------------------------------------------------------------------------

        /// <summary>
        /// Fails unless both texts mean the same: the same elements and attribute values, the same element text once
        /// KScript tidies it (scripts), and the same rendered whitespace (pages). Raw content must match exactly.
        /// </summary>
        internal static void AssertSameMeaning(string before, string after, DocumentKind kind)
        {
            Assert.Equal(Meaning(before, kind), Meaning(after, kind));
        }

        private static readonly Regex Spaces = new Regex(@"\s+");

        private static string Meaning(string text, DocumentKind kind)
        {
            MarkupDocument doc = MarkupParser.Parse(text.TrimStart('\uFEFF'), kind);
            Assert.DoesNotContain(doc.Diagnostics, d => d.Severity == Syntax.DiagnosticSeverity.Error);
            StringBuilder sb = new StringBuilder();
            foreach (MarkupNode n in doc.Children) Describe(n, kind, sb, raw: false);
            string s = sb.ToString();
            if (kind != DocumentKind.Script)
            {
                // Whitespace next to a block-level tag never shows.
                s = Regex.Replace(s, @"\s*(</?(html|head|body|title|meta|link|div|p|ul|ol|li|main|nav|section|h[1-6]|table|tr|td|th|thead|tbody|header|footer|form|layout|slot|import|live|pre|textarea|script|style|article|aside|dl|dt|dd|fieldset|figure|figcaption|blockquote|details|summary|option|select)\b[^>]*>)\s*", "$1");
            }
            return s;
        }

        private static void Describe(MarkupNode node, DocumentKind kind, StringBuilder sb, bool raw)
        {
            switch (node)
            {
                case MarkupElement e:
                    bool isRaw = raw || (kind != DocumentKind.Script && new[] { "pre", "textarea", "script", "style" }.Contains(e.Name.ToLowerInvariant()));
                    sb.Append('<').Append(e.Name);
                    foreach (MarkupAttribute a in e.Attributes) sb.Append(' ').Append(a.Name).Append("=[").Append(a.ValueText).Append(']');
                    sb.Append('>');
                    bool hasText = e.Children.OfType<MarkupText>().Any(t => !t.IsWhitespace || t.IsCData);
                    if (kind == DocumentKind.Script && hasText && !e.ChildElements.Any() && !isRaw)
                    {
                        sb.Append(TextLayout.Tidy(string.Concat(e.Children.OfType<MarkupText>().Select(t => t.Value))));
                    }
                    else
                    {
                        foreach (MarkupNode c in e.Children) Describe(c, kind, sb, isRaw);
                    }
                    sb.Append("</").Append(e.Name).Append('>');
                    break;
                case MarkupText t when t.IsCData || raw:
                    sb.Append("[raw:").Append(t.Value).Append(']');
                    break;
                case MarkupText t when kind == DocumentKind.Script:
                    if (!t.IsWhitespace) sb.Append(Spaces.Replace(t.Value.Trim(), " "));
                    break;
                case MarkupText t:
                    sb.Append(Spaces.Replace(t.Value, " "));
                    break;
                case MarkupComment:
                    sb.Append("<!---->");
                    break;
            }
        }
    }

    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class FormatCommandTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-fmt-" + Guid.NewGuid().ToString("N"));

        public FormatCommandTests() => Directory.CreateDirectory(dir);

        public void Dispose() => Directory.Delete(dir, recursive: true);

        private string Write(string name, string text)
        {
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        private static (int Code, string Output) Run(params string[] args)
        {
            StringWriter output = new StringWriter();
            int code = FormatCommand.Run(args, output);
            return (code, output.ToString());
        }

        private const string Messy = "<kscript>\n<echo>hi</echo>\n</kscript>";
        private const string Tidy = "<kscript>\n  <echo>hi</echo>\n</kscript>\n";

        [Fact]
        public void FormatsKscriptFiles()
        {
            const string code = "x=1\nif $x > 0 {\necho \"hi\"\n}\n";
            string path = Write("code.kscript", code);
            Write("a.ks.xml", Tidy);

            (int code1, string folder) = Run(dir, "--check");
            Assert.Equal(1, code1);
            Assert.Matches(@"code\.kscript \.+ NOT FORMATTED", folder);
            Assert.Equal(code, File.ReadAllText(path));

            (int code2, string named) = Run(path);
            Assert.Equal(0, code2);
            Assert.Matches(@"code\.kscript \.+ FORMATTED", named);
            Assert.Equal("x = 1\nif $x > 0 {\n  echo \"hi\"\n}\n", File.ReadAllText(path));
            Assert.Equal(0, Run(dir, "--check").Code);
        }

        [Fact]
        public void CheckReportsWithoutChangingFiles()
        {
            string path = Write("a.ks.xml", Messy);
            Write("b.ks.xml", Tidy);
            (int code, string output) = Run(dir, "--check");
            Assert.Equal(1, code);
            Assert.Matches(@"a\.ks\.xml \.+ NOT FORMATTED", output);
            Assert.DoesNotContain("b.ks.xml", output);
            Assert.Contains("2 files checked: 1 need formatting.", output);
            Assert.Equal(Messy, File.ReadAllText(path));
        }

        [Fact]
        public void FormatsFilesInPlace()
        {
            string path = Write("a.ks.xml", Messy);
            (int code, string output) = Run(dir);
            Assert.Equal(0, code);
            Assert.Contains("1 file checked: 1 formatted.", output);
            Assert.Equal(Tidy, File.ReadAllText(path));
            Assert.Equal(0, Run(dir, "--check").Code);
        }

        [Fact]
        public void KeepsAByteOrderMark()
        {
            string path = Path.Combine(dir, "a.ks.xml");
            File.WriteAllText(path, Messy, new UTF8Encoding(true));
            Run(path);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
            Assert.Equal(Tidy, new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
        }

        [Fact]
        public void FilesWithErrorsFailAndAreLeftAlone()
        {
            string path = Write("bad.ks.xml", "<kscript>\n<if test=\"yes\">\n</kscript>");
            (int code, string output) = Run(path);
            Assert.Equal(1, code);
            Assert.Contains("bad.ks.xml(2,2): error: not formatted", output);
            Assert.Equal("<kscript>\n<if test=\"yes\">\n</kscript>", File.ReadAllText(path));
        }

        [Fact]
        public void ProjectSettingsChooseTheStyleAndFiles()
        {
            Write("kscript.json", "{ \"format\": { \"indent\": 4, \"exclude\": [\"vendor\"] } }");
            string page = Write("index.html", "<div>\n<p>hi</p>\n</div>\n");
            string script = Write("a.ks.xml", Messy);
            string vendor = Write("vendor/v.ks.xml", Messy);
            Write("about.md", "# About  \n");
            (int code, string output) = Run(dir);
            Assert.Equal(0, code);
            Assert.Equal("<div>\n    <p>hi</p>\n</div>\n", File.ReadAllText(page));
            Assert.Equal("<kscript>\n    <echo>hi</echo>\n</kscript>\n", File.ReadAllText(script));
            Assert.Equal(Messy, File.ReadAllText(vendor));
            Assert.Equal("# About  \n", File.ReadAllText(Path.Combine(dir, "about.md")));
            Assert.DoesNotContain("about.md", output);
        }

        [Fact]
        public void ProjectsCanTurnFormattingOff()
        {
            Write("kscript.json", "{ \"format\": { \"enabled\": false } }");
            string script = Write("a.ks.xml", Messy);
            (int code, string output) = Run(dir, "--check");
            Assert.Equal(0, code);
            Assert.Contains("format.enabled is false", output);
            Assert.Equal(Messy, File.ReadAllText(script));
        }

        [Fact]
        public void Empty_documents_and_inline_tags_at_the_top_of_a_page()
        {
            Assert.Equal(string.Empty, Formatter.Format(string.Empty, "test.ks.xml").Text);
            Assert.Equal("<b>a</b><i>b</i>\n", Formatter.Format("<b>a</b><i>b</i>", "page.html").Text);
        }

        [Fact]
        public void FormatSettingsAreRead()
        {
            ProjectConfig config = ProjectConfig.Parse(
                "{ \"format\": { \"indent\": \"tab\", \"tabWidth\": 4, \"printWidth\": 80, \"maxBlankLines\": 2, \"endOfLine\": \"crlf\", \"preserve\": [\"echo\"], \"nope\": 1 } }",
                Path.Combine(dir, "kscript.json"));
            FormatOptions options = FormatOptions.From(config, editorTabSize: 8, editorInsertSpaces: true);
            Assert.True(options.UseTabs);
            Assert.Equal(4, options.IndentSize);
            Assert.Equal(80, options.PrintWidth);
            Assert.Equal(2, options.MaxBlankLines);
            Assert.Equal(EndOfLine.CrLf, options.EndOfLine);
            Assert.Contains("echo", options.Preserve);
            Assert.Contains(config.Warnings, w => w.Contains("format.nope"));

            ProjectConfig rules = ProjectConfig.Parse(
                "{ \"format\": { \"enabled\": false, \"rules\": { \"statementPerLine\": false, \"tidytags\": \"off\", \"bogus\": false, \"indent\": 3 } } }",
                Path.Combine(dir, "kscript.json"));
            Assert.False(rules.Format.Enabled);
            FormatOptions ruled = FormatOptions.From(rules);
            Assert.False(ruled.StatementPerLine);
            Assert.False(ruled.TidyTags);
            Assert.True(ruled.Indent);
            Assert.Contains(rules.Warnings, w => w.Contains("no rule 'bogus'"));
            Assert.Contains(rules.Warnings, w => w.Contains("format.rules.indent should be true or false"));

            ProjectConfig off = ProjectConfig.Parse(
                "{ \"format\": { \"endOfLine\": \"lf\", \"rules\": { \"indent\": false, \"wrapAttributes\": \"off\", \"trimTrailingWhitespace\": false, \"collapseBlankLines\": false, \"finalNewline\": false } } }",
                Path.Combine(dir, "kscript.json"));
            FormatOptions allOff = FormatOptions.From(off);
            Assert.Equal(EndOfLine.Lf, allOff.EndOfLine);
            Assert.False(allOff.Indent || allOff.WrapAttributes || allOff.TrimTrailingWhitespace || allOff.CollapseBlankLines || allOff.FinalNewline);

            // Without a "format" section the editor's settings are used.
            FormatOptions editor = FormatOptions.From(null, editorTabSize: 4, editorInsertSpaces: false);
            Assert.Equal(4, editor.IndentSize);
            Assert.True(editor.UseTabs);
        }

        [Fact]
        public void MinimalChangeCoversOnlyTheDifference()
        {
            string before = "<a>\n<b/>\n</a>\n", after = "<a>\n  <b />\n</a>\n";
            Syntax.TextChange change = KScriptLanguageServer.MinimalChange(before, after);
            Assert.Equal(4, change.Span.Start);
            Assert.True(change.Span.End < before.Length - 5);
            Assert.Equal(after, before.Substring(0, change.Span.Start) + change.NewText + before.Substring(change.Span.End));
            Assert.Null(KScriptLanguageServer.MinimalChange("x", "x"));
        }

        [Fact]
        public void Usage_and_unusual_paths()
        {
            Assert.Equal(0, Run("--help").Code);
            Assert.Contains("Unknown option --nope", Run("--nope").Output);
            Assert.Contains("file not found", Run(Path.Combine(dir, "missing.ks.xml")).Output);
            Assert.Contains("No KScript files found", Run(dir).Output);

            string md = Write("notes.md", "# Hi  \n");
            Assert.Matches(@"notes\.md \.+ Markdown pages are not reformatted SKIPPED", Run(md).Output);

            Write("_templates/t.ks.xml", Messy);
            string xml = Write("plain.xml", Messy);
            Write("data.xml", "<data />");
            File.CreateSymbolicLink(Path.Combine(dir, "gone.xml"), Path.Combine(dir, "nowhere.xml"));
            (int code, string output) = Run(dir);
            Assert.Equal(0, code);
            Assert.Contains("1 file checked: 1 formatted.", output);
            Assert.Equal(Tidy, File.ReadAllText(xml));
        }

        [Fact]
        public void Runs_on_the_current_project()
        {
            string previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(dir);
                Assert.Contains("run it in a project with a kscript.json", Run().Output);

                Write("kscript.json", "{ \"nope\": 1 }");
                string script = Write("a.ks.xml", Messy);
                (int code, string output) = Run();
                Assert.Equal(0, code);
                Assert.Contains("warning unknown setting 'nope'", output);
                Assert.Equal(Tidy, File.ReadAllText(script));
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }

        [Fact]
        public void A_broken_project_file_stops_the_run()
        {
            Write("kscript.json", "{ nope");
            string script = Write("a.ks.xml", Messy);
            Assert.Equal(2, Run(dir).Code);
            Assert.Equal(2, Run(script).Code);
            Assert.Equal(Messy, File.ReadAllText(script));
        }
    }
}
