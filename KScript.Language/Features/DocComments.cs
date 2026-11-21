using System.Text.RegularExpressions;
using KScript.Language.Analysis;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    /// <summary>
    /// The editor inside a doc comment (<c>///</c> lines in a .kscript file, <c>&lt;!--- --&gt;</c> in XML): tags such
    /// as <c>@param</c> and <c>@returns</c> are completed after <c>@</c>, parameter names after <c>@param</c> (or
    /// <c>@arg</c>), and hovering a tag explains it. Nothing else is offered in a doc comment.
    /// </summary>
    public sealed partial class LanguageService
    {
        /// <summary>Where a doc comment is: the start of the cursor's line of doc text, and the first offset after the comment.</summary>
        private readonly record struct DocCommentSpot(int LineContentStart, int CommentStart, int CommentEnd);

        private static readonly Regex TagTyped = new Regex(@"^\s*@([A-Za-z]*)$", RegexOptions.CultureInvariant);
        private static readonly Regex ParamNameTyped = new Regex(@"^\s*@([A-Za-z]+)\s+\$?([\w-]*)$", RegexOptions.CultureInvariant);
        private static readonly Regex TagsWritten = new Regex(@"(?m)^(?:\s*///)?\s*(?:<!---)?\s*@([A-Za-z]+)(?:[ \t]+\$?([\w-]+))?", RegexOptions.CultureInvariant);

        /// <summary>The doc comment the offset is in, or null.</summary>
        private static DocCommentSpot? DocCommentAt(DocumentAnalysis analysis, int offset)
        {
            string text = analysis.Source.Text;
            int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;

            if (analysis.Markup.Syntax == SourceSyntax.Code)
            {
                int slashes = SkipBlanks(text, lineStart);
                if (string.CompareOrdinal(text, slashes, "///", 0, 3) != 0 || offset < slashes + 3)
                {
                    return null;
                }
                int first = lineStart;
                while (first > 0)
                {
                    int previous = first == 1 ? 0 : text.LastIndexOf('\n', first - 2) + 1;
                    if (string.CompareOrdinal(text, SkipBlanks(text, previous), "///", 0, 3) != 0)
                    {
                        break;
                    }
                    first = previous;
                }
                int end = lineStart;
                while (end < text.Length && string.CompareOrdinal(text, SkipBlanks(text, end), "///", 0, 3) == 0)
                {
                    int newline = text.IndexOf('\n', end);
                    end = newline < 0 ? text.Length : newline + 1;
                }
                return new DocCommentSpot(slashes + 3, first, end);
            }

            int open = text.LastIndexOf("<!--", Math.Max(0, offset - 1), StringComparison.Ordinal);
            if (open < 0 || offset < open + 5 || open + 5 >= text.Length || text[open + 4] != '-' || text[open + 5] == '-')
            {
                return null;
            }
            int close = text.IndexOf("-->", open + 4, StringComparison.Ordinal);
            if (close >= 0 && close < offset)
            {
                return null;
            }
            return new DocCommentSpot(lineStart <= open ? open + 5 : lineStart, open, close < 0 ? text.Length : close + 3);
        }

        private static int SkipBlanks(string text, int i)
        {
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            {
                i++;
            }
            return i;
        }

        /// <summary>The function a doc comment documents: the first declaration after it, if that is a function.</summary>
        private static Symbol DocumentedFunction(DocumentAnalysis analysis, DocCommentSpot spot)
        {
            MarkupElement next = analysis.Markup.DescendantNodes().OfType<MarkupElement>()
                .Where(e => e.Span.Start >= spot.CommentEnd)
                .OrderBy(e => e.Span.Start)
                .FirstOrDefault();
            return next == null ? null : analysis.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Function && s.DeclaringElement == next);
        }

        /// <summary>The tags already written in the comment, and the parameters they describe.</summary>
        private static (HashSet<string> Tags, HashSet<string> Params) WrittenTags(string text, DocCommentSpot spot, int skipLineStart)
        {
            HashSet<string> tags = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            string comment = text.Substring(spot.CommentStart, spot.CommentEnd - spot.CommentStart);
            int skipLineEnd = text.IndexOf('\n', skipLineStart) is int nl && nl >= 0 ? nl : text.Length;
            foreach (Match m in TagsWritten.Matches(comment))
            {
                // The line being typed doesn't count, or its half-written tag would hide itself.
                int at = spot.CommentStart + m.Groups[1].Index - 1;
                if (at >= skipLineStart && at < skipLineEnd)
                {
                    continue;
                }
                string tag = DocComment.FindTag(m.Groups[1].Value) is DocTag t ? t.AliasOf ?? t.Name : m.Groups[1].Value;
                tags.Add(tag);
                if (tag == "param" && m.Groups[2].Success)
                {
                    names.Add(m.Groups[2].Value);
                }
            }
            return (tags, names);
        }

        /// <summary>Completion inside a doc comment, or null when the offset is not in one.</summary>
        private static List<CompletionItem> DocCommentCompletions(DocumentAnalysis analysis, int offset)
        {
            if (DocCommentAt(analysis, offset) is not DocCommentSpot spot)
            {
                return null;
            }
            string text = analysis.Source.Text;
            List<CompletionItem> items = new List<CompletionItem>();
            string typed = text.Substring(spot.LineContentStart, offset - spot.LineContentStart);
            Symbol fn = DocumentedFunction(analysis, spot);
            int lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
            (HashSet<string> written, HashSet<string> documented) = WrittenTags(text, spot, lineStart);
            List<FunctionParameter> undocumented = fn?.Parameters.Where(p => !documented.Contains(p.Name)).ToList() ?? new List<FunctionParameter>();

            if (TagTyped.Match(typed) is Match tagMatch && tagMatch.Success)
            {
                TextSpan replace = TextSpan.FromBounds(offset - tagMatch.Groups[1].Length, offset);
                int order = 0;
                foreach (DocTag tag in DocComment.KnownTags)
                {
                    order++;
                    string meaning = tag.AliasOf ?? tag.Name;
                    bool forFunctions = meaning == "param" || meaning == "returns" || meaning == "public";
                    if ((!tag.Repeats && written.Contains(meaning)) || (forFunctions && fn == null && DocumentedDeclarationExists(analysis, spot))
                        || (meaning == "param" && fn != null && undocumented.Count == 0))
                    {
                        continue;
                    }
                    items.Add(new CompletionItem
                    {
                        Label = "@" + tag.Name,
                        Kind = CompletionKind.Keyword,
                        Detail = tag.Takes.Length > 0 ? "@" + tag.Name + " " + tag.Takes : "@" + tag.Name,
                        Documentation = tag.Description,
                        InsertText = tag.Takes.Length > 0 ? tag.Name + " " : tag.Name,
                        FilterText = tag.Name,
                        // Aliases sort after the tag they stand for.
                        SortText = (tag.AliasOf == null ? "0" : "1") + order.ToString("D2"),
                        ReplaceSpan = replace,
                    });
                    if (tag.Name == "param")
                    {
                        // One item per parameter not described yet: @param who
                        foreach (FunctionParameter p in undocumented)
                        {
                            items.Add(new CompletionItem
                            {
                                Label = "@param " + p.Name,
                                Kind = CompletionKind.Parameter,
                                Detail = p.Label,
                                Documentation = $"Describes the parameter `{p.Name}` of {fn.Name}.",
                                InsertText = "param " + p.Name + " ",
                                FilterText = "param " + p.Name,
                                SortText = "00" + fn.Parameters.IndexOf(p).ToString("D2"),
                                ReplaceSpan = replace,
                            });
                        }
                    }
                }
                return items;
            }

            if (fn != null && ParamNameTyped.Match(typed) is Match nameMatch && nameMatch.Success && DocComment.IsParamTag(nameMatch.Groups[1].Value))
            {
                TextSpan replace = TextSpan.FromBounds(offset - nameMatch.Groups[2].Length, offset);
                foreach (FunctionParameter p in undocumented)
                {
                    items.Add(new CompletionItem
                    {
                        Label = p.Name,
                        Kind = CompletionKind.Parameter,
                        Detail = p.Label,
                        Documentation = $"A parameter of {fn.Name}.",
                        InsertText = p.Name + " ",
                        FilterText = p.Name,
                        SortText = fn.Parameters.IndexOf(p).ToString("D2"),
                        ReplaceSpan = replace,
                    });
                }
            }
            return items;
        }

        /// <summary>True when a declaration (function or not) follows the comment, so function-only tags can be judged.</summary>
        private static bool DocumentedDeclarationExists(DocumentAnalysis analysis, DocCommentSpot spot) =>
            analysis.Markup.DescendantNodes().OfType<MarkupElement>().Any(e => e.Span.Start >= spot.CommentEnd);

        /// <summary>Hover on a tag in a doc comment, or on the parameter an @param names; null elsewhere.</summary>
        private static HoverResult DocCommentHover(DocumentAnalysis analysis, int offset, DocCommentSpot spot)
        {
            string text = analysis.Source.Text;
            int lineEnd = text.IndexOf('\n', spot.LineContentStart);
            string line = text.Substring(spot.LineContentStart, (lineEnd < 0 ? text.Length : lineEnd) - spot.LineContentStart);
            Match m = Regex.Match(line, @"^(\s*)@([A-Za-z]+)(?:([ \t]+)(\$?)([\w-]+))?", RegexOptions.CultureInvariant);
            if (!m.Success || DocComment.FindTag(m.Groups[2].Value) is not DocTag tag)
            {
                return null;
            }
            int tagStart = spot.LineContentStart + m.Groups[1].Length;
            TextSpan tagSpan = TextSpan.FromBounds(tagStart, tagStart + 1 + tag.Name.Length);
            if (offset >= tagSpan.Start && offset <= tagSpan.End)
            {
                string alias = tag.AliasOf != null ? $"\n\nThe same as `@{tag.AliasOf}`." : string.Empty;
                return new HoverResult($"**@{tag.Name}**{(tag.Takes.Length > 0 ? " `" + tag.Takes + "`" : string.Empty)}\n\n{tag.Description}{alias}", tagSpan);
            }
            if (DocComment.IsParamTag(tag.Name) && m.Groups[5].Success)
            {
                int nameStart = spot.LineContentStart + m.Groups[5].Index;
                TextSpan nameSpan = TextSpan.FromBounds(nameStart - m.Groups[4].Length, nameStart + m.Groups[5].Length);
                if (offset >= nameSpan.Start && offset <= nameSpan.End && DocumentedFunction(analysis, spot) is Symbol fn)
                {
                    FunctionParameter p = fn.Parameters.FirstOrDefault(x => x.Name == m.Groups[5].Value);
                    return p != null
                        ? new HoverResult($"**{p.Label}**\n\nA parameter of {fn.Name}.", nameSpan)
                        : new HoverResult($"{fn.Name} has no parameter called `{m.Groups[5].Value}`.", nameSpan);
                }
            }
            return null;
        }
    }
}
