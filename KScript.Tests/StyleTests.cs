using System.Globalization;

using KScript.Pages;
using KScript.Runtime;
using KScript.Style;

namespace KScript.Tests;

public sealed class StyleTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-style-" + Guid.NewGuid().ToString("N"));

    public StyleTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string FileOf(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
        return path;
    }

    private string Css(string source, Dictionary<string, object> vars = null) =>
        Stylesheets.CompileText(source, Path.Combine(dir, "test.ks.css"), vars);

    private static string Eval(string expression) => ScriptRunner.Eval(expression, "KScript.Style");

    [Fact]
    public void Plain_css_passes_through_untouched()
    {
        const string css = ".a > .b { color: red; }\n@media (min-width: 1px) { .c { margin: 0 } }\n/* { not KScript } */\n.d::after { content: '{$no}'; }\n";
        Assert.Equal(css.Replace("'{$no}'", "''"), Css(css.Replace("'{$no}'", "''")));
        Assert.Equal(".x { a: b }", Css(".x { a: b }"));
        Assert.Equal("a{}b{ }c{@media}", Css("a{}b{ }c{@media}"));
    }

    [Fact]
    public void Themes_come_from_json_with_loops_and_helpers()
    {
        FileOf("theme.json", "{ \"colors\": { \"primary\": \"#3355ff\", \"text\": { \"muted\": \"#666\" } }, \"space\": { \"sm\": 4, \"lg\": 16 } }");
        string css = Css(
            "<let name=\"theme\" value=\"@read_json('theme.json')\" />\n" +
            ":root {\n  { @css_vars($theme.colors, 'color') }\n}\n" +
            "<for each=\"name, px\" in=\"$theme.space\">\n" +
            ".p-{$name} { padding: {@rem($px)}; }\n" +
            "</for>\n" +
            "    <if test=\"$vars.dark ?? false\">\n" +
            ".dark { color: {!$theme.colors.primary}; }\n" +
            "    </if>\n", new Dictionary<string, object> { ["dark"] = true });
        Assert.Equal(":root {\n  --color-primary: #3355ff;\n  --color-text-muted: #666;\n}\n.p-sm { padding: 0.25rem; }\n.p-lg { padding: 1rem; }\n.dark { color: #3355ff; }\n", css);
    }

    [Fact]
    public void Sass_and_scss_keep_their_own_syntax()
    {
        Assert.Equal("$brand: #3355ff;\n.a { color: $brand; &:hover { color: darken($brand, 10%); } @include x; }\n",
            Css("<let name=\"b\" value=\"'#3355ff'\" />\n$brand: {$b};\n.a { color: $brand; &:hover { color: darken($brand, 10%); } @include x; }\n"));
        Assert.Equal("@if $a < $b\n  .x\n    color: red\n", Css("@if $a < $b\n  .x\n    color: red\n"));
        Assert.Equal("a { b: #{$c}; }", Css("a { b: #{$c}; }"));
    }

    [Fact]
    public void Inline_tags_and_comments()
    {
        Assert.Equal(".a { x: 1 }  .b {}", Css("<let name=\"n\" value=\"1\" />.a { x: {$n} } <!-- a note --> .b {}"));
        Assert.Equal("a{} <b", Css("a{} <b"));
        Assert.Equal("a <!-- open", Css("a <!-- open"));
        Assert.Equal("a x 1\n b", Css("a <echo>x {$y ?? 1}</echo> b"));
    }

    [Fact]
    public void Errors_point_at_the_stylesheet_line()
    {
        var ex = Assert.Throws<KScriptException>(() => Css(".a {}\n.b { color: {$missing}; }\n"));
        Assert.Equal(2, ex.Line);
        Assert.Contains("isn't closed", Assert.Throws<KScriptException>(() => Css(".a {}\n.b { color: {@rem(4)\n")).Message);
    }

    [Fact]
    public void Screen_width_helpers()
    {
        Assert.Equal("(min-width: 768px)", Eval("@up('md')"));
        Assert.Equal("(min-width: 900px)", Eval("@up(900)"));
        Assert.Equal("(min-width: 900px)", Eval("@up('900px')"));
        Assert.Equal("(min-width: 480px)", Eval("@up('30em')"));
        Assert.Equal("(max-width: 1023.98px)", Eval("@down('lg')"));
        Assert.Equal("(min-width: 640px) and (max-width: 1279.98px)", Eval("@between('sm', 'xl')"));
        Assert.Equal("(min-width: 800px)", ScriptRunner.RunBody("<let name=\"breakpoints\" value=\"{ md: 800 }\" /><echo>{ @up('md') }</echo>", "", "KScript.Style").TrimEnd());
        Assert.Contains("There is no breakpoint 'huge'. Breakpoints: sm, md, lg, xl, xxl", ScriptRunner.Fails("<echo>{ @up('huge') }</echo>", "KScript.Style").Message);
    }

    [Fact]
    public void Size_helpers()
    {
        Assert.Equal("1.5rem", Eval("@rem(24)"));
        Assert.Equal("2rem", Eval("@rem(20, 10)"));
        Assert.Equal("clamp(1.25rem, 0.5rem + 1.875vw, 2rem)", Eval("@fluid(20, 32)"));
        Assert.Equal("clamp(1rem, 1rem + 0vw, 1rem)", Eval("@fluid(16, 16, 'sm', 'md')"));
        Assert.Equal("clamp(1rem, 1.625rem - 0.625vw, 1.5rem)", Eval("@fluid(24, 16, 320, 1600)"));
        Assert.Contains("second screen width to be larger", ScriptRunner.Fails("<echo>{ @fluid(1, 2, 900, 600) }</echo>", "KScript.Style").Message);
    }

    [Fact]
    public void Colour_helpers()
    {
        Assert.Equal("#002bff", Eval("@darken('#3355ff', '10%')"));
        Assert.Equal("#ffffff", Eval("@lighten('#3355ff', 1)"));
        Assert.Equal("#808080", Eval("@mix('black', 'white')"));
        Assert.Equal("#bfbfbf", Eval("@mix('#fff', '#000', 0.75)"));
        Assert.Equal("rgba(51, 85, 255, 0.5)", Eval("@alpha('#3355ff', '50%')"));
        Assert.Equal("rgba(255, 0, 0, 0.25)", Eval("@alpha('rgb(255, 0, 0)', 0.25)"));
        Assert.Equal("#ffffff", Eval("@contrast('#3355ff')"));
        Assert.Equal("#111", Eval("@contrast('#ffff00', '#111', '#eee')"));
        Assert.Equal("#ff0000", Eval("@lighten('hsl(0, 100%, 50%)', 0)"));
        Assert.Equal("#00ff00", Eval("@lighten('hsla(120deg 100% 50% / 1)', 0)"));
        Assert.Equal("#808080", Eval("@lighten('hsl(0, 0%, 50%)', 0)"));
        Assert.Equal("#11223344", Colors.Parse("#1234", "x").ToHex());
        Assert.Equal("rgba(0, 0, 0, 0)", Eval("@alpha('transparent', 0)"));
        Assert.Equal("#ff8000", Eval("@lighten('rgba(100%, 50%, 0%, 1)', 0)"));
        foreach (string bad in new[] { "nope", "#12", "#gggggg", "rgb(1, 2)", "rgb(a, b, c)" })
        {
            Assert.Contains("isn't a colour", Assert.Throws<KScriptException>(() => Colors.Parse(bad, "x")).Message);
        }
        var (h, s, l) = new Rgba(0, 0, 255, 1).ToHsl();
        Assert.Equal((240.0, 1.0, 0.5), (h, s, l));
        Assert.Equal(120.0, new Rgba(0, 255, 0, 1).ToHsl().H);
        Assert.Equal(300.0, Math.Round(new Rgba(255, 0, 255, 1).ToHsl().H));
        Assert.Equal("#bf40bf", Rgba.FromHsl(300, 0.5, 0.5, 1).ToHex());
        Assert.Equal("#40bf6a", Rgba.FromHsl(-220, 0.5, 0.5, 1).ToHex());
    }

    [Fact]
    public void Custom_property_helpers()
    {
        Assert.Equal("var(--brand)", Eval("@css_var('brand')"));
        Assert.Equal("var(--gap, 4px 8px)", Eval("@css_var('gap', ['4px', '8px'])"));
        Assert.Equal("--a: 1;\n--b-c: ;", Eval("@css_vars({ a: 1, b: { c: null } })"));
    }

    [Fact]
    public void Stylesheet_element_and_command_from_scripts()
    {
        FileOf("theme.ks.css", ".a { color: {$vars.c ?? 'red'}; }");
        string script = FileOf("build.ks.xml", "<kscript><import package=\"KScript.Style\" /><stylesheet src=\"theme.ks.css\" /><stylesheet src=\"theme.ks.css\" to=\"out/blue.css\" vars=\"{ c: 'blue' }\" /><echo>{ @stylesheet('theme.ks.css', { c: 'green' }) }</echo></kscript>");
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output)).RunFile(script);
        Assert.Equal(".a { color: green; }\n", output.ToString());
        Assert.Equal(".a { color: red; }", File.ReadAllText(Path.Combine(dir, "theme.css")));
        Assert.Equal(".a { color: blue; }", File.ReadAllText(Path.Combine(dir, "out", "blue.css")));
        Assert.Contains("vars should be a map", ScriptRunner.Fails("<echo>{ @stylesheet('x.ks.css', 1) }</echo>", "KScript.Style").Message);
        Assert.Contains("isn't a stylesheet", Assert.Throws<KScriptException>(() => Stylesheets.OutputPath("a.css")).Message);
        Assert.Equal("a.scss", Stylesheets.OutputPath("a.ks.scss"));
        Assert.Equal("a.sass", Stylesheets.OutputPath("a.KS.SASS"));
        Assert.Equal("{}", Stylesheets.CompileText("{ @to_json($vars) }", null));
        Assert.Equal("{\"a\":{\"b\":1}}", Stylesheets.CompileText("{ @to_json({ a: { b: 1 } }) }", null));
        Assert.NotEmpty(new StylePackage().Description);
    }

    [Fact]
    public void Stylesheets_in_read_only_hosts_cannot_write()
    {
        FileOf("t.ks.css", "a{}");
        string script = FileOf("b.ks.xml", "<kscript><import package=\"KScript.Style\" /><stylesheet src=\"t.ks.css\" /></kscript>");
        var host = KScriptHost.ForText(new StringWriter());
        host.CanWriteFiles = false;
        host.FileRoot = dir;
        Assert.Contains("can't change files", Assert.Throws<KScriptException>(() => new Interpreter(host).RunFile(script)).Message);
        var output = new StringWriter();
        var reader = KScriptHost.ForText(output);
        reader.FileRoot = dir;
        new Interpreter(reader).RunText("<kscript><import package=\"KScript.Style\" /><echo>{ @stylesheet('t.ks.css') }</echo></kscript>", Path.Combine(dir, "x.ks.xml"));
        Assert.Equal("a{}", output.ToString().Trim());
    }

    [Fact]
    public void Kscript_css_compiles_files_and_folders()
    {
        FileOf("site/a.ks.css", ".a { w: {@rem(32)}; }");
        FileOf("site/sub/b.ks.scss", "$x: {$vars.x};");
        FileOf("site/_partial.ks.scss", "skipped");
        FileOf("site/node_modules/c.ks.css", "skipped");
        FileOf("site/bad.ks.sass", "{$nope}");
        var output = new StringWriter();
        var error = new StringWriter();
        string cwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(dir);
        try
        {
            Assert.Equal(3, CssCommand.Run(new[] { "site", "--var", "x=2", "--out", "out" }, output, error));
            Assert.Equal(".a { w: 2rem; }", File.ReadAllText(Path.Combine(dir, "out", "a.css")));
            Assert.Equal("$x: 2;", File.ReadAllText(Path.Combine(dir, "out", "sub", "b.scss")));
            Assert.False(File.Exists(Path.Combine(dir, "out", "_partial.scss")));
            Assert.Contains("nope", error.ToString());
            Assert.Contains("site/a.ks.css -> out/a.css".Replace('/', Path.DirectorySeparatorChar), output.ToString());

            File.Delete(Path.Combine(dir, "site", "bad.ks.sass"));
            Assert.Equal(0, CssCommand.Run(new[] { "site/a.ks.css" }, output, error));
            Assert.True(File.Exists(Path.Combine(dir, "site", "a.css")));
            Assert.Equal(0, CssCommand.Run(Array.Empty<string>(), output, error));

            Directory.CreateDirectory(Path.Combine(dir, "empty"));
            var none = new StringWriter();
            Assert.Equal(0, CssCommand.Run(new[] { "empty" }, none, error));
            Assert.Contains("No stylesheets found", none.ToString());

            var e = new StringWriter();
            Assert.Equal(2, CssCommand.Run(new[] { "--out" }, output, e));
            Assert.Equal(2, CssCommand.Run(new[] { "--var", "x" }, output, e));
            Assert.Equal(2, CssCommand.Run(new[] { "--var" }, output, e));
            Assert.Equal(2, CssCommand.Run(new[] { "--watch" }, output, e));
            Assert.Equal(2, CssCommand.Run(new[] { "missing" }, output, e));
            Assert.Equal(2, CssCommand.Run(new[] { "site/sub/b.scss" }, output, e));
            Assert.Contains("--out needs a folder", e.ToString());
            Assert.Contains("--var needs name=value", e.ToString());
            Assert.Contains("Unknown option '--watch'", e.ToString());
            Assert.Contains("There is no file or folder at 'missing'", e.ToString());
            Assert.Contains("isn't a stylesheet", e.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
        }
    }

    [Fact]
    public async Task Kscript_serve_compiles_ks_css_on_request()
    {
        FileOf("site/theme.ks.css", ".a { padding: {@rem(8)}; }");
        FileOf("site/plain.css", ".p {}");
        FileOf("site/plain.ks.css", "never used");
        FileOf("site/broken.ks.css", ".a { x: {$nope}; }");
        FileOf("site/escape.ks.css", "<let name=\"x\" value=\"@read_text('../../secret.txt')\" />");
        var site = new PageSite(Path.Combine(dir, "site"));

        Assert.Equal(PageRoute.RouteKind.Stylesheet, site.Resolve("/theme.css").Kind);
        Assert.Equal(PageRoute.RouteKind.Static, site.Resolve("/plain.css").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, site.Resolve("/theme.ks.css").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, site.Resolve("/missing.css").Kind);

        PageResult ok = await Get(site, "/theme.css");
        Assert.Equal("text/css; charset=utf-8", ok.ContentType);
        Assert.Equal(".a { padding: 0.5rem; }", ok.Body);
        PageResult broken = await Get(site, "/broken.css");
        Assert.Equal(500, broken.Status);
        Assert.StartsWith("/* ", broken.Body);
        Assert.Contains("nope", broken.Body);
        Assert.Contains("outside the site folder", (await Get(site, "/escape.css")).Body);
    }

    private static Task<PageResult> Get(PageSite site, string path)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        return PageServer.HandleAsync(site, context.Request);
    }
}
