using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        /// <summary>
        /// KS6014: <c>timestamps</c> and <c>soft_deletes</c> are the Timestamps and SoftDeletes mixins KScript.Data
        /// ships, so a model extends them: <c>model Post extends Timestamps { }</c>. The fix takes the options out of
        /// the brackets (or the attributes, in XML) and adds the mixins to what the model extends. An option turned off
        /// (<c>timestamps: false</c>) is only taken out. KScript.Data's own scripts, which declare the mixins, are exempt.
        /// </summary>
        private void CheckRetiredModelOptions(MarkupElement e)
        {
            if (!IsModelElement(e) || (result.Path != null && KScript.Runtime.PackageSource.IsSource(result.Path)))
            {
                return;
            }
            string model = e.GetAttributeValue("name")?.Trim();
            var retired = e.Attributes.Where(a => ModelMixins.IsRetiredOption(a.Name) && a.NameSpan.Length > 0
                && (!IsCodeFile || e.ContentSpan.Length == 0 || a.NameSpan.Start < e.ContentSpan.Start)).ToList();

            MarkupAttribute with = e.GetAttribute("with");
            if (with?.ValueText is string bases)
            {
                foreach (string option in ModelMixins.ForOption.Keys.Where(o => System.Text.RegularExpressions.Regex.IsMatch(bases, @"\(\s*[^)]*\b" + o + @"\b")))
                    Report(with.Span, DiagnosticSeverity.Error, DiagnosticCodes.TypeForm,
                        ModelMixins.Message(option, model) + " Extend the mixin, or leave it out, instead of setting the option on a base.");
            }
            if (retired.Count == 0)
            {
                return;
            }

            var mixins = retired.Where(a => !IsOff(a)).Select(a => ModelMixins.ForOption[a.Name]).ToList();
            QuickFix fix = IsCodeFile ? CodeFix(e, retired, mixins, with) : XmlFix(retired, mixins, with);
            foreach (MarkupAttribute a in retired)
                Report(a.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.TypeForm, ModelMixins.Message(a.Name, model), fix == null ? null : new[] { fix });
        }

        private static bool IsOff(MarkupAttribute a) => a.ValueText?.Trim().ToLowerInvariant() is "false" or "no" or "off" or "0";

        private static string FixTitle(List<string> mixins) =>
            mixins.Count == 0 ? "Take out the option" : "Extend " + string.Join(" and ", mixins);

        /// <summary>model Post(timestamps, unique: [slug]) { }: model Post(unique: [slug]) extends Timestamps { }.</summary>
        private QuickFix CodeFix(MarkupElement e, List<MarkupAttribute> retired, List<string> mixins, MarkupAttribute with)
        {
            string text = source.Text;
            MarkupAttribute nameAttr = e.GetAttribute("name");
            int open = nameAttr?.Span.End ?? -1;
            int limit = e.ContentSpan.Length > 0 ? e.ContentSpan.Start : e.Span.End;
            if (open < 0 || open >= text.Length || text[open] != '(')
            {
                return null;
            }
            int close = MatchingParen(text, open, limit);
            if (close < 0)
            {
                return null;
            }
            // The options that stay, as written.
            var kept = SplitOptions(text.Substring(open + 1, close - open - 1))
                .Select(o => o.Trim())
                .Where(o => o.Length > 0 && !retired.Any(r => o == r.Name || o.StartsWith(r.Name + ":", StringComparison.Ordinal) || o.StartsWith(r.Name + " ", StringComparison.Ordinal)))
                .ToList();
            string brackets = kept.Count == 0 ? string.Empty : "(" + string.Join(", ", kept) + ")";
            var changes = new List<TextChange>();
            if (with?.Value != null)
            {
                changes.Add(new TextChange(TextSpan.FromBounds(open, close + 1), brackets));
                if (mixins.Count > 0)
                {
                    TextSpan bases = with.Value.SourceSpan;
                    string current = text.Substring(bases.Start, bases.Length).Trim();
                    changes.Add(current.StartsWith('[') && current.EndsWith(']')
                        ? new TextChange(new TextSpan(bases.Start + text.Substring(bases.Start, bases.Length).LastIndexOf(']'), 0), ", " + string.Join(", ", mixins))
                        : new TextChange(bases, "[" + current + ", " + string.Join(", ", mixins) + "]"));
                }
            }
            else
            {
                string extends = mixins.Count == 0 ? string.Empty : mixins.Count == 1 ? " extends " + mixins[0] : " extends [" + string.Join(", ", mixins) + "]";
                changes.Add(new TextChange(TextSpan.FromBounds(open, close + 1), brackets + extends));
            }
            return new QuickFix(FixTitle(mixins), changes.ToArray());
        }

        /// <summary>&lt;model name="Post" timestamps="true"&gt;: &lt;model name="Post" with="Timestamps"&gt;.</summary>
        private QuickFix XmlFix(List<MarkupAttribute> retired, List<string> mixins, MarkupAttribute with)
        {
            string text = source.Text;
            var changes = new List<TextChange>();
            foreach (MarkupAttribute a in retired)
            {
                int start = a.Span.Start;
                while (start > 0 && text[start - 1] is ' ' or '\t') start--;
                changes.Add(new TextChange(TextSpan.FromBounds(start, a.Span.End), string.Empty));
            }
            if (mixins.Count > 0)
            {
                if (with?.Value != null)
                {
                    TextSpan bases = with.Value.SourceSpan;
                    changes.Add(new TextChange(new TextSpan(bases.End, 0), ", " + string.Join(", ", mixins)));
                }
                else
                {
                    changes[0] = new TextChange(changes[0].Span, $" with=\"{string.Join(", ", mixins)}\"");
                }
            }
            return new QuickFix(FixTitle(mixins), changes.OrderBy(c => c.Span.Start).ToArray());
        }

        private static IEnumerable<string> SplitOptions(string text)
        {
            int depth = 0, start = 0;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c is '\'' or '"') quote = c;
                else if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    yield return text.Substring(start, i - start);
                    start = i + 1;
                }
            }
            yield return text.Substring(start);
        }
    }
}
