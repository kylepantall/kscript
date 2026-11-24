using KScript.Language.Analysis;
using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        private List<CompletionItem> InheritanceCompletionsAt(DocumentAnalysis analysis, int offset)
        {
            if (offset == 0 || analysis.Kind != DocumentKind.Script || analysis.MarkupAt(offset) != analysis.Markup)
                return null;

            MarkupElement declaration = analysis.Markup.FindElementAt(offset - 1);
            if (declaration?.Name is not ("type" or "model" or "error") || offset > declaration.StartTagSpan.End)
                return null;

            bool model = declaration.Name == "model";
            string text = analysis.Source.Text;
            MarkupAttribute attribute = declaration.GetAttribute(model ? "with" : "extends");
            if (attribute?.Value is not DecodedText value)
                return null;

            int start = value.SourceSpan.Start;
            int end = value.SourceSpan.End;
            if (offset < start || offset > end &&
                (analysis.Markup.Syntax != SourceSyntax.Code || !text.AsSpan(end, offset - end).Trim().IsEmpty))
                return null;

            int wordStart = Math.Max(start, WordStart(text, offset));
            int wordEnd = offset;
            while (wordEnd < end && Lexer.IsIdentChar(text[wordEnd])) wordEnd++;
            TextSpan replace = TextSpan.FromBounds(wordStart, wordEnd);
            var previous = Lexer.Tokenize(text, start, wordStart);
            if (previous.Count != 0
                && (!model || (!previous[^1].Is("[") && !previous[^1].Is(","))
                    || previous.Count(t => t.Is("(")) != previous.Count(t => t.Is(")"))))
                return null;

            IEnumerable<Symbol> available = analysis.ScopeAt(offset).VisibleAt(offset)
                .Where(s => s.DeclaringElement != declaration && (s.SourcePath != null || s.DeclarationSpan.Start < declaration.Span.Start));
            if (declaration.Name == "error")
            {
                var names = available.Where(s => s.Shape.Type == KType.ErrorType).Select(s => s.Name)
                    .Concat(Runtime.ErrorTypes.BuiltIn.Keys).ToHashSet(StringComparer.Ordinal);
                return ErrorTypeCompletions(analysis, offset, replace).Where(item => names.Contains(item.Label)).ToList();
            }

            if (analysis.DeclaredTypes.TryGetValue(declaration, out TypeShape shape) && shape.RefinementBase != null)
                return new List<CompletionItem>();

            var entries = new List<CompletionItem>();
            if (!model)
            {
                entries.Add(new CompletionItem
                {
                    Label = ComponentScript.Contract,
                    Kind = CompletionKind.Value,
                    Detail = "web component contract",
                    ReplaceSpan = replace,
                    SortText = "0" + ComponentScript.Contract,
                });
            }

            var chosen = new HashSet<string>(StringComparer.Ordinal);
            if (model && Data.ModelComposition.TryReadBases(value.Value, out var bases, out _))
                chosen.UnionWith(bases.Select(b => b.Name));
            else if (model)
            {
                // An unfinished list can still have complete bases to its left.
                int depth = 0;
                foreach (Token token in previous)
                {
                    if (token.Is("(")) depth++;
                    else if (token.Is(")")) depth--;
                    else if (depth == 0 && token.Kind == TokenKind.Identifier) chosen.Add(token.Text);
                }
            }
            chosen.Remove(text.Substring(wordStart, wordEnd - wordStart));

            foreach (Symbol symbol in available.Where(s => s.Shape.Type == KType.Type
                && s.Shape.Instance is { Type: KType.Map, RefinementBase: null }
                && (model || s.Shape.Instance.Signatures.Count > 0)
                && !chosen.Contains(s.Name)))
            {
                entries.Add(new CompletionItem
                {
                    Label = symbol.Name,
                    Kind = CompletionKind.Value,
                    Detail = model ? "base type" : "type contract",
                    Documentation = SymbolMarkdown(analysis, symbol),
                    ReplaceSpan = replace,
                    SortText = "1" + symbol.Name,
                });
            }
            return entries;
        }
    }
}
