using System.Text;

using KScript.Language.Formatting;
using KScript.Projects;
using KScript.Syntax.Text;

namespace KScript.LanguageServer
{
    /// <summary>
    /// <c>kscript fmt</c>: tidies scripts and pages in place, or with <c>--check</c> only reports the files that would
    /// change (for CI). Inside a project, the nearest kscript.json's "format" section sets the style and decides which
    /// files a folder covers (format.include / format.exclude). Markdown pages are never reformatted.
    /// </summary>
    public static class FormatCommand
    {
        /// <summary>Returns 0 when every file is (or now is) formatted, 1 when --check found unformatted files or a file could not be formatted, 2 for bad usage.</summary>
        public static int Run(IEnumerable<string> args, TextWriter output)
        {
            bool check = false;
            List<string> given = new List<string>();
            foreach (string arg in args)
            {
                switch (arg)
                {
                    case "--check":
                        check = true;
                        break;
                    case "--help":
                    case "-h":
                        output.WriteLine("Usage: kscript fmt [file-or-folder]... [--check]");
                        output.WriteLine("  Tidies .ks.xml, .ks and .xml scripts and kscript serve .html pages. With no path, formats the current project.");
                        output.WriteLine("  --check   Change nothing; list the files that aren't formatted and exit 1 if there are any (for CI).");
                        return 0;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal))
                        {
                            output.WriteLine($"Unknown option {arg}. Usage: kscript fmt [file-or-folder]... [--check]");
                            return 2;
                        }
                        given.Add(arg);
                        break;
                }
            }

            var configs = new Dictionary<string, ProjectConfig>(StringComparer.Ordinal);
            var files = new List<string>();
            try
            {
                if (given.Count == 0)
                {
                    string project = ProjectConfig.Find(KScript.Runtime.WorkingFolder.Current);
                    if (project == null)
                    {
                        output.WriteLine("Usage: kscript fmt <file-or-folder>... [--check]   (or run it in a project with a kscript.json)");
                        return 2;
                    }
                    given.Add(Path.GetRelativePath(KScript.Runtime.WorkingFolder.Current, Path.GetDirectoryName(project)));
                }

                foreach (string path in given)
                {
                    if (Directory.Exists(path))
                    {
                        files.AddRange(FilesIn(path, configs, output).ToList());
                    }
                    else if (File.Exists(path))
                    {
                        files.Add(path);
                    }
                    else
                    {
                        output.WriteLine($"{path}: file not found");
                        return 2;
                    }
                }
            }
            catch (ProjectConfigException ex)
            {
                KScript.Runtime.ErrorReport.Write(output, ex);
                return 2;
            }

