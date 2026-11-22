using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary><c>extend date { fn json() => ... }</c>: methods scripts add to built-in and declared types.</summary>
public class ExtensionTests
{

    private static string RunCode(string text)
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "main.kscript");
        File.WriteAllText(file, text);
        return RunFile(file);
    }

    private static string RunFile(string file, InterpreterOptions options = null)
    {
        var output = new StringWriter { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output, string.Empty), options: options ?? new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) }).RunFile(file);
        return output.ToString();
    }

    private static KScriptException CodeFails(string text) => Assert.Throws<KScriptException>(() => RunCode(text));

    [Fact]
    public void ExtendAddsMethodsToBuiltInTypes()
    {
        string output = RunCode(
            "extend date {\n  /// The date for an API.\n  fn json() => $this->format('yyyy-MM-dd')\n  fn is_weekend() => $this->format('ddd') in ['Sat', 'Sun']\n}\n"
            + "extend list {\n  fn second() => $this[1]\n}\n"
            + "extend int {\n  fn double(by = 2) => $this * $by\n}\n"
            + "extend string {\n  fn slug() => $this->lower()->replace(' ', '-')\n}\n"
            + "extend map {\n  fn size() => $this->keys()->count()\n}\n"
            + "echo \"{ @date('2026-11-21')->json() } { @date('2026-11-21')->is_weekend() } { [1, 2, 3]->second() }\"\n"
            + "echo \"{ 4->double() } { 4->double(by: 3) } { 4 |> double() } { 'Hello World'->slug() } { { a: 1 }->size() }\"\n");
        Assert.Equal("2026-11-21 true 2\n8 12 8 hello-world 1\n", output);
    }

    [Fact]
    public void ExtendAddsMethodsToDeclaredTypes()
    {
        string output = RunCode(
            "type User {\n  name: string\n}\n"
            + "extend User {\n  fn hello(greeting = 'Hi') => $greeting + ' ' + $this.name\n}\n"
            + "u = User({ name: 'Kyle' })\n"
            + "echo \"{ $u->hello() } { $u->hello(greeting: 'Hey') } { $u->keys()->join(',') }\"\n");
        Assert.Equal("Hi Kyle Hey Kyle name\n", output);
    }

    [Fact]
    public void ExtensionMethodsCanHaveBodies()
    {
        Assert.Equal("weekday\n", RunCode(
            "extend date {\n  fn kind() {\n    if $this->format('ddd') in ['Sat', 'Sun'] {\n      return 'weekend'\n    }\n    return 'weekday'\n  }\n}\necho @date('2026-11-20')->kind()\n"));
    }

    [Fact]
    public void OverrideReplacesABuiltInMethod()
    {
        Assert.Equal("D:2026 [1,2]\n", RunCode(
            "extend date {\n  override fn to_json() => 'D:' + $this->format('yyyy')\n}\necho \"{ @date('2026-01-02')->to_json() } { [1, 2]->to_json() }\"\n"));
    }

    [Fact]
    public void ReusingABuiltInNameNeedsOverride()
    {
        KScriptException ex = CodeFails("extend date {\n  fn to_json() => 1\n}\n");
        Assert.Contains("A date already has ->to_json(). To replace it, write override fn to_json(...)", ex.Message);
        Assert.Equal(2, ex.Line);

        ex = CodeFails("extend list {\n  override fn zzz() => 1\n}\n");
        Assert.Contains("A list has no built-in ->zzz() to override. Drop override", ex.Message);

        ex = CodeFails("type User {\n  name: string\n}\nextend User {\n  fn keys() => 1\n}\n");
        Assert.Contains("A User already has ->keys()", ex.Message);
    }

    [Fact]
    public void MistakesInExtendBlocksAreReported()
    {
        Assert.Contains("There is no type called dat to extend. Did you mean date?", CodeFails("extend dat {\n  fn x() => 1\n}\n").Message);
        Assert.Contains("holds fn methods only, not echo", CodeFails("extend date {\n  echo 'hi'\n}\n").Message);
        Assert.Contains("added to date twice", CodeFails("extend date {\n  fn x() => 1\n}\nextend date {\n  fn x() => 2\n}\n").Message);
        Assert.Contains("top level", CodeFails("if true {\n  extend date {\n    fn x() => 1\n  }\n}\n").Message);
        Assert.Contains("Text can't go directly inside <extend>", Fails("<extend type=\"date\">text</extend>").Message);
        Assert.Contains("<extend> needs a type", Fails("<extend type=\" \"><fn name=\"x\" value=\"1\" /></extend>").Message);
        Assert.Contains("<fn> needs a name", Fails("<extend type=\"date\"><fn name=\" \" value=\"1\" /></extend>").Message);
    }

    [Fact]
    public void ExtensionMethodsOnlyApplyWhereDeclared()
    {
        Assert.Contains("Dates have no method ->is_weekend()", Fails("<echo>{ @today()->is_weekend() }</echo>").Message);
    }

    [Fact]
    public void XmlExtendWorksToo()
    {
        Assert.Equal("2026 x\n", RunBody(
            "<extend type=\"date\"><fn name=\"json\" value=\"$this->format('yyyy')\" /><fn name=\"to_json\" override=\"yes\" value=\"'x'\" /></extend>"
            + "<echo>{ @date('2026-11-21')->json() } { @date('2026-11-21')->to_json() }</echo>"));
    }

    [Fact]
    public void ImportingAFileBringsItsExtensions()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        File.WriteAllText(Path.Combine(dir, "lib", "inner.kscript"), "extend string {\n  fn shout() => $this->upper() + '!'\n}\n");
        File.WriteAllText(Path.Combine(dir, "lib", "dates.kscript"), "import \"inner.kscript\"\nextend date {\n  fn stamp() => $this->format('yyyy')\n}\n");
        File.WriteAllText(Path.Combine(dir, "main.kscript"), "import \"lib/dates.kscript\" as d\necho \"{ @date('2026-01-01')->stamp() } { 'hi'->shout() }\"\n");
        Assert.Equal("2026 HI!\n", RunFile(Path.Combine(dir, "main.kscript")));
    }

    private static string Project(string json, params (string Path, string Text)[] files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        if (json != null) File.WriteAllText(Path.Combine(dir, "kscript.json"), json);
        foreach ((string path, string text) in files)
        {
            string full = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
        }
        return dir;
    }

    [Fact]
    public void TheExtensionsFolderAppliesToTheWholeProject()
    {
        string dir = Project("{ \"name\": \"p\" }",
            ("extensions/date.kscript", "extend date {\n  fn json() => $this->format('yyyy-MM-dd')\n}\n"),
            ("extensions/text/slug.kscript", "extend string {\n  fn slug() => $this->lower()->replace(' ', '-')\n}\n"),
            ("extensions/lists.ks.xml", "<kscript><extend type=\"list\"><fn name=\"second\" value=\"$this[1]\" /></extend></kscript>"),
            ("extensions/date.test.kscript", "this isn't loaded"),
            ("scripts/main.kscript", "echo \"{ @date('2026-11-21')->json() } { 'Hello World'->slug() } { [1, 2]->second() }\"\n"));
        Assert.Equal("2026-11-21 hello-world 2\n", RunFile(Path.Combine(dir, "scripts", "main.kscript")));
        Assert.Contains("Dates have no method ->json()", Assert.Throws<KScriptException>(() =>
            RunFile(Path.Combine(dir, "scripts", "main.kscript"), new InterpreterOptions { ProjectExtensions = false })).Message);
    }

    [Fact]
    public void KscriptJsonCanListOtherFoldersAndGlobs()
    {
        string dir = Project("{ \"extensions\": [\"shared/ext\", \"more/**/*.kscript\", \"one.kscript\"] }",
            ("extensions/ignored.kscript", "extend date {\n  fn ignored() => 1\n}\n"),
            ("shared/ext/a.kscript", "extend string {\n  fn a() => 'a'\n}\n"),
            ("more/deep/b.kscript", "extend string {\n  fn b() => 'b'\n}\n"),
            ("one.kscript", "extend string {\n  fn c() => 'c'\n}\n"),
            ("main.kscript", "echo \"{ ''->a() }{ ''->b() }{ ''->c() }\"\necho @today()->ignored()\n"));
        KScriptException ex = Assert.Throws<KScriptException>(() => RunFile(Path.Combine(dir, "main.kscript")));
        Assert.Contains("->ignored()", ex.Message);
    }

    [Fact]
    public void TwoExtensionFilesAddingTheSameMethodIsAnError()
    {
        string dir = Project("{}",
            ("extensions/a.kscript", "extend string {\n  fn slug() => 1\n}\n"),
            ("extensions/b.kscript", "extend string {\n  fn slug() => 2\n}\n"),
            ("main.kscript", "echo 'x'->slug()\n"));
        KScriptException ex = Assert.Throws<KScriptException>(() => RunFile(Path.Combine(dir, "main.kscript")));
        Assert.Contains("Two extension files add ->slug() to string: a.kscript line 2 and b.kscript line 2", ex.Message);
    }

    [Fact]
    public void AScriptsOwnExtensionWinsOverTheProjects()
    {
        string dir = Project("{}",
            ("extensions/a.kscript", "extend string {\n  fn slug() => 'project'\n}\n"),
            ("main.kscript", "extend string {\n  fn slug() => 'mine'\n}\necho 'x'->slug()\n"));
        Assert.Equal("mine\n", RunFile(Path.Combine(dir, "main.kscript")));
    }

    [Fact]
    public void ExtensionFilesCanUseEachOther()
    {
        string dir = Project("{}",
            ("extensions/a.kscript", "extend string {\n  fn shout() => $this->upper() + '!'\n}\n"),
            ("extensions/b.kscript", "extend date {\n  fn loud() => $this->format('yyyy')->shout()\n}\n"),
            ("main.kscript", "echo @date('2026-01-01')->loud()\n"));
        Assert.Equal("2026!\n", RunFile(Path.Combine(dir, "main.kscript")));
    }
}
