using KScript.Syntax;
using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax.Text;

namespace KScript.Language.Tests
{
    /// <summary>Type-aware completion in pages: JSON data, list items in lambdas and loops, and component props.</summary>
    public class IntelliSenseTests
    {
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

        private static readonly string Site = Path.Combine(Path.GetTempPath(), "kscript-intellisense");

        private const string ScriptComponent = """
            import KScript.Web
            import "../models/note.kscript"

            type NoteCard extends Component {
              record: Note
              caption?: string
              label => $record.title
              fn caption_text(): string => $caption ?? 'Note'
              fn render(): Component {
                return ```html
                <section><h2>{$label}</h2><p>{$this->caption_text()}</p><p>{$record.title}</p><slot /></section>
                ```
              }
            }
            """;

        private static readonly LanguageService Service = new LanguageService(files: new FakeFiles(new Dictionary<string, string>
        {
            [Path.Combine(Site, "_layout.html")] = "<html><slot /></html>",
            [Path.Combine(Site, "_layouts", "details.html")] = "<prop name=\"page-title\" required /><prop name=\"subtitle\" /><h1>{$page_title}</h1><slot />",
            [Path.Combine(Site, "_components", "note.html")] = "<prop name=\"title\" required /><p>{@heading($data.title)}</p>",
            [Path.Combine(Site, "_components", "note.kscript")] = "fn heading(title: string) => $title->upper()\nfn load(props: map) => $props\n",
            [Path.Combine(Site, "data", "people.json")] = "[{\"name\": \"Joan\", \"age\": 82, \"address\": {\"city\": \"Leeds\"}}, {\"name\": \"Ellie\", \"role\": null}]",
            [Path.Combine(Site, "_components", "note-card.kscript")] = ScriptComponent,
            [Path.Combine(Site, "models", "note.kscript")] = "import KScript.Data\nmodel Note { title: string; body: string }",
            [Path.Combine(Site, "_components", "person-card.html")] = "<div class=\"card\"><h2>{$person.name}</h2><p>{$caption ?? ''}</p><slot /></div>",
        }));

        private const string People = "<let name=\"people\" value=\"@read_json('data/people.json')\" />\n";

        private static List<CompletionItem> Complete(string textWithCaret, string file = "index.html")
        {
            int offset = textWithCaret.IndexOf('‸');
            string text = textWithCaret.Remove(offset, 1);
            DocumentAnalysis analysis = Service.Analyze(text, Path.Combine(Site, file));
            return Service.GetCompletions(analysis, offset);
        }

        private static List<string> Labels(string textWithCaret, string file = "index.html") => Complete(textWithCaret, file).Select(i => i.Label).ToList();

        [Theory]
        [InlineData(People + "<p>{$people->first().‸}</p>")]
        [InlineData(People + "<for each=\"p\" in=\"$people\"><p>{$p.‸}</p></for>")]
        [InlineData(People + "<p>{$people->filter(fn(p) => $p.‸)}</p>")]
        [InlineData(People + "<p>{$people |> max_by(fn(p) => $p.‸)}</p>")]
        [InlineData(People + "<p>{$people->reduce(fn(total, p) => $total + $p.‸, initial: 0)}</p>")]
        public void JsonFileFieldsComplete(string page)
        {
            List<string> labels = Labels(page);
            Assert.Equal(new[] { "address", "age", "name", "role" }, labels.Intersect(new[] { "name", "age", "address", "role" }).OrderBy(l => l));
        }

        [Fact]
        public void NestedJsonAndMappedListsHaveTypes()
        {
            Assert.Contains("city", Labels(People + "<p>{$people->first().address.‸}</p>"));
            List<string> methods = Labels(People + "<p>{$people->map(fn(p) => $p.name)->first()->‸}</p>");
            Assert.Contains("upper", methods);
            Assert.DoesNotContain("filter", methods);
        }

        [Fact]
        public void ListMethodsAreOfferedForJsonLists()
        {
            List<string> methods = Labels(People + "<p>{$people->‸}</p>");
            Assert.Contains("filter", methods);
            Assert.DoesNotContain("upper", methods);
        }

        [Fact]
        public void MissingJsonFilesAreStillUnknown()
        {
            Assert.Empty(Labels("<let name=\"x\" value=\"@read_json('data/missing.json')\" />\n<p>{$x.‸}</p>"));
        }

