using KScript.Auth;
using KScript.Runtime;
using KScript.Testing;

namespace KScript.Tests;

public sealed class TestStorageIsolationTests : StorageFixture
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_file_has_its_own_auth_and_app_storage(bool configuredAuthFolder)
    {
        string http = configuredAuthFolder ? """, "http": { "auth": { "folder": "auth-data" } }""" : "";
        File.WriteAllText(Path.Combine(Project, "kscript.json"), """{ "name": "isolation", "test": { "env": "test" }"""
            + http + " }");
        Directory.CreateDirectory(Path.Combine(Project, "routes"));
        File.WriteAllText(Path.Combine(Project, "routes", "api.kscript"), """
            import KScript.Auth
            import KScript.Http
            import KScript.Storage

            Routes {
              Get(path: '/state') {
                return { signed_in: @is_signed_in(), note: @storage()->read('note.txt') }
              }
            }
            """);
        string files = """
            import KScript.Auth
            import KScript.Net
            import KScript.Storage

            test(name: 'writes test data') {
              expect(value: @users()->count(), equals: 0)
              expect(value: @storage()->exists('note.txt'), equals: false)
              user = @register('tester@example.test', 'correct-horse-battery-9')
              token = @api_token($user, name: 'test')
              @storage()->write('note.txt', 'test')
              response = @api('/api/state', options: { headers: { authorization: "Bearer {$token}" } })->json()
              expect(value: $response.signed_in, equals: true)
              expect(value: $response.note, equals: 'test')
              expect(value: @api_tokens($user)->count(), equals: 1)
            }
            """;
        string first = Path.Combine(Project, "first.test.kscript");
        string second = Path.Combine(Project, "second.test.kscript");
        File.WriteAllText(first, files);
        File.WriteAllText(second, files + """

            test(name: 'fails after writing') {
              expect(value: true, equals: false)
            }
            """);

        AuthApp app = AuthApp.ForFolder(Project);
        string realData = AppFolders.Folder("isolation", "data");
        var folder = new List<string>();
        foreach (string path in new[] { first, second, first })
        {
            TestFileResult result = TestRunner.RunFile(path, new TestRunSettings(), test =>
            {
                if (test.Outcome != TestOutcome.Passed) return;
                string current = AppFolders.Folder("isolation", "data");
                Assert.NotEqual(realData, current);
                Assert.True(File.Exists(Path.Combine(current, "note.txt")));
                folder.Add(current);
            });

            Assert.Null(result.Error);
            Assert.True(result.Tests[0].Outcome == TestOutcome.Passed, result.Tests[0].Message);
            Assert.Equal(path == second, result.Failed);
        }

        Assert.Equal(3, folder.Distinct().Count());
        Assert.All(folder, path => Assert.False(Directory.Exists(path)));
        Assert.Equal(0, app.Users.Count());
        Assert.Equal(0, app.Tokens.Count());
        Assert.False(Directory.Exists(realData));
        Assert.False(Directory.Exists(Path.Combine(Project, "auth-data")));
    }

    [Fact]
    public void Model_backed_auth_uses_the_test_database()
    {
        File.WriteAllText(Path.Combine(Project, "kscript.json"), """
            { "name": "isolation", "http": { "auth": { "users": "User" } } }
            """);
        Directory.CreateDirectory(Path.Combine(Project, "models"));
        File.WriteAllText(Path.Combine(Project, "models", "user.kscript"), """
            import KScript.Data
            model User(timestamps, unique: [email], hidden: [password]) {
              email: email
              password: password
            }
            """);
        string path = Path.Combine(Project, "users.test.kscript");
        File.WriteAllText(path, """
            import KScript.Auth
            test(name: 'registers a model user') {
              expect(value: @users()->count(), equals: 0)
              @register('tester@example.test', 'correct-horse-battery-9')
              expect(value: @users()->count(), equals: 1)
            }
            """);

        foreach (int run in new[] { 1, 2 })
        {
            TestFileResult result = TestRunner.RunFile(path, new TestRunSettings());
            Assert.Null(result.Error);
            TestResult test = Assert.Single(result.Tests);
            Assert.True(test.Outcome == TestOutcome.Passed, $"Run {run}: {test.Message}");
        }
        Assert.Empty(Directory.EnumerateFiles(Project, "*.sqlite", SearchOption.AllDirectories));
    }
}
