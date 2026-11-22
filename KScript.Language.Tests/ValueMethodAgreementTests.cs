using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Language.Features;
using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>
    /// The editor offers a date, number, bool or file only the methods the interpreter runs on it: its own and the ones
    /// every value has (->to_string(), ->to_json()). String methods such as ->json() or ->upper() need ->to_string() first,
    /// since the interpreter doesn't treat these values as text.
    /// </summary>
    public class ValueMethodAgreementTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private static List<string> MethodsAfter(string code, string file)
        {
            (string text, int offset) = WithCaret(code);
            return Service.GetCompletions(Service.Analyze(text, file), offset).Select(i => i.Label).ToList();
        }

        /// <summary>The names the interpreter finds for a value of this type: its own methods and those on any value.</summary>
        private static HashSet<string> RuntimeMethods(string type) =>
            LanguageCatalog.Default.MethodsFor(type).Where(m => m.ReceiverType == type || m.ReceiverType == KType.Any).Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        public static TheoryData<string, string, string, string> Receivers => new TheoryData<string, string, string, string>
        {
            { KType.Date, "echo @today()->‸", "types.kscript", "format" },
            { KType.Date, "d = @now()\necho $d->‸", "types.kscript", "add_days" },
            { KType.Date, "<echo>{ @today()->‸ }</echo>", "types.ks.xml", "format" },
            { KType.Date, "---\ntitle: About\n---\n\n# About\n\nToday is { @today()->‸ }.", "about.md", "format" },
            { KType.Number, "n = 5\necho $n->‸", "types.kscript", "round" },
            { KType.Bool, "echo true->‸", "types.kscript", "not" },
        };

        [Theory]
        [MemberData(nameof(Receivers))]
        public void Only_methods_the_interpreter_runs_are_offered(string type, string code, string file, string own)
        {
            List<string> offered = MethodsAfter(code, file);
            HashSet<string> runs = RuntimeMethods(type);

            Assert.Contains(own, offered);
            Assert.Contains("to_string", offered);
            Assert.Contains("to_json", offered);
            Assert.All(offered, name => Assert.True(runs.Contains(name), $"->{name}() is offered on a {type}, which has no such method at run time."));
            foreach (string text in new[] { "json", "upper", "count", "split", "to_date" })
            {
                Assert.DoesNotContain(text, offered);
            }
        }

        [Theory]
        [InlineData("echo @today()->json()", "->json() works on strings, but this value is a date. To write it as JSON text, use ->to_json().")]
        [InlineData("echo @today()->upper()", "->upper() works on strings, but this value is a date. Use ->to_string()->upper() to work on its text.")]
        [InlineData("n = 5\necho $n->upper()", "->upper() works on strings, but this value is a number. Use ->to_string()->upper() to work on its text.")]
        public void String_methods_on_values_that_arent_text_are_reported(string code, string message)
        {
            Diagnostic d = Assert.Single(Analyze(code, "types.kscript").Diagnostics);
            Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
            Assert.Contains(message, d.Message, StringComparison.Ordinal);
        }

        /// <summary>A package that gives dates a ->json() of their own, as someone extending KScript could.</summary>
        internal sealed class DateJsonPackage : KScriptPackage
        {
            public override string Name => "KScript.DateJson";

            public override string Description => "Dates written as JSON, for tests.";

            protected override void Register(PackageBuilder p) =>
                p.Method(KType.Date, "json", "indent?", "The date as a JSON string.", KType.String, (target, call) => null);
        }

        [Fact]
        public void A_method_a_package_adds_to_dates_is_offered_once_imported()
        {
            var registry = new PackageRegistry();
            foreach (var (name, type, description) in PackageRegistry.BuiltIn.Skip(1)) registry.Register(name, type, description);
            registry.Register(new DateJsonPackage());
            var service = new LanguageService(new LanguageCatalog(registry));

            (string text, int offset) = WithCaret("import KScript.DateJson\n\necho @today()->‸");
            List<string> offered = service.GetCompletions(service.Analyze(text, "types.kscript"), offset).Select(i => i.Label).ToList();
            Assert.Contains("json", offered);
            Assert.DoesNotContain("upper", offered);
            AssertClean(service.Analyze("import KScript.DateJson\n\necho @today()->json()", "types.kscript"));
            // Without it, the date has no ->json() and the editor says so.
            Assert.Contains(Service.Analyze("echo @today()->json()", "types.kscript").Diagnostics, d => d.Code == DiagnosticCodes.TypeMismatch);
        }

        [Theory]
        [InlineData("echo @today()->to_json()")]
        [InlineData("echo @today()->to_string()->upper()")]
        [InlineData("echo @today()->format('d MMMM yyyy')")]
        [InlineData("n = 5\necho $n->to_string()->count()")]
        public void Methods_the_interpreter_runs_are_clean(string code) => AssertClean(Analyze(code, "types.kscript"));
    }
}
