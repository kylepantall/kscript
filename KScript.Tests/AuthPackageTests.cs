using System.Text.RegularExpressions;

using KScript.Auth;
using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// KScript.Auth's commands, $session and @users() from scripts of a small site of its own, and the edges the sign-in
/// kit doesn't reach: settings, the record stores, the middleware's other modes and the cookie's flags.
/// </summary>
public sealed class AuthPackageTests : IDisposable
{
    private const string Controller = """
        import KScript.Http
        import KScript.Auth

        type Secret {
          password: password
        }

        fn t_reg(json) => @register($json.email, $json.password, name: $json.name ?? 'Joan')
        fn t_attempt(json) => { user: @attempt($json.email, $json.password, remember: $json.remember ?? false, session: $json.session ?? true) }
        fn t_me() => { user: @user(), signed: @is_signed_in(), id: @user()?.id }
        fn t_login_copy(json) => @login({ id: 'x1', email: 'own@example.com', name: $json.name }, remember: true)
        fn t_login_bad() => @login('')
        fn t_logout(json) {
          @logout(everywhere: $json.everywhere ?? false)
          return { ok: true }
        }
        fn t_put(json) {
          $session->set('a', $json.v)
          $session->flash('f', 'once')
          return { a: $session->get('a'), missing: $session->get('nope', 'dflt'), has: $session->has('a'), all: $session->all(), token: $session.token }
        }
        fn t_peek() => { a: $session->get('a'), f: $session->get('f', 'gone'), all: $session->all() }
        fn t_forget() => { gone: $session->remove('a'), again: $session->remove('a') }
        fn t_regen() {
          $session->regenerate()
          $session->regenerate()
          return { ok: true }
        }
        fn t_private_key() => $session->set('_user', 1)
        fn t_keep_password(json) => $session->set('p', [{ secret: Secret($json).password }])
        fn t_users(json) {
          u = @users()
          joan = $u->find_email('JOAN@example.com')
          return { count: $u.count, counted: $u->count(), found: $u->find($joan), all: $u->all(), ok: $u->verify('joan@example.com', $json.password), wrong: $u->verify('joan@example.com', 'not the password'), short: $u->verify('joan@example.com', ''), nobody: $u->verify('who@example.com', $json.password), none: $u->find('not-an-id') }
        }
        fn t_update(json) => @users()->update($json.id, nickname: 'Jo', name: null)
        fn t_update_email(json) => @users()->update($json.id, email: $json.email)
        fn t_update_kept(json) => @users()->update($json.id, password_hash: 'x')
        fn t_update_password(json) => @users()->update($json.id, secret: Secret($json).password)
        fn t_set_password(json) => @users()->set_password($json.id, $json.password)
        fn t_delete(json) => { first: @users()->delete($json.id), second: @users()->delete($json.id) }
        fn t_find_number() => @users()->find(42)
        fn t_find_bad() => @users()->find(true)
        fn t_token(json) => { token: @api_token($json.id, name: $json.name ?? 'cli', expires: $json.expires ?? null) }
        fn t_token_bare(json) => @api_token({ id: $json.id })
        fn t_token_nobody() => @api_token('nobody')
        fn t_tokens(json) => @api_tokens($json.id)
        fn t_my_tokens() => @api_tokens(null)
        fn t_revoke(json) => { done: @revoke_token($json.token, user: $json.user ?? null) }
        fn t_reset_token(json) => { token: @reset_token($json.email) }
        fn t_reset(json) => { user: @reset_password($json.token, $json.password) }
        fn t_text() => { session: '' + $session, users: '' + @users() }
        fn t_intended(json) => { to: @intended($json.to ?? null) }
        fn t_csrf() => { token: @csrf_token(), field: @csrf_field() }
        fn t_ok() => { ok: true }
        """;

