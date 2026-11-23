using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>Every mistake the .kscript parser explains, and the less common shapes it accepts.</summary>
    public class CodeParserCoverageTests
    {
        [Theory]
        [InlineData("catch { }", "KS3007")]
        [InlineData("case 1 { }", "KS3007")]
        [InlineData(")", "KS1001")]
        [InlineData("import", "KS3005")]
        [InlineData("import a, b from x", "KS3005")]
        [InlineData("import A, B", "KS1001")]
        [InlineData("import a, from \"x\"", "KS1001")]
        [InlineData("import KScript.IO as", "KS3005")]
        [InlineData("let $x = 1", "KS3002")]
        [InlineData("const", "KS3005")]
        [InlineData("x[1]", "KS1001")]
        [InlineData("for { }", "KS3005")]
        [InlineData("match $x", "KS1001")]
        [InlineData("match $x {", "KS1003")]
        [InlineData("match $x {\n  bogus\n}", "KS3007")]
        [InlineData("fn { }", "KS3005")]
        [InlineData("fn f(a", "KS1001")]
        [InlineData("throw Validation 3", "KS1001")]
        [InlineData("Foo(a: 1", "KS1001")]
        [InlineData("Foo(", "KS1001")]
        [InlineData("Foo(@: 1)", "KS1004")]
        [InlineData("Foo(a: 1, a: 2)", "KS1005")]
        [InlineData("Foo(a: )", "KS1004")]
        [InlineData("while $x", "KS1001")]
        [InlineData("if { }", "KS3005")]
        [InlineData("x = 'abc", "KS1001")]
        [InlineData("/* never closed", "KS1001")]
        public void ExplainsTheMistake(string code, string diagnostic)
        {
            MarkupDocument doc = CodeParser.Parse(code);
            Assert.Contains(doc.Diagnostics, d => d.Code == diagnostic);
        }

        [Theory]
        [InlineData("true", "do")]
        [InlineData("let(name: \"x\", value: 1)", "let")]
        [InlineData("$x = 1", "do")]
        [InlineData("echo(to: \"x\")", "echo")]
        [InlineData("if\n  $x { }", "if")]
        [InlineData("$1", "do")]
        [InlineData("x = 1 +  \n  2", "do")]
        [InlineData("x =\n  2", "do")]
        [InlineData("x = $a\n  and $b", "do")]
        [InlineData("x = $a and\n $b", "do")]
        [InlineData("x = \"a\\\"b\"", "do")]
        [InlineData("x = 'C:\\temp\\'", "do")]
        [InlineData("try { } catch(type: \"Io\") err { }", "try")]
        public void Accepts(string code, string element)
        {
            MarkupDocument doc = CodeParser.Parse(code);
            Assert.Empty(doc.Diagnostics);
            Assert.Equal(element, doc.RootElements.Single().ChildElements.Concat(doc.RootElements).First(e => e.Name != "kscript").Name);
        }

        [Theory]
        [InlineData("min: 8,\n  normalize: $value->trim()")]
        [InlineData("min: 8, // a note\r\n  max: 12")]
        [InlineData("max: 12, /* a note */\n  min: 8")]
        [InlineData("pattern: /[a-z,]+/,")]
        [InlineData("pattern: 'x,',")]
        [InlineData("pattern: /x/ /* note */,")]
        [InlineData("validate: $value > 0,\n  fn twice() => $value * 2")]
        [InlineData("normalize: @trim($value),")]
        [InlineData("one_of: [one, two],")]
        [InlineData("not: one, two,\n  min: 2")]
        [InlineData("validate: $value > 0,\n  min: 1")]
        [InlineData("validate: $value > 0, 'Too small',")]
        public void ATrailingRuleCommaHasOnePreciseRemoval(string rules)
        {
            string code = "type T with [string] { " + rules + " }";
            MarkupDocument doc = CodeParser.Parse(code);
            var problem = Assert.Single(doc.Diagnostics);
            int comma = code.LastIndexOf(',');
            Assert.Equal("KS1001", problem.Code);
            Assert.Equal(comma, problem.Span.Start);
            Assert.Equal(1, problem.Span.Length);
            var fix = Assert.Single(Assert.Single(problem.Fixes).Changes);
            Assert.Equal(problem.Span, fix.Span);
            Assert.Equal(string.Empty, fix.NewText);
            Assert.Empty(CodeParser.Parse(code.Remove(comma, 1)).Diagnostics);
        }

        [Fact]
        public void InlineRuleCommaRecoversAtTheNextRule()
        {
            var doc = CodeParser.Parse("type Length with [int] { min: 8, max: 32 }");
            var problem = Assert.Single(doc.Diagnostics);
            Assert.Equal(";", Assert.Single(Assert.Single(problem.Fixes).Changes).NewText);
            var type = doc.RootElements.Single().ChildElements.Single();
            Assert.Equal("8", type.GetAttributeValue("min"));
            Assert.Equal("32", type.GetAttributeValue("max"));
        }

        [Theory]
        [InlineData("min: 8; max: 12")]
        [InlineData("one_of: [one, two,]; not: three, four")]
        [InlineData("one_of: one,\n two")]
        [InlineData("one_of: https://a.example, https://b.example")]
        [InlineData("pattern: /a{1,3}[,]/; normalize: @join([$value, ','], ',')")]
        [InlineData("validate: $value->between(1, 3), 'Needs 1, 2, or 3'")]
        [InlineData("validate: $value > 0,\n 'Too small'; min: 1")]
        [InlineData("min: 8 // keep this comma,\n max: 12")]
        public void CommasInsideRuleValuesKeepTheirMeaning(string rules)
        {
            Assert.Empty(CodeParser.Parse("type T with [string] { " + rules + " }").Diagnostics);
        }

        [Fact]
        public void ADocCommentDescribesATypedVariable()
        {
            MarkupDocument doc = CodeParser.Parse("/// The answer.\nx: int = 1");
            Assert.Empty(doc.Diagnostics);
            MarkupElement let = doc.RootElements.Single().ChildElements.Single();
            Assert.Equal("The answer.", let.GetAttributeValue("description"));
            Assert.Equal("int", let.GetAttributeValue("type"));
        }

        [Fact]
        public void ACatchCanNameTheErrorTypes()
        {
            MarkupDocument doc = CodeParser.Parse("try { } catch(type: \"Io\") err { }");
            MarkupElement c = doc.RootElements.Single().ChildElements.Single().ChildElements.Single(e => e.Name == "catch");
            Assert.Equal("Io", c.GetAttributeValue("type"));
        }
    }
}
