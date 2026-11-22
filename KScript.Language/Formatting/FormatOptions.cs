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
        public int IndentSize { get; set; } = 2;

        /// <summary>Indent with tabs instead of spaces.</summary>
        public bool UseTabs { get; set; }

        /// <summary>
        /// The longest a line should be. Longer lines are broken: a start tag puts one attribute per line, and in
        /// .kscript files a long call, chain, one-line function, string or comment is wrapped.
        /// </summary>
        public int PrintWidth { get; set; } = 80;

        public EndOfLine EndOfLine { get; set; } = EndOfLine.Auto;

        /// <summary>Blank lines kept in a row between elements; extra ones are removed.</summary>
        public int MaxBlankLines { get; set; } = 1;

        /// <summary>
        /// Elements whose content is left exactly as written, on top of the ones that are always kept
        /// (&lt;pre&gt;, &lt;textarea&gt;, &lt;script&gt; and &lt;style&gt; in pages, and CDATA sections).
        /// </summary>
        public HashSet<string> Preserve { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Re-indent elements by how deeply they are nested. Off: every line keeps its indentation.</summary>
        public bool Indent { get; set; } = true;

        /// <summary>Put each statement (and block-level element) on its own line. Off: line breaks are only changed where there already was whitespace.</summary>
        public bool StatementPerLine { get; set; } = true;

        /// <summary>One space between attributes and <c>&lt;x /&gt;</c> for self-closing tags. Off: start tags are kept as written.</summary>
        public bool TidyTags { get; set; } = true;

        /// <summary>One attribute per line for long or already-wrapped start tags. Off: tags are never wrapped or joined.</summary>
        public bool WrapAttributes { get; set; } = true;

        public bool TrimTrailingWhitespace { get; set; } = true;

        /// <summary>Shrink runs of blank lines to <see cref="MaxBlankLines"/>. Off: blank lines are kept.</summary>
        public bool CollapseBlankLines { get; set; } = true;

        /// <summary>End the file with exactly one newline. Off: the file ends as it did.</summary>
        public bool FinalNewline { get; set; } = true;

        /// <summary>.kscript files: spaces after commas and colons, around assignments and before a block's '{'. Off: spacing inside lines is kept.</summary>
        public bool Spacing { get; set; } = true;

        /// <summary>.kscript files: <c>} else {</c>, <c>} catch {</c> on one line. Off: else and catch stay on the line they are on.</summary>
        public bool BraceStyle { get; set; } = true;

        /// <summary>.kscript files: break a string past the print width with a \ at the end of the line. Off: long strings stay on one line.</summary>
        public bool WrapStrings { get; set; } = true;

        /// <summary>.kscript files: a long <c>-&gt;</c> chain or <c>|&gt;</c> pipe puts each call on its own line. Off: chains stay on one line.</summary>
        public bool WrapChains { get; set; } = true;

        /// <summary>.kscript files: a long one-line function's body moves to the line after its <c>=&gt;</c>. Off: it stays on the line.</summary>
        public bool WrapBodies { get; set; } = true;

        /// <summary>.kscript files: a long <c>//</c> or <c>///</c> comment line is broken between words. Off: comments stay as written.</summary>
        public bool WrapComments { get; set; } = true;

        public static FormatOptions Default => new FormatOptions();

        /// <summary>
        /// Options for a file: the editor's tab settings (when given) are the starting point, and anything the
        /// project's kscript.json sets under "format" wins, so the CLI and every editor agree.
        /// </summary>
        public static FormatOptions From(ProjectConfig project, int? editorTabSize = null, bool? editorInsertSpaces = null)
        {
            FormatOptions options = new FormatOptions();
            if (editorTabSize is int size && size > 0) options.IndentSize = size;
            if (editorInsertSpaces is bool spaces) options.UseTabs = !spaces;

            FormatSettings f = project?.Format;
            if (f == null) return options;
            if (f.IndentSize is int indent) options.IndentSize = indent;
            if (f.UseTabs is bool tabs) options.UseTabs = tabs;
            if (f.PrintWidth is int width) options.PrintWidth = width;
            if (f.MaxBlankLines is int blank) options.MaxBlankLines = blank;
            options.EndOfLine = f.EndOfLine switch
            {
                "lf" => EndOfLine.Lf,
                "crlf" => EndOfLine.CrLf,
                _ => EndOfLine.Auto,
            };
            foreach (string name in f.Preserve) options.Preserve.Add(name);
            foreach (var rule in f.Rules)
            {
                switch (rule.Key)
                {
                    case "indent": options.Indent = rule.Value; break;
                    case "statementPerLine": options.StatementPerLine = rule.Value; break;
                    case "tidyTags": options.TidyTags = rule.Value; break;
                    case "wrapAttributes": options.WrapAttributes = rule.Value; break;
                    case "trimTrailingWhitespace": options.TrimTrailingWhitespace = rule.Value; break;
                    case "collapseBlankLines": options.CollapseBlankLines = rule.Value; break;
                    case "finalNewline": options.FinalNewline = rule.Value; break;
                    case "spacing": options.Spacing = rule.Value; break;
                    case "braceStyle": options.BraceStyle = rule.Value; break;
                    case "wrapStrings": options.WrapStrings = rule.Value; break;
                    case "wrapChains": options.WrapChains = rule.Value; break;
                    case "wrapBodies": options.WrapBodies = rule.Value; break;
                    case "wrapComments": options.WrapComments = rule.Value; break;
                }
            }
            return options;
        }
    }
}
