using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of &lt;type&gt;: records whose fields complete, typed calls, and the mistakes it catches.</summary>
    public class TypedJsonAnalysisTests
    {
        private sealed class FakeFiles : IFileSystem
        {
            private readonly Dictionary<string, string> files;
            public FakeFiles(Dictionary<string, string> files) => this.files = files.ToDictionary(k => Path.GetFullPath(k.Key), k => k.Value);
            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path));
            public bool DirectoryExists(string path) => false;
            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;
            public IEnumerable<string> ListFiles(string directory, string pattern) => Enumerable.Empty<string>();
        }

        private const string Types =
            "<enum name=\"Visibility\" values=\"public, private\" />\n"
            + "<type name=\"User\" fields=\"login: string, id: int, avatar_url?: string\" />\n"
            + "<type name=\"Repo\" description=\"A repository.\">\n"
            + "  <field name=\"name\" type=\"string\" />\n"
            + "  <field name=\"stars\" type=\"int\" from=\"stargazers_count\" description=\"Stars.\" />\n"
            + "  <field name=\"owner\" type=\"User\" />\n"
            + "  <field name=\"topics\" type=\"string[]\" default=\"[]\" />\n"
            + "  <field name=\"visibility\" type=\"Visibility\" />\n"
            + "  <field name=\"label\" value=\"$name + ' ' + $stars\" />\n"
            + "  <field name=\"note\" description=\"Free text.\" />\n"
            + "</type>\n";

        private static readonly LanguageService Service = new LanguageService(files: new FakeFiles(new Dictionary<string, string>
        {
            ["/tmp/shapes/repo.json"] = "{ \"name\": \"k\", \"stargazers_count\": 1, \"avatar-url\": \"x\", \"owner\": { \"login\": \"kyle\" } }",
            ["/tmp/shapes/list.json"] = "[{ \"id\": 1 }, { \"id\": 2, \"tag\": \"x\" }]",
            ["/tmp/shapes/users.json"] = "[{ \"login\": \"a\", \"id\": 1 }]",
            ["/tmp/shapes/bad.json"] = "{ nope",
            ["/tmp/shapes/empty.json"] = "[]",
            ["/tmp/shapes/digits.json"] = "{ \"2x\": 1 }",
            ["/tmp/shapes/lib.ks.xml"] = "<kscript><type name=\"Team\" fields=\"name: string, size: int\" /></kscript>",
        }));

        private const string DocPath = "/tmp/shapes/test.ks.xml";

        private static DocumentAnalysis Open(string body) => WithoutUnused(Service.Analyze(Script(body), DocPath));

        private static List<string> Labels(string body)
        {
            (string text, int offset) = WithCaret(Script(body));
            return Service.GetCompletions(Service.Analyze(text, DocPath), offset).Select(i => i.Label).ToList();
        }

        private static string Show(DocumentAnalysis a, string variable) => a.Symbols.Last(s => s.Name == variable).Shape.Display();

        [Fact]
        public void TypesAreConstantsWhoseRecordsHaveFields()
        {
            DocumentAnalysis a = Open(Types);
            AssertClean(a);
            Symbol repo = a.Symbols.First(s => s.Name == "Repo");
            Assert.Equal(SymbolKind.Constant, repo.Kind);
            Assert.Equal("type Repo", repo.Shape.Display());
            Assert.Equal("Repo", repo.Shape.Instance.Display());
            Assert.Equal("Repo", repo.Shape.Instance.Pretty());
            Assert.Equal(new[] { "name", "stars", "owner", "topics", "visibility", "label", "note" }, repo.Shape.Instance.Fields.Keys);
            Assert.Equal("User", repo.Shape.Instance.Fields["owner"].Shape.Display());
            Assert.Equal("list<string>", repo.Shape.Instance.Fields["topics"].Shape.Display());
            Assert.Equal("string", repo.Shape.Instance.Fields["visibility"].Shape.Display());
            Assert.Equal("string", repo.Shape.Instance.Fields["label"].Shape.Display());
            Assert.Equal("Stars. Read from stargazers_count.", repo.Shape.Instance.Fields["stars"].Description);
            Assert.Equal("Worked out from the other fields.", repo.Shape.Instance.Fields["label"].Description);
            Assert.Equal("Free text.", repo.Shape.Instance.Fields["note"].Description);
            Assert.Equal("Optional.", a.Symbols.First(s => s.Name == "User").Shape.Instance.Fields["avatar_url"].Description);
            Assert.StartsWith("A repository.\n\ntype Repo {\n  name: string\n  stars: number\n  owner: User\n", repo.Description);
            Assert.True(repo.Shape.Instance.Closed);
        }

        [Theory]
        [InlineData("<let name=\"r\" value=\"Repo({})\" />", "Repo")]
        [InlineData("<let name=\"r\" value=\"Repo([{}])\" />", "list<Repo>")]
        [InlineData("<let name=\"r\" value=\"'{}'->json(as: Repo)\" />", "Repo")]
        [InlineData("<let name=\"r\" value=\"'{}'->json(Repo)\" />", "Repo")]
        [InlineData("<let name=\"r\" value=\"'{}' |> json(as: Repo)\" />", "Repo")]
        [InlineData("<let name=\"r\" value=\"'[]'->json(as: 'Repo[]')\" />", "list<Repo>")]
        [InlineData("<let name=\"r\" value=\"'[]'->json(as: 'int[][]?')\" />", "list<list<number>>")]
        [InlineData("<let name=\"r\" value=\"'{}'->json(as: User)\" />", "User")]
        [InlineData("<let name=\"r\" value=\"'{}'->json(as: 'Nope')\" />", "any")]
        [InlineData("<let name=\"r\" value=\"'{}'->json()\" />", "any")]
        [InlineData("<let name=\"r\" value=\"'{}'->json(as: 3)\" />", "any")]
        [InlineData("<let name=\"r\" type=\"Repo\" value=\"{}\" />", "Repo")]
        [InlineData("<let name=\"r\" type=\"Repo[]?\" value=\"[]\" />", "list<Repo>")]
        [InlineData("<let name=\"r\" type=\"Visibility\" value=\"'public'\" />", "string")]
        [InlineData("<let name=\"r\" type=\"date\" value=\"@now()\" />", "date")]
        [InlineData("<let name=\"r\" type=\"bool\" value=\"1\" />", "bool")]
        [InlineData("<let name=\"r\" type=\"map\" value=\"{}\" />", "map")]
        [InlineData("<let name=\"r\" type=\"list\" value=\"[]\" />", "list")]
        [InlineData("<let name=\"r\" type=\"fn\" value=\"fn() => 1\" />", "fn")]
        [InlineData("<let name=\"r\" type=\"any\" value=\"1\" />", "any")]
        [InlineData("<let name=\"r\" value=\"Repo({}).owner\" />", "User")]
        [InlineData("<let name=\"r\" value=\"Repo([{}])->first()\" />", "Repo")]
        [InlineData("<let name=\"r\" value=\"Repo()\" />", "Repo")]
        [InlineData("<let name=\"r\" value=\"[1]->json(as: Repo)\" />", "any")]
        public void CallsAndDeclarationsGiveRecords(string body, string expected)
        {
            Assert.Equal(expected, Show(Open(Types + body), "r"));
        }

        [Fact]
        public void ResponsesAndFilesMapToTypes()
        {
            Assert.Equal("User", Show(Open("<import package=\"KScript.Net\" />\n" + Types + "<let name=\"r\" value=\"@api('https://x')->json(as: User)\" />"), "r"));
            Assert.Equal("list<User>", Show(Open("<import package=\"KScript.IO\" />\n" + Types + "<let name=\"r\" value=\"@read_json('users.json', as: User)\" />"), "r"));
            Assert.Equal("User", Show(Open("<import package=\"KScript.IO\" />\n" + Types + "<let name=\"r\" value=\"@read_json($path, as: User)\" />"), "r"));
            Assert.Equal("list<{ login: string, id: number }>", Show(Open("<import package=\"KScript.IO\" />\n" + Types + "<let name=\"r\" value=\"@read_json('users.json')\" />"), "r"));
            Assert.Equal("User", Show(Open(Types + "<let name=\"r\" value=\"'{}' |> json(as: User)\" />"), "r"));
        }

        [Fact]
        public void StorageJsonReturnsTheRequestedShape()
        {
            const string storage = "<import package=\"KScript.Storage\" />\n" + Types;
            Assert.Equal("User", Show(Open(storage + "<let name=\"r\" value=\"@storage()->read_json('user.json', as: User)\" />"), "r"));
            Assert.Equal("list<User>", Show(Open(storage + "<let name=\"r\" value=\"@storage() |> read_json('users.json', [], 'User[]')\" />"), "r"));
            Assert.Contains("User", Labels(storage + "<echo>{ @storage()->read_json('user.json', as: ‸) }</echo>"));
            AssertClean(Open(storage + "<echo>{ @storage()->read_json('user.json', default: null, as: User).login }</echo>"));
        }

        [Fact]
        public void RecordFieldsComplete()
        {
            Assert.Equal(new[] { "name", "stars", "owner", "topics", "visibility", "label", "note" },
                Labels(Types + "<let name=\"r\" value=\"Repo({})\" />\n<echo>{ $r.‸ }</echo>").Take(7));
            Assert.Contains("login", Labels(Types + "<fn name=\"f\" params=\"repo: Repo\"><echo>{ $repo.owner.‸ }</echo></fn>"));
            Assert.Contains("stars", Labels(Types + "<let name=\"rs\" type=\"Repo[]\" value=\"[]\" />\n<for each=\"r\" in=\"$rs\"><echo>{ $r.‸ }</echo></for>"));
            Assert.Contains("check", Labels(Types + "<echo>{ Repo->‸ }</echo>"));
            Assert.DoesNotContain("from", Labels(Types + "<echo>{ Repo->‸ }</echo>"));
        }

        [Fact]
        public void TypeNamesComplete()
        {
            List<string> types = Labels(Types + "<let name=\"r\" type=\"‸\" value=\"1\" />");
            Assert.Contains("Repo", types);
            Assert.Contains("Visibility", types);
            Assert.Contains("number", types);
            Assert.Contains("Repo", Labels(Types + "<type name=\"T\"><field name=\"a\" type=\"‸\" /></type>"));
            Assert.Contains("Repo", Labels(Types + "<echo>{ '{}'->json(as: ‸) }</echo>"));
            Assert.Contains("User", Labels(Types + "<echo>{ '{}'->json(‸) }</echo>"));
        }

        [Fact]
        public void HoversAndInlayHintsNameTheType()
        {
            (string text, int offset) = WithCaret(Script(Types + "<echo>{ Re‸po }</echo>"));
            Assert.Contains("type Repo {", Service.GetHover(Service.Analyze(text, DocPath), offset).Markdown);

            DocumentAnalysis a = Open(Types + "<let name=\"r\" value=\"Repo({})\" />");
            Assert.Contains(Service.GetInlayHints(a), h => h.Label == ": Repo");

            (text, offset) = WithCaret(Script(Types + "<echo>{ '{}'->json(as: Re‸po) }</echo>"));
            DocumentAnalysis b = Service.Analyze(text, DocPath);
            AssertClean(b);
            Assert.NotEmpty(Service.GetDefinition(b, offset));
        }

        [Fact]
        public void BareTypeNamesAreValues()
        {
            AssertClean(Open(Types + "<let name=\"t\" value=\"Repo\" />\n<echo>{ '{}'->json(User) } { '{}'->json(as: Repo) }</echo>"));
            AssertSingle(Open(Types + "<let name=\"t\" value=\"Nope\" />"), DiagnosticCodes.UndefinedVariable);
            AssertSingle(Open(Types + "<let name=\"t\" value=\"nope\" />"), DiagnosticCodes.BareIdentifier);
        }

        [Fact]
        public void UnknownFieldsOfRecordsAreWarnings()
        {
            Diagnostic d = AssertSingle(Open(Types + "<let name=\"r\" value=\"Repo({})\" />\n<echo>{ $r.starz }</echo>"), DiagnosticCodes.UnknownField);
            Assert.Equal("Repo has no field 'starz'. Did you mean stars?", d.Message);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal("stars", d.Fixes.Single().Changes.Single().NewText);
            Assert.StartsWith("Repo has no field 'zzzzzz'. Its fields are: name, stars", AssertSingle(Open(Types + "<let name=\"r\" value=\"Repo({})\" />\n<echo>{ $r.zzzzzz }</echo>"), DiagnosticCodes.UnknownField).Message);
            AssertClean(Open("<type name=\"T\" fields=\"a\" extra=\"keep\" />\n<let name=\"r\" value=\"T({})\" />\n<echo>{ $r.b }</echo>"));
            AssertClean(Open("<let name=\"r\" value=\"{ a: 1 }\" />\n<echo>{ $r.b }</echo>"));
        }

        [Theory]
        [InlineData("<let name=\"r\" type=\"Rpo\" value=\"1\" />", "Unknown type 'Rpo'.")]
        [InlineData("<let name=\"r\" type=\"Rpo[]\" value=\"1\" />", "Unknown type 'Rpo'.")]
        [InlineData("<let name=\"r\" type=\"1x\" value=\"1\" />", "'1x' isn't a type.")]
        [InlineData("<let name=\"r\" type=\"\" value=\"1\" />", "'' isn't a type.")]
        [InlineData("<fn name=\"f\" params=\"r: Rpo\" value=\"1\" />", "Unknown type 'Rpo'.")]
        [InlineData("<type name=\"T\" fields=\"a: Rpo\" />", "Unknown type 'Rpo'.")]
        [InlineData("<type name=\"T\" fields=\"a: \" />", "'' isn't a type.")]
        [InlineData("<type name=\"T\"><field name=\"a\" type=\"Rpo\" /></type>", "Unknown type 'Rpo'.")]
        public void UnknownTypesAreErrors(string body, string message)
        {
            Diagnostic d = AssertSingle(Open(Types + body), DiagnosticCodes.InvalidAttributeValue);
            Assert.StartsWith(message, d.Message);
        }

        [Fact]
        public void UnknownTypesSuggestCloseOnes()
        {
            Diagnostic d = AssertSingle(Open(Types + "<let name=\"r\" type=\"Rpo\" value=\"1\" />"), DiagnosticCodes.InvalidAttributeValue);
            Assert.Equal("Repo", d.Fixes.Single().Changes.Single().NewText);
        }

        [Fact]
        public void TypedParametersAreKnownTypes()
        {
            DocumentAnalysis a = Open(Types + "<fn name=\"f\" params=\"r: Repo, rs: Repo[], v: Visibility\" value=\"$r.stars\" />\n<echo>{ @f({}, [], public) }</echo>");
            AssertClean(a);
            Assert.Equal("Repo", a.Symbols.First(s => s.Name == "r").Shape.Display());
            Assert.Equal("list<Repo>", a.Symbols.First(s => s.Name == "rs").Shape.Display());
        }

        [Theory]
        [InlineData("<type name=\"string\" fields=\"a\" />", DiagnosticCodes.InvalidAttributeValue, "'string' is a built-in type")]
        [InlineData("<type name=\"T\" fields=\"a\" /><type name=\"T\" fields=\"b\" />", DiagnosticCodes.DuplicateDeclaration, "A type named 'T' is already declared.")]
        [InlineData("<type name=\"T\" fields=\"a, a\" />", DiagnosticCodes.DuplicateDeclaration, "The field 'a' is listed twice.")]
        [InlineData("<type name=\"T\" fields=\"a\"><field name=\"a\" /></type>", DiagnosticCodes.DuplicateDeclaration, "The field 'a' is listed twice.")]
        [InlineData("<type name=\"T\" fields=\"1a\" />", DiagnosticCodes.InvalidAttributeValue, "'1a' isn't a field.")]
        [InlineData("<type name=\"T\" fields=\"a = (\" />", DiagnosticCodes.ExpressionSyntax, "The default of 'a' isn't a valid expression")]
        [InlineData("<type name=\"T\" />", DiagnosticCodes.MissingAttribute, "<type name=\"T\"> needs fields")]
        [InlineData("<type name=\"T\"><echo>x</echo></type>", DiagnosticCodes.MisplacedElement, "<type> can only hold <field>, <validate>, <normalize> and <fn> elements, not <echo>.")]
        [InlineData("<type name=\"T\"><field name=\"1a\" /></type>", DiagnosticCodes.InvalidAttributeValue, "'1a' can't be a field name")]
        [InlineData("<type name=\"T\"><field name=\"a\" from=\"a..b\" /></type>", DiagnosticCodes.InvalidAttributeValue, "'a..b' isn't a path.")]
        [InlineData("<type name=\"T\"><field name=\"a\" value=\"1\" from=\"b\" /></type>", DiagnosticCodes.InvalidAttribute, "A field with a value is worked out from the others")]
        [InlineData("<type name=\"T\"><field name=\"a\" value=\"$nope\" /></type>", DiagnosticCodes.UndefinedVariable, "$nope is not defined")]
        [InlineData("<type name=\"T\" path=\"bad.json\" />", DiagnosticCodes.InvalidAttributeValue, "'bad.json' isn't a JSON file the editor can read")]
        [InlineData("<type fields=\"a\" />", DiagnosticCodes.MissingAttribute, "<type> needs")]
        [InlineData("<type name=\"1x\" fields=\"a\" />", DiagnosticCodes.InvalidAttributeValue, "'1x' isn't a valid name.")]
        [InlineData("<type name=\"T\" fields=\": int\" />", DiagnosticCodes.InvalidAttributeValue, "': int' isn't a field.")]
        [InlineData("<type name=\"T\" fields=\"a\"><field type=\"int\" /></type>", DiagnosticCodes.MissingAttribute, "<field> needs")]
        [InlineData("<type name=\"T\" fields=\"a\"><field value=\"1\" /></type>", DiagnosticCodes.MissingAttribute, "<field> needs")]
        [InlineData("<field name=\"a\" value=\"1\" />", DiagnosticCodes.MisplacedElement, "<field>")]
        public void DeclarationMistakesAreReported(string body, string code, string message)
        {
            Assert.StartsWith(message, AssertSingle(Open(body), code).Message);
        }

        [Theory]
        [InlineData("a", true)]
        [InlineData("owner.login", true)]
        [InlineData("items[0].name", true)]
        [InlineData("items[0][1]", true)]
        [InlineData(" ", false)]
        [InlineData(".a", false)]
        [InlineData("[0]", false)]
        [InlineData("a.", false)]
        [InlineData("a..b", false)]
        [InlineData("a]", false)]
        [InlineData("a[x]", false)]
        [InlineData("a[-1]", false)]
        [InlineData("a[0", false)]
        [InlineData("a[0]b", false)]
        public void FieldPathsAreChecked(string path, bool valid)
        {
            DocumentAnalysis a = Open($"<type name=\"T\"><field name=\"a\" from=\"{path}\" /></type>");
            Assert.Equal(valid, !a.Diagnostics.Any(d => d.Message.Contains("isn't a path")));
        }

        [Fact]
        public void ComputedFieldsSeeTheOtherFields()
        {
            DocumentAnalysis a = Open("<type name=\"P\"><field name=\"first\" type=\"string\" /><field name=\"n\" type=\"int\" /><field name=\"both\" value=\"$first + $n\" /><field name=\"typed\" type=\"int\" value=\"$n\" /></type>");
            AssertClean(a);
            TypeShape p = a.Symbols.First(s => s.Name == "P").Shape.Instance;
            Assert.Equal("string", p.Fields["both"].Shape.Display());
            Assert.Equal("number", p.Fields["typed"].Shape.Display());
            AssertClean(Open("<field name=\"loose\" value=\"1\" />".Replace("<field", "<type name=\"Q\"><field").Replace("/>", "/></type>")));
        }

        [Fact]
        public void SampleFilesGiveFields()
        {
            DocumentAnalysis a = Open("<type name=\"Repo\" path=\"repo.json\"><field name=\"stars\" type=\"int\" from=\"stargazers_count\" /></type>\n<type name=\"Item\" path=\"list.json\" />\n<type name=\"Gone\" path=\"missing.json\" fields=\"a\" />");
            Assert.Equal(new[] { "name", "stargazers_count", "avatar_url", "owner", "stars" }, a.Symbols.First(s => s.Name == "Repo").Shape.Instance.Fields.Keys);
            Assert.Equal(new[] { "id", "tag" }, a.Symbols.First(s => s.Name == "Item").Shape.Instance.Fields.Keys);
            Assert.Single(a.Diagnostics, d => d.Message.Contains("missing.json"));
        }

        [Fact]
        public void FieldsWithoutANameValueAreSkipped()
        {
            DocumentAnalysis a = Open("<type name=\"T\" fields=\"a\"><field name value=\"1\" /></type>");
            Assert.NotEmpty(a.Diagnostics);
            Assert.Equal(new[] { "a" }, a.Symbols.First(s => s.Name == "T").Shape.Instance.Fields.Keys);
        }

        [Fact]
        public void OddSamplesAndInlineFields()
        {
            Assert.Empty(Open("<type name=\"E\" path=\"empty.json\" />").Symbols.First(s => s.Name == "E").Shape.Instance.Fields);
            Assert.Equal("_2x", Open("<type name=\"D\" path=\"digits.json\" />").Symbols.First(s => s.Name == "D").Shape.Instance.Fields.Keys.Single());
            DocumentAnalysis a = Open("<type name=\"T\" fields=\"a = 'x,y', b: list = [{ c: (1) }], d: string[] = []\" />");
            AssertClean(a);
            Assert.Equal(new[] { "a", "b", "d" }, a.Symbols.First(s => s.Name == "T").Shape.Instance.Fields.Keys);
            AssertClean(Analyzer.Analyze(Script("<type name=\"T\" path=\"bad.json\" fields=\"a\" />"), null, EmptyFileSystem.Instance));
        }

        [Fact]
        public void ImportedTypesAreKnown()
        {
            DocumentAnalysis a = Open("<import path=\"lib.ks.xml\" />\n<let name=\"t\" value=\"Team({})\" />\n<let name=\"u\" type=\"Team\" value=\"{}\" />\n<echo>{ $t.size } { $u.name }</echo>");
            AssertClean(a);
            Assert.Equal("Team", Show(a, "t"));
            Assert.Equal("Team", Show(a, "u"));
        }
    }
}