    private const string Api = """
        import KScript.Http
        import KScript.Auth
        import "c.kscript" as c

        Routes {
          Post(path: "/reg", to: @c.t_reg)
          Post(path: "/attempt", to: @c.t_attempt)
          Get(path: "/me", to: @c.t_me)
          Post(path: "/login-copy", to: @c.t_login_copy)
          Post(path: "/login-bad", to: @c.t_login_bad)
          Post(path: "/logout", to: @c.t_logout)
          Post(path: "/put", to: @c.t_put)
          Get(path: "/peek", to: @c.t_peek)
          Post(path: "/forget", to: @c.t_forget)
          Post(path: "/regen", to: @c.t_regen)
          Post(path: "/private-key", to: @c.t_private_key)
          Post(path: "/keep-password", to: @c.t_keep_password)
          Post(path: "/users", to: @c.t_users)
          Post(path: "/update", to: @c.t_update)
          Post(path: "/update-email", to: @c.t_update_email)
          Post(path: "/update-kept", to: @c.t_update_kept)
          Post(path: "/update-password", to: @c.t_update_password)
          Post(path: "/set-password", to: @c.t_set_password)
          Post(path: "/delete", to: @c.t_delete)
          Get(path: "/find-number", to: @c.t_find_number)
          Get(path: "/find-bad", to: @c.t_find_bad)
          Post(path: "/token", to: @c.t_token)
          Post(path: "/token-bare", to: @c.t_token_bare)
          Post(path: "/token-nobody", to: @c.t_token_nobody)
          Post(path: "/tokens", to: @c.t_tokens)
          Get(path: "/my-tokens", to: @c.t_my_tokens, middleware: auth)
          Post(path: "/revoke", to: @c.t_revoke)
          Post(path: "/reset-token", to: @c.t_reset_token)
          Post(path: "/reset", to: @c.t_reset)
          Get(path: "/text", to: @c.t_text)
          Post(path: "/intended", to: @c.t_intended)
          Get(path: "/session-only", to: @c.t_ok, middleware: auth:session)
          Get(path: "/token-only", to: @c.t_ok, middleware: auth:token)
          Get(path: "/machines", to: @c.t_ok, middleware: auth:MACHINE_TOKEN)
          Get(path: "/unset", to: @c.t_ok, middleware: auth:UNSET_TOKEN)
          Get(path: "/guests", to: @c.t_ok, middleware: guest)
        }
        """;

    private const string Web = """
        import KScript.Http
        import KScript.Auth
        import "c.kscript" as c

        Routes {
          Get(path: "/form", to: @c.t_csrf)
          Post(path: "/post", to: @c.t_ok)
          Get(path: "/private", to: @c.t_ok, middleware: auth)
          Get(path: "/login", to: @c.t_ok, middleware: auth)
        }
        """;

    private readonly TempSite site;
    private readonly Func<DateTime> previousClock = AuthApp.Clock;
    private DateTime now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    public AuthPackageTests()
    {
        site = new TempSite(("c.kscript", Controller), ("api.kscript", Api), ("routes.kscript", Web));
        AuthApp.Clock = () => now;
    }

    public void Dispose()
    {
        AuthApp.Clock = previousClock;
        site.Dispose();
    }

    private HttpApp App(string auth = "", string types = "\"types\": { \"password\": { \"min\": 10 } },")
    {
        string json = $$"""{ "name": "cov", {{types}} "http": { "auth": { "folder": "storage"{{auth}} } } }""";
        File.WriteAllText(Path.Combine(site.Root, "kscript.json"), json);
        ProjectConfig project = ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        return new HttpApp(site.Root, project, new StringWriter()) { Environment = n => n == "MACHINE_TOKEN" ? "m4chine" : null };
    }

    private static PageResult Post(Browser b, string path, string json = "{}", Dictionary<string, object> headers = null) =>
        b.Send("POST", "/api" + path, json: json, headers: headers);

    private static Dictionary<string, object> Map(PageResult r)
    {
        Assert.True(r.Status == 200, r.Status + " " + r.Body);
        return (Dictionary<string, object>)Values.ParseJson(r.Body);
    }

    private static string Id(Browser b, string email = "joan@example.com", string password = "correct horse battery")
    {
        PageResult r = Post(b, "/reg", $$"""{ "email": "{{email}}", "password": "{{password}}" }""");
        return Values.ToStr(Map(r)["id"]);
    }

