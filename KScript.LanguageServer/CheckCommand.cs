using KScript.Language.Analysis;
using KScript.Language.Text;
using KScript.Projects;

namespace KScript.LanguageServer
{
    /// <summary>
    /// <c>kscript check</c>: analyzes files without running them and prints problems, for CI and editors without LSP.
    /// Inside a project, the nearest kscript.json decides which files a folder check covers (lint.include / lint.exclude),
    /// the severity of each rule (lint.rules) and whether warnings fail the check (lint.warningsAsErrors).
    /// </summary>
    public static class CheckCommand
    {
        /// <summary>Returns 0 when there are no errors, 1 when there are errors, 2 for bad usage.</summary>
        public static int Run(IEnumerable<string> paths, TextWriter output, bool warningsAsErrors = false)
        {
            List<string> given = paths.ToList();
            var configs = new Dictionary<string, ProjectConfig>(StringComparer.Ordinal);
            var files = new List<(string File, bool Explicit)>();

            try
            {
                if (given.Count == 0)
                {
                    // `kscript check` on its own checks the project the current folder is in.
                    string project = ProjectConfig.Find(Directory.GetCurrentDirectory());
                    if (project == null)
                    {
                        output.WriteLine("Usage: kscript check <file-or-folder>...   (or run it in a project with a kscript.json)");
                        return 2;
                    }
                    given.Add(Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetDirectoryName(project)));
                }

                foreach (string path in given)
                {
                    if (Directory.Exists(path))
                    {
                        files.AddRange(FilesIn(path, configs, output).Select(f => (f, false)).ToList());
                    }
                    else if (File.Exists(path))
                    {
                        files.Add((path, true));
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
                output.WriteLine($"{ex.Message}");
                return 2;
            }

            if (files.Count == 0)
            {
                output.WriteLine($"No KScript files found in {string.Join(", ", given)}. Scripts are .ks, .ks.xml or .xml files that start with <kscript>; " +
                                 ".html and .md pages are checked in folders with _layout.html, _components/, _layouts/ or kscript.json.");
                return 2;
            }

            int errors = 0;
            int warnings = 0;
            bool anyWarningsAsErrors = warningsAsErrors;
            foreach (var (file, isExplicit) in files.DistinctBy(f => Path.GetFullPath(f.File)).OrderBy(f => f.File, StringComparer.Ordinal))
            {
                ProjectConfig config;
                try
                {
                    config = ConfigFor(file, configs, output);
                }
                catch (ProjectConfigException ex)
                {
                    output.WriteLine(ex.Message);
                    return 2;
                }
                anyWarningsAsErrors |= config?.Lint.WarningsAsErrors == true;

                DocumentAnalysis analysis = Analyzer.Analyze(File.ReadAllText(file), file);
                // A file named on the command line is checked even if lint.exclude covers it.
                LintRules.Apply(analysis.Diagnostics, config, isExplicit ? null : file);
                foreach (Diagnostic d in analysis.Diagnostics.OrderBy(d => d.Span.Start))
                {
                    LinePosition pos = analysis.Source.GetPosition(d.Span.Start);
                    string severity = d.Severity switch
                    {
                        DiagnosticSeverity.Error => "error",
                        DiagnosticSeverity.Warning => "warning",
                        DiagnosticSeverity.Information => "info",
                        _ => "hint",
                    };
                    string shown = file.StartsWith("./", StringComparison.Ordinal) || file.StartsWith(".\\", StringComparison.Ordinal) ? file.Substring(2) : file;
                    output.WriteLine($"{shown}({pos.Line + 1},{pos.Character + 1}): {severity} {d.Code}: {d.Message}");
                    if (d.Severity == DiagnosticSeverity.Error) errors++;
                    else if (d.Severity == DiagnosticSeverity.Warning) warnings++;
                }
            }

            output.WriteLine($"{files.Count} file{(files.Count == 1 ? string.Empty : "s")} checked: {errors} error{(errors == 1 ? string.Empty : "s")}, {warnings} warning{(warnings == 1 ? string.Empty : "s")}.");
            return errors > 0 || (anyWarningsAsErrors && warnings > 0) ? 1 : 0;
        }

        /// <summary>
        /// The files a folder check covers. Files inside a project (below a kscript.json) follow its lint.include and
        /// lint.exclude; other files are scripts, plus .html and .md pages when the folder looks like a site.
        /// </summary>
        private static IEnumerable<string> FilesIn(string folder, Dictionary<string, ProjectConfig> configs, TextWriter output)
        {
            // .html and .md files are pages only in a kscript serve site; elsewhere they are left alone.
            bool site = IsSite(folder);
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
                    if (!config.IsExcluded(f) && (config.IsIncluded(f) || xmlScript))
                    {
                        yield return f;
                    }
                    continue;
                }

                // _templates/ holds `kscript make` templates ({{ }} placeholders), not runnable files.
                if (Path.GetRelativePath(folder, f).Replace('\\', '/').Split('/').Contains("_templates"))
                {
                    continue;
                }
                if (IsScriptName(f) || xmlScript
                    || (site && (f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))))
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

        /// <summary>The same markers the editor extension uses to spot a kscript serve site.</summary>
        private static readonly string[] SiteMarkers = { "_components", "_layouts", "_layout.html", ProjectConfig.FileName };

        private static bool IsSite(string folder) =>
            SiteMarkers.Any(m => Directory.Exists(Path.Combine(folder, m)) || File.Exists(Path.Combine(folder, m)));

        private static bool IsScriptName(string f) =>
            f.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ks", StringComparison.OrdinalIgnoreCase);

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
