using KScript.Projects;

namespace KScript.Tests;

public sealed class PasskeyScaffoldTests : AuthSiteFixture
{
    [Theory]
    [InlineData("kscript")]
    [InlineData("xml")]
    public void PasswordlessKitUsesProjectPortAndPreservesSettings(string syntax)
    {
        Write("kscript.json", """
            {
              // café and existing settings must survive generation
              "serve": { "port": 4820 }, "http": { "auth": { "folder": "storage", }, },
            }
            """);
        var made = Make(Dir, "auth", "--passkeys", "--api", "--syntax", syntax);
        Assert.True(made.Code == 0, made.Err);
        var auth = App().Settings.Auth;
        Assert.Equal("http://localhost:4820", auth.PasskeyOrigin);
        Assert.True(auth.PasskeySignup);
        Assert.Contains("// café", Read("kscript.json"));
        Assert.DoesNotContain("password", Read("kscript.json"));
        string controller = Read("controllers/accounts." + (syntax == "xml" ? "ks.xml" : "kscript"));
        Assert.DoesNotContain("@attempt", controller);
        Assert.DoesNotContain("reset_password", controller);
        Assert.False(File.Exists(Path.Combine(Dir, "views/auth/forgot-password.html")));
        var browser = new Browser(App());
        Assert.Contains("data-ks-passkey=\"signup\"", browser.Get("/register").Body);
        Assert.Contains("data-ks-passkey=\"login\"", browser.Get("/login").Body);
        Assert.DoesNotContain("password", browser.Last.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(405, browser.Send("POST", "/login").Status);
        Assert.Equal(401, browser.Get("/api/me").Status);
        Assert.Equal(404, browser.Send("POST", "/api/tokens").Status);
    }

    [Fact]
    public void OriginAndRpCanBeSetWithoutWritingOnAnInvalidConfiguration()
    {
        string before = Read("kscript.json");
        Assert.Contains("--rp-id", Make(Dir, "auth", "--help").Out);
        Assert.NotEqual(0, Make(Dir, "auth", "--passkeys", "--origin", "http://public.example.com").Code);
        Assert.Equal(before, Read("kscript.json"));
        Assert.False(File.Exists(Path.Combine(Dir, "views/auth/register.html")));
        string[] options = { "auth", "--passkeys", "--origin", "https://login.example.com", "--rp-id", "example.com" };
        Assert.Equal(0, Make(Dir, options.Append("--dry-run").ToArray()).Code);
        Assert.Equal(before, Read("kscript.json"));
        Assert.Equal(0, Make(Dir, options).Code);
        AuthSettings settings = App().Settings.Auth;
        Assert.Equal("https://login.example.com", settings.PasskeyOrigin);
        Assert.Equal("example.com", settings.PasskeyRpId);
        Assert.Equal(0, Make(Dir, options.Append("--force").ToArray()).Code);
        Assert.NotEqual(0, Make(Dir, "auth", "--force").Code);
    }
}
