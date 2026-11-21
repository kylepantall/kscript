using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KScript.Output
{
    /// <summary>
    /// The options of a command line: <c>--docker</c>, <c>--no-docker</c>, <c>--port=8080</c> or <c>--port 8080</c>,
    /// <c>-p 8080</c>, and the other (positional) arguments. A setup wizard reads what was given here and asks
    /// (with <see cref="CliPrompt"/>) only for the rest.
    /// </summary>
    public sealed class CliArgs
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        public List<string> Positional { get; } = new List<string>();

        /// <summary>The option names in the order given, without dashes.</summary>
        public List<string> Names { get; } = new List<string>();

        /// <summary>
        /// Parses <paramref name="args"/>. Options named in <paramref name="valued"/> take the next argument as their
        /// value when it isn't written with =; the rest are flags. <paramref name="aliases"/> maps short names (p) to
        /// long ones (port). Everything after "--" is positional.
        /// </summary>
        public static CliArgs Parse(IEnumerable<string> args, IEnumerable<string> valued = null, IReadOnlyDictionary<string, string> aliases = null)
        {
            var result = new CliArgs();
            var takesValue = new HashSet<string>(valued ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            List<string> list = args.ToList();
            bool rest = false;
            for (int i = 0; i < list.Count; i++)
            {
                string a = list[i];
                if (rest || a == "-" || !a.StartsWith("-", StringComparison.Ordinal))
                {
                    result.Positional.Add(a);
                    continue;
                }
                if (a == "--")
                {
                    rest = true;
                    continue;
                }
                string name = a.TrimStart('-');
                string value = null;
                int eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    value = name.Substring(eq + 1);
                    name = name.Substring(0, eq);
                }
                if (aliases != null && aliases.TryGetValue(name, out string full)) name = full;
                if (value == null && takesValue.Contains(name) && i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal)) value = list[++i];
                if (value == null && name.StartsWith("no-", StringComparison.Ordinal) && !takesValue.Contains(name))
                {
                    name = name.Substring(3);
                    value = "false";
                }
                result.values[name] = value ?? "true";
                if (!result.Names.Contains(name)) result.Names.Add(name);
            }
            return result;
        }

        public bool Has(string name) => values.ContainsKey(name);

        /// <summary>The option's value, or null when it wasn't given.</summary>
        public string Value(string name) => values.TryGetValue(name, out string v) ? v : null;

        /// <summary>true for --name, false for --no-name (or --name=no), null when neither was given.</summary>
        public bool? Flag(string name)
        {
            if (!values.TryGetValue(name, out string v)) return null;
            return !(v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("no", StringComparison.OrdinalIgnoreCase) || v == "0" || v.Equals("off", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The options given that aren't in <paramref name="known"/>, to report as unknown.</summary>
        public IEnumerable<string> Unknown(IEnumerable<string> known)
        {
            var set = new HashSet<string>(known, StringComparer.Ordinal);
            return Names.Where(n => !set.Contains(n));
        }
    }

    /// <summary>
    /// Questions for a setup wizard, styled like the rest of the CLI: text, yes/no, a choice from a list, and secrets.
    /// Each takes the answer from <see cref="CliArgs"/> when the option was given, so the same command works from flags
    /// (for scripts and CI) or as a wizard. When input isn't a terminal, the default is used (or the question fails
    /// with a message naming the option to pass).
    /// </summary>
    public sealed class CliPrompt
    {
        private readonly TextReader input;
        private readonly Func<ConsoleKeyInfo> readKey;

        public CliPrompt(CliOutput output, TextReader input = null, bool? interactive = null, Func<ConsoleKeyInfo> readKey = null)
        {
            Output = output;
            this.input = input ?? Console.In;
            Interactive = interactive ?? (input == null && !Console.IsInputRedirected && !Console.IsOutputRedirected);
            this.readKey = readKey ?? (() => Console.ReadKey(true));
        }

        public CliOutput Output { get; }

        /// <summary>True when a person can answer; otherwise defaults are taken.</summary>
        public bool Interactive { get; }

        private CliStyle Style => Output.Style;

        private string Question(string question, string hint) =>
            $"  {Style.Green("?")} {Style.Bold(question)}{(string.IsNullOrEmpty(hint) ? string.Empty : " " + Style.Gray(hint))} ";

        private string ReadLine()
        {
            string line = input.ReadLine();
            if (line == null) throw new CliPromptException("The input ended before the question was answered.");
            return line;
        }

        private void Answered(string question, string answer)
        {
            // Piped answers aren't echoed by a terminal, so the transcript shows them.
            if (!Interactive) Output.Line();
        }

        /// <summary>
        /// Asks for text. <paramref name="validate"/> returns an error message, or null when the answer is fine. When
        /// <paramref name="required"/> is false, Enter with no default skips the question and returns "".
        /// </summary>
        public string Text(string question, string defaultValue = null, Func<string, string> validate = null, string option = null, CliArgs args = null, bool required = true)
        {
            if (option != null && args?.Value(option) is string given && given != "true")
            {
                string problem = validate?.Invoke(given);
                if (problem != null) throw new CliPromptException($"--{option}: {problem}");
                return given;
            }
            if (!Interactive && defaultValue != null) return defaultValue;
            if (!Interactive && !required) return string.Empty;
            if (!Interactive && option != null) throw new CliPromptException($"{question} Pass --{option}=<value>.");
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Output.Writer.Write(Question(question, defaultValue != null ? $"({defaultValue})" : required ? null : "(Enter to skip)"));
                string answer = ReadLine().Trim();
                Answered(question, answer);
                if (answer.Length == 0 && defaultValue != null) answer = defaultValue;
                if (answer.Length == 0 && !required) return string.Empty;
                string problem = answer.Length == 0 ? "An answer is needed." : validate?.Invoke(answer);
                if (problem == null) return answer;
                Output.Line($"    {Style.Red("✗ " + problem)}");
            }
            throw new CliPromptException($"No valid answer to '{question}'.");
        }

        /// <summary>Asks a yes/no question.</summary>
        public bool Confirm(string question, bool defaultValue = false, string option = null, CliArgs args = null)
        {
            if (option != null && args?.Flag(option) is bool given) return given;
            if (!Interactive) return defaultValue;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Output.Writer.Write(Question(question, defaultValue ? "(Y/n)" : "(y/N)"));
                string answer = ReadLine().Trim().ToLowerInvariant();
                Answered(question, answer);
                if (answer.Length == 0) return defaultValue;
                if (answer is "y" or "yes") return true;
                if (answer is "n" or "no") return false;
                Output.Line($"    {Style.Red("✗ Answer yes or no.")}");
            }
            return defaultValue;
        }

        /// <summary>
        /// Asks for one of <paramref name="choices"/>: by number or name from a numbered list, or with the arrow keys and
        /// Enter at a terminal. Returns the chosen item.
        /// </summary>
        public string Select(string question, IReadOnlyList<string> choices, string defaultValue = null, string option = null, CliArgs args = null)
        {
            if (choices == null || choices.Count == 0) throw new ArgumentException("Select needs at least one choice.", nameof(choices));
            int start = Math.Max(0, defaultValue == null ? 0 : IndexOf(choices, defaultValue));
            if (option != null && args?.Value(option) is string given && given != "true")
            {
                int found = IndexOf(choices, given);
                if (found < 0) throw new CliPromptException($"--{option} should be one of: {string.Join(", ", choices)}.");
                return choices[found];
            }
            if (!Interactive) return choices[start];
            if (Style.Enabled && input == Console.In) return SelectWithKeys(question, choices, start);

            Output.Line(Question(question, null).TrimEnd());
            for (int i = 0; i < choices.Count; i++) Output.Line($"    {Style.Gray($"{i + 1})")} {choices[i]}{(i == start ? Style.Gray(" (default)") : string.Empty)}");
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Output.Writer.Write($"    {Style.Gray("›")} ");
                string answer = ReadLine().Trim();
                Answered(question, answer);
                if (answer.Length == 0) return choices[start];
                if (int.TryParse(answer, out int n) && n >= 1 && n <= choices.Count) return choices[n - 1];
                int named = IndexOf(choices, answer);
                if (named >= 0) return choices[named];
                Output.Line($"    {Style.Red($"✗ Pick 1 to {choices.Count}, or type a name.")}");
            }
            return choices[start];
        }

        private string SelectWithKeys(string question, IReadOnlyList<string> choices, int selected)
        {
            Output.Line(Question(question, "(↑/↓ and Enter)").TrimEnd());
            void Draw()
            {
                for (int i = 0; i < choices.Count; i++)
                    Output.Line(i == selected ? $"    {Style.Green("›")} {Style.Green(choices[i])}" : $"      {choices[i]}");
            }
            Draw();
            while (true)
            {
                ConsoleKeyInfo key = readKey();
                if (key.Key == ConsoleKey.Enter) return choices[selected];
                if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.K) selected = (selected + choices.Count - 1) % choices.Count;
                else if (key.Key == ConsoleKey.DownArrow || key.Key == ConsoleKey.J) selected = (selected + 1) % choices.Count;
                else if (key.KeyChar >= '1' && key.KeyChar <= '9' && key.KeyChar - '1' < choices.Count) selected = key.KeyChar - '1';
                else continue;
                Output.Writer.Write($"\u001b[{choices.Count}A");
                Draw();
            }
        }

        /// <summary>Asks for a secret, such as a password, without showing what is typed at a terminal.</summary>
        public string Secret(string question, string option = null, CliArgs args = null)
        {
            if (option != null && args?.Value(option) is string given && given != "true") return given;
            if (!Interactive) throw new CliPromptException(option != null ? $"{question} Pass --{option}=<value>." : $"{question} needs a terminal.");
            Output.Writer.Write(Question(question, null));
            var text = new System.Text.StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = readKey();
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; continue; }
                if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
            }
            Output.Line();
            return text.ToString();
        }

        private static int IndexOf(IReadOnlyList<string> choices, string value)
        {
            for (int i = 0; i < choices.Count; i++)
                if (string.Equals(choices[i], value?.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }

    /// <summary>A question that couldn't be answered: input ended, or an option's value doesn't fit.</summary>
    public sealed class CliPromptException : Exception
    {
        public CliPromptException(string message) : base(message) { }
    }
}
