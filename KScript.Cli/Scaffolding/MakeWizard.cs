using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using KScript.Output;

namespace KScript.Scaffolding
{
    /// <summary>
    /// The questions <c>kscript make &lt;type&gt;</c> asks at a terminal when no name is given (kscript make page): the
    /// name, then, when no options were given either, each of the type's options. A yes/no option (one written
    /// <c>[--timestamps]</c> in the template's usage) is a yes/no question; the rest take text, and Enter skips them.
    /// With -n, or when input or output isn't a terminal, nothing is asked and a missing name is an error.
    /// </summary>
    public static class MakeWizard
    {
        /// <summary>Asks the questions, adds the answered options to <paramref name="options"/>, and returns the name.</summary>
        public static string Ask(CliPrompt prompt, StubInfo type, Dictionary<string, string> options)
        {
            CliOutput o = prompt.Output;
            bool askOptions = options.Count == 0 && type.Options.Count > 0;
            o.NewLine();
            o.Heading($"  New {type.Type}");
            if (type.Description != null) o.Comment("  " + type.Description);
            if (askOptions) o.Comment($"  Press Enter to skip an option. kscript make {type.Type} --help lists them, to skip these questions next time.");
            o.NewLine();

            string name = prompt.Text($"What should the {type.Type} be called?", defaultValue: null,
                validate: answer => answer.Trim().Length == 0 ? "An answer is needed." : null);

            if (!askOptions) return name.Trim();
            foreach (StubOption option in type.Options)
            {
                string key = option.Name.TrimStart('-');
                string question = option.Description ?? key;
                if (IsFlag(type, key))
                {
                    if (prompt.Confirm(question, defaultValue: false)) options[key] = "true";
                    continue;
                }
                string answer = prompt.Text(question, required: false);
                if (answer.Length > 0) options[key] = answer;
            }
            return name.Trim();
        }

        /// <summary>True when the template's usage shows the option on its own, as in [--timestamps], not [--title "Title"].</summary>
        public static bool IsFlag(StubInfo type, string key) =>
            type.Usage != null && Regex.IsMatch(type.Usage, @"\[--" + Regex.Escape(key) + @"\]");
    }
}
