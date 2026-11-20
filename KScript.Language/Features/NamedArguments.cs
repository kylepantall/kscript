using KScript.Syntax.Markup;
using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    /// <summary>
    /// Editor support for named arguments (<c>@known_folder(name: 'downloads')</c>): the call around the cursor and
    /// its signatures (shared with signature help), parameter-name completion, and hover on an argument's name.
    /// </summary>
    public sealed partial class LanguageService
    {
        /// <summary>The innermost call whose parentheses contain <paramref name="index"/>, or (null, null).</summary>
        private static (ExprNode Node, ArgumentList Args) InnermostCall(ExpressionRegion region, int index) =>
            region.Nodes.SelectMany(n => n.DescendantsAndSelf())
                .Select(n => (Node: n, Args: ArgumentsOf(n)))
                .Where(x => x.Args != null && index > x.Args.Span.Start && (index < x.Args.Span.End || !x.Args.Closed || index == x.Args.Span.End && !x.Args.Closed))
                .OrderBy(x => x.Args.Span.Length)
                .FirstOrDefault();

        /// <summary>The signatures of a call node; <paramref name="piped"/> is 1 when a pipe supplies the first argument.</summary>
        private List<SignatureInfo> SignaturesFor(DocumentAnalysis analysis, ExpressionRegion region, ExprNode node, out int piped)
        {
            var signatures = new List<SignatureInfo>();
            piped = 0;

            void AddCallable(CallableInfo c)
            {
                foreach (Signature s in c.Signatures)
                {
                    signatures.Add(new SignatureInfo
                    {
                        Label = $"{c.Prefix}{c.Name}({string.Join(", ", s.Parameters.Select(p => p.Label))})",
                        Documentation = c.Description,
                        Parameters = s.Parameters.Select(p => (p.Label, p.Description)).ToList(),
                    });
                }
            }

            void AddFunction(Symbol fn)
            {
                DocComment doc = fn.Doc;
                signatures.Add(new SignatureInfo
                {
                    Label = fn.TypedSignature,
                    Documentation = doc != null ? doc.ToMarkdown(includeParams: false) : fn.Description,
                    Parameters = fn.Parameters.Select(p => (p.Label, doc?.ParamText(p.Name))).ToList(),
                });
            }

            PipeNode PipeInto(ExprNode right) => region.Nodes.SelectMany(n => n.DescendantsAndSelf()).OfType<PipeNode>().FirstOrDefault(p => p.Right == right);

            switch (node)
            {
                case CommandCallNode c:
                {
                    Symbol fn = analysis.RootScope.Lookup(c.Name, int.MaxValue);
                    piped = PipeInto(c) != null ? 1 : 0;
                    if (fn?.Kind == SymbolKind.Function)
                    {
                        AddFunction(fn);
                    }
                    else if (Catalog.GetCommand(c.Name) is CallableInfo command)
                    {
                        AddCallable(command);
                    }
                    break;
                }

                case MethodCallNode m:
                {
                    TypeShape receiver = InferAt(analysis, region, m.Target);
                    foreach (CallableInfo method in Catalog.GetMethods(m.Name, receiver.Type).Where(x => KType.Accepts(x.ReceiverType, receiver.Type)))
                    {
                        AddCallable(method);
                    }
                    break;
                }

                case CallNode call when call.Callee is IdentifierNode id:
                {
                    Symbol fn = analysis.RootScope.Lookup(id.Name, int.MaxValue);
                    PipeNode pipe = PipeInto(call);
                    piped = pipe != null ? 1 : 0;
                    if (fn?.Kind == SymbolKind.Function)
                    {
                        AddFunction(fn);
                    }
                    else if (pipe != null)
                    {
                        piped = 0; // A method's receiver isn't one of its parameters.
                        TypeShape receiver = InferAt(analysis, region, pipe.Left);
                        foreach (CallableInfo method in Catalog.GetMethods(id.Name, receiver.Type).Where(x => KType.Accepts(x.ReceiverType, receiver.Type)))
                        {
                            AddCallable(method);
                        }
                    }
                    break;
                }
            }
            return signatures;
        }

        /// <summary>The name in a parameter label: <c>name?</c>, <c>rest...</c>, <c>count: int = 1</c>.</summary>
        private static string ParameterName(string label)
        {
            int end = 0;
            while (end < label.Length && Lexer.IsIdentChar(label[end]))
            {
                end++;
            }
            return label.Substring(0, end);
        }

        /// <summary>
        /// At the start of an argument (after '(' or ','), offers <c>name:</c> for each parameter of the call that
        /// isn't filled yet.
        /// </summary>
        private List<CompletionItem> ParameterNameCompletions(DocumentAnalysis analysis, ExpressionRegion region, int wordStart, TextSpan replace)
        {
            string text = region.Source.Value;
            int p = wordStart;
            while (p > 0 && char.IsWhiteSpace(text[p - 1]))
            {
                p--;
            }
            if (p == 0 || (text[p - 1] != '(' && text[p - 1] != ','))
            {
                return new List<CompletionItem>();
            }

            (ExprNode node, ArgumentList args) = InnermostCall(region, wordStart);
            if (node == null)
            {
                return new List<CompletionItem>();
            }

            List<SignatureInfo> signatures = SignaturesFor(analysis, region, node, out int piped);
            List<ArgumentNode> others = args.Arguments.Where(a => !(a.Span.Start <= wordStart && wordStart <= a.Span.End)).ToList();
            int positional = piped + others.Count(a => a.Name == null);
            var named = new HashSet<string>(others.Where(a => a.Name != null).Select(a => a.Name), StringComparer.Ordinal);

            var items = new List<CompletionItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (SignatureInfo signature in signatures)
            {
                for (int i = 0; i < signature.Parameters.Count; i++)
                {
                    (string label, string doc) = signature.Parameters[i];
                    string name = ParameterName(label);
                    if (name.Length == 0 || label.EndsWith("...", StringComparison.Ordinal) || i < positional || named.Contains(name) || !seen.Add(name))
                    {
                        continue;
                    }
                    items.Add(new CompletionItem
                    {
                        Label = name + ":",
                        Kind = CompletionKind.Parameter,
                        Detail = label,
                        Documentation = (doc != null ? doc + "\n\n" : string.Empty) + $"Named argument of `{signature.Label}`.",
                        InsertText = name + ": ",
                        ReplaceSpan = replace,
                        FilterText = name,
                        SortText = "00" + i.ToString("D2"),
                    });
                }
            }
            return items;
        }

        /// <summary>Hover on <c>name</c> in <c>name: value</c>: the parameter it fills.</summary>
        private HoverResult ArgumentNameHover(DocumentAnalysis analysis, ExpressionRegion region, int index)
        {
            foreach (ExprNode call in region.Nodes.SelectMany(n => n.DescendantsAndSelf()))
            {
                ArgumentList args = ArgumentsOf(call);
                ArgumentNode arg = args?.Arguments.FirstOrDefault(a => a.Name != null && a.NameSpan.Start <= index && index <= a.NameSpan.End);
                if (arg == null)
                {
                    continue;
                }
                foreach (SignatureInfo signature in SignaturesFor(analysis, region, call, out _))
                {
                    foreach ((string label, string doc) in signature.Parameters)
                    {
                        if (ParameterName(label) == arg.Name)
                        {
                            return new HoverResult($"```kscript\n{label}\n```\n\nParameter of `{signature.Label}`" + (doc != null ? "\n\n" + doc : string.Empty),
                                region.Source.ToSource(arg.NameSpan));
                        }
                    }
                }
                return null;
            }
            return null;
        }
    }
}
