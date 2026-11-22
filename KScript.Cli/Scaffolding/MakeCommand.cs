using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Output;

namespace KScript.Scaffolding
{
    /// <summary>
    /// The command line for <c>kscript make</c>:
    /// <code>
    /// kscript make                          list the templates
    /// kscript make component Hero --props title,image
    /// kscript make page "people/[id]"
    /// kscript make templates [type…]        copy the built-in templates to _templates/ to customise them
    /// kscript make templates project         copy the project template to _templates/project (or ~/.kscript/templates/project with --global)
    /// kscript make project my-app           a new web app (pages, a live page, tests) with a desktop app unless --no-desktop
    /// </code>
    /// Inside a project (a folder with kscript.json, or below one), files are made relative to the project's site root,
    /// wherever the command is run from.
    /// Options: --dir &lt;project&gt;, --force, --dry-run; any other --key value (or --key=value, or a bare --flag) is
    /// passed to the template as {{ key }}.
    /// </summary>
    public static class MakeCommand
    {
        /// <summary>
        /// Makes the prompts the project setup wizard asks with, for the output it writes to. Tests replace it to
        /// give answers; by default it asks at a terminal, and when input or output is redirected nothing is asked.
        /// </summary>
        public static Func<TextWriter, CliPrompt> Prompts { get; set; } = writer => new CliPrompt(new CliOutput(writer),
            interactive: ReferenceEquals(writer, Console.Out) && !Console.IsInputRedirected && !Console.IsOutputRedirected);

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            bool noInteraction = false;
            var positional = new List<string>();
            var positionalAt = new HashSet<int>();
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string root = null;
            bool force = false, dryRun = false, help = false;

