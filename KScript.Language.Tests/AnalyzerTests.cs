using KScript.Language.Analysis;
using KScript.Syntax.Markup;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    public class AnalyzerTests
    {
        /// <summary>Each snippet must produce exactly one diagnostic with the code, on the marked text.</summary>
        [Theory]
        // Elements
        [InlineData("<ehco>hi</ehco>", DiagnosticCodes.UnknownElement, "ehco", "Did you mean <echo>?")]
        [InlineData("<def id=\"x\" />", DiagnosticCodes.RetiredElement, "def", "Use <let")]
        [InlineData("<afor from=\"a\" to=\"b\"></afor>", DiagnosticCodes.RetiredElement, "afor", "<for each")]
        [InlineData("<Echo>hi</Echo>", DiagnosticCodes.UnknownElement, "Echo", "Did you mean <echo>?")]
        [InlineData("<echo colour=\"red\">hi</echo>", DiagnosticCodes.UnknownAttribute, "colour", null)]
        [InlineData("<for each=\"i\" in=\"0..3\" indx=\"n\"><echo>$i</echo></for>", DiagnosticCodes.UnknownAttribute, "indx", "Did you mean 'index'?")]
        [InlineData("<if condition=\"yes\"></if>", DiagnosticCodes.RetiredAttribute, "condition", "Use test=")]
        [InlineData("<for in=\"0..3\"></for>", DiagnosticCodes.MissingAttribute, "for", "'each'")]
        [InlineData("<echo format=\"html\">x</echo>", DiagnosticCodes.InvalidAttributeValue, "html", "text, json, markdown")]
        [InlineData("<let name=\"c\" value=\"1\" const=\"maybe\" />", DiagnosticCodes.InvalidAttributeValue, "maybe", "yes or no")]
        [InlineData("<elif test=\"true\"></elif>", DiagnosticCodes.MisplacedElement, "elif", "straight after <if>")]
        [InlineData("<let name=\"a\" value=\"1\" /><else></else>", DiagnosticCodes.MisplacedElement, "else", null)]
        [InlineData("<while test=\"false\"></while><else></else>", DiagnosticCodes.MisplacedElement, "else", "<for>")]
        [InlineData("<case is=\"1\"></case>", DiagnosticCodes.MisplacedElement, "case", "inside <match>")]
        [InlineData("<return value=\"1\" />", DiagnosticCodes.MisplacedElement, "return", "inside <fn>")]
        [InlineData("<break />", DiagnosticCodes.MisplacedElement, "break", "inside a loop")]
        [InlineData("<for each=\"i\" in=\"0..3\"><fn name=\"f\"><continue /></fn></for>", DiagnosticCodes.MisplacedElement, "continue", null)]
        [InlineData("<catch as=\"e\"></catch>", DiagnosticCodes.MisplacedElement, "catch", "inside <try>")]
        [InlineData("plain text", DiagnosticCodes.StrayText, "plain text", "<echo>")]
        // Variables
        [InlineData("<echo>$nobody</echo>", DiagnosticCodes.UndefinedVariable, "nobody", null)]
        [InlineData("<let name=\"total\" value=\"1\" /><echo>{ $totl }</echo>", DiagnosticCodes.UndefinedVariable, "totl", "Did you mean $total?")]
        [InlineData("<echo>$x</echo><let name=\"x\" value=\"1\" />", DiagnosticCodes.UndefinedVariable, "x", null)]
        [InlineData("<let name=\"x\" value=\"$x + 1\" />", DiagnosticCodes.UndefinedVariable, "x", null)]
        [InlineData("<if test=\"true\"><let name=\"inner\" value=\"1\" /></if><echo>$inner</echo>", DiagnosticCodes.UndefinedVariable, "inner", null)]
        [InlineData("<for each=\"i\" in=\"0..3\"></for><echo>$i</echo>", DiagnosticCodes.UndefinedVariable, "i", null)]
        [InlineData("<let name=\"f\" value=\"fn($a) => $a + $b\" />", DiagnosticCodes.UndefinedVariable, "b", null)]
        [InlineData("<let name=\"f\" value=\"fn($a) => $a\" /><echo>$a</echo>", DiagnosticCodes.UndefinedVariable, "a", null)]
        [InlineData("<set name=\"ghost\" value=\"1\" />", DiagnosticCodes.SetUndeclared, "ghost", "<let")]
        [InlineData("<do>$ghost = 1</do>", DiagnosticCodes.SetUndeclared, "ghost", null)]
        [InlineData("<let name=\"c\" value=\"1\" const=\"yes\" /><set name=\"c\" value=\"2\" />", DiagnosticCodes.AssignConst, "c", "constant")]
        [InlineData("<let name=\"c\" value=\"1\" const=\"yes\" /><do>c += 1</do>", DiagnosticCodes.AssignConst, "c", null)]
        [InlineData("<set name=\"args\" value=\"1\" />", DiagnosticCodes.AssignConst, "args", "provided by KScript")]
        [InlineData("<let name=\"2x\" value=\"1\" />", DiagnosticCodes.InvalidAttributeValue, "2x", "isn't a valid name")]
        [InlineData("<let name=\"and\" value=\"1\" />", DiagnosticCodes.InvalidAttributeValue, "and", "keyword")]
        [InlineData("<fn name=\"f\" params=\"a, a\"></fn>", DiagnosticCodes.DuplicateDeclaration, "a", null)]
        [InlineData("<fn name=\"f\"></fn><fn name=\"f\"></fn>", DiagnosticCodes.DuplicateDeclaration, "f", null)]
        [InlineData("<fn name=\"f\" params=\"a = 1, b\"></fn>", DiagnosticCodes.InvalidAttributeValue, "b", "default")]
        [InlineData("<fn name=\"f\" params=\"a: number\"></fn>", DiagnosticCodes.InvalidAttributeValue, "number", "Unknown type")]
        [InlineData("<call fn=\"nothing\" />", DiagnosticCodes.UndefinedFunction, "nothing", null)]
        [InlineData("<echo>{ ~people }</echo>", DiagnosticCodes.RetiredSigil, "~people", "$people")]
        // Expressions
        [InlineData("<if test=\"$args = 3\"></if>", DiagnosticCodes.AssignmentNotAllowed, "=", "==")]
        [InlineData("<if test=\"$args eq\"></if>", DiagnosticCodes.ExpressionSyntax, "q", null)]
        [InlineData("<if test=\"\"></if>", DiagnosticCodes.ExpressionSyntax, "test=\"\"", null)]
        [InlineData("<echo>{ foo }</echo>", DiagnosticCodes.BareIdentifier, "foo", "'foo'")]
        [InlineData("<let>x</let>", DiagnosticCodes.StatementSyntax, "x", "assignment")]
        [InlineData("<do>1 + 2</do>", DiagnosticCodes.StatementSyntax, "1 + 2", "does nothing")]
        [InlineData("<let name=\"x\" value=\"1\">2</let>", DiagnosticCodes.InvalidAttribute, "value", "both")]
        [InlineData("<let name=\"x\" />", DiagnosticCodes.MissingAttribute, "let", "needs a value")]
        // Calls
        [InlineData("<echo>@nope()</echo>", DiagnosticCodes.UnknownCommand, "@nope", null)]
        [InlineData("<echo>@join_pth('a')</echo>", DiagnosticCodes.UnknownCommand, "@join_pth", "@join_path")]
        [InlineData("<echo>@more_less(1, 'lt', 2)</echo>", DiagnosticCodes.RetiredCommand, "@more_less", "lt")]
        [InlineData("<echo>@iftrue('yes', 'a')</echo>", DiagnosticCodes.RetiredCommand, "@iftrue", "? 'yes' : 'no'")]
        [InlineData("<import package=\"KScript.IO\" /><echo>@read_text()</echo>", DiagnosticCodes.ArgumentCount, "@read_text", "@read_text(path)")]
        [InlineData("<fn name=\"greet\" params=\"who\" value=\"$who\" /><echo>@greet()</echo>", DiagnosticCodes.ArgumentCount, "@greet", "takes 1 argument")]
        [InlineData("<fn name=\"greet\" params=\"who, g = 'Hi'\" value=\"$who\" /><echo>@greet(1, 2, 3)</echo>", DiagnosticCodes.ArgumentCount, "@greet", "1 to 2")]
        [InlineData("<fn name=\"greet\" params=\"who\" value=\"$who\" /><call fn=\"greet\" />", DiagnosticCodes.ArgumentCount, "greet", null)]
        [InlineData("<echo>{ 'abc'->uper() }</echo>", DiagnosticCodes.UnknownMethod, "uper", "->upper()")]
        [InlineData("<echo>{ 'abc'->replace('a') }</echo>", DiagnosticCodes.ArgumentCount, "replace", "replace(old, new)")]
        [InlineData("<echo>{ [1, 2]->upper() }</echo>", DiagnosticCodes.TypeMismatch, "upper", "list")]
        [InlineData("<echo>{ { a: 1 }->push(2) }</echo>", DiagnosticCodes.TypeMismatch, "push", "map")]
        [InlineData("<echo>{ 'a' |> nothing() }</echo>", DiagnosticCodes.UnknownMethod, "nothing", null)]
        [InlineData("<echo>{ 'a' |> upper }</echo>", DiagnosticCodes.ExpressionSyntax, "upper", "upper()")]
        [InlineData("<echo>{ upper('a') }</echo>", DiagnosticCodes.UnknownMethod, "upper", "method")]
        [InlineData("<fn name=\"f\" params=\"x\" value=\"$x\" /><echo>{ f(1) }</echo>", DiagnosticCodes.UnknownCommand, "f", "@f")]
        [InlineData("<echo>{ 'a' |> fn($a, $b) => $a }</echo>", DiagnosticCodes.ArgumentCount, "fn($a, $b) => $a", "one parameter")]
        public void ReportsProblem(string body, string code, string marked, string messagePart)
        {
            DocumentAnalysis a = Analyze(Script(body));
            Diagnostic d = AssertSingle(a, code);
            Assert.True(a.Diagnostics.Count == 1, "Expected only " + code + ":\n" + Describe(a));
            Assert.Equal(marked, a.Source[d.Span]);
            if (messagePart != null)
            {
                Assert.Contains(messagePart, d.Message);
            }
        }

        /// <summary>Valid KScript 2 that must produce no diagnostics at all.</summary>
        [Theory]
        [InlineData("<let name=\"x\" value=\"1\" /><echo>$x { $x + 1 }</echo>")]
        [InlineData("<let>x = 3; y = $x * 2</let><echo>$y</echo>")]
        [InlineData("<let name=\"x\" value=\"1\" /><set>x += 1</set><do>$x = $x * 2; x ??= 0</do>")]
        [InlineData("<let name=\"p\" value=\"{ a: [1, 2] }\" /><do>$p.a[0] = 3</do>")]
        [InlineData("<for each=\"i\" index=\"n\" in=\"0..10 step 2\"><echo>$n: $i</echo><if test=\"$i gt 4\"><break /></if></for>")]
        [InlineData("<let name=\"m\" value=\"{ a: 1 }\" /><for each=\"k, v\" in=\"$m\"><echo>$k=$v</echo></for>")]
        [InlineData("<while test=\"true\"><continue /></while>")]
        [InlineData("<if test=\"true\"></if><elif test=\"false\"></elif><elif test=\"false\"></elif><else></else>")]
        [InlineData("<if test=\"true\"></if>\n  <!-- comment -->\n  <else></else>")]
        [InlineData("<let name=\"v\" value=\"2\" /><match value=\"$v\"><case is=\"1, 2\"></case><case when=\"$v gt 9\"></case><default></default></match>")]
        [InlineData("<echo>@later(1)</echo><fn name=\"later\" params=\"x\" value=\"$x\" />")]
        [InlineData("<fn name=\"f\" params=\"a: int, b: string = 'x'\"><return value=\"$a + $b\" /></fn><echo>{ 1 |> @f() } { @f(1, b: 'y') }</echo>")]
        [InlineData("<fn name=\"twice\" params=\"x\" value=\"$x * 2\" /><echo>{ 3 |> twice() }</echo>")]
        [InlineData("<fn name=\"g\" params=\"x\" value=\"$x\" /><let name=\"r\" value=\"[1]->map(@g)\" />")]
        [InlineData("<let name=\"f\" value=\"fn($x) => $x\" /><echo>{ $f->call(1) } { 1 |> $f }</echo>")]
        [InlineData("<let name=\"names\" value=\"[1, 2] |> map(fn($n) => $n + 1) |> join(', ')\" />")]
        [InlineData("<let name=\"s\" value=\"[3, 1]->sort(fn($a, $b) => $a - $b)->first()\" />")]
        [InlineData("<try><throw type=\"X\" message=\"bad {1}\" /><catch as=\"err\" when=\"$err.type eq 'X'\"><echo>{$err.message} {$err.line}</echo></catch><finally></finally></try>")]
        [InlineData("<import package=\"KScript.Terminal\" /><input to=\"name\">Name: </input><echo>Hi $name</echo>")]
        [InlineData("<echo>email kyle@example.com, it's $args fine</echo>")]
        [InlineData("<echo>Use {{ and }}.</echo>")]
        [InlineData("<let name=\"u\" value=\"null\" /><echo>{ $u?.name ?? 'guest' }</echo>")]
        [InlineData("<let name=\"x\" value=\"match 2 { 1 => 'a', _ => 'b' }\" />")]
        [InlineData("<import package=\"KScript.AI\" /><let name=\"c\" value=\"@chat(system: 'Be kind.')\" /><echo>{ $c->ask('hi') }</echo>")]
        [InlineData("<let name=\"n\" type=\"int\" value=\"'3'\" /><echo>{ $n->round() }</echo>")]
        [InlineData("<marray name=\"cfg\"><item key=\"a\">1</item></marray><echo>$cfg.a</echo>")]
        [InlineData("<if test=\"$args->count() gt 0 and 1 &lt; 2\"></if>")]
        [InlineData("<if test=\"'3' == 3 and '3' !== 3\"></if>")]
        [InlineData("<echo><![CDATA[ raw < text ]]></echo>")]
        [InlineData("<import package=\"KScript.IO\" /><let name=\"f\" value=\"@files('.')\" /><for each=\"x\" in=\"$f\"><echo>{ $x->extension() }</echo></for>")]
        [InlineData("<let name=\"x\" value=\"'a'\" /><echo format=\"markdown\">**$x**</echo>")]
        [InlineData("<render into=\"#out\"><li class=\"{ 1 }\">x</li><input type=\"text\"></input></render>")]
        public void AcceptsValidKScript(string body) => AssertClean(Analyze(Script(body)));

        [Fact]
        public void ScriptsHaveOneRoot()
        {
            DocumentAnalysis a = Analyze("<kscript></kscript><kscript></kscript>");
            AssertSingle(a, DiagnosticCodes.MarkupSyntax);
        }

        [Fact]
        public void RecordsVariableReferences()
        {
            DocumentAnalysis a = Analyze(Script("<let name=\"n\" value=\"1\" /><set name=\"n\" value=\"$n + 1\" /><echo>$n</echo>"));
            Symbol n = a.Symbols.Single(s => s.Name == "n");
            List<SymbolReference> refs = a.ReferencesTo(n).ToList();
            Assert.Equal(4, refs.Count);
            Assert.Single(refs, r => r.IsDeclaration);
            Assert.Equal(2, refs.Count(r => r.IsWrite));
            Assert.All(refs, r => Assert.Equal("n", a.Source[r.Span]));
        }

        [Fact]
        public void InfersShapes()
        {
            DocumentAnalysis a = Analyze(Script(
                "<let name=\"people\" value=\"[{ name: 'Joan', age: 82 }]\" />" +
                "<let name=\"oldest\" value=\"$people->max_by(fn($p) => $p.age)\" />" +
                "<let name=\"names\" value=\"$people->map(fn($p) => $p.name)->join(', ')\" />" +
                "<let name=\"r\" value=\"0..3\" />" +
                "<let name=\"t\" value=\"'a' + 1\" />" +
                "<let name=\"b\" value=\"1 lt 2\" />"));
            AssertClean(a);
            Assert.Equal("list", a.Symbols.Single(s => s.Name == "people").Shape.Type);
            Assert.Equal(new[] { "name", "age" }, a.Symbols.Single(s => s.Name == "people").Shape.Element.Fields.Keys);
            Assert.Equal(new[] { "name", "age" }, a.Symbols.Single(s => s.Name == "oldest").Shape.Fields.Keys);
            Assert.Equal("string", a.Symbols.Single(s => s.Name == "names").Shape.Type);
            Assert.Equal("list<number>", a.Symbols.Single(s => s.Name == "r").Shape.ToString());
            Assert.Equal("string", a.Symbols.Single(s => s.Name == "t").Shape.Type);
            Assert.Equal("bool", a.Symbols.Single(s => s.Name == "b").Shape.Type);
        }

        [Fact]
        public void LoopVariablesGetTheItemShape()
        {
            DocumentAnalysis a = Analyze(Script("<let name=\"people\" value=\"[{ name: 'Joan' }]\" /><for each=\"p\" in=\"$people\"><echo>$p.name</echo></for>"));
            Assert.Equal(new[] { "name" }, a.Symbols.Single(s => s.Name == "p").Shape.Fields.Keys);
        }

        [Fact]
        public void QuickFixesProduceValidScripts()
        {
            string[] broken =
            {
                "<ehco>hi</ehco>",
                "<let name=\"total\" value=\"1\" /><echo>{ $totl }</echo>",
                "<echo>{ 'a'->uper() }</echo>",
                "<if condition=\"true\"></if>",
                "<echo format=\"markdwn\">x</echo>",
                "<def id=\"x\">1</def>",
                "<for each=\"i\" in=\"0..2\" indx=\"n\"></for>",
                "<let name=\"x\" value=\"1\" /><echo>{ x }</echo>",
                "<fn name=\"f\" params=\"x\" value=\"$x\" /><echo>{ f(1) }</echo>",
                "<echo>@join_pth('a')</echo>",
            };
            foreach (string body in broken)
            {
                string text = Script(body);
                DocumentAnalysis a = Analyze(text);
                Diagnostic d = a.Diagnostics.First(x => x.Fixes.Count > 0);
                string fixedText = Apply(text, d.Fixes[0].Changes);
                DocumentAnalysis after = Analyze(fixedText);
                Assert.DoesNotContain(after.Diagnostics, x => x.Code == d.Code && x.Severity == DiagnosticSeverity.Error);
            }
        }

        internal static string Apply(string text, IEnumerable<TextChange> changes)
        {
            foreach (TextChange c in changes.OrderByDescending(c => c.Span.Start))
            {
                text = text.Remove(c.Span.Start, c.Span.Length).Insert(c.Span.Start, c.NewText);
            }
            return text;
        }

        [Fact]
        public void PositionsInsideEntitiesAreExact()
        {
            string text = Script("<if test=\"1 &lt; 2 and $nope\"></if>");
            DocumentAnalysis a = Analyze(text);
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedVariable);
            Assert.Equal(text.IndexOf("nope", StringComparison.Ordinal), d.Span.Start);
        }

        #region Pages

        private sealed class FakeFiles : IFileSystem
        {
            private readonly Dictionary<string, string> files;
            public FakeFiles(Dictionary<string, string> files) => this.files = files.ToDictionary(k => Path.GetFullPath(k.Key), k => k.Value);
            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path));
            public bool DirectoryExists(string path) => files.Keys.Any(f => f.StartsWith(Path.GetFullPath(path) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;
            public IEnumerable<string> ListFiles(string directory, string pattern) =>
                files.Keys.Where(f => Path.GetDirectoryName(f) == Path.GetFullPath(directory) && f.EndsWith(pattern.TrimStart('*'), StringComparison.Ordinal));
        }

        private static readonly string Site = Path.Combine(Path.GetTempPath(), "kscript-site");

        private static FakeFiles SiteFiles() => new FakeFiles(new Dictionary<string, string>
        {
            [Path.Combine(Site, "_layout.html")] = "<html><slot /></html>",
            [Path.Combine(Site, "_components", "people-card.html")] = "<div>{$person.name}</div>",
            [Path.Combine(Site, "_components", "hero.html")] = "<header>{$title}</header>",
            [Path.Combine(Site, "lib", "strings.ks.xml")] = "<kscript><fn name=\"slugify\" params=\"text\" value=\"$text\" /><let name=\"dash\" value=\"'-'\" const=\"yes\" /><let name=\"scratch\" value=\"1\" /></kscript>",
        });

        [Fact]
        public void PagesMixHtmlAndKScript()
        {
            string page = "<!doctype html>\n<let name=\"min\" value=\"$request.query.min ?? 0\" />\n" +
                          "<layout src=\"_layout.html\" title=\"People\">\n<h1 class=\"{ $min gt 0 ? 'x' : '' }\">Over {$min}</h1>\n" +
                          "<input type=\"checkbox\" checked=\"{$min gt 0}\"><br>\n<people-card person=\"{ { name: 'Joan' } }\" />\n" +
                          "<script>if (a < b) {}</script>\n</layout>";
            DocumentAnalysis a = Analyze(page, Path.Combine(Site, "people.html"), SiteFiles());
            AssertClean(a);
            Assert.Contains(a.Links, l => l.Kind == "component" && l.TargetPath.EndsWith("people-card.html"));
            Assert.Contains(a.Links, l => l.Kind == "layout");
        }

        [Fact]
        public void LiveBlocksDeclareStateAndCheckHandlers()
        {
            string page = "<live id=\"todos\">\n<state name=\"items\" value=\"[{ id: 1, text: 'a' }]\" />\n<state name=\"draft\" value=\"''\" />\n" +
                          "<form on-submit=\"items = $items->push({ id: 2, text: $draft->trim() }); draft = ''\"><input bind=\"draft\"></form>\n" +
                          "<for each=\"t\" in=\"$items\"><p on-click=\"items = $items->reject(fn($x) => $x.id eq $t.id)\">{$t.text}</p></for>\n" +
                          "<else><p>Nothing yet.</p></else>\n</live>";
            AssertClean(Analyze(page, Path.Combine(Site, "todo.html"), SiteFiles()));
        }

        [Theory]
        [InlineData("<button on-click=\"n += 1\">+</button>", DiagnosticCodes.MisplacedElement, "on-click")]
        [InlineData("<input bind=\"q\">", DiagnosticCodes.MisplacedElement, "bind")]
        [InlineData("<live id=\"x\"><button on-click=\"nope += 1\">+</button></live>", DiagnosticCodes.SetUndeclared, "nope")]
        [InlineData("<live id=\"x\"><button on-click=\"1 + 2\">+</button></live>", DiagnosticCodes.StatementSyntax, "1 + 2")]
        [InlineData("<live id=\"x\"><input bind=\"ghost\"></live>", DiagnosticCodes.SetUndeclared, "ghost")]
        [InlineData("<state name=\"n\" value=\"0\" />", DiagnosticCodes.MisplacedElement, "state")]
        [InlineData("<live><state name=\"n\" value=\"0\" /></live>", DiagnosticCodes.MissingAttribute, "live")]
        public void LiveBlockProblems(string page, string code, string marked)
        {
            DocumentAnalysis a = Analyze(page, Path.Combine(Site, "p.html"), SiteFiles());
            Diagnostic d = AssertSingle(a, code);
            Assert.Equal(marked, a.Source[d.Span]);
        }

        [Fact]
        public void MatchProvidesIt()
        {
            string page = "<let name=\"c\" value=\"'20'->to_number(default: 0)\" /><match value=\"$c\"><case when=\"$it lt 0\"><p>Cold</p></case><default><p>{ $c->clamp(min: 0, max: 9) }</p></default></match><p>{$it}</p>";
            DocumentAnalysis a = Analyze(page, Path.Combine(Site, "p.html"), SiteFiles());
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedVariable);
            Assert.Equal(page.LastIndexOf("it", StringComparison.Ordinal), d.Span.Start);
        }

        [Fact]
        public void ComponentAttributesAreTemplates()
        {
            DocumentAnalysis a = Analyze("<let name=\"p\" value=\"{ name: 'J' }\" /><people-card person=\"{$p}\" title=\"Hi {$p.name}\" />", Path.Combine(Site, "p.html"), SiteFiles());
            AssertClean(a);
        }

        [Fact]
        public void TheExampleSiteIsClean()
        {
            string site = null;
            for (string dir = AppContext.BaseDirectory; dir != null && site == null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "Example Sites", "team");
                site = File.Exists(Path.Combine(dir, "KScript.sln")) && Directory.Exists(candidate) ? candidate : null;
            }
            if (site == null)
            {
                return;
            }
            foreach (string file in Directory.EnumerateFiles(site, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".html") || f.EndsWith(".md")))
            {
                DocumentAnalysis a = Analyzer.Analyze(File.ReadAllText(file), file, DiskFileSystem.Instance);
                Assert.True(!a.Errors.Any(), file + ":\n" + Describe(a));
            }
        }

        [Fact]
        public void PagesReportUnknownComponentsAndMissingFiles()
        {
            string page = "<layout src=\"_missing.html\"><people-crd person=\"1\" /><p>{ $nope }</p></layout>";
            DocumentAnalysis a = Analyze(page, Path.Combine(Site, "x.html"), SiteFiles());
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.InvalidAttributeValue && d.Message.Contains("_missing.html"));
            Diagnostic component = a.Diagnostics.Single(d => d.Code == DiagnosticCodes.UnknownElement);
            Assert.Contains("people-card", component.Message);
            AssertSingle(a, DiagnosticCodes.UndefinedVariable);
        }

        [Fact]
        public void ComponentsWithoutAHyphenMatchThePageEngine()
        {
            // Same rule as kscript serve (PageElementNames): <hero> is a component because _components/hero.html exists;
            // an unknown hyphenless name is left alone, and HTML or KScript element names are never components.
            DocumentAnalysis a = Analyze("<hero title=\"Hi\" /><banner /><section><p>x</p></section>", Path.Combine(Site, "x.html"), SiteFiles());
            AssertClean(a);
            Assert.Contains(a.Links, l => l.Kind == "component" && l.TargetPath.EndsWith("hero.html"));
            Assert.Single(a.Links, l => l.Kind == "component");
        }

        [Fact]
        public void PartialsGetVariablesFromTheirCaller()
        {
            DocumentAnalysis a = Analyze("<div>{$person.name}</div>", Path.Combine(Site, "_components", "people-card.html"), SiteFiles());
            AssertClean(a);
            Assert.True(a.IsPartial);
        }

        [Fact]
        public void HtmlElementsWinInPages()
        {
            // <input> and <menu> are HTML in pages, so their attributes aren't KScript attributes.
            DocumentAnalysis a = Analyze("<input name=\"q\" value=\"\"><menu><li>a</li></menu>", Path.Combine(Site, "p.html"), SiteFiles());
            AssertClean(a);
        }

        [Fact]
        public void ScriptOnlyElementsAreFlaggedInPages()
        {
            DocumentAnalysis a = Analyze("<echo>hi</echo><clear />", Path.Combine(Site, "p.html"), SiteFiles());
            // <echo> and <clear> are script-only, so in a page they are plain (unknown) HTML.
            AssertClean(a);
        }

        [Fact]
        public void BrowserScriptBlocksAreCheckedAsScripts()
        {
            DocumentAnalysis a = Analyze("<script type=\"text/kscript\"><kscript><ehco>x</ehco></kscript></script>", Path.Combine(Site, "p.html"), SiteFiles());
            AssertSingle(a, DiagnosticCodes.UnknownElement);
        }

        [Fact]
        public void MarkdownFrontMatterDeclaresVariables()
        {
            string md = "---\ntitle: Team\nlayout: _layout.html\n---\n# { $title } { $nope }\n<for each=\"p\" in=\"[1]\">\n- {$p}\n</for>\n";
            DocumentAnalysis a = Analyze(md, Path.Combine(Site, "team.md"), SiteFiles());
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedVariable);
            Assert.Equal("nope", a.Source[d.Span]);
            Assert.Single(a.Diagnostics);
            Assert.Contains(a.Links, l => l.Kind == "layout");
        }

        [Fact]
        public void ImportsBringInFunctions()
        {
            string script = Script("<import src=\"lib/strings.ks.xml\" as=\"str\" /><import src=\"lib/strings.ks.xml\" names=\"slugify\" />" +
                                   "<echo>{ 'A B' |> str.slugify() } @slugify('x') { str.titlecase('x') }</echo>");
            DocumentAnalysis a = Analyze(script, Path.Combine(Site, "main.ks.xml"), SiteFiles());
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedFunction);
            Assert.Equal("titlecase", a.Source[d.Span]);
            Symbol slugify = a.Symbols.Single(s => s.Kind == SymbolKind.Function && s.Name == "slugify");
            Assert.Single(slugify.Parameters);
            Assert.True(a.ExternalDeclarations.ContainsKey(slugify));
        }

        [Fact]
        public void ImportsBringInConstants()
        {
            string script = Script("<import src=\"lib/strings.ks.xml\" /><import src=\"lib/strings.ks.xml\" names=\"dash\" as=\"s\" /><echo>{$dash} {$scratch}</echo>");
            DocumentAnalysis a = Analyze(script, Path.Combine(Site, "main.ks.xml"), SiteFiles());
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedVariable);
            Assert.Equal("scratch", a.Source[d.Span]);
            Symbol dash = a.Symbols.First(s => s.Name == "dash");
            Assert.Equal(SymbolKind.Constant, dash.Kind);
            Assert.True(a.ExternalDeclarations.ContainsKey(dash));
        }

        [Fact]
        public void PagesImportScriptsAndHandlersSeeTheEvent()
        {
            string page = "<import src=\"lib/strings.ks.xml\" />\n<live id=\"x\"><state name=\"v\" value=\"''\" />\n" +
                          "<input on-input=\"v = @slugify($event.value) + $dash\"><p>{$v}</p></live>\n<p>{ $event ?? '' }</p>";
            DocumentAnalysis a = Analyze(page, Path.Combine(Site, "p.html"), SiteFiles());
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedVariable);
            Assert.Equal("event", a.Source[d.Span]);
        }

        [Fact]
        public void ImportingAMissingNameIsAnError()
        {
            string script = Script("<import src=\"lib/strings.ks.xml\" names=\"slugfy\" />");
            DocumentAnalysis a = Analyze(script, Path.Combine(Site, "main.ks.xml"), SiteFiles());
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedFunction);
            Assert.Equal("slugify", d.Fixes.Single().Changes.Single().NewText);
        }

        #endregion

        [Fact]
        public void V1ScriptsGetActionableErrors()
        {
            const string v1 = "<kscript throw_exceptions=\"no\">\n  <def id=\"max\">0</def>\n  <for to=\"i\" math=\"@math('$i+1')\" while=\"@more_less($i,'lt',3)\">\n    <update to=\"max\">$i</update>\n  </for>\n  <echo>$max</echo>\n</kscript>";
            DocumentAnalysis a = Analyzer.Analyze(v1, "old.xml");
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.RetiredElement);
            Assert.All(a.Diagnostics.Where(d => d.Code == DiagnosticCodes.RetiredElement || d.Code == DiagnosticCodes.RetiredAttribute),
                d => Assert.Contains("KScript 2", d.Message));
        }
    }
}
