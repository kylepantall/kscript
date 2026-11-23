using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of pattern literals joined by - + / or . (<c>my-post</c>, <c>1.4.2</c>) and of pattern types on text native types.</summary>
    public class CompoundPatternLiteralAnalysisTests
    {
        private const string Slug = "type Slug with [string] { pattern: /^[a-z0-9]+(-[a-z0-9]+)+$/ }\n";
        private static readonly LanguageService Service = new LanguageService();

        private static DocumentAnalysis Code(string code) => WithoutUnused(Analyze(code, "main.kscript"));

        [Theory]
        [InlineData(Slug + "post = my-first-post\necho $post")]
        [InlineData(Slug + "echo [my-post, other-post]")]
        [InlineData(Slug + "fn show(s: string) => \"<$s>\"\necho @show(my-post)\necho @show(s: my-post)")]
        [InlineData(Slug + "echo my-post == my-post")]
        [InlineData("type Ver with [string] { pattern: /^\\d+\\.\\d+\\.\\d+$/ }\nv = 1.4.2\necho $v")]
        [InlineData("type Host with [domain] { pattern: /^[a-z]+\\.corp\\.com$/ }\nh = api.corp.com\necho $h")]
        public void JoinedWordsATypeFitsAreValues(string code) => AssertClean(Code(code));

        [Fact]
        public void TheWordHasItsTypeInHoverAndInference()
        {
            (string text, int at) = WithCaret(Slug + "post = my-‸post\necho $post");
            DocumentAnalysis a = Analyze(text, "main.kscript");
            AssertClean(a);
            Assert.Equal("Slug", a.Symbols.Single(s => s.Name == "post").Shape.Name);
            Assert.Contains("$post: Slug", Service.GetHover(a, text.LastIndexOf("$post", StringComparison.Ordinal) + 1).Markdown);
        }

        [Fact]
        public void ANativeBaseChecksTheWordAsThatType()
        {
            Diagnostic d = AssertSingle(Code("type Host with [domain] {\n  min: 20\n  pattern: /^[a-z]+\\.corp\\.com$/\n}\nh = api.corp.com\necho $h"), DiagnosticCodes.InvalidTypedValue);
            Assert.Contains("'api.corp.com' doesn't fit Host", d.Message);
        }

        [Fact]
        public void TwoTypesThatFitAreNamed()
        {
            Diagnostic d = AssertSingle(Code(Slug + "type Ref with [string] { pattern: /^[a-z]+-[a-z]+$/ }\nr = my-post\necho $r"), DiagnosticCodes.InvalidTypedValue);
            Assert.Contains("matches more than one literal type: Ref, Slug", d.Message);
        }

        [Theory]
        // No part of a word no type fits is read as a type on its own, as at run time.
        [InlineData("type Word with [string] { pattern: /^[a-z]+$/ }\nh = api.corp.com\necho $h", "'api' isn't a value")]
        [InlineData("type Word with [string] { pattern: /^[a-z]+$/ }\nx = my-post\necho $x", "my-post isn't a value")]
        public void WordsNoTypeFitsAreStillReported(string code, string message) =>
            Assert.Contains(message, AssertSingle(Code(code), DiagnosticCodes.BareIdentifier).Message);
    }
}
