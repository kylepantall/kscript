using KScript.Language.Analysis;
using KScript.Language.Features;

namespace KScript.Language.Tests
{
    /// <summary>
    /// A script imported with an alias (<c>import "../controllers/accounts.kscript" as accounts</c>) works like code in
    /// the file: <c>@accounts.</c> suggests its functions, hover shows their signatures and docs, go to definition and
    /// find references reach into the imported file, and a name it doesn't have is reported.
    /// </summary>
    public class ImportedModuleTests
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "kscript-imported-modules");
        private static readonly string Controller = Path.Combine(Folder, "controllers", "accounts.kscript");
        private static readonly string RoutesFile = Path.Combine(Folder, "routes", "web.kscript");
        private static readonly string OtherRoutes = Path.Combine(Folder, "routes", "api.kscript");

        private const string ControllerText =
            "import KScript.Http\n\n" +
            "/// GET /login\n" +
            "fn show_login() => @view('auth/login', data: {})\n\n" +
            "/// Signs in.\n" +
            "/// @param form the posted form\n" +
            "fn sign_in(form: map) {\n  return @redirect('/')\n}\n\n" +
            "/// How many tries before a wait.\n" +
            "const tries = 5\n";

        private const string Head = "import KScript.Http\nimport \"../controllers/accounts.kscript\" as accounts\n\nRoutes {\n";

        private static readonly LanguageService Service = new LanguageService(files: new MemoryFiles()
            .Add(Path.Combine(Folder, "kscript.json"), "{}")
            .Add(Controller, ControllerText)
            .Add(OtherRoutes, "import KScript.Http\nimport \"../controllers/accounts.kscript\" as accounts\n\nRoutes {\n  Post(path: \"/login\", to: @accounts.sign_in)\n}\n"));

        private static (DocumentAnalysis Analysis, int Offset) At(string textWithCaret, string path = null)
        {
            (string text, int offset) = TestHelpers.WithCaret(textWithCaret);
            return (Service.Analyze(text, path ?? RoutesFile), offset);
        }

        private static List<CompletionItem> Complete(string textWithCaret, string path = null)
        {
            (DocumentAnalysis a, int offset) = At(textWithCaret, path);
            return Service.GetCompletions(a, offset);
        }

        [Fact]
        public void At_alias_dot_suggests_the_imported_functions_with_their_signatures()
        {
            List<CompletionItem> items = Complete(Head + "  Get(path: \"/login\", to: @accounts.‸)\n}");
            CompletionItem login = Assert.Single(items, i => i.Label == "show_login");
            Assert.Equal(CompletionKind.Function, login.Kind);
            Assert.StartsWith("@accounts.show_login()", login.Detail, StringComparison.Ordinal);
            Assert.Contains("GET /login", login.Documentation, StringComparison.Ordinal);
            // A route's to: names the function; it isn't called, so no () goes in.
            Assert.Null(login.InsertText);
            Assert.StartsWith("@accounts.sign_in(form: map)", Assert.Single(items, i => i.Label == "sign_in").Detail, StringComparison.Ordinal);
            Assert.Equal(CompletionKind.Constant, Assert.Single(items, i => i.Label == "tries").Kind);
            Assert.DoesNotContain(items, i => i.Label.StartsWith("$", StringComparison.Ordinal) || i.Label == "now");
        }

        [Fact]
        public void Partly_typed_members_are_replaced_whole_and_calls_get_brackets()
        {
            CompletionItem item = Assert.Single(Complete(Head + "  Get(path: \"/login\", to: @accounts.sh‸)\n}"), i => i.Label == "show_login");
            string text = Head + "  Get(path: \"/login\", to: @accounts.sh)\n}";
            Assert.Equal("sh", text.Substring(item.ReplaceSpan.Start, item.ReplaceSpan.Length));

            List<CompletionItem> calls = Complete("import \"../controllers/accounts.kscript\" as accounts\nx = @accounts.‸");
            Assert.Equal("sign_in($1)", Assert.Single(calls, i => i.Label == "sign_in").InsertText);
            Assert.Equal("show_login()", Assert.Single(calls, i => i.Label == "show_login").InsertText);
            Assert.Contains(Complete("import \"../controllers/accounts.kscript\" as accounts\nx = accounts.‸"), i => i.Label == "sign_in");
        }

        [Fact]
        public void A_package_alias_suggests_its_commands()
        {
            List<CompletionItem> items = Complete("import KScript.Math as math\nx = @math.‸", Path.Combine(Folder, "maths.kscript"));
            Assert.Contains(items, i => i.Label == "sqrt" && i.Kind == CompletionKind.Command);
            Assert.DoesNotContain(items, i => i.Label == "now");
        }

        [Fact]
        public void Hover_shows_the_signature_docs_and_where_it_comes_from()
        {
            (DocumentAnalysis a, int offset) = At(Head + "  Post(path: \"/login\", to: @accounts.sig‸n_in)\n}");
            HoverResult hover = Service.GetHover(a, offset);
            Assert.NotNull(hover);
            Assert.Contains("@accounts.sign_in(form: map)", hover.Markdown, StringComparison.Ordinal);
            Assert.Contains("Signs in.", hover.Markdown, StringComparison.Ordinal);
            Assert.Contains("../controllers/accounts.kscript", hover.Markdown, StringComparison.Ordinal);
            Assert.Equal("sign_in", a.Source.Text.Substring(hover.Span.Start, hover.Span.Length));

            (DocumentAnalysis b, int at) = At(Head + "  Get(path: \"/\", to: @acc‸ounts.show_login)\n}");
            Assert.Contains("Functions: `show_login`, `sign_in`", Service.GetHover(b, at).Markdown, StringComparison.Ordinal);

            (DocumentAnalysis c, int constant) = At("import \"../controllers/accounts.kscript\" as accounts\necho accounts.tri‸es");
            Assert.Contains("const accounts.tries: number", Service.GetHover(c, constant).Markdown, StringComparison.Ordinal);

            (DocumentAnalysis m, int sqrt) = At("import KScript.Math as math\necho @math.sq‸rt(4)", Path.Combine(Folder, "maths.kscript"));
            Assert.Contains("sqrt", Service.GetHover(m, sqrt).Markdown, StringComparison.Ordinal);
        }

        [Fact]
        public void Go_to_definition_opens_the_function_in_the_imported_file()
        {
            (DocumentAnalysis a, int offset) = At(Head + "  Get(path: \"/login\", to: @accounts.show_lo‸gin)\n}");
            SourceLocation location = Assert.Single(Service.GetDefinition(a, offset));
            Assert.Equal(Path.GetFullPath(Controller), location.Path);
            Assert.Equal(3, location.Start.Line);

            (DocumentAnalysis b, int call) = At("import \"../controllers/accounts.kscript\" as accounts\nx = accounts.sign_‸in({})\necho $x");
            Assert.Equal(7, Assert.Single(Service.GetDefinition(b, call)).Start.Line);
        }

        [Fact]
        public void Find_references_lists_uses_in_every_file_of_the_project()
        {
            (DocumentAnalysis a, int offset) = At(Head + "  Post(path: \"/login\", to: @accounts.sign_‸in)\n  Post(path: \"/again\", to: @accounts.sign_in)\n}");
            List<SourceLocation> refs = Service.GetProjectReferences(a, offset, includeDeclaration: true);
            Assert.Equal(2, refs.Count(r => r.Path == null));
            Assert.Contains(refs, r => r.Path == Path.GetFullPath(OtherRoutes) && r.Start.Line == 4);
            Assert.Contains(refs, r => r.Path == Path.GetFullPath(Controller) && r.Start.Line == 7);
            Assert.DoesNotContain(Service.GetProjectReferences(a, offset, includeDeclaration: false), r => r.Path == Path.GetFullPath(Controller));

            // From the declaration in the controller: the routes files that use it.
            DocumentAnalysis controller = Service.Analyze(ControllerText, Controller);
            List<SourceLocation> fromDeclaration = Service.GetProjectReferences(controller, ControllerText.IndexOf("sign_in", StringComparison.Ordinal) + 2, includeDeclaration: true);
            Assert.Contains(fromDeclaration, r => r.Path == Path.GetFullPath(OtherRoutes));
        }

        [Fact]
        public void Members_the_import_lacks_are_reported_with_a_suggestion()
        {
            (DocumentAnalysis a, _) = At(Head + "  Get(path: \"/login\", to: @accounts.show_logn‸)\n}");
            Diagnostic d = TestHelpers.AssertSingle(TestHelpers.WithoutUnused(a), DiagnosticCodes.UndefinedFunction);
            Assert.Contains("has no 'show_logn'", d.Message, StringComparison.Ordinal);
            Assert.Equal("show_login", d.Fixes.Single().Changes.Single().NewText);

            (DocumentAnalysis b, _) = At("import \"../controllers/accounts.kscript\" as accounts\necho accounts.trie‸");
            Assert.Contains(b.Diagnostics, x => x.Message.Contains("has no 'trie'", StringComparison.Ordinal));

            (DocumentAnalysis c, _) = At(Head + "  Get(path: \"/login\", to: @accounts.‸)\n}");
            Assert.Contains(c.Diagnostics, x => x.Message == "Name a function of the import 'accounts' after the dot.");
        }

        [Fact]
        public void Calls_through_the_alias_check_their_arguments()
        {
            (DocumentAnalysis a, _) = At("import \"../controllers/accounts.kscript\" as accounts\nx = @accounts.sign_in()‸\necho $x");
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.ArgumentCount);
            (DocumentAnalysis b, _) = At("import \"../controllers/accounts.kscript\" as accounts\nx = @accounts.sign_in(nope: {})‸\necho $x");
            Assert.Contains(b.Diagnostics, d => d.Code == DiagnosticCodes.UnknownParameter);

            // A piped value is the first argument.
            (DocumentAnalysis piped, _) = At("import \"../controllers/accounts.kscript\" as accounts\nx = {} |> accounts.sign_in()‸\necho $x");
            Assert.DoesNotContain(piped.Diagnostics, d => d.Code == DiagnosticCodes.ArgumentCount);

            (DocumentAnalysis c, int offset) = At("import \"../controllers/accounts.kscript\" as accounts\nx = @accounts.sign_in(‸)\necho $x");
            SignatureHelpResult help = Service.GetSignatureHelp(c, offset);
            Assert.StartsWith("@accounts.sign_in(form: map)", help.Signatures.Single().Label, StringComparison.Ordinal);
            Assert.Equal("the posted form", help.Signatures.Single().Parameters.Single().Documentation.Split("\n")[0]);
        }

        [Fact]
        public void Members_cant_be_renamed_from_the_file_that_imports_them()
        {
            (DocumentAnalysis a, int offset) = At(Head + "  Get(path: \"/login\", to: @accounts.show_lo‸gin)\n}");
            Assert.Null(Service.PrepareRename(a, offset));
        }
    }
}