    [Fact]
    public void TheSessionKeepsValuesAndFlashesThemOnce()
    {
        var b = new Browser(App());
        Dictionary<string, object> put = Map(Post(b, "/put", """{ "v": { "n": 1 } }"""));
        Assert.Equal("dflt", put["missing"]);
        Assert.Equal(true, put["has"]);
        Assert.NotEmpty(Values.ToStr(put["token"]));
        Assert.True(b.Cookies.ContainsKey("ks_session"));

        Dictionary<string, object> peek = Map(b.Get("/api/peek"));
        Assert.Equal("once", peek["f"]);
        Assert.Equal("gone", Map(b.Get("/api/peek"))["f"]);

        Dictionary<string, object> forget = Map(Post(b, "/forget"));
        Assert.Equal(true, forget["gone"]);
        Assert.Equal(false, forget["again"]);
        Assert.Equal(true, Map(Post(b, "/regen"))["ok"]);

        // A guest with no cookie has nothing to forget, and an empty session.
        var guest = new Browser(App());
        Assert.Equal(false, Map(Post(guest, "/forget"))["gone"]);
        Assert.Empty((Dictionary<string, object>)Map(guest.Get("/api/peek"))["all"]);
        Assert.Contains("can't be a session key", Post(b, "/private-key").Body);
        Assert.Contains("A password can't be kept", Post(b, "/keep-password", """{ "password": "correct horse battery" }""").Body);
        Assert.Equal("{\"session\":\"a session\",\"users\":\"the users of cov\"}", b.Get("/api/text").Body);
        Assert.Contains("no session", new Browser(App()).Get("/api/text").Body);
    }

    [Fact]
    public void TheCookieHasSafeFlags()
    {
        var b = new Browser(App());
        Post(b, "/put", """{ "v": 1 }""");
        string cookie = b.Last.Cookies.Single();
        Assert.Contains("HttpOnly", cookie);
        Assert.Contains("SameSite=Lax", cookie);
        Assert.DoesNotContain("Secure", cookie);

        var https = new Browser(App(", \"cookie\": \"sid\", \"same_site\": \"none\""));
        https.Send("POST", "/api/put", json: """{ "v": 1 }""");
        Assert.Contains("Secure", https.Last.Cookies.Single());
        Assert.Contains("SameSite=None", https.Last.Cookies.Single());
        Assert.StartsWith("sid=", https.Last.Cookies.Single());

        var proxied = new Browser(App());
        proxied.Send("POST", "/api/put", json: """{ "v": 1 }""", headers: new Dictionary<string, object> { ["x-forwarded-proto"] = "https" });
        Assert.Contains("Secure", proxied.Last.Cookies.Single());
        var tls = new Browser(App());
        tls.Send("POST", "/api/put", json: """{ "v": 1 }""", scheme: "https");
        Assert.Contains("Secure", tls.Last.Cookies.Single());
        var forced = new Browser(App(", \"secure\": false, \"same_site\": \"Strict\""));
        forced.Send("POST", "/api/put", json: """{ "v": 1 }""", scheme: "https");
        Assert.DoesNotContain("Secure", forced.Last.Cookies.Single());
    }

    [Fact]
    public void SessionsEndAfterTheirLifetime()
    {
        HttpApp app = App(", \"lifetime\": \"1m\", \"sessions\": \"memory\"");
        var b = new Browser(app);
        Post(b, "/put", """{ "v": 7 }""");
        now = now.AddSeconds(30);
        Assert.Equal(7.0, Map(b.Get("/api/peek"))["a"]);
        now = now.AddSeconds(90);
        // The session ended: its cookie is taken away and the value is gone.
        Dictionary<string, object> peek = Map(b.Get("/api/peek"));
        Assert.Null(peek["a"]);
        Assert.False(b.Cookies.ContainsKey("ks_session"));

        // A cookie no session has, or one too long to be an id, is taken away too.
        b.Cookies["ks_session"] = "nope";
        b.Get("/api/peek");
        Assert.False(b.Cookies.ContainsKey("ks_session"));
        b.Cookies["ks_session"] = new string('a', 200);
        b.Get("/api/peek");
        Assert.False(b.Cookies.ContainsKey("ks_session"));
    }

