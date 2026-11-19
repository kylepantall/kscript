using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KScript.Scaffolding
{
    /// <summary>
    /// The command line for <c>kscript make</c>:
    /// <code>
    /// kscript make                          list the templates
    /// kscript make component Hero --props title,image
    /// kscript make page "people/[id]"
    /// kscript make templates [type…]        copy the built-in templates to _templates/ to customise them
    /// </code>
    /// Options: --dir &lt;project&gt;, --force, --dry-run; any other --key value (or --key=value, or a bare --flag) is
    /// passed to the template as {{ key }}.
    /// </summary>
    public static class MakeCommand
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            var positional = new List<string>();
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string root = ".";
            bool force = false, dryRun = false, help = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-h" || a == "--help") { help = true; continue; }
                if (!a.StartsWith("--", StringComparison.Ordinal) || a.Length == 2)
                {
                    positional.Add(a);
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

                if (value == null)
                {
                    // --flag on its own (or followed by another option) is true.
                    value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
                }
                options[key] = value;
            }

            try
            {
                if (!Directory.Exists(root))
                {
                    error.WriteLine($"The folder '{root}' does not exist.");
                    return 1;
                }

                if (help || positional.Count == 0)
                {
                    PrintTemplates(root, output);
                    return 0;
                }

                string type = positional[0];

                if (string.Equals(type, "templates", StringComparison.OrdinalIgnoreCase))
                {
                    var written = Scaffolder.PublishTemplates(root, force, positional.Skip(1));
                    if (written.Count == 0) output.WriteLine($"Nothing to copy: {Scaffolder.TemplatesFolder}/ already has those templates. Use --force to replace them.");
                    foreach (var w in written) output.WriteLine($"Created {w}");
                    if (written.Count > 0) output.WriteLine($"Edit them to change what kscript make creates, or add new ones (e.g. {Scaffolder.TemplatesFolder}/card.html for kscript make card …).");
                    return 0;
                }

                if (positional.Count < 2)
                {
                    var t = Scaffolder.FindTemplate(root, type);
                    error.WriteLine($"Give the {t.Type} a name, e.g. kscript make {t.Type} Hero.");
                    return 1;
                }
                if (positional.Count > 2)
                {
                    error.WriteLine($"Unexpected '{positional[2]}'. Put names with spaces in quotes, and options after --, e.g. --title \"About us\".");
                    return 1;
                }

                var result = Scaffolder.Make(new MakeRequest { Type = type, Name = positional[1], Root = root, Options = options, Force = force, DryRun = dryRun });

                if (dryRun)
                {
                    output.WriteLine($"Would create {result.RelativePath}{(result.Overwrote ? " (replacing it)" : "")}:");
                    output.WriteLine();
                    output.Write(result.Content);
                    if (!result.Content.EndsWith('\n')) output.WriteLine();
                }
                else
                {
                    output.WriteLine($"{(result.Overwrote ? "Replaced" : "Created")} {result.RelativePath}{(result.Template.BuiltIn ? "" : $" (from {Scaffolder.TemplatesFolder}/{Path.GetFileName(result.Template.FilePath)})")}");
                }
                if (!string.IsNullOrEmpty(result.Hint)) output.WriteLine(result.Hint);
                return 0;
            }
            catch (StubException ex)
            {
                error.WriteLine(ex.Message);
                return 1;
            }
            catch (IOException ex)
            {
                error.WriteLine(ex.Message);
                return 1;
            }
            catch (UnauthorizedAccessException ex)
            {
                error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static void PrintTemplates(string root, TextWriter output)
        {
            output.WriteLine("Usage: kscript make <type> <Name> [--key value …] [--dir <project>] [--force] [--dry-run]");
            output.WriteLine("       kscript make templates [type…]   copy the built-in templates to _templates/ to customise them");
            output.WriteLine();
            output.WriteLine("Types:");
            var all = Scaffolder.ListTemplates(root);
            int width = all.Max(t => t.Type.Length) + 2;
            foreach (var t in all)
            {
                string source = t.BuiltIn ? "" : t.OverridesBuiltIn ? $"  [{Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}, replaces built-in]" : $"  [{Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}]";
                output.WriteLine($"  {t.Type.PadRight(width)}{t.Description ?? ""}{source}");
            }
            output.WriteLine();
            output.WriteLine("Examples:");
            output.WriteLine("  kscript make component Hero --props title,subtitle");
            output.WriteLine("  kscript make page about --title \"About us\"");
            output.WriteLine("  kscript make page \"people/[id]\"");
            output.WriteLine("  kscript make markdown blog/first-post --layout _layouts/blog.html");
        }
    }
}
