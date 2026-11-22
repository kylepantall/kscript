using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Language.Features;
using KScript.Metadata;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>
    /// Values of a native type (uuid, email, handle, regex) are offered their own methods and only the string methods
    /// that make sense for them: <c>3f2a8c1e-...->to_date()</c> is not suggested. Plain text keeps every string method.
    /// </summary>
    public class NativeTypeMethodTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private const string Uuid = "3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f";

        private static List<string> MethodsAfter(string code)
        {
            (string text, int offset) = WithCaret(code);
            return Service.GetCompletions(Service.Analyze(text, "types.kscript"), offset).Select(i => i.Label).ToList();
        }

        // Each native type written without quotes, as a literal and through a declared variable.
        public static TheoryData<string, string, string> Receivers => new TheoryData<string, string, string>
        {
            { KType.Uuid, $"echo {Uuid}->‸", "version" },
            { KType.Uuid, $"u: uuid = {Uuid}\necho $u->‸", "version" },
            { KType.Uuid, $"u = {Uuid}\necho $u->‸", "version" },
            { KType.Email, "echo joan@example.com->‸", "domain" },
            { KType.Email, "e: email = joan@example.com\necho $e->‸", "local" },
            { KType.Handle, "h: handle = @joan\necho $h->‸", "name" },
            { KType.Regex, "echo /ab+/i->‸", "test" },
            { KType.Regex, "r: regex = /ab+/\necho $r->‸", "match" },
        };

        [Theory]
        [MemberData(nameof(Receivers))]
        public void Native_values_are_offered_their_own_methods_and_the_string_methods_they_keep(string type, string code, string own)
        {
            List<string> methods = MethodsAfter(code);

            Assert.Contains(own, methods);
            Assert.Contains("to_string", methods);
            // Kept methods from packages (->markdown_escape() is KScript.Markdown's) are only offered once imported.
            foreach (string kept in KType.StringMethodsOf[type].Where(n => LanguageCatalog.Default.GetMethods(n, KType.String).Any(m => m.Package is null or "KScript.Core")))
            {
                Assert.Contains(kept, methods);
            }
            foreach (string missing in new[] { "to_date", "to_number", "to_int", "to_bool", "lines", "chars", "repeat", "reverse", "url_decode" })
            {
                Assert.DoesNotContain(missing, methods);
            }
        }

        [Fact]
        public void Plain_text_keeps_every_string_method()
        {
            List<string> methods = MethodsAfter("echo '2026-11-19'->‸");
            Assert.Contains("to_date", methods);
            Assert.Contains("to_number", methods);
            Assert.Contains("lines", methods);
            Assert.DoesNotContain("version", methods);
        }

        [Fact]
        public void Text_that_only_looks_like_a_uuid_in_quotes_is_a_string()
        {
            List<string> methods = MethodsAfter($"echo '{Uuid}'->‸");
            Assert.Contains("to_date", methods);
            Assert.DoesNotContain("version", methods);
        }

        [Theory]
        [InlineData("echo 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f->upper()")]
        [InlineData("echo 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f->slice(0, 8)")]
        [InlineData("u: uuid = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\necho $u->starts_with('3f')")]
        [InlineData("echo joan@example.com->lower()")]
        [InlineData("e: email = joan@example.com\necho $e->split('@')")]
        [InlineData("h: handle = @joan\necho $h->count()")]
        [InlineData("r: regex = /ab+/\necho $r->count()")]
        [InlineData("echo 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f->to_string()->to_date()")]
        public void String_methods_that_make_sense_are_clean(string code) => AssertClean(Analyze(code, "types.kscript"));

        [Theory]
        [InlineData("echo 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f->to_date()", "->to_date() works on strings, but this value is a uuid. Use ->to_string()->to_date() to work on its text.")]
        [InlineData("u: uuid = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\necho $u->to_number()", "->to_number() works on strings, but this value is declared as a uuid.")]
        [InlineData("echo joan@example.com->to_date()", "->to_date() works on strings, but this value is an email.")]
        [InlineData("h: handle = @joan\necho $h->lower()", "->lower() works on strings, but this value is declared as a handle.")]
        [InlineData("r: regex = /ab+/\necho $r->upper()", "->upper() works on strings, but this value is declared as a regex.")]
        public void String_methods_that_dont_make_sense_are_reported(string code, string message)
        {
            Diagnostic d = Assert.Single(Analyze(code, "types.kscript").Diagnostics);
            Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
            Assert.Contains(message, d.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_kept_method_is_a_real_string_method()
        {
            HashSet<string> stringMethods = LanguageCatalog.Default.MethodsFor(KType.String).Where(m => m.ReceiverType == KType.String).Select(m => m.Name).ToHashSet();
            foreach ((string type, HashSet<string> kept) in KType.StringMethodsOf)
            {
                Assert.All(kept, name => Assert.True(stringMethods.Contains(name), $"{type} keeps ->{name}(), which isn't a string method."));
            }
        }
    }
}
