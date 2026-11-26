using KScript.Language.Analysis;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    /// <summary>A type shown in the editor after a name, as if it had been written: <c>name="people": list&lt;{ name: string }&gt;</c>.</summary>
    public sealed class InlayHint
    {
        public InlayHint(int offset, string label, string tooltip = null)
        {
            Offset = offset;
            Label = label;
            Tooltip = tooltip;
        }

        /// <summary>Where the hint goes (it is drawn just before the character at this offset).</summary>
        public int Offset { get; }

        public string Label { get; }

        /// <summary>Markdown shown when the pointer rests on the hint.</summary>
        public string Tooltip { get; }

        public override string ToString() => $"{Offset} {Label}";
    }

    public sealed partial class LanguageService
    {
        /// <summary>
        /// Type hints for the names a document declares: variables, loop variables, untyped parameters and the return
        /// type of functions. Values whose type is plain from the code (<c>value="3"</c>, <c>type="int"</c>) get none.
        /// </summary>
        public List<InlayHint> GetInlayHints(DocumentAnalysis analysis, TextSpan? within = null)
        {
            List<InlayHint> hints = new List<InlayHint>();
            HashSet<int> placed = new HashSet<int>();

            void Add(int offset, string label, string tooltip)
            {
                if ((within == null || (offset >= within.Value.Start && offset <= within.Value.End)) && placed.Add(offset))
                {
                    hints.Add(new InlayHint(offset, label, tooltip));
                }
            }

            foreach (Symbol s in analysis.Symbols)
            {
                if (s.SourcePath != null || s.DeclaringElement == null || s.DeclarationSpan.Length == 0)
                {
                    continue;
                }

                if (s.Kind == SymbolKind.Function)
                {
                    if (s.ReturnShape.IsVague || !string.IsNullOrWhiteSpace(s.DeclaringElement.GetAttributeValue("returns")))
                    {
                        continue;
                    }
                    // After params="..." when there is one, so the hint reads like a signature: params="x: int" → number.
                    MarkupAttribute parameters = s.DeclaringElement.GetAttribute("params");
                    int at = parameters?.Value != null ? parameters.Span.End : s.DeclarationSpan.End;
                    if (parameters?.Value == null && s.DeclaringElement.GetAttribute("name") is MarkupAttribute name)
                    {
                        at = name.Span.End;
                    }
                    if (analysis.Markup.Syntax == SourceSyntax.Code)
                    {
                        // fn name(a, b) =>: after the ')', so it reads as the signature's result: (a, b) → bool.
                        at = AfterParameterList(analysis.Source.Text, at);
                    }
                    Add(at, " → " + s.ReturnShape.Brief(), $"`@{s.Name}` returns\n\n```kscript\n{s.ReturnShape.Pretty()}\n```");
                    continue;
                }

                // A type, enum, model, error or job is named in its own declaration: there is no value type to show.
                if (s.IsTypeDeclaration)
                {
                    continue;
                }
                bool typed = s.Kind == SymbolKind.Variable || s.Kind == SymbolKind.Constant || s.Kind == SymbolKind.LoopVariable || s.Kind == SymbolKind.Parameter;
                if (!typed || s.Shape.IsVague || IsObvious(analysis, s))
                {
                    continue;
                }
                Add(s.DeclarationSpan.End, ": " + s.Shape.Brief(), $"```kscript\n${s.Name}: {s.Shape.Pretty()}\n```" + (s.TypeFromCalls ? "\n\nWhat every call in this file passes." : string.Empty));
            }

            AddParameterNames(analysis, Add);
            return hints.OrderBy(h => h.Offset).ToList();
        }

        /// <summary>
        /// Past the ')' that closes a .kscript fn's parameter list, from the end of its parameters or (with none) its
        /// name; <paramref name="at"/> when no list follows.
        /// </summary>
        private static int AfterParameterList(string text, int at)
        {
            int close = text.IndexOf(')', at);
            return close >= 0 && text.Substring(at, close - at).Trim() is "" or "(" ? close + 1 : at;
        }

        /// <summary>
        /// The parameter's name before an argument whose meaning isn't plain from the code: a number, text, yes/no,
        /// list or map written straight into a call that takes several arguments, as in
        /// <c>->pad_left(width: 10, char: '.')</c>. Variables, named arguments and one-argument calls get none.
        /// </summary>
        private void AddParameterNames(DocumentAnalysis analysis, Action<int, string, string> add)
        {
            foreach (ExpressionRegion region in analysis.Regions)
            {
                foreach (ExprNode node in region.Nodes.SelectMany(n => n.DescendantsAndSelf()))
                {
                    ArgumentList list = ArgumentsOf(node);
                    if (list == null || list.Arguments.Count < 2 || !list.Arguments.Any(a => a.Name == null && IsPlainValue(a.Value)))
                    {
                        continue;
                    }
                    List<SignatureInfo> signatures = SignaturesFor(analysis, region, node, out int piped).ToList();
                    SignatureInfo signature = signatures.FirstOrDefault(s => s.Parameters.Count >= list.Arguments.Count + piped);
                    if (signature == null || signatures.Count(s => s.Parameters.Count >= list.Arguments.Count + piped) > 1)
                    {
                        continue; // No signature fits, or several do and the names would be a guess.
                    }
                    for (int i = 0; i < list.Arguments.Count; i++)
                    {
                        ArgumentNode a = list.Arguments[i];
                        if (a.Name != null)
                        {
                            break; // After a named argument, positions no longer say which parameter is meant.
                        }
                        (string Label, string Documentation) parameter = signature.Parameters[i + piped];
                        if (!IsPlainValue(a.Value) || parameter.Label.Contains("...", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        string name = ParameterName(parameter.Label);
                        if (name.Length > 0)
                        {
                            add(region.Source.ToSource(a.Span.Start), name + ": ", parameter.Documentation);
                        }
                    }
                }
            }
        }

        private static bool IsPlainValue(ExprNode node) =>
            node is LiteralNode { Template: null } || node is UnaryNode { Operand: LiteralNode } || node is ListNode || node is MapNode;

        /// <summary>True when the declaration already says the type: a literal value, a type="..." or a typed parameter.</summary>
        private static bool IsObvious(DocumentAnalysis analysis, Symbol s)
        {
            MarkupElement e = s.DeclaringElement;
            if (s.Kind == SymbolKind.Parameter)
            {
                bool fnParameter = e.GetAttribute("params")?.Value is DecodedText ps && ps.SourceSpan.Contains(s.DeclarationSpan.Start);
                if (!fnParameter)
                {
                    // A lambda's parameter: only worth a hint when it holds a list or map (fn(p) => $p.name).
                    return s.Shape.Type != KType.Map && s.Shape.Type != KType.List;
                }
                // params="x: int" and params="count = 0" already show the type.
                string text = analysis.Source.Text;
                int end = s.DeclarationSpan.End;
                while (end < text.Length && text[end] == ' ')
                {
                    end++;
                }
                return end < text.Length && (text[end] == ':' || text[end] == '=');
            }
            if (s.Kind == SymbolKind.LoopVariable && e.GetAttribute("at")?.Value is DecodedText index && index.SourceSpan.Contains(s.DeclarationSpan.Start))
            {
                return true;
            }
            if (e.GetAttribute("type") != null)
            {
                return true;
            }

            // The value the name was given, written as a plain literal: value="3", value="'Kyle'", <let>x = true</let>.
            foreach (ExpressionRegion region in analysis.Regions.Where(r => r.Element == e))
            {
                if (region.Kind == RegionKind.Statements)
                {
                    // The statements that declare the name hold its assignment.
                    AssignmentNode a = region.Parse.Nodes.OfType<AssignmentNode>().FirstOrDefault(n => region.Source.ToSource(n.Span).Contains(s.DeclarationSpan.Start));
                    return a != null && IsLiteral(a.Value);
                }
                else if (region.Kind == RegionKind.Expression && (region.Attribute == null || region.Attribute.Name == "value") && e.Name != "for")
                {
                    return IsLiteral(region.Parse?.Root);
                }
            }
            return false;
        }

        /// <summary>
        /// A number, text or yes/no written as it is. Literals of a native type (30s, https://..., joan@example.com, a
        /// UUID, a /regex/) aren't: their type isn't plain from the text, so they keep their hint.
        /// </summary>
        private static bool IsLiteral(ExprNode node) =>
            node is LiteralNode { Kind: LiteralKind.Number or LiteralKind.String or LiteralKind.Bool or LiteralKind.Null }
            || node is UnaryNode { Operand: LiteralNode { Kind: LiteralKind.Number } };
    }
}
