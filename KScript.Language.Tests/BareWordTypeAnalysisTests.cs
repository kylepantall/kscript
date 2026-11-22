using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of bare words read as the declared type whose pattern fits them (<c>key = passkey_login</c>).</summary>
    public class BareWordTypeAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private const string Passkey = "type PasskeySessionKey with [string] {\n  min: 12\n  normalize: $value->trim()\n  pattern: /^passkey_[a-z]+$/\n}\n";
        private const string Slug = "type Slug with [string] { pattern: /^[a-z0-9]+(-[a-z0-9]+)+$/ }\n";

        private static DocumentAnalysis Code(string code) => WithoutUnused(Analyze(code, "words.kscript"));

        [Theory]
        [InlineData(Passkey + "key = passkey_login\necho $key")]
        [InlineData(Slug + "post = my-first-post\necho $post")]
        [InlineData(Slug + "echo [my-post, other-post]")]
        [InlineData(Slug + "fn show(s: string) => \"<$s>\"\necho @show(my-post)")]
        [InlineData("type Ver with [string] { pattern: /^\\d+\\.\\d+\\.\\d+$/ }\nv = 1.4.2\necho $v")]
        [InlineData("type Host with [domain] { pattern: /^[a-z]+\\.corp\\.com$/ }\nh = api.corp.com\necho $h")]
        public void WordsATypeFitsAreValues(string code) => AssertClean(Code(code));

        [Fact]
        public void TheWordGetsTheTypeInHintsAndInference()
        {
            DocumentAnalysis a = Analyze(Passkey + "key = passkey_login\necho $key", "words.kscript");
            Symbol key = a.Symbols.Single(s => s.Name == "key");
            Assert.Equal("PasskeySessionKey", key.Shape.Name ?? key.Shape.Type);
            Assert.Contains(Service.GetInlayHints(a), h => h.Label.Contains("PasskeySessionKey"));
        }

        [Fact]
        public void AWordThatBreaksTheTypesOtherRulesIsCheckedAtTheWord()
        {
            Diagnostic d = AssertSingle(Code("type K with [string] {\n  min: 12\n  pattern: /^passkey_[a-z]+$/\n}\nkey = passkey_x\necho $key"), DiagnosticCodes.InvalidTypedValue);
            Assert.Contains("'passkey_x' doesn't fit K: it must be at least 12 characters", d.Message);
        }

        [Fact]
        public void AWordTwoTypesFitAsksWhichOne()
        {
            string code = "type A with [string] { pattern: /^x[a-z]+$/ }\ntype B with [string] { pattern: /^[a-z]+$/ }\nk = xyz\necho $k\necho [xyz]";
            DocumentAnalysis a = Code(code);
            List<Diagnostic> found = a.Diagnostics.Where(d => d.Code == DiagnosticCodes.AmbiguousBareWord).ToList();
            Assert.Equal(2, found.Count);
            Assert.Contains("xyz fits more than one type (A and B)", found[0].Message);
            Assert.Equal(new[] { "Declare it as A", "Declare it as B" }, found[0].Fixes.Select(f => f.Title));
            Assert.Equal("k: B = xyz", Apply(code, found[0].Fixes[1]).Split('\n')[2]);
            Assert.Equal("echo ['xyz' as A]", Apply(code, found[1].Fixes[0]).Split('\n')[4]);
            AssertClean(Code(Apply(code, found[0].Fixes[0]).Replace("echo [xyz]", string.Empty)));
        }

        [Theory]
        [InlineData(Passkey + "key = login\necho $key", "'login' isn't a value")]
        [InlineData(Slug + "h = api.corp.com\necho $h", "'api' isn't a value")]
        [InlineData("type K with [string] { pattern: /key_[a-z]+/ }\nk = my_key_abc\necho $k", "'my_key_abc' isn't a value")]
        [InlineData("type N with [int] { pattern: /^[a-z]+$/ }\nn = abc\necho $n", "'abc' isn't a value")]
        public void OtherWordsAreStillReported(string code, string message) =>
            Assert.Contains(message, AssertSingle(Code(code), DiagnosticCodes.BareIdentifier).Message);

        [Fact]
        public void ProjectNativeTypesReadWords()
        {
            string root = Path.Combine(Path.GetTempPath(), "bare-word-natives-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "natives"));
            try
            {
                File.WriteAllText(Path.Combine(root, "kscript.json"), "{ \"name\": \"words\" }");
                File.WriteAllText(Path.Combine(root, "natives", "slug.kscript"), Slug);
                string script = Path.Combine(root, "main.kscript");
                File.WriteAllText(script, "post = my-post\necho $post\n");
                AssertClean(WithoutUnused(Service.Analyze(File.ReadAllText(script), script)));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string Apply(string code, QuickFix fix)
        {
            string text = code;
            foreach (TextChange change in fix.Changes.OrderByDescending(c => c.Span.Start))
            {
                text = text.Substring(0, change.Span.Start) + change.NewText + text.Substring(change.Span.End);
            }
            return text;
        }
    }
}
