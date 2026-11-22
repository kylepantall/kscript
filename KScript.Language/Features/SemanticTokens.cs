using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;
using Group = System.Text.RegularExpressions.Group;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        private static readonly System.Text.RegularExpressions.Regex ParameterType =
            new System.Text.RegularExpressions.Regex(@"(?<![\w$'""])\$?[A-Za-z_]\w*\s*:\s*([A-Za-z_]\w*)");

        private static readonly HashSet<string> BuiltInTypeNames = new HashSet<string>(KType.Declarable.Concat(new[] { "number", "date", "any" }), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Type names, built-in and declared: params="count: int", &lt;let type="uuid(v: 4)"&gt;, field types, and the
        /// types a &lt;type&gt; is named and built on (<c>type Admin with [User, Auditable]</c>).
        /// </summary>
        private static void AddTypeNameTokens(DocumentAnalysis analysis, List<SemanticToken> tokens)
        {
            HashSet<string> declared = new HashSet<string>(analysis.Symbols.Where(s => s.EnumValues != null || s.Shape?.Type == KType.Type).Select(s => s.Name), StringComparer.Ordinal);
            bool IsType(string name) => BuiltInTypeNames.Contains(name) || declared.Contains(name);

            foreach (MarkupElement e in analysis.Markup.DescendantElements())
            {
                if (e.Name == "fn" && e.GetAttribute("params")?.Value is DecodedText ps)
                {
                    foreach (System.Text.RegularExpressions.Match m in ParameterType.Matches(ps.Value))
                    {
                        Group g = m.Groups[1];
                        if (IsType(g.Value))
                        {
                            tokens.Add(new SemanticToken(ps.ToSource(new TextSpan(g.Index, g.Length)), SemanticTokenType.Type, SemanticTokenModifiers.None));
                        }
                    }
                }
                if (e.Name is "let" or "set" or "state" or "const" or "field" && e.GetAttribute("type")?.Value is DecodedText type)
                {
                    AddTypeWords(type, IsType, tokens);
                }
                if (e.Name == "type")
                {
                    if (e.GetAttribute("with")?.Value is DecodedText with)
                    {
                        AddTypeWords(with, IsType, tokens);
                    }
                    if (e.GetAttribute("name")?.Value is DecodedText typeName)
                    {
                        AddTypeWords(typeName, IsType, tokens, SemanticTokenModifiers.Declaration);
                    }
                }
                if (e.Name == "enum" && e.GetAttribute("name")?.Value is DecodedText enumName)
                {
                    AddTypeWords(enumName, IsType, tokens, SemanticTokenModifiers.Declaration);
                }
            }
        }

        /// <summary>The type names in a type's text (<c>uuid(v: 4)[]?</c>, <c>User, Auditable</c>), skipping what's inside its arguments.</summary>
        private static void AddTypeWords(DecodedText text, Func<string, bool> isType, List<SemanticToken> tokens, SemanticTokenModifiers modifiers = SemanticTokenModifiers.None)
        {
            string s = text.Value;
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(')
                {
                    depth++;
                    continue;
                }
                if (c == ')')
                {
                    depth = Math.Max(0, depth - 1);
                    continue;
                }
                if (depth > 0 || !Lexer.IsIdentStart(c) || (i > 0 && Lexer.IsIdentChar(s[i - 1])))
                {
                    continue;
                }
                int end = i;
                while (end < s.Length && Lexer.IsIdentChar(s[end]))
                {
                    end++;
                }
                if (isType(s.Substring(i, end - i)))
                {
                    tokens.Add(new SemanticToken(text.ToSource(new TextSpan(i, end - i)), SemanticTokenType.Type, modifiers));
                }
                i = end - 1;
            }
        }

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
                List<CastNode> casts = region.Nodes.SelectMany(n => n.DescendantsAndSelf()).OfType<CastNode>().ToList();
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
                            case TokenKind.Duration:
                                tokens.Add(new SemanticToken(span, SemanticTokenType.Number, SemanticTokenModifiers.None));
                                break;
                            case TokenKind.String:
                            case TokenKind.Email:
                            case TokenKind.Uuid:
                            case TokenKind.Url:
                            case TokenKind.SlashPath:
                                tokens.Add(new SemanticToken(span, SemanticTokenType.String, SemanticTokenModifiers.None));
                                break;
                            case TokenKind.Regex:
                                tokens.Add(new SemanticToken(span, SemanticTokenType.Regexp, SemanticTokenModifiers.None));
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
                                if (prev.Text == "as" && casts.Any(c => c.TypeSpan.Start == t.Span.Start))
                                {
                                    tokens.Add(new SemanticToken(span, SemanticTokenType.Type, SemanticTokenModifiers.None));
                                }
                                else if (Lexer.Keywords.Contains(t.Text) || (t.Text == "as" && casts.Any(c => c.OperatorSpan.Start == t.Span.Start)))
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
                    // A <type> or <enum> by name (Age in with [Age], or where it is declared) is coloured as a type.
                    _ when r.Symbol.Shape.Type == KType.Type || r.Symbol.EnumValues != null => SemanticTokenType.Type,
                    _ => SemanticTokenType.Variable,
                };
                tokens.Add(new SemanticToken(r.Span, type,
                    (r.IsDeclaration ? SemanticTokenModifiers.Declaration : 0) | (r.Symbol.IsConst ? SemanticTokenModifiers.Readonly : 0)));
            }

            AddTypeNameTokens(analysis, tokens);

            return tokens.Where(t => t.Span.Length > 0).GroupBy(t => t.Span.Start).Select(g => g.First()).OrderBy(t => t.Span.Start).ToList();
        }
    }
}
