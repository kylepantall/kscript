using KScript.Output;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>KScript.Output: the artisan-style output every kscript command and KScript.Terminal writes through.</summary>
public class CliOutputTests
{
    private static (CliOutput Cli, StringWriter Text) Plain(int width = 60)
    {
        var text = new StringWriter { NewLine = "\n" };
        return (new CliOutput(text, colour: false, width: width), text);
    }

    private static (CliOutput Cli, StringWriter Text) Coloured(int width = 60)
    {
        var text = new StringWriter { NewLine = "\n" };
        return (new CliOutput(text, colour: true, width: width), text);
    }

    [Fact]
    public void Labels_line_up_later_lines_under_the_message()
    {
        var (cli, text) = Plain();
        cli.Info("Created [a.kscript].");
        cli.Success("Done.");
        cli.Warn("Careful.");
        cli.Error("Broken.\nSecond line.");
        Assert.Equal("   INFO  Created [a.kscript].\n   DONE  Done.\n   WARN  Careful.\n   ERROR  Broken.\n          Second line.\n", text.ToString());
    }

    [Fact]
    public void Colours_wrap_labels_and_bold_bracketed_parts()
    {
        var (cli, text) = Coloured();
        cli.Error("Missing [x.csv].");
        Assert.Equal("  \u001b[1;97;41m ERROR \u001b[0m Missing \u001b[1m[x.csv]\u001b[0m.\n", text.ToString());
        Assert.Equal("Missing [x.csv].", CliStyle.Strip("Missing \u001b[1m[x.csv]\u001b[0m."));
    }

    [Fact]
    public void Long_labels_wrap_only_at_a_terminal()
    {
        string message = string.Join(" ", Enumerable.Repeat("word", 30));
        var (plain, plainText) = Plain(40);
        plain.Info(message);
        Assert.Single(plainText.ToString().TrimEnd('\n').Split('\n'));

        var (coloured, colouredText) = Coloured(40);
        coloured.Info(message);
        string[] lines = CliStyle.Strip(colouredText.ToString()).TrimEnd('\n').Split('\n');
        Assert.True(lines.Length > 1);
        Assert.All(lines, l => Assert.True(l.Length <= 40, l));
        Assert.All(lines.Skip(1), l => Assert.StartsWith("         word", l));
    }

    [Fact]
    public void Two_columns_fill_the_line_with_dots()
    {
        var (cli, text) = Plain(40);
        cli.TwoColumn("Version", "1.2");
        cli.TwoColumn("Name");
        cli.TwoColumn(new string('x', 50), "y");
        string[] lines = text.ToString().Split('\n');
        Assert.Equal("  Version " + new string('.', 25) + " 1.2", lines[0]);
        Assert.Equal(39, lines[0].Length);
        Assert.Equal("  Name " + new string('.', 32), lines[1]);
        Assert.EndsWith(" .. y", lines[2]);
    }

    [Fact]
    public void Tasks_show_their_time_and_result()
    {
        var (cli, text) = Plain(50);
        Assert.True(cli.Task("Copying", () => true));
        Assert.False(cli.Task("Checking", () => false));
        Assert.Throws<InvalidOperationException>(() => cli.Task("Breaking", () => throw new InvalidOperationException()));
        cli.TaskLine("Slow", true, TimeSpan.FromSeconds(2.5));
        string[] lines = text.ToString().TrimEnd('\n').Split('\n');
        Assert.Matches(@"^  Copying \.+ \d+ms DONE$", lines[0]);
        Assert.Matches(@"^  Checking \.+ \d+ms FAIL$", lines[1]);
        Assert.Matches(@"^  Breaking \.+ \d+ms FAIL$", lines[2]);
        Assert.Matches(@"^  Slow \.+ 2\.5s DONE$", lines[3]);

        // At a terminal the description shows first and the rest of the line follows.
        var (coloured, colouredText) = Coloured(50);
        coloured.Task("Copying", () => true);
        Assert.Matches(@"^  Copying \S+\.+\S+ \S+\d+ms\S+ \S+DONE\S+\n$", colouredText.ToString());
    }

