using KScript.Http;
using KScript.Runtime;
using KScript.Runtime.Dependencies;

namespace KScript.Tests;

/// <summary>A server parses each script once, and again only when it changes.</summary>
public class ScriptCacheTests
{
    private static string Run(ScriptCache cache, string path)
    {
        var output = new StringWriter { NewLine = "\n" };
        var interpreter = new Interpreter(new KScriptHost(output, output), options: new InterpreterOptions { Scripts = cache });
        interpreter.Run(interpreter.Load(path));
        return output.ToString().Trim();
    }

    [Fact]
    public void Interpreters_share_the_parse_until_the_text_changes()
    {
        using var site = new TempSite(
            ("main.kscript", "import \"lib.kscript\"\n\necho @greet('Ann')\n"),
            ("lib.kscript", "fn greet(name) => \"Hello { $name }\"\n"));
        var cache = new ScriptCache();
        string main = Path.Combine(site.Root, "main.kscript");

        Assert.Equal("Hello Ann", Run(cache, main));
        Assert.Equal(2, cache.Parses);
        Assert.Equal("Hello Ann", Run(cache, main));
        Assert.Equal(2, cache.Parses);
        Assert.Equal(2, cache.Hits);

        // Only the changed script is parsed again; the script importing it uses its new version.
        site.Write("lib.kscript", "fn greet(name) => \"Hi { $name }\"\n");
        Assert.Equal("Hi Ann", Run(cache, main));
        Assert.Equal(3, cache.Parses);
        Assert.Equal(3, cache.Hits);

        cache.Clear();
        Assert.Equal("Hi Ann", Run(cache, main));
        Assert.Equal(5, cache.Parses);
    }

    [Fact]
    public void A_script_that_does_not_load_is_not_kept()
    {
        using var site = new TempSite(("main.kscript", "echo (\n"));
        var cache = new ScriptCache();
        string main = Path.Combine(site.Root, "main.kscript");
        Assert.Throws<KScriptException>(() => Run(cache, main));
        Assert.Throws<KScriptException>(() => Run(cache, main));
        Assert.Equal(0, cache.Hits);

        site.Write("main.kscript", "echo 'fixed'\n");
        Assert.Equal("fixed", Run(cache, main));
        Assert.Equal("fixed", Run(cache, main));
        Assert.Equal(1, cache.Hits);

        // Past the limit the cache starts again.
        var small = new ScriptCache { Limit = 1 };
        site.Write("other.kscript", "echo 'other'\n");
        Run(small, main);
        Run(small, Path.Combine(site.Root, "other.kscript"));
        Run(small, main);
        Assert.Equal(0, small.Hits);
    }

    [Fact]
    public void The_route_table_is_built_again_when_a_script_the_routes_use_changes()
    {
        using var site = new TempSite(
            ("routes.kscript", "import KScript.Http\nimport \"lib/paths.kscript\"\n\nRoutes {\n  Get(path: /hello) {\n    return @greeting()\n  }\n}\n"),
            ("lib/paths.kscript", "fn greeting() => 'hello'\n"));
        var app = new HttpApp(site.Root, null);
        RouteTable first = app.Table();
        Assert.Same(first, app.Table());

        site.Write("lib/paths.kscript", "fn greeting() => 'hi'\n");
        File.SetLastWriteTimeUtc(Path.Combine(site.Root, "lib", "paths.kscript"), DateTime.UtcNow.AddMinutes(1));
        Assert.NotSame(first, app.Table());

        // Building the table inside a test run adds what it reached to the test's own record.
        var outer = new DependencySet();
        DependencyTrace.Current = outer;
        try
        {
            File.SetLastWriteTimeUtc(Path.Combine(site.Root, "lib", "paths.kscript"), DateTime.UtcNow.AddMinutes(2));
            app.Table();
        }
        finally
        {
            DependencyTrace.Current = null;
        }
        Assert.Contains(outer.Files.Keys, k => k.EndsWith("paths.kscript", StringComparison.Ordinal));
    }
}
