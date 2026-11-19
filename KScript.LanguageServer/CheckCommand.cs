using KScript.Language.Analysis;
using KScript.Language.Text;

namespace KScript.LanguageServer
{
    /// <summary><c>kscript check</c>: analyzes files without running them and prints problems, for CI and editors without LSP.</summary>
    public static class CheckCommand
    {
        /// <summary>Returns 0 when there are no errors, 1 when there are errors, 2 for bad usage.</summary>
        public static int Run(IEnumerable<string> paths, TextWriter output, bool warningsAsErrors = false)
        {
            List<string> files = new List<string>();
            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    // .html and .md files are pages only in a kscript serve site; elsewhere they are left alone.
                    bool site = IsSite(path);
                    files.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ks", StringComparison.OrdinalIgnoreCase)
                                    || (f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && LooksLikeScript(f))
                                    || (site && (f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))))
                        // _templates/ holds `kscript make` templates ({{ }} placeholders), not runnable files.
                        .Where(f => !Path.GetRelativePath(path, f).Replace('\\', '/').Split('/').Contains("_templates")));
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

            if (files.Count == 0)
            {
                output.WriteLine(paths.Any()
                    ? $"No KScript files found in {string.Join(", ", paths)}. Scripts are .ks, .ks.xml or .xml files that start with <kscript>; .html and .md pages are checked in folders with _layout.html, _components/, _layouts/ or kscript.json."
                    : "Usage: kscript check <file-or-folder>...");
                return 2;
            }

            int errors = 0;
            int warnings = 0;
            foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                DocumentAnalysis analysis = Analyzer.Analyze(File.ReadAllText(file), file);
                foreach (Diagnostic d in analysis.Diagnostics.OrderBy(d => d.Span.Start))
                {
                    LinePosition pos = analysis.Source.GetPosition(d.Span.Start);
                    string severity = d.Severity == DiagnosticSeverity.Error ? "error" : d.Severity == DiagnosticSeverity.Warning ? "warning" : "info";
                    output.WriteLine($"{file}({pos.Line + 1},{pos.Character + 1}): {severity} {d.Code}: {d.Message}");
                    if (d.Severity == DiagnosticSeverity.Error) errors++;
                    else if (d.Severity == DiagnosticSeverity.Warning) warnings++;
                }
            }

            output.WriteLine($"{files.Count} file{(files.Count == 1 ? string.Empty : "s")} checked: {errors} error{(errors == 1 ? string.Empty : "s")}, {warnings} warning{(warnings == 1 ? string.Empty : "s")}.");
            return errors > 0 || (warningsAsErrors && warnings > 0) ? 1 : 0;
        }

        /// <summary>The same markers the editor extension uses to spot a kscript serve site.</summary>
        private static readonly string[] SiteMarkers = { "_components", "_layouts", "_layout.html", "kscript.json" };

        private static bool IsSite(string folder) =>
            SiteMarkers.Any(m => Directory.Exists(Path.Combine(folder, m)) || File.Exists(Path.Combine(folder, m)));

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
