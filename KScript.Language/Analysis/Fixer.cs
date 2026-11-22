using KScript.Projects;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Applies the fixes the consistency rules offer, for <c>kscript check --fix</c>. Only rules whose fix is the one
    /// right spelling are applied (quotes round text, arrays for lists, true/false, bare names...); rules that need a
    /// decision, such as an unused function, are left to the person.
    /// </summary>
    public static class Fixer
    {
        /// <summary>The rules whose first fix is always safe to apply.</summary>
        public static readonly IReadOnlySet<string> Rules = new HashSet<string>(StringComparer.Ordinal)
        {
            DiagnosticCodes.PreferArrayList,
            DiagnosticCodes.QuoteText,
            DiagnosticCodes.BareDeclaration,
            DiagnosticCodes.BoolWords,
            DiagnosticCodes.OneSpelling,
            DiagnosticCodes.TypeForm,
        };

        private static readonly bool Migrating = Environment.GetEnvironmentVariable("KSCRIPT_MIGRATE") == "1";

        /// <summary>The text with every safe fix applied, and how many were. Runs until nothing more changes.</summary>
        public static (string Text, int Fixed) Apply(string text, string path, ProjectConfig config = null, Func<Diagnostic, bool> also = null)
        {
            int total = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                DocumentAnalysis analysis = Analyzer.Analyze(text, path);
                List<Diagnostic> diagnostics = analysis.Diagnostics.ToList();
                // A rule the project turned off is not fixed either.
                LintRules.Apply(diagnostics, config, path);
                var changes = new List<TextChange>();
                foreach (Diagnostic d in diagnostics.OrderBy(d => d.Span.Start))
                {
                    if (d.Fixes == null || d.Fixes.Count == 0 || !(Rules.Contains(d.Code) || also?.Invoke(d) == true || (Migrating && d.Code == DiagnosticCodes.InvalidAttributeValue && d.Fixes[0].Title == "Write it in braces")))
                    {
                        continue;
                    }
                    IReadOnlyList<TextChange> edits = d.Fixes[0].Changes;
                    if (edits.Any(c => changes.Any(o => c.Span.Start < o.Span.End && o.Span.Start < c.Span.End)))
                    {
                        // Overlaps a fix already taken: the next pass picks it up.
                        continue;
                    }
                    changes.AddRange(edits);
                    total++;
                }
                if (changes.Count == 0)
                {
                    break;
                }
                var sb = new System.Text.StringBuilder(text);
                foreach (TextChange c in changes.OrderByDescending(c => c.Span.Start))
                {
                    sb.Remove(c.Span.Start, c.Span.Length);
                    sb.Insert(c.Span.Start, c.NewText);
                }
                text = sb.ToString();
            }
            return (text, total);
        }
    }
}
