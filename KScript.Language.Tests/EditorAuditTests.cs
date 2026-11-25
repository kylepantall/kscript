using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Language.Formatting;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>
    /// Editor behaviour that must never change what a script does: a rename reaches every place the name is written
    /// and nothing else, a quick fix leaves every call working, and formatting twice gives the same file.
    /// </summary>
    public class EditorAuditTests
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "kscript-editor-audit"));

        private static string At(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

        #region Rename

        [Fact]
        public void RenameLeavesSingleQuotedTextAlone()
        {
            (string text, int offset) = WithCaret(Lines("x = 1", "echo 'single $x'", "label = 'Write $x to use a variable.'", "echo \"{ $‸x } { $label }\""));
            LanguageService service = new LanguageService(files: new MemoryFiles());
            DocumentAnalysis a = service.Analyze(text, At("quotes.kscript"));

            // The text names $x, so x isn't unused, but the text is not a reference to it.
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.UnusedVariable);
            Assert.Equal(2, service.GetReferences(a, offset, includeDeclaration: true).Count);
            Assert.Null(a.ReferenceAt(text.IndexOf("single", StringComparison.Ordinal)));

            RenameResult rename = service.Rename(a, offset, "count");
            Assert.Null(rename.Error);
            Assert.Equal(
                Lines("count = 1", "echo 'single $x'", "label = 'Write $x to use a variable.'", "echo \"{ $count } { $label }\""),
                AnalyzerTests.Apply(text, rename.Changes));
        }

        private const string Library = "/// Doubles.\nfn twice(n: number) => $n * 2\nconst limit = 3\nfn helper(a: number) => @twice($a)\n";

        private const string Main =
            "import twice, limit from \"lib/m.kscript\"\nimport \"lib/m.kscript\" as m\n" +
            "echo \"{ @twice(2) } { $limit } { @m.twice(1) } { m.limit } { [1]->map(@twice) }\"\n";

        private static LanguageService Project() => new LanguageService(files: new MemoryFiles()
            .Add(At("p", "kscript.json"), "{}")
            .Add(At("p", "lib", "m.kscript"), Library)
            .Add(At("p", "main.kscript"), Main));

        private static List<string> Positions(RenameResult rename) =>
            rename.OtherFiles.Select(o => $"{Path.GetFileName(o.Path)}:{o.Start.Line}:{o.Start.Character}").OrderBy(x => x, StringComparer.Ordinal).ToList();

        [Fact]
        public void RenamingAFunctionOtherFilesUseRenamesItThereToo()
        {
            LanguageService service = Project();
            (string text, int offset) = WithCaret(Library.Replace("fn twice", "fn tw‸ice"));
            DocumentAnalysis a = service.Analyze(text, At("p", "lib", "m.kscript"));

            RenameResult rename = service.Rename(a, offset, "double");
            Assert.Null(rename.Error);
            Assert.Equal("/// Doubles.\nfn double(n: number) => $n * 2\nconst limit = 3\nfn helper(a: number) => @double($a)\n", AnalyzerTests.Apply(text, rename.Changes));
            // The name in the import, the two calls, the call through the alias and the function passed as a value.
            Assert.Equal(new[] { "main.kscript:0:7", "main.kscript:2:36", "main.kscript:2:71", "main.kscript:2:9" }, Positions(rename));
            Assert.All(rename.OtherFiles, o => Assert.Equal(5, o.End.Character - o.Start.Character));
        }

        [Fact]
        public void RenamingAnImportedFunctionRenamesItsDeclaration()
        {
            LanguageService service = Project();
            (string text, int offset) = WithCaret(Main.Replace("{ @twice(2) }", "{ @tw‸ice(2) }"));
            DocumentAnalysis a = service.Analyze(text, At("p", "main.kscript"));

            RenameResult rename = service.Rename(a, offset, "double");
            Assert.Null(rename.Error);
            string renamed = AnalyzerTests.Apply(text, rename.Changes);
            Assert.Contains("import double, limit from", renamed);
            Assert.Contains("{ @double(2) } { $limit } { @m.double(1) } { m.limit } { [1]->map(@double) }", renamed);
            Assert.Equal(new[] { "m.kscript:1:3", "m.kscript:3:25" }, Positions(rename));
        }

        [Fact]
        public void RenamingATypeFollowsEveryPlaceItIsWritten()
        {
            (string text, int offset) = WithCaret(Lines(
                "type Us‸er {",
                "  login: string",
                "  friend?: User",
                "}",
                "fn show(u: User, all: User[] = []): User => $u",
                "users: User[] = [{ login: 'a' }]",
                "w: User? = null",
                "v = '{\"login\":\"x\"}'->json(as: User)",
                "echo \"{ @show($v).login } { $users->count() } { $w } { User.fields->count() }\""));
            LanguageService service = new LanguageService(files: new MemoryFiles());
            DocumentAnalysis a = service.Analyze(text, At("types.kscript"));

            RenameResult rename = service.Rename(a, offset, "Person");
            Assert.Null(rename.Error);
            string renamed = AnalyzerTests.Apply(text, rename.Changes);
            Assert.DoesNotContain("User", renamed);
            Assert.Contains("friend?: Person\n", renamed);
            Assert.Contains("fn show(u: Person, all: Person[] = []): Person => $u", renamed);
            Assert.Contains("users: Person[] = ", renamed);
            Assert.Contains("w: Person? = null", renamed);
            Assert.Empty(service.Analyze(renamed, At("types.kscript")).Errors);
        }

        #endregion

        #region Formatting

        private static string Fmt(string text)
        {
            FormatResult result = Formatter.Format(text, "test.kscript");
            Assert.True(result.Succeeded, result.Error);
            FormatResult again = Formatter.Format(result.Text, "test.kscript");
            Assert.True(again.Succeeded, again.Error);
            Assert.Equal(result.Text, again.Text);
            return result.Text;
        }

        [Fact]
        public void ABlockAfterSingleQuotedTextIsABlock()
        {
            Assert.Equal(
                Lines("s = 'a'", "if $s == 'a' {", "  m = { a: 1 }", "  echo $m", "} else {", "  echo 'no'", "}", "for c in 'abc' {", "  echo $c", "}"),
                Fmt(Lines("s = 'a'", "if $s == 'a' { m={a:1}; echo $m }", "else { echo 'no' }", "for c in 'abc' { echo $c }")));
        }

        [Fact]
        public void AMatchUsedAsAValueIsKeptWhateverItsSubjectEndsWith()
        {
            string text = Lines("x = [1]", "label = match $x->count() { 1 => 'one', _ => 'many' }", "other = match $x[0] { 1 => 'one', _ => 'many' }", "echo $label + $other");
            Assert.Equal(text, Fmt(text));
        }

        [Fact]
        public void AStatementAfterAnAssignmentOnTheSameLineIsFormattedInOnePass()
        {
            Assert.Equal(
                Lines("x = 5", "if $x > 3 {", "  echo 'gt'", "}", "echo 'after'"),
                Fmt(Lines("x = 5; if $x > 3 { echo 'gt' }; echo 'after'")));
        }

        [Fact]
        public void WrappingADocCommentNeverStartsALineWithATag()
        {
            string formatted = Fmt(Lines(
                "/// Sends the welcome mail to one person; the tags you can use on a function are @deprecated and @param.",
                "fn send(who: string) => $who",
                "echo @send('k')"));
            Assert.Equal(
                Lines(
                    "/// Sends the welcome mail to one person; the tags you can use on a function",
                    "/// are @deprecated and @param.",
                    "fn send(who: string) => $who",
                    "echo @send('k')"),
                formatted);
            // The formatted file checks as the original did: the function is not deprecated.
            Assert.DoesNotContain(Analyzer.Analyze(formatted, At("doc.kscript"), new MemoryFiles()).Diagnostics, d => d.Code == DiagnosticCodes.Deprecated);
        }

        [Fact]
        public void WrappingACommentNeverStartsALineWithAListItem()
        {
            string formatted = Fmt(Lines("// The total is the price times the quantity, and the result of that sum is then - if there is one - reduced.", "x = 1", "echo $x"));
            Assert.DoesNotContain(formatted.Split('\n'), line => line.StartsWith("// - ", StringComparison.Ordinal));
        }

        [Fact]
        public void ExampleCodeThatStartsWithACallIsLeftAlone()
        {
            string text = Lines(
                "/// Greets someone.",
                "/// @example",
                "///   @greet('Kyle')   // a very long example line that should be left alone even though it is past the width",
                "fn greet(who: string) => 'Hi ' + $who",
                "echo @greet('k')");
            Assert.Equal(text, Fmt(text));
        }

        #endregion

        #region Quick fixes

        private static DocumentAnalysis Check(string text, string file = "fix.kscript", IFileSystem files = null) =>
            Analyzer.Analyze(text, At(file), files ?? new MemoryFiles());

        private static Diagnostic Find(DocumentAnalysis a, string code, string marked) =>
            a.Diagnostics.Single(d => d.Code == code && a.Source[d.Span] == marked);

        private static string Fix(DocumentAnalysis a, Diagnostic d, string titleStart) =>
            AnalyzerTests.Apply(a.Source.Text, d.Fixes.Single(f => f.Title.StartsWith(titleStart, StringComparison.Ordinal)).Changes);

        [Fact]
        public void RemovingAnUnusedVariableKeepsTheCallThatFilledIt()
        {
            DocumentAnalysis a = Check(Lines("fn log_it(msg: string) {", "  echo $msg", "  return 1", "}", "r = @log_it('side effect')", "plain = 3"));
            Diagnostic call = Find(a, DiagnosticCodes.UnusedVariable, "r");
            Assert.DoesNotContain(call.Fixes, f => f.Title == "Remove $r");
            Assert.Contains("}\n@log_it('side effect')\nplain = 3", Fix(a, call, "Remove $r and keep the call"));
            // A plain value does nothing, so the whole line goes.
            Assert.EndsWith("@log_it('side effect')\n", Fix(a, Find(a, DiagnosticCodes.UnusedVariable, "plain"), "Remove $plain"));
        }

        [Fact]
        public void AParameterACallPassesIsNotOfferedForRemoval()
        {
            DocumentAnalysis a = Check(Lines("fn f(a: int, b: int) => $a", "fn g(a: int, spare: int = 0) => $a", "echo \"{ @f(1, 2) } { @f(b: 2, a: 1) } { @g(1) }\""));
            Diagnostic passed = Find(a, DiagnosticCodes.UnusedParameter, "b");
            Assert.DoesNotContain(passed.Fixes, f => f.Title.StartsWith("Remove", StringComparison.Ordinal));
            // The rename follows the call that names the parameter.
            Assert.Equal(
                Lines("fn f(a: int, _b: int) => $a", "fn g(a: int, spare: int = 0) => $a", "echo \"{ @f(1, 2) } { @f(_b: 2, a: 1) } { @g(1) }\""),
                Fix(a, passed, "Rename"));
            // No call passes spare, so it can go.
            Assert.StartsWith("fn f(a: int, b: int) => $a\nfn g(a: int) => $a\n", Fix(a, Find(a, DiagnosticCodes.UnusedParameter, "spare"), "Remove"));
        }

        [Fact]
        public void AParameterOfAFunctionOtherFilesCallGetsNoFixThatChangesItsSignature()
        {
            MemoryFiles files = new MemoryFiles()
                .Add(At("q", "kscript.json"), "{}")
                .Add(At("q", "main.kscript"), "import \"lib.kscript\"\necho @shared(1, extra: 2)\n")
                .Add(At("q", "lib.kscript"), "fn shared(a: int, extra: int = 0) => $a\n");
            DocumentAnalysis a = Check("fn shared(a: int, extra: int = 0) => $a\n", Path.Combine("q", "lib.kscript"), files);
            Assert.Empty(Find(a, DiagnosticCodes.UnusedParameter, "extra").Fixes ?? Array.Empty<QuickFix>());
        }

        #endregion

        #region Checks

        [Fact]
        public void ANamedFunctionDeclaresItsParametersBare()
        {
            DocumentAnalysis a = Check(Lines("fn add($a: number, b: number = [1, 2]->count(), $c = 1) => $a + $b + $c", "echo @add(1, 2)"));
            List<Diagnostic> found = a.Diagnostics.Where(d => d.Code == DiagnosticCodes.BareDeclaration).OrderBy(d => d.Span.Start).ToList();
            Assert.Equal(new[] { "$a", "$c" }, found.Select(d => a.Source[d.Span]));
            Assert.All(found, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
            Assert.Contains("Declare the parameter as a", found[0].Message);
            string once = AnalyzerTests.Apply(a.Source.Text, found[1].Fixes.Single().Changes);
            Assert.StartsWith("fn add($a: number, b: number = [1, 2]->count(), c = 1) =>", once);

            (string fixedText, int count) = Fixer.Apply(a.Source.Text, At("fix.kscript"), null);
            Assert.Equal(2, count);
            Assert.StartsWith("fn add(a: number, b: number = [1, 2]->count(), c = 1) =>", fixedText);
        }

        [Fact]
        public void SettingAFieldARecordDoesNotHaveIsReported()
        {
            DocumentAnalysis a = Check(Lines("type T {", "  n: int", "}", "t = T({ n: 1 })", "t.n = 2", "t.zz = 3", "echo $t"));
            Diagnostic unknown = Assert.Single(a.Diagnostics, d => d.Code == DiagnosticCodes.UnknownField);
            Assert.Equal("zz", a.Source[unknown.Span]);
            Assert.Equal("T has no field 'zz'. Its fields are: n.", unknown.Message);
        }

        #endregion
    }
}
