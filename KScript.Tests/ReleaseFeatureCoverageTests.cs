using KScript.Projects;
using KScript.Scaffolding;
using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>Edge cases of kscript.json paths, kscript make route and bare-word arrays that the v0.5.0 pull requests left without a test.</summary>
public sealed class ReleaseFeatureCoverageTests
{
    private static (int Code, string Out, string Err) Make(string dir, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(args.Concat(new[] { "--dir", dir }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static ProjectConfig Parse(string json) => ProjectConfig.Parse(json, Path.Combine(Path.GetTempPath(), "proj", ProjectConfig.FileName));

    [Fact]
    public void Paths_set_folders_inside_the_project()
    {
        ProjectConfig c = Parse("""{ "paths": { "views": "resources/views/", "Controllers": "app\\controllers", "lib": "", "styles": 3, "routes": "../outside", "nope": "x" } }""");
        Assert.Equal("resources/views", c.Paths["views"]);
        Assert.Equal("app/controllers", c.Paths["controllers"]);
        Assert.Equal(".", c.Paths["lib"]);
        Assert.False(c.Paths.ContainsKey("styles"));
        Assert.False(c.Paths.ContainsKey("routes"));
        Assert.Equal("resources/views", c.Http.Views);
        string warnings = string.Join("\n", c.Warnings);
        Assert.Contains("paths: unknown folder 'nope'", warnings);
        Assert.Contains("paths.routes should be a folder inside the project, such as \"routes\", not \"../outside\".", warnings);

        // http.views wins over paths.views.
        Assert.Equal("pages", Parse("""{ "http": { "views": "pages" }, "paths": { "views": "resources/views" } }""").Http.Views);
        Assert.Contains("paths", string.Join("\n", Parse("""{ "paths": [] }""").Warnings));
    }

    [Fact]
    public void Put_routes_update_and_other_route_files_need_listing()
    {
        using var site = new TempSite(
            ("kscript.json", "{ \"paths\": { \"routes\": \"app/routes\" } }"));
        var (code, output, err) = Make(site.Root, "route", "users/{id}", "--method", "put");
        Assert.True(code == 0, err);
        Assert.Contains("Put(path: \"/users/{id}\", to: users.update)", output);
        Assert.Contains("kscript serve only finds app/routes/web.kscript once kscript.json lists it", output);
        Assert.True(File.Exists(Path.Combine(site.Root, "app", "routes", "web.kscript")));
    }

    [Fact]
    public void Controller_aliases_stay_unique()
    {
        using var site = new TempSite(
            ("kscript.json", "{}"),
            ("routes/web.kscript", "KScript.Routes {\n  import \"../lib/a.kscript\" as users\n  import \"../lib/b.kscript\" as admin_users\n  import \"../lib/c.kscript\" as admin_users2\n}\n"));
        var (code, _, err) = Make(site.Root, "route", "admin/users", "--controller", "admin/users");
        Assert.True(code == 0, err);
        string routes = File.ReadAllText(Path.Combine(site.Root, "routes", "web.kscript")).Replace("\r\n", "\n");
        Assert.Contains("import \"../controllers/admin/users.kscript\" as admin_users3", routes);
        Assert.Contains("Get(path: \"/admin/users\", to: admin_users3.index)", routes);
    }

    private const string Sizes = "<enum name=\"Size\" values=\"small, medium, large\" />"
        + "<fn name=\"plain\" params=\"items\" value=\"$items\" />"
        + "<fn name=\"odd\" params=\"sizes: Nope[]\" value=\"$sizes\" />";

    [Theory]
    [InlineData("@plain(1, extra: [large])", "@plain() has no parameter named 'extra'.")]
    [InlineData("@odd([large])", "Unknown name 'large' in @odd().")]
    [InlineData("@odd(1)", "doesn't fit Nope[]")]
    [InlineData("@max(1, 2, extra: [large])", "Unknown name 'large' in @max().")]
    public void Bare_words_only_fit_lists_of_known_enums(string expression, string expected)
    {
        Assert.Contains(expected, Fails(Sizes + $"<echo>{{ {expression} }}</echo>").Message);
    }

    [Fact]
    public void A_comment_after_an_operator_keeps_the_statement_going()
    {
        string path = Path.Combine(Path.GetTempPath(), "comment.kscript");
        Assert.Equal("3\n", Run("total = 1 + /* tax */\n  2\necho \"$total\"\n", path: path));
    }
}