    [Fact]
    public void Tables_have_borders_and_line_up_styled_cells()
    {
        var (cli, text) = Plain();
        cli.Table(new[] { "Queue", "Waiting" }, new[] { new[] { "default", "3" }, new[] { "mail", "12" } }, new[] { CliAlign.Left, CliAlign.Right });
        Assert.Equal(
            "  ┌─────────┬─────────┐\n" +
            "  │ Queue   │ Waiting │\n" +
            "  ├─────────┼─────────┤\n" +
            "  │ default │       3 │\n" +
            "  │ mail    │      12 │\n" +
            "  └─────────┴─────────┘\n", text.ToString());

        var (coloured, colouredText) = Coloured();
        coloured.Table(new[] { "A" }, new[] { new[] { coloured.Style.Red("x"), "extra" } });
        string stripped = CliStyle.Strip(colouredText.ToString());
        Assert.Contains("│ A │       │", stripped);
        Assert.Contains("│ x │ extra │", stripped);
    }

    [Fact]
    public void Lists_headings_and_definitions()
    {
        var (cli, text) = Plain();
        cli.Heading("Options:");
        cli.Definitions(new[] { ("--port", "The port."), ("-v", "More.") });
        cli.Definitions(Array.Empty<(string, string)>());
        cli.Bullets(new[] { "a", "b" });
        cli.Comment("note");
        cli.Line();
        Assert.Equal("Options:\n  --port  The port.\n  -v      More.\n  ⇂ a\n  ⇂ b\nnote\n\n", text.ToString());

        var (coloured, colouredText) = Coloured(40);
        coloured.Definitions(new[] { ("name", string.Join(" ", Enumerable.Repeat("word", 12))) });
        string[] lines = CliStyle.Strip(colouredText.ToString()).TrimEnd('\n').Split('\n');
        Assert.True(lines.Length > 1);
        Assert.StartsWith("        word", lines[1]);
    }

    [Fact]
    public void Styles_pad_by_visible_width()
    {
        var style = new CliStyle(true);
        string red = style.Red("ab");
        Assert.Equal(2, CliStyle.VisibleLength(red));
        Assert.Equal(red + "  ", CliStyle.Pad(red, 4));
        Assert.Equal("  " + red, CliStyle.PadLeft(red, 4));
        Assert.Equal(string.Empty, style.Bold(null));
        foreach (Func<string, string> f in new Func<string, string>[] { style.Dim, style.Italic, style.Underline, style.Green, style.Yellow, style.Blue, style.Magenta, style.Cyan, style.Gray })
            Assert.StartsWith("\u001b[", f("x"));
        Assert.Equal("x", CliStyle.Plain.Bold("x"));
    }

    [Fact]
    public void Colour_follows_ansi_flags_then_force_color_then_no_color()
    {
        string force = Environment.GetEnvironmentVariable("FORCE_COLOR"), no = Environment.GetEnvironmentVariable("NO_COLOR"), term = Environment.GetEnvironmentVariable("TERM");
        try
        {
            Environment.SetEnvironmentVariable("FORCE_COLOR", null);
            Environment.SetEnvironmentVariable("NO_COLOR", null);
            Environment.SetEnvironmentVariable("TERM", null);
            var text = new StringWriter();
            Assert.False(CliStyle.Supports(text)); // not the console
            Assert.False(CliStyle.Supports(null));
            Assert.False(CliStyle.For(text).Enabled);
            Assert.True(CliStyle.Allowed(interactive: true));
            Assert.False(CliStyle.Allowed(interactive: false));

            CliStyle.Override = true;
            Assert.True(CliStyle.Supports(text));
            Assert.True(CliStyle.Allowed(false));
            CliStyle.Override = false;
            Assert.False(CliStyle.Allowed(true));
            CliStyle.Override = null;

            Environment.SetEnvironmentVariable("FORCE_COLOR", "1");
            Assert.True(CliStyle.Supports(text));
            Assert.True(CliStyle.Allowed(false));
            Environment.SetEnvironmentVariable("FORCE_COLOR", "0");
            Assert.False(CliStyle.Supports(text));
            Assert.False(CliStyle.Allowed(true));
            Environment.SetEnvironmentVariable("FORCE_COLOR", null);

            Environment.SetEnvironmentVariable("NO_COLOR", "1");
            Assert.False(CliStyle.Supports(Console.Out));
            Assert.False(CliStyle.Allowed(true));
            Environment.SetEnvironmentVariable("NO_COLOR", null);

            Environment.SetEnvironmentVariable("TERM", "dumb");
            Assert.False(CliStyle.Supports(Console.Out));
            Assert.False(CliStyle.Allowed(true));
            Environment.SetEnvironmentVariable("TERM", null);

            // Tests redirect the console, so even Console.Out and Console.Error are plain here.
            Assert.False(CliStyle.Supports(Console.Out));
            Assert.False(CliStyle.Supports(Console.Error));
        }
        finally
        {
            CliStyle.Override = null;
            Environment.SetEnvironmentVariable("FORCE_COLOR", force);
            Environment.SetEnvironmentVariable("NO_COLOR", no);
            Environment.SetEnvironmentVariable("TERM", term);
        }
    }

