using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Output;
using KScript.Projects;

namespace KScript.Scaffolding
{
    /// <summary>
    /// <c>kscript publish [model...]</c>: with no names, lists the models and mixins the packages ship and whether the
    /// project has its own copy; with names, copies each into the models folder (see <see cref="Publisher"/>).
    /// </summary>
    public static class PublishCommand
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            var names = new List<string>();
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool force = false, dryRun = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--force": force = true; continue;
                    case "--dry-run": dryRun = true; continue;
                    case "--syntax":
                        if (i + 1 >= args.Length) return Fail(error, "--syntax needs kscript or xml.");
                        options[ScriptFormat.Option] = args[++i];
                        continue;
                }
                if (a.StartsWith("--syntax=", StringComparison.Ordinal)) { options[ScriptFormat.Option] = a.Substring(9); continue; }
                if (a.StartsWith("-", StringComparison.Ordinal)) return Fail(error, $"Unknown option '{a}'. kscript publish takes model names, --syntax, --force and --dry-run.");
                names.Add(a);
            }

            string start = KScript.Runtime.WorkingFolder.Current;
            string file = ProjectConfig.Find(start);
            string root = file != null ? Path.GetDirectoryName(file) : start;
            ProjectConfig project = file != null ? ProjectConfig.Load(file) : null;
            var cli = new CliOutput(output);
            try
            {
                if (names.Count == 0)
                {
                    cli.Heading("Models the packages ship. kscript publish <name> copies one into your project to change:");
                    foreach (Publisher.Publishable item in Publisher.All())
                    {
                        string mine = Publisher.Existing(root, project, item);
                        cli.Line($"  {cli.Style.Bold(item.Model)} {cli.Style.Gray($"({item.Kind}, {item.Source.Package})")}  {item.Summary}"
                            + (mine != null ? cli.Style.Gray($" Yours: {mine}.") : string.Empty));
                    }
                    return 0;
                }
                if (file == null) return Fail(error, "kscript publish copies a model into a project's models folder, and there is no kscript.json here. Run it in a project, or make one with kscript make project.");
                var items = names.Select(Publisher.Find).Distinct().ToList();
                foreach (Publisher.Publishable item in items)
                {
                    string rel = Publisher.Publish(root, item, options, force, dryRun);
                    cli.Info($"{(dryRun ? "Would publish" : "Published")} {item.Model} to {cli.Style.Bold(rel)}. It replaces {item.Source.Package}'s {item.Model} in this project.");
                }
                return 0;
            }
            catch (StubException ex)
            {
                return Fail(error, ex.Message);
            }
        }

        private static int Fail(TextWriter error, string message)
        {
            new CliOutput(error).Error(message);
            return ExitCodes.Usage;
        }
    }
}
