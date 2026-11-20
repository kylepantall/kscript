using System.Globalization;

using KScript.Runtime;
using KScript.Terminal;

namespace KScript.Tests;

/// <summary>A terminal with a person at it: key presses come from a script, and clearing or measuring can fail.</summary>
internal sealed class FakeTerminalHost : KScriptHost
{
    private readonly Queue<ConsoleKeyInfo> keys = new Queue<ConsoleKeyInfo>();

    public FakeTerminalHost(StringWriter output, string input = "") : base(output, output, new StringReader(input), interactive: true)
    {
    }

    public bool ClearFails { get; set; }

    public bool WidthFails { get; set; }

    public int Clears { get; private set; }

    public FakeTerminalHost Type(string text)
    {
        foreach (char ch in text)
        {
            keys.Enqueue(ch switch
            {
                '\n' => new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
                '\b' => new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
                '\t' => new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false),
                _ => new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false),
            });
        }
        return this;
    }

    public override ConsoleKeyInfo ReadKey() => keys.Count > 0 ? keys.Dequeue() : throw new InvalidOperationException("No keys left.");

    public override void ClearScreen()
    {
        if (ClearFails) throw new IOException("Not a terminal.");
        Clears++;
    }

    public override int TerminalWidth => WidthFails ? throw new IOException("Not a terminal.") : 120;
}

public class TerminalTests
{
    private static (string Output, FakeTerminalHost Host) Terminal(string body, string input = "", string keys = "", Action<FakeTerminalHost> setup = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = new FakeTerminalHost(output, input).Type(keys);
        setup?.Invoke(host);
        new Interpreter(host).RunText("<kscript><import package=\"KScript.Terminal\" />" + body + "</kscript>");
        return (output.ToString(), host);
    }

    private static string Piped(string body, string input = "") => ScriptRunner.RunBody(body, input, "KScript.Terminal");

    private static KScriptException Fails(string body, string input = "") => Assert.Throws<KScriptException>(() => Piped(body, input));

    [Fact]
    public void Secret_input_reads_keys_without_showing_them()
    {
        var (output, _) = Terminal("<input to=\"pw\" secret=\"yes\">Password</input><echo>[$pw]</echo>", keys: "ab\bc\td\n");
        Assert.Equal("Password\n[acd]\n", output);
    }

    [Fact]
    public void Clear_pause_and_width_use_the_terminal()
    {
        var (output, host) = Terminal("<clear /><pause /><echo>{ @terminal_width() } { @is_terminal() }</echo>", keys: "x");
        Assert.Equal(1, host.Clears);
        Assert.Equal("Press any key to continue...\n120 true\n", output);

        var (fallback, _) = Terminal("<clear /><pause>Go</pause><echo>{ @terminal_width() }</echo>", input: "\n", setup: h => { h.ClearFails = true; h.WidthFails = true; });
        Assert.Equal("\u001b[2J\u001b[HGo\n80\n", fallback);

        Assert.Equal("Press any key to continue...\n", Piped("<clear /><pause />", "\n"));
        Assert.Equal("80\n", Piped("<echo>{ @terminal_width() }</echo>"));
    }

    [Fact]
    public void Styles_are_ansi_codes_at_a_terminal()
    {
        if (Environment.GetEnvironmentVariable("NO_COLOR") != null) return;
        var (output, _) = Terminal("<echo>{ @color('a', 'red') }{ @bold('b') }{ @dim('c') }{ @italic('d') }{ @underline('e') }</echo>");
        Assert.Equal("\u001b[31ma\u001b[0m\u001b[1mb\u001b[0m\u001b[2mc\u001b[0m\u001b[3md\u001b[0m\u001b[4me\u001b[0m\n", output);
        Assert.Contains("Unknown colour 'puce'", Fails("<echo>{ @color('a', 'puce') }</echo>").Message);
        Assert.Contains("bright_red", Ansi.ColourNames);
        Assert.Null(Ansi.Colour(null));
    }

    [Fact]
    public void Input_converts_and_asks_again()
    {
        Assert.Equal("N: \nPlease enter a number.\nN: \n2.5\n", Piped("<input to=\"n\" type=\"float\">N:</input><echo>$n</echo>", "x\n2.5\n"));
        Assert.Contains("Say yes or no", Piped("<input to=\"b\" type=\"bool\" error=\"Say yes or no\">OK?</input><echo>$b</echo>", "maybe\nyes\n"));
        Assert.Contains("Please enter yes or no.", Piped("<input to=\"b\" type=\"bool\" /><echo>$b</echo>", "maybe\nno\n"));
        Assert.EndsWith("7\n", Piped("<input to=\"n\" type=\"int\" default=\"7\">N</input><echo>$n</echo>", ""));
        Assert.EndsWith("plain\n", Piped("<input to=\"s\" /><echo>$s</echo>", "plain\n"));
    }

    [Fact]
    public void Picker_asks_until_the_path_exists()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-picker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
            string output = Piped($"<picker to=\"f\" type=\"file\">File?</picker><echo>$f</echo>", $"\n{dir}/missing.txt\n\"{dir}/a.txt\"\n");
            Assert.Contains("There is no file at ''. Try again.", output);
            Assert.Contains("There is no file at '" + dir + "/missing.txt'", output);
            Assert.EndsWith(Path.Combine(dir, "a.txt") + "\n", output);

