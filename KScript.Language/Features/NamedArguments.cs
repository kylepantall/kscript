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
                        Details = s.Parameters.Select(p => (p.Name, p.Values.Count > 0 ? p.Values : p.Type == KType.Type ? TypeNames(analysis) : null, p.IsFlag)).ToList(),
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
                    Details = fn.Parameters.Select(p => (p.Name,
                        p.Type != null ? (IReadOnlyList<string>)analysis.Symbols.FirstOrDefault(x => x.Name == p.Type && x.EnumValues != null)?.EnumValues : null,
                        p.Type == KType.Bool)).ToList(),
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

            // Bare values of the enum parameter this argument lands on: @known_folder(‸) offers downloads, home...
            bool afterNamed = others.Any(a => a.Name != null && a.Span.End <= wordStart);
            foreach (SignatureInfo signature in signatures.Where(sig => sig.Details != null && !afterNamed && positional < sig.Details.Count))
            {
                (string name, IReadOnlyList<string> values, _) = signature.Details[positional];
                foreach (string value in values ?? Array.Empty<string>())
                {
                    if (seen.Add("=" + value))
                    {
                        items.Add(new CompletionItem
                        {
                            Label = value,
                            Kind = CompletionKind.Value,
                            Detail = name,
                            Documentation = $"A value of `{name}` in `{signature.Label}`.",
                            ReplaceSpan = replace,
                            SortText = "000" + value,
                        });
                    }
                }
            }

            // Yes/no parameters can be written as flags: recursive, not recursive.
            foreach (SignatureInfo signature in signatures.Where(sig => sig.Details != null))
            {
                for (int i = 0; i < signature.Details.Count; i++)
                {
                    (string name, _, bool flag) = signature.Details[i];
                    if (!flag || i < positional || named.Contains(name) || others.Any(a => LanguageService.FlagName(a) == name) || !seen.Add("!" + name))
                    {
                        continue;
                    }
                    foreach (string label in new[] { name, "not " + name })
                    {
                        items.Add(new CompletionItem
                        {
                            Label = label,
                            Kind = CompletionKind.Keyword,
                            Detail = "flag",
                            Documentation = $"Sets `{name}` to {(label.StartsWith("not ", StringComparison.Ordinal) ? "no" : "yes")} in `{signature.Label}`.",
                            ReplaceSpan = replace,
                            FilterText = label,
                            SortText = "01" + label,
                        });
                    }
                }
            }

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

        /// <summary>The flag a bare-word argument sets (<c>recursive</c> or <c>not recursive</c>), or null.</summary>
        internal static string FlagName(ArgumentNode a) => a.Name == null ? Analysis.Analyzer.BareWord(a)?.Word : null;

        /// <summary>Right after <c>name:</c> in a call, the enum values that parameter takes.</summary>
        private List<CompletionItem> NamedValueCompletions(DocumentAnalysis analysis, ExpressionRegion region, int wordStart, TextSpan replace)
        {
            string text = region.Source.Value;
            int p = wordStart;
            while (p > 0 && char.IsWhiteSpace(text[p - 1]))
            {
                p--;
            }
            if (p == 0 || text[p - 1] != ':')
            {
                return new List<CompletionItem>();
            }
            int end = p - 1, start = end;
            while (start > 0 && Lexer.IsIdentChar(text[start - 1]))
            {
                start--;
            }
            string name = text.Substring(start, end - start);
            (ExprNode node, ArgumentList args) = InnermostCall(region, wordStart);
            if (node == null || name.Length == 0)
            {
                return new List<CompletionItem>();
            }
            // Each value once, from the first signature that has it.
            return SignaturesFor(analysis, region, node, out _).Where(sig => sig.Details != null)
                .SelectMany(signature => signature.Details.Where(d => d.Name == name)
                    .SelectMany(d => (d.Values ?? Array.Empty<string>()).Select(value => new CompletionItem
                    {
                        Label = value,
                        Kind = CompletionKind.Value,
                        Detail = d.Name,
                        Documentation = $"A value of `{d.Name}` in `{signature.Label}`.",
                        ReplaceSpan = replace,
                        SortText = "000" + value,
                    })))
                .GroupBy(item => item.Label)
                .Select(g => g.First())
                .ToList();
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
                    // A bare word: an enum value or a flag.
                    ArgumentNode bare = args?.Arguments.FirstOrDefault(a => Analysis.Analyzer.BareWord(a) is var (_, _, n) && n.Span.Start <= index && index <= n.Span.End);
                    if (bare != null && Analysis.Analyzer.BareWord(bare) is var (word, negated, node))
                    {
                        foreach (SignatureInfo signature in SignaturesFor(analysis, region, call, out _).Where(sig => sig.Details != null))
                        {
                            var flag = signature.Details.FirstOrDefault(d => d.Flag && d.Name == word);
                            if (bare.Name == null && flag.Name != null)
                            {
                                return new HoverResult($"**{(negated ? "not " : string.Empty)}{word}**: flag, sets `{word}` to {(negated ? "no" : "yes")} in `{signature.Label}`.", region.Source.ToSource(node.Span));
                            }
                            var owner = signature.Details.FirstOrDefault(d => d.Values != null && d.Values.Contains(word, StringComparer.OrdinalIgnoreCase));
                            if (owner.Name != null)
                            {
                                return new HoverResult($"**{word}**: a value of `{owner.Name}` ({string.Join(", ", owner.Values)}) in `{signature.Label}`.", region.Source.ToSource(node.Span));
                            }
                        }
                    }
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
