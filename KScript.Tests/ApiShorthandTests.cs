using KScript.Testing;

namespace KScript.Tests;

/// <summary>@api's shorthands: post:, put:, patch:, delete: true, bearer: and as: in place of options: { ... }.</summary>
public sealed class ApiShorthandTests : StorageFixture
{
    private static TempSite Site(string tests) => new(
        ("kscript.json", """{ "name": "api-shorthands" }"""),
        ("types/note.kscript", """
            type Note {
              method: string
              body: string
              auth: string = ''
            }
            """),
        ("routes/api.kscript", """
            import KScript.Http
            Routes {
              Post(path: /echo) { return { method: $request.method, body: $request.body, auth: $request.headers.authorization ?? '' } }
              Put(path: /echo) { return { method: $request.method, body: $request.body } }
              Patch(path: /echo) { return { method: $request.method, body: $request.body } }
              Delete(path: /echo) { return { method: $request.method, body: $request.body } }
            }
            """),
        ("api.test.kscript", "import KScript.Net\nimport \"types/note.kscript\"\n" + tests));

    private static IReadOnlyList<TestResult> Run(TempSite site)
    {
        var result = TestRunner.RunFile(Path.Combine(site.Root, "api.test.kscript"), new TestRunSettings());
        Assert.True(result.Error == null, result.Error);
        return result.Tests;
    }

    [Fact]
    public void Each_shorthand_sends_its_method_body_and_token()
    {
        using var site = Site("""
            test(name: 'post') {
              reply = @api('/api/echo', post: { title: 'Hi' }, bearer: 'tok')->json()
              expect(value: $reply.method, equals: 'POST')
              expect(value: $reply.body, equals: '{"title":"Hi"}')
              expect(value: $reply.auth, equals: 'Bearer tok')
            }
            test(name: 'put and patch') {
              expect(value: @api('/api/echo', put: { a: 1 })->json().method, equals: 'PUT')
              expect(value: @api('/api/echo', patch: { a: 1 })->json().method, equals: 'PATCH')
            }
            test(name: 'delete sends no body') {
              reply = @api('/api/echo', delete: true)->json()
              expect(value: $reply.method, equals: 'DELETE')
              expect(value: $reply.body, equals: '')
            }
            test(name: 'as reads the reply into a type') {
              note = @api('/api/echo', post: 'x', as: Note)
              expect(value: $note.method, equals: 'POST')
            }
            """);
        Assert.All(Run(site), test => Assert.True(test.Outcome == TestOutcome.Passed, test.Name + ": " + test.Message));
    }

    [Theory]
    [InlineData("@api('/api/echo', post: 1, put: 2)", "one of post:, put:, patch: and delete:")]
    [InlineData("@api('/api/echo', post: 1, options: { method: 'PUT' })", "was given method twice")]
    [InlineData("@api('/api/echo', bearer: 'a', options: { headers: { Authorization: 'b' } })", "an Authorization header and bearer:")]
    public void A_mixed_up_call_says_what_to_write(string call, string message)
    {
        using var site = Site($"test(name: 'mistake') {{\n  {call}\n}}\n");
        TestResult test = Assert.Single(Run(site));
        Assert.Equal(TestOutcome.Failed, test.Outcome);
        Assert.Contains(message, test.Message);
    }
}
