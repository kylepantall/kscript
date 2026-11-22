using System.Collections;
using System.Globalization;
using System.Text.Json;

using KScript.Metadata;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>A package whose class can't be created, as a broken plug-in would be.</summary>
public sealed class BrokenPackage : KScriptPackage
{
    public BrokenPackage() => throw new InvalidOperationException("Broken on purpose.");

    public override string Name => "KScript.Broken";

    public override string Description => "Never loads.";

    protected override void Register(PackageBuilder p)
    {
    }
}

/// <summary>A package with a value type of its own, for the parts of the runtime packages use.</summary>
internal sealed class ProbePackage : KScriptPackage
{
    public override string Name => "KScript.Probe";

    public override string Description => "Values for tests.";

    protected override void Register(PackageBuilder p)
    {
        p.Command("probe", "", "A value with its own methods.", "probe", c => new ProbeValue());
        p.Command("on_starting_thread", "fail", "Runs on the thread that started the script and says whether that is another thread.", "bool", c =>
        {
            bool fail = Values.Truthy(c.Arg(0));
            int here = Environment.CurrentManagedThreadId;
            int there = 0;
            c.Interpreter.InvokeOnStartingThread(() =>
            {
                if (fail) throw new KScriptException("refused on the starting thread", "Probe");
                there = Environment.CurrentManagedThreadId;
            });
            return there != here;
        });
        p.Command("lambda_params", "fn", "The parameter names of a lambda.", "list", c => ((Lambda)c.Arg(0)).Parameters.Cast<object>().ToList());
        p.Element(new KScript.Metadata.ElementInfo("needs-x", "Reads an attribute it didn't declare as required.", KScript.Metadata.ContentKind.Empty,
            new KScript.Metadata.AttributeInfo("x", KScript.Metadata.AttributeKind.Template, "Something.")), run => { run.RequiredTemplate("x"); return null; });
        p.Element(new KScript.Metadata.ElementInfo("needs-y", "Reads an attribute it doesn't have.", KScript.Metadata.ContentKind.Empty), run => { run.RequiredRaw("y"); return null; });
    }

    private sealed class ProbeValue : IKObject
    {
        public string TypeName => "probe";

        public string ToText() => "probe";

        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = name == "hello" ? "hi " + Values.ToStr(call.Arg(0)) : null;
            return name == "hello";
        }
    }
}

