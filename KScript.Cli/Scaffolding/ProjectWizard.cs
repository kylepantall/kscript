using System;
using System.Collections.Generic;
using System.Linq;

using KScript.Output;

namespace KScript.Scaffolding
{
    /// <summary>
    /// The setup wizard of <c>kscript make project</c>: when it is run at a terminal with no options, it asks for the
    /// folder (if none was given), the kind of project, and each of that template's options, using the template's own
    /// questions, choices and defaults (kscript-template.json). With options, or with -n / --no-interaction, nothing
    /// is asked: what wasn't given takes its default.
    /// </summary>
    public static class ProjectWizard
    {
        /// <summary>The answers: the project folder and the options, as if they had been given on the command line.</summary>
        public sealed class Answers
        {
            public string Name { get; init; }
            public Dictionary<string, string> Options { get; init; }
        }

        /// <summary>Asks the questions. <paramref name="name"/> is the folder given on the command line, or null to ask.</summary>
        public static Answers Ask(CliPrompt prompt, string parent, string name)
        {
            CliOutput o = prompt.Output;
            o.NewLine();
            o.Heading("  New KScript project");
            o.Comment("  Answer each question, or press Enter for the default. kscript make project --help lists the options, to skip these questions next time.");
            o.NewLine();

            name ??= prompt.Text("What should the project folder be called?", validate: answer =>
                StubNames.Kebab(answer.Trim().Replace('\\', '/').TrimEnd('/').Split('/').Last()).Length == 0 ? "Use at least one letter or digit." : null);

            IReadOnlyList<string> names = ProjectTemplate.BuiltInNames();
            o.Comment("  Project templates:");
            foreach (string n in names) o.Comment($"    {n,-10} {Summary(ProjectTemplate.BuiltInTemplate(n).Description)}");
            string template = prompt.Select("What kind of project?", names, defaultValue: names[0]);

            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["template"] = template };
            foreach (ProjectTemplateOption option in ProjectTemplate.BuiltInTemplate(template).Options)
            {
                string question = option.Question ?? option.Description ?? option.Name;
                if (option.Choices.Count > 0)
                {
                    options[option.Name] = prompt.Select(question, option.Choices, defaultValue: option.Default);
                }
                else if (option.IsFlag)
                {
                    options[option.Name] = prompt.Confirm(question, defaultValue: option.Default == "true") ? "true" : "false";
                }
                else
                {
                    Func<string, string> check = option.Name == "port"
                        ? answer => int.TryParse(answer, out int p) && p > 0 && p < 65536 ? null : "Give a number from 1 to 65535."
                        : null;
                    options[option.Name] = prompt.Text(question, option.Default, check);
                }
            }
            o.NewLine();
            return new Answers { Name = name, Options = options };
        }

        /// <summary>The start of a template's description, up to its first colon: "A JSON API".</summary>
        internal static string Summary(string description)
        {
            if (string.IsNullOrEmpty(description)) return string.Empty;
            int colon = description.IndexOf(':');
            return colon > 0 ? description.Substring(0, colon) : description;
        }
    }
}