    [Fact]
    public void UsersCanBeFoundCheckedChangedAndDeleted()
    {
        var b = new Browser(App());
        string id = Id(b);
        Dictionary<string, object> users = Map(Post(b, "/users", """{ "password": "correct horse battery" }"""));
        Assert.Equal(1.0, users["count"]);
        Assert.Equal(1.0, users["counted"]);
        Assert.Equal(id, ((Dictionary<string, object>)users["found"])["id"]);
        Assert.Single((List<object>)users["all"]);
        Assert.Equal("joan@example.com", ((Dictionary<string, object>)users["ok"])["email"]);
        Assert.Null(users["wrong"]);
        Assert.Null(users["short"]);
        Assert.Null(users["nobody"]);
        Assert.Null(users["none"]);
        Assert.DoesNotContain("password_hash", Post(b, "/users", """{ "password": "x" }""").Body);

        Dictionary<string, object> updated = Map(Post(b, "/update", $$"""{ "id": "{{id}}" }"""));
        Assert.Equal("Jo", updated["nickname"]);
        Assert.False(updated.ContainsKey("name"));
        Assert.Equal("New@example.com", Map(Post(b, "/update-email", $$"""{ "id": "{{id}}", "email": "New@Example.com" }"""))["email"]);
        string bad = Post(b, "/update-email", $$"""{ "id": "{{id}}", "email": "nope" }""").Body;
        Assert.True(bad.Contains("isn't an email address"), bad);
        Assert.Contains("kept by KScript.Auth", Post(b, "/update-kept", $$"""{ "id": "{{id}}" }""").Body);
        Assert.Contains("is a password", Post(b, "/update-password", $$"""{ "id": "{{id}}", "password": "correct horse battery" }""").Body);

        string other = Id(b, "other@example.com");
        PageResult taken = Post(b, "/update-email", $$"""{ "id": "{{other}}", "email": "new@example.com" }""");
        Assert.Equal(422, taken.Status);
        Assert.Contains("already signed up", taken.Body);

        Assert.Equal(id, Map(Post(b, "/set-password", $$"""{ "id": "{{id}}", "password": "a brand new password" }"""))["id"]);
        Assert.Equal(422, Post(b, "/set-password", $$"""{ "id": "{{id}}", "password": "short" }""").Status);
        Assert.Equal(404, Post(b, "/set-password", """{ "id": "nobody", "password": "a brand new password" }""").Status);
        // An id given as a number is looked up as text: there is no such user, so the route answers nothing.
        Assert.Equal(204, b.Get("/api/find-number").Status);
        Assert.Contains("needs a user", b.Get("/api/find-bad").Body);

        Dictionary<string, object> deleted = Map(Post(b, "/delete", $$"""{ "id": "{{id}}" }"""));
        Assert.Equal(true, deleted["first"]);
        Assert.Equal(false, deleted["second"]);
    }

    [Fact]
    public void OldPasswordHashesAreMadeAgainAtSignIn()
    {
        HttpApp app = App();
        var b = new Browser(app);
        string id = Id(b);
        AuthApp auth = app.Auth;
        Dictionary<string, object> record = auth.Users.Get(id);
        record["password_hash"] = BCrypt.Net.BCrypt.HashPassword("correct horse battery", 4);
        auth.Users.Put(id, record);

        Assert.NotNull(Map(Post(b, "/attempt", """{ "email": "joan@example.com", "password": "correct horse battery", "session": false }"""))["user"]);
        Assert.StartsWith("$argon2id$", Values.ToStr(auth.Users.Get(id)["password_hash"]));
    }

    [Fact]
    public void AProjectThatHashesWithBcryptKeepsItsHashesAtSignIn()
    {
        HttpApp app = App(types: "\"types\": { \"password\": { \"min\": 10, \"algorithm\": \"bcrypt\" } },");
        var b = new Browser(app);
        string id = Id(b);
        AuthApp auth = app.Auth;
        string registered = Values.ToStr(auth.Users.Get(id)["password_hash"]);
        Assert.StartsWith("$2", registered);

        // Signing in leaves a hash of the project's own algorithm as it is.
        Assert.NotNull(Map(Post(b, "/attempt", """{ "email": "joan@example.com", "password": "correct horse battery", "session": false }"""))["user"]);
        Assert.Equal(registered, Values.ToStr(auth.Users.Get(id)["password_hash"]));

        // A weaker one is made again, still with bcrypt.
        Dictionary<string, object> record = auth.Users.Get(id);
        record["password_hash"] = BCrypt.Net.BCrypt.HashPassword("correct horse battery", 4);
        auth.Users.Put(id, record);
        Assert.NotNull(Map(Post(b, "/attempt", """{ "email": "joan@example.com", "password": "correct horse battery", "session": false }"""))["user"]);
        Assert.StartsWith("$2a$12$", Values.ToStr(auth.Users.Get(id)["password_hash"]).Replace("$2b$", "$2a$"));
    }

