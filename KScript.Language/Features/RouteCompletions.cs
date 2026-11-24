using KScript.Language.Analysis;
using KScript.Metadata;
using KScript.Projects;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        /// <summary>Only offers route names and text where the name argument can be written.</summary>
        private List<CompletionItem> RouteNameCompletions(DocumentAnalysis analysis, ExpressionRegion region, int index)
        {
            (ExprNode node, ArgumentList args) = InnermostCall(region, index);
            if (node is not CommandCallNode call) return null;
            int source = region.Source.ToSource(call.NameSpan.Start);
            Scope scope = analysis.ScopeAt(source);
            int dot = call.Name.IndexOf('.');
            if (dot < 0 ? call.Name != "route" || scope.Lookup("route", source, includeMethods: true)?.Kind == SymbolKind.Function
                : call.Name[(dot + 1)..] != "route" || scope.Lookup(call.Name[..dot], source)?.Package != "KScript.Http")
                return null;
            if (Catalog.GetCommand("route") is not { Package: "KScript.Http" }) return null;

            SignatureInfo signature = SignaturesFor(analysis, region, call, out int piped, suggestValues: false).FirstOrDefault();
            if (signature == null || piped != 0) return null;

            string text = region.Source.Value;
            ArgumentNode active = args.Arguments.FirstOrDefault(a => a.Span.Contains(index));
            if (active != null && BoundParameterIndex(signature, args, active, piped) != 0) return null;
            ArgumentNode previous = active ?? args.Arguments.LastOrDefault(a => a.Span.End <= index);
            if (previous?.Value is LiteralNode { Kind: LiteralKind.String } literal
                && BoundParameterIndex(signature, args, previous, piped) == 0)
            {
                if (previous == active && InQuotedText(region, index) && text[literal.Span.Start] is '\'' or '"')
                {
                    int start = literal.Span.Start;
                    // An unclosed token can consume the call's closing ')' and the interpolation's '}'.
                    int end = Quotes.Close(text, start, literal.Span.End) >= 0 ? literal.Span.End : index;
                    return NamedRouteItems(analysis, region.Source.ToSource(TextSpan.FromBounds(start, end)), Quotes.Decode(text, start + 1, index, text[start]));
                }
                if (literal.Span.End <= index && text.AsSpan(literal.Span.End, index - literal.Span.End).Trim().IsEmpty)
                    return new List<CompletionItem>();
                return null;
            }

            int wordStart = WordStart(text, index);
            int before = wordStart;
            while (before > 0 && char.IsWhiteSpace(text[before - 1])) before--;
            if (before == 0 || text[before - 1] is not ('(' or ':')
                || (text[before - 1] == '(' && before - 1 != args.Span.Start)
                || text[before - 1] == ':' && active?.Name != "name"
                || active?.Value is not (null or IdentifierNode or ErrorNode))
                return null;

            TextSpan replace = region.Source.ToSource(TextSpan.FromBounds(wordStart, index));
            List<CompletionItem> items = ParameterNameCompletions(analysis, region, wordStart, replace, out _);
            items.AddRange(NamedRouteItems(analysis, replace, text.Substring(wordStart, index - wordStart)));
            items.AddRange(VariableCompletions(analysis, region.Source.ToSource(index), replace, withSigil: true, textOnly: true));
            return items;
        }

        private List<CompletionItem> NamedRouteItems(DocumentAnalysis analysis, TextSpan replace, string prefix)
        {
            try
            {
                return ProjectRouteDocuments(ProjectFolder(analysis.Path))
                    .SelectMany(file => file.Document.DescendantElements())
                    .Where(e => e.Name is "Get" or "Post" or "Put" or "Patch" or "Delete" or "Head" or "Options" or "Any" or "Route")
                    .Select(e =>
                    {
                        string name = e.GetAttributeValue("name")?.Trim();
                        if (string.IsNullOrEmpty(name)) return null;
                        return string.Concat(e.Ancestors().Reverse().Where(a => ElementInfo.BareName(a.Name) is "Routes" or "Group")
                            .Select(a => a.GetAttributeValue("name")?.Trim())) + name;
                    })
                    .Where(name => name != null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && name.All(c => Lexer.IsIdentChar(c) || c is '.' or '-'))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .Take(MaxAcceptedValues)
                    .Select(name => new CompletionItem
                    {
                        Label = name,
                        Kind = CompletionKind.Value,
                        Detail = "named route",
                        InsertText = QuotedValue(name),
                        ReplaceSpan = replace,
                        FilterText = name,
                        SortText = "00" + name,
                    }).ToList();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ProjectConfigException)
            {
                return new List<CompletionItem>();
            }
        }
    }
}
