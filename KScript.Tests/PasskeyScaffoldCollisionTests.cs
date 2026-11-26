namespace KScript.Tests;

public sealed class PasskeyScaffoldCollisionTests : AuthSiteFixture
{
    [Theory]
    [InlineData("Post(path: '/login', to: @old.login)")]
    [InlineData("Get(path: '/account', to: @old.account)")]
    [InlineData("Post(path: '/forgot-password', to: @old.reset)")]
    public void ForceKeepsExistingHandlersAndFiles(string route)
    {
        string original = "import KScript.Http\n\nRoutes {\n  " + route + "\n}\n";
        Write("routes/web.kscript", original);
        string config = Read("kscript.json");
        var result = Make(Dir, "auth", "--passkeys", "--force");
        Assert.Equal(2, result.Code);
        Assert.Contains("overlapping sign-in routes", result.Err);
        Assert.Equal(original, Read("routes/web.kscript"));
        Assert.Equal(config, Read("kscript.json"));
        Assert.False(File.Exists(Path.Combine(Dir, "controllers/accounts.kscript")));
    }

    [Fact]
    public void ChecksOtherMountedFilesBeforeWriting()
    {
        Write("kscript.json", """{"http":{"routes":[{"file":"admin.kscript","prefix":"/admin"},"auth-routes.kscript","other.kscript"]}}""");
        Write("other.kscript", "import KScript.Http\nRoutes {\n  Post(path: '/login', to: @old.login)\n}\n");
        var result = Make(Dir, "auth", "--passkeys", "--force");
        Assert.Equal(2, result.Code);
        Assert.Contains("other.kscript already has a /login route", result.Err);
        Assert.False(File.Exists(Path.Combine(Dir, "auth-routes.kscript")));
        Write("other.kscript", "import KScript.Http\nRoutes {\n}\n");
        Assert.Equal(0, Make(Dir, "auth", "--passkeys", "--force").Code);
        Assert.Contains("accounts.show_login", Read("auth-routes.kscript"));
        Assert.False(File.Exists(Path.Combine(Dir, "admin.kscript")));
    }

    [Fact]
    public void ForceKeepsAnInstalledApiHandlerWhenTheFlagIsOmitted()
    {
        Assert.Equal(0, Make(Dir, "auth", "--passkeys", "--api").Code);
        Assert.Equal(0, Make(Dir, "auth", "--passkeys", "--force").Code);
        Assert.Contains("fn me()", Read("controllers/accounts.kscript"));
        Assert.Equal(401, new Browser(App()).Get("/api/me").Status);
    }

    [Theory]
    [InlineData("kscript", "routes/web.kscript")]
    [InlineData("xml", "routes/web.ks.xml")]
    public void ForceRefreshesTheSamePasskeyKitWithoutDuplicateRoutes(string syntax, string file)
    {
        File.Delete(Path.Combine(Dir, "routes/web.kscript"));
        string[] options = ["auth", "--passkeys", "--api", "--syntax", syntax, "--force"];
        var first = Make(Dir, options);
        Assert.True(first.Code == 0, first.Err);
        string routes = Read(file);
        var again = Make(Dir, options);
        Assert.True(again.Code == 0, again.Err);
        Assert.Equal(routes, Read(file));
    }
}
