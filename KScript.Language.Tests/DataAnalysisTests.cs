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
            model User(unique: [email], hidden: [password]) extends [Timestamps, SoftDeletes] {
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
                adults = User->where(role: admin, name: ['a', 'b'])->or_where(fn(x) => $x.name->starts_with('A'))->order_by(name: desc, email)->get()
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
        public void TableMethodsAcceptModelsAndRecordsAndHasTableAlsoAcceptsNames()
        {
            TestHelpers.AssertClean(Code(Users + """
                echo $db->has_table(User)
                echo $db->has_table('users')
                echo $db->has_table(name: users)
                echo $db->create_table(model: User)
                member = User->first()
                echo $db->has_table($member)
                echo $db->create_table($member)
                fn table_exists(name: string) => $db->has_table($name)
                fn create_for(value) => $db->create_table($value)
                fn create_or_fallback(value) => $db->create_table($value ?? 'fallback')
                fn check_or_fallback(value) => $db->has_table($value ?? 42)
                fn custom_method(target) => $target->create_table('users')
                """));
        }

        [Theory]
        [InlineData("has_table(42)", "a model or a table name")]
        [InlineData("has_table(name: [])", "a model or a table name")]
        [InlineData("has_table('   ')", "table name cannot be blank")]
        [InlineData("create_table('users')", "a model")]
        [InlineData("create_table(model: false)", "a model")]
        [InlineData("create_table({name: 'User'})", "a model")]
        public void TableMethodsReportKnownInvalidArguments(string call, string expected)
        {
            DocumentAnalysis a = Code("import KScript.Data\necho $db->" + call);
            Diagnostic d = Assert.Single(a.Diagnostics, x => x.Code == DiagnosticCodes.TypeMismatch);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Contains(expected, d.Message);
        }

        [Fact]
        public void PatternLiteralsAreTableNamesButCannotStandInForAModel()
        {
            const string declarations = "import KScript.Data\ntype TableName with [string] { pattern: /^table_[a-z]+$/ }\n";
            TestHelpers.AssertClean(Code(declarations + "echo $db->has_table(table_users)\necho $db->has_table(name: table_users)"));
            foreach (string argument in new[] { "table_users", "model: table_users" })
            {
                DocumentAnalysis a = Code(declarations + $"echo $db->create_table({argument})");
                Diagnostic diagnostic = Assert.Single(a.Diagnostics);
                Assert.Equal(DiagnosticCodes.TypeMismatch, diagnostic.Code);
                Assert.Contains("needs a model", diagnostic.Message);
            }
        }

        [Fact]
        public void TableMethodsReportPlainTypesAndDeclaredWrongVariables()
        {
            DocumentAnalysis a = Code("""
                import KScript.Data
                type Person { name: string }
                echo $db->has_table(Person)
                fn create_for(name: string) => $db->create_table($name)
                """);
            Assert.Equal(2, a.Diagnostics.Count(d => d.Code == DiagnosticCodes.TypeMismatch));
        }

        [Fact]
        public void AModelCanNameItsKey()
        {
            DocumentAnalysis a = Code("""
                import KScript.Data

                model Country(key: 'code') {
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

                model Post(table: 'posts', auto_create, unique: [slug, [team, slug]], index: [team], guarded: [team]) {
                  slug: string
                  team: int
                }
                """;
            MarkupDocument doc = CodeParser.Parse(code);
            Assert.DoesNotContain(doc.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            MarkupElement model = doc.RootElements.Single().ChildElements.Single(e => e.Name == "model");
            Assert.Equal("'posts'", model.GetAttributeValue("table"));
            Assert.Equal("yes", model.GetAttributeValue("auto_create"));
            Assert.Equal("[slug, [team, slug]]", model.GetAttributeValue("unique"));

            string xml = ScriptConverter.Convert(code, "post.kscript").Text;
            Assert.Contains("<model name=\"Post\" table=\"posts\" auto_create=\"yes\" unique=\"[slug, [team, slug]]\" index=\"[team]\" guarded=\"[team]\">", xml);
            string back = ScriptConverter.Convert(xml, "post.ks.xml").Text;
            // Options go in the brackets, whichever way the file was written.
            Assert.Contains("model Post(table: \"posts\", auto_create, unique: [slug, [team, slug]], index: [team], guarded: [team]) {\n  slug: string", back);
            Assert.Contains("model User(hidden: [password]) {\n  name: string", ScriptConverter.Convert("<kscript><import package=\"KScript.Data\" /><model name=\"User\" hidden=\"password\" fields=\"name: string, password: password\" /></kscript>", "u.ks.xml").Text);

            MarkupDocument twice = CodeParser.Parse("model A {\n  auto_create: yes\n  auto_create: no\n  name: string\n}\n");
            Assert.Contains(twice.Diagnostics, d => d.Message == "This model already sets auto_create.");
        }

        private const string Blog = """
            import KScript.Data

            model User {
              name: string
              posts: Post[](by: author)
              profile?: Profile
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
                u = User->find(1)
                echo $u.posts->map(fn(p) => $p.title)
                echo $u.profile.bio
                echo $u.posts_count
                p = Post->first()
                echo [$p.author.name, $p.author_id]
                q = $u->posts()->where(published: true)
                echo $q->first().title
                echo $u->posts()->create(title: 'x').author.name
                echo $p->tags()->attach([1, 2])
                echo Post->drafts()->popular(5)->with(author, tags, author.posts)->with_count(tags)->get()
                echo Post->where(fn(x) => $x.author.name == 'Ada')->drafts()->first().title
                echo $p->load(author).title
                """);
            TestHelpers.AssertClean(a);

            DocumentAnalysis wrong = Code(Blog + """
                u = User->find(1)
                echo $u.profile_id
                echo $u->postz()
                echo Post->popular(1, 2, 3)
                echo Post->draftz()
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
                  self?: User
                }

                u = User->first()
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

                  fn popular(query, at_least: int = 10) => $query->where(fn(p) => $p.views >= $at_least)
                  fn unused(query) => $query
                }

                echo Post->popular()->count()
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
                  CreateTable(name: 'teams') {
                    Field(name: 'id', type: int, key, auto)
                    Field(name: 'name', type: string, max: 40)
                    Index(columns: [name], unique)
                  }
                  AddField(table: 'teams', name: 'size', type: int, default: 0)
                  Down {
                    DropTable(name: 'teams')
                  }
                }
                """);
            TestHelpers.AssertClean(a);

            DocumentAnalysis outside = Code("import KScript.Data\n\nField(name: 'x', type: string)\n");
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
                  User->find_or_fail(1)
                } catch ModelNotFound as e {
                  echo $e.message
                } catch UniqueViolation {
                  echo 'taken'
                } catch Query {
                  echo 'query'
                }
                """);
            TestHelpers.AssertClean(a);
        }

        private const string Shop = """
            import KScript.Data

            model Author {
              name: string
              posts: Post[]
            }

            model Post extends SoftDeletes {
              title: string
              views: int = 0
              author: Author
            }

            """;

        private static List<string> Labels(string textWithCaret)
        {
            int offset = textWithCaret.IndexOf('‸', StringComparison.Ordinal);
            string text = textWithCaret.Remove(offset, 1);
            return Service.GetCompletions(Service.Analyze(text, Path.Combine(Folder, "app.kscript")), offset).Select(i => i.Label).ToList();
        }

        [Fact]
        public void ColumnsAreCheckedAndCompleted()
        {
            DocumentAnalysis a = Code(Shop + """
                echo Post->where(titel: 'x', views: 1)->order_by(veiws, title: desc)->with(autor, author.posts)->get()
                echo Post->where(fn(p) => $p.titel == 'x')->pluck(title)
                echo Author->create(nme: 'Joan')
                echo Post->fake_many(2, count: 3)
                echo Post->latest(xyz)->first()
                """);
            string[] messages = a.Diagnostics.Where(d => d.Code is "KS5013" or "KS5010").Select(d => d.Code + " " + d.Message).ToArray();
            Assert.Contains("KS5013 Post has no field 'titel'. Did you mean title?", messages);
            Assert.Contains("KS5013 Post has no field 'veiws'. Did you mean views?", messages);
            Assert.Contains("KS5013 Post has no relation 'autor'. Did you mean author?", messages);
            Assert.Contains("KS5010 Post has no field 'titel'. Did you mean title?", messages);
            Assert.Contains("KS5013 Author has no field 'nme'. Did you mean name?", messages);
            Assert.Contains(messages, m => m.StartsWith("KS5013 Post has no field 'xyz'. Its fields are: ", StringComparison.Ordinal));
            Assert.Equal(6, messages.Length);
            Assert.Contains(a.Diagnostics, d => d.Code == "KS5013" && d.Fixes?.Count > 0);

            List<string> where = Labels(Shop + "echo Post->where(‸)");
            Assert.Equal(new[] { "author_id:", "deleted_at:", "id:", "title:", "views:" }, where.Take(5).OrderBy(l => l, StringComparer.Ordinal));
            Assert.DoesNotContain("title:", Labels(Shop + "echo Post->where(title: 'x', ‸)"));
            Assert.Contains("views", Labels(Shop + "echo Post->order_by(‸)"));
            Assert.Equal("author", Labels(Shop + "echo Post->with(‸)")[0]);
            Assert.Contains("title", Labels(Shop + "echo Post->where(fn(p) => $p.‸)"));
            Assert.DoesNotContain("title:", Labels(Shop + "echo Post->paginate(‸)"));
            Assert.DoesNotContain("title:", Labels("echo [1]->map(‸)"));
        }

        [Fact]
        public void HoverShowsAQuerysSql()
        {
            string Sql(string chain, string at)
            {
                string text = Shop + chain + "\n";
                DocumentAnalysis a = Service.Analyze(text, Path.Combine(Folder, "app.kscript"));
                string markdown = Service.GetHover(a, text.LastIndexOf(at, StringComparison.Ordinal) + 3).Markdown;
                int start = markdown.IndexOf("```sql\n", StringComparison.Ordinal);
                return start < 0 ? null : markdown.Substring(start + 7, markdown.IndexOf("\n```", start + 7, StringComparison.Ordinal) - start - 7);
            }

            Assert.Equal("SELECT * FROM posts\nWHERE title = ? AND views IN (…) AND deleted_at IS NULL\nORDER BY views, title DESC\nLIMIT ? OFFSET ?",
                Sql("echo Post->where(title: 'x', views: [1])->order_by(views, title: desc)->limit(2)->offset(4)->get()", "->get"));
            Assert.Equal("SELECT * FROM posts\nWHERE ((title LIKE ? OR views > ?) AND (author_id IS NULL) IS NOT TRUE OR (UPPER(title) = ?) IS NOT TRUE) AND deleted_at IS NULL\nORDER BY id DESC\nLIMIT 1",
                Sql("echo Post->where(fn(p) => ($p.title->starts_with('A') or $p.views > 3) and not ($p.author_id == null) or $p.title->upper() != $x)->latest()->first()", "->first"));
            Assert.Equal("SELECT DISTINCT COUNT(*) FROM posts\nWHERE (author_id IS NOT NULL OR …) AND deleted_at IS NOT NULL",
                Sql("echo Post->where(fn(p) => $p.author_id != null)->or_where(fn(a, b) => 1)->only_trashed()->distinct()->count()", "->count"));
            Assert.Equal("SELECT title FROM authors\nWHERE (name IS NULL AND …) IS NOT TRUE AND id = ?\nLIMIT 1\n-- then one query for posts.author",
                Sql("echo Author->select(title)->where_not(name: null, fn(a) => $a.name->trim())->with(posts.author)->find(1)", "->find"));
            Assert.Equal("SELECT SUM(views) FROM posts\nORDER BY id ASC",
                Sql("echo Post->with_trashed()->oldest()->sum(views)", "->sum"));
            Assert.Equal("SELECT 1 FROM authors\nLIMIT 1", Sql("echo Author->exists()", "->exists"));
            Assert.Equal("SELECT MAX(…) FROM authors", Sql("echo Author->select($x)->max()", "->max"));
            Assert.Equal("SELECT name FROM authors\nWHERE …", Sql("echo Author->where()->pluck('name')", "->pluck"));
            Assert.Null(Sql("p = Post->first()\necho $p->posts()->count()", "->count"));
            Assert.Null(Sql("echo [1, 2]->count()", "->count"));
            Assert.Null(Sql("echo $db->tables()", "->tables"));
            Assert.Equal("SELECT * FROM posts\nWHERE … AND deleted_at IS NULL", Sql("echo Post->where(fn(p) => [1])->get()", "->get"));
        }

        [Fact]
        public void AModelTheMigrationsDontMakeIsFlagged()
        {
            string root = Path.Combine(Path.GetTempPath(), "kscript-drift-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "models"));
                Directory.CreateDirectory(Path.Combine(root, "database", "migrations"));
                File.WriteAllText(Path.Combine(root, "kscript.json"), "{ \"name\": \"drift\" }");
                const string model = "import KScript.Data\n\nmodel Note {\n  body: string\n}\n";
                string path = Path.Combine(root, "models", "note.kscript");
                File.WriteAllText(path, model);
                DocumentAnalysis Check(string text) => Analyzer.Analyze(text, path, DiskFileSystem.Instance);

                // No migrations yet: nothing to compare with.
                Assert.DoesNotContain(Check(model).Diagnostics, d => d.Code == "KS5014");

                string migration = Path.Combine(root, "database", "migrations", "2026_01_01_000000_notes.kscript");
                File.WriteAllText(migration, "import KScript.Data\n\nMigration {\n  CreateTable(name: 'notes') {\n    Field(name: 'id', type: int, key, auto)\n  }\n}\n");
                Diagnostic drift = Assert.Single(Check(model).Diagnostics, d => d.Code == "KS5014");
                Assert.Equal("The migrations don't make Note's table as it is now: it needs a migration to add field notes.body. Run kscript db diff to write it.", drift.Message);
                Assert.Equal("Note", model.Substring(drift.Span.Start, drift.Span.Length));

                // The same files give the same answer without running them again; a changed one is read again.
                Assert.Single(Check(model).Diagnostics, d => d.Code == "KS5014");
                File.WriteAllText(migration, "import KScript.Data\n\nMigration {\n  CreateTable(name: 'notes') {\n    Field(name: 'id', type: int, key, auto)\n    Field(name: 'body', type: string)\n  }\n}\n");
                File.SetLastWriteTimeUtc(migration, DateTime.UtcNow.AddMinutes(1));
                Assert.DoesNotContain(Check(model).Diagnostics, d => d.Code == "KS5014");

                // Two changes, a migration that can't run, and a file that isn't in a project.
                File.WriteAllText(migration, "import KScript.Data\n\nMigration {\n  CreateTable(name: 'notes') {\n    Field(name: 'id', type: int, key, auto)\n    Field(name: 'old', type: string)\n  }\n}\n");
                File.SetLastWriteTimeUtc(migration, DateTime.UtcNow.AddMinutes(2));
                Assert.Contains("and 1 more change", Assert.Single(Check(model).Diagnostics, d => d.Code == "KS5014").Message);
                File.WriteAllText(migration, "import KScript.Data\n\nMigration {\n  DropTable(name: 'nope')\n}\n");
                File.SetLastWriteTimeUtc(migration, DateTime.UtcNow.AddMinutes(3));
                Assert.DoesNotContain(Check(model).Diagnostics, d => d.Code == "KS5014");
                File.WriteAllText(Path.Combine(root, "kscript.json"), "{ \"name\": ");
                Assert.DoesNotContain(Check(model).Diagnostics, d => d.Code == "KS5014");
                Assert.Empty(KScript.Data.Migrations.Drift.Of(Path.GetTempPath()));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
