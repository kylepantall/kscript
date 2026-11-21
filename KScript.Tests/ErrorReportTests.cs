using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>Errors are shown as KScript errors, never as .NET exceptions (unless --debug), and a deleted working folder is survived.</summary>
public class ErrorReportTests
{
    private static string Written(Exception ex, string doing = null)
    {
        var w = new StringWriter();
        if (doing == null) ErrorReport.Write(w, ex);
        else ErrorReport.Write(w, doing, ex);
        return w.ToString();
    }

    [Fact]
    public void Dotnet_exceptions_become_KScript_error_types()
    {
        Assert.Equal("FileNotFound", ErrorReport.ToKScript(new FileNotFoundException("gone")).Type);
        Assert.Equal("Access", ErrorReport.ToKScript(new UnauthorizedAccessException("no")).Type);
        Assert.Equal("Cancelled", ErrorReport.ToKScript(new OperationCanceledException()).Type);
        Assert.Equal("Limit", ErrorReport.ToKScript(new InsufficientExecutionStackException()).Type);
        Assert.Equal("Limit", ErrorReport.ToKScript(new OutOfMemoryException()).Type);
        Assert.Equal("Context", ErrorReport.ToKScript(new BreakSignal()).Type);
        Assert.Equal(ErrorReport.InternalType, ErrorReport.ToKScript(new NullReferenceException()).Type);
        Assert.Equal("Config", ErrorReport.ToKScript(new ProjectConfigException("bad json")).Type);

        // Wrappers are unwrapped to the exception they carry.
        var inner = new IOException("disk");
        Assert.Equal("IO", ErrorReport.ToKScript(new AggregateException(new System.Reflection.TargetInvocationException(new TypeInitializationException("T", inner)))).Type);
        var kscript = new KScriptException("mine", "Value");
        Assert.Same(kscript, ErrorReport.ToKScript(new AggregateException(kscript)));
        Assert.Equal(ErrorReport.InternalType, ErrorReport.ToKScript(new AggregateException(new IOException("a"), new IOException("b"))).Type);
    }

    [Fact]
    public void Errors_print_in_the_cli_style_without_a_stack_trace()
    {
        string thrown;
        try { throw new InvalidOperationException("odd"); }
        catch (Exception ex) { thrown = Written(ex); }
        Assert.Equal("   ERROR  Error: odd\n", thrown.Replace("\r\n", "\n"));

        var located = new KScriptException("No variable $x.", "Name") { File = "/site/app.kscript", Line = 3, Column = 7 };
        Assert.Equal("   ERROR  app.kscript:3:7: Name: No variable $x.\n", Written(located).Replace("\r\n", "\n"));

        // A cause and what kscript was doing go on their own lines, under the message.
        var caused = new KScriptException("Saving failed.", "IO", new KScriptException("Disk full.", "IO"));
        Assert.Equal("   ERROR  Could not save.\n          IO: Saving failed.\n          Caused by IO: Disk full.\n", Written(caused, "Could not save.").Replace("\r\n", "\n"));

        // Something kscript didn't expect says so, and how to see more.
        string bug = Written(new NullReferenceException("Object reference not set to an instance of an object."));
        Assert.Contains("ERROR  Internal: Object reference", bug);
        Assert.Contains("--debug", bug);
        Assert.DoesNotContain("System.NullReferenceException", bug);
    }

    [Fact]
    public void Debug_adds_the_dotnet_details()
    {
        Exception thrown;
        try { throw new InvalidOperationException("odd", new ArgumentException("why")); }
        catch (Exception ex) { thrown = ex; }
        string text = ErrorReport.Format(thrown, debug: true);
        Assert.StartsWith("Error: odd", text);
        Assert.Contains("System.InvalidOperationException: odd", text);
        Assert.Contains("--- inner exception ---", text);
        Assert.Contains("System.ArgumentException: why", text);
        Assert.Contains(nameof(Debug_adds_the_dotnet_details), text);

        Assert.True(ErrorReport.IsOn("1"));
        Assert.True(ErrorReport.IsOn("yes"));
        Assert.False(ErrorReport.IsOn("0"));
        Assert.False(ErrorReport.IsOn("false"));
        Assert.False(ErrorReport.IsOn(" "));
        Assert.False(ErrorReport.IsOn(null));
    }