        [Fact]
        public void ComponentPropsCompleteAsAttributes()
        {
            List<CompletionItem> items = Complete("<person-card ‸ />");
            Assert.Equal(new[] { "person", "caption", "onLoad" }, items.Select(i => i.Label));
            Assert.All(items, i => Assert.Equal(CompletionKind.Attribute, i.Kind));
            Assert.Equal(new[] { "caption", "onLoad" }, Labels("<person-card person=\"{$x}\" ‸ />"));
        }

        [Fact]
        public void ComponentSnippetFillsInItsProps()
        {
            CompletionItem card = Complete("<‸").Single(i => i.Label == "person-card");
            Assert.Equal("person-card person=\"$1\" caption=\"$2\" />", card.InsertText);
            Assert.Contains("person, caption", card.Documentation);
        }

        [Fact]
        public void LayoutRequirementsAndComponentCallbacksAreAvailableInHtml()
        {
            const string layout = "<layout path=\"_layouts/details.html\"";
            var missing = Service.Analyze(layout + "><Note title=\"A\" onLoad={fn(data) => $data} /></layout>", Path.Combine(Site, "index.html"));
            Diagnostic required = Assert.Single(missing.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Equal(DiagnosticCodes.MissingAttribute, required.Code);
            Assert.Contains("page-title", required.Message);
            var published = Complete(layout + " ‸></layout>");
            Assert.Contains(published, i => i.Label == "page-title" && i.Detail.Contains("Required"));
            Assert.Contains(published, i => i.Label == "subtitle");
            Assert.Contains(Complete("<Note ‸ />"), i => i.Label == "onLoad");
            Assert.Contains(Service.Analyze("<Note title=\"A\" onLoad=\"hello\" />", Path.Combine(Site, "index.html")).Diagnostics,
                d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("onLoad"));

            var component = Service.Analyze("<prop name=\"title\" required /><p>{@heading($data.title)}</p>", Path.Combine(Site, "_components", "note.html"));
            Assert.DoesNotContain(component.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains(component.ExternalDeclarations.Values, location => location.Path.EndsWith("note.kscript", StringComparison.Ordinal));
            Assert.Contains(Service.GetSemanticTokens(missing), t =>
                t.Type == SemanticTokenType.Keyword && missing.Source[t.Span] == "layout");
            Assert.Contains(Service.GetSemanticTokens(missing), t =>
                t.Type == SemanticTokenType.Type && missing.Source[t.Span] == "Note");
        }

        [Fact]
        public void CompanionFunctionsCalledByTheirTemplateAreNotUnused()
        {
            string script = Path.Combine(Site, "_components", "note.kscript");
            const string functions = """
                fn heading(title: string) => $title->upper()
                fn load(props: map) => $props
                fn spare() => 0
                """;
            DocumentAnalysis analysis = Service.Analyze(functions, script);
            Diagnostic warning = Assert.Single(analysis.Diagnostics, d => d.Code == DiagnosticCodes.UnusedFunction);
            Assert.Contains("spare", warning.Message);
        }

        [Fact]
        public void PageCommandSuggestionsFollowItsImports()
        {
            var local = Labels("<p>{@‸}</p>");
            Assert.Contains("read_json", local);
            Assert.DoesNotContain("api", local);
            Assert.Contains("api", Labels("<import package=\"KScript.Net\" /><p>{@‸}</p>"));
        }

        [Fact]
        public void ScopedSigilCompletesAndKeepsTheVariableNameSpan()
        {
            Assert.Contains("title", Labels("title = 'Draft'\necho $.‸", "title.kscript"));
            Assert.Contains("title", Labels("<let name=\"title\" value=\"'Draft'\" /><p>{$.‸}</p>"));
            Assert.Contains("title", Labels("<let name=\"title\" value=\"'Draft'\" /><p>{$‸}</p>"));
        }

        private const string RouteComponentFile = "_components/site-nav.kscript";
        private const string RouteComponent = """
            import KScript.Web

            type SiteNav extends Component {
              fn render(): Component {
                return ```html
                <a href="{EXPRESSION}">Notes</a>
                ```
              }
            }
            """;

        [Theory]
        [InlineData("@")]
        [InlineData("@ro")]
        public void RouteCompletionCanImportHttpInsideAComponent(string prefix)
        {
            string text = RouteComponent.Replace("EXPRESSION", prefix + "‸");
            CompletionItem route = Assert.Single(Complete(text, RouteComponentFile), item => item.Label == "route");
            Assert.Equal("route($1)", route.InsertText);
            Assert.Equal(prefix.Substring(1), text.Substring(route.ReplaceSpan.Start, route.ReplaceSpan.Length));
            Assert.Equal("\nimport KScript.Http", Assert.Single(route.AdditionalEdits).NewText);

            string imported = "import KScript.Http\n" + text;
            Assert.Null(Assert.Single(Complete(imported, RouteComponentFile), item => item.Label == "route").AdditionalEdits);
        }

        [Fact]
        public void ARouteCallInComponentHtmlOffersAnImportFix()
        {
            string text = RouteComponent.Replace("EXPRESSION", "@route('notes.index')");
            DocumentAnalysis analysis = Service.Analyze(text, Path.Combine(Site, RouteComponentFile));
            Diagnostic missing = Assert.Single(analysis.Errors, d => d.Code == DiagnosticCodes.MissingImport);
            Assert.Equal("@route", analysis.Source[missing.Span]);
            TextChange change = Assert.Single(Assert.Single(missing.Fixes).Changes);
            string fixedText = text.Insert(change.Span.Start, change.NewText);
            Assert.Empty(Service.Analyze(fixedText, Path.Combine(Site, RouteComponentFile)).Errors);
        }

        [Theory]
        [InlineData("SiteNav")]
        [InlineData("name: SiteNav")]
        [InlineData("null")]
        [InlineData("name: ''")]
        [InlineData("'  '")]
        public void ARouteNameMustBeNonemptyTextInsideComponentHtml(string argument)
        {
            string text = "import KScript.Http\n" + RouteComponent.Replace("EXPRESSION", $"@route({argument})");
            DocumentAnalysis analysis = Service.Analyze(text, Path.Combine(Site, RouteComponentFile));
            Diagnostic problem = Assert.Single(analysis.Errors, d => d.Code == DiagnosticCodes.TypeMismatch);
            Assert.Contains("nonempty route name", problem.Message);
        }

        [Theory]
        [InlineData("@route('notes.index')", DiagnosticCodes.MissingImport)]
        [InlineData("@http.route(null)", DiagnosticCodes.TypeMismatch)]
        [InlineData("http.route(null)", DiagnosticCodes.TypeMismatch)]
        [InlineData("null |> http.route()", DiagnosticCodes.TypeMismatch)]
        [InlineData("null |> @http.route()", DiagnosticCodes.TypeMismatch)]
        [InlineData("'' |> @http.route", DiagnosticCodes.TypeMismatch)]
        [InlineData("@http.route(name)", DiagnosticCodes.NamedArgument)]
        public void AliasedRouteCallsUseTheSameChecks(string expression, string diagnostic)
        {
            const string alias = "import KScript.Http as http\n";
            string template = RouteComponent.Replace("EXPRESSION", expression);
            var analysis = Service.Analyze(alias + template, Path.Combine(Site, RouteComponentFile));
            var problem = Assert.Single(analysis.Errors);
            Assert.Equal(diagnostic, problem.Code);
            if (diagnostic == DiagnosticCodes.MissingImport)
            {
                TextChange change = Assert.Single(Assert.Single(problem.Fixes).Changes);
                string repaired = (alias + template).Insert(change.Span.Start, change.NewText);
                Assert.Empty(Service.Analyze(repaired, Path.Combine(Site, RouteComponentFile)).Errors);
            }
        }

        [Theory]
        [InlineData("@route(‸)", true)]
        [InlineData("@route(name: ‸)", true)]
        [InlineData("@route(name: 'admin.‸')", true)]
        [InlineData("@route(name: 'admin.‸)", true)]
        [InlineData("@route(name: 'admin.notes'‸)", false)]
        [InlineData("@route('admin.notes'   ‸, id: 42)", false)]
        public void CompletesNamedRoutesOnlyWhereTheRouteNameCanBeWritten(string expression, bool offersRoute)
        {
            string root = Directory.CreateTempSubdirectory("kscript-route-completion").FullName;
            try
            {
                File.WriteAllText(Path.Combine(root, "kscript.json"), "{}");
                File.WriteAllText(Path.Combine(root, "routes.kscript"), """
                    import KScript.Http
                    Routes {
                      Group(name: 'admin.') {
                        Get(path: /notes, name: 'notes') { return null }
                      }
                    }
                    """);
                var service = new LanguageService();
                string marked = "import KScript.Http\n" + RouteComponent.Replace("EXPRESSION", expression);
                (string text, int offset) = TestHelpers.WithCaret(marked);
                var document = service.Analyze(text, Path.Combine(root, RouteComponentFile));
                var items = service.GetCompletions(document, offset);
                Assert.DoesNotContain(items, item => item.Label is "SiteNav" or "$this" or "@round");
                if (offersRoute)
                {
                    CompletionItem route = Assert.Single(items, i => i.Label == "admin.notes");
                    Assert.Equal("'admin.notes'", route.InsertText);
                    string inserted = text.Remove(route.ReplaceSpan.Start, route.ReplaceSpan.Length).Insert(route.ReplaceSpan.Start, route.InsertText);
                    Assert.Empty(service.Analyze(inserted, Path.Combine(root, RouteComponentFile)).Errors);
                }
                else
                {
                    Assert.Empty(items);
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Theory]
        [InlineData("@route(name: '‸)")]
        [InlineData("@route(name: 'notes.‸')")]
        [InlineData("@route(name: 'SiteNav SiteNav S‸)")]
        [InlineData("@route('@ro‸')")]
        public void ComponentHtmlDoesNotSuggestExpressionsInsideQuotedText(string expression)
        {
            string text = "import KScript.Http\n" + RouteComponent.Replace("EXPRESSION", expression);
            Assert.Empty(Complete(text, RouteComponentFile));
        }

        [Fact]
        public void ClassComponentCompletesPropsOnItsTagAndFieldsInsideTheFence()
        {
            Assert.Equal(new[] { "record", "caption" }, Labels("<note-card ‸ />"));
            var analysis = Service.Analyze(ScriptComponent, Path.Combine(Site, "_components", "note-card.kscript"));
            Assert.Empty(analysis.Errors);
            var fields = Labels(ScriptComponent.Replace("{$record.title}", "{$record.‸}"), "_components/note-card.kscript");
            Assert.Contains("title", fields);
            Assert.Contains("body", fields);
            Assert.Contains("id", fields);
            Assert.Contains("record", Labels(ScriptComponent.Replace("{$record.title}", "{$.‸}"), "_components/note-card.kscript"));
            Assert.Contains("body", Labels(ScriptComponent.Replace("{$record.title}", "{$.record.‸}"), "_components/note-card.kscript"));
            var scoped = Service.Analyze(ScriptComponent.Replace("{$record.title}", "{$.record.title}"),
                Path.Combine(Site, "_components", "note-card.kscript"));
            int sigil = scoped.Source.Text.IndexOf("$.record", StringComparison.Ordinal);
            Assert.Equal("record", scoped.ReferenceAt(sigil)?.Symbol.Name);
            Assert.Equal("record", scoped.ReferenceAt(sigil + 1)?.Symbol.Name);
            var broken = Service.Analyze(ScriptComponent.Replace("{$record.title}", "{$record.titel}"),
                Path.Combine(Site, "_components", "note-card.kscript"));
            Assert.Contains(broken.Diagnostics, diagnostic => diagnostic.Message.Contains("titel"));
        }

        [Fact]
        public void LegacyRecordInputHasAReferenceAndCompletesInsideTheComponent()
        {
            const string text = "<p>{$record.id}</p>";
            var analysis = Service.Analyze(text, Path.Combine(Site, "_components", "legacy.html"));
            Assert.Contains("record", analysis.Inputs.Keys);
            Assert.Equal("record", analysis.ReferenceAt(text.IndexOf("$record", StringComparison.Ordinal))?.Symbol.Name);
            Assert.Contains("record", Labels("<p>{$record.id} {$rec‸}</p>", "_components/legacy.html"));
        }

        [Fact]
        public void ComponentPropsAreItsUndeclaredVariables()
        {
            DocumentAnalysis a = Service.Analyze("<let name=\"local\" value=\"1\" /><p>{$local} {$request.path} {$title} {$title}</p>", Path.Combine(Site, "_components", "x.html"));
            Assert.Equal(new[] { "title" }, a.Inputs.Keys);
        }
    }
}