    [Fact]
    public void SessionsAndResetTokensThatRanOutAreSweptNowAndThen()
    {
        HttpApp app = App();
        var b = new Browser(app);
        string id = Id(b);
        AuthApp auth = app.Auth;
        Assert.NotNull(Map(Post(b, "/attempt", """{ "email": "joan@example.com", "password": "correct horse battery" }"""))["user"]);
        Assert.NotNull(Map(Post(b, "/reset-token", """{ "email": "joan@example.com" }"""))["token"]);
        int sessions = auth.Sessions.All().Count();
        Assert.True(sessions > 0);
        Assert.Single(auth.Resets.All());

        // Nothing that is still good is removed.
        Assert.Equal(0, auth.Sweep());

        // A visitor who never comes back leaves a session behind; the writes of other visitors clear it away.
        now = now.AddDays(400);
        Assert.Equal(sessions, auth.Sessions.All().Count());
        for (int i = 0; i < AuthApp.SweepEvery; i++) auth.Wrote();
        Assert.Empty(auth.Sessions.All());
        Assert.Empty(auth.Resets.All());
        Assert.NotNull(auth.Users.Get(id));
    }

    [Fact]
    public void SignInOutAndUsersFromAStoreOfYourOwn()
    {
        var b = new Browser(App());
        Id(b);
        Dictionary<string, object> me = Map(b.Get("/api/me"));
        Assert.Equal(false, me["signed"]);

        Map(Post(b, "/attempt", """{ "email": "joan@example.com", "password": "correct horse battery", "remember": true }"""));
        Assert.Contains("Max-Age=", b.Last.Cookies.Single());
        Assert.Equal(true, Map(b.Get("/api/me"))["signed"]);
        Assert.Equal(200, b.Get("/api/session-only").Status);
        Assert.Equal(401, b.Get("/api/token-only").Status);
        Assert.Equal(403, b.Get("/api/guests").Status);

        // A second device, then signing out everywhere ends both.
        var phone = new Browser(App());
        Map(Post(phone, "/attempt", """{ "email": "joan@example.com", "password": "correct horse battery" }"""));
        Map(Post(b, "/logout", """{ "everywhere": true }"""));
        Assert.Equal(false, Map(phone.Get("/api/me"))["signed"]);

        // A user from somewhere else: the session keeps a copy.
        Dictionary<string, object> own = Map(Post(b, "/login-copy", """{ "name": "Own" }"""));
        Assert.Equal("x1", own["id"]);
        Assert.Equal("Own", ((Dictionary<string, object>)Map(b.Get("/api/me"))["user"])["name"]);
        Assert.Contains("needs a user", Post(b, "/login-bad").Body);
        Map(Post(b, "/logout"));
        Assert.Equal(false, Map(b.Get("/api/me"))["signed"]);
    }

    [Fact]
    public void DeletingAUserEndsTheirSession()
    {
        var b = new Browser(App());
        string id = Id(b);
        Map(Post(b, "/attempt", """{ "email": "joan@example.com", "password": "correct horse battery" }"""));
        var admin = new Browser(App());
        Map(Post(admin, "/delete", $$"""{ "id": "{{id}}" }"""));
        Assert.Equal(false, Map(b.Get("/api/me"))["signed"]);

        // A user removed from the store some other way: their next request signs them out.
        HttpApp app = App();
        var c = new Browser(app);
        id = Id(c, "carol@example.com");
        Map(Post(c, "/attempt", """{ "email": "carol@example.com", "password": "correct horse battery" }"""));
        app.Auth.Users.Delete(id);
        Assert.Equal(false, Map(c.Get("/api/me"))["signed"]);
        Assert.False(c.Cookies.ContainsKey("ks_session"));
    }

    [Fact]
    public void TheValuesDescribeThemselves()
    {
        AuthApp app = App().Auth;
        var users = new UserStore(app);
        var session = new SessionValue(null);
        Assert.Equal("the users of cov", users.ToString());
        Assert.Equal("no session", session.ToString());
        Assert.False(users.TryCallMethod(null, "find", out _));
        Assert.False(session.TryCallMethod(null, "get", out _));
        Assert.False(session.TryGetMember("a", out _));
        Assert.False(users.TryGetMember("nope", out _));
        UuidValue uuid = UuidValue.New(4, null, null);
        Assert.Equal(uuid.Text, UserStore.IdOf(uuid, "test"));
    }