    [Fact]
    public void Width_comes_from_columns_or_defaults_to_80()
    {
        string columns = Environment.GetEnvironmentVariable("COLUMNS");
        try
        {
            Environment.SetEnvironmentVariable("COLUMNS", "120");
            Assert.Equal(120, new CliOutput(new StringWriter()).Width);
            Environment.SetEnvironmentVariable("COLUMNS", null);
            Assert.Equal(80, new CliOutput(new StringWriter()).Width);
            Assert.Equal(80, new CliOutput(new StringWriter(), colour: true).Width > 0 ? 80 : 0);
            Assert.Equal(40, new CliOutput(null, width: 10).Width);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLUMNS", columns);
        }
    }

    [Fact]
    public void Args_parse_flags_values_aliases_and_positionals()
    {
        var args = CliArgs.Parse(new[] { "my-app", "--docker", "--no-auth", "--port=3000", "-d", "postgres", "--cache", "off", "--", "--literal" },
            valued: new[] { "database" }, aliases: new Dictionary<string, string> { ["d"] = "database" });
        Assert.Equal(new[] { "my-app", "off", "--literal" }, args.Positional);
        Assert.True(args.Flag("docker"));
        Assert.False(args.Flag("auth"));
        Assert.Null(args.Flag("missing"));
        Assert.Equal("3000", args.Value("port"));
        Assert.Equal("postgres", args.Value("database"));
        Assert.True(args.Has("cache"));
        Assert.Null(args.Value("missing"));
        Assert.Equal(new[] { "port" }, args.Unknown(new[] { "docker", "auth", "database", "cache" }));
        Assert.False(CliArgs.Parse(new[] { "--x=no" }).Flag("x"));
        Assert.Equal(new[] { "-" }, CliArgs.Parse(new[] { "-", "--x", "--x" }).Positional);
    }

    private static CliPrompt Prompt(string input, out StringWriter text, bool interactive = true)
    {
        text = new StringWriter { NewLine = "\n" };
        return new CliPrompt(new CliOutput(text, colour: false), new StringReader(input), interactive);
    }

    [Fact]
    public void Prompts_ask_for_text_and_check_it()
    {
        var prompt = Prompt("\nab\nabc\n", out StringWriter text);
        Assert.True(prompt.Interactive);
        Assert.Equal("abc", prompt.Text("Name?", validate: a => a.Length < 3 ? "Too short." : null));
        Assert.Contains("  ? Name? ", text.ToString());
        Assert.Contains("✗ An answer is needed.", text.ToString());
        Assert.Contains("✗ Too short.", text.ToString());

        Assert.Equal("8080", Prompt("\n", out _).Text("Port?", "8080"));
        var given = CliArgs.Parse(new[] { "--port=3000" });
        Assert.Equal("3000", Prompt("", out _).Text("Port?", "8080", option: "port", args: given));
        Assert.Throws<CliPromptException>(() => Prompt("", out _).Text("Port?", validate: _ => "Nope.", option: "port", args: given));
        Assert.Equal("8080", Prompt("", out _, interactive: false).Text("Port?", "8080"));
        Assert.Contains("--name=<value>", Assert.Throws<CliPromptException>(() => Prompt("", out _, interactive: false).Text("Name?", option: "name")).Message);
        Assert.Throws<CliPromptException>(() => Prompt("", out _).Text("Name?"));
        Assert.Throws<CliPromptException>(() => Prompt(string.Concat(Enumerable.Repeat("x\n", 100)), out _).Text("Name?", validate: _ => "No."));
    }

