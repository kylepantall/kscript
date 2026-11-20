using System.Text;

using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    // Style rules: warnings that are on by default and can be turned off per project with kscript.json lint.rules.
    public sealed partial class Analyzer
    {
        /// <summary>Elements after which the rest of a block never runs.</summary>
        private static readonly HashSet<string> ExitElements = new HashSet<string>(StringComparer.Ordinal) { "return", "break", "continue", "throw", "exit" };

        /// <summary>
        /// KS6001: an <c>else</c> or <c>elif</c> in a script. Early exits read better: end the <c>if</c> branch with
        /// <c>return</c> (in a function) or <c>continue</c>/<c>break</c> (in a loop) and write the rest after it.
        /// Pages, <c>&lt;live&gt;</c> blocks and the <c>&lt;for&gt;…&lt;else&gt;</c> empty-list case are left alone,
        /// because there is nothing to return from there. At the top of a script, outside any function or loop, it
        /// only warns when every branch before already ends early (so the <c>else</c> is redundant).
        /// </summary>
        private void CheckElse(MarkupElement e, MarkupElement previous, bool markupMode)
        {
            if (Kind != DocumentKind.Script || markupMode || (e.Name != "else" && e.Name != "elif"))
            {
                return;
            }
            if (previous == null || (previous.Name != "if" && previous.Name != "elif"))
            {
                return;
            }
            List<MarkupElement> earlier = ChainBefore(e);
            if (earlier == null)
            {
                return;
            }

            MarkupElement holder = e.Ancestors().FirstOrDefault(a => a.Name == "fn" || catalog.GetElement(a.Name)?.IsLoop == true);
            string exit = holder == null ? null : holder.Name == "fn" ? "return" : "continue";
            bool allExit = earlier.All(EndsWithExit);
            if (!allExit && exit == null)
            {
                return;
            }

            // Without an early exit in every earlier branch, the fix can still add one when nothing follows the
            // chain in the function or loop: falling off the end and returning (or continuing) do the same thing.
            bool tail = e.Parent == holder && IsLastStatement(ChainEnd(e));
            QuickFix fix = allExit || tail ? EarlyExitFix(e, previous, earlier, allExit ? null : exit) : null;

            bool code = result.Markup.Syntax == SourceSyntax.Code;
            string ifName = code ? "if" : "<if>";
            string word = !code ? "<" + e.Name + ">" : e.Name == "elif" && source[e.NameSpan] == "else" ? "else if" : e.Name;
            string message;
            if (allExit)
            {
                message = e.Name == "else"
                    ? $"{Capital(word)} isn't needed: the branches before it already end early, so its body can go after the {ifName}."
                    : $"{Capital(word)} can be a separate {ifName}: the branches before it already end early.";
            }
            else
            {
                string how = exit == "return" ? "return early" : "continue (or break) early";
                message = $"Avoid {word}: {how} from the {ifName} branch and write the rest after it.";
            }
            Report(e.NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.PreferEarlyExit, message, fix != null ? new[] { fix } : null);
        }

        /// <summary>
        /// KS6006: a list of names written as one string in a .kscript file, <c>use: "log, cors"</c>, where an array
        /// reads better: <c>use: [log, cors]</c>. XML attributes are always text, so they are left alone there.
        /// </summary>
        private void CheckNameListString(MarkupElement e, MarkupAttribute attr, AttributeInfo a)
        {
            if (result.Markup.Syntax != SourceSyntax.Code || !a.IsNameList || e.Name == "import" || attr.Quote != '"' || !attr.ValueText.Contains(','))
            {
                return;
            }
            List<string> names = KScript.Metadata.NameList.Names(attr.ValueText);
            if (names.Count < 2 || names.Any(n => !n.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or ':' or '-' or '$' or '@')))
            {
                return;
            }
            string array = "[" + string.Join(", ", names) + "]";
            int open = attr.Value.SourceSpan.Start - 1;
            QuickFix fix = open >= 0 && source.Text[open] == '"' && attr.Span.End > attr.Value.SourceSpan.End
                ? new QuickFix($"Write {array}", new TextChange(TextSpan.FromBounds(open, attr.Span.End), array))
                : null;
            Report(TrimmedSpan(attr.Value), DiagnosticSeverity.Warning, DiagnosticCodes.PreferArrayList,
                $"Write the list as an array: {attr.Name}: {array}.", fix != null ? new[] { fix } : null);
        }

        private static string Capital(string s) => s.StartsWith("<", StringComparison.Ordinal) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>The <c>if</c> and <c>elif</c>s before this branch, starting with the <c>if</c>; null if the chain doesn't start with one.</summary>
        private static List<MarkupElement> ChainBefore(MarkupElement e)
        {
            List<MarkupElement> chain = new List<MarkupElement>();
            for (MarkupElement b = PreviousElement(e); b != null; b = PreviousElement(b))
            {
                if (b.Name == "elif")
                {
                    chain.Insert(0, b);
                    continue;
                }
                if (b.Name == "if")
                {
                    chain.Insert(0, b);
                    return chain;
                }
                return null;
            }
            return null;
        }

        /// <summary>The last branch of the chain this branch is in.</summary>
        private static MarkupElement ChainEnd(MarkupElement e)
        {
            MarkupElement last = e;
            for (MarkupElement next = NextElement(e); next != null && (next.Name == "elif" || next.Name == "else") && last.Name != "else"; next = NextElement(next))
            {
                last = next;
            }
            return last;
        }

        private static bool IsLastStatement(MarkupElement e) => NextElement(e) == null;

        /// <summary>The element just before this one, skipping whitespace and comments; null if text or nothing is there.</summary>
        private static MarkupElement PreviousElement(MarkupElement e) => Sibling(e, -1);

        private static MarkupElement NextElement(MarkupElement e) => Sibling(e, 1);

        private static MarkupElement Sibling(MarkupElement e, int step)
        {
            List<MarkupNode> siblings = e.Parent?.Children;
            if (siblings == null)
            {
                return null;
            }
            for (int i = siblings.IndexOf(e) + step; i >= 0 && i < siblings.Count; i += step)
            {
                switch (siblings[i])
                {
                    case MarkupElement m:
                        return m;
                    case MarkupText t when !t.IsWhitespace:
                        return null;
                }
            }
            return null;
        }

        private static MarkupElement LastChildElement(MarkupElement branch)
        {
            MarkupElement last = null;
            foreach (MarkupNode child in branch.Children)
            {
                switch (child)
                {
                    case MarkupElement m:
                        last = m;
                        break;
                    case MarkupText t when !t.IsWhitespace:
                        last = null;
                        break;
                }
            }
            return last;
        }

        private static bool EndsWithExit(MarkupElement branch)
        {
            MarkupElement last = LastChildElement(branch);
            return last != null && ExitElements.Contains(last.Name);
        }

        /// <summary>
        /// Rewrites the chain so this branch no longer needs else: adds <paramref name="exit"/> to the earlier branches
        /// that don't end early (when given), then moves an else's body after the chain, or starts a new if for an elif.
        /// Null when the text around the chain isn't simple enough to change safely.
        /// </summary>
        private QuickFix EarlyExitFix(MarkupElement e, MarkupElement previous, List<MarkupElement> earlier, string exit)
        {
            string text = source.Text;
            bool code = result.Markup.Syntax == SourceSyntax.Code;
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            if (!e.StartTagClosed || !string.IsNullOrWhiteSpace(text.Substring(previous.Span.End, e.Span.Start - previous.Span.End)))
            {
                return null;
            }
            string indent = LineIndent(earlier[0].Span.Start);
            List<TextChange> changes = new List<TextChange>();

            if (exit != null)
            {
                string statement = code ? exit : "<" + exit + " />";
                foreach (MarkupElement branch in earlier.Where(b => !EndsWithExit(b)))
                {
                    MarkupElement last = LastChildElement(branch);
                    if (last == null)
                    {
                        return null;
                    }
                    int at = InsertionPoint(last.Span.End, branch.ContentSpan.End, code);
                    string inserted = LineOf(last.Span.Start) == LineOf(branch.Span.Start)
                        ? (code ? "; " : " ") + statement
                        : newline + LineIndent(last.Span.Start) + statement;
                    changes.Add(new TextChange(new TextSpan(at, 0), inserted));
                }
            }

            if (e.Name == "else")
            {
                string body = e.SelfClosing ? string.Empty : Dedent(text.Substring(e.ContentSpan.Start, e.ContentSpan.Length), indent, newline);
                changes.Add(new TextChange(TextSpan.FromBounds(previous.Span.End, e.Span.End), body.Length == 0 ? string.Empty : newline + body));
                return new QuickFix($"Remove the else and put its body after the {(code ? "if" : "<if>")}", changes.ToArray());
            }

            if (code)
            {
                MarkupAttribute test = e.GetAttribute("test");
                if (test?.Value == null)
                {
                    return null;
                }
                changes.Add(new TextChange(TextSpan.FromBounds(previous.Span.End, test.Value.SourceSpan.Start), newline + indent + "if "));
                return new QuickFix($"Change {(source[e.NameSpan] == "else" ? "else if" : "elif")} to a separate if", changes.ToArray());
            }

            changes.Add(new TextChange(TextSpan.FromBounds(previous.Span.End, e.NameSpan.End), newline + indent + "<if"));
            if (e.EndNameSpan is TextSpan endName)
            {
                changes.Add(new TextChange(endName, "if"));
            }
            return new QuickFix("Change <elif> to a separate <if>", changes.ToArray());
        }

        /// <summary>Where a new statement goes after one ending at <paramref name="end"/>: after a trailing // comment on the same line, if there is one.</summary>
        private int InsertionPoint(int end, int limit, bool code)
        {
            string text = source.Text;
            if (!code)
            {
                return end;
            }
            int p = end;
            while (p < limit && (text[p] == ' ' || text[p] == '\t'))
            {
                p++;
            }
            if (p + 1 < limit && text[p] == '/' && text[p + 1] == '/')
            {
                while (p < limit && text[p] != '\n' && text[p] != '\r')
                {
                    p++;
                }
                return p;
            }
            return end;
        }

        private int LineOf(int offset) => source.GetPosition(offset).Line;

        /// <summary>The spaces and tabs at the start of the line that contains the offset.</summary>
        private string LineIndent(int offset)
        {
            string text = source.Text;
            int start = offset;
            while (start > 0 && text[start - 1] != '\n')
            {
                start--;
            }
            int end = start;
            while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            {
                end++;
            }
            return text.Substring(start, end - start);
        }

        /// <summary>A block's content re-indented to <paramref name="indent"/>, without its leading and trailing blank lines.</summary>
        private static string Dedent(string content, string indent, string newline)
        {
            List<string> lines = content.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
            while (lines.Count > 0 && lines[0].Length == 0)
            {
                lines.RemoveAt(0);
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }
            if (lines.Count == 0)
            {
                return string.Empty;
            }
            int common = lines.Where(l => l.Length > 0).Min(l => l.Length - l.TrimStart(' ', '\t').Length);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(newline);
                }
                if (lines[i].Length > 0)
                {
                    sb.Append(indent).Append(lines[i].Substring(common));
                }
            }
            return sb.ToString();
        }
    }
}
