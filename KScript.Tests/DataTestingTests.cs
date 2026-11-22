using KScript.Data;
using KScript.Runtime;
using KScript.Testing;

namespace KScript.Tests;

/// <summary>KScript.Data under kscript test: a database per test file, each test undone, fakes and @queries().</summary>
public sealed class DataTestingTests : DataFixture
{
    private const string Author = """
        import KScript.Data

        enum Level { reader, writer }

        model Author {
          name: string(min: 30, max: 40)
          email: email
          handle: handle
          level: Level
          born: date
          score: number(min: 5)
          tags: string[]
          code: uuid
          secret: password
          verified: bool
          nickname?: string
          posts: Post[]
        }
        """;

    private const string Post = """
        import KScript.Data

        model Post {
          title: string(max: 12)
          views: int = 0
          author: Author
        }
        """;

    private TestFileResult RunTests(string tests, string json = "{ \"name\": \"testing\" }")
    {
        Database.CloseAll();
        if (Server != null) DatabaseServer.Reset(Server);
        FileOf("kscript.json", json);
        FileOf("models/author.kscript", Author);
        FileOf("models/post.kscript", Post);
        FileOf("tests/blog.test.kscript", "import KScript.Data\nimport \"../models/author.kscript\"\nimport \"../models/post.kscript\"\n\n" + tests);
        return TestRunner.RunFile(Path.Combine(Project, "tests", "blog.test.kscript"), new TestRunSettings { BaseDirectory = Project });
    }

    private static void Passed(TestFileResult file)
    {
        Assert.Null(file.Error);
        Assert.All(file.Tests, t => Assert.True(t.Outcome == TestOutcome.Passed, t.Name + ": " + t.Message));
    }

    [Fact]
    public void EachTestFileHasItsOwnDatabaseMadeFromTheModels()
    {
        TestFileResult file = RunTests("""
            test(name: 'one') {
              post = Post->fake()
              expect(value: $post.author.name->count(), equals: 30)
              expect(value: Author->count(), equals: 1)
              expect(value: @queries(), count_at_most: 4)
            }

            test(name: 'two') {
              expect(value: Post->count(), equals: 0)
              expect(value: @queries(), count: 1)
              $db->transaction(fn() => Author->fake())
              expect(value: Author->count(), equals: 1)
            }

            test(name: 'three') {
              expect(value: Author->count(), equals: 0)
              joan = Author->fake(name: 'Joan Joan Joan Joan Joan Joan Joan', nickname: 'Jo')
              posts = Post->fake_many(2, author: $joan)
              expect(value: $posts, count: 2)
              expect(value: $posts[1].title->count() <= 12, equals: true)
              expect(value: $joan.level, equals: 'reader')
              expect(value: $joan.score >= 5, equals: true)
              expect(value: $joan.email->ends_with('@example.com'), equals: true)
              expect(value: Author->fake().verified, equals: false)
            }

            test(name: 'fails') {
              Post->fake_many(5)
              expect(value: @queries(), count_at_most: 2)
            }
            """);
        Assert.Null(file.Error);
        Assert.True(file.Tests.Take(3).All(t => t.Outcome == TestOutcome.Passed), string.Join("\n", file.Tests.Select(t => t.Message)));
        Assert.Equal(new[] { TestOutcome.Passed, TestOutcome.Passed, TestOutcome.Passed, TestOutcome.Failed }, file.Tests.Select(t => t.Outcome));
        Assert.Contains("to have at most 2 items, but it has", file.Tests[3].Message);

        // Nothing went in the project's own database.
        Assert.False(File.Exists(Path.Combine(Project, "data", "app.sqlite")));
    }

    [Fact]
    public void MigrationsMakeTheTestDatabaseWhenThereAreSome()
    {
        FileOf("database/migrations/2026_01_01_000000_notes.kscript", "import KScript.Data\n\nMigration {\n  CreateTable(name: 'notes') {\n    Field(name: 'id', type: int, key, auto)\n    Field(name: 'body', type: string)\n  }\n}\n");
        TestFileResult file = RunTests("""
            test(name: 'the migrations ran') {
              expect(value: $db->tables(), contains: 'notes')
              expect(value: $db->tables(), not_contains: 'posts')
            }
            """);
        Passed(file);
    }