/// <summary>Built-in elements and methods, values from .NET code, and the parts of the runtime other programs use.</summary>
public sealed class BuiltInTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-builtins-" + Guid.NewGuid().ToString("N"));

    public BuiltInTests()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "lib.ks.xml"), "<kscript><fn name=\"helper\" params=\"x\" value=\"$x + 1\" /></kscript>");
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Run(string script)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output)).RunText(script, Path.Combine(dir, "main.ks.xml"));
        return output.ToString();
    }

    private string Body(string body) => Run("<kscript>" + body + "</kscript>");

    private KScriptException Fails(string body) => Assert.Throws<KScriptException>(() => Body(body));

    [Theory]
    [InlineData("{ [1, 2, 3]->at(-1) } { [1, 2, 3]->at(5) ?? 'none' }", "3 none")]
    [InlineData("{ [1, 2, 3]->find_index(fn($x) => $x > 1) } { [1]->find_index(fn($x) => $x > 5) }", "1 -1")]
    [InlineData("{ [1, 2]->each(fn($x, $i) => $x)->count() }", "2")]
    [InlineData("{ ['ab', 'ac', 'b']->group_by(fn($x) => $x[0])->to_json() }", "{\"a\":[\"ab\",\"ac\"],\"b\":[\"b\"]}")]
    [InlineData("{ [1, 2, 3, 4]->slice(1, -1) } { [1, 2, 3]->slice(-2) } { [1, 2]->slice(5, 1)->count() }", "2, 3 2, 3 0")]
    [InlineData("{ [1, 3]->insert(1, 2) } { [1, 2, 3]->remove_at(-1) } { [1]->remove_at(9) }", "1, 2, 3 1, 2 1")]
    [InlineData("{ [1, 2, 3]->chunk(2)->to_json() } { [1]->chunk(0)->count() }", "[[1,2],[3]] 1")]
    [InlineData("{ [{ id: 'a' }, { id: 'b' }]->to_map(fn($x) => $x.id)->keys() }", "a, b")]
    [InlineData("{ 'hello'->slice(1, -1) } { 'hello'->slice(-3) } { 'hello world'->truncate(5) } { 'hi'->truncate(5) } { 'abcdef'->truncate(3, '...') }", "ell llo hello… hi abc...")]
    public void List_and_text_methods(string expression, string expected)
    {
        Assert.Equal(expected + "\n", Body($"<echo>{expression}</echo>"));
    }

    [Fact]
    public void Elements_cover_their_less_common_forms()
    {
        Assert.Equal("x\n", Body("<kscript><echo>x</echo></kscript>"));
        Assert.Equal(string.Empty, Run("<KScript.fn name=\"f\" value=\"4\" />"));
        Assert.Equal("done\n", Body("<wait duration=\"1ms\" /><wait duration=\"0.001\" /><wait duration=\"0s\" /><echo>done</echo>"));
        Assert.Contains("needs a name and value", Fails("<set />").Message);
        Assert.Equal("[]012ab0:x1:yk=123\n", Body(
            "<let name=\"out\" value=\"''\" />" +
            "<for each=\"i\" in=\"null\"><set name=\"out\" value=\"$out + 'never'\" /></for>" +
            "<echo newline=\"no\">[] </echo>" +
            "<for each=\"i\" in=\"3\"><echo newline=\"no\">$i </echo></for>" +
            "<for each=\"c\" in=\"'ab'\"><echo newline=\"no\">$c </echo></for>" +
            "<for each=\"i, item\" in=\"['x', 'y']\"><echo newline=\"no\">{$i}:{$item} </echo></for>" +
            "<for each=\"k, v\" in=\"{ k: 1 }\"><echo newline=\"no\">{$k}={$v} </echo></for>" +
            "<for each=\"n\" in=\"[1, 2, 3, 4]\"><if test=\"$n == 1\"><continue /></if><if test=\"$n == 4\"><break /></if><echo newline=\"no\">$n </echo></for>" +
            "<echo></echo>"));
        Assert.Contains("can't loop over", Fails("<for each=\"x\" in=\"true\"></for>").Message);
        Assert.Contains("names the loop variable", Fails("<for each=\"a, b, c\" in=\"[1]\"></for>").Message);
    }

    [Fact]
    public void Match_needs_cases()
    {
        Assert.Equal("", Body("<match value=\"3\"><case is=\"1\"><echo>one</echo></case></match>"));
        Assert.Contains("<case> needs is=\"...\" or when=\"...\"", Fails("<match value=\"3\"><case><echo>x</echo></case></match>").Message);
        Assert.Contains("Only <case> and <default> can go inside <match>, not <echo>", Fails("<match value=\"3\"><echo>x</echo></match>").Message);
    }

    [Fact]
    public void Call_finds_commands_aliases_and_functions()
    {
        Assert.Equal("A 3 6\n", Body(
            "<import package=\"KScript.Math\" as=\"m\" /><import path=\"lib.ks.xml\" as=\"lib\" />" +
            "<call fn=\"to_upper\" args=\"'a'\" into=\"a\" /><call fn=\"m.sqrt\" args=\"9\" into=\"b\" /><call fn=\"lib.helper\" args=\"5\" into=\"c\" />" +
            "<echo>$a $b $c</echo>"));
        Assert.Contains("There is no function called nope. Declare it with <fn name=\"nope\"", Fails("<call fn=\"nope\" />").Message);
        Assert.Contains("Did you mean greet?", Fails("<fn name=\"greet\" value=\"1\" /><call fn=\"gret\" />").Message);
        Assert.Contains("There is no function called m.nope", Fails("<import package=\"KScript.Math\" as=\"m\" /><call fn=\"m.nope\" />").Message);
    }

    [Fact]
    public void Try_runs_finally_however_the_block_ends()
    {
        Assert.Equal("f1\nf2\nf3\n", Body(
            "<try><try><throw message=\"x\" type=\"A\" /><catch type=\"B\"><echo>wrong</echo></catch><finally><echo>f1</echo></finally></try><catch /></try>" +
            "<try><try><throw message=\"x\" /><catch><throw message=\"again\" /></catch><finally><echo>f2</echo></finally></try><catch /></try>" +
            "<for each=\"i\" in=\"[1]\"><try><break /><finally><echo>f3</echo></finally></try></for>"));
    }

    [Fact]
    public void Values_from_dotnet_code_become_kscript_values()
    {
        Assert.Equal(1.0, Values.From(1));
        Assert.Equal(2.0, Values.From(2L));
        Assert.Equal(1.5, Values.From(1.5f));
        Assert.Equal(2.5, Values.From(2.5m));
        Assert.Equal(3.0, Values.From((short)3));
        Assert.Equal(4.0, Values.From((byte)4));
        Assert.Equal(5.0, Values.From(5u));
        Assert.Equal(6.0, Values.From(6ul));
        Assert.Equal("c", Values.From('c'));
        Assert.IsType<DateTime>(Values.From(DateTimeOffset.Now));
        Assert.Equal(90.0, Values.From(TimeSpan.FromMinutes(1.5)));
        Assert.Equal(1.0, ((Dictionary<string, object>)Values.From(JsonDocument.Parse("{\"a\":1}").RootElement))["a"]);
        Assert.Equal("v", ((Dictionary<string, object>)Values.From(new Dictionary<string, string> { ["k"] = "v" }))["k"]);
        Assert.Equal(1.0, ((Dictionary<string, object>)Values.From(new SortedDictionary<string, object> { ["k"] = 1 }))["k"]);
        Assert.Equal(1.0, ((Dictionary<string, object>)Values.From(new Hashtable { [7] = 1 }))["7"]);
        Assert.Equal(new List<object> { 1.0, 2.0 }, Values.From(new[] { 1, 2 }));
        Assert.Equal("System.Object", Values.From(new object()));

        Assert.Equal("Object", Values.TypeOf(new object()));
        Assert.Equal("[1.5,\"2026-01-02T00:00:00.0000000\",\"x\"]", Values.ToJson(new List<object> { 1.5, new DateTime(2026, 1, 2), new Uri("x", UriKind.Relative) }));
        var fn = new NativeFunction("f", c => null);
        Assert.Equal("@f", Values.ToStr(fn));
        Assert.Equal("System.Object", Values.ToStr(new object()));
        Assert.False(Values.Truthy(null));
        Assert.True(Values.Truthy("x"));
        Assert.False(Values.Truthy(string.Empty));
        Assert.True(Values.Truthy(new List<object> { 1.0 }));
        Assert.False(Values.Truthy(new Dictionary<string, object>()));
        Assert.True(Values.Truthy(fn));
        Assert.Equal("null", Values.Describe(null));
        Assert.True(Values.StrictEquals(fn, fn));
        Assert.False(Values.StrictEquals(fn, new NativeFunction("f", c => null)));
        Assert.Equal(0, Values.Compare("b", "b", "lt"));
        Assert.Contains("Can't compare null with", Assert.Throws<KScriptException>(() => Values.Compare(null, "a", "lt")).Message);

        Assert.Same(fn, Values.ConvertTo(fn, "fn", "x"));
        var list = new List<object>();
        Assert.Same(list, Values.ConvertTo(list, "list", "x"));
        var map = new Dictionary<string, object>();
        Assert.Same(map, Values.ConvertTo(map, "map", "x"));
        var now = DateTime.Now;
        Assert.Equal(now, Values.ConvertTo(now, "date", "x"));
        Assert.Equal(new DateTime(2026, 3, 4), Values.ConvertTo("2026-03-04", "date", "x"));
        Assert.Equal(5.0, Values.ConvertTo(5.0, "", "x"));
        Assert.Contains("must be a list", Assert.Throws<KScriptException>(() => Values.ConvertTo("x", "list", "The value")).Message);
        Assert.Contains("must be a map", Assert.Throws<KScriptException>(() => Values.ConvertTo("x", "map", "The value")).Message);
        Assert.Contains("must be a fn", Assert.Throws<KScriptException>(() => Values.ConvertTo("x", "fn", "The value")).Message);
        Assert.Contains("must be a date", Assert.Throws<KScriptException>(() => Values.ConvertTo("never", "date", "The value")).Message);
        Assert.Contains("Unknown type 'blob'", Assert.Throws<KScriptException>(() => Values.ConvertTo("x", "blob", "The value")).Message);
        Assert.Equal("a &amp; &quot;b&quot; &lt;c&gt; &#39;", Values.HtmlEscape("a & \"b\" <c> '"));
    }

    [Fact]
    public void Callables_describe_themselves()
    {
        List<Parameter> parameters = Parameter.ParseList("a, b: int, c = 1", out string error);
        Assert.Null(error);
        Assert.Equal("a, b: int, c = …", string.Join(", ", parameters));
        Parameter.ParseList("a: 9lives", out error);
        Assert.Contains("Unknown type '9lives' for parameter 'a'", error);

        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var interpreter = new Interpreter(KScriptHost.ForText(output));
        interpreter.RunText("<kscript><fn name=\"f\" params=\"x\" value=\"$x\" /><let name=\"l\" value=\"fn($a, $b) => $a\" /><echo>{ $l } | {@f} | {@to_upper} | { ['a']->map(@to_upper) } { ['b']->map(@f) }</echo><fn name=\"g\" params=\"x, i\" value=\"$x + $i\" /><echo>{ ['c']->map(@g) }</echo></kscript>");
        Assert.Equal("fn($a, $b) => $a | fn f(x) | @to_upper | A b\nc0\n", output.ToString());

        Assert.Contains("has no parameter named 'y'", Fails("<fn name=\"f\" params=\"x\" value=\"$x\" /><echo>{ @f(y: 1) }</echo>").Message);
        Assert.Contains("@f() takes 1 argument but was given 2", Fails("<fn name=\"f\" params=\"x\" value=\"$x\" /><echo>{ @f(1, 2) }</echo>").Message);
        Assert.Contains("@f() takes 0 arguments but was given 1", Fails("<fn name=\"f\" value=\"1\" /><echo>{ @f(1) }</echo>").Message);
        Assert.Contains("needs a list as argument 1", Fails("<echo>{ [1]->zip(5) }</echo>").Message);

        var ns = new ImportNamespace("m", interpreter.Packages.Get("KScript.Core"));
        Assert.Equal("m", ns.Alias);
    }

    [Fact]
    public void Json_text_handles_edge_cases()
    {
        Assert.False(JsonText.IsValid("{ broken"));
        Assert.False(JsonText.IsValid("plain"));
        Assert.True(JsonText.IsValid("[1]"));
        Assert.Equal("{\n  \"a\": []\n}", JsonText.Pretty(new Dictionary<string, object> { ["a"] = new List<object>() }));
        Assert.Equal(string.Empty, JsonText.Colourize(string.Empty));
        Assert.Contains("\"unterminated", JsonText.Colourize("{ \"unterminated"));
        Assert.Contains("\"k\"", JsonText.Colourize("{ \"k\"   : 1 }"));
    }

    [Fact]
    public void Modules_packages_and_scopes_answer_questions()
    {
        var interpreter = new Interpreter(KScriptHost.ForText(new StringWriter()));
        Module module = interpreter.LoadText("<kscript><import package=\"KScript.Math\" /></kscript>", Path.Combine(dir, "m.ks.xml"));
        Assert.True(module.Imports("Math"));
        Assert.False(module.Imports("KScript.IO"));
        Assert.Equal(module.DisplayName, module.ToString());

        KScriptPackage math = interpreter.Packages.Get("Math");
        Assert.Equal("Math", math.ShortName);
        Assert.Equal("KScript.Math", math.ToString());
        Assert.Same(math, math.Elements.Values.FirstOrDefault()?.Package ?? math);
        Assert.Same(math, math.Commands["sqrt"].Package);
        Assert.Same(math, math.Methods.First().Package);
        Assert.Equal("Core", interpreter.Packages.Core.ShortName);
        Assert.Equal("Custom", new CustomNamed().ShortName);

        var registry = new PackageRegistry();
        registry.Register("KScript.Broken", typeof(BrokenPackage).AssemblyQualifiedName);
        Assert.Null(registry.Get("Broken"));
        Assert.Null(registry.Get("Broken"));

        var root = new Scope();
        Scope child = root.Child().Child();
        Assert.Same(root, child.Root);
        root.Declare("x", 1.0);
        Assert.True(child.Has("x"));
        Assert.False(child.HasOwn("x"));
        root.Declare("k", 1.0, isConst: true);
        Assert.Contains("$k is const and can't be declared again", Assert.Throws<KScriptException>(() => root.Declare("k", 2.0)).Message);
    }

    private sealed class CustomNamed : KScriptPackage
    {
        public override string Name => "Custom";

        public override string Description => "A package named without KScript.";

        protected override void Register(PackageBuilder p) => Assert.Equal("Custom", p.Name);
    }

    [Fact]
    public void Hosts_resolve_home_and_talk_to_the_console()
    {
        var host = KScriptHost.ForText(new StringWriter());
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.GetFullPath(home), Path.GetFullPath(host.ResolvePath("~", dir)));
        Assert.Equal(Path.Combine(home, "x"), host.ResolvePath("~/x", dir));
        Assert.NotNull(KScriptHost.Console());
        Assert.ThrowsAny<Exception>(() => host.ReadKey());
        try { host.ClearScreen(); } catch (IOException) { }
        try { _ = host.TerminalWidth; } catch (IOException) { }
        _ = new CustomNamed().Commands;
    }

    [Fact]
    public void Elements_check_variable_names_at_run_time()
    {
        Assert.Contains("isn't a variable name", Fails("<for each=\"i\" in=\"[1]\" at=\"2x\"></for>").Message);
    }

    [Fact]
    public void Package_values_and_attributes_work_through_the_runtime()
    {
        var registry = new PackageRegistry();
        registry.Register(new ProbePackage());
        string Run(string body)
        {
            var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            new Interpreter(KScriptHost.ForText(output), registry).RunText("<kscript><import package=\"KScript.Probe\" />" + body + "</kscript>");
            return output.ToString();
        }

        Assert.Equal("hi you a, b\n", Run("<echo>{ @probe() |> hello('you') } { @lambda_params(fn($a, $b) => 1) }</echo>"));
        Assert.Contains("<needs-x> needs a x attribute: Something.", Assert.Throws<KScriptException>(() => Run("<needs-x />")).Message);
        Assert.Contains("<needs-y> needs a y attribute.", Assert.Throws<KScriptException>(() => Run("<needs-y />")).Message);
        Assert.Equal("hi you\n", Run("<echo>{ @probe()->hello(x: 1, 'you') }</echo>"));
        Assert.Equal("true\n", Run("<echo>{ @on_starting_thread(false) }</echo>"));
        Assert.Contains("refused on the starting thread", Assert.Throws<KScriptException>(() => Run("<echo>{ @on_starting_thread(true) }</echo>")).Message);
        int ran = 0;
        new Interpreter(KScriptHost.ForText(new StringWriter()), registry).InvokeOnStartingThread(() => ran = Environment.CurrentManagedThreadId);
        Assert.Equal(Environment.CurrentManagedThreadId, ran);
        Assert.Same(PackageRegistry.Default.Get("Math"), PackageRegistry.Default.Get("Math").Variables["pi"].Package);
        Assert.Equal("ok\nfin\n", Body("<try><echo>ok</echo><catch /><finally><echo>fin</echo></finally></try><try><do>1</do></try>"));
        Assert.Contains("There is no function called m.pi", Fails("<import package=\"KScript.Math\" as=\"m\" /><call fn=\"m.pi\" />").Message);
    }

    private sealed class DateJsonPackage : KScriptPackage
    {
        public override string Name => "KScript.DateJson";

        public override string Description => "Dates written as JSON, for tests.";

        protected override void Register(PackageBuilder p) =>
            p.Method(KType.Date, "json", "", "The date as a JSON string.", KType.String, (target, call) => "\"" + ((DateTime)target).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\"");
    }

    [Fact]
    public void A_package_can_give_dates_a_method_strings_have()
    {
        var registry = new PackageRegistry();
        registry.Register(new DateJsonPackage());
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output), registry).RunText("<kscript><import package=\"KScript.DateJson\" /><echo>{ @date('2026-11-21')->json() }</echo></kscript>");
        Assert.Equal("\"2026-11-21\"\n", output.ToString());
    }

    [Fact]
    public void Project_files_warn_about_settings_they_do_not_know()
    {
        string path = Path.Combine(dir, "kscript.json");
        File.WriteAllText(path, "{ \"lint\": { \"warningsAsErrors\": \"maybe\", \"colour\": 1 }, \"test\": { \"timeout\": \"soon\", \"packages\": [\"Nope\"], \"speed\": 1 }, \"name\": 5, " +
            "\"app\": { \"colour\": 1 }, \"format\": { \"indent\": 40, \"tabWidth\": 0, \"printWidth\": 5, \"maxBlankLines\": -1, \"endOfLine\": \"cr\", \"enabled\": \"yes\" } }");
        ProjectConfig config = ProjectConfig.Load(path);
        string warnings = string.Join("\n", config.Warnings);
        Assert.Contains("lint.warningsAsErrors should be true or false.", warnings);
        foreach (string expected in new[] { "unknown setting 'app.colour'", "format.indent should be", "format.tabWidth should be", "format.printWidth should be", "format.maxBlankLines should be", "format.endOfLine should be", "format.enabled should be" })
            Assert.Contains(expected, warnings);
        Assert.Contains("unknown setting 'lint.colour'", warnings);
        Assert.Contains("test.timeout should be a number of seconds", warnings);
        Assert.Contains("test.packages: there is no package 'Nope'.", warnings);
        Assert.Contains("unknown setting 'test.speed'", warnings);
        Assert.Contains("name should be a string.", warnings);
        Assert.Null(ProjectConfig.Find("bad\0path"));
    }
}
