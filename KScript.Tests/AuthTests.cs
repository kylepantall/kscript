using System.Text.RegularExpressions;

using KScript.Auth;
using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;
using KScript.Scaffolding;

namespace KScript.Tests;

/// <summary>
/// A browser for <see cref="HttpApp.Handle"/>: it keeps the cookies the app sets and sends them back, and finds the
/// CSRF token in the forms it is given.
/// </summary>
internal sealed class Browser
{
    private readonly HttpApp app;

    public Browser(HttpApp app) => this.app = app;

    public Dictionary<string, string> Cookies { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public PageResult Last { get; private set; }

    public string Token => Regex.Match(Last?.Body ?? string.Empty, "name=\"_token\" value=\"([^\"]+)\"").Groups[1].Value;

    public PageResult Send(string method, string path, Dictionary<string, object> form = null, Dictionary<string, object> headers = null, string json = null, string scheme = null, string ip = null)
    {
        headers ??= new Dictionary<string, object>();
        if (json != null) headers["content-type"] = "application/json";
        var request = new Dictionary<string, object>
        {
            ["method"] = method,
            ["path"] = path,
            ["url"] = path,
            ["headers"] = headers,
            ["cookies"] = Cookies.ToDictionary(kv => kv.Key, kv => (object)kv.Value),
            ["form"] = form ?? new Dictionary<string, object>(),
            ["body"] = json ?? string.Empty,
        };
        if (scheme != null) request["scheme"] = scheme;
        if (ip != null) request["ip"] = ip;
        PageResult result = app.Handle(request);
        foreach (string cookie in result?.Cookies ?? new List<string>())
        {
            string pair = cookie.Split(';')[0];
            string name = pair.Split('=')[0], value = pair.Substring(name.Length + 1);
            if (value.Length == 0) Cookies.Remove(name);
            else Cookies[name] = value;
        }
        return Last = result;
    }

    public PageResult Get(string path, Dictionary<string, object> headers = null) => Send("GET", path, headers: headers);

    /// <summary>Opens the form's page, then posts the fields with its CSRF token.</summary>
    public PageResult Submit(string page, string action, params (string Name, object Value)[] fields)
    {
        Get(page);
        var form = fields.ToDictionary(f => f.Name, f => f.Value);
        form["_token"] = Token;
        return Send("POST", action, form);
    }
}

/// <summary>A site made by kscript make project --bare --web, with kscript make auth run in it.</summary>
public abstract class AuthSiteFixture : IDisposable
{
    protected readonly TempSite Parent = new TempSite();
    protected readonly string Dir;
    private readonly string previousTemplates = Environment.GetEnvironmentVariable("KSCRIPT_TEMPLATES");
    private readonly Func<DateTime> previousClock = AuthApp.Clock;

    protected AuthSiteFixture()
    {
        Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", Path.Combine(Parent.Root, "no-templates"));
        Dir = Path.Combine(Parent.Root, "app");
        Make(Parent.Root, "project", "app", "--bare", "--web", "--api");
        // Users and sessions go in the project, not the person's own app folders.
        string config = File.ReadAllText(Path.Combine(Dir, "kscript.json"));
        File.WriteAllText(Path.Combine(Dir, "kscript.json"), config.Replace("\"name\": \"app\",", "\"name\": \"app\",\n  \"http\": { \"auth\": { \"folder\": \"storage\" } },"));
    }

    public void Dispose()
    {
        AuthApp.Clock = previousClock;
        Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", previousTemplates);
        Parent.Dispose();
        GC.SuppressFinalize(this);
    }