    [Fact]
    public void The_label_is_coloured_at_a_terminal()
    {
        var w = new StringWriter();
        ErrorReport.WriteMessage(w, "one\n\ntwo", colour: true);
        Assert.Equal("  \u001b[1;97;41m ERROR \u001b[0m one\n\n          two\n", w.ToString().Replace("\r\n", "\n"));

        string noColour = Environment.GetEnvironmentVariable("NO_COLOR"), force = Environment.GetEnvironmentVariable("FORCE_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", null);
            Environment.SetEnvironmentVariable("FORCE_COLOR", "1");
            Assert.True(ErrorReport.UseColour(new StringWriter()));
            Environment.SetEnvironmentVariable("FORCE_COLOR", null);
            Assert.False(ErrorReport.UseColour(new StringWriter()));
            Environment.SetEnvironmentVariable("NO_COLOR", "1");
            Assert.False(ErrorReport.UseColour(Console.Error));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", noColour);
            Environment.SetEnvironmentVariable("FORCE_COLOR", force);
        }
    }

    [Fact]
    public void Locations_are_read_from_text()
    {
        KScriptException e = ErrorReport.At("bad", "Page", "pages/index.html:12:4");
        Assert.Equal(("pages/index.html", 12, 4), (e.File, e.Line, e.Column));
        Assert.Equal("index.html:12:4: Page: bad", e.ToString());
        Assert.Equal(("a.html", 2, 0), (ErrorReport.At("bad", "Page", "a.html:2").File, ErrorReport.At("bad", "Page", "a.html:2").Line, 0));
        Assert.Equal("a.html", ErrorReport.At("bad", "Page", "a.html").File);
        Assert.Null(ErrorReport.At("bad", "Page", null).File);

        var wrapped = new KScriptException("no such thing", "Name");
        Assert.Same(wrapped, new PageException("no such thing", wrapped).ToKScriptError());
        Assert.Equal("Page", new PageException("other", wrapped).ToKScriptError().Type);
        Assert.Equal("Template", new KScript.Scaffolding.StubException("x").ToKScriptError().Type);
        Assert.Equal("Compile", new KScript.Compile.CompileException("x").ToKScriptError().Type);
    }

    [Fact]
    public void The_debug_option_is_taken_before_the_script_or_anywhere_for_a_command()
    {
        try
        {
            Assert.Equal(new[] { "serve", "site" }, CommandLine.TakeDebug(new[] { "serve", "--debug", "site" }));
            Assert.True(ErrorReport.Debug);
            ErrorReport.Debug = false;
            Assert.Equal(new[] { "app.kscript", "--debug" }, CommandLine.TakeDebug(new[] { "--debug", "app.kscript", "--debug" }));
            Assert.True(ErrorReport.Debug);
            ErrorReport.Debug = false;
            Assert.Equal(new[] { "./app", "--debug" }, CommandLine.TakeDebug(new[] { "./app", "--debug" }));
            Assert.False(ErrorReport.Debug);
        }
        finally
        {
            ErrorReport.Debug = false;
        }
    }

    [Fact]
    public void Any_failure_in_a_command_prints_a_KScript_error()
    {
        // kscript.json that isn't JSON fails the check command with a Config error, not a .NET exception.
        string dir = Path.Combine(Path.GetTempPath(), "kscript-err-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "kscript.json"), "{ nope");
            File.WriteAllText(Path.Combine(dir, "app.kscript"), "echo 'hi'");
            var e = new StringWriter();
            new CommandLine { Out = new StringWriter(), Error = e }.Run(new[] { "serve", dir, "--port", "0" });
            Assert.StartsWith("   ERROR  Config: ", e.ToString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Runs <paramref name="test"/> in a folder that it may delete. Linux and macOS only: Windows can't delete a process's folder.</summary>
    private static void InDeletableFolder(Action<string> test)
    {
        if (OperatingSystem.IsWindows()) return;
        string before = Directory.GetCurrentDirectory();
        string dir = Path.Combine(Path.GetTempPath(), "kscript-cwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Directory.SetCurrentDirectory(dir);
            WorkingFolder.Remember();
            test(Directory.GetCurrentDirectory());
        }
        finally
        {
            Directory.SetCurrentDirectory(before);
            WorkingFolder.Remember();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_deleted_working_folder_is_a_clear_error()
    {
        InDeletableFolder(dir =>
        {
            Directory.Delete(dir);
            var error = Assert.Throws<KScriptException>(() => WorkingFolder.Current);
            Assert.Equal("FileNotFound", error.Type);
            Assert.Contains($"({dir}) no longer exists", error.Message);
            Assert.Throws<KScriptException>(WorkingFolder.Check);

            // .NET's own exception for it says what happened too.
            Exception raw = Assert.ThrowsAny<IOException>(() => Directory.GetCurrentDirectory());
            Assert.Contains("no longer exists", ErrorReport.Format(raw));

            // A host made now doesn't fail until it needs the folder.
            var host = new KScriptHost(new StringWriter()) { WorkingDirectory = "/tmp" };
            Assert.Equal("/tmp", host.WorkingDirectory);
            Assert.Throws<KScriptException>(() => new KScriptHost(new StringWriter()).WorkingDirectory);

            // The CLI says so instead of crashing.
            var e = new StringWriter();
            Assert.Equal(1, new CommandLine { Out = new StringWriter(), Error = e }.Run(new[] { "fmt" }));
            Assert.Contains("ERROR  FileNotFound: The folder kscript was started in", e.ToString());
        });
    }

    [Fact]
    public void A_replaced_working_folder_is_gone_back_to()
    {
        InDeletableFolder(dir =>
        {
            Directory.Delete(dir);
            Directory.CreateDirectory(dir);
            Assert.Equal(dir, WorkingFolder.Current);
            Assert.Equal(dir, Directory.GetCurrentDirectory());
        });
    }

    [Fact]
    public void The_server_keeps_answering_after_its_folder_is_recreated()
    {
        // What happened in the bug report: kscript serve's folder was recreated, and each request then failed in getcwd.
        InDeletableFolder(dir =>
        {
            using var site = new TempSite(("routes.ks.xml", """
                <KScript.Routes>
                  <let name="hello" value="fn($request) => 'Hello ' + $request.path" />
                  <Get path="/hi" to="hello" />
                </KScript.Routes>
                """));
            var app = new HttpApp(site.Root, null);
            Directory.Delete(dir);
            PageResult answer = app.Handle(new Dictionary<string, object> { ["method"] = "GET", ["path"] = "/hi" });
            Assert.Equal(200, answer.Status);
            Assert.Equal("Hello /hi", answer.Body);
        });
    }
}
