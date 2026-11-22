using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Language.Features;

namespace KScript.Language.Tests
{
    /// <summary>
    /// KScript.Http views (views/*.html): the variables a controller gives them with @view, a page's
    /// &lt;import package&gt; variables such as $session, and the fields packages say their commands return.
    /// </summary>
    public class ViewTemplateTests
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "kscript-views"));

        private static string At(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        private const string Controller =
            "import KScript.Http\n" +
            "import KScript.Auth\n\n" +
            "fn account() =>\n" +
            "  @view('auth/account', data: { user: @user(), tokens: @api_tokens(@user()) })\n\n" +
            "fn make_token(form: map) {\n" +
            "  token = @api_token(@user(), name: $form.name ?? 'token')\n" +
            "  return @view('auth/account', data: { user: @user(), new_token: $token })\n" +
            "}\n\n" +
            "fn show(id: string) => @view('people/show.html', { person: { name: 'Ada', age: 36 } }, title: 'Person')\n";

        private const string Account =
            "<import package=\"KScript.Auth\" />\n" +
            "<layout path=\"/_layout.html\" title=\"Your account\">\n" +
            "  <if test=\"$session->has('status')\"><p>{ $session->get('status') }</p></if>\n" +
            "  <p>{ $data.user.email }</p>\n" +
            "  <for each=\"t\" in=\"$data.tokens ?? []\">\n" +
            "    <li>{ $t.name }, made { $t.created_at }</li>\n" +
            "  </for>\n" +
            "</layout>\n";

        private static MemoryFiles Files() => new MemoryFiles()
            .Add(At("kscript.json"), "{ \"name\": \"site\" }")
            .Add(At("_layout.html"), "<html><body><slot /></body></html>")
            .Add(At("controllers", "accounts.kscript"), Controller)
            .Add(At("views", "auth", "account.html"), Account)
            .Add(At("views", "people", "show.html"), "<h1>{ $title }</h1><p>{ $person.name }</p>");

        private static (LanguageService Service, DocumentAnalysis Analysis, int Offset) Open(string file, string textWithCaret)
        {
            ViewInputs.ClearCache();
            (string text, int offset) = textWithCaret.Contains('‸') ? TestHelpers.WithCaret(textWithCaret) : (textWithCaret, 0);
            MemoryFiles files = Files().Add(file, text);
            LanguageService service = new LanguageService(files: files);
            return (service, service.Analyze(text, file), offset);
        }

        private static List<string> Complete(string file, string textWithCaret)
        {
            var (service, analysis, offset) = Open(file, textWithCaret);
            return service.GetCompletions(analysis, offset).Select(i => i.Label).ToList();
        }

        private static string View(string line) => Account.Replace("  <p>{ $data.user.email }</p>\n", line + "\n", StringComparison.Ordinal);

        [Fact]
        public void A_view_has_no_problems_when_its_controller_gives_it_what_it_uses()
        {
            var (_, analysis, _) = Open(At("views", "auth", "account.html"), Account);
            Assert.Empty(analysis.Diagnostics);
        }

        [Fact]
        public void The_values_a_controller_passes_are_the_views_variables()
        {
            List<string> labels = Complete(At("views", "auth", "account.html"), View("  <p>{ $‸ }</p>"));
            Assert.Contains("data", labels);
            Assert.Contains("session", labels);
            Assert.Contains("request", labels);
        }

        [Fact]
        public void Fields_of_the_data_come_from_every_call_that_renders_the_view()
        {
            List<string> labels = Complete(At("views", "auth", "account.html"), View("  <p>{ $data.‸ }</p>"));
            Assert.Equal(new[] { "new_token", "tokens", "user" }, labels.OrderBy(l => l, StringComparer.Ordinal));
        }

        [Fact]
        public void A_commands_described_result_gives_its_fields()
        {
            List<string> labels = Complete(At("views", "auth", "account.html"), View("  <p>{ $data.user.‸ }</p>"));
            Assert.Contains("email", labels);
            Assert.Contains("id", labels);
        }

        [Fact]
        public void A_loop_over_a_passed_list_knows_its_items_fields()
        {
            string text = Account.Replace("{ $t.name }", "{ $t.‸ }", StringComparison.Ordinal);
            List<string> labels = Complete(At("views", "auth", "account.html"), text);
            Assert.Equal(new[] { "created_at", "expires_at", "id", "last_used_at", "name" }, labels.OrderBy(l => l, StringComparer.Ordinal));
        }

        [Fact]
        public void An_imported_packages_variable_has_its_methods()
        {
            List<string> labels = Complete(At("views", "auth", "account.html"), View("  <p>{ $session->‸ }</p>"));
            Assert.Contains("flash", labels);
            Assert.Contains("get", labels);
            Assert.DoesNotContain("upper", labels);
        }

        [Fact]
        public void A_package_variable_needs_the_pages_import()
        {
            string text = Account.Replace("<import package=\"KScript.Auth\" />\n", string.Empty, StringComparison.Ordinal)
                .Replace("  <p>{ $data.user.email }</p>\n", "  <p>{ $‸ }</p>\n", StringComparison.Ordinal);
            Assert.DoesNotContain("session", Complete(At("views", "auth", "account.html"), text));
        }

        [Fact]
        public void A_mistyped_field_of_the_data_is_reported()
        {
            var (_, analysis, _) = Open(At("views", "auth", "account.html"), View("  <p>{ $data.tokns }</p>"));
            Assert.Contains(analysis.Diagnostics, d => d.Code == DiagnosticCodes.UnknownField && d.Message.Contains("Did you mean tokens?", StringComparison.Ordinal));
        }

        [Fact]
        public void Hover_says_where_a_views_variable_comes_from_and_go_to_definition_opens_it()
        {
            string text = View("  <p>{ $da‸ta.user }</p>");
            var (service, analysis, offset) = Open(At("views", "auth", "account.html"), text);
            string hover = service.GetHover(analysis, offset).Markdown;
            Assert.Contains("controllers/accounts.kscript (account(), make_token())", hover, StringComparison.Ordinal);
            Assert.Contains("tokens: list<", hover, StringComparison.Ordinal);

            SourceLocation definition = Assert.Single(service.GetDefinition(analysis, offset));
            Assert.Equal(At("controllers", "accounts.kscript"), definition.Path);
            Assert.Equal(4, definition.Start.Line);
        }

        [Fact]
        public void A_map_given_without_a_name_spreads_its_keys_and_the_extension_may_be_named()
        {
            List<string> labels = Complete(At("views", "people", "show.html"), "<h1>{ $‸ }</h1>");
            Assert.Contains("person", labels);
            Assert.Contains("title", labels);
            Assert.Contains("age", Complete(At("views", "people", "show.html"), "<p>{ $person.‸ }</p>"));
        }

        [Fact]
        public void A_view_nothing_renders_keeps_its_variables_as_inputs()
        {
            var (_, analysis, _) = Open(At("views", "other.html"), "<p>{ $thing }</p>");
            Assert.Empty(analysis.Diagnostics);
            Assert.Equal(new[] { "thing" }, analysis.Inputs.Keys);
        }

        [Fact]
        public void A_page_outside_a_project_has_no_controller()
        {
            string page = Path.Combine(Path.GetTempPath(), "kscript-no-project", "views", "show.html");
            DocumentAnalysis analysis = Analyzer.Analyze("<p>{ $person.name }</p>", page, new MemoryFiles().Add(page, "<p>{ $person.name }</p>"));
            Assert.Equal(new[] { "person" }, analysis.Inputs.Keys);
        }

        [Fact]
        public void A_call_outside_a_function_names_only_its_file()
        {
            ViewInputs.ClearCache();
            MemoryFiles files = Files().Add(At("preview.kscript"), "import KScript.Http\n\npage = @view('solo', title: 'Hi')\n");
            LanguageService service = new LanguageService(files: files);
            (string text, int offset) = TestHelpers.WithCaret("<h1>{ $ti‸tle }</h1>");
            string file = At("views", "solo.html");
            files.Add(file, text);
            Assert.EndsWith("Given to this view by @view in preview.kscript.", service.GetHover(service.Analyze(text, file), offset).Markdown, StringComparison.Ordinal);
        }

        [Fact]
        public void A_scripts_calls_are_kept_until_it_changes()
        {
            int limit = ViewInputs.CacheLimit;
            try
            {
                ViewInputs.ClearCache();
                MemoryFiles files = Files();
                string view = At("views", "people", "show.html");
                Assert.Contains("person", Analyzer.Analyze("<p>{ $person }</p>", view, files).Symbols.Select(s => s.Name));
                Assert.Contains("person", Analyzer.Analyze("<p>{ $person }</p>", view, files).Symbols.Select(s => s.Name));

                files.Add(At("controllers", "accounts.kscript"), Controller.Replace("person:", "someone:", StringComparison.Ordinal));
                ViewInputs.CacheLimit = 0;
                List<string> names = Analyzer.Analyze("<p>{ $someone }</p>", view, files).Symbols.Select(s => s.Name).ToList();
                Assert.Contains("someone", names);
                Assert.DoesNotContain("person", names);
            }
            finally
            {
                ViewInputs.CacheLimit = limit;
                ViewInputs.ClearCache();
            }
        }

        [Fact]
        public void View_names_are_the_path_in_the_views_folder_or_the_site()
        {
            HashSet<string> names = ViewInputs.NamesOf(At("views", "auth", "account.html"), Root);
            Assert.Equal(new[] { "auth/account", "auth/account.html", "views/auth/account", "views/auth/account.html" }, names.OrderBy(n => n, StringComparer.Ordinal));
            Assert.Empty(ViewInputs.NamesOf(Path.Combine(Path.GetTempPath(), "elsewhere.html"), Root));
        }

        [Theory]
        [InlineData("string", "string")]
        [InlineData("int", "number")]
        [InlineData("string?", "string")]
        [InlineData("any", "any")]
        [InlineData("{ id: string, tags: list<string> }", "{ id: string, tags: list<string> }")]
        [InlineData("list<{ id: string }>", "list<{ id: string }>")]
        [InlineData("{ id: string }[]", "list<{ id: string }>")]
        [InlineData("{ }", "map")]
        public void Shapes_are_read_like_types(string text, string display)
        {
            Assert.Equal(display, ShapeText.Parse(text).Display());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{ id string }")]
        [InlineData("{ id: }")]
        [InlineData("{ id: string")]
        [InlineData("list<string")]
        [InlineData("list<>")]
        [InlineData("string extra")]
        [InlineData("?")]
        public void Shapes_that_are_not_written_right_are_null(string text)
        {
            Assert.Null(ShapeText.Parse(text));
        }

        [Fact]
        public void Every_described_result_is_read_and_matches_its_return_type()
        {
            List<CallableInfo> described = LanguageCatalog.Default.Commands.Concat(LanguageCatalog.Default.Methods).Where(c => c.ReturnShape != null).ToList();
            Assert.NotEmpty(described);
            foreach (CallableInfo c in described)
            {
                TypeShape shape = ShapeText.Parse(c.ReturnShape);
                Assert.True(shape != null, $"{c.Prefix}{c.Name}'s result shape '{c.ReturnShape}' isn't written like a type.");
                Assert.True(shape.Type == c.ReturnType, $"{c.Prefix}{c.Name} returns {c.ReturnType}, but its result shape is a {shape.Type}.");
            }
        }
    }
}