            files = files.DistinctBy(f => Path.GetFullPath(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (files.Count == 0)
            {
                output.WriteLine($"No KScript files found in {string.Join(", ", given)}. Scripts are .ks, .ks.xml or .xml files that start with <kscript>; " +
                                 ".html pages are formatted in folders with _layout.html, _components/, _layouts/ or kscript.json.");
                return 2;
            }

            int changed = 0, failed = 0, formatted = 0;
            foreach (string file in files)
            {
                string shown = file.StartsWith("./", StringComparison.Ordinal) || file.StartsWith(".\\", StringComparison.Ordinal) ? file.Substring(2) : file;
                if (MarkupKind(file) == Syntax.Markup.DocumentKind.Markdown)
                {
                    output.WriteLine($"{shown}: skipped (Markdown pages are not reformatted)");
                    continue;
                }

                ProjectConfig config;
                try
                {
                    config = ConfigFor(file, configs, output);
                }
                catch (ProjectConfigException ex)
                {
                    KScript.Runtime.ErrorReport.Write(output, ex);
                    return 2;
                }

                if (config != null && !config.Format.Enabled)
                {
                    output.WriteLine($"{shown}: skipped (format.enabled is false in {config.FilePath})");
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(file);
                bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                string original = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));

                FormatResult result = Formatter.Format(original, file, FormatOptions.From(config));
                if (!result.Succeeded)
                {
                    LinePosition pos = new SourceText(original).GetPosition(result.ErrorSpan?.Start ?? 0);
                    output.WriteLine($"{shown}({pos.Line + 1},{pos.Character + 1}): error: {result.Error}");
                    failed++;
                    continue;
                }

                formatted++;
                if (result.Text == original)
                {
                    continue;
                }

                changed++;
                if (check)
                {
                    output.WriteLine($"{shown}: not formatted");
                }
                else
                {
                    File.WriteAllText(file, result.Text, new UTF8Encoding(bom));
                    output.WriteLine($"{shown}: formatted");
                }
            }

            string files1 = formatted == 1 ? "file" : "files";
            if (check)
            {
                output.WriteLine(changed == 0
                    ? $"{formatted} {files1} checked: all formatted."
                    : $"{formatted} {files1} checked: {changed} need formatting. Run kscript fmt to fix {(changed == 1 ? "it" : "them")}.");
            }
            else
            {
                output.WriteLine($"{formatted} {files1} checked: {changed} formatted.");
            }
            if (failed > 0)
            {
                output.WriteLine($"{failed} file{(failed == 1 ? " has" : "s have")} errors and {(failed == 1 ? "was" : "were")} left alone; kscript check shows the details.");
            }
            return failed > 0 || (check && changed > 0) ? 1 : 0;
        }

        private static Syntax.Markup.DocumentKind MarkupKind(string file) => Syntax.Markup.MarkupParser.KindFromPath(file);

        /// <summary>
        /// The files a folder covers. Inside a project they follow format.include and format.exclude (plus .xml files
        /// that are scripts); elsewhere they are scripts, plus .html pages when the folder looks like a site.
        /// </summary>
        private static IEnumerable<string> FilesIn(string folder, Dictionary<string, ProjectConfig> configs, TextWriter output)
        {
            bool site = SiteMarkers.Any(m => Directory.Exists(Path.Combine(folder, m)) || File.Exists(Path.Combine(folder, m)));
            var projectByDir = new Dictionary<string, ProjectConfig>(StringComparer.Ordinal);

            foreach (string f in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(f));
                if (!projectByDir.TryGetValue(dir, out ProjectConfig config))
                {
                    projectByDir[dir] = config = ConfigFor(dir, configs, output);
                }

                bool xmlScript = f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !IsScriptName(f) && LooksLikeScript(f);
                if (config != null)
                {
                    if (!config.IsFormatExcluded(f) && (config.IsFormatIncluded(f) || xmlScript))
                    {
                        yield return f;
                    }
                    continue;
                }

                string[] parts = Path.GetRelativePath(folder, f).Replace('\\', '/').Split('/');
                if (parts.Contains("_templates") || parts.Contains("node_modules") || parts.Contains(".git"))
                {
                    continue;
                }
                if (IsScriptName(f) || xmlScript || (site && f.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return f;
                }
            }
        }

        private static ProjectConfig ConfigFor(string path, Dictionary<string, ProjectConfig> cache, TextWriter output)
        {
            string file = ProjectConfig.Find(path);
            if (file == null)
            {
                return null;
            }
            if (!cache.TryGetValue(file, out ProjectConfig config))
            {
                config = ProjectConfig.Load(file);
                cache[file] = config;
                foreach (string warning in config.Warnings)
                {
                    output.WriteLine($"{file}: warning {warning}");
                }
            }
            return config;
        }

        private static readonly string[] SiteMarkers = { "_components", "_layouts", "_layout.html", ProjectConfig.FileName };

        private static bool IsScriptName(string f) =>
            f.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ks", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".kscript", StringComparison.OrdinalIgnoreCase);

        private static bool LooksLikeScript(string path)
        {
            try
            {
                using StreamReader reader = new StreamReader(path);
                char[] buffer = new char[512];
                int n = reader.Read(buffer, 0, buffer.Length);
                return new string(buffer, 0, n).Contains("<kscript", StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
