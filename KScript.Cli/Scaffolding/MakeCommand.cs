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
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            var positional = new List<string>();
            var positionalAt = new HashSet<int>();
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string root = null;
            bool force = false, dryRun = false, help = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-h" || a == "--help") { help = true; continue; }
                if (!a.StartsWith("--", StringComparison.Ordinal) || a.Length == 2)
                {
                    positional.Add(a);
                    positionalAt.Add(i);
                    continue;
                }

                string key = a.Substring(2), value = null;
                int eq = key.IndexOf('=');
                if (eq >= 0) { value = key.Substring(eq + 1); key = key.Substring(0, eq); }

                switch (key.ToLowerInvariant())
                {
                    case "force": force = true; continue;
                    case "dry-run": dryRun = true; continue;
                    case "dir":
                        value ??= i + 1 < args.Length ? args[++i] : null;
                        if (string.IsNullOrEmpty(value)) { error.WriteLine("--dir needs a folder, e.g. --dir \"Example Sites/team\""); return 1; }
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
                    value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
                }
                options[key] = value;
            }

            try
            {
                string type = positional.Count > 0 ? positional[0] : null;

                if (string.Equals(type, "project", StringComparison.OrdinalIgnoreCase))
                {
                    if (root != null && !Directory.Exists(root)) { error.WriteLine($"The folder '{root}' does not exist."); return 1; }
                    if (help) { PrintProjectHelp(root ?? ".", options, output); return 0; }
                    if (positional.Count < 2) { error.WriteLine("Give the project a folder name, e.g. kscript make project my-site (or . for the current folder)."); return 1; }
                    if (positional.Count > 2) { error.WriteLine($"Unexpected '{positional[2]}'. Put names with spaces in quotes."); return 1; }
                    return MakeProject(root ?? ".", positional[1], options, force, dryRun, output);
                }

                bool fromProject = root == null;
                root ??= ProjectRoot();
                if (!Directory.Exists(root))
                {
                    error.WriteLine($"The folder '{root}' does not exist.");
                    return 1;
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
                        error.WriteLine($"Copy one project template at a time: kscript make templates project [{string.Join("|", ProjectTemplate.BuiltInNames())}]");
                        return 1;
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
                    if (positional.Count > 1) { error.WriteLine($"kscript make auth takes no name (unexpected '{positional[1]}'). Options: --api, --format, --force, --dry-run."); return 1; }
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
                    var t = Scaffolder.FindTemplate(root, type);
                    error.WriteLine($"Give the {t.Type} a name, e.g. kscript make {t.Type} {Scaffolder.Example(t.Type)}.");
                    return 1;
                }
                if (positional.Count > 2)
                {
                    error.WriteLine(TooManyNames(root, positional, args.Where((_, i) => !positionalAt.Contains(i))));
                    return 1;
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
                return 1;
            }
            catch (StubException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return 1;
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

            if (dryRun)
            {
                output.WriteLine($"Would create a KScript project in {where}:");
                foreach (var f in result.Files) output.WriteLine($"  {f}");
                output.WriteLine();
                output.WriteLine("kscript.json:");
                output.Write(result.Contents["kscript.json"]);
                return 0;
            }

            output.WriteLine($"Created a KScript project in {where}{(result.Template.BuiltIn ? "" : $", from the template in {Shown(result.Template.Folder)}")}:");
            foreach (var f in result.Files) output.WriteLine($"  {f}");
            if (result.Note != null) output.WriteLine(result.Note);
            output.WriteLine();
            output.WriteLine("Next:");
            if (result.DisplayDirectory != ".") output.WriteLine($"  cd {(result.DisplayDirectory.Contains(' ') ? "\"" + result.DisplayDirectory + "\"" : result.DisplayDirectory)}");
            foreach (string line in result.Next) output.WriteLine("  " + line);
            return 0;
        }

        /// <summary>"Next:" and the commands to run, after making files.</summary>
        private static void NextSteps(CliOutput cli, IEnumerable<string> lines)
        {
            cli.NewLine();
            cli.Heading("  Next:");
            foreach (string line in lines) cli.Line("  " + line);
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
            if (written.Count == 0)
            {
                output.WriteLine($"Nothing to copy: {shown}/ already has {what}. Use --force to replace it.");
                return 0;
            }
            output.WriteLine($"Copied {what} to {shown}/:");
            foreach (var w in written) output.WriteLine($"  {w}");
            bool inProject = folder.Contains(Path.DirectorySeparatorChar + Scaffolder.TemplatesFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            string command = name == ProjectTemplate.DefaultName ? "kscript make project" : $"kscript make project --template {name}";
            output.WriteLine(inProject
                ? $"{command} now uses it for projects made inside this one. Edit, add or remove files to change what it makes."
                : $"{command} now uses it for every new project. Edit, add or remove files to change what it makes.");
            output.WriteLine($"{ProjectTemplate.ManifestName} lists its options (such as --no-desktop) and the 'Next:' lines; a '--- make' header with 'when: desktop' copies a file only when that option is on.");
            return 0;
        }

        private static void PrintProjectHelp(string parent, Dictionary<string, string> options, TextWriter output)
        {
            options.TryGetValue("template", out string templateOption);
            if (templateOption == null && options.TryGetValue("bare", out string bare) && StubTemplate.IsSet(bare)) templateOption = "bare";
            var template = ProjectTemplate.Find(Path.Combine(Path.GetFullPath(parent), "new-project"), templateOption);
            output.WriteLine("Usage: kscript make project <folder> [--bare] [--template <name or folder>] [options] [--force] [--dry-run]");
            output.WriteLine();
            output.WriteLine($"Built-in templates: {string.Join(", ", ProjectTemplate.BuiltInNames())} (web is the default; --bare is --template bare).");
            output.WriteLine();
            string templateLabel = template.Name ?? "folder";
            output.WriteLine(template.BuiltIn ? $"Template: {templateLabel} (built in)" : $"Template: {templateLabel}, from {Shown(template.Folder)}");
            if (template.Description != null) output.WriteLine("  " + template.Description);
            if (template.Options.Count > 0)
            {
                output.WriteLine();
                output.WriteLine("Options:");
                int width = template.Options.Max(o => o.Name.Length) + 4;
                foreach (var o in template.Options)
                {
                    bool flag = o.Default is "true" or "false";
                    string label = flag && o.Default == "true" ? "--no-" + o.Name : "--" + o.Name;
                    string def = o.Default == null || flag ? "" : $" (default {o.Default})";
                    output.WriteLine($"  {label.PadRight(width + 5)}{o.Description}{def}");
                }
            }
            output.WriteLine();
            output.WriteLine("kscript make templates project [bare] copies a template to change it: to _templates/project[-bare] in a");
            output.WriteLine("project, or to ~/.kscript/templates/project[-bare] (outside a project, or with --global) for every new project.");
        }

        private static void PrintTemplates(string root, TextWriter output)
        {
            output.WriteLine("Usage: kscript make <type> <Name> [--key value …] [--dir <project>] [--force] [--dry-run]");
            output.WriteLine("       kscript make templates [type…]   copy the built-in templates to _templates/ to customise them");
            output.WriteLine("       kscript make templates project   copy the project template to customise new projects (--global for all)");
            output.WriteLine("       kscript make project <folder>    start a new web app, with a desktop app unless --no-desktop");
            output.WriteLine("       kscript make auth                sign-in pages, routes and a controller (KScript.Auth); --api adds token routes");
            output.WriteLine("       kscript make project <folder> --bare   start a bare project: kscript.json, one script and a test");
            output.WriteLine();
            var all = Scaffolder.ListTemplates(root);
            int width = all.Max(t => t.Type.Length) + 2;
            foreach (var (category, types) in Grouped(all))
            {
                output.WriteLine(category + ":");
                foreach (var t in types)
                {
                    string source = t.BuiltIn ? "" : t.OverridesBuiltIn ? $"  [{Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}, replaces built-in]" : $"  [{Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}]";
                    output.WriteLine($"  {t.Type.PadRight(width)}{t.Description ?? ""}{source}");
                }
                output.WriteLine();
            }
            output.WriteLine("kscript make --list shows every type's options and examples; kscript make <type> --help shows one.");
            output.WriteLine();
            output.WriteLine("Examples:");
            output.WriteLine("  kscript make project my-app --port 3000");
            output.WriteLine("  kscript make project my-site --no-desktop");
            output.WriteLine("  kscript make project my-tool --bare");
            output.WriteLine("  kscript make route users --api");
            output.WriteLine("  kscript make controller people --resource");
            output.WriteLine("  kscript make component Hero --props title,subtitle");
            output.WriteLine("  kscript make page about --title \"About us\"");
            output.WriteLine("  kscript make page \"people/[id]\"");
            output.WriteLine("  kscript make markdown blog/first-post --layout _layouts/blog.html");
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
            output.WriteLine("kscript make: every type of file it can make. kscript make <type> --help shows one; kscript make templates");
            output.WriteLine("copies them to _templates/ to change what they make.");
            foreach (var (category, types) in Grouped(Scaffolder.ListTemplates(root)))
            {
                output.WriteLine();
                output.WriteLine(category);
                output.WriteLine(new string('-', category.Length));
                foreach (var t in types)
                {
                    output.WriteLine();
                    PrintType(t, output, indent: "  ");
                }
            }
        }

        private static void PrintType(StubInfo t, TextWriter output, string indent = "")
        {
            output.WriteLine($"{indent}kscript make {t.Type} {t.Usage ?? "<name>"}");
            if (t.Description != null) output.WriteLine($"{indent}  {t.Description}");
            if (!t.BuiltIn) output.WriteLine($"{indent}  From {Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}{(t.OverridesBuiltIn ? ", which replaces the built-in template" : "")}.");
            if (t.Options.Count > 0)
            {
                int width = t.Options.Max(o => o.Name.Length) + 2;
                foreach (var o in t.Options) output.WriteLine($"{indent}    {o.Name.PadRight(width)}{o.Description}");
            }
            foreach (string e in t.Examples) output.WriteLine($"{indent}  $ {e}");
        }
}
}