    [Fact]
    public void Prompts_confirm()
    {
        Assert.True(Prompt("maybe\ny\n", out StringWriter text).Confirm("Docker?"));
        Assert.Contains("(y/N)", text.ToString());
        Assert.Contains("Answer yes or no.", text.ToString());
        Assert.False(Prompt("no\n", out _).Confirm("Docker?", true));
        Assert.True(Prompt("\n", out _).Confirm("Docker?", true));
        Assert.True(Prompt("", out _, interactive: false).Confirm("Docker?", true));
        Assert.False(Prompt("", out _).Confirm("Docker?", true, "docker", CliArgs.Parse(new[] { "--no-docker" })));
        Assert.True(Prompt(string.Concat(Enumerable.Repeat("x\n", 100)), out _).Confirm("Docker?", true));
    }

    [Fact]
    public void Prompts_select_by_number_or_name()
    {
        string[] dbs = { "sqlite", "postgres", "mysql" };
        Assert.Equal("postgres", Prompt("9\n2\n", out StringWriter text).Select("Database?", dbs));
        Assert.Contains("1) sqlite (default)", text.ToString());
        Assert.Contains("Pick 1 to 3", text.ToString());
        Assert.Equal("mysql", Prompt("MySQL\n", out _).Select("Database?", dbs));
        Assert.Equal("postgres", Prompt("\n", out _).Select("Database?", dbs, "postgres"));
        Assert.Equal("mysql", Prompt("", out _, interactive: false).Select("Database?", dbs, "mysql"));
        Assert.Equal("sqlite", Prompt("", out _).Select("Database?", dbs, option: "database", args: CliArgs.Parse(new[] { "--database=SQLite" })));
        Assert.Throws<CliPromptException>(() => Prompt("", out _).Select("Database?", dbs, option: "database", args: CliArgs.Parse(new[] { "--database=oracle" })));
        Assert.Throws<ArgumentException>(() => Prompt("", out _).Select("Database?", Array.Empty<string>()));
        Assert.Equal("sqlite", Prompt(string.Concat(Enumerable.Repeat("x\n", 100)), out _).Select("Database?", dbs));
    }

    [Fact]
    public void Prompts_select_with_arrow_keys_and_read_secrets()
    {
        var keys = new Queue<ConsoleKeyInfo>(new[]
        {
            new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false),
            new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false),
            new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
        });
        var text = new StringWriter();
        TextReader stdin = Console.In;
        var prompt = new CliPrompt(new CliOutput(text, colour: true), Console.In, interactive: true, readKey: () => keys.Dequeue());
        Assert.Equal("c", prompt.Select("Pick", new[] { "a", "b", "c" }));
        Assert.Contains("\u001b[3A", text.ToString());

        var typed = new Queue<ConsoleKeyInfo>("ab\bc\u0001\r".Select(ch => new ConsoleKeyInfo(ch, ch == '\r' ? ConsoleKey.Enter : ch == '\b' ? ConsoleKey.Backspace : ConsoleKey.A, false, false, false)));
        var secret = new CliPrompt(new CliOutput(new StringWriter(), colour: false), new StringReader(""), interactive: true, readKey: () => typed.Dequeue());
        Assert.Equal("ac", secret.Secret("Password?"));
        Assert.Equal("s3cret", secret.Secret("Password?", "password", CliArgs.Parse(new[] { "--password=s3cret" })));
        var piped = new CliPrompt(new CliOutput(new StringWriter(), colour: false), new StringReader(""), interactive: false);
        Assert.Contains("--password", Assert.Throws<CliPromptException>(() => piped.Secret("Password?", "password")).Message);
        Assert.Contains("needs a terminal", Assert.Throws<CliPromptException>(() => piped.Secret("Password?")).Message);
        Assert.Same(stdin, Console.In);
    }

    [Fact]
    public void Prompts_default_to_the_console()
    {
        var prompt = new CliPrompt(new CliOutput(new StringWriter(), colour: false));
        Assert.False(prompt.Interactive); // tests redirect the console
    }
}

/// <summary>KScript.Terminal's &lt;info&gt;, &lt;table&gt;, &lt;task&gt; and friends: the CLI's style for scripts.</summary>
public class TerminalOutputTests
{
    private static string Run(string body) => ScriptRunner.RunBody(body, "", "KScript.Terminal");

