using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>kscript convert: the shapes the printer handles, and what it warns about.</summary>
    public class ScriptConverterCoverageTests
    {
        private static ConversionResult ToCode(string xml) => ScriptConverter.Convert(xml, "a.ks.xml");

        private static ConversionResult ToXml(string code) => ScriptConverter.Convert(code, "a.kscript");

        [Fact]
        public void XmlBecomesCode()
        {
            ConversionResult r = ToCode(string.Join("\n",
                "<kscript>",
                "  <let name=\"x\" />",
                "  <echo>",
                "    Hi \"there\" \\",
                "  </echo>",
                "  stray text",
                "  <!--- Greets someone. -->",
                "  <fn name=\"f\" params=\"a = 'it\\'s', b = [1, 2]\"><echo>x</echo></fn>",
                "  <let name=\"y\" value=\"1\" />",
                "  <if test=\"true\"><let name=\"y\" value=\"2\" /></if>",
                "  <echo value=\"$y\" />",
                "  <match value=\"$y\"><!-- c --><case is=\"1\"><echo>one</echo></case></match>",
                "  <call fn=\"greet\" args=\"'x'\" to=\"r\" />",
                "  <do />",
                "  <do>x = = 1</do>",
                "  <if test=\"$x\"></if>",
                "  <try><echo>t</echo><catch type=\"Io, Http\" as=\"err\"><echo>c</echo></catch><catch /></try>",
                "  <echo foo=\"1\">hi</echo>",
                "  <Foo a=\"\" b=\"x)\" c=\"x,y\">text<bar /></Foo>",
                "  <!--- lonely -->",
                "</kscript>"));

            Assert.Equal(string.Join("\n",
                "x = null",
                "echo \"Hi \\\"there\\\" \\\\\"",
                "// stray text",
                "/// Greets someone.",
                "fn f(a = 'it\\'s', b = [1, 2]) {",
                "  echo \"x\"",
                "}",
                "y = 1",
                "if true {",
                "  y = 2",
                "}",
                "echo $y",
                "match $y {",
                "  // c",
                "  case 1 {",
                "    echo \"one\"",
                "  }",
                "}",
                "r = @greet('x')",
                "x = = 1",
                "if $x { }",
                "try {",
                "  echo \"t\"",
                "} catch(type: \"Io, Http\") err {",
                "  echo \"c\"",
                "} catch { }",
                "echo(foo: 1) \"hi\"",
                "Foo(a: \"\", b: \"x)\", c: \"x,y\") {",
                "  // text",
                "  bar",
                "}",
                "/// lonely",
                ""), r.Text);
            Assert.Contains(r.Warnings, w => w.Contains("stray text"));
            Assert.Contains(r.Warnings, w => w.Contains("hiding the outer one"));
            Assert.Contains(r.Warnings, w => w.Contains("<echo> on line 18"));
            Assert.Contains(r.Warnings, w => w.Contains("<Foo> on line 19 mixes text and elements"));
        }

        [Fact]
        public void CodeBecomesXml()
        {
            ConversionResult r = ToXml(string.Join("\n",
                "// - a plain comment",
                "/// Greets.",
                "/// @param who the person",
                "fn greet(who) { }",
                "/// Line one.",
                "/// Line two.",
                "const names = [",
                "  1,",
                "  2",
                "]",
                "Foo \"text\" {",
                "  bar",
                "}",
                "do {",
                "  echo \"inside\"",
                "}",
                "x = = 1",
                ""));

            Assert.Equal(string.Join("\n",
                "<kscript>",
                "  <!-- - a plain comment -->",
                "  <!---",
                "    Greets.",
                "    @param who the person",
                "  -->",
                "  <fn name=\"greet\" params=\"who\" />",
                "  <!---",
                "    Line one.",
                "    Line two.",
                "  -->",
                "  <const name=\"names\">[",
                "      1,",
                "      2",
                "    ]</const>",
                "  <Foo>",
                "    text",
                "    <bar />",
                "  </Foo>",
                "  <do>",
                "    <echo>inside</echo>",
                "  </do>",
                "  <let>x = = 1</let>",
                "</kscript>",
                ""), r.Text);
            Assert.Contains(r.Warnings, w => w.Contains("declares new variables"));
        }

        [Fact]
        public void ADashedCommentIsNotADocComment()
        {
            MarkupDocument doc = CodeParser.Parse("// - not docs\nfn f() { }");
            MarkupComment comment = doc.RootElements.Single().Children.OfType<MarkupComment>().Single();
            Assert.False(comment.IsDoc);
            Assert.Null(doc.RootElements.Single().ChildElements.Single().Documentation);
        }

        [Theory]
        [InlineData("a.kscript", "a.ks.xml")]
        [InlineData("a.ks.xml", "a.kscript")]
        [InlineData("a.xml", "a.kscript")]
        [InlineData("a.txt", "a.txt.kscript")]
        public void ConvertedPaths(string path, string converted) => Assert.Equal(converted, ScriptConverter.ConvertedPath(path));
    }
}
