using KScript.Projects;

namespace KScript.Language.Analysis
{
    /// <summary>Applies a project's kscript.json lint settings to the analyzer's diagnostics, the same way for <c>kscript check</c> and the editor.</summary>
    public static class LintRules
    {
        /// <summary>Changes severities as lint.rules says, drops rules set to "off", and drops everything for excluded files. Returns the list it changed.</summary>
        public static List<Diagnostic> Apply(List<Diagnostic> diagnostics, ProjectConfig config, string path)
        {
            if (config == null || diagnostics.Count == 0)
            {
                return diagnostics;
            }
            if (path != null && config.IsExcluded(path))
            {
                diagnostics.Clear();
                return diagnostics;
            }
            if (config.Lint.Rules.Count == 0)
            {
                return diagnostics;
            }

            for (int i = diagnostics.Count - 1; i >= 0; i--)
            {
                Diagnostic d = diagnostics[i];
                string severity = config.SeverityFor(d.Code);
                if (severity == null)
                {
                    continue;
                }
                if (severity == "off")
                {
                    diagnostics.RemoveAt(i);
                    continue;
                }
                DiagnosticSeverity wanted = ToSeverity(severity);
                if (wanted != d.Severity)
                {
                    diagnostics[i] = new Diagnostic(d.Span, wanted, d.Code, d.Message, d.Fixes);
                }
            }
            return diagnostics;
        }

        public static DiagnosticSeverity ToSeverity(string name) => name switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "info" => DiagnosticSeverity.Information,
            _ => DiagnosticSeverity.Hint,
        };
    }
}
