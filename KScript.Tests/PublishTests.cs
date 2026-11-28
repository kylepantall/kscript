using KScript.Scaffolding;

namespace KScript.Tests;

/// <summary>kscript publish: copies of the models packages ship (KScript.Auth's User, Guest and Ownable).</summary>
public sealed class PublishTests
{
    private static (int Code, string Out, string Err) Publish(string dir, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = PublishCommand.Run(new[] { "--dir", dir }.Concat(args).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Lists_publishes_and_keeps_the_projects_copy()
    {
        using var site = new TempSite(("kscript.json", "{ \"name\": \"p\", \"paths\": { \"models\": \"app/models\" } }"));
        var list = Publish(site.Root);
        Assert.Equal(0, list.Code);
        Assert.Contains("Guest", list.Out);
        Assert.Contains("Ownable", list.Out);
        Assert.Contains("(mixin, KScript.Auth)", list.Out);
        Assert.DoesNotContain("Yours:", list.Out);

        var dry = Publish(site.Root, "user", "--dry-run");
        Assert.Equal(0, dry.Code);
        Assert.Contains("Would publish User to app/models/user.kscript", dry.Out);
        Assert.False(File.Exists(Path.Combine(site.Root, "app", "models", "user.kscript")));

        var made = Publish(site.Root, "User", "Ownable");
        Assert.True(made.Code == 0, made.Err);
        string user = File.ReadAllText(Path.Combine(site.Root, "app", "models", "user.kscript"));
        Assert.Contains("model User(auto_create, unique: [email], hidden: [password]) extends Timestamps {", user);
        Assert.Contains("Yours: app/models/user.kscript.", Publish(site.Root).Out);

        File.WriteAllText(Path.Combine(site.Root, "app", "models", "user.kscript"), "// mine\n");
        var again = Publish(site.Root, "User");
        Assert.Equal(ExitCodes.Usage, again.Code);
        Assert.Contains("already has its own User in app/models/user.kscript. Use --force", again.Err);
        Assert.Equal(0, Publish(site.Root, "User", "--force").Code);
        Assert.Contains("model User(", File.ReadAllText(Path.Combine(site.Root, "app", "models", "user.kscript")));
    }

    [Fact]
    public void Writes_xml_and_explains_mistakes()
    {
        using var site = new TempSite(("kscript.json", "{ \"name\": \"p\", \"make\": { \"syntax\": \"xml\" } }"));
        Assert.Equal(0, Publish(site.Root, "Guest").Code);
        Assert.Contains("<model name=\"Guest\"", File.ReadAllText(Path.Combine(site.Root, "models", "guest.ks.xml")));
        Assert.Equal(0, Publish(site.Root, "Ownable", "--syntax", "kscript").Code);
        Assert.True(File.Exists(Path.Combine(site.Root, "models", "ownable.kscript")));

        Assert.Contains("Did you mean User?", Publish(site.Root, "Usr").Err);
        Assert.Contains("Unknown option '--nope'", Publish(site.Root, "--nope").Err);
        Assert.Contains("--syntax needs kscript or xml", Publish(site.Root, "User", "--syntax").Err);
        Assert.Contains("--syntax should be kscript or xml", Publish(site.Root, "User", "--syntax=json").Err);
        var noDir = new StringWriter();
        Assert.Equal(ExitCodes.Usage, PublishCommand.Run(new[] { "User", "--dir" }, new StringWriter(), noDir));
        Assert.Contains("--dir needs a folder", noDir.ToString());

        using var outside = new TempSite();
        Assert.Contains("there is no kscript.json here", Publish(outside.Root, "User").Err);
    }
}