    [Fact]
    public void ApiTokensSignRequestsIn()
    {
        var b = new Browser(App());
        string id = Id(b);
        string token = Values.ToStr(Map(Post(b, "/token", $$"""{ "id": "{{id}}", "expires": "1h" }"""))["token"]);
        Assert.StartsWith("ks_", token);
        Assert.StartsWith("ks_", Post(b, "/token-bare", $$"""{ "id": "{{id}}" }""").Body.Trim('"'));
        Assert.Contains("should be a length of time", Post(b, "/token", $$"""{ "id": "{{id}}", "expires": "soon" }""").Body);
        Assert.Equal(404, Post(b, "/token-nobody").Status);

        var bearer = new Dictionary<string, object> { ["authorization"] = "Bearer " + token };
        var api = new Browser(App());
        Assert.Equal(200, api.Get("/api/token-only", bearer).Status);
        Assert.Equal(401, api.Get("/api/session-only", bearer).Status);
        List<object> mine = (List<object>)Values.ParseJson(api.Get("/api/my-tokens", bearer).Body);
        Assert.Equal(2, mine.Count);
        Assert.Single(mine, t => ((Dictionary<string, object>)t)["expires_at"] != null);
        Assert.Equal(2, ((List<object>)Values.ParseJson(Post(b, "/tokens", $$"""{ "id": "{{id}}" }""").Body)).Count);

        // last_used is written at most once a minute.
        now = now.AddMinutes(2);
        api.Get("/api/token-only", bearer);
        Assert.Contains((List<object>)Values.ParseJson(api.Get("/api/my-tokens", bearer).Body), t => ((Dictionary<string, object>)t)["last_used_at"] != null);

        // Too old, too long, or someone else's: no.
        now = now.AddHours(2);
        Assert.Equal(401, api.Get("/api/token-only", bearer).Status);
        Assert.Equal(401, api.Get("/api/token-only", new Dictionary<string, object> { ["authorization"] = "Bearer " + new string('k', 300) }).Status);
        string other = Id(b, "other@example.com");
        Assert.Equal(false, Map(Post(b, "/revoke", $$"""{ "token": "{{token}}", "user": "{{other}}" }"""))["done"]);
        Assert.Equal(true, Map(Post(b, "/revoke", $$"""{ "token": "{{token}}", "user": "{{id}}" }"""))["done"]);
        Assert.Equal(false, Map(Post(b, "/revoke", $$"""{ "token": "{{token}}" }"""))["done"]);
        Assert.Equal(false, Map(Post(b, "/revoke", """{ "token": "" }"""))["done"]);
        Assert.Equal(false, Map(Post(b, "/revoke", """{ "token": "abc" }"""))["done"]);
    }

    [Fact]
    public void ResetTokensWorkOnceAndNotForLong()
    {
        var b = new Browser(App());
        string id = Id(b);
        Assert.Null(Map(Post(b, "/reset-token", """{ "email": "who@example.com" }"""))["token"]);
        string token = Values.ToStr(Map(Post(b, "/reset-token", """{ "email": "joan@example.com" }"""))["token"]);
        Assert.Null(Map(Post(b, "/reset", $$"""{ "token": "{{new string('t', 300)}}", "password": "a brand new password" }"""))["user"]);
        Assert.Null(Map(Post(b, "/reset", """{ "token": "", "password": "a brand new password" }"""))["user"]);
        now = now.AddHours(2);
        Assert.Null(Map(Post(b, "/reset", $$"""{ "token": "{{token}}", "password": "a brand new password" }"""))["user"]);

        // The user was deleted after the link was sent.
        token = Values.ToStr(Map(Post(b, "/reset-token", """{ "email": "joan@example.com" }"""))["token"]);
        HttpApp app = App();
        Dictionary<string, object> user = app.Auth.Users.Get(id);
        app.Auth.Users.Delete(id);
        Assert.Null(Map(Post(b, "/reset", $$"""{ "token": "{{token}}", "password": "a brand new password" }"""))["user"]);
        Assert.NotNull(user);
    }

