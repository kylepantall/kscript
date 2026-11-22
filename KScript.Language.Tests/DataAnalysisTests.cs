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
            model User {
              timestamps: yes
              soft_deletes: yes
              unique: [email]
              hidden: [password]
              name: string
              email: email
              role: Role = 'member'
              password: password?

              fn label() => $name + ' <' + $email + '>'
            }

            """;

        [Fact]
        public void ModelsAreTypesWithQueryMethods()
        {
            DocumentAnalysis a = Code(Users + """
                $db->create_table($User)
                u = $User->create(name: 'Ada', email: 'ada@example.com')
                $u->save()
                echo $u->label()
                echo [$u.id, $u.created_at, $u.updated_at, $u.deleted_at]
                adults = $User->where(role: admin, name: ['a', 'b'])->or_where(fn($x) => $x.name->starts_with('A'))->order_by(name: desc, email)->get()
                for one in $adults {
                  echo $one.name
                  $one->delete()
                }
                first = $User->where(role: member)->first()
                echo $first.email
                echo $User->pluck(email)
                echo $User->find_or_fail(1).name
                echo $User->paginate(10).items
                q = $User->query()
                echo $q->count()
                echo $User->all()->count()
                email = 'ada@example.com'
                limit = 3
                echo @sql("select * from users where email = {$email} and id > {{ 1 }}")
                echo $db->sql("select * from users limit {$limit + 1}")
                text = 'select 1'
                echo @sql($text)
                echo @db(users)->where(name: 'Ada')->get()
                echo @db(connection: main)->tables()
                transaction {
                  $User->where(name: 'Ada')->update(name: 'Ada L')
                }
                """);
            TestHelpers.AssertClean(a);
        }

        [Fact]
        public void MistakesOnModelsAndRecordsAreFound()
        {
            DocumentAnalysis a = Code(Users + """
                u = $User->find(1)
                echo $u.nme
                echo $User->wher(name: 'x')
                echo $User->where(name: 'x')->frist()
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

                model Country {
                  key: code
                  code: string
                }
                c = $Country->find('nz')
                echo $c.code
                echo $c.id
                """);
            Assert.Contains(a.Diagnostics, d => d.Message.Contains("Country has no field 'id'"));
        }

        [Fact]
        public void HoverShowsTheModel()
        {
            string text = Users + "q = $User->where(name: 'x')\necho $q\n";
            DocumentAnalysis a = Service.Analyze(text, Path.Combine(Folder, "app.kscript"));
            string hover = Service.GetHover(a, text.IndexOf("$User->where", StringComparison.Ordinal) + 2).Markdown;
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

                echo $User->first().name
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

                model Post(table: posts) {
                  timestamps: yes
                  unique: [slug, [team, slug]]
                  index: [team]
                  guarded: [team]
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
            Assert.Contains("model Post {\n  table: posts\n  timestamps: yes\n  unique: [slug, [team, slug]]\n  index: [team]\n  guarded: [team]\n  slug: string", back);
            Assert.Contains("model User {\n  hidden: [password]", ScriptConverter.Convert("<kscript><import package=\"KScript.Data\" /><model name=\"User\" hidden=\"password\" fields=\"name: string, password: password\" /></kscript>", "u.ks.xml").Text);

            MarkupDocument twice = CodeParser.Parse("model A {\n  timestamps: yes\n  timestamps: no\n  name: string\n}\n");
            Assert.Contains(twice.Diagnostics, d => d.Message == "This model already sets timestamps.");
        }

        private const string Blog = """
            import KScript.Data

            model User {
              name: string
              posts: Post[](by: author)
              profile: Profile?
            }

            model Profile {
              user: User
              bio: string
            }

            model Post {
              title: string
              published: bool = false
              author: User(on_delete: cascade)
              tags: Tag[]

              fn drafts(query) => $query->where(published: false)
              fn popular(query, over: int = 10) => $query
            }

            model Tag {
              name: string
            }

            """;

        [Fact]
        public void RelationsAreFieldsMethodsAndColumns()
        {
            DocumentAnalysis a = Code(Blog + """
                u = $User->find(1)
                echo $u.posts->map(fn($p) => $p.title)
                echo $u.profile.bio
                echo $u.posts_count
                p = $Post->first()
                echo [$p.author.name, $p.author_id]
                q = $u->posts()->where(published: true)
                echo $q->first().title
                echo $u->posts()->create(title: 'x').author.name
                echo $p->tags()->attach([1, 2])
                echo $Post->drafts()->popular(5)->with(author, tags, author.posts)->with_count(tags)->get()
                echo $Post->where(fn($x) => $x.author.name == 'Ada')->drafts()->first().title
                echo $p->load(author).title
                """);
            TestHelpers.AssertClean(a);

            DocumentAnalysis wrong = Code(Blog + """
                u = $User->find(1)
                echo $u.profile_id
                echo $u->postz()
                echo $Post->popular(1, 2, 3)
                echo $Post->draftz()
                echo @len(author.posts)
                """);
            Assert.Contains(wrong.Diagnostics, d => d.Message.Contains("User has no field 'profile_id'"));
            Assert.Contains(wrong.Diagnostics, d => d.Message.Contains("->postz()"));
            Assert.Contains(wrong.Diagnostics, d => d.Message.Contains("->popular() takes 0 to 1 argument"));
            Assert.Contains(wrong.Diagnostics, d => d.Message.Contains("Unknown method ->draftz()"));
            Assert.Contains(wrong.Diagnostics, d => d.Code == "KS2004" && d.Message.Contains("'author' isn't a value"));
        }

        [Fact]
        public void ModelsInTheModelsFolderAreKnownWithoutImports()
        {
            const string user = """
                import KScript.Data

                model User {
                  name: string
                  posts: Post[]
                  badges: Badge[]
                  self: User?
                }

                u = $User->first()
                echo $u.posts[0].author.name
                echo $u->posts()->count()
                echo $u.posts[0]->publish()
                """;
            var files = new AnalyzerCoverageTests.Files(
                ("kscript.json", "{ \"name\": \"blog\" }"),
                ("models/user.kscript", user),
                ("models/post.kscript", "import KScript.Data\n\n/// A blog post.\nmodel Post {\n  title: string\n  author: User\n}\n"),
                ("models/badge.kscript", "import KScript.Data\n\nenum Colour { red }\n"));
            string path = Path.Combine(Folder, "models", "user.kscript");
            DocumentAnalysis a = TestHelpers.WithoutUnused(Analyzer.Analyze(user, path, files));
            Assert.DoesNotContain(a.Diagnostics, d => d.Message.Contains("Post", StringComparison.Ordinal));
            Assert.Contains(a.Symbols, s => s.Name == "Post" && s.Description.Contains("A blog post", StringComparison.Ordinal));
            Assert.DoesNotContain(a.Symbols, s => s.Name == "Badge");

            // A field with no type in an XML model is skipped when looking for the models it names.
            DocumentAnalysis odd = TestHelpers.WithoutUnused(Analyzer.Analyze("<kscript><import package=\"KScript.Data\" /><model name=\"Odd\" fields=\"name: string, oops\" /></kscript>", Path.Combine(Folder, "odd.ks.xml"), EmptyFileSystem.Instance));
            Assert.DoesNotContain(odd.Symbols, s => s.Name == "oops" && s.Kind == SymbolKind.Constant);
        }

        [Fact]
        public void ValuesOfNoKnownTypeMayBeRecordsWithRelations()
        {
            DocumentAnalysis a = Code("""
                import KScript.Data

                fn titles(user) => $user->posts()->where(published: true)->count()
                fn loaded(query) => $query->with(author, tags)->get()
                """);
            TestHelpers.AssertClean(a);

            // Without KScript.Data there are no relations, so a misspelt method is still found.
            DocumentAnalysis plain = Code("""
                fn size(text) => $text->lenght()
                """);
            Assert.Contains(plain.Diagnostics, d => d.Code == DiagnosticCodes.UnknownMethod);
        }

        [Fact]
        public void ScopesNeedNoTypeAndAreCalledThroughTheModel()
        {
            const string post = """
                import KScript.Data

                model Post {
                  title: string
                  views: int = 0

                  fn popular(query, at_least: int = 10) => $query->where(fn($p) => $p.views >= $at_least)
                  fn unused(query) => $query
                }

                echo $Post->popular()->count()
                """;
            DocumentAnalysis a = Analyzer.Analyze(post, Path.Combine(Folder, "app.kscript"), EmptyFileSystem.Instance);
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.MissingParameterType);
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.UnusedFunction && d.Message.Contains("popular", StringComparison.Ordinal));
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.UnusedFunction && d.Message.Contains("unused", StringComparison.Ordinal));

            // Outside a model, query is a parameter like any other.
            DocumentAnalysis plain = Analyzer.Analyze("fn f(query) => $query\necho f(1)\n", Path.Combine(Folder, "app.kscript"), EmptyFileSystem.Instance);
            Assert.Contains(plain.Diagnostics, d => d.Code == DiagnosticCodes.MissingParameterType);
        }

        [Fact]
        public void MigrationFilesAreChecked()
        {
            DocumentAnalysis a = Code("""
                import KScript.Data

                Migration {
                  CreateTable(name: teams) {
                    Field(name: id, type: int, key: yes, auto: yes)
                    Field(name: name, type: string, max: 40)
                    Index(columns: [name], unique: yes)
                  }
                  AddColumn(table: teams, name: size, type: int, default: 0)
                  Down {
                    DropTable(name: teams)
                  }
                }
                """);
            TestHelpers.AssertClean(a);

            DocumentAnalysis outside = Code("import KScript.Data\n\nField(name: x, type: string)\n");
            Assert.Contains(outside.Diagnostics, d => d.Code == "KS3007" && d.Message.Contains("inside CreateTable", StringComparison.Ordinal));

            DocumentAnalysis xml = TestHelpers.WithoutUnused(Analyzer.Analyze(
                "<kscript><import package=\"KScript.Data\" /><Migration><DropTable name=\"teams\" /></Migration></kscript>",
                Path.Combine(Folder, "m.ks.xml"), EmptyFileSystem.Instance));
            TestHelpers.AssertClean(xml);
        }

        [Fact]
        public void TheErrorTypesAreKnown()
        {
            DocumentAnalysis a = Code(Users + """
                try {
                  $User->find_or_fail(1)
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