    internal static (int Code, string Out, string Err) Make(string dir, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(args.Concat(new[] { "--dir", dir }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    internal HttpApp App(Func<string, string> environment = null)
    {
        var app = new HttpApp(Dir, ProjectConfig.Load(Path.Combine(Dir, "kscript.json")), new StringWriter());
        if (environment != null) app.Environment = environment;
        return app;
    }

    protected string Read(string file) => File.ReadAllText(Path.Combine(Dir, file)).Replace("\r\n", "\n");

    protected void Write(string file, string text)
    {
        string full = Path.Combine(Dir, file);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>Signs up Joan through the form; the browser is then signed in.</summary>
    internal static void SignUp(Browser b, string email = "joan@example.com", string password = "correct horse battery")
    {
        PageResult r = b.Submit("/register", "/register", ("name", "Joan"), ("email", email), ("password", password));
        Assert.True(r.Status == 302, r.Body);
    }
}

public sealed class AuthKitTests : AuthSiteFixture
{
    public AuthKitTests()
    {
        var made = Make(Dir, "auth", "--api");
        Assert.True(made.Code == 0, made.Err);
    }

    [Fact]
    public void MakeAuthWritesTheControllerViewsRoutesAndAPasswordRule()
    {
        string web = Read("routes/web.kscript");
        Assert.Contains("import KScript.Auth", web);
        Assert.Contains("import \"../controllers/accounts.kscript\" as accounts", web);
        Assert.Contains("Group(middleware: guest) {", web);
        Assert.Contains("Post(path: \"/logout\", to: accounts.sign_out, name: \"logout\")", web);
        Assert.Contains("Post(path: \"/tokens\", to: accounts.issue_token)", Read("routes/api.kscript"));
        Assert.Contains("fn issue_token(json: map)", Read("controllers/accounts.kscript"));
        Assert.Contains("import \"../requests/sign-up.kscript\"", Read("controllers/accounts.kscript"));
        Assert.Contains("type SignUp {", Read("requests/sign-up.kscript"));
        Assert.Contains("{! @csrf_field() }", Read("views/auth/login.html"));
        Assert.Contains("\"types\": { \"password\": { \"min\": 10 } }", Read("kscript.json"));
        Assert.True(File.Exists(Path.Combine(Dir, "_layout.html")));

        // Running it again would add the routes twice.
        var again = Make(Dir, "auth");
        Assert.Equal(2, again.Code);
        Assert.Contains("already has controllers/accounts.kscript", again.Err);
    }

    [Fact]
    public void SigningUpInAndOut()
    {
        var b = new Browser(App());
        Assert.Equal(302, b.Get("/account").Status);
        Assert.Equal("/login", b.Last.Headers["Location"]);

        PageResult weak = b.Submit("/register", "/register", ("name", "Joan"), ("email", "joan@example.com"), ("password", "short"));
        Assert.Equal(422, weak.Status);
        Assert.Contains("at least 10 characters", weak.Body);
        PageResult notEmail = b.Submit("/register", "/register", ("name", "Joan"), ("email", "nope"), ("password", "correct horse battery"));
        Assert.Contains("has no @", notEmail.Body);

        SignUp(b);
        Assert.Equal("/account", b.Last.Headers["Location"]);
        Assert.Contains("Signed in as <strong>Joan</strong>", b.Get("/account").Body);
        Assert.Equal("/", b.Get("/login").Headers["Location"]);

        // Someone else can't sign up with the same email, in any case.
        var other = new Browser(App());
        PageResult taken = other.Submit("/register", "/register", ("name", "J"), ("email", "JOAN@example.com"), ("password", "correct horse battery"));
        Assert.Equal(422, taken.Status);
        Assert.Contains("already signed up", taken.Body);

        Assert.Equal(419, b.Send("POST", "/logout").Status);
        b.Get("/account");
        PageResult loggedOut = b.Send("POST", "/logout", new Dictionary<string, object> { ["_token"] = b.Token });
        Assert.Equal(302, loggedOut.Status);
        Assert.Contains(loggedOut.Cookies, c => c.StartsWith("ks_session=;", StringComparison.Ordinal) && c.Contains("Max-Age=0"));
        Assert.Equal(302, b.Get("/account").Status);

        PageResult wrong = b.Submit("/login", "/login", ("email", "joan@example.com"), ("password", "wrong password"));
        Assert.Equal(422, wrong.Status);
        Assert.Contains("don&#39;t match an account", wrong.Body);

        PageResult right = b.Submit("/login", "/login", ("email", " Joan@Example.com "), ("password", "correct horse battery"), ("remember", "yes"));
        Assert.Equal(302, right.Status);
        Assert.Equal("/account", right.Headers["Location"]);
        Assert.Contains(right.Cookies, c => c.Contains("Max-Age=2592000") && c.Contains("HttpOnly") && c.Contains("SameSite=Lax"));
    }

    [Fact]
    public void TooManyWrongPasswordsMakeSignInWait()
    {
        DateTime now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        AuthApp.Clock = () => now;
        var b = new Browser(App());
        SignUp(b, email: "ada@example.com");
        b.Get("/account");
        b.Send("POST", "/logout", new Dictionary<string, object> { ["_token"] = b.Token });

        for (int i = 0; i < 5; i++) Assert.Equal(422, b.Submit("/login", "/login", ("email", "ada@example.com"), ("password", "nope " + i)).Status);
        PageResult wait = b.Submit("/login", "/login", ("email", "ada@example.com"), ("password", "correct horse battery"));
        Assert.Equal(429, wait.Status);
        Assert.Contains("Try again in 60 seconds", wait.Body);

        now = now.AddSeconds(59);
        Assert.Contains("Try again in 1 second.", b.Submit("/login", "/login", ("email", "ada@example.com"), ("password", "x")).Body);
        now = now.AddSeconds(2);
        Assert.Equal(302, b.Submit("/login", "/login", ("email", "ada@example.com"), ("password", "correct horse battery")).Status);
    }

    [Fact]
    public void ResettingAPassword()
    {
        // The controller's log statement writes the link to the server's error stream.
        var log = new StringWriter();
        TextWriter error = Console.Error;
        Console.SetError(log);
        try
        {
            Reset(App(), log);
        }
        finally
        {
            Console.SetError(error);
        }
    }

    private static void Reset(HttpApp app, StringWriter log)
    {
        var b = new Browser(app);
        SignUp(b);
        var elsewhere = new Browser(app);
        elsewhere.Submit("/login", "/login", ("email", "joan@example.com"), ("password", "correct horse battery"));
        b.Get("/account");
        b.Send("POST", "/logout", new Dictionary<string, object> { ["_token"] = b.Token });

        PageResult nobody = b.Submit("/forgot-password", "/forgot-password", ("email", "nobody@example.com"));
        Assert.Equal(302, nobody.Status);
        Assert.DoesNotContain("reset-password/", log.ToString());

        b.Submit("/forgot-password", "/forgot-password", ("email", "joan@example.com"));
        Assert.Contains("a link to reset its password is on its way", b.Get("/forgot-password").Body);
        string link = Regex.Match(log.ToString(), @"http://localhost/reset-password/([A-Za-z0-9_-]+)").Groups[1].Value;
        Assert.NotEmpty(link);

        PageResult weak = b.Submit("/reset-password/" + link, "/reset-password", ("token", link), ("password", "short"));
        Assert.Equal(422, weak.Status);
        Assert.Contains("The password must be at least 10 characters.", weak.Body);

        PageResult done = b.Submit("/reset-password/" + link, "/reset-password", ("token", link), ("password", "a brand new passphrase"));
        Assert.Equal("/account", done.Headers["Location"]);
        Assert.Contains("Your password has been changed.", b.Get("/account").Body);
        // The other device was signed out.
        Assert.Equal(302, elsewhere.Get("/account").Status);

        var stranger = new Browser(app);
        PageResult used = stranger.Submit("/reset-password/" + link, "/reset-password", ("token", link), ("password", "another passphrase"));
        Assert.Contains("expired or was already used", used.Body);
        Assert.Equal(302, stranger.Submit("/login", "/login", ("email", "joan@example.com"), ("password", "a brand new passphrase")).Status);
    }

    [Fact]
    public void ApiTokensFromTheAccountPageAndTheApi()
    {
        var b = new Browser(App());
        SignUp(b);
        b.Get("/account");
        PageResult made = b.Send("POST", "/account/tokens", new Dictionary<string, object> { ["_token"] = b.Token, ["name"] = "laptop" });
        string token = Regex.Match(made.Body, "ks_[A-Za-z0-9_-]+").Value;
        Assert.StartsWith("ks_", token);
        Assert.Contains("laptop, made", made.Body);

        var api = new Browser(App());
        var bearer = new Dictionary<string, object> { ["authorization"] = "Bearer " + token };
        PageResult me = api.Get("/api/me", bearer);
        Assert.Equal(200, me.Status);
        Assert.Contains("\"email\":\"joan@example.com\"", me.Body);
        Assert.DoesNotContain("password", me.Body);
        Assert.Empty(api.Cookies);

        Assert.Equal(401, api.Get("/api/me").Status);
        Assert.Equal("Bearer", api.Last.Headers["WWW-Authenticate"]);
        Assert.Equal(401, api.Get("/api/me", new Dictionary<string, object> { ["authorization"] = "Bearer ks_nope" }).Status);
        Assert.Contains("The token isn't right.", api.Last.Body);

        PageResult issued = api.Send("POST", "/api/tokens", json: "{\"email\":\"joan@example.com\",\"password\":\"correct horse battery\",\"device\":\"cli\"}");
        Assert.Equal(201, issued.Status);
        Assert.Matches("\\{\"token\":\"ks_[A-Za-z0-9_-]+\"\\}", issued.Body);
        Assert.Empty(api.Cookies);
        PageResult refused = api.Send("POST", "/api/tokens", json: "{\"email\":\"joan@example.com\",\"password\":\"nah\"}");
        Assert.Equal(401, refused.Status);
        Assert.Contains("don't match an account", refused.Body);

        string id = Regex.Match(b.Get("/account").Body, @"laptop, made[^<]*<form method=""post"" action=""/account/tokens/([a-f0-9]+)/delete""").Groups[1].Value;
        b.Send("POST", $"/account/tokens/{id}/delete", new Dictionary<string, object> { ["_token"] = b.Token });
        Assert.Contains("The token was revoked.", b.Get("/account").Body);
        Assert.Equal(401, api.Get("/api/me", bearer).Status);
    }

    [Fact]
    public void TheMadeFilesPassKScriptCheck()
    {
        var check = new StringWriter();
        KScript.LanguageServer.CheckCommand.Run(new[] { Dir }, check);
        string text = check.ToString();
        // The email and password types are new to the analyzer; everything else must be clean.
        foreach (string line in text.Split('\n').Where(l => l.Contains(": error ")))
            Assert.Matches("Unknown type '(email|password)'", line);
        Assert.DoesNotContain(": warning ", text);
    }
}

public sealed class AuthMakeTests : AuthSiteFixture
{
    [Fact]
    public void XmlProjectsGetXmlRoutesAndControllers()
    {
        File.Delete(Path.Combine(Dir, "routes/web.kscript"));
        var made = Make(Dir, "auth", "--syntax", "xml", "--dry-run");
        Assert.Equal(0, made.Code);
        Assert.Contains("Would: created routes/web.ks.xml", made.Out);
        Assert.False(File.Exists(Path.Combine(Dir, "controllers/accounts.ks.xml")));

        made = Make(Dir, "auth", "--syntax", "xml");
        Assert.True(made.Code == 0, made.Err);
        string routes = Read("routes/web.ks.xml");
        Assert.Contains("<import package=\"KScript.Auth\" />", routes);
        Assert.Contains("<Group middleware=\"guest\">", routes);
        Assert.Matches(@"<fn\s+name=""sign_up""\s+params=""form: map""", Read("controllers/accounts.ks.xml"));
        Assert.Contains("<import src=\"../requests/sign-up.ks.xml\" />", Read("controllers/accounts.ks.xml"));
        Assert.Contains("name=\"SignUp\"", Read("requests/sign-up.ks.xml"));
        Assert.DoesNotContain("accounts", Read("routes/api.kscript"));

        var b = new Browser(App());
        SignUp(b);
        Assert.Contains("Signed in as", b.Get("/account").Body);
    }

    [Fact]
    public void ItExplainsWhatStopsIt()
    {
        Assert.Contains("takes no name", Make(Dir, "auth", "users").Err);

        Write("routes/web.kscript", "KScript.Routes {\n  Get(path: \"/login\", view: login)\n}\n");
        var taken = Make(Dir, "auth");
        Assert.Equal(2, taken.Code);
        Assert.Contains("routes/web.kscript already has a /login route", taken.Err);
        Assert.Equal(0, Make(Dir, "auth", "--force").Code);

        // Running it again with --force replaces the files without adding the routes twice.
        var again = Make(Dir, "auth", "--force");
        Assert.Equal(0, again.Code);
        Assert.DoesNotContain("routes/web.kscript", again.Out);
        Assert.Single(Regex.Matches(Read("routes/web.kscript"), "accounts.show_login"));
    }

    [Fact]
    public void ItUsesTheRouteFilesKScriptJsonLists()
    {
        Write("kscript.json", "{ \"http\": { \"routes\": [\"site.kscript\", { \"file\": \"rest.kscript\", \"api\": true }] } }");
        Write("site.kscript", "KScript.Routes {\n}\n");
        var made = Make(Dir, "auth", "--api");
        Assert.True(made.Code == 0, made.Err);
        Assert.Contains("Added to site.kscript", made.Out);
        Assert.Contains("Created rest.kscript", made.Out);
        Assert.Contains("import \"controllers/accounts.kscript\" as accounts", Read("site.kscript"));
        Assert.Contains("\"types\"", Read("kscript.json"));
        Assert.DoesNotContain("kscript.json lists it", made.Out);
    }

    [Fact]
    public void RouteFilesOutsideTheDefaultsNeedListing()
    {
        Write("kscript.json", "{ \"paths\": { \"routes\": \"web\" } }");
        File.Delete(Path.Combine(Dir, "routes/web.kscript"));
        File.Delete(Path.Combine(Dir, "routes/api.kscript"));
        File.Delete(Path.Combine(Dir, "_layout.html"));
        var made = Make(Dir, "auth");
        Assert.True(made.Code == 0, made.Err);
        Assert.Contains("kscript serve only finds web/web.kscript once kscript.json lists it", made.Out);
        Assert.Contains("Created _layout.html", made.Out);
    }

    [Theory]
    [InlineData("{}", "{\n  // New passwords (@register, @reset_password) must be at least this long.\n  \"types\": { \"password\": { \"min\": 10 } }\n}")]
    [InlineData("{ \"name\": \"x\" }", "{\n  // New passwords (@register, @reset_password) must be at least this long.\n  \"types\": { \"password\": { \"min\": 10 } },\n\"name\": \"x\" }")]
    [InlineData("{\n  \"name\": \"x\"\n}", "{\n  // New passwords (@register, @reset_password) must be at least this long.\n  \"types\": { \"password\": { \"min\": 10 } },\n  \"name\": \"x\"\n}")]
    [InlineData("{\n  \"$schema\": \"s\",\n  \"name\": \"x\"\n}", "{\n  \"$schema\": \"s\",\n  // New passwords (@register, @reset_password) must be at least this long.\n  \"types\": { \"password\": { \"min\": 10 } },\n  \"name\": \"x\"\n}")]
    [InlineData("[]", null)]
    public void PasswordRuleGoesFirst(string json, string expected) => Assert.Equal(expected, AuthMaker.AddPasswordRule(json));

    [Fact]
    public void AProjectWithTypesKeepsItsOwnRules()
    {
        Write("kscript.json", "{ \"types\": { \"password\": { \"min\": 14 } } }");
        var made = Make(Dir, "auth");
        Assert.DoesNotContain("password rule", made.Out);
        Assert.Equal("{ \"types\": { \"password\": { \"min\": 14 } } }", Read("kscript.json"));
    }

    [Fact]
    public void AFolderWithoutKScriptJsonStillWorks()
    {
        string bare = Path.Combine(Parent.Root, "bare");
        Directory.CreateDirectory(bare);
        var made = Make(bare, "auth");
        Assert.True(made.Code == 0, made.Err);
        Assert.True(File.Exists(Path.Combine(bare, "routes/web.kscript")));
        Assert.DoesNotContain("password rule", made.Out);
    }
}
