using System.Globalization;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>A package with the unusual kinds of element a package may add.</summary>
internal sealed class OddPackage : KScriptPackage
{
    public override string Name => "KScript.Odd";

    public override string Description => "Elements for tests.";

    protected override void Register(PackageBuilder p)
    {
        p.Element(new ElementInfo("page-only", "Only in pages, with no handler.", ContentKind.Empty) { Context = ElementContext.Page }, null);
        p.Element(new ElementInfo("described", "Described, but nothing runs it.", ContentKind.Empty), null);
        p.Element(new ElementInfo("panel", "Holds output markup.", ContentKind.Markup) { AllowsExtraAttributes = false }, run => { run.Host.Write("panel"); return null; });
        p.Element(new ElementInfo("disk-full", "Fails the way file code does.", ContentKind.Empty), run => throw new IOException("The disk is full."));
        p.Element(new ElementInfo("loose", "Takes any attribute.", ContentKind.Empty) { AllowsExtraAttributes = true }, run => null);
    }
}

/// <summary>Every way a script can be wrong before or while it runs, and what kscript says about it.</summary>
public sealed class ScriptErrorTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-errors-" + Guid.NewGuid().ToString("N"));

    public ScriptErrorTests()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "lib.ks.xml"), "<kscript><fn name=\"helper\" value=\"1\" /></kscript>");
        File.WriteAllText(Path.Combine(dir, "self.ks.xml"), "<kscript><import src=\"self.ks.xml\" /></kscript>");
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private (string Output, int Code) Run(string script, InterpreterOptions options = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        int code = new Interpreter(KScriptHost.ForText(output), options: options).RunText(script, Path.Combine(dir, "main.ks.xml"));
        return (output.ToString(), code);
    }

    private KScriptException Fails(string script, InterpreterOptions options = null) => Assert.Throws<KScriptException>(() => Run(script, options));

    [Theory]
    [InlineData("<!-- only a comment -->", "Syntax", "The script is empty.")]
    [InlineData("<kscript><KScript.nothing /></kscript>", "Import", "No KScript package has an element called <nothing>")]
    [InlineData("<kscript><import package=\"KScript.Math\" as=\"1x\" /></kscript>", "Import", "as=\"1x\" isn't a name.")]
    [InlineData("<kscript><import src=\"self.ks.xml\" /></kscript>", "Import", "imports this script back")]
    [InlineData("<kscript><import src=\"missing.ks.xml\" /></kscript>", "FileNotFound", "There is no script at missing.ks.xml")]
    [InlineData("<kscript version=\"1\"></kscript>", "Retired", "version=\"...\" was removed in KScript 2.")]
    [InlineData("<kscript foo=\"1\"></kscript>", "Syntax", "<kscript> has no attributes, but this one has foo=\"...\".")]
    [InlineData("<kscript><case is=\"1\" /></kscript>", "Syntax", "<case> must be inside <match>.")]
    [InlineData("<kscript><echo><let name=\"x\" value=\"1\" /></echo></kscript>", "Syntax", "<echo> can only hold text, not <let>.")]
    [InlineData("<kscript>stray text that is very long indeed and goes past forty characters</kscript>", "Syntax", "'stray text that is very long indeed and …'")]
    [InlineData("<kscript><echo condition=\"1\" /></kscript>", "Retired", "condition=\"...\" was removed")]
    [InlineData("<kscript><echo valu=\"1\" /></kscript>", "Syntax", "<echo> has no attribute 'valu'. Did you mean value? Its attributes are: value, format, newline.")]
    [InlineData("<kscript><clear-all now=\"yes\" /></kscript>", "Syntax", "Unknown element <clear-all>.")]
    [InlineData("<kscript><break /></kscript>", "Syntax", "<break/> must be inside a loop")]
    [InlineData("<kscript><continue /></kscript>", "Syntax", "<continue/> must be inside a loop")]
    [InlineData("<kscript><fn name=\"f\"><break /></fn><do>@f()</do></kscript>", "Syntax", "<break/> must be inside a loop")]
    [InlineData("<kscript><let name=\"x\" value=\"   \" /></kscript>", "Syntax", "Expected an expression")]
    [InlineData("<kscript><echo>{ 1 + }</echo></kscript>", "Syntax", "Expected a value, found '}'.")]
    [InlineData("<kscript><fn name=\"f\" params=\"a b\" /></kscript>", "Syntax", "'a b' isn't a parameter name.")]
    [InlineData("<kscript><import package=\"KScript.Web\" /><live id=\"x\" /></kscript>", "Context", "<live> only works in pages served by kscript serve")]
    [InlineData("<kscript><break>x</break></kscript>", "Syntax", "<break> has no content; write it as <break ... />.")]
    [InlineData("<kscript><echo format=\"weird\">x</echo></kscript>", "Syntax", "format=\"weird\" isn't one of: text, json, markdown.")]
    [InlineData("<kscript><echo newline=\"maybe\">x</echo></kscript>", "Syntax", "newline=\"maybe\" should be yes or no.")]
    [InlineData("<kscript><if><echo>x</echo></if></kscript>", "Syntax", "<if> needs a test attribute")]
    [InlineData("<kscript><import src=\"lib.ks.xml\" names=\"nope\" /></kscript>", "Import", "lib.ks.xml doesn't export 'nope'. Scripts export")]
    [InlineData("<kscript><import src=\"lib.ks.xml\" names=\"helpr\" /></kscript>", "Import", "Did you mean helper?")]
    [InlineData("<kscript><echo>{@f()}</echo><fn name=\"f\" params=\"x = \" /></kscript>", "Syntax", "The default value of 'x' isn't a valid expression")]
    public void Errors_have_a_type_and_say_what_to_do(string script, string type, string message)
    {
        KScriptException ex = Fails(script);
        Assert.Equal(type, ex.Type);
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Page_imports_check_paths_names_and_circles()
    {
        var interpreter = new Interpreter(KScriptHost.ForText(new StringWriter()));
        Module module = interpreter.LoadText("<kscript></kscript>", Path.Combine(dir, "page.ks.xml"));
        var scope = new Scope();
        interpreter.ImportScript(module, Path.Combine(dir, "lib.ks.xml"), scope);
        Assert.True(scope.TryGet("helper", out _));
        interpreter.ImportScript(module, Path.Combine(dir, "lib.ks.xml"), scope, alias: "lib");
        Assert.True(module.Aliases.ContainsKey("lib"));

        Assert.Contains("isn't a name", Assert.Throws<KScriptException>(() => interpreter.ImportScript(module, Path.Combine(dir, "lib.ks.xml"), scope, alias: "9")).Message);
        var missing = Assert.Throws<KScriptException>(() => interpreter.ImportScript(module, Path.Combine(dir, "nowhere.ks.xml"), scope));
        Assert.Equal("FileNotFound", missing.Type);
        Assert.EndsWith("nowhere.ks.xml", missing.File);
    }

    [Fact]
    public void Package_elements_without_handlers_or_with_markup_are_checked()
    {
        var registry = new PackageRegistry();
        registry.Register(new OddPackage());
        registry.Register("KScript.Ghost", "No.Such.Type, Nowhere");
        string Run(string body)
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            new Interpreter(KScriptHost.ForText(output), registry).RunText("<kscript><import package=\"KScript.Odd\" />" + body + "</kscript>");
            return output.ToString();
        }

        Assert.Contains("<page-only> only works in web pages", Assert.Throws<KScriptException>(() => Run("<page-only />")).Message);
        Assert.Contains("<described> only works in web pages", Assert.Throws<KScriptException>(() => Run("<described />")).Message);
        Assert.Equal("panel", Run("<panel on-click=\"x\"><div>text</div><let name=\"a\" value=\"1\" /></panel><loose anything=\"1\" />"));
        Assert.Contains("Expected an expression", Assert.Throws<KScriptException>(() => Run("<panel><let name=\"a\" value=\"1 +\" /></panel>")).Message);
        var disk = Assert.Throws<KScriptException>(() => Run("<disk-full />"));
        Assert.Equal("IO", disk.Type);
        Assert.Equal(1, disk.Line);
        Assert.Contains("KScript.Ghost isn't installed in this copy of kscript", Assert.Throws<KScriptException>(() => Run("<import package=\"Ghost\" />")).Message);
    }

    [Fact]
    public void A_package_element_can_be_the_root()
    {
        Assert.Equal("hi\n", Run("<KScript.echo>hi</KScript.echo>").Output);
    }

    [Fact]
    public void Spawned_functions_report_their_errors_and_are_waited_for()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output)).RunText("<kscript><spawn fn=\"f\" /><spawn fn=\"g\" /><fn name=\"f\"><throw message=\"boom\" /></fn><fn name=\"g\"><break /></fn></kscript>");
        Assert.Contains("boom", output.ToString());
    }

    [Fact]
    public void Runaway_scripts_are_stopped()
    {
        Assert.Equal("Limit", Fails("<kscript><while test=\"true\"><do>1</do></while></kscript>", new InterpreterOptions { MaxSteps = 5000 }).Type);
        var timeout = Fails("<kscript><while test=\"true\"><do>1</do></while></kscript>", new InterpreterOptions { Timeout = TimeSpan.FromMilliseconds(200) });
        Assert.Contains("ran for more than 0.2 seconds", timeout.Message);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Assert.Contains("The script was stopped.", Fails("<kscript><while test=\"true\"><do>1</do></while></kscript>", new InterpreterOptions { Cancellation = cancel.Token }).Message);
    }

    [Fact]
    public void Dotnet_errors_become_typed_script_errors()
    {
        Assert.Equal("FileNotFound", Interpreter.Wrap(new FileNotFoundException("x")).Type);
        Assert.Equal("FileNotFound", Interpreter.Wrap(new DirectoryNotFoundException("x")).Type);
        Assert.Equal("Access", Interpreter.Wrap(new UnauthorizedAccessException("x")).Type);
        Assert.Equal("IO", Interpreter.Wrap(new IOException("x")).Type);
        Assert.Equal("Network", Interpreter.Wrap(new HttpRequestException("x")).Type);
        Assert.Equal("Timeout", Interpreter.Wrap(new TaskCanceledException("x")).Type);
        Assert.Equal("Format", Interpreter.Wrap(new FormatException("x")).Type);
        Assert.Equal("Type", Interpreter.Wrap(new OverflowException("x")).Type);
        Assert.Equal("Argument", Interpreter.Wrap(new ArgumentException("x")).Type);
        Assert.Equal("Error", Interpreter.Wrap(new InvalidOperationException("x")).Type);
        Assert.Equal("Platform", Interpreter.Wrap(new PlatformNotSupportedException("x")).Type);
        Assert.Equal("Platform", Interpreter.Wrap(new NotSupportedException("x")).Type);
        var same = new KScriptException("x");
        Assert.Same(same, Interpreter.Wrap(same));
        Assert.Null(Interpreter.Wrap(new Exception("x")));
    }
}
