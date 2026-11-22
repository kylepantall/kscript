using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>KScript.Data in the editor: models are types, their methods and column words are known, and @sql's { } use variables.</summary>
    public class DataAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);

        private static string Folder => AnalyzerCoverageTests.Folder;

        private static DocumentAnalysis Code(string text, IFileSystem files = null) =>
            TestHelpers.WithoutUnused(Analyzer.Analyze(text, Path.Combine(Folder, "app.kscript"), files ?? EmptyFileSystem.Instance));

        private const string Users = """
            import KScript.Data

            enum Role { member, admin }

            /// Someone who can sign in.
            model User(timestamps, soft_deletes, unique: [email], hidden: [password]) {
              name: string
              email: email
              role: Role = 'member'
              password?: password

              fn label() => $name + ' <' + $email + '>'
            }

            """;

        [Fact]
        public void ModelsAreTypesWithQueryMethods()
        {
            DocumentAnalysis a = Code(Users + """
                $db->create_table(User)
                u = User->create(name: 'Ada', email: 'ada@example.com')
                $u->save()
                echo $u->label()
                echo [$u.id, $u.created_at, $u.updated_at, $u.deleted_at]
                adults = User->where(role: admin, name: ['a', 'b'])->or_where(fn($x) => $x.name->starts_with('A'))->order_by(name: desc, email)->get()
                for one in $adults {
                  echo $one.name
                  $one->delete()
                }
                first = User->where(role: member)->first()
                echo $first.email
                echo User->pluck(email)
                echo User->find_or_fail(1).name
                echo User->paginate(10).items
                q = User->query()
                echo $q->count()
                echo User->all()->count()
                email = 'ada@example.com'
                limit = 3
                echo @sql("select * from users where email = {$email} and id > {{ 1 }}")
                echo $db->sql("select * from users limit {$limit + 1}")
                text = 'select 1'
                echo @sql($text)
                echo @db(users)->where(name: 'Ada')->get()
                echo @db(connection: main)->tables()
                transaction {
                  User->where(name: 'Ada')->update(name: 'Ada L')
                }
                """);
            TestHelpers.AssertClean(a);
        }

        [Fact]
        public void MistakesOnModelsAndRecordsAreFound()
        {
            DocumentAnalysis a = Code(Users + """
                u = User->find(1)
                echo $u.nme
                echo User->wher(name: 'x')
                echo User->where(name: 'x')->frist()
                """);
            Assert.Contains(a.Diagnostics, d => d.Message.Contains("User has no field 'nme'"));
            Assert.Contains(a.Diagnostics, d => d.Message.Contains("Unknown method ->wher()"));
            Assert.Contains(a.Diagnostics, d => d.Message.Contains("Unknown method ->frist()"));
        }

        [Fact]
        public void AModelCanNameItsKey()
        {
            DocumentAnalysis a = Code("""
                import KScript.Data

                model Country(key: code) {
                  code: string
                }
                c = Country->find('nz')
                echo $c.code
                echo $c.id
                """);
            Assert.Contains(a.Diagnostics, d => d.Message.Contains("Country has no field 'id'"));
        }

        [Fact]
        public void HoverShowsTheModel()
        {
            string text = Users + "q = User->where(name: 'x')\necho $q\n";
            DocumentAnalysis a = Service.Analyze(text, Path.Combine(Folder, "app.kscript"));
            string hover = Service.GetHover(a, text.IndexOf("User->where", StringComparison.Ordinal) + 2).Markdown;
            Assert.Contains("model User {", hover);
            Assert.Contains("created_at", hover);
            string query = Service.GetHover(a, text.LastIndexOf("$q", StringComparison.Ordinal) + 1).Markdown;
            Assert.Contains("query of User", query);
        }

        [Fact]
        public void ModelsInXmlAndImportedModels()
        {
            var files = new AnalyzerCoverageTests.Files(("user.ks.xml", """
                <kscript>
                  <import package="KScript.Data" />
                  <model name="User" fields="name: string">
                    <fn name="hello" value="'hi ' + $name" />
                  </model>
                </kscript>
                """));
            DocumentAnalysis a = Code("""
                import KScript.Data
                import "user.ks.xml"

                echo User->first().name
                """, files);
            TestHelpers.AssertClean(a);
            DocumentAnalysis xml = TestHelpers.WithoutUnused(Analyzer.Analyze(files.ReadText(Path.Combine(Folder, "user.ks.xml")), Path.Combine(Folder, "user.ks.xml"), files));
            TestHelpers.AssertClean(xml);
            Assert.DoesNotContain(xml.Symbols, s => s.Name == "hello" && s.Kind == SymbolKind.Function && s.DeclaringElement.Parent is MarkupElement p && p.Name == "kscript");
        }

        [Fact]
        public void ModelSettingsParseAndConvert()
        {
            const string code = """
                import KScript.Data

                model Post(table: posts, timestamps, unique: [slug, [team, slug]], index: [team], guarded: [team]) {
                  slug: string
                  team: int
                }
                """;
            MarkupDocument doc = CodeParser.Parse(code);
            Assert.DoesNotContain(doc.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            MarkupElement model = doc.RootElements.Single().ChildElements.Single(e => e.Name == "model");
            Assert.Equal("posts", model.GetAttributeValue("table"));
            Assert.Equal("yes", model.GetAttributeValue("timestamps"));
            Assert.Equal("[slug, [team, slug]]", model.GetAttributeValue("unique"));

            string xml = ScriptConverter.Convert(code, "post.kscript").Text;
            Assert.Contains("<model name=\"Post\" table=\"posts\" timestamps=\"yes\" unique=\"[slug, [team, slug]]\" index=\"[team]\" guarded=\"[team]\">", xml);
            string back = ScriptConverter.Convert(xml, "post.ks.xml").Text;
            // Options go in the brackets, whichever way the file was written.
            Assert.Contains("model Post(table: \"posts\", timestamps, unique: [slug, [team, slug]], index: [team], guarded: [team]) {\n  slug: string", back);
            Assert.Contains("model User(hidden: [password]) {\n  name: string", ScriptConverter.Convert("<kscript><import package=\"KScript.Data\" /><model name=\"User\" hidden=\"password\" fields=\"name: string, password: password\" /></kscript>", "u.ks.xml").Text);

            MarkupDocument twice = CodeParser.Parse("model A {\n  timestamps: yes\n  timestamps: no\n  name: string\n}\n");
            Assert.Contains(twice.Diagnostics, d => d.Message == "This model already sets timestamps.");
        }

        [Fact]
        public void TheErrorTypesAreKnown()
        {
            DocumentAnalysis a = Code(Users + """
                try {
                  User->find_or_fail(1)
                } catch ModelNotFound as e {
                  echo $e.message
                } catch UniqueViolation {
                  echo 'taken'
                } catch QueryError {
                  echo 'query'
                }
                """);
            TestHelpers.AssertClean(a);
        }
    }
}
