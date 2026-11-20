using KScript.Language.Analysis;
using KScript.Syntax;
using KScript.Syntax.Text;
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
        /// <param name="json">
        /// Print one JSON document instead of text: <c>{ "files", "errors", "warnings", "problems": [...] }</c>, where
        /// each problem has its file, 1-based line and column range, severity, code, message and fixes, and each fix
        /// has a title and exact text edits. Usage and settings errors are <c>{ "error": "..." }</c>.
        /// </param>
        public static int Run(IEnumerable<string> paths, TextWriter output, bool warningsAsErrors = false, bool json = false)
        {
            if (json)
            {
                var text = new StringWriter();
                var report = new JsonReport();
                int code = Run(paths, text, warningsAsErrors, report);
                System.Text.Json.Nodes.JsonArray problems = report.Problems;
                var result = new System.Text.Json.Nodes.JsonObject();
                if (code == 2)
                {
                    result["error"] = text.ToString().Trim();
                }
                else
                {
                    int errors = problems.Count(p => (string)p["severity"] == "error");
                    int warnings = problems.Count(p => (string)p["severity"] == "warning");
                    result["files"] = report.Files;
                    result["errors"] = errors;
                    result["warnings"] = warnings;
                    result["problems"] = problems;
                }
                output.WriteLine(result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return code;
            }
            return Run(paths, output, warningsAsErrors, null);
        }

        /// <summary>What a check found, for --format json.</summary>
        private sealed class JsonReport
        {
            public System.Text.Json.Nodes.JsonArray Problems { get; } = new System.Text.Json.Nodes.JsonArray();
            public int Files { get; set; }
        }

        private static int Run(IEnumerable<string> paths, TextWriter output, bool warningsAsErrors, JsonReport report)
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
                output.WriteLine($"No KScript files found in {string.Join(", ", given)}. Scripts are .kscript, .ks or .ks.xml files, or .xml files that start with <kscript>; " +
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
                    if (report != null)
                    {
                        report.Problems.Add(ProblemJson(shown.Replace('\\', '/'), analysis.Source, d, severity));
                    }
                    else
                    {
                        output.WriteLine($"{shown}({pos.Line + 1},{pos.Character + 1}): {severity} {d.Code}: {d.Message}");
                    }
                    if (d.Severity == DiagnosticSeverity.Error) errors++;
                    else if (d.Severity == DiagnosticSeverity.Warning) warnings++;
                }
            }

            if (report != null) report.Files = files.Count;
            output.WriteLine($"{files.Count} file{(files.Count == 1 ? string.Empty : "s")} checked: {errors} error{(errors == 1 ? string.Empty : "s")}, {warnings} warning{(warnings == 1 ? string.Empty : "s")}.");
            return errors > 0 || (anyWarningsAsErrors && warnings > 0) ? 1 : 0;
        }

        /// <summary>A problem as JSON: positions are 1-based, and the end is just past the last character.</summary>
        internal static System.Text.Json.Nodes.JsonObject ProblemJson(string file, SourceText source, Diagnostic d, string severity)
        {
            System.Text.Json.Nodes.JsonObject Range(TextSpan span, System.Text.Json.Nodes.JsonObject into)
            {
                LinePosition start = source.GetPosition(span.Start);
                LinePosition end = source.GetPosition(span.End);
                into["line"] = start.Line + 1;
                into["column"] = start.Character + 1;
                into["endLine"] = end.Line + 1;
                into["endColumn"] = end.Character + 1;
                return into;
            }

            var fixes = new System.Text.Json.Nodes.JsonArray();
            foreach (QuickFix fix in d.Fixes)
            {
                var edits = new System.Text.Json.Nodes.JsonArray();
                foreach (TextChange change in fix.Changes)
                {
                    edits.Add(Range(change.Span, new System.Text.Json.Nodes.JsonObject()).Also(e => e["newText"] = change.NewText));
                }
                fixes.Add(new System.Text.Json.Nodes.JsonObject { ["title"] = fix.Title, ["edits"] = edits });
            }

            var problem = new System.Text.Json.Nodes.JsonObject { ["file"] = file };
            Range(d.Span, problem);
            problem["severity"] = severity;
            problem["code"] = d.Code;
            problem["message"] = d.Message;
            problem["fixes"] = fixes;
            return problem;
        }

        /// <summary>
        /// <c>kscript check --print-config [path]</c>: prints the lint and format settings that apply to a file or folder,
        /// after its standards and kscript.json are combined, and where each one came from. Returns 0, or 2 when the
        /// settings can't be read.
        /// </summary>
        public static int PrintConfig(string path, TextWriter output)
        {
            path ??= Directory.GetCurrentDirectory();
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                output.WriteLine($"{path}: file not found");
                return 2;
            }

            ProjectConfig config;
            try
            {
                string file = ProjectConfig.Find(path);
                config = file == null ? ProjectConfig.Default(Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))) : ProjectConfig.Load(file);
            }
            catch (ProjectConfigException ex)
            {
                output.WriteLine(ex.Message);
                return 2;
            }

            output.Write(DescribeConfig(config));
            return 0;
        }

        /// <summary>The resolved lint and format settings as text, each with the layer that set it.</summary>
        public static string DescribeConfig(ProjectConfig config)
        {
            var w = new StringWriter();
            string From(string key) => config.Sources.TryGetValue(key, out string source) ? source : "default";

            w.WriteLine(config.FilePath != null ? $"Project: {config.FilePath}" : $"No kscript.json above {config.Directory}: using the defaults.");
            w.WriteLine();
            w.WriteLine("Standards (applied in this order; later ones win, and kscript.json wins over all of them):");
            if (config.AppliedStandards.Count == 0)
            {
                w.WriteLine("  (none: \"standard\": [] in kscript.json)");
            }
            foreach (AppliedStandard s in config.AppliedStandards)
            {
                string extends = s.Extends.Count > 0 ? $"  extends {string.Join(", ", s.Extends)}" : string.Empty;
                string name = s.Name != null && s.Name != s.Label ? $"  \"{s.Name}\"" : string.Empty;
                w.WriteLine($"  {s.Label}{name}{extends}");
                if (!string.IsNullOrEmpty(s.Description)) w.WriteLine($"      {s.Description}");
            }

            var rows = new List<(string Key, string Value, string Source)>();
            rows.Add(("lint.warningsAsErrors", config.Lint.WarningsAsErrors ? "true" : "false", From("lint.warningsAsErrors")));
            if (config.Lint.Rules.Count == 0)
            {
                rows.Add(("lint.rules", "(none: every rule at its built-in severity, style rules KS6xxx off)", "default"));
            }
            foreach (var rule in config.Lint.Rules.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(($"lint.rules.{rule.Key}", rule.Value, From("lint.rules." + rule.Key)));
            }
            rows.Add(("lint.include", string.Join(", ", config.Lint.Include), From("lint.include")));
            rows.Add(("lint.exclude", string.Join(", ", config.Lint.Exclude), From("lint.exclude")));

            FormatSettings f = config.Format;
            rows.Add(("format.enabled", f.Enabled ? "true" : "false", From("format.enabled")));
            string indent = f.UseTabs == true ? "tab" : f.IndentSize is int size ? size.ToString() : "the editor's tab settings (kscript fmt: 2)";
            rows.Add(("format.indent", indent, config.Sources.ContainsKey("format.indent") ? From("format.indent") : From("format.tabWidth")));
            rows.Add(("format.printWidth", (f.PrintWidth ?? 120).ToString(), From("format.printWidth")));
            rows.Add(("format.maxBlankLines", (f.MaxBlankLines ?? 1).ToString(), From("format.maxBlankLines")));
            rows.Add(("format.endOfLine", f.EndOfLine, From("format.endOfLine")));
            rows.Add(("format.preserve", f.Preserve.Count == 0 ? "(none)" : string.Join(", ", f.Preserve), From("format.preserve")));
            foreach (string rule in FormatSettings.RuleNames)
            {
                bool on = !f.Rules.TryGetValue(rule, out bool value) || value;
                rows.Add(($"format.rules.{rule}", on ? "on" : "off", From("format.rules." + rule)));
            }
            rows.Add(("format.include", string.Join(", ", f.Include), From("format.include")));
            rows.Add(("format.exclude", string.Join(", ", f.Exclude), From("format.exclude")));

            w.WriteLine();
            int keyWidth = rows.Max(r => r.Key.Length);
            int valueWidth = Math.Min(40, rows.Max(r => r.Value.Length));
            foreach (var (key, value, source) in rows)
            {
                w.WriteLine($"  {key.PadRight(keyWidth)}  {value.PadRight(valueWidth)}  {source}");
            }

            if (config.Warnings.Count > 0)
            {
                w.WriteLine();
                foreach (string warning in config.Warnings) w.WriteLine($"warning: {warning}");
            }
            return w.ToString();
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
            f.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ks", StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".kscript", StringComparison.OrdinalIgnoreCase);

        private static System.Text.Json.Nodes.JsonObject Also(this System.Text.Json.Nodes.JsonObject o, Action<System.Text.Json.Nodes.JsonObject> set)
        {
            set(o);
            return o;
        }

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
