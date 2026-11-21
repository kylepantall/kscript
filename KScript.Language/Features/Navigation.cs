using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        #region Signature help

        public SignatureHelpResult GetSignatureHelp(DocumentAnalysis analysis, int offset)
        {
            Cursor cursor = Locate(analysis, offset);
            if (cursor.Region == null || !cursor.InCode)
            {
                return ElementSignatureHelp(analysis, offset);
            }
            return CallSignatureHelp(analysis, cursor) ?? ElementSignatureHelp(analysis, offset);
        }

        /// <summary>
        /// A .kscript statement such as <c>log(level: warn, ‸)</c> or <c>wait(‸)</c>: its element's attributes as the
        /// parameters, with the one being written (or the first not written yet) active.
        /// </summary>
        private SignatureHelpResult ElementSignatureHelp(DocumentAnalysis analysis, int offset)
        {
            if (analysis.Markup.Syntax != SourceSyntax.Code)
            {
                return null;
            }
            string text = analysis.Source.Text;
            foreach (MarkupElement e in analysis.Markup.DescendantNodes().OfType<MarkupElement>().Where(x => x.NameSpan.Length > 0).OrderByDescending(x => x.NameSpan.Start))
            {
                int open = e.NameSpan.End;
                if (open >= text.Length || text[open] != '(' || offset <= open || CodeStatementSnippets.ContainsKey(e.Name))
                {
                    continue;
                }
                int close = CloseOf(text, open);
                if (close >= 0 && offset > close)
                {
                    continue;
                }
                ElementInfo info = Catalog.GetElement(e.Name);
                if (info == null || info.Attributes.Count == 0)
                {
                    return null;
                }
                List<AttributeInfo> attributes = info.Attributes.Where(a => a.Deprecated == null || e.GetAttribute(a.Name) != null).ToList();
                string Label(AttributeInfo a) => a.Name + (a.Required ? string.Empty : "?") + ": " + ShortKind(a);
                SignatureInfo signature = new SignatureInfo
                {
                    Label = e.Name + "(" + string.Join(", ", attributes.Select(Label)) + ")",
                    Documentation = info.Description,
                    Parameters = attributes.Select(a => (Label(a), DeprecationLine(a.Deprecated, a.Name) + a.Description + OptionsLine(a))).ToList(),
                };

                // The attribute the cursor is in, else the first one not written yet.
                int argStart = LastTopLevelComma(text, open, offset) + 1;
                string written = text.Substring(argStart, offset - argStart).TrimStart();
                int colon = written.IndexOf(':');
                string name = colon > 0 ? written.Substring(0, colon).Trim() : null;
                int active = name != null ? attributes.FindIndex(a => a.Name == name) : -1;
                if (active < 0)
                {
                    active = attributes.FindIndex(a => !e.Attributes.Any(x => x.Name == a.Name && !(x.Span.Start <= offset && offset <= x.Span.End)));
                }
                // Every attribute written: none is active (an index past the end highlights nothing).
                SignatureHelpResult help = new SignatureHelpResult { ActiveParameter = active < 0 ? attributes.Count : active };
                help.Signatures.Add(signature);
                return help;
            }
            return null;
        }

        /// <summary>An attribute's type as a signature shows it: <c>trace|debug|info</c>, <c>text</c>, <c>any</c>, <c>bool</c>.</summary>
        private static string ShortKind(AttributeInfo a) => a.Kind switch
        {
            AttributeKind.Enum when a.Options.Count > 0 => string.Join("|", a.Options),
            AttributeKind.Expression => "any",
            AttributeKind.Template => "text",
            AttributeKind.Bool => "bool",
            _ => KindLabel(a.Kind),
        };

        /// <summary>The ')' that closes the '(' at <paramref name="open"/>, or -1 when it isn't closed.</summary>
        private static int CloseOf(string text, int open) => Scan(text, open, text.Length).Close;

        /// <summary>The last comma between the '(' at <paramref name="open"/> and <paramref name="end"/> that separates its arguments, or <paramref name="open"/>.</summary>
        private static int LastTopLevelComma(string text, int open, int end) => Scan(text, open, end).Comma;

        /// <summary>Walks the arguments after the '(' at <paramref name="open"/>, skipping strings and nested brackets.</summary>
        private static (int Comma, int Close) Scan(string text, int open, int end)
        {
            int depth = 0;
            int comma = open;
            char quote = '\0';
            for (int i = open + 1; i < Math.Min(end, text.Length); i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    quote = c == quote ? '\0' : quote;
                }
                else if (c == '\'' || c == '"')
                {
                    quote = c;
                }
                else if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' && depth == 0)
                {
                    return (comma, i);
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    comma = i;
                }
            }
            return (comma, -1);
        }

        private SignatureHelpResult CallSignatureHelp(DocumentAnalysis analysis, Cursor cursor)
        {

            ExpressionRegion region = cursor.Region;
            int index = cursor.LocalIndex;

            (ExprNode node, ArgumentList args) = InnermostCall(region, index);
            if (node == null)
            {
                return null;
            }

            int active = CountCommas(region.Source.Value, args.Span.Start + 1, index);
            SignatureHelpResult help = new SignatureHelpResult();
            help.Signatures.AddRange(SignaturesFor(analysis, region, node, out int piped));
            active += piped;

            // A named argument (name: value) highlights the parameter of that name.
            ArgumentNode current = args.Arguments.LastOrDefault(a => a.Span.Start <= index);
            if (current?.Name != null && CountCommas(region.Source.Value, current.Span.Start, index) == 0)
            {
                int signature = help.Signatures.FindIndex(sig => sig.Parameters.Any(p => ParameterName(p.Label) == current.Name));
                if (signature >= 0)
                {
                    help.ActiveSignature = signature;
                    help.ActiveParameter = help.Signatures[signature].Parameters.ToList().FindIndex(p => ParameterName(p.Label) == current.Name);
                    return help;
                }
            }

            if (help.Signatures.Count == 0)
            {
                return null;
            }

            int count = Math.Max(0, active) + 1;
            int best = help.Signatures.FindIndex(s => s.Parameters.Count >= count || s.Parameters.Any(p => p.Label.EndsWith("...")));
            help.ActiveSignature = best < 0 ? 0 : best;
            help.ActiveParameter = active;
            return help;
        }

        private static ArgumentList ArgumentsOf(ExprNode node) => node switch
        {
            CommandCallNode c => c.ArgumentList,
            MethodCallNode m => m.ArgumentList,
            CallNode call => call.ArgumentList,
            _ => null,
        };

        private static int CountCommas(string text, int start, int end)
        {
            int depth = 0;
            int commas = 0;
            char quote = '\0';
            for (int i = start; i < Math.Min(end, text.Length); i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                }
                else if (c == '\'' || c == '"')
                {
                    quote = c;
                }
                else if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    commas++;
                }
            }
            return commas;
        }

        #endregion

        #region Definition and references

        public List<SourceLocation> GetDefinition(DocumentAnalysis analysis, int offset)
        {
            List<SourceLocation> locations = new List<SourceLocation>();

            SymbolReference reference = analysis.ReferenceAt(offset);
            if (reference != null)
            {
                Symbol s = reference.Symbol;
                if (analysis.ExternalDeclarations.TryGetValue(s, out ExternalLocation external) && reference.IsDeclaration)
                {
                    locations.Add(new SourceLocation(external.Path, external.Start, external.End));
                }
                else if (analysis.ExternalDeclarations.TryGetValue(s, out external))
                {
                    locations.Add(new SourceLocation(external.Path, external.Start, external.End));
                }
                else if (s.Kind == SymbolKind.Import && reference.IsDeclaration && s.SourcePath != null)
                {
                    locations.Add(new SourceLocation(s.SourcePath, new LinePosition(0, 0), new LinePosition(0, 0)));
                }
                else if (s.Kind != SymbolKind.BuiltIn)
                {
                    locations.Add(new SourceLocation(null, s.DeclarationSpan));
                }
                return locations;
            }

            DocumentLink link = analysis.LinkAt(offset);
            if (link != null)
            {
                locations.Add(new SourceLocation(link.TargetPath, new LinePosition(0, 0), new LinePosition(0, 0)));
                return locations;
            }

            // A field of a map literal: $person.name -> the 'name:' key.
            Cursor cursor = Locate(analysis, offset);
            if (cursor.Region != null && cursor.InCode && NodeAt(cursor.Region, cursor.LocalIndex) is MemberNode member && member.NameSpan.Contains(cursor.LocalIndex))
            {
                TypeShape target = InferAt(analysis, cursor.Region, member.Target);
                if (target.Fields.TryGetValue(member.Name, out FieldInfo field) && field.Declaration.HasValue)
                {
                    locations.Add(new SourceLocation(null, field.Declaration.Value));
                }
                else if (member.Target is IdentifierNode alias && analysis.RootScope.Lookup(alias.Name, int.MaxValue) is Symbol import && import.Kind == SymbolKind.Import)
                {
                    if (analysis.ExternalMembers.TryGetValue((import, member.Name), out ExternalLocation ext))
                    {
                        locations.Add(new SourceLocation(ext.Path, ext.Start, ext.End));
                    }
                    else if (import.SourcePath != null)
                    {
                        locations.Add(new SourceLocation(import.SourcePath, new LinePosition(0, 0), new LinePosition(0, 0)));
                    }
                }
            }
            return locations;
        }

        public List<SymbolReference> GetReferences(DocumentAnalysis analysis, int offset, bool includeDeclaration)
        {
            SymbolReference at = analysis.ReferenceAt(offset);
            if (at == null)
            {
                return new List<SymbolReference>();
            }
            return analysis.ReferencesTo(at.Symbol).Where(r => includeDeclaration || !r.IsDeclaration).OrderBy(r => r.Span.Start).ToList();
        }

        /// <summary>The span that would be renamed at the offset, or null if nothing can be renamed there.</summary>
        public TextSpan? PrepareRename(DocumentAnalysis analysis, int offset)
        {
            SymbolReference at = analysis.ReferenceAt(offset);
            return at != null && at.Symbol.CanRename && at.Symbol.Kind != SymbolKind.FrontMatter ? at.Span : null;
        }

        public RenameResult Rename(DocumentAnalysis analysis, int offset, string newName)
        {
            SymbolReference at = analysis.ReferenceAt(offset);
            if (at == null || !at.Symbol.CanRename || at.Symbol.Kind == SymbolKind.FrontMatter)
            {
                return new RenameResult { Error = "Nothing to rename here." };
            }

            string name = (newName ?? string.Empty).Trim().TrimStart('$', '@');
            if (name.Length == 0 || !Lexer.IsIdentStart(name[0]) || !name.All(Lexer.IsIdentChar) || Lexer.Keywords.Contains(name))
            {
                return new RenameResult { Error = $"'{newName}' isn't a valid name." };
            }

            if (name == at.Symbol.Name)
            {
                return new RenameResult();
            }
            Symbol clash = analysis.Symbols.FirstOrDefault(s => s != at.Symbol && s.Name == name &&
                (s.Kind == SymbolKind.Function) == (at.Symbol.Kind == SymbolKind.Function) &&
                (s.Kind == SymbolKind.BuiltIn || analysis.ReferencesTo(at.Symbol).Any(r => analysis.ScopeAt(r.Span.Start).Lookup(name, r.Span.Start) == s)
                    || analysis.ReferencesTo(s).Any(r => analysis.ScopeAt(r.Span.Start).Lookup(at.Symbol.Name, r.Span.Start) == at.Symbol)));
            if (clash != null)
            {
                return new RenameResult { Error = $"'{name}' is already used here." };
            }

            RenameResult result = new RenameResult();
            foreach (SymbolReference r in analysis.ReferencesTo(at.Symbol))
            {
                result.Changes.Add(new TextChange(r.Span, name));
            }
            return result;
        }

        #endregion

        #region Outline

        public List<OutlineItem> GetOutline(DocumentAnalysis analysis)
        {
            Dictionary<MarkupElement, OutlineItem> functions = new Dictionary<MarkupElement, OutlineItem>();
            List<OutlineItem> roots = new List<OutlineItem>();

            // Every import has its <import> element, so the declaring element is always there.
            foreach (Symbol s in analysis.Symbols.Where(s => s.DeclaringElement != null && (s.SourcePath == null || s.Kind == SymbolKind.Import)))
            {

                OutlineKind? kind = s.Kind switch
                {
                    SymbolKind.Function => OutlineKind.Function,
                    SymbolKind.Variable => OutlineKind.Variable,
                    SymbolKind.Constant => OutlineKind.Constant,
                    SymbolKind.Import => OutlineKind.Module,
                    _ => null,
                };
                if (kind == null)
                {
                    continue;
                }

                OutlineItem item = new OutlineItem
                {
                    Name = s.Kind == SymbolKind.Function ? s.Name : s.Kind == SymbolKind.Import ? s.Name : "$" + s.Name,
                    Detail = s.Kind == SymbolKind.Function ? s.TypedSignature : s.Kind == SymbolKind.Import ? s.Description : s.Shape.ToString(),
                    Kind = kind.Value,
                    Span = s.DeclaringElement.Span,
                    SelectionSpan = s.DeclarationSpan,
                };

                MarkupElement owner = s.DeclaringElement.Ancestors().FirstOrDefault(a => a.Name == "fn");
                if (s.Kind == SymbolKind.Function)
                {
                    functions[s.DeclaringElement] = item;
                }
                if (owner != null && functions.TryGetValue(owner, out OutlineItem parent))
                {
                    parent.Children.Add(item);
                }
                else
                {
                    roots.Add(item);
                }
            }
            return roots;
        }

        #endregion

        #region Quick fixes

        /// <summary>Fixes for the diagnostics that overlap a span.</summary>
        public List<(Diagnostic Diagnostic, QuickFix Fix)> GetQuickFixes(DocumentAnalysis analysis, TextSpan span) =>
            analysis.Diagnostics
                .Where(d => d.Fixes.Count > 0 && d.Span.Start <= span.End && span.Start <= d.Span.End)
                .SelectMany(d => d.Fixes.Select(f => (d, f)))
                .ToList();

        #endregion
    }
}
