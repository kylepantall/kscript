using System.Text;

using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Terminal
{
    /// <summary>
    /// KScript.Terminal: asking questions and printing nicely in a terminal. &lt;input&gt;, &lt;picker&gt;,
    /// &lt;menu&gt;, &lt;confirm&gt;, &lt;clear&gt;, &lt;pause&gt;, colours, and <c>&lt;echo format="markdown"&gt;</c>.
    /// </summary>
    public sealed class TerminalPackage : KScriptPackage
    {
        public override string Name => "KScript.Terminal";

        public override string Description => "Prompts, menus, colours and Markdown output for terminal scripts.";

        protected override void Register(PackageBuilder p)
        {
            p.Element(new ElementInfo("input", "Asks for a line of input and stores it. The element text is the prompt. With `type`, the answer is converted and asked again until it fits.", ContentKind.Template,
                new A("to", K.DeclareVariable, "The variable that receives the answer.", true),
                new A("type", K.Enum, "Convert the answer: string (the default), int, number or bool.", false, "string", "int", "number", "float", "bool"),
                new A("default", K.Expression, "The value used when the answer is empty (or input has ended)."),
                new A("secret", K.Bool, "yes hides what is typed, for passwords."),
                new A("error", K.Template, "What to say when the answer doesn't fit the type."))
            {
                Context = ElementContext.Script,
                HtmlWins = true,
                Example = "<input to=\"name\">What's your name? </input>\n<input to=\"age\" type=\"int\">Age: </input>",
            }, Input);

            p.Element(new ElementInfo("picker", "Asks for a file or folder path, and asks again until it exists.", ContentKind.Template,
                new A("to", K.DeclareVariable, "The variable that receives the path.", true),
                new A("type", K.Enum, "What to ask for: folder (the default) or file.", false, "folder", "file"),
                new A("must-exist", K.Bool, "no accepts paths that don't exist yet."),
                new A("error", K.Template, "What to say when the path doesn't exist."))
            {
                Context = ElementContext.Script,
                Example = "<picker to=\"folder\">Which folder? </picker>",
            }, Picker);

            p.Element(new ElementInfo("menu", "Shows a numbered list and stores the chosen item. The element text is the title.", ContentKind.Template,
                new A("to", K.DeclareVariable, "The variable that receives the chosen item.", true),
                new A("options", K.Expression, "The choices: a list, or a map (the key is shown, the value is stored).", true),
                new A("index", K.DeclareVariable, "A variable for the chosen position, from 0."))
            {
                Context = ElementContext.Script,
                HtmlWins = true,
                Example = "<menu to=\"choice\" options=\"['Settings', 'Exit']\">What next?</menu>",
            }, Menu);

            p.Element(new ElementInfo("confirm", "Asks a yes/no question and stores true or false.", ContentKind.Template,
                new A("to", K.DeclareVariable, "The variable that receives the answer.", true),
                new A("default", K.Bool, "The answer when nothing is typed."))
            {
                Context = ElementContext.Script,
                Example = "<confirm to=\"sure\">Delete the files?</confirm>",
            }, Confirm);

            p.Element(new ElementInfo("clear", "Clears the terminal.", ContentKind.Empty) { Context = ElementContext.Script }, run =>
            {
                if (run.Host.IsInteractive)
                {
                    try { run.Host.ClearScreen(); }
                    catch (IOException) { run.Host.Write("\u001b[2J\u001b[H"); }
                }
                return null;
            });

            p.Element(new ElementInfo("pause", "Waits for a key press (or Enter when input is piped). The element text is the prompt.", ContentKind.Template)
            {
                Context = ElementContext.Script,
                Example = "<pause>Press any key to continue...</pause>",
            }, run =>
            {
                string prompt = run.HasContent ? run.Text() : "Press any key to continue...";
                run.Host.Write(prompt);
                if (run.Host.IsInteractive)
                {
                    try { run.Host.ReadKey(); }
                    catch (InvalidOperationException) { run.Host.ReadLine(); }
                }
                else
                {
                    run.Host.ReadLine();
                }
                run.Host.WriteLine(string.Empty);
                return null;
            });

            p.Command("color", "text, colour", "Text in a colour: red, green, yellow, blue, magenta, cyan, white, gray (or a bright_ one). Plain text when the output isn't a terminal.", KType.String,
                c => Ansi.Style(c.Host, c.Str(0), Ansi.Colour(c.Str(1)) ?? throw c.Error($"Unknown colour '{c.Str(1)}'. Use one of: {string.Join(", ", Ansi.ColourNames)}.", "Argument")),
                "<echo>{ @color('Done', 'green') }</echo>");
            p.Command("bold", "text", "Bold text.", KType.String, c => Ansi.Style(c.Host, c.Str(0), "1"));
            p.Command("dim", "text", "Dimmed text.", KType.String, c => Ansi.Style(c.Host, c.Str(0), "2"));
            p.Command("italic", "text", "Italic text.", KType.String, c => Ansi.Style(c.Host, c.Str(0), "3"));
            p.Command("underline", "text", "Underlined text.", KType.String, c => Ansi.Style(c.Host, c.Str(0), "4"));
            p.Command("terminal_width", "", "The width of the terminal in characters (80 when it isn't a terminal).", KType.Number, c =>
            {
                try { return c.Host.IsInteractive ? (double)c.Host.TerminalWidth : 80d; }
                catch (IOException) { return 80d; }
            });

            p.Command("is_terminal", "", "yes when a person is at a terminal, no when input or output is redirected (a pipe, a file, a test). Use it to skip animations and prompts.", KType.Bool,
                c => c.Host.IsInteractive, "<if @is_terminal()><clear /></if>");

            p.EchoFormat("markdown", (text, interpreter) => TerminalMarkdown.Render(text, Ansi.Enabled(interpreter.Host)));
        }

        private static string Prompt(ElementRun run)
        {
            string prompt = run.HasContent ? run.Text() : string.Empty;
            if (prompt.Length > 0 && !char.IsWhiteSpace(prompt[^1]) && !prompt.EndsWith(":", StringComparison.Ordinal) && !prompt.EndsWith("?", StringComparison.Ordinal)) return prompt;
            return prompt.Length > 0 && !char.IsWhiteSpace(prompt[^1]) ? prompt + " " : prompt;
        }

        private static string Read(ElementRun run, bool secret)
        {
            if (secret && run.Host.IsInteractive)
            {
                var sb = new StringBuilder();
                while (true)
                {
                    ConsoleKeyInfo key = run.Host.ReadKey();
                    if (key.Key == ConsoleKey.Enter) break;
                    if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
                    if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
                }
                run.Host.WriteLine(string.Empty);
                return sb.ToString();
            }
            string line = run.Host.ReadLine();
            if (!run.Host.IsInteractive && line != null && run.Host.Out != TextWriter.Null) run.Host.WriteLine(string.Empty);
            return line;
        }

        private static bool? Input(ElementRun run)
        {
            string name = run.VariableName("to");
            string type = run.Raw("type")?.Trim() ?? "string";
            string prompt = Prompt(run);
            bool hasDefault = run.Has("default");
            for (int attempt = 0; ; attempt++)
            {
                run.Host.Write(prompt);
                string answer = Read(run, run.Flag("secret"));
                object value;
                if (answer == null || answer.Length == 0 && hasDefault)
                {
                    if (!hasDefault) throw run.Error($"<input to=\"{name}\"> reached the end of the input with no answer. Give it a default=\"...\".", null, "Input");
                    run.Scope.Declare(name, run.Eval("default"));
                    return null;
                }

                try
                {
                    value = Values.ConvertTo(answer, type == "float" ? "number" : type, "The answer");
                    run.Scope.Declare(name, value);
                    return null;
                }
                catch (KScriptException) when (attempt < 100)
                {
                    run.Host.WriteLine(run.Template("error") ?? $"Please enter {(type == "int" ? "a whole number" : type == "bool" ? "yes or no" : "a number")}.");
                }
            }
        }

        private static bool? Picker(ElementRun run)
        {
            string name = run.VariableName("to");
            bool file = (run.Raw("type")?.Trim() ?? "folder") == "file";
            bool mustExist = run.Flag("must-exist", true);
            string prompt = Prompt(run);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                run.Host.Write(prompt);
                string answer = Read(run, false) ?? throw run.Error($"<picker to=\"{name}\"> reached the end of the input with no answer.", null, "Input");
                string path = answer.Trim().Trim('"');
                if (path.StartsWith("~", StringComparison.Ordinal)) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
                if (path.Length > 0) path = Path.GetFullPath(Path.Combine(run.Host.WorkingDirectory, path));
                if (path.Length > 0 && (!mustExist || (file ? File.Exists(path) : Directory.Exists(path))))
                {
                    run.Scope.Declare(name, path);
                    return null;
                }
                run.Host.WriteLine(run.Template("error") ?? $"There is no {(file ? "file" : "folder")} at '{answer.Trim()}'. Try again.");
            }
            throw run.Error("No valid path after 100 tries.", null, "Input");
        }

        private static bool? Menu(ElementRun run)
        {
            string name = run.VariableName("to");
            object options = run.Eval("options");
            List<(string Label, object Value)> choices = options switch
            {
                List<object> list => list.Select(x => (x is Dictionary<string, object> m && m.TryGetValue("label", out object l) ? Values.ToStr(l) : Values.ToStr(x), x)).ToList(),
                Dictionary<string, object> map => map.Select(kv => (kv.Key, kv.Value)).ToList(),
                _ => throw run.Error($"options needs a list or a map, but got {Values.Describe(options)}.", "options", "Type"),
            };
            if (choices.Count == 0) throw run.Error("The menu has no options.", "options", "Argument");

            string title = run.HasContent ? run.Text() : null;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (title != null) run.Host.WriteLine(title);
                for (int i = 0; i < choices.Count; i++) run.Host.WriteLine($"  {i + 1}. {choices[i].Label}");
                run.Host.Write("> ");
                string answer = Read(run, false) ?? throw run.Error($"<menu to=\"{name}\"> reached the end of the input with no answer.", null, "Input");
                answer = answer.Trim();
                int index = -1;
                if (int.TryParse(answer, out int n) && n >= 1 && n <= choices.Count) index = n - 1;
                else index = choices.FindIndex(c => string.Equals(c.Label, answer, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    run.Scope.Declare(name, choices[index].Value);
                    if (run.Has("index")) run.Scope.Declare(run.VariableName("index"), (double)index);
                    return null;
                }
                run.Host.WriteLine($"Please choose a number from 1 to {choices.Count}.");
            }
            throw run.Error("No valid choice after 100 tries.", null, "Input");
        }

        private static bool? Confirm(ElementRun run)
        {
            string name = run.VariableName("to");
            bool? fallback = run.Has("default") ? run.Flag("default") : null;
            string hint = fallback == true ? "[Y/n]" : fallback == false ? "[y/N]" : "[y/n]";
            string prompt = run.HasContent ? run.Text() : "Are you sure?";
            for (int attempt = 0; attempt < 100; attempt++)
            {
                run.Host.Write($"{prompt} {hint} ");
                string answer = Read(run, false);
                if (answer == null)
                {
                    if (fallback == null) throw run.Error($"<confirm to=\"{name}\"> reached the end of the input with no answer.", null, "Input");
                    run.Scope.Declare(name, fallback.Value);
                    return null;
                }
                if (answer.Trim().Length == 0 && fallback != null)
                {
                    run.Scope.Declare(name, fallback.Value);
                    return null;
                }
                if (Values.ParseBool(answer) is bool b && answer.Trim().Length > 0)
                {
                    run.Scope.Declare(name, b);
                    return null;
                }
                run.Host.WriteLine("Please answer y or n.");
            }
            throw run.Error("No valid answer after 100 tries.", null, "Input");
        }
    }

    /// <summary>ANSI colours and styles, used only when the output is an interactive terminal (and NO_COLOR isn't set).</summary>
    public static class Ansi
    {
        private static readonly Dictionary<string, string> Colours = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = "30", ["red"] = "31", ["green"] = "32", ["yellow"] = "33", ["blue"] = "34", ["magenta"] = "35", ["cyan"] = "36", ["white"] = "37",
            ["gray"] = "90", ["grey"] = "90", ["bright_red"] = "91", ["bright_green"] = "92", ["bright_yellow"] = "93", ["bright_blue"] = "94",
            ["bright_magenta"] = "95", ["bright_cyan"] = "96", ["bright_white"] = "97",
        };

        public static IEnumerable<string> ColourNames => Colours.Keys;

        public static string Colour(string name) => Colours.TryGetValue((name ?? string.Empty).Trim(), out string code) ? code : null;

        public static bool Enabled(KScriptHost host) => host.UseColour;

        public static string Style(KScriptHost host, string text, string code) => Enabled(host) ? Wrap(text, code) : text;

        public static string Wrap(string text, string code) => $"\u001b[{code}m{text}\u001b[0m";
    }
}
