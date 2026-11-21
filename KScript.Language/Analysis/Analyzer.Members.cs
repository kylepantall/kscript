using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Help with the functions a type or job is expected to have, like C#'s "implement missing members" and
    /// "generate overrides": a job's missing <c>fn handle()</c> gets a fix that writes it, a job can add
    /// <c>fn failed(err)</c>, a joined type (<c>type Admin with [User]</c>) can override the methods it gets from
    /// its bases, and a method two bases both give must be declared, with a fix that writes it.
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <summary>A function a type or job can declare: its name, its parameters as written, and where it comes from.</summary>
        private sealed record MemberStub(string Name, string Params, string Comment);

        /// <summary>The fix that writes <c>fn handle()</c> into a job that has none.</summary>
        private QuickFix[] AddJobHandleFix(MarkupElement job)
        {
            TextChange change = AddMembersChange(job, new[] { new MemberStub("handle", null, "The job's work. Its fields are variables here, and $job says which attempt this is.") });
            return change == null ? null : new[] { new QuickFix("Add fn handle()", change) };
        }

        /// <summary>A job without <c>fn failed(err)</c> can add one, which runs when the job gives up.</summary>
        private void OfferJobFailed(MarkupElement job, bool hasFailed)
        {
            if (hasFailed || AddMembersChange(job, new[] { new MemberStub("failed", "err", "Runs once when the job gives up, with the last error as $err.") }) is not TextChange change)
            {
                return;
            }
            Offer(job.StartTagSpan, new QuickFix("Add fn failed(err), run when the job gives up", change));
        }

        /// <summary>
        /// For a type joined from others: each base method it doesn't declare can be overridden, and one that two
        /// bases give differently must be declared (as at run time), with a fix that writes it.
        /// </summary>
        private void CheckInheritedMembers(MarkupElement type)
        {
            List<string> bases = WithNames(type);
            if (bases.Count == 0)
            {
                return;
            }
            string typeName = type.GetAttributeValue("name")?.Trim();
            var own = new HashSet<string>(FnChildren(type).Select(f => f.GetAttributeValue("name")?.Trim()).Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);
            var inherited = new Dictionary<string, (MarkupElement Fn, string From)>(StringComparer.Ordinal);
            var conflicts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string baseName in bases)
            {
                MarkupElement baseElement = TypeElementNamed(TypeInference.WithoutTypeArguments(baseName));
                if (baseElement == null)
                {
                    continue;
                }
                foreach ((string name, MarkupElement fn) in MethodsOf(baseElement, new HashSet<MarkupElement>()))
                {
                    if (own.Contains(name))
                    {
                        continue;
                    }
                    if (!inherited.TryGetValue(name, out var first))
                    {
                        inherited[name] = (fn, baseName);
                    }
                    else if (first.Fn != fn && !conflicts.ContainsKey(name))
                    {
                        conflicts[name] = $"{first.From} and {baseName}";
                    }
                }
            }

            foreach ((string name, (MarkupElement fn, string from)) in inherited)
            {
                string parameters = fn.GetAttributeValue("params")?.Trim();
                MemberStub stub = new MemberStub(name, parameters, conflicts.TryGetValue(name, out string clash)
                    ? $"{clash} both have {name}: write the one {typeName} uses."
                    : $"Replaces {from}'s {name} in {typeName}.");
                if (AddMembersChange(type, new[] { stub }) is not TextChange change)
                {
                    continue;
                }
                var fix = new QuickFix(conflicts.ContainsKey(name) ? $"Declare fn {name} in {typeName}" : $"Override fn {name}({parameters}) from {from}", change);
                if (clash != null)
                {
                    Report(type.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration,
                        $"{typeName} gets the method '{name}' from {clash}. Declare fn {name} in {typeName} to choose.", new[] { fix });
                    continue;
                }
                Offer(type.StartTagSpan, fix);
            }
        }

        /// <summary>A type's methods, its own first, then those of the types it joins, left to right.</summary>
        private IEnumerable<(string Name, MarkupElement Fn)> MethodsOf(MarkupElement type, HashSet<MarkupElement> seen)
        {
            if (!seen.Add(type))
            {
                yield break;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (MarkupElement fn in FnChildren(type))
            {
                string name = fn.GetAttributeValue("name")?.Trim();
                if (!string.IsNullOrEmpty(name) && names.Add(name))
                {
                    yield return (name, fn);
                }
            }
            foreach (string baseName in WithNames(type))
            {
                if (TypeElementNamed(TypeInference.WithoutTypeArguments(baseName)) is MarkupElement baseElement)
                {
                    foreach ((string name, MarkupElement fn) in MethodsOf(baseElement, seen))
                    {
                        if (names.Add(name))
                        {
                            yield return (name, fn);
                        }
                    }
                }
            }
        }

        private IEnumerable<MarkupElement> FnChildren(MarkupElement e) => e.ChildElements.Where(c => IsKScriptElementName(c, "fn"));

        /// <summary>A code action offered on <paramref name="span"/>, without a problem to report.</summary>
        private void Offer(TextSpan span, QuickFix fix)
        {
            if (!result.Actions.Any(a => a.Span.Equals(span) && a.Fix.Title == fix.Title))
            {
                result.Actions.Add(new CodeAction(span, fix));
            }
        }

        /// <summary>
        /// The edit that adds functions at the end of a type's or job's body, in the file's own format and indented one
        /// step in from the element. Null when the element's body can't be found (an unfinished one).
        /// </summary>
        private TextChange AddMembersChange(MarkupElement e, IReadOnlyList<MemberStub> stubs)
        {
            string text = result.Source.Text;
            if (e.Span.End > text.Length || e.Span.IsEmpty)
            {
                return null;
            }
            string indent = LineIndent(text, e.Span.Start);
            string inner = indent + "  ";
            string body = string.Concat(stubs.Select(s => IsCode
                ? $"{inner}fn {s.Name}({s.Params}) {{\n{inner}  // {s.Comment}\n{inner}}}\n"
                : $"{inner}<fn name=\"{s.Name}\"{(string.IsNullOrEmpty(s.Params) ? string.Empty : $" params=\"{s.Params}\"")}>\n{inner}  <!-- {s.Comment} -->\n{inner}</fn>\n"));

            if (e.SelfClosing)
            {
                string whole = text.Substring(e.Span.Start, e.Span.Length);
                if (!IsCode)
                {
                    int slash = whole.LastIndexOf("/>", StringComparison.Ordinal);
                    if (slash < 0)
                    {
                        return null;
                    }
                    int from = slash;
                    while (from > 0 && whole[from - 1] == ' ')
                    {
                        from--;
                    }
                    return new TextChange(TextSpan.FromBounds(e.Span.Start + from, e.Span.Start + slash + 2), ">\n" + body + indent + "</" + e.Name + ">");
                }
                // type B with [User] {} or type B with [User]: open the braces.
                string trimmed = whole.TrimEnd();
                int end = e.Span.Start + trimmed.Length;
                if (trimmed.EndsWith('}') && trimmed.LastIndexOf('{') is int open && open >= 0 && trimmed.Substring(open + 1, trimmed.Length - open - 2).Trim().Length == 0)
                {
                    return new TextChange(TextSpan.FromBounds(e.Span.Start + open, end), "{\n" + body + indent + "}");
                }
                return new TextChange(new TextSpan(end, 0), " {\n" + body + indent + "}");
            }

            TextSpan content = e.ContentSpan;
            if (content.End > text.Length || content.End < content.Start)
            {
                return null;
            }
            string inside = text.Substring(content.Start, content.Length);
            int lastBreak = inside.LastIndexOf('\n');
            bool closingOnOwnLine = lastBreak >= 0 && inside.Substring(lastBreak + 1).Trim().Length == 0;
            bool hasMembers = inside.Trim().Length > 0;
            if (closingOnOwnLine)
            {
                // Insert on the line of the closing brace or tag, after a blank line when there is something above.
                return new TextChange(new TextSpan(content.Start + lastBreak + 1, 0), (hasMembers ? "\n" : string.Empty) + body);
            }
            return new TextChange(new TextSpan(content.End, 0), "\n" + (hasMembers ? "\n" : string.Empty) + body + indent);
        }

        private static string LineIndent(string text, int offset)
        {
            int lineStart = offset;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
            {
                lineStart--;
            }
            int i = lineStart;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            {
                i++;
            }
            return text.Substring(lineStart, i - lineStart);
        }
    }
}
