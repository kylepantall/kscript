using KScript.Projects;

namespace KScript.Language.Analysis
{
    /// <summary>Applies a project's kscript.json lint settings to the analyzer's diagnostics, the same way for <c>kscript check</c> and the editor.</summary>
    public static class LintRules
    {
        /// <summary>
        /// Changes severities as the project's lint.rules say (its standards' rules, then its own), drops rules set to
        /// "off", and drops everything for excluded files. Style rules (KS6xxx) are dropped unless a rule names them,
        /// which kscript:recommended does. A file outside any project (<paramref name="config"/> null) gets
        /// kscript:recommended. Returns the list it changed.
        /// </summary>
        public static List<Diagnostic> Apply(List<Diagnostic> diagnostics, ProjectConfig config, string path)
        {
            if (diagnostics.Count == 0)
            {
                return diagnostics;
            }
            if (config == null)
            {
                config = ProjectConfig.Recommended;
                path = null;
            }
            if (path != null && config.IsExcluded(path))
            {
                diagnostics.Clear();
                return diagnostics;
            }

            for (int i = diagnostics.Count - 1; i >= 0; i--)
            {
                Diagnostic d = diagnostics[i];
                // An old spelling in a .kscript file is an error the script doesn't run with: no setting changes that.
                if (d.Severity == DiagnosticSeverity.Error && KScript.Syntax.Markup.OldSpellings.IsOldSpelling(d.Code))
                {
                    continue;
                }
                string severity = config.SeverityFor(d.Code);
                if (severity == null && Standards.IsStyleRule(d.Code))
                {
                    severity = "off";
                }
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
