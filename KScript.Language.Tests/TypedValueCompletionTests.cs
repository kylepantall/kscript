using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Runtime;

namespace KScript.Language.Tests
{
    /// <summary>
    /// Where a value's type is known, completion offers what that type accepts first: time zones for a timezone,
    /// an enum's values, durations. After @ only commands come, after $ only variables. Hover and signature help say
    /// what a type accepts, and unquoted text such as dd-MM-yyyy isn't mistaken for a sum.
    /// </summary>
    public class TypedValueCompletionTests
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "kscript-typed-values");
        private static readonly LanguageService Service = new LanguageService(files: new MemoryFiles());

        private static (DocumentAnalysis Analysis, int Offset) At(string textWithCaret)
        {
            (string text, int offset) = TestHelpers.WithCaret(textWithCaret);
            return (Service.Analyze(text, Path.Combine(Folder, "main.kscript")), offset);
        }

        private static List<CompletionItem> Complete(string textWithCaret)
        {
            (DocumentAnalysis a, int offset) = At(textWithCaret);
            return Service.GetCompletions(a, offset);
        }

        [Fact]
        public void A_typed_variable_offers_its_types_values_first()
        {
            List<CompletionItem> items = Complete("tz: timezone = Eu‸");
            CompletionItem london = Assert.Single(items, i => i.Label == "Europe/London");
            Assert.Equal(CompletionKind.Value, london.Kind);
            Assert.True(items.IndexOf(london) < items.FindIndex(i => i.Kind != CompletionKind.Value), "values come before variables and commands");
            Assert.Contains(items, i => i.Label == "@env");

            // The whole value is replaced, not only the word after the slash.
            (DocumentAnalysis a, int offset) = At("tz: timezone = Europe/Lo‸");
            CompletionItem item = Assert.Single(Service.GetCompletions(a, offset), i => i.Label == "Europe/London");
            Assert.Equal("Europe/Lo", a.Source.Text.Substring(item.ReplaceSpan.Start, item.ReplaceSpan.Length));
        }

        [Fact]
        public void Typed_variables_offer_enum_values_durations_and_more()
        {
            Assert.Equal(new[] { "s", "m" }, Complete("enum Size { s, m }\nx: Size = ‸").Where(i => i.Kind == CompletionKind.Value).Select(i => i.Label));
            Assert.Contains(Complete("x: duration = ‸"), i => i.Label == "30s" && i.Kind == CompletionKind.Value);
            Assert.Contains(Complete("c: country = ‸"), i => i.Label == "GB");
            Assert.Contains(Complete("c: currency = ‸"), i => i.Label == "GBP");
            Assert.Contains(Complete("s: cron = ‸"), i => i.Label == "@daily");
            // A variable declared with a type gets them again when it is given a new value.
            Assert.Contains(Complete("tz: timezone = UTC\ntz = ‸\necho $tz"), i => i.Label == "Europe/London");
            // Untyped and plainly typed values have nothing extra.
            Assert.DoesNotContain(Complete("x = ‸"), i => i.Kind == CompletionKind.Value);
        }

        [Fact]
        public void Arguments_offer_the_values_of_their_parameters_type()
        {
            List<CompletionItem> named = Complete("fn f(tz: timezone) => $tz\necho @f(tz: Europe/Lo‸)");
            CompletionItem london = Assert.Single(named, i => i.Label == "Europe/London");
            // An argument reads unquoted text as code, so a value that isn't one word goes in quotes.
            Assert.Equal("'Europe/London'", london.InsertText);
            Assert.Contains(Complete("enum Size { s, m }\nfn f(size: Size) => $size\necho @f(size: ‸)"), i => i.Label == "m" && i.InsertText == null);
            Assert.Contains(Complete("fn f(wait: duration) => $wait\necho @f(‸)"), i => i.Label == "30s");
        }

        [Fact]
        public void After_at_only_commands_and_after_dollar_only_variables()
        {
            List<CompletionItem> commands = Complete("tz: timezone = @‸");
            Assert.NotEmpty(commands);
            Assert.All(commands, i => Assert.True(i.Kind is CompletionKind.Command or CompletionKind.Function, i.Label));
            Assert.Contains(commands, i => i.Label == "env");

            Assert.All(Complete("import KScript.Http\nRoutes {\n  timezone: timezone = @‸\n}"), i => Assert.True(i.Kind is CompletionKind.Command or CompletionKind.Function, i.Label));

            List<CompletionItem> variables = Complete("x = 1\ny = $‸\necho $y");
            Assert.NotEmpty(variables);
            Assert.All(variables, i => Assert.True(i.Kind is CompletionKind.Variable or CompletionKind.Constant or CompletionKind.Parameter, i.Label));

            // Commands that give the expected type come first.
            List<CompletionItem> dates = Complete("d: date = @‸\necho $d");
            Assert.Equal("today", dates.OrderBy(i => i.SortText, StringComparer.Ordinal).First(i => i.Label is "today" or "env" or "random").Label);

            // An email address isn't a command.
            Assert.DoesNotContain(Complete("echo 'joan@exa‸'"), i => i.Label == "env");
        }

        [Fact]
        public void Hovering_a_type_says_what_it_accepts()
        {
            (DocumentAnalysis a, int offset) = At("tz: time‸zone = Europe/London\necho $tz");
            HoverResult hover = Service.GetHover(a, offset);
            Assert.Contains("IANA time zone", hover.Markdown, StringComparison.Ordinal);
            Assert.Contains("Accepts", hover.Markdown, StringComparison.Ordinal);
            Assert.Equal("timezone", a.Source.Text.Substring(hover.Span.Start, hover.Span.Length));

            (DocumentAnalysis e, int at) = At("enum Size { s, m }\nfn f(size: Si‸ze) => $size\necho @f(size: m)");
            Assert.Contains("One of: `s`, `m`.", Service.GetHover(e, at).Markdown, StringComparison.Ordinal);

            (DocumentAnalysis s, int call) = At("fn f(tz: timezone) => $tz\necho @f(‸)");
            Assert.Contains("IANA time zone", Service.GetSignatureHelp(s, call).Signatures.Single().Parameters.Single().Documentation, StringComparison.Ordinal);
        }

        [Fact]
        public void Unquoted_text_with_dashes_is_one_problem_not_a_number()
        {
            (DocumentAnalysis a, _) = At("_day = dd-MM-yyyy‸\necho $_day");
            Diagnostic d = Assert.Single(a.Diagnostics, x => x.Code == DiagnosticCodes.BareIdentifier);
            Assert.StartsWith("dd-MM-yyyy isn't a value: dd, MM and yyyy aren't variables", d.Message, StringComparison.Ordinal);
            Assert.Equal("'dd-MM-yyyy'", d.Fixes[0].Changes.Single().NewText);
            Assert.DoesNotContain(a.Diagnostics, x => x.Code == DiagnosticCodes.UndefinedVariable);
            Assert.DoesNotContain(Service.GetInlayHints(a), h => h.Label == ": number");

            // Real arithmetic keeps its type.
            (DocumentAnalysis b, _) = At("a = 3\nb = $a - 1‸\necho $b");
            Assert.Contains(Service.GetInlayHints(b), h => h.Label == ": number");
        }

        [Fact]
        public void Native_literals_get_a_type_hint()
        {
            (DocumentAnalysis a, _) = At("_time = 0.20s\n_site = https://example.com\n_n = 3‸\necho $_time $_site $_n");
            List<InlayHint> hints = Service.GetInlayHints(a);
            Assert.Contains(hints, h => h.Label == ": duration");
            Assert.Contains(hints, h => h.Label == ": url");
            Assert.DoesNotContain(hints, h => h.Label == ": number");
        }

        [Fact]
        public void A_native_type_can_suggest_values_from_where_it_is_written()
        {
            // The hook a type such as a route path uses: it gets the file, the project and its routes.
            string root = Path.Combine(Path.GetTempPath(), "kscript-suggest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "routes"));
            try
            {
                File.WriteAllText(Path.Combine(root, "kscript.json"), "{}");
                File.WriteAllText(Path.Combine(root, "routes", "web.kscript"),
                    "import KScript.Http\n\nRoutes {\n  Get(path: \"/login\", to: @accounts.show_login)\n  Group(prefix: \"/admin\") {\n    Post(path: \"/users\") {\n      return 1\n    }\n  }\n}\n");
                File.WriteAllText(Path.Combine(root, "routes", "api.kscript"), "import KScript.Http\n\nRoutes {\n  Get(path: \"/health\") {\n    return 1\n  }\n}\n");

                var service = new LanguageService(files: DiskFileSystem.Instance);
                List<(string Method, string Path, string Handler)> routes = service.ProjectRoutes(root);
                Assert.Contains(("GET", "/login", "@accounts.show_login"), routes);
                Assert.Contains(routes, r => r.Method == "POST" && r.Path == "/admin/users");
                Assert.Contains(routes, r => r.Method == "GET" && r.Path == "/api/health");
                Assert.Empty(service.ProjectRoutes(Path.GetTempPath()));

                // slash_path suggests them: typing / where a path is expected lists the project's routes.
                (string text, int offset) = TestHelpers.WithCaret("p: slash_path = /lo‸\necho $p");
                CompletionItem login = Assert.Single(service.GetCompletions(service.Analyze(text, Path.Combine(root, "main.kscript")), offset), i => i.Label == "/login");
                Assert.Equal("GET → @accounts.show_login", login.Detail);
                Assert.Equal("/lo", text.Substring(login.ReplaceSpan.Start, login.ReplaceSpan.Length));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Fixed_lists_and_type_options_reach_the_hook()
        {
            Assert.Contains(NativeParser.Fixed(() => new[] { "a" })(new NativeValueContext()), v => v.Value == "a");
            (string name, Dictionary<string, string> options) = LanguageService.SplitType("path(exists: 'route')?");
            Assert.Equal("path", name);
            Assert.Equal("route", options["exists"]);
            Assert.Equal("timezone", LanguageService.SplitType("timezone?").Name);
        }
    }
}
