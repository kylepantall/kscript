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
        // Timestamps and SoftDeletes only name a behaviour KScript.Data has built in, so there is nothing to change.
        Assert.DoesNotContain("Timestamps", list.Out);
        Assert.Contains("No package ships a model called 'SoftDeletes'", Publish(site.Root, "SoftDeletes").Err);

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

    [Fact]
    public void Only_replaceable_sources_have_project_copies()
    {
        string file = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "models", "timestamps.kscript"));
        Assert.Null(new KScript.Runtime.PackageSource("KScript.Data", "timestamps.kscript", "").ProjectCopy(new[] { file }));
        Assert.Equal(file, new KScript.Runtime.PackageSource("KScript.Auth", "timestamps.kscript", "", replaceable: true).ProjectCopy(new[] { file }));
    }

    [Fact]
    public void Queue_writes_the_tables_migration_and_turns_auto_create_off()
    {
        using var site = new TempSite(("kscript.json", "{\n  \"name\": \"p\",\n  \"queue\": { \"connections\": { \"jobs\": { \"driver\": \"database\", \"tables\": { \"jobs\": \"work\" } } } }\n}\n"));
        Assert.Contains("queue (migration, KScript.Queue)", Publish(site.Root).Out);
        Assert.Contains("There is no queue connection called nope", Publish(site.Root, "queue", "--connection", "nope").Err);
        Assert.Contains("uses the file driver, which keeps no tables", Publish(site.Root, "queue", "--connection", "file").Err);
        Assert.Contains("--connection is for kscript publish queue", Publish(site.Root, "User", "--connection", "jobs").Err);

        var made = Publish(site.Root, "queue", "--connection", "jobs");
        Assert.True(made.Code == 0, made.Err);
        string file = Assert.Single(Directory.GetFiles(Path.Combine(site.Root, "database", "migrations")));
        Assert.EndsWith("_create_queue_tables.kscript", file);
        string migration = File.ReadAllText(file);
        Assert.Contains("CreateTable(name: 'work') {", migration);
        Assert.Contains("CreateTable(name: 'kscript_failed_jobs') {", migration);
        Assert.Contains("CreateTable(name: 'kscript_job_locks') {", migration);
        var config = KScript.Projects.ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        Assert.Empty(config.Warnings);
        Assert.False(config.Queue.Connections["jobs"].AutoCreate);
        Assert.Equal("work", config.Queue.Connections["jobs"].JobsTable);
        Assert.Contains("Yours: database/migrations/", Publish(site.Root).Out);
        Assert.Contains("already has the queue tables' migration", Publish(site.Root, "queue", "--connection", "jobs").Err);

        // The built-in database connection gets an entry of its own in kscript.json.
        Assert.Equal(0, Publish(site.Root, "queue", "--force").Code);
        config = KScript.Projects.ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        Assert.False(config.Queue.Connections["database"].AutoCreate);
        Assert.Equal("database", config.Queue.Connections["database"].Driver);
    }
}