    [Fact]
    public void SignInAttemptsAreLimited()
    {
        var b = new Browser(App(", \"attempts\": 2, \"lockout\": 60"));
        Id(b);
        const string Wrong = """{ "email": "joan@example.com", "password": "not the password" }""";
        Assert.Null(Map(Post(b, "/attempt", Wrong))["user"]);
        Assert.Null(Map(Post(b, "/attempt", Wrong))["user"]);
        PageResult locked = Post(b, "/attempt", Wrong);
        Assert.Equal(429, locked.Status);
        Assert.True(locked.Headers.ContainsKey("Retry-After"), locked.Body + string.Join(",", locked.Headers.Keys));
        now = now.AddMinutes(2);
        Assert.NotNull(Map(Post(b, "/attempt", """{ "email": "joan@example.com", "password": "correct horse battery" }"""))["user"]);
    }

    [Fact]
    public void TheAuthMiddlewareHasModes()
    {
        var b = new Browser(App());
        Assert.Equal(200, b.Get("/api/machines", new Dictionary<string, object> { ["authorization"] = "Bearer m4chine" }).Status);
        PageResult wrong = b.Get("/api/machines", new Dictionary<string, object> { ["authorization"] = "Bearer nope" });
        Assert.Equal(401, wrong.Status);
        Assert.Contains("isn't right", wrong.Body);
        Assert.Contains("needs an Authorization", b.Get("/api/machines").Body);
        Assert.Equal(500, b.Get("/api/unset").Status);
        Assert.Equal(200, b.Get("/api/guests").Status);

        // A browser is sent to sign in; the login page itself, and a bad token, get a 401.
        PageResult away = b.Get("/private");
        Assert.Equal(302, away.Status);
        Assert.Equal(401, b.Get("/login").Status);
        Assert.Equal(401, b.Get("/private", new Dictionary<string, object> { ["authorization"] = "Bearer nope" }).Status);
        Assert.Equal("/private", Map(Post(b, "/intended"))["to"]);
        Assert.Equal("/", Map(Post(b, "/intended"))["to"]);
        Assert.Equal("/home", Map(Post(b, "/intended", """{ "to": "/home" }"""))["to"]);
        Assert.Equal("/", Map(Post(b, "/intended", """{ "to": "//evil.example" }"""))["to"]);
    }

    [Fact]
    public void WebFormsNeedTheCsrfToken()
    {
        var b = new Browser(App());
        Dictionary<string, object> form = Map(b.Get("/form"));
        string token = Values.ToStr(form["token"]);
        Assert.Contains(token, Values.ToStr(form["field"]));
        Assert.Equal(419, b.Send("POST", "/post").Status);
        Assert.Equal(200, b.Send("POST", "/post", headers: new Dictionary<string, object> { ["x-csrf-token"] = token }).Status);
        Assert.Equal(200, b.Send("POST", "/post", headers: new Dictionary<string, object> { ["x-xsrf-token"] = token }).Status);
        Assert.Equal(200, b.Send("POST", "/post", json: $$"""{ "_token": "{{token}}" }""").Status);
        Assert.Equal(419, b.Send("POST", "/post", json: """{ "_token": "nope" }""").Status);
        // A bearer token without a cookie isn't a browser, so there is no form to forge.
        Assert.Equal(200, new Browser(App()).Send("POST", "/post", headers: new Dictionary<string, object> { ["authorization"] = "Bearer x" }).Status);

        var off = new Browser(App(", \"csrf\": false"));
        Assert.Equal(200, off.Send("POST", "/post").Status);
    }

