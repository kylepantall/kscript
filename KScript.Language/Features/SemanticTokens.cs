using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        /// <summary>
        /// Tokens for the KScript inside attributes and text, so editors colour expressions instead of showing them as
        /// plain strings. Markup itself is left to the editor's XML/HTML grammar.
        /// </summary>
        public List<SemanticToken> GetSemanticTokens(DocumentAnalysis analysis)
        {
            List<SemanticToken> tokens = new List<SemanticToken>();
            Dictionary<int, SymbolReference> references = new Dictionary<int, SymbolReference>();
            foreach (SymbolReference r in analysis.References)
            {
                references.TryAdd(r.Span.Start, r);
            }

            foreach (ExpressionRegion region in analysis.Regions)
            {
                string text = region.Source.Value;
                foreach (TextSpan code in region.CodeSpans)
                {
                    List<Token> list = Lexer.Tokenize(text, code.Start, Math.Min(code.End, text.Length));
                    for (int i = 0; i < list.Count; i++)
                    {
                        Token t = list[i];
                        Token prev = i > 0 ? list[i - 1] : default;
                        Token next = i + 1 < list.Count ? list[i + 1] : default;
                        TextSpan span = region.Source.ToSource(t.Span);

                        switch (t.Kind)
                        {
                            case TokenKind.Number:
                                tokens.Add(new SemanticToken(span, SemanticTokenType.Number, SemanticTokenModifiers.None));
                                break;
                            case TokenKind.String:
                                tokens.Add(new SemanticToken(span, SemanticTokenType.String, SemanticTokenModifiers.None));
                                break;
                            case TokenKind.Variable:
                            {
                                references.TryGetValue(span.Start + 1, out SymbolReference r);
                                tokens.Add(new SemanticToken(span, r?.Symbol.Kind == SymbolKind.Parameter ? SemanticTokenType.Parameter : SemanticTokenType.Variable,
                                    (r?.Symbol.IsConst == true ? SemanticTokenModifiers.Readonly : 0)
                                    | (r?.Symbol.Kind == SymbolKind.BuiltIn ? SemanticTokenModifiers.DefaultLibrary : 0)
                                    | (r?.IsDeclaration == true ? SemanticTokenModifiers.Declaration : 0)));
                                break;
                            }
                            case TokenKind.Command:
                            {
                                bool builtIn = Catalog.GetCommand(t.Text) != null && analysis.RootScope.Lookup(t.Text, int.MaxValue)?.Kind != SymbolKind.Function;
                                tokens.Add(new SemanticToken(span, SemanticTokenType.Function, builtIn ? SemanticTokenModifiers.DefaultLibrary : 0));
                                break;
                            }
                            case TokenKind.Identifier:
                                if (Lexer.Keywords.Contains(t.Text))
                                {
                                    tokens.Add(new SemanticToken(span, SemanticTokenType.Keyword, SemanticTokenModifiers.None));
                                }
                                else if (prev.Is("->"))
                                {
                                    tokens.Add(new SemanticToken(span, SemanticTokenType.Method, SemanticTokenModifiers.DefaultLibrary));
                                }
                                else if (prev.Is(".") || prev.Is("?.") || next.Is(":"))
                                {
                                    tokens.Add(new SemanticToken(span, SemanticTokenType.Property, SemanticTokenModifiers.None));
                                }
                                else if (next.Is("("))
                                {
                                    tokens.Add(new SemanticToken(span, analysis.RootScope.Lookup(t.Text, int.MaxValue)?.Kind == SymbolKind.Function ? SemanticTokenType.Function : SemanticTokenType.Method, SemanticTokenModifiers.None));
                                }
                                else if (next.Is("."))
                                {
                                    tokens.Add(new SemanticToken(span, SemanticTokenType.Namespace, SemanticTokenModifiers.None));
                                }
                                else if (references.TryGetValue(span.Start, out SymbolReference r))
                                {
                                    tokens.Add(new SemanticToken(span, r.Symbol.Kind == SymbolKind.Parameter ? SemanticTokenType.Parameter : SemanticTokenType.Variable,
                                        r.IsDeclaration ? SemanticTokenModifiers.Declaration : 0));
                                }
                                break;
                            case TokenKind.Punctuation:
                                if (!"()[]{},;:".Contains(t.Text) || t.Text == ":" && false)
                                {
                                    tokens.Add(new SemanticToken(span, SemanticTokenType.Operator, SemanticTokenModifiers.None));
                                }
                                break;
                        }
                    }
                }
            }

            // Names declared in attributes (name="x", each="p", params="a, b") and function references.
            HashSet<int> covered = new HashSet<int>(tokens.Select(t => t.Span.Start));
            foreach (SymbolReference r in analysis.References)
            {
                if (covered.Contains(r.Span.Start) || covered.Contains(r.Span.Start - 1))
                {
                    continue;
                }
                SemanticTokenType type = r.Symbol.Kind switch
                {
                    SymbolKind.Function => SemanticTokenType.Function,
                    SymbolKind.Parameter => SemanticTokenType.Parameter,
                    SymbolKind.Import => SemanticTokenType.Namespace,
                    SymbolKind.FrontMatter => SemanticTokenType.Property,
                    _ => SemanticTokenType.Variable,
                };
                tokens.Add(new SemanticToken(r.Span, type,
                    (r.IsDeclaration ? SemanticTokenModifiers.Declaration : 0) | (r.Symbol.IsConst ? SemanticTokenModifiers.Readonly : 0)));
            }

            return tokens.Where(t => t.Span.Length > 0).GroupBy(t => t.Span.Start).Select(g => g.First()).OrderBy(t => t.Span.Start).ToList();
        }
    }
}