    [Fact]
    public void ConfigUsesTheConfiguredDatabaseAndUndoesEachTest()
    {
        if (Server == null) FileOf("config.yaml", "database:\n  connections:\n    main:\n      url: sqlite:data/test.sqlite\n");
        Keep();
        TestFileResult file = RunTests("""
            $db->create_table(Author)
            $db->create_table(Post)

            test(name: 'adds') {
              Author->fake()
              expect(value: Author->count(), equals: 1)
              expect_error(type: UniqueViolation) {
                $db->sql("INSERT INTO authors (id, name, email, handle, level, born, score, tags, code, secret, verified) SELECT id, name, email, handle, level, born, score, tags, code, secret, verified FROM authors")
              }
              expect(value: Author->count(), equals: 1)
            }

            test(name: 'sees nothing') {
              expect(value: Author->count(), equals: 0)
            }
            """, "{ \"name\": \"testing\", \"test\": { \"database\": \"config\" } }");
        Passed(file);
        if (Server == null) Assert.True(File.Exists(Path.Combine(Project, "data", "test.sqlite")));
    }

    private void Keep() => Directory.CreateDirectory(Path.Combine(Project, "data"));

    [Fact]
    public void FakesNeedAModelTheyCanFill()
    {
        TestFileResult file = RunTests("""
            test(name: 'count') {
              expect_error(contains: "whole") {
                Post->fake_many(-1)
              }
              expect_error(contains: "count") {
                Post->fake_many(1, 'x')
              }
              expect(value: Post->fake_many(0), count: 0)
              expect(value: Post->fake_many(1, { title: 'Mapped' })[0].title, equals: 'Mapped')
              expect_error(contains: "needs") {
                Post->create(title: 'x', author: Post->fake())
              }
            }
            """);
        Passed(file);
    }

    [Fact]
    public void AFakeWhoseRequiredRelationsGoRoundInACircleSaysSo()
    {
        FileOf("models/node.kscript", "import KScript.Data\n\nmodel Node {\n  name: string\n  parent: Node\n}\n");
        TestFileResult file = RunTests("""
            import "../models/node.kscript"

            test(name: 'circle') {
              expect_error(contains: "circle") {
                Node->fake()
              }
            }
            """);
        Passed(file);
    }

    [Fact]
    public void AMigrationThatFailsStopsTheTestFile()
    {
        FileOf("database/migrations/2026_01_01_000000_bad.kscript", "import KScript.Data\n\nMigration {\n  DropTable(name: 'nothing_here')\n}\n");
        TestFileResult file = RunTests("""
            test(name: 'never runs') {
              expect(value: Post->count(), equals: 0)
            }
            """);
        Assert.NotNull(file.Error ?? file.Tests.FirstOrDefault(t => t.Outcome != TestOutcome.Passed)?.Message);
        Assert.Contains("nothing_here", file.Error?.ToString() ?? file.Tests[0].Message);
    }

    [Fact]
    public void BindingsAreShownAsScriptValues()
    {
        Assert.Null(QueryLog.Shown(null));
        Assert.Equal("a", QueryLog.Shown("a"));
        Assert.Equal(3.0, QueryLog.Shown(3L));
        Assert.Equal("AQI=", QueryLog.Shown(new byte[] { 1, 2 }));
        Assert.Equal("d1f9a1a2-0000-0000-0000-000000000000", QueryLog.Shown(Guid.Parse("d1f9a1a2-0000-0000-0000-000000000000")));
    }

    [Fact]
    public void WorkAfterATestThatFailsIsTheTestsFailure()
    {
        var host = KScriptHost.ForText(new StringWriter());
        var order = new List<int>();
        host.AfterTest.Add(() => order.Add(1));
        host.AfterTest.Add(() => throw new KScriptException("first", "Error"));
        host.AfterTest.Add(() => throw new KScriptException("last", "Error"));
        KScriptException failure = TestRunner.EndTest(host);
        Assert.Contains("last", failure.Message);
        Assert.Equal(new[] { 1 }, order);
        Assert.Empty(host.AfterTest);
    }

    [Fact]
    public void QueriesOutsideATestAreTheRunsSoFar()
    {
        string output = Run("""
            model Thing {
              name: string
            }
            $db->create_table(Thing)
            Thing->create(name: 'a')
            q = @queries()
            echo $q->count() > 1
            echo $q->last().sql->contains('INSERT')
            echo $q->last().bindings[0]
            """);
        Assert.Equal("true\ntrue\na", output.Trim());
    }
}
