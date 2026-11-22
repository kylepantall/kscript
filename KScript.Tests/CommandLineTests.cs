using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KScript.Tests;

/// <summary>The kscript command itself: every subcommand is reached, and scripts run with their project.</summary>
public sealed class CommandLineTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("kscript-cli").FullName;
    private readonly TextWriter stdout = Console.Out;
    private readonly TextWriter stderr = Console.Error;
    private readonly string env = Environment.GetEnvironmentVariable("KSCRIPT_ENV");

    public void Dispose()
    {
        Console.SetOut(stdout);
        Console.SetError(stderr);
        Environment.SetEnvironmentVariable("KSCRIPT_ENV", env);
        Directory.Delete(dir, true);
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private (int Code, string Out, string Err) Run(string[] args, Func<StringWriter, StringWriter, CommandLine> make = null, string compiled = "")
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        Console.SetOut(o);
        Console.SetError(e);
        try
        {
            CommandLine cli = make?.Invoke(o, e) ?? new CommandLine();
            int code = compiled == "" ? cli.Run(args) : cli.RunCompiled(compiled, args);
            return (code, o.ToString(), e.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
    }

    private (int Code, string Out, string Err) Kscript(params string[] args) => Run(args);

    [Fact]
    public void Main_runs_the_command_line()
    {
        var o = new StringWriter();
        Console.SetOut(o);
        try
        {
            object code = typeof(CommandLine).Assembly.EntryPoint!.Invoke(null, new object[] { new[] { "--version" } });
            Assert.Equal(0, code);
        }
        finally
        {
            Console.SetOut(stdout);
        }
        Assert.Equal(KScript.Updates.SelfUpdater.CurrentVersion, o.ToString().Trim());
    }

    [Fact]
    public void Usage_version_and_unknown_options()
    {
        var none = Kscript();
        Assert.Equal(1, none.Code);
        Assert.Contains("kscript <command> [options] [arguments]", none.Out);
        foreach (string help in new[] { "--help", "-h", "help", "-?", "/?" })
        {
            Assert.Equal(0, Kscript(help).Code);
        }
        Assert.Equal(0, Kscript("version").Code);
        var unknown = Kscript("--frobnicate");
        Assert.Equal(1, unknown.Code);
        Assert.Contains("Unknown option '--frobnicate'", unknown.Err);
        Assert.Contains("--env needs a name", Kscript("--env").Err);
        Assert.Contains("--env needs a name", Kscript("--env=").Err);
    }

    [Fact]
    public void Help_lists_every_command()
    {
        string help = Kscript("--help").Out;
        Assert.StartsWith("KScript ", help);
        foreach (CliCommand command in CliReference.Commands)
        {
            Assert.Matches(@"\n  " + command.Name + " +[A-Z]", help);
            var one = Kscript("help", command.Name);
            Assert.Equal(0, one.Code);
            foreach (string usage in command.Usage.Split('\n')) Assert.Contains("  " + usage, one.Out);
            foreach (CliOption option in command.Options) Assert.Contains(option.Name, one.Out);
        }
        Assert.Contains("kscript [--env name] <script>", Kscript("help", "run").Out);
        Assert.Contains("kscript serve [site folder]", Kscript("serve", "--help").Out);
        Assert.Contains("kscript serve [site folder]", Kscript("serve", "-h").Out);
        Assert.Contains("kscript make project", Kscript("help", "make:").Out);
        var unknown = Kscript("help", "nope");
        Assert.Equal(1, unknown.Code);
        Assert.Contains("ERROR  There is no command 'nope'", unknown.Err);
        Assert.Contains("kscript --help lists the commands", Kscript("serv").Err);
    }

    [Fact]
    public void The_default_writers_follow_the_console_after_its_encoding_is_set()
    {
        // Setting Console.OutputEncoding (Run does) replaces Console.Out and Console.Error. The command line must
        // write to the new writers, or CliStyle doesn't see the console and a terminal gets no colour.
        var cli = new CommandLine();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Assert.Same(Console.Out, cli.Out);
        Assert.Same(Console.Error, cli.Error);
        var given = new StringWriter();
        Assert.Same(given, new CommandLine { Out = given }.Out);
    }

    [Fact]
    public void Ansi_flags_force_colours_on_or_off()
    {
        try
        {
            Assert.Contains("\u001b[1;33mUsage:", Kscript("help", "--ansi").Out);
            Assert.Contains("\u001b[", Kscript("--ansi", "help", "serve").Out);
            Assert.DoesNotContain('\u001b', Kscript("--no-ansi", "--help").Out);
            Assert.False(KScript.Output.CliStyle.Override ?? true);
        }
        finally
        {
            KScript.Output.CliStyle.Override = null;
        }
    }

    [Fact]
    public void Docs_opens_the_browser_and_stops_on_ctrl_c()
    {
        using var stop = new CancellationTokenSource();
        string opened = null;
        var result = Run(new[] { "docs", "--port", TestServer.FreePort().ToString() }, (o, e) => new CommandLine
        {
            Out = o,
            Error = e,
            Cancellation = stop.Token,
            OpenBrowser = url =>
            {
                opened = url;
                stop.Cancel();
            },
        });
        Assert.Equal(0, result.Code);
        Assert.StartsWith("http://localhost:", opened);
        Assert.Contains("KScript docs: " + opened, result.Out);
    }

    [Fact]
    public void Every_subcommand_is_reached()
    {
        string script = Write("ok.ks.xml", "<kscript>\n  <echo>hi</echo>\n</kscript>\n");
        Assert.Contains("  kscript update ", Kscript("update", "--help").Out);
        Assert.Contains("kscript ide install", Kscript("ide", "help").Out);
        Assert.Contains("  kscript compile ", Kscript("compile", "--help").Out);
        Assert.Contains("  kscript docs ", Kscript("docs", "--help").Out);
        Assert.Contains("1 file checked: 0 errors", Kscript("check", script, "--warnaserror").Out);
        Assert.Contains("kscript:recommended", Kscript("check", "--print-config", dir).Out);
        Assert.Contains("1 file checked: all formatted", Kscript("fmt", script, "--check").Out);
        Assert.Equal(0, Kscript("css", Write("site.ks.css", "a { color: red; }")).Code);
        Assert.Equal(0, Kscript("make", "--dir", dir).Code);
        Assert.Contains("Created", Kscript("make:component", "Hero", "--dir", dir).Out);

        string test = Write("t.test.ks.xml", "<kscript><test name=\"adds\"><expect value=\"1 + 1\" equals=\"2\" /></test></kscript>");
        Assert.Equal(0, Kscript("test", test).Code);
        Assert.Equal(0, Kscript(test).Code); // a test file run directly runs its tests
        Assert.Equal(0, Kscript("test", test, "--env=ci").Code);
        Assert.Contains("  kscript worker ", Kscript("worker", "--help").Out);
        Assert.Contains("  kscript queue ", Kscript("queue", "--help").Out);
        Assert.Contains("Did you mean db migrate?", Kscript("db", "migrat").Err);
    }

    [Fact]
    public void The_language_server_talks_over_the_given_streams()
    {
        byte[] exit = Encoding.ASCII.GetBytes("Content-Length: 17\r\n\r\n{\"method\":\"exit\"}");
        var output = new MemoryStream();
        var (code, _, _) = Run(new[] { "lsp" }, (o, e) => new CommandLine { StandardInput = () => new MemoryStream(exit), StandardOutput = () => output });
        Assert.Equal(1, code); // exit without shutdown
    }

    [Fact]
    public void Scripts_run_with_their_project_and_environment()
    {
        Write("proj/kscript.json", "{ \"bogus\": 1, \"config\": { } }");
        Write("proj/config.yaml", "greeting: hello");
        Write("proj/config.staging.yaml", "greeting: hi from staging");
        string script = Write("proj/greet.ks.xml", "<kscript><echo>{ $config.greeting } { $args[0] }</echo></kscript>");
        var (code, output, error) = Kscript("--env", "staging", script, "Ann");
        Assert.True(code == 0, error);
        Assert.Contains("hi from staging Ann", output);
        Assert.Contains("unknown setting 'bogus'", error);
        Assert.Equal("staging", Environment.GetEnvironmentVariable("KSCRIPT_ENV"));

        Write("proj/kscript.json", "{ nope");
        Assert.Equal(1, Kscript(script).Code);
    }

    [Fact]
    public void Failing_missing_and_stopped_scripts()
    {
        var missing = Kscript(Path.Combine(dir, "nope.ks.xml"));
        Assert.Equal(2, missing.Code);
        Assert.Contains("There is no script at", missing.Err);

        string bad = Write("bad.ks.xml", "<kscript><throw message=\"boom\" /></kscript>");
        var failed = Kscript(bad);
        Assert.Equal(3, failed.Code);
        Assert.Contains("boom", failed.Err);
        Assert.DoesNotContain(dir, failed.Err);

        // An editor asks for the whole path of the failing line, to link to it.
        Environment.SetEnvironmentVariable(CommandLine.ErrorPathsVariable, "full");
        try
        {
            Assert.Contains($"ERROR  {bad}:1:10: ", Kscript(bad).Err);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CommandLine.ErrorPathsVariable, null);
        }

        string ok = Write("loop.ks.xml", "<kscript><let name=\"n\" value=\"0\" /><while test=\"true\"><set name=\"n\" value=\"$n + 1\" /></while></kscript>");
        var stopped = Run(new[] { ok }, (o, e) => new CommandLine { Cancellation = new CancellationToken(true) });
        Assert.Equal(130, stopped.Code);
    }

    [Fact]
    public void Script_names_are_matched_without_case_in_the_current_folder()
    {
        Write("Hello.ks.xml", "<kscript><echo>hello</echo></kscript>");
        string previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dir);
            Assert.Contains("hello", Kscript("hello.ks.xml").Out);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public void Ctrl_c_stops_once_then_lets_the_process_end()
    {
        using var cancel = new CancellationTokenSource();
        Assert.True(CommandLine.StopOnFirstCtrlC(cancel));
        Assert.True(cancel.IsCancellationRequested);
        Assert.False(CommandLine.StopOnFirstCtrlC(cancel));
    }

    [Fact]
    public void Serve_options_and_failures()
    {
        Assert.Contains("--port needs a number", Kscript("serve", "--port", "x").Err);
        Assert.Contains("--port needs a number", Kscript("serve", "-p").Err);
        Assert.Contains("--host needs a value", Kscript("serve", "--host").Err);
        Assert.Equal(1, Kscript("serve", Path.Combine(dir, "missing")).Code);

        Write("site/kscript.json", "{ nope");
        Assert.Equal(1, Kscript("serve", Path.Combine(dir, "site")).Code);

        // A port that is taken.
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        int port = ((IPEndPoint)taken.LocalEndpoint).Port;
        Write("site/kscript.json", $"{{ \"serve\": {{ \"port\": {port}, \"host\": \"127.0.0.1\" }}, \"bogus\": 1 }}");
        Write("site/index.html", "<h1>Hi</h1>");
        var busy = Kscript("serve", Path.Combine(dir, "site"));
        Assert.Equal(1, busy.Code);
        Assert.Contains("Could not start the server", busy.Err);
        Assert.Contains("unknown setting 'bogus'", busy.Err);
    }

    [Fact]
    public void Routes_and_convert()
    {
        Assert.Contains("  kscript convert ", Kscript("convert", "--help").Out);

        Write("site/kscript.json", "{ \"serve\": { \"root\": \"public\" } }");
        Write("site/public/routes.ks.xml", "<KScript.Routes><Get path=\"/hi\"><return value=\"'hi'\" /></Get></KScript.Routes>");
        var listed = Kscript("routes", Path.Combine(dir, "site"));
        Assert.True(listed.Code == 0, listed.Err);
        Assert.Contains("/hi", listed.Out);

        string cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.Combine(dir, "site"));
            Assert.Contains("/hi", Kscript("routes").Out);
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
        }

        Assert.Contains("Site folder not found", Kscript("routes", Path.Combine(dir, "nowhere")).Err);
        Write("broken/kscript.json", "{ nope");
        Assert.Equal(1, Kscript("routes", Path.Combine(dir, "broken")).Code);

        // A route file kscript.json names but that isn't there stops kscript serve.
        Write("missing/kscript.json", "{ \"http\": { \"routes\": [\"nope.ks.xml\"] } }");
        var serve = Kscript("serve", Path.Combine(dir, "missing"));
        Assert.Equal(1, serve.Code);
        Assert.Contains("there is no route file nope.ks.xml", serve.Err);
    }

    [Fact]
    public void Openapi_writes_the_document()
    {
        Write("site/api.kscript", "KScript.Routes {\n  Get(path: \"/hi\") {\n    return 'hi'\n  }\n}\n");
        string site = Path.Combine(dir, "site");

        var json = Kscript("openapi", site, "--json");
        Assert.True(json.Code == 0, json.Err);
        Assert.Contains("\"/api/hi\"", json.Out);
        var yaml = Kscript("openapi", site, "--yaml");
        Assert.True(yaml.Code == 0, yaml.Err);
        Assert.Contains("  /api/hi:\n", yaml.Out);

        // --out writes a file; a .yaml or .yml name means YAML.
        string file = Path.Combine(dir, "api.yml");
        var written = Kscript("openapi", site, "-o", file);
        Assert.True(written.Code == 0, written.Err);
        Assert.Contains("Wrote " + file + " (1 paths).", written.Out);
        Assert.StartsWith("openapi:", File.ReadAllText(file));
        Assert.True(Kscript("openapi", site, "--out", Path.Combine(dir, "api.json")).Code == 0);
        Assert.StartsWith("{", File.ReadAllText(Path.Combine(dir, "api.json")));

        Assert.Contains("--out needs a file name.", Kscript("openapi", site, "--out").Err);
        Assert.Contains("Unknown option '--nope' for kscript openapi", Kscript("openapi", "--nope").Err);
        Assert.Contains("Site folder not found", Kscript("openapi", Path.Combine(dir, "nowhere")).Err);
        Write("badproject/kscript.json", "{ nope");
        Assert.Equal(1, Kscript("openapi", Path.Combine(dir, "badproject")).Code);

        // No routes, a route file kscript.json names that isn't there, and a broken route file.
        Write("plain/index.html", "<h1>Hi</h1>");
        var none = Kscript("openapi", Path.Combine(dir, "plain"));
        Assert.Equal(1, none.Code);
        Assert.Contains("There are no route files in", none.Err);
        Write("missing/kscript.json", "{ \"http\": { \"routes\": [\"nope.ks.xml\"] } }");
        var missing = Kscript("openapi", Path.Combine(dir, "missing"));
        Assert.Equal(1, missing.Code);
        Assert.Contains("nope.ks.xml", missing.Err);
        Write("broken/api.kscript", "KScript.Routes {\n  Get(path: \"/hi\", to: nowhere.fn)\n}\n");
        var broken = Kscript("openapi", Path.Combine(dir, "broken"));
        Assert.Equal(1, broken.Code);
        Assert.NotEqual(string.Empty, broken.Err);
    }

    [Fact]
    public void Serve_runs_until_stopped()
    {
        Write("site/index.html", "<h1>Hi</h1>");
        Write("site/blog/index.html", "<h1>Blog</h1>");
        Write("site/kscript.json", "{ \"serve\": { \"port\": 0, \"host\": \"127.0.0.1\" } }");
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var (code, _, error) = Run(new[] { "serve", Path.Combine(dir, "site", "blog"), "--port", "0", "--host", "127.0.0.1" }, (o, e) => new CommandLine { Cancellation = stop.Token });
        Assert.True(code == 0, error);
    }

    [Fact]
    public void Serve_with_a_worker_runs_one_beside_the_server()
    {
        Write("site/index.html", "<h1>Hi</h1>");
        Write("site/kscript.json", "{ \"queue\": { \"default\": \"sync\" } }");
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var (code, _, error) = Run(new[] { "serve", Path.Combine(dir, "site"), "--port", "0", "--host", "127.0.0.1", "--with-worker" }, (o, e) => new CommandLine { Cancellation = stop.Token });
        Assert.True(code == 0, error);
        // The sync connection runs jobs as they are dispatched, so its worker stops straight away and says so.
        Assert.Contains("nothing for a worker to do", error);
        Assert.Contains("Worker: stopped.", error);
    }

    [Fact]
    public void Serve_stopped_before_it_has_started_is_a_normal_stop()
    {
        // On a slow machine Ctrl+C can come before the server has finished starting.
        Write("site/index.html", "<h1>Hi</h1>");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var (code, _, error) = Run(new[] { "serve", Path.Combine(dir, "site"), "--port", "0", "--host", "127.0.0.1" }, (o, e) => new CommandLine { Cancellation = stop.Token });
        Assert.True(code == 0, error);
    }

    [Fact]
    public void Routes_and_convert_are_reached()
    {
        Write("api/routes.ks.xml", "<KScript.Routes>\n  <Get path=\"/hi\" view=\"home\" name=\"hi\" />\n</KScript.Routes>\n");
        Write("api/kscript.json", "{ \"name\": \"api\" }");
        var (code, output, error) = Kscript("routes", Path.Combine(dir, "api"));
        Assert.True(code == 0, error);
        Assert.Contains("/hi", output);
        Assert.Contains("Site folder not found", Kscript("routes", Path.Combine(dir, "nowhere")).Err);
        Write("badproject/kscript.json", "{ nope");
        Assert.Equal(1, Kscript("routes", Path.Combine(dir, "badproject")).Code);
        Assert.NotEqual(0, Kscript("convert").Code);

        // A route file kscript.json names but that isn't there stops serve.
        Write("broken/kscript.json", "{ \"http\": { \"routes\": [ { \"file\": \"gone.ks.xml\" } ] } }");
        Write("broken/index.html", "<h1>Hi</h1>");
        (code, _, error) = Kscript("serve", Path.Combine(dir, "broken"), "--port", "0");
        Assert.Equal(1, code);
        Assert.Contains("gone.ks.xml", error);
    }

    [Fact]
    public void Compiled_programs_run_their_script_and_desktop_apps_show_errors_in_a_window()
    {
        string manifest = Write("app/kscript-app/kscript-app.json", "{ \"name\": \"Hello\", \"kind\": \"script\", \"main\": \"app/main.ks.xml\" }");
        Write("app/kscript-app/app/kscript.json", "{ \"packages\": [\"KScript.Math\"] }");
        Write("app/kscript-app/app/main.ks.xml", "<kscript><echo>{ @round(2.6) } { $args->join(',') }</echo></kscript>");
        var (code, output, _) = Compiled(manifest, "a", "b");
        Assert.Equal(0, code);
        Assert.Contains("3 a,b", output);

        Write("app/kscript-app/kscript-app.json", "{ \"name\": \"Hello\", \"kind\": \"window\", \"main\": \"app/main.ks.xml\" }");
        (code, output, _) = Compiled(manifest, "-psn_0_1", "a");
        Assert.Equal(0, code);
        Assert.Contains("3 a", output);

        Write("app/kscript-app/app/kscript.json", "{ nope");
        Write("app/kscript-app/app/main.ks.xml", "<kscript><throw message=\"broken\" /></kscript>");
        string shown = null;
        (code, _, string error) = Run(Array.Empty<string>(), (o, e) => new CommandLine { ShowError = (title, message) => shown = title + ": " + message }, manifest);
        Assert.Equal(1, code);
        Assert.Contains("broken", error);
        Assert.StartsWith("main.ks.xml: ", shown);
    }

    [Fact]
    public void Compiled_sites_are_served()
    {
        string manifest = Write("site/kscript-app/kscript-app.json", "{ \"name\": \"Site\", \"kind\": \"site\", \"main\": \"app\" }");
        Write("site/kscript-app/app/index.html", "<h1>Hi</h1>");
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var (code, output, error) = Run(new[] { "--port", "0", "--host", "127.0.0.1" }, (o, e) => new CommandLine { Cancellation = stop.Token }, manifest);
        Assert.True(code == 0, error);
    }

    [Fact]
    public void Compiled_programs_whose_files_are_gone_say_so()
    {
        var (code, _, error) = Compiled(null);
        Assert.Equal(1, code);
        Assert.Contains("files are missing", error);
    }

    [Fact]
    public void Compiled_programs_find_their_manifest_among_the_extracted_files()
    {
        string manifest = Write("x/kscript-app/kscript-app.json", "{}");
        string search = string.Join(Path.PathSeparator, Path.Combine(dir, "nothing"), Path.Combine(dir, "x"));
        Assert.Equal(Path.GetFullPath(manifest), CompiledApp.FindManifest("kscript-app/kscript-app.json", search));
        Assert.Null(CompiledApp.FindManifest("kscript-app/kscript-app.json", Path.Combine(dir, "nothing")));
        Assert.Null(CompiledApp.FindManifest(null, search));
        Assert.Null(CompiledApp.FindManifest("kscript-app/kscript-app.json", null));
        Assert.False(CompiledApp.IsCompiled);
        Assert.Null(CompiledApp.FindManifest());
    }

    private (int Code, string Out, string Err) Compiled(string manifest, params string[] args) => Run(args, null, manifest);
}