            // kscript make project: a yes/no option (--docker) never takes the next word unless it is yes/no/true/false,
            // so kscript make project --docker shop names the folder shop.
            HashSet<string> flags = args.FirstOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal)) is string first
                && string.Equals(first, "project", StringComparison.OrdinalIgnoreCase) ? ProjectFlags() : new HashSet<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-h" || a == "--help") { help = true; continue; }
                if (a == "-n" || a == "--no-interaction") { noInteraction = true; continue; }
                if (!a.StartsWith("--", StringComparison.Ordinal) || a.Length == 2)
                {
                    positional.Add(a);
                    positionalAt.Add(i);
                    continue;
                }

                if (CliFlags.Retired("make", a) is string renamed) return Fail(error, renamed);
                string key = a.Substring(2), value = null;
                int eq = key.IndexOf('=');
                if (eq >= 0) { value = key.Substring(eq + 1); key = key.Substring(0, eq); }

                switch (key.ToLowerInvariant())
                {
                    case "force": force = true; continue;
                    case "dry-run": dryRun = true; continue;
                    case "dir":
                        value ??= i + 1 < args.Length ? args[++i] : null;
                        if (string.IsNullOrEmpty(value)) return Fail(error, "--dir needs a folder, e.g. --dir \"Example Sites/team\"");
                        root = value;
                        continue;
                }

                // --no-desktop turns an option off, and never takes the next word as its value.
                if (value == null && key.StartsWith("no-", StringComparison.OrdinalIgnoreCase) && key.Length > 3)
                {
                    options[key.Substring(3)] = "false";
                    continue;
                }

                if (value == null)
                {
                    // --flag on its own (or followed by another option) is true.
                    bool takes = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                        && (!flags.Contains(key) || args[i + 1].ToLowerInvariant() is "true" or "false" or "yes" or "no");
                    value = takes ? args[++i] : "true";
                    if (flags.Contains(key) && value.ToLowerInvariant() is "yes") value = "true";
                    else if (flags.Contains(key) && value.ToLowerInvariant() is "no") value = "false";
                }
                options[key] = value;
            }

            try
            {
                string type = positional.Count > 0 ? positional[0] : null;

                if (string.Equals(type, "project", StringComparison.OrdinalIgnoreCase))
                {
                    if (root != null && !Directory.Exists(root)) return Fail(error, $"The folder '{root}' does not exist.");
                    if (help) { PrintProjectHelp(root ?? ".", options, output); return 0; }
                    if (positional.Count > 2) return Fail(error, $"Unexpected '{positional[2]}'. Put names with spaces in quotes.");
                    string folder = positional.Count > 1 ? positional[1] : null;
                    // No options at a terminal: the setup wizard asks for them. Any option (or -n) means no questions.
                    if (options.Count == 0 && !noInteraction)
                    {
                        CliPrompt prompt = Prompts(output);
                        if (prompt.Interactive)
                        {
                            ProjectWizard.Answers answers = ProjectWizard.Ask(prompt, root ?? ".", folder);
                            folder = answers.Name;
                            foreach (var kv in answers.Options) options[kv.Key] = kv.Value;
                        }
                    }
                    if (folder == null) return Fail(error, "Give the project a folder name, e.g. kscript make project my-site (or . for the current folder).");
                    return MakeProject(root ?? ".", folder, options, force, dryRun, output);
                }

                bool fromProject = root == null;
                root ??= ProjectRoot();
                if (!Directory.Exists(root))
                {
                    return Fail(error, $"The folder '{root}' does not exist.");
                }

                if (options.ContainsKey("list") && positional.Count == 0)
                {
                    PrintDirectory(root, output);
                    return 0;
                }
                if (help && positional.Count == 1 && !string.Equals(type, "templates", StringComparison.OrdinalIgnoreCase))
                {
                    PrintType(Scaffolder.FindTemplate(root, type), output);
                    return 0;
                }
                if (help || positional.Count == 0)
                {
                    PrintTemplates(root, output);
                    return 0;
                }

                if (string.Equals(type, "templates", StringComparison.OrdinalIgnoreCase)
                    && positional.Skip(1).Any(p => string.Equals(p, "project", StringComparison.OrdinalIgnoreCase)))
                {
                    var rest = positional.Skip(1).Where(p => !string.Equals(p, "project", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (rest.Count > 1 || positional.Count(p => string.Equals(p, "project", StringComparison.OrdinalIgnoreCase)) > 1 || !string.Equals(positional[1], "project", StringComparison.OrdinalIgnoreCase))
                    {
                        return Fail(error, $"Copy one project template at a time: kscript make templates project [{string.Join("|", ProjectTemplate.BuiltInNames())}]");
                    }
                    string which = rest.Count == 1 ? rest[0] : options.ContainsKey("bare") ? "bare" : ProjectTemplate.DefaultName;
                    return PublishProjectTemplate(fromProject ? null : root, which, options.ContainsKey("global"), force, output);
                }

                if (string.Equals(type, "templates", StringComparison.OrdinalIgnoreCase))
                {
                    var written = Scaffolder.PublishTemplates(root, force, positional.Skip(1));
                    var made = new CliOutput(output);
                    if (written.Count == 0) made.Info($"Nothing to copy: {Scaffolder.TemplatesFolder}/ already has those templates. Use --force to replace them.");
                    foreach (var w in written) made.Info($"Created {made.Style.Bold(w)}");
                    if (written.Count > 0) made.Comment($"  Edit them to change what kscript make creates, or add new ones (e.g. {Scaffolder.TemplatesFolder}/card.html for kscript make card …).");
                    return 0;
                }

                if (string.Equals(type, "auth", StringComparison.OrdinalIgnoreCase))
                {
                    if (positional.Count > 1) return Fail(error, $"kscript make auth takes no name (unexpected '{positional[1]}'). Options: --api, --syntax, --force, --dry-run.");
                    AuthResult made = AuthMaker.Make(root, options, force, dryRun);
                    var cli = new CliOutput(output);
                    foreach (var (action, path) in made.Files)
                    {
                        string where = fromProject && root != "." ? Path.GetRelativePath(KScript.Runtime.WorkingFolder.Current, Path.Combine(Path.GetFullPath(root), path)).Replace('\\', '/') : path;
                        cli.Info($"{(dryRun ? "Would: " + action.ToLowerInvariant() : action)} {cli.Style.Bold(where)}");
                    }
                    NextSteps(cli, made.Next);
                    return 0;
                }

                if (positional.Count < 2)
                {
                    // No name at a terminal: ask for it (and, when no options were given, for the type's options).
                    var t = Scaffolder.FindTemplate(root, type);
                    CliPrompt prompt = noInteraction ? null : Prompts(output);
                    if (prompt == null || !prompt.Interactive)
                        return Fail(error, $"Give the {t.Type} a name, e.g. kscript make {t.Type} {Scaffolder.Example(t.Type)}.\nAt a terminal, kscript make {t.Type} asks for it.");
                    positional.Add(MakeWizard.Ask(prompt, t, options));
                }
                if (positional.Count > 2)
                {
                    return Fail(error, TooManyNames(root, positional, args.Where((_, i) => !positionalAt.Contains(i))));
                }

                if (string.Equals(Scaffolder.FindTemplate(root, type).Type, "route", StringComparison.OrdinalIgnoreCase))
                {
                    var route = RouteMaker.Make(new MakeRequest { Type = "route", Name = positional[1], Root = root, Options = options, DryRun = dryRun });
                    var cli = new CliOutput(output);
                    foreach (var (action, path) in route.Files)
                    {
                        string where = fromProject && root != "." ? Path.GetRelativePath(KScript.Runtime.WorkingFolder.Current, Path.Combine(Path.GetFullPath(root), path)).Replace('\\', '/') : path;
                        cli.Info($"{(dryRun ? "Would " + (action == "Created" ? "create" : "add" + action.Substring("Added".Length)) : action)} {cli.Style.Bold(where)}");
                    }
                    cli.NewLine();
                    cli.Line("  " + cli.Style.Cyan(route.Route.Replace("\n", "\n  ")));
                    cli.NewLine();
                    cli.Comment("  " + route.Hint);
                    return 0;
                }

                var result = Scaffolder.Make(new MakeRequest { Type = type, Name = positional[1], Root = root, Options = options, Force = force, DryRun = dryRun });
                // In a project, files are made from its site root; show them relative to where the command was run.
                string shown = !fromProject || root == "." ? result.RelativePath : Path.GetRelativePath(KScript.Runtime.WorkingFolder.Current, result.FullPath).Replace('\\', '/');

                var file = new CliOutput(output);
                if (dryRun)
                {
                    file.Info($"Would create {file.Style.Bold(shown)}{(result.Overwrote ? " (replacing it)" : "")}:");
                    output.WriteLine();
                    output.Write(result.Content);
                    if (!result.Content.EndsWith('\n')) output.WriteLine();
                }
                else
                {
                    file.Info($"{(result.Overwrote ? "Replaced" : "Created")} {file.Style.Bold(shown)}{(result.Template.BuiltIn ? "" : $" (from {Scaffolder.TemplatesFolder}/{Path.GetFileName(result.Template.FilePath)})")}");
                }
                // A new page that uses the default _layout.html, in a folder without one, gets one too: otherwise the page
                // renders without <html>/<head>, kscript check warns, and editors don't see the folder as a site.
                var layout = Scaffolder.MakeDefaultLayoutIfMissing(root, result, options, dryRun);
                if (layout != null)
                {
                    file.Info(dryRun ? $"Would also create {file.Style.Bold(layout.RelativePath)}, the layout pages use by default." : $"Created {file.Style.Bold(layout.RelativePath)}, the layout pages use by default");
                }
                if (!string.IsNullOrEmpty(result.Hint)) file.Comment("  " + result.Hint);
                return 0;
            }
            catch (KScript.Projects.ProjectConfigException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return ExitCodes.Usage;
            }
            catch (StubException ex)
            {
                // What was asked for can't be made: an unknown type, a name that won't do, a file that is already there.
                KScript.Runtime.ErrorReport.Write(error, ex);
                return ExitCodes.Usage;
            }
            catch (CliPromptException ex)
            {
                return Fail(error, ex.Message);
            }
            catch (IOException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return 1;
            }
            catch (UnauthorizedAccessException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return 1;
            }
        }

        /// <summary>
        /// Explains a second name, e.g. <c>kscript make markdown page about</c>: a template type given twice is dropped from
        /// the suggested command, and other words are suggested in quotes.
        /// </summary>
        private static string TooManyNames(string root, List<string> positional, IEnumerable<string> options)
        {
            string type = positional[0];
            var names = positional.Skip(1).ToList();
            var known = Scaffolder.ListTemplates(root).Select(t => t.Type).Concat(new[] { "md", "c" }).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var notTypes = names.Where(n => !known.Contains(n)).ToList();

            string suggestedName = notTypes.Count == 1 ? notTypes[0] : string.Join(" ", names);
            var rest = options.Select(o => o.StartsWith("--", StringComparison.Ordinal) ? o : Quote(o));
            string suggestion = string.Join(" ", new[] { "kscript make", type, Quote(suggestedName) }.Concat(rest));

            string given = string.Join(" and ", names.Select(n => $"'{n}'"));
            string why = notTypes.Count == 1 && names.Count == 2
                ? $"'{names.First(known.Contains)}' is a type too, but only one type can be given."
                : "Put a name with spaces in quotes.";
            return $"kscript make {type} takes one name, but got {given}. {why}\nDid you mean: {suggestion}";
        }

        private static string Quote(string arg) =>
            arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\'' || c == '[' || c == ']' || c == '*') ? arg : $"\"{arg.Replace("\"", "\\\"")}\"";

        /// <summary>The site root of the project the current folder is in, or "." outside a project.</summary>
        private static string ProjectRoot()
        {
            var project = KScript.Projects.ProjectConfig.FindAndLoad(KScript.Runtime.WorkingFolder.Current);
            if (project == null) return ".";
            string root = project.SiteRoot;
            return Path.GetFullPath(".") == root ? "." : root;
        }

        private static int MakeProject(string parent, string name, Dictionary<string, string> options, bool force, bool dryRun, TextWriter output)
        {
            var result = ProjectScaffolder.Create(parent, name, options, force, dryRun);
            string where = result.DisplayDirectory == "." ? "this folder" : result.DisplayDirectory + "/";

            var cli = new CliOutput(output);
            if (dryRun)
            {
                cli.Info($"Would create a KScript project in {cli.Style.Bold(where)}:");
                cli.Bullets(result.Files);
                cli.NewLine();
                cli.Heading("  kscript.json:");
                output.Write(result.Contents["kscript.json"]);
                return 0;
            }

            cli.Success($"Created a KScript project in {cli.Style.Bold(where)}{(result.Template.BuiltIn ? "" : $", from the template in {cli.Style.Bold(Shown(result.Template.Folder))}")}:");
            cli.Bullets(result.Files);
            if (result.Note != null)
            {
                cli.NewLine();
                cli.Comment("  " + result.Note);
            }
            var next = new List<string>();
            if (result.DisplayDirectory != ".") next.Add($"cd {(result.DisplayDirectory.Contains(' ') ? "\"" + result.DisplayDirectory + "\"" : result.DisplayDirectory)}");
            next.AddRange(result.Next);
            NextSteps(cli, next);
            return 0;
        }

        /// <summary>"Next:" and the commands to run, after making files.</summary>
        private static void NextSteps(CliOutput cli, IEnumerable<string> lines)
        {
            cli.NewLine();
            cli.Heading("  Next:");
            foreach (string line in lines) cli.Line("    " + Command(cli.Style, line));
        }

        /// <summary>An error from kscript make, after the ERROR label.</summary>
        private static int Fail(TextWriter error, string message)
        {
            new CliOutput(error).Error(message);
            return ExitCodes.Usage;
        }

        /// <summary>
        /// A command line in help and next steps, coloured: kscript and its command in green, &lt;names&gt; in yellow,
        /// --options in cyan, and [optional parts] in gray. Text after two spaces (a note) is gray.
        /// </summary>
        internal static string Command(CliStyle style, string line)
        {
            if (!style.Enabled || string.IsNullOrEmpty(line)) return line;
            int note = line.IndexOf("  ", StringComparison.Ordinal);
            if (note > 0) return Command(style, line.Substring(0, note)) + style.Gray(line.Substring(note));
            return System.Text.RegularExpressions.Regex.Replace(line, @"^(kscript(?: [a-z][\w:-]*(?=\s|$))?)|<[^<>]+>|(?<![\w-])--?[a-zA-Z][\w-]*|[\[\]]", m =>
                m.Groups[1].Success ? style.Green(m.Value)
                : m.Value[0] == '<' ? style.Yellow(m.Value)
                : m.Value[0] == '-' ? style.Cyan(m.Value)
                : style.Gray(m.Value));
        }

        /// <summary>A path for messages: relative to the current folder when it is inside it, with ~ for the home folder.</summary>
        private static string Shown(string path)
        {
            string full = Path.GetFullPath(path);
            string rel = Path.GetRelativePath(KScript.Runtime.WorkingFolder.Current, full);
            if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel)) return rel.Replace('\\', '/');
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 0 && full.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return "~/" + full.Substring(home.Length + 1).Replace('\\', '/');
            return full;
        }

        /// <summary>
        /// kscript make templates project: copies the built-in project template to _templates/project in the current
        /// project (or the --dir one), or, outside a project or with --global, to ~/.kscript/templates/project, where
        /// kscript make project finds it for every new project.
        /// </summary>
        private static int PublishProjectTemplate(string dir, string name, bool global, bool force, TextWriter output)
        {
            string folderName = ProjectTemplate.FolderFor(name);
            string folder;
            if (!global && (dir != null || KScript.Projects.ProjectConfig.FindAndLoad(KScript.Runtime.WorkingFolder.Current) != null))
            {
                string root = dir ?? ProjectRoot();
                folder = Path.Combine(Path.GetFullPath(root), Scaffolder.TemplatesFolder, folderName);
            }
            else
            {
                folder = Path.Combine(ProjectTemplate.UserTemplatesFolder(), folderName);
            }

            var written = ProjectTemplate.Publish(folder, force, name);
            string shown = Shown(folder);
            string what = name == ProjectTemplate.DefaultName ? "the project template" : $"the {name} project template";
            var cli = new CliOutput(output);
            if (written.Count == 0)
            {
                cli.Info($"Nothing to copy: {cli.Style.Bold(shown + "/")} already has {what}. Use --force to replace it.");
                return 0;
            }
            cli.Success($"Copied {what} to {cli.Style.Bold(shown + "/")}:");
            cli.Bullets(written);
            cli.NewLine();
            bool inProject = folder.Contains(Path.DirectorySeparatorChar + Scaffolder.TemplatesFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            string command = name == ProjectTemplate.DefaultName ? "kscript make project" : $"kscript make project --template {name}";
            cli.Comment(inProject
                ? $"  {command} now uses it for projects made inside this one. Edit, add or remove files to change what it makes."
                : $"  {command} now uses it for every new project. Edit, add or remove files to change what it makes.");
            cli.Comment($"  {ProjectTemplate.ManifestName} lists its options (such as --no-desktop) and the 'Next:' lines; a '--- make' header with 'when: desktop' copies a file only when that option is on.");
            return 0;
        }

        /// <summary>The yes/no options of the built-in project templates, and --bare.</summary>
        private static HashSet<string> ProjectFlags()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bare" };
            foreach (string name in ProjectTemplate.BuiltInNames())
                foreach (var o in ProjectTemplate.BuiltInTemplate(name).Options.Where(o => o.IsFlag)) names.Add(o.Name);
            return names;
        }

        private static void PrintProjectHelp(string parent, Dictionary<string, string> options, TextWriter output)
        {
            options.TryGetValue("template", out string templateOption);
            if (templateOption == null && options.TryGetValue("bare", out string bare) && StubTemplate.IsSet(bare)) templateOption = "bare";
            var template = ProjectTemplate.Find(Path.Combine(Path.GetFullPath(parent), "new-project"), templateOption);
            var o = new CliOutput(output);
            o.Heading("Usage:");
            o.Line("  " + Command(o.Style, "kscript make project <folder> [--bare] [--template <name or folder>] [options] [--force] [--dry-run]"));
            o.Line("  " + Command(o.Style, "kscript make project") + o.Style.Gray("   (at a terminal, with no options: asks for each one)"));
            o.NewLine();
            o.Heading("Templates:");
            o.Line($"  Built-in templates: {string.Join(", ", ProjectTemplate.BuiltInNames())} (web is the default; --bare is --template bare).");
            o.Definitions(ProjectTemplate.BuiltInNames().Select(n => (n, ProjectTemplate.BuiltInTemplate(n).Description ?? "")));
            o.NewLine();
            string templateLabel = template.Name ?? "folder";
            o.Heading(template.BuiltIn ? $"Template: {templateLabel} (built in)" : $"Template: {templateLabel}, from {Shown(template.Folder)}");
            if (template.Description != null) o.Line("  " + template.Description);
            if (template.Options.Count > 0)
            {
                o.NewLine();
                o.Heading("Options:");
                o.Definitions(template.Options.Select(opt =>
                {
                    bool flag = opt.Default is "true" or "false";
                    string label = flag && opt.Default == "true" ? "--no-" + opt.Name : "--" + opt.Name;
                    string def = opt.Default == null || flag ? "" : o.Style.Gray($" (default {opt.Default})");
                    return (label, (opt.Description ?? "") + def);
                }).Concat(new[]
                {
                    ("--force", "Make the project even when the folder isn't empty"),
                    ("--dry-run", "Show what would be made, without writing anything"),
                    ("-n, --no-interaction", "Ask nothing: what isn't given takes its default"),
                }));
            }
            o.NewLine();
            o.Comment("  kscript make templates project [bare] copies a template to change it: to _templates/project[-bare] in a");
            o.Comment("  project, or to ~/.kscript/templates/project[-bare] (outside a project, or with --global) for every new project.");
        }

        private static void PrintTemplates(string root, TextWriter output)
        {
            var o = new CliOutput(output);
            o.Heading("Usage:");
            o.Line("  " + Command(o.Style, "kscript make <type> <name> [--key value …] [--dir <project>] [--force] [--dry-run]"));
            o.Line("  " + Command(o.Style, "kscript make <type>") + o.Style.Gray("   (at a terminal: asks for the name and options)"));
            o.NewLine();
            o.Heading("Options:");
            var general = new (string, string)[]
            {
                ("--dir <project>", "Make the file in that project (default: the project of the current folder)"),
                ("--force", "Replace a file that already exists"),
                ("--dry-run", "Show what would be made, without writing anything"),
                ("-n, --no-interaction", "Ask nothing; a missing name is an error"),
                ("--list", "Every type's options and examples"),
                ("-h, --help", "This list, or one type's help with kscript make <type> --help"),
            };
            var projects = new (string, string)[]
            {
                ("project <folder>", "Start a new web app, with a desktop app unless --no-desktop (--bare for a bare project)"),
                ("auth", "Sign-in pages, routes and a controller (KScript.Auth); --api adds token routes"),
                ("templates [type…]", "Copy the built-in templates to _templates/ to customise them"),
                ("templates project", "Copy the project template to customise new projects (--global for all)"),
            };
            var all = Scaffolder.ListTemplates(root);
            int width = Math.Max(Math.Max(general.Max(g => g.Item1.Length), projects.Max(g => g.Item1.Length)), all.Max(t => t.Type.Length));
            o.Definitions(general, nameWidth: width);
            o.NewLine();
            o.Heading("Projects:");
            o.Definitions(projects, nameWidth: width);
            foreach (var (category, types) in Grouped(all))
            {
                o.NewLine();
                o.Heading(category + ":");
                o.Definitions(types.Select(t =>
                {
                    string source = t.BuiltIn ? "" : t.OverridesBuiltIn ? $"  [{Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}, replaces built-in]" : $"  [{Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}]";
                    return (t.Type, (t.Description ?? "") + o.Style.Gray(source));
                }), nameWidth: width);
            }
            o.NewLine();
            o.Heading("Examples:");
            foreach (string e in new[]
            {
                "kscript make project my-app --port 3000",
                "kscript make project my-site --no-desktop",
                "kscript make project my-tool --bare",
                "kscript make route users --api",
                "kscript make controller people --resource",
                "kscript make component Hero --props title,subtitle",
                "kscript make page about --title \"About us\"",
                "kscript make page \"people/[id]\"",
                "kscript make markdown blog/first-post --layout _layouts/blog.html",
            }) o.Line("  " + o.Style.Gray("$ ") + Command(o.Style, e));
            o.NewLine();
            o.Comment("  kscript make --list shows every type's options and examples; kscript make <type> --help shows one.");
        }
    
        /// <summary>The templates by the command directory's sections; a project's own types go under "Your templates".</summary>
        public static List<(string Category, List<StubInfo> Types)> Grouped(IReadOnlyList<StubInfo> all)
        {
            var groups = new List<(string, List<StubInfo>)>();
            foreach (var t in all)
            {
                string category = t.Category ?? Scaffolder.Categories.FirstOrDefault(c => c.Types.Contains(t.Type, StringComparer.OrdinalIgnoreCase)).Name ?? "Your templates";
                int at = groups.FindIndex(g => g.Item1 == category);
                if (at < 0) groups.Add((category, new List<StubInfo> { t }));
                else groups[at].Item2.Add(t);
            }
            var order = Scaffolder.Categories.Select(c => c.Name).ToList();
            return groups.OrderBy(g => order.IndexOf(g.Item1) is int i && i >= 0 ? i : int.MaxValue).ToList();
        }

        /// <summary>kscript make --list: every type, by section, with its usage, options and examples.</summary>
        private static void PrintDirectory(string root, TextWriter output)
        {
            var o = new CliOutput(output);
            o.Comment("kscript make: every type of file it can make. kscript make <type> --help shows one; kscript make templates");
            o.Comment("copies them to _templates/ to change what they make.");
            foreach (var (category, types) in Grouped(Scaffolder.ListTemplates(root)))
            {
                o.NewLine();
                o.Heading(category);
                o.Line(o.Style.Gray(new string('-', category.Length)));
                foreach (var t in types)
                {
                    o.NewLine();
                    PrintType(t, output, indent: "  ");
                }
            }
        }

        private static void PrintType(StubInfo t, TextWriter output, string indent = "")
        {
            var o = new CliOutput(output);
            if (indent.Length == 0)
            {
                // kscript make <type> --help: laid out like kscript help <command>.
                if (t.Description != null)
                {
                    o.Heading("Description:");
                    o.Line("  " + t.Description);
                    if (!t.BuiltIn) o.Comment($"  From {Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}{(t.OverridesBuiltIn ? ", which replaces the built-in template" : "")}.");
                    o.NewLine();
                }
                o.Heading("Usage:");
                o.Line("  " + Command(o.Style, $"kscript make {t.Type} {t.Usage ?? "<name>"}"));
                o.Line("  " + Command(o.Style, $"kscript make {t.Type}") + o.Style.Gray("   (at a terminal: asks for the name and options)"));
                o.NewLine();
                o.Heading("Options:");
                o.Definitions(t.Options.Select(opt => (opt.Name, opt.Description ?? "")).Concat(new[]
                {
                    ("--dir <project>", "Make the file in that project"),
                    ("--force", "Replace the file if it exists"),
                    ("--dry-run", "Show the file, without writing it"),
                    ("-n, --no-interaction", "Ask nothing; a missing name is an error"),
                }));
                if (t.Examples.Count > 0)
                {
                    o.NewLine();
                    o.Heading("Examples:");
                    foreach (string e in t.Examples) o.Line($"  {o.Style.Gray("$")} {Command(o.Style, e)}");
                }
                return;
            }
            o.Line(indent + Command(o.Style, $"kscript make {t.Type} {t.Usage ?? "<name>"}"));
            if (t.Description != null) o.Line($"{indent}  {t.Description}");
            if (!t.BuiltIn) o.Comment($"{indent}  From {Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}{(t.OverridesBuiltIn ? ", which replaces the built-in template" : "")}.");
            if (t.Options.Count > 0)
            {
                int width = t.Options.Max(opt => opt.Name.Length) + 2;
                foreach (var opt in t.Options) o.Line($"{indent}    {o.Style.Cyan(opt.Name)}{new string(' ', width - opt.Name.Length)}{opt.Description}");
            }
            foreach (string e in t.Examples) o.Line($"{indent}  {o.Style.Gray("$")} {Command(o.Style, e)}");
        }
}
}
