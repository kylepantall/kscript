using KScript.Testing;

namespace KScript.Tests;

/// <summary>In-process @api() calls from tests: cookies, @acting_as() and repeated Set-Cookie headers (#183-#185).</summary>
public sealed class InProcessApiRequestTests : StorageFixture
{
    private static void AllPass(TempSite site, string file)
    {
        var result = TestRunner.RunFile(Path.Combine(site.Root, file), new TestRunSettings());
        Assert.Null(result.Error);
        Assert.NotEmpty(result.Tests);
        Assert.All(result.Tests, test => Assert.True(test.Outcome == TestOutcome.Passed, test.Name + ": " + test.Message));
    }

    [Fact]
    public void A_cookie_header_the_test_sends_reaches_the_route()
    {
        using var site = new TempSite(
            ("kscript.json", """{ "name": "cookies" }"""),
            ("routes/api.kscript", """
                import KScript.Http
                Routes {
                  Get(path: /guest) { return { guest: $cookies.sb_guest, theme: $request.cookies.theme } }
                }
                """),
            ("cookies.test.kscript", """
                import KScript.Net
                test(name: 'sends my cookies') {
                  reply = @api('/api/guest', options: { headers: { Cookie: 'sb_guest=0f8f; theme="dark"' } })->json()
                  expect($reply.guest, equals: '0f8f')
                  expect($reply.theme, equals: 'dark')
                }
                test(name: 'carries nothing over') {
                  expect(@api('/api/guest')->json().guest, equals: null)
                }
                """));
        AllPass(site, "cookies.test.kscript");
    }

    [Fact]
    public void Acting_as_applies_to_routes_the_test_calls()
    {
        using var site = new TempSite(
            ("kscript.json", """{ "name": "acting", "test": { "env": "test" } }"""),
            ("routes/api.kscript", """
                import KScript.Auth
                import KScript.Http
                Routes {
                  Get(path: /me) { return { email: @user()?.email, signed_in: @is_signed_in() } }
                  Get(path: /session, middleware: 'auth:session') { return @user().email }
                  Get(path: /any, middleware: auth) { return 'in' }
                }
                """),
            ("acting.test.kscript", """
                import KScript.Auth
                import KScript.Net
                test(name: 'acts as the user') {
                  user = @register('ada@example.test', 'correct-horse-battery-9')
                  expect(@api('/api/me')->json().signed_in, equals: false)
                  @acting_as($user)
                  expect(@api('/api/me')->json().email, equals: 'ada@example.test')
                  expect(@api('/api/session').text, equals: 'ada@example.test')
                  expect(@api('/api/any').status, equals: 200)
                  @acting_as(null)
                  expect(@api('/api/any').status, equals: 401)
                }
                test(name: 'ends with the test') {
                  expect(@api('/api/me')->json().signed_in, equals: false)
                }
                """));
        AllPass(site, "acting.test.kscript");
    }

    [Fact]
    public void Every_set_cookie_header_is_kept()
    {
        using var site = new TempSite(
            ("kscript.json", """{ "name": "set-cookies" }"""),
            ("routes/api.kscript", """
                import KScript.Http
                Routes {
                  Get(path: /two) {
                    return @response('ok')->with_header('Set-Cookie', 'a=1; Path=/')->with_header('Set-Cookie', 'b=2; Path=/; HttpOnly')
                  }
                  Get(path: /one) { return @response('ok')->with_header('Set-Cookie', 'a=1') }
                }
                """),
            ("cookies.test.kscript", """
                import KScript.Net
                test(name: 'keeps both') {
                  reply = @api('/api/two')
                  expect($reply.headers['Set-Cookie'], equals: ['a=1; Path=/', 'b=2; Path=/; HttpOnly'])
                  expect($reply->header('set-cookie'), equals: 'a=1; Path=/')
                  expect($reply.cookies, equals: { a: '1', b: '2' })
                }
                test(name: 'one stays text') {
                  reply = @api('/api/one')
                  expect($reply.headers['Set-Cookie'], equals: 'a=1')
                  expect($reply.cookies.a, equals: '1')
                }
                """));
        AllPass(site, "cookies.test.kscript");
    }
}
