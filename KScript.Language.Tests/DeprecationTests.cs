using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Language.Features;
using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>A package with something of every kind deprecated, as a package would mark it.</summary>
    internal sealed class OldPackage : KScriptPackage
    {
        public override string Name => "KScript.Old";

        public override string Description => "Things on their way out, for tests.";

        protected override void Register(PackageBuilder p)
        {
            p.Command("old_sum", "a, b", "Adds two numbers.", KType.Number, call => null);
            p.Command("fetch_text", "url, timeout?, retries?, recursive?: bool", "Fetches text.", KType.String, call => null);
            p.Method(KType.String, "shout", "", "Upper-cases text.", KType.String, (target, call) => null);
            p.Method(KType.String, "whisper", "volume?", "Lower-cases text.", KType.String, (target, call) => null);
            p.Element(new ElementInfo("old-beep", "Beeps.", ContentKind.Empty), run => null);
            p.Element(new ElementInfo("beep", "Beeps.", ContentKind.Empty,
                new AttributeInfo("sound", AttributeKind.Template, "The sound."),
                new AttributeInfo("tone", AttributeKind.Template, "The sound, by its old name.")), run => null);

            p.Deprecate("@old_sum", "Sums moved to KScript.Math", "@sum");
            p.Deprecate("@fetch_text(retries)", "Requests are retried for you");
            p.Deprecate("@fetch_text(recursive)", "");
            p.Deprecate("string->shout", "", "->upper");
            p.Deprecate("string->whisper(volume)", "It was never used");
            p.Deprecate("<old-beep>", "Use the shorter name", "<beep>");
            p.Deprecate("<beep tone>", "Renamed", "sound");
        }
    }

    /// <summary>
    /// Deprecated commands, methods, elements, attributes and parameters: <c>kscript check</c> warns (KS4008) with
    /// why and what to use instead, the editor strikes them through, and hover says so.
    /// </summary>
    public class DeprecationTests
    {
        private static readonly LanguageCatalog Catalog = CatalogWith(new OldPackage());

        private static readonly LanguageService Service = new LanguageService(Catalog, EmptyFileSystem.Instance);

        private static LanguageCatalog CatalogWith(KScriptPackage package)
        {
            var registry = new PackageRegistry();
            foreach (var (name, type, description) in PackageRegistry.BuiltIn.Skip(1)) registry.Register(name, type, description);
            registry.Register(package);
            return new LanguageCatalog(registry);
        }

        private static List<Diagnostic> Deprecations(string text, string path = "test.ks.xml") =>
            Service.Analyze(text, path).Diagnostics.Where(d => d.Code == DiagnosticCodes.Deprecated).ToList();

        [Fact]
        public void Calls_to_deprecated_commands_methods_and_parameters_are_warned_about()
        {
            string text = Script("""
                <import package="KScript.Old" />
                <echo>{ @old_sum(1, 2) }</echo>
                <echo>{ @fetch_text('u', 5, 3) }</echo>
                <echo>{ @fetch_text('u', retries: 2) }</echo>
                <echo>{ @fetch_text('u', recursive) }</echo>
                <echo>{ @fetch_text('u', timeout: 1) }</echo>
                <echo>{ 'a'->shout() }</echo>
                <echo>{ 'a'->whisper(volume: 1) }</echo>
                <echo>{ 'a'->whisper() }</echo>
                """);
            List<Diagnostic> found = Deprecations(text);
            Assert.All(found, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
            Assert.Equal(new[]
            {
                "@old_sum is deprecated. Sums moved to KScript.Math. Use @sum instead.",
                "The 'retries' parameter of @fetch_text is deprecated. Requests are retried for you.",
                "The 'retries' parameter of @fetch_text is deprecated. Requests are retried for you.",
                "The 'recursive' flag of @fetch_text is deprecated.",
                "->shout is deprecated. Use ->upper instead.",
                "The 'volume' parameter of ->whisper is deprecated. It was never used.",
            }, found.Select(d => d.Message));

            // A plain rename comes with a quick fix.
            Diagnostic sum = found[0];
            Assert.Equal("old_sum", text.Substring(sum.Span.Start, sum.Span.Length));
            Assert.Equal("Change to sum", Assert.Single(sum.Fixes).Title);
            Assert.Equal("sum", sum.Fixes[0].Changes[0].NewText);
            Assert.Equal("Change to upper", Assert.Single(found[4].Fixes).Title);
            Assert.Empty(found[1].Fixes);
            Assert.Equal("3", text.Substring(found[1].Span.Start, found[1].Span.Length));
        }

        [Fact]
        public void Deprecated_elements_and_attributes_are_warned_about_in_both_formats()
        {
            string xml = Script("""
                <import package="KScript.Old" />
                <old-beep />
                <beep tone="ping" />
                <beep sound="ping" />
                """);
            List<Diagnostic> found = Deprecations(xml);
            Assert.Equal(new[]
            {
                "<old-beep> is deprecated. Use the shorter name. Use <beep> instead.",
                "The 'tone' attribute of <beep> is deprecated. Renamed. Use sound instead.",
            }, found.Select(d => d.Message));
            Assert.Equal("Change to beep", Assert.Single(found[0].Fixes).Title);
            Assert.Equal("Change to sound", Assert.Single(found[1].Fixes).Title);

            // The rename isn't offered when the element already has the new attribute.
            Diagnostic both = Assert.Single(Deprecations(Script("<import package=\"KScript.Old\" />\n<beep tone=\"a\" sound=\"b\" />")));
            Assert.Empty(both.Fixes);

            List<Diagnostic> code = Deprecations("import KScript.Old\nold-beep\nbeep(tone: ping)\necho \"{ @old_sum(1, 2) }\"\n", "test.kscript");
            Assert.Equal(3, code.Count);
            Assert.Contains(code, d => d.Message.StartsWith("The 'tone' attribute", StringComparison.Ordinal));
        }

        [Fact]
        public void Hover_and_completion_show_deprecations()
        {
            (string text, int offset) = WithCaret(Script("<import package=\"KScript.Old\" />\n<echo>{ @old_s‸um(1, 2) }</echo>"));
            HoverResult hover = Service.GetHover(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.Contains("**Deprecated.** Sums moved to KScript.Math. Use @sum instead.", hover.Markdown);

            (text, offset) = WithCaret(Script("<import package=\"KScript.Old\" />\n<echo>{ @fetch_text('u', retr‸ies: 2) }</echo>"));
            Assert.Contains("**Deprecated.** Requests are retried for you.", Service.GetHover(Service.Analyze(text, "test.ks.xml"), offset).Markdown);

            (text, offset) = WithCaret(Script("<import package=\"KScript.Old\" />\n<echo>{ @‸ }</echo>"));
            List<CompletionItem> commands = Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset);
            CompletionItem oldSum = commands.Single(i => i.Label.TrimStart('@') == "old_sum");
            Assert.True(oldSum.Deprecated);
            Assert.False(commands.Single(i => i.Label.TrimStart('@') == "fetch_text").Deprecated);
            Assert.StartsWith("9", oldSum.SortText);

            (text, offset) = WithCaret(Script("<import package=\"KScript.Old\" />\n<echo>{ @fetch_text('u', ‸) }</echo>"));
            List<CompletionItem> names = Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.True(names.Single(i => i.Label == "retries:").Deprecated);
            Assert.False(names.Single(i => i.Label == "timeout:").Deprecated);
            Assert.True(names.Single(i => i.Label == "recursive").Deprecated);

            (text, offset) = WithCaret(Script("<import package=\"KScript.Old\" />\n<echo>{ 'a'->‸ }</echo>"));
            Assert.True(Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset).Single(i => i.Label == "shout").Deprecated);

            (text, offset) = WithCaret(Script("<import package=\"KScript.Old\" />\n<‸"));
            List<CompletionItem> elements = Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.True(elements.Single(i => i.Label == "old-beep").Deprecated);
            Assert.Contains("**Deprecated.** Use the shorter name. Use <beep> instead.", elements.Single(i => i.Label == "old-beep").Documentation);

            (text, offset) = WithCaret(Script("<import package=\"KScript.Old\" />\n<beep ‸ />"));
            CompletionItem tone = Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset).Single(i => i.Label == "tone");
            Assert.True(tone.Deprecated);
            Assert.Contains("(deprecated)", tone.Detail);

            (text, offset) = WithCaret(Script("<import package=\"KScript.Old\" />\n<beep to‸ne=\"a\" />"));
            Assert.Contains("**Deprecated.** Renamed. Use sound instead.", Service.GetHover(Service.Analyze(text, "test.ks.xml"), offset).Markdown);
        }

        [Fact]
        public void Deprecate_names_something_that_exists()
        {
            var package = new BrokenDeprecations();
            var registry = new PackageRegistry();
            registry.Register(package);
            InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => _ = package.Commands);
            Assert.Contains(package.Problem, e.Message);
        }

        [Theory]
        [InlineData("@nope", "has no command or method @nope")]
        [InlineData("string->nope", "has no command or method string->nope")]
        [InlineData("nope", "has no command or method nope")]
        [InlineData("@thing(nope)", "@thing has no parameter 'nope'")]
        [InlineData("<nope>", "has no element or attribute <nope>")]
        [InlineData("<thing-el nope>", "has no element or attribute <thing-el nope>")]
        public void Each_kind_of_bad_key_is_rejected(string key, string message)
        {
            var package = new BrokenDeprecations(key);
            Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => _ = package.Commands).Message);
        }

        [Fact]
        public void Messages_read_well_with_or_without_a_reason_or_replacement()
        {
            Assert.Equal("@a is deprecated.", new Deprecation("").Describe("@a"));
            Assert.Equal("@a is deprecated. Old.", new Deprecation("Old.").Describe("@a"));
            Assert.Equal("@a is deprecated. Use @b instead.", new Deprecation(null, " @b ").Describe("@a"));
            Assert.Equal("b", new Deprecation("", "@b()").RenameTo);
            Assert.Equal("beep", new Deprecation("", "<beep>").RenameTo);
            Assert.Equal("upper", new Deprecation("", "->upper").RenameTo);
            Assert.Null(new Deprecation("", "a sentence, not a name").RenameTo);
            Assert.Null(new Deprecation("").RenameTo);
            Assert.Equal("**Deprecated.**\n\n", LanguageService.DeprecationLine(new Deprecation(""), "@a"));
            Assert.Equal(string.Empty, LanguageService.DeprecationLine(null, "@a"));
        }

        private sealed class BrokenDeprecations : KScriptPackage
        {
            private readonly string key;

            public BrokenDeprecations(string key = "@thing(nope)") => this.key = key;

            public string Problem => "has no parameter 'nope'";

            public override string Name => "KScript.Broken";

            public override string Description => "Deprecates what isn't there.";

            protected override void Register(PackageBuilder p)
            {
                p.Command("thing", "a", "A thing.", KType.Any, call => null);
                p.Element(new ElementInfo("thing-el", "A thing.", ContentKind.Empty), run => null);
                p.Deprecate(key, "Gone");
            }
        }
    }
}
