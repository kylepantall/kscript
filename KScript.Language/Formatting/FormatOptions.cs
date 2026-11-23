using KScript.Language.Rules;
using KScript.Projects;

namespace KScript.Language.Formatting
{
    /// <summary>Line endings the formatter writes.</summary>
    public enum EndOfLine
    {
        /// <summary>Keep the file's own line endings (whichever it uses most; LF for a new file).</summary>
        Auto,
        Lf,
        CrLf,
    }

    /// <summary>
    /// How <see cref="Formatter"/> lays a document out. The defaults match the samples in this repository:
    /// two-space indents, lines wrapped past 80 columns, at most one blank line in a row.
    /// </summary>
    public sealed class FormatOptions
    {
        /// <summary>Columns per indent level (and the width of a tab when measuring existing indentation).</summary>
        public int IndentSize { get; set; }

        /// <summary>Indent with tabs instead of spaces.</summary>
        public bool UseTabs { get; set; }

        /// <summary>
        /// The longest a line should be. Longer lines are broken: a start tag puts one attribute per line, and in
        /// .kscript files a long call, chain, one-line function, string or comment is wrapped.
        /// </summary>
        public int PrintWidth { get; set; }

        public EndOfLine EndOfLine { get; set; }

        /// <summary>Blank lines kept in a row between elements; extra ones are removed.</summary>
        public int MaxBlankLines { get; set; }

        /// <summary>
        /// Elements whose content is left exactly as written, on top of the ones that are always kept
        /// (&lt;pre&gt;, &lt;textarea&gt;, &lt;script&gt; and &lt;style&gt; in pages, and CDATA sections).
        /// </summary>
        public HashSet<string> Preserve { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Re-indent elements by how deeply they are nested. Off: every line keeps its indentation.</summary>
        public bool Indent { get; set; }

        /// <summary>Put each statement (and block-level element) on its own line. Off: line breaks are only changed where there already was whitespace.</summary>
        public bool StatementPerLine { get; set; }

        /// <summary>One space between attributes and <c>&lt;x /&gt;</c> for self-closing tags. Off: start tags are kept as written.</summary>
        public bool TidyTags { get; set; }

        /// <summary>One attribute per line for long or already-wrapped start tags. Off: tags are never wrapped or joined.</summary>
        public bool WrapAttributes { get; set; }

        public bool TrimTrailingWhitespace { get; set; }

        /// <summary>Shrink runs of blank lines to <see cref="MaxBlankLines"/>. Off: blank lines are kept.</summary>
        public bool CollapseBlankLines { get; set; }

        /// <summary>End the file with exactly one newline. Off: the file ends as it did.</summary>
        public bool FinalNewline { get; set; }

        /// <summary>.kscript files: spaces after commas and colons, around assignments and before a block's '{'. Off: spacing inside lines is kept.</summary>
        public bool Spacing { get; set; }

        /// <summary>.kscript files: <c>} else {</c>, <c>} catch {</c> on one line. Off: else and catch stay on the line they are on.</summary>
        public bool BraceStyle { get; set; }

        /// <summary>.kscript files: break a string past the print width with a \ at the end of the line. Off: long strings stay on one line.</summary>
        public bool WrapStrings { get; set; }

        /// <summary>.kscript files: a long <c>-&gt;</c> chain or <c>|&gt;</c> pipe puts each call on its own line. Off: chains stay on one line.</summary>
        public bool WrapChains { get; set; }

        /// <summary>.kscript files: a long one-line function's body moves to the line after its <c>=&gt;</c>. Off: it stays on the line.</summary>
        public bool WrapBodies { get; set; }

        /// <summary>.kscript files: a long <c>//</c> or <c>///</c> comment line is broken between words. Off: comments stay as written.</summary>
        public bool WrapComments { get; set; }

        public FormatOptions() => DocumentRules.Default.ApplyStyle(this);

        public static FormatOptions Default => new FormatOptions();

        /// <summary>
        /// Options for a file: start with the built-in KScript style, then apply editor indentation, the project's
        /// KScript style, and explicit settings from kscript.json.
        /// </summary>
        public static FormatOptions From(ProjectConfig project, int? editorTabSize = null, bool? editorInsertSpaces = null, DocumentRules rules = null)
        {
            FormatOptions options = new FormatOptions();
            if (editorTabSize is int size && size > 0) options.IndentSize = size;
            if (editorInsertSpaces is bool spaces) options.UseTabs = !spaces;

            rules ??= DocumentRules.ForProject(project?.Directory);
            if (!ReferenceEquals(rules, DocumentRules.Default)) rules.ApplyStyle(options);
            if (project != null)
            {
                options.Apply(project.Format, applyAutoEndOfLine: project.Sources.ContainsKey("format.end_of_line"));
            }
            return options;
        }

        internal void Apply(FormatSettings f, bool applyAutoEndOfLine = true)
        {
            if (f.IndentSize is int indent) IndentSize = indent;
            if (f.UseTabs is bool tabs) UseTabs = tabs;
            if (f.PrintWidth is int width) PrintWidth = width;
            if (f.MaxBlankLines is int blank) MaxBlankLines = blank;
            if (f.EndOfLine != null && (f.EndOfLine != "auto" || applyAutoEndOfLine))
            {
                EndOfLine = f.EndOfLine switch
                {
                    "lf" => EndOfLine.Lf,
                    "crlf" => EndOfLine.CrLf,
                    _ => EndOfLine.Auto,
                };
            }
            foreach (string name in f.Preserve) Preserve.Add(name);
            foreach (var rule in f.Rules)
            {
                switch (rule.Key)
                {
                    case "indent": Indent = rule.Value; break;
                    case "statement_per_line": StatementPerLine = rule.Value; break;
                    case "tidy_tags": TidyTags = rule.Value; break;
                    case "wrap_attributes": WrapAttributes = rule.Value; break;
                    case "trim_trailing_whitespace": TrimTrailingWhitespace = rule.Value; break;
                    case "collapse_blank_lines": CollapseBlankLines = rule.Value; break;
                    case "final_newline": FinalNewline = rule.Value; break;
                    case "spacing": Spacing = rule.Value; break;
                    case "brace_style": BraceStyle = rule.Value; break;
                    case "wrap_strings": WrapStrings = rule.Value; break;
                    case "wrap_chains": WrapChains = rule.Value; break;
                    case "wrap_bodies": WrapBodies = rule.Value; break;
                    case "wrap_comments": WrapComments = rule.Value; break;
                }
            }
        }
    }
}