    [Fact]
    public void AuthSettingsAreReadAndChecked()
    {
        ProjectConfig good = ProjectConfig.Parse("""
            { "http": { "auth": {
                "token_env": "KEY", "login": "/in", "home": "/dash", "sessions": "memory", "folder": "x", "cookie": "c",
                "secure": true, "same_site": "strict", "csrf": false, "attempts": 3,
                "lifetime": "15m", "remember": "1w", "lockout": 90, "reset_lifetime": "2h"
            } } }
            """, Path.Combine(site.Root, "kscript.json"));
        AuthSettings a = good.Http.Auth;
        Assert.Empty(good.Warnings);
        Assert.Equal(("KEY", "/in", "/dash", "memory", "x", "c"), (good.Http.AuthTokenEnv, a.Login, a.Home, a.Sessions, a.Folder, a.Cookie));
        Assert.Equal((true, "Strict", false, 3), (a.Secure, a.SameSite, a.Csrf, a.Attempts));
        Assert.Equal((900.0, 604800.0, 90.0, 7200.0), (a.Lifetime, a.Remember, a.Lockout, a.ResetLifetime));

        ProjectConfig bad = ProjectConfig.Parse("""
            { "http": { "auth": { "sessions": "redis", "same_site": "sometimes", "attempts": 0, "lifetime": "soon", "colour": 1 } } }
            """, Path.Combine(site.Root, "kscript.json"));
        string warnings = string.Join("\n", bad.Warnings);
        Assert.Contains("http.auth.sessions should be file or memory", warnings);
        Assert.Contains("http.auth.same_site should be Lax, Strict or None", warnings);
        Assert.Contains("http.auth.attempts should be a whole number above 0", warnings);
        Assert.Contains("http.auth.lifetime should be a number of seconds", warnings);
        Assert.Contains("unknown setting 'http.auth.colour'", warnings);
        Assert.Equal(7200.0, bad.Http.Auth.Lifetime);

        Assert.Equal(90.0, ProjectConfig.Seconds("90"));
        Assert.Equal(3600.0, ProjectConfig.Seconds("1H"));
        // The same lengths of time a script takes: milliseconds and joined units too.
        Assert.Equal(0.5, ProjectConfig.Seconds("500ms"));
        Assert.Equal(5400.0, ProjectConfig.Seconds("1h30m"));
        Assert.Null(ProjectConfig.Seconds("soon"));
        Assert.Null(ProjectConfig.Seconds("-5s"));
        Assert.Null(ProjectConfig.Seconds("0"));
        Assert.Null(ProjectConfig.Seconds(null));
    }

    [Fact]
    public void RecordStoresKeepJsonRecords()
    {
        foreach (RecordStore store in new RecordStore[] { new MemoryRecordStore(), new FileRecordStore(Path.Combine(site.Root, "records")) })
        {
            Assert.Null(store.Get("a1"));
            store.Put("a1", new Dictionary<string, object> { ["n"] = 1.0 });
            Assert.Equal(1.0, store.Get("a1")!["n"]);
            Assert.Single(store.All());
            Assert.True(store.Delete("a1"));
            Assert.False(store.Delete("a1"));
            Assert.Throws<KScriptException>(() => store.Get("../x"));
            Assert.Throws<KScriptException>(() => store.Get(null));
        }
        var files = new FileRecordStore(Path.Combine(site.Root, "broken"));
        Assert.Empty(files.All());
        Directory.CreateDirectory(files.Folder);
        File.WriteAllText(Path.Combine(files.Folder, "b1.json"), "{ not json");
        Assert.Null(files.Get("b1"));
        Assert.Empty(files.All());
    }

    [Fact]
    public void ScriptsOutsideARequestAreTold()
    {
        File.WriteAllText(Path.Combine(site.Root, "kscript.json"), "{ \"name\": \"cov\", \"http\": { \"auth\": { \"folder\": \"storage\" } } }");
        string path = Path.Combine(site.Root, "outside.kscript");
        string output = ScriptRunner.Run("""
            import KScript.Auth
            echo @user() ?? 'nobody'
            echo @is_signed_in()
            echo @users()->count()
            try {
              $session->get('a')
            } catch err {
              echo $err.message
            }
            try {
              @csrf_token()
            } catch err {
              echo $err.message
            }
            """, path: path);
        Assert.Contains("nobody", output);
        Assert.Contains("false", output);
        Assert.Contains("works while kscript serve handles a request", output);

        // A broken kscript.json falls back to the defaults.
        File.WriteAllText(Path.Combine(site.Root, "kscript.json"), "{ nope");
        Assert.NotNull(AuthApp.ForFolder(site.Root));
        Assert.Equal(Path.GetFullPath(site.Root), AuthApp.ForFolder(site.Root).Root);
    }

    [Fact]
    public void ErrorsKeepTheirStatusAndHeaders()
    {
        Assert.Equal(429, ErrorTypes.StatusOf(new KScriptException("slow down", "TooManyRequests")));
        var b = new Browser(App());
        PageResult r = Post(b, "/reg", """{ "email": "joan@example.com", "password": "short" }""");
        Assert.Equal(422, r.Status);
        Assert.Contains("problems", r.Body);
        Assert.Matches(new Regex("at least 10 characters"), r.Body);
    }
}
