using KScript.Language.Analysis;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Tests
{
    /// <summary>
    /// The one way to write a declared type: its name is bare, calling it makes a value, an optional field is marked on
    /// its name, a model's options are in its brackets (KS6014 for each long form, with a fix), and a package is named
    /// only in an import.
    /// </summary>
    public class TypeFormTests
    {
        private const string Path = "/tmp/type-forms/main.kscript";

        private const string Types = """
            enum Size { small, large }

            type Age with [int] { min: 0; max: 150 }

            type Repo {
              name: string
              stars: int = 0
            }

            error Missing {
              what: string
            }


            """;

        private static List<Diagnostic> Check(string code, string path = Path) =>
            TestHelpers.WithoutUnused(Analyzer.Analyze(code, path)).Diagnostics.ToList();

        private static List<Diagnostic> TypeForms(string code, string path = Path) =>
            Check(code, path).Where(d => d.Code == DiagnosticCodes.TypeForm).ToList();

        private static string Fixed(string code, string path = Path) => Fixer.Apply(code, path).Text;

        /// <summary>The text after a diagnostic's own quick fix, for errors that kscript check --fix leaves to the writer.</summary>
        private static string AfterFix(string code, Diagnostic d)
        {
            var sb = new System.Text.StringBuilder(code);
            foreach (TextChange c in d.Fixes[0].Changes.OrderByDescending(c => c.Span.Start))
            {
                sb.Remove(c.Span.Start, c.Span.Length).Insert(c.Span.Start, c.NewText);
            }
            return sb.ToString();
        }

        [Fact]
        public void TheRuleIsOneThatCheckFixApplies()
        {
            Assert.Equal("KS6014", DiagnosticCodes.TypeForm);
            Assert.Contains(DiagnosticCodes.TypeForm, Fixer.Rules);
        }

        [Theory]
        [InlineData("r = @json('{}', as: Repo)\necho $r.name")]
        [InlineData("r = Repo({ name: 'k' })\necho $r.name")]
        [InlineData("r = Repo(name: 'k', stars: 3)\necho $r.stars")]
        [InlineData("r = { name: 'k' } |> Repo()\necho $r.name")]
        [InlineData("a = Age(7)\necho $a")]
        [InlineData("s = Size('small')\necho $s")]
        [InlineData("t = Repo\necho $t.name")]
        [InlineData("echo Repo->check({ name: 'k' }).ok")]
        [InlineData("fn show(repo: Repo?) {\n  echo $repo?.name\n}\n@show(null)")]
        [InlineData("try {\n  throw Missing(what: 'x')\n} catch Missing as e {\n  echo $e.what\n}")]
        public void TheOneFormIsClean(string code)
        {
            List<Diagnostic> diagnostics = Check(Types + code);
            Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
        }

        [Theory]
        [InlineData("r = @json('{}', as: $Repo)\necho $r.name", "r = @json('{}', as: Repo)\necho $r.name")]
        [InlineData("r = $Repo({ name: 'k' })\necho $r.name", "r = Repo({ name: 'k' })\necho $r.name")]
        [InlineData("echo $Repo->check({ name: 'k' }).ok", "echo Repo->check({ name: 'k' }).ok")]
        [InlineData("s = $Size('small')\necho $s", "s = Size('small')\necho $s")]
        [InlineData("a = $Age(7)\necho $a", "a = Age(7)\necho $a")]
        public void ADollarOnATypeIsReportedAndFixed(string code, string better)
        {
            Diagnostic d = Assert.Single(TypeForms(Types + code));
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Contains("without $", d.Message);
            Assert.Equal(Types + better, Fixed(Types + code));
            Assert.Empty(TypeForms(Types + better));
        }

        [Fact]
        public void AVariableKeepsItsDollar()
        {
            Assert.Empty(TypeForms(Types + "repo = Repo(name: 'k')\necho $repo.name"));
        }

        [Theory]
        [InlineData("type T {\n  age: int?\n}\n", "type T {\n  age?: int\n}\n", "age?: int")]
        [InlineData("type T {\n  tags: string[]?\n  name: string\n}\n", "type T {\n  tags?: string[]\n  name: string\n}\n", "tags?: string[]")]
        [InlineData("type T {\n  age?: int?\n}\n", "type T {\n  age?: int\n}\n", "age?: int")]
        [InlineData("error Gone {\n  id: int?\n}\n", "error Gone {\n  id?: int\n}\n", "id?: int")]
        [InlineData("import KScript.Data\n\nmodel M {\n  bio: string?\n}\n", "import KScript.Data\n\nmodel M {\n  bio?: string\n}\n", "bio?: string")]
        [InlineData("import KScript.Queue\n\njob J {\n  note: string?\n\n  fn handle() {\n    echo $note\n  }\n}\n", "import KScript.Queue\n\njob J {\n  note?: string\n\n  fn handle() {\n    echo $note\n  }\n}\n", "note?: string")]
        public void AnOptionalFieldIsMarkedOnItsName(string code, string better, string form)
        {
            Diagnostic d = Assert.Single(TypeForms(code));
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Contains("Mark an optional field on its name: " + form, d.Message);
            Assert.Equal(better, Fixed(code));
            Assert.Empty(TypeForms(better));
        }

        [Theory]
        [InlineData("type T {\n  age?: int\n  tags?: string[]\n}\n")]
        [InlineData("fn show(name: string?, tags: string[]? = null) {\n  echo $name ?? $tags?->count()\n}\n@show(null)\n")]
        public void NullableStaysForParameters(string code)
        {
            Assert.Empty(TypeForms(code));
        }

        [Fact]
        public void AnOptionalFieldInXmlIsMarkedOnItsNameToo()
        {
            const string xml = "<kscript>\n<type name=\"T\">\n  <field name=\"age\" type=\"int?\" />\n</type>\n</kscript>\n";
            Diagnostic d = Assert.Single(TypeForms(xml, "/tmp/type-forms/t.ks.xml"));
            Assert.Contains("name=\"age?\" type=\"int\"", d.Message);
            Assert.Equal(xml.Replace("name=\"age\" type=\"int?\"", "name=\"age?\" type=\"int\""), Fixed(xml, "/tmp/type-forms/t.ks.xml"));
        }

        [Fact]
        public void AModelsOptionLinesMoveIntoItsBrackets()
        {
            const string code = "import KScript.Data\n\nmodel User {\n  timestamps: yes\n  unique: [email]\n\n  name: string\n  email: email\n}\n";
            const string better = "import KScript.Data\n\nmodel User(timestamps, unique: [email]) {\n  name: string\n  email: email\n}\n";
            List<Diagnostic> found = TypeForms(code);
            // One for each line, and one fix that moves them all.
            Assert.Equal(2, found.Count);
            Assert.All(found, d => Assert.Contains("model User(timestamps, unique: [email])", d.Message));
            Assert.Equal(better, Fixed(code));
            Assert.Empty(Check(better));
        }

        [Theory]
        [InlineData("model Post(table: \"posts\") {\n  soft_deletes: yes\n  title: string\n}\n", "model Post(table: \"posts\", soft_deletes) {\n  title: string\n}\n")]
        [InlineData("model Post {\n  title: string\n  hidden: [title]\n}\n", "model Post(hidden: [title]) {\n  title: string\n}\n")]
        [InlineData("model Post {\n  timestamps: no\n  title: string\n}\n", "model Post(timestamps: false) {\n  title: string\n}\n")]
        public void OptionLinesJoinTheOptionsAlreadyInBrackets(string code, string better)
        {
            const string import = "import KScript.Data\n\n";
            Assert.NotEmpty(TypeForms(import + code));
            Assert.Equal(import + better, Fixed(import + code));
            Assert.Empty(Check(import + better));
        }

        [Fact]
        public void FromAndNewAreGone()
        {
            Diagnostic from = Assert.Single(Check(Types + "r = Repo->from({ name: 'k' })\necho $r"));
            Assert.Equal(DiagnosticSeverity.Error, from.Severity);
            Assert.Equal(DiagnosticCodes.UnknownMethod, from.Code);
            Assert.Contains("->from() is gone: call the type to make a value of it, Repo($data).", from.Message);
            Assert.Equal(Types + "r = Repo({ name: 'k' })\necho $r", AfterFix(Types + "r = Repo->from({ name: 'k' })\necho $r", from));

            Diagnostic made = Assert.Single(Check(Types + "e = Missing->new(what: 'x')\necho $e"));
            Assert.Equal(DiagnosticCodes.UnknownMethod, made.Code);
            Assert.Contains("->new() is gone", made.Message);
            Assert.Equal(Types + "e = Missing(what: 'x')\necho $e", AfterFix(Types + "e = Missing->new(what: 'x')\necho $e", made));
        }

        [Fact]
        public void ATypeIsCalledWithItsFieldsOrItsData()
        {
            Assert.Contains(Check(Types + "r = Repo(nope: 1)\necho $r"), d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("nope"));
            Assert.Contains(Check(Types + "r = Repo({}, {})\necho $r"), d => d.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void ANameThatIsNoTypeSaysSo()
        {
            Diagnostic d = Assert.Single(Check("x = Nope\necho $x"));
            Assert.Equal(DiagnosticCodes.UndefinedVariable, d.Code);
            Assert.Equal("No type named Nope is declared or imported here. Text needs quotes ('Nope').", d.Message);
        }

        [Fact]
        public void APackageIsNamedOnlyInItsImport()
        {
            const string code = "import KScript.UI\n\nKScript.Window(title: \"Hi\") {\n  Text \"Hello\"\n}\n";
            Diagnostic d = Assert.Single(Check(code));
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Equal(DiagnosticCodes.MarkupSyntax, d.Code);
            Assert.Contains("Write Window, not KScript.Window", d.Message);
            string better = AfterFix(code, d);
            Assert.Equal("import KScript.UI\n\nWindow(title: \"Hi\") {\n  Text \"Hello\"\n}\n", better);
            Assert.Empty(Check(better));
        }

        [Theory]
        [InlineData("Window(title: \"Hi\") {\n  Text \"Hello\"\n}\n", "Window", "KScript.UI")]
        [InlineData("Routes {\n  Get(path: \"/\") {\n    return 'ok'\n  }\n}\n", "Routes", "KScript.Http")]
        [InlineData("job Ping {\n  fn handle() {\n    echo 'pong'\n  }\n}\n", "job", "KScript.Queue")]
        [InlineData("KScript.Window(title: \"Hi\") {\n  Text \"Hello\"\n}\n", "Window", "KScript.UI")]
        public void NothingIsImportedSilently(string code, string element, string package)
        {
            Diagnostic d = Assert.Single(Check(code), x => x.Code == DiagnosticCodes.MissingImport && x.Message.StartsWith(element + " is part of", StringComparison.Ordinal));
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Contains($"{element} is part of {package}. Add import {package}", d.Message);
            // The fix puts the import at the top of the file, where imports go.
            string better = AfterFix(code, d);
            Assert.StartsWith("import " + package + "\n", better, StringComparison.Ordinal);
            Assert.DoesNotContain(Check(better), x => x.Code == DiagnosticCodes.MissingImport);
        }

        [Fact]
        public void AnXmlFileStillNamesItsPackageInTheRootTag()
        {
            Assert.Empty(Check("<KScript.Window title=\"Hi\">\n  <Text>Hello</Text>\n</KScript.Window>\n", "/tmp/type-forms/app.ks.xml"));
        }
    }
}