            Assert.EndsWith(Path.Combine(dir, "new") + "\n", Piped($"<picker to=\"f\" must-exist=\"no\" /><echo>$f</echo>", $"{dir}/new\n"));
            Assert.Contains("Nope", Piped($"<picker to=\"f\" error=\"Nope\" /><echo>$f</echo>", $"{dir}/zzz\n{dir}\n"));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert.EndsWith(Path.GetFullPath(home) + "\n", Piped("<picker to=\"f\" /><echo>$f</echo>", "~\n"));
            Assert.Contains("reached the end of the input", Fails("<picker to=\"f\" />").Message);
            Assert.Contains("No valid path after 100 tries", Fails("<picker to=\"f\" />", string.Concat(Enumerable.Repeat($"{dir}/nope\n", 100))).Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Menus_take_lists_of_maps_and_maps()
    {
        Assert.EndsWith("b-value\n", Piped("<menu to=\"c\" options=\"[{ label: 'A', v: 1 }, { label: 'B', v: 2 }]\" /><echo>{ $c.v == 2 ? 'b-value' : 'other' }</echo>", "B\n"));
        Assert.EndsWith("2\n", Piped("<menu to=\"c\" options=\"{ First: 1, Second: 2 }\" /><echo>$c</echo>", "9\nsecond\n"));
        Assert.Contains("Please choose a number from 1 to 2.", Piped("<menu to=\"c\" options=\"['a', 'b']\" /><echo>$c</echo>", "9\n1\n"));
        Assert.Contains("needs a list or a map", Fails("<menu to=\"c\" options=\"5\" />").Message);
        Assert.Contains("has no options", Fails("<menu to=\"c\" options=\"[]\" />").Message);
        Assert.Contains("reached the end of the input", Fails("<menu to=\"c\" options=\"['a']\" />").Message);
        Assert.Contains("No valid choice after 100 tries", Fails("<menu to=\"c\" options=\"['a']\" />", string.Concat(Enumerable.Repeat("x\n", 100))).Message);
    }

    [Fact]
    public void Confirm_takes_defaults_and_asks_again()
    {
        Assert.Equal("Are you sure? [Y/n] true\n", Piped("<confirm to=\"ok\" default=\"yes\" /><echo>$ok</echo>"));
        Assert.Contains("[y/N]", Piped("<confirm to=\"ok\" default=\"no\" /><echo>$ok</echo>"));
        Assert.Contains("Please answer y or n.", Piped("<confirm to=\"ok\" /><echo>$ok</echo>", "hmm\n\nn\n"));
        Assert.Contains("reached the end of the input", Fails("<confirm to=\"ok\" />").Message);
        Assert.Contains("No valid answer after 100 tries", Fails("<confirm to=\"ok\" />", string.Concat(Enumerable.Repeat("x\n", 100))).Message);
    }

    [Fact]
    public void Markdown_renders_every_kind_of_block_as_plain_text()
    {
        string markdown = string.Join("\n",
            "# Title",
            "## Section",
            "Text with `code`, *em*, **strong**, ~~gone~~, [a link](https://a.example), [https://b.example](https://b.example),",
            "<https://c.example>, ![alt](i.png), ![](j.png), &amp; and <b>html</b> and $x^2$.  ",
            "After a break.",
            "",
            "3. three",
            "4. four",
            "",
            "- one",
            "",
            "  more about one",
            "- [x] done",
            "- [ ] todo",
            "",
            "> quoted",
            "> lines",
            "",
            "```",
            "fenced",
            "```",
            "",
            "    indented",
            "",
            "---",
            "",
            "| A | Long header |",
            "|---|---|",
            "| 1 | 2 |",
            "| 3 |",
            "",
            "<div>",
            "block html",
            "</div>",
            "",
            ":::note",
            "Inside a container.",
            ":::",
            "",
            "Term",
            ":   Definition");
        string text = TerminalMarkdown.Render(markdown, colour: false);
        Assert.Equal(string.Join("\n",
            "Title",
            "",
            "Section",
            "",
            "Text with code, em, strong, gone, a link (https://a.example), https://b.example,",
            "https://c.example, [alt], [image], & and html and x^2.",
            "After a break.",
            "",
            "3. three",
            "4. four",
            "",
            "• one",
            "  more about one",
            "• [x] done",
            "• [ ] todo",
            "",
            "│ quoted",
            "│ lines",
            "",
            "    fenced",
            "",
            "    indented",
            "",
            new string('─', 40),
            "",
            "A  Long header",
            "─  ───────────",
            "1  2",
            "3",
            "",
            "<div>",
            "block html",
            "</div>",
            "",
            "Inside a container.",
            "",
            "Term",
            "",
            "Definition"), text);
        Assert.Equal(string.Empty, TerminalMarkdown.Render((string)null, colour: false));

        // Inline containers the renderer doesn't know still show their text.
        var paragraph = new Markdig.Syntax.ParagraphBlock { Inline = new Markdig.Syntax.Inlines.ContainerInline() };
        var wrapper = new Markdig.Syntax.Inlines.ContainerInline();
        wrapper.AppendChild(new Markdig.Syntax.Inlines.LiteralInline("wrapped"));
        paragraph.Inline.AppendChild(wrapper);
        var doc = new Markdig.Syntax.MarkdownDocument { paragraph };
        Assert.Equal("wrapped", TerminalMarkdown.Render(doc, colour: false));
    }

    [Fact]
    public void Markdown_in_colour_marks_up_headings_code_and_links()
    {
        string text = TerminalMarkdown.Render("# T\n\n## S\n\n`c` [l](u)\n\n> q\n\n| A |\n|---|\n| 1 |\n\n---", colour: true);
        Assert.Contains("\u001b[1;4mT\u001b[0m", text);
        Assert.Contains("\u001b[1mS\u001b[0m", text);
        Assert.Contains("\u001b[36mc\u001b[0m \u001b[4ml\u001b[0m \u001b[90m(u)\u001b[0m", text);
        Assert.Contains("\u001b[90m│ \u001b[0mq", text);
        Assert.Contains("\u001b[1mA\u001b[0m", text);
    }
}