    [Fact]
    public void Labels_headings_details_and_bullets()
    {
        string output = Run("<heading>Report</heading><detail name=\"People\">2</detail><bullets items=\"['a', 'b']\" />" +
            "<info>Wrote [r.txt].</info><success>Done.</success><warn>Careful.</warn><alert>Plain.</alert><alert type=\"success\">Ok.</alert><alert type=\"warn\">Hm.</alert>");
        Assert.Contains("Report\n", output);
        Assert.Matches(@"  People \.+ 2\n", output);
        Assert.Contains("  ⇂ a\n  ⇂ b\n", output);
        Assert.Contains("   INFO  Wrote [r.txt].\n", output);
        Assert.Contains("   DONE  Done.\n", output);
        Assert.Contains("   WARN  Careful.\n", output);
        Assert.Contains("   INFO  Plain.\n", output);
        Assert.Contains("   DONE  Ok.\n", output);
        Assert.Contains("   WARN  Hm.\n", output);
    }

    [Fact]
    public void Errors_go_to_stderr()
    {
        var text = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var host = new KScriptHost(text, error);
        new Interpreter(host).RunText("<kscript><import package=\"KScript.Terminal\" /><alert type=\"error\">Broken [x].</alert></kscript>");
        Assert.Equal(string.Empty, text.ToString());
        Assert.Contains("   ERROR  Broken [x].", error.ToString());
        Assert.Contains("type=\"loud\" isn't one of", Assert.Throws<KScriptException>(() => Run("<alert type=\"loud\">x</alert>")).Message);
        Assert.Contains("items needs a list", Assert.Throws<KScriptException>(() => Run("<bullets items=\"3\" />")).Message);
        Assert.Contains("rows should be a list of rows", Assert.Throws<KScriptException>(() => Run("<table rows=\"3\" />")).Message);
    }

    [Fact]
    public void Tables_from_maps_and_lists()
    {
        string maps = Run("<table rows=\"[{ name: 'Joan', age: 82 }, { name: 'Sam', age: 9 }]\" />");
        Assert.Contains("│ name │ age │", maps);
        Assert.Contains("│ Joan │  82 │", maps);
        Assert.Contains("│ Sam  │   9 │", maps);

        string chosen = Run("<table rows=\"[{ name: 'Joan', age: 82 }]\" columns=\"{ Age: 'age' }\" align=\"['left']\" />");
        Assert.Contains("│ Age │", chosen);
        Assert.Contains("│ 82  │", chosen);

        // A list of lists: the first row holds the headings, as in a document's Table.
        string lists = Run("<table rows=\"[['a', 1], ['b'], 'c']\" />");
        Assert.Contains("│ a │ 1 │\n  ├", lists);
        Assert.Contains("│ b │   │", lists);
        Assert.Contains("│ c │   │", lists);
        Assert.Contains("│ x │", Run("<table rows=\"[{ x: null }]\" />"));
        Assert.Contains("┌┐", Run("<table rows=\"[]\" />"));
    }

    [Fact]
    public void Tasks_run_their_body()
    {
        Assert.Matches(@"inside\n  Copying \.+ \d+ms DONE\n", Run("<task name=\"Copying\"><echo>inside</echo></task>"));
        var text = new StringWriter { NewLine = "\n" };
        var host = new KScriptHost(text);
        Assert.Throws<KScriptException>(() => new Interpreter(host).RunText("<kscript><import package=\"KScript.Terminal\" /><task name=\"Breaking\"><throw>no</throw></task></kscript>"));
        Assert.Matches(@"  Breaking \.+ \d+ms FAIL", text.ToString());
    }

    [Fact]
    public void A_terminal_gets_colours_at_its_width()
    {
        var text = new StringWriter { NewLine = "\n" };
        var host = new FakeTerminalHost(text);
        string force = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", null);
            new Interpreter(host).RunText("<kscript><import package=\"KScript.Terminal\" /><info>Hi</info></kscript>");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", force);
        }
        Assert.Contains("\u001b[1;97;44m INFO \u001b[0m Hi", text.ToString());

        var narrow = new StringWriter { NewLine = "\n" };
        var failing = new FakeTerminalHost(narrow) { WidthFails = true };
        new Interpreter(failing).RunText("<kscript><import package=\"KScript.Terminal\" /><detail name=\"a\">b</detail></kscript>");
        Assert.Equal(79, CliStyle.Strip(narrow.ToString()).TrimEnd('\n').Length);
    }
}
