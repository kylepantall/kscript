using KScript.Syntax.Markup;
using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Metadata;
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
                        Parameters = s.Parameters.Select(p => (p.Label, WithAccepts(p.Deprecated != null ? DeprecationLine(p.Deprecated, p.Name) + p.Description : p.Description, p.Values.Count > 0 ? null : p.Type))).ToList(),
                        Details = s.Parameters.Select(p => (p.Name, p.Values.Count > 0 ? p.Values : p.Type == KType.Type ? TypeNames(analysis) : NativeValues(analysis, p.Type), p.IsFlag)).ToList(),
                        DeprecatedParameters = s.Parameters.Where(p => p.Deprecated != null).Select(p => p.Name).ToList(),
                    });
                }
            }

            // A parameter typed with a native type or enum says what it takes: timezone gives its description and some zones.
            string WithAccepts(string doc, string type)
            {
                string name = type != null ? SplitType(type).Name : null;
                if (name == null || (EnumValuesOf(analysis, name) == null && KScript.Metadata.NativeTypes.Get(name) == null))
                {
                    return doc;
                }
                string accepts = EnumValuesOf(analysis, name) is IReadOnlyList<string> members
                    ? $"One of {string.Join(", ", members.Take(12))}{(members.Count > 12 ? ", ..." : string.Empty)}."
                    : KScript.Metadata.NativeTypes.Get(name).Description;
                return string.IsNullOrEmpty(doc) ? accepts : doc + "\n\n" + accepts;
            }

            void AddFunction(Symbol fn)
            {
                DocComment doc = fn.Doc;
                signatures.Add(new SignatureInfo
                {
                    Label = fn.TypedSignature,
                    Documentation = doc != null ? doc.ToMarkdown(includeParams: false) : fn.Description,
                    Parameters = fn.Parameters.Select(p => (p.Label, WithAccepts(doc?.ParamText(p.Name), p.Type))).ToList(),
                    Details = fn.Parameters.Select(p => (p.Name,
                        p.Type != null ? EnumValuesOf(analysis, SplitType(p.Type).Name) ?? NativeValues(analysis, p.Type) : null,
                        p.Type == KType.Bool)).ToList(),
                    ItemValues = fn.Parameters.Select(p => p.Type != null && p.Type.EndsWith("[]", StringComparison.Ordinal)
                        ? EnumValuesOf(analysis, p.Type.Substring(0, p.Type.Length - 2).Trim()) ?? NativeValues(analysis, p.Type.Substring(0, p.Type.Length - 2).Trim())
                        : null).ToList(),
                });
            }

            // @accounts.sign_in(...) or math.sqrt(...): the imported script's function, or the package's command.
            void AddAliasMember(string aliasName, string memberName)
            {
                if (!(analysis.RootScope?.Lookup(aliasName, int.MaxValue) is Symbol { Kind: SymbolKind.Import } import))
                {
                    return;
                }
                if (import.Members.TryGetValue(memberName, out Symbol fn) && fn.Kind == SymbolKind.Function)
                {
                    AddFunction(fn);
                }
                else if (import.Package != null && Catalog.GetCommand(memberName) is CallableInfo command && command.Package == import.Package)
                {
                    AddCallable(command);
                }
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
                    else if (c.Name.IndexOf('.') is int dot && dot > 0)
                    {
                        AddAliasMember(c.Name.Substring(0, dot), c.Name.Substring(dot + 1));
                    }
                    break;
                }

                case CallNode call when call.Callee is MemberNode member && member.Target is IdentifierNode alias:
                    piped = PipeInto(call) != null ? 1 : 0;
                    AddAliasMember(alias.Name, member.Name);
                    break;

                case MethodCallNode m:
                {
                    TypeShape receiver = InferAt(analysis, region, m.Target);
                    string type = new TypeInference(Catalog, analysis).ReceiverType(m.Name, receiver);
                    foreach (CallableInfo method in Catalog.GetMethods(m.Name, type).Where(x => KType.AcceptsMethod(x.ReceiverType, x.Name, type)))
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
                        string type = new TypeInference(Catalog, analysis).ReceiverType(id.Name, receiver);
                        foreach (CallableInfo method in Catalog.GetMethods(id.Name, type).Where(x => KType.AcceptsMethod(x.ReceiverType, x.Name, type)))
                        {
                            AddCallable(method);
                        }
                    }
                    break;
                }
            }
            return signatures;
        }

        /// <summary>
        /// A value as an argument is written: a word (an enum value, en, GBP) or a duration (30s) as it is, anything
        /// else (Europe/London, image/png, @daily) in quotes, since an argument reads unquoted text as code.
        /// </summary>
        internal static string ArgumentText(string value) =>
            value.Length > 0 && (Lexer.IsIdentStart(value[0]) || char.IsDigit(value[0])) && value.All(c => Lexer.IsIdentChar(c) || c == '.') ? null : "'" + value + "'";

        /// <summary>The values a native type such as timezone suggests for an argument (quoted when they aren't one word), or null.</summary>
        private IReadOnlyList<string> NativeValues(DocumentAnalysis analysis, string type)
        {
            if (type == null || KScript.Metadata.NativeTypes.Get(SplitType(type).Name) == null)
            {
                return null;
            }
            List<string> values = AcceptedValues(analysis, type).Select(v => v.Value).ToList();
            return values.Count > 0 ? values : null;
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
            items.AddRange(ColumnCompletions(analysis, region, node, named, replace));

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
                            InsertText = ArgumentText(value),
                            FilterText = value,
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
                            Deprecated = signature.DeprecatedParameters.Contains(name),
                            Documentation = $"Sets `{name}` to {(label.StartsWith("not ", StringComparison.Ordinal) ? "false" : "true")} in `{signature.Label}`.",
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
                        Deprecated = signature.DeprecatedParameters.Contains(name),
                        Documentation = (doc != null ? doc + "\n\n" : string.Empty) + $"Named argument of `{signature.Label}`.",
                        InsertText = name + ": ",
                        // An enum or yes/no parameter: show its values as soon as the name is in.
                        SuggestAgain = signature.Details != null && i < signature.Details.Count && (signature.Details[i].Values?.Count > 0 || signature.Details[i].Flag),
                        ReplaceSpan = replace,
                        FilterText = name,
                        SortText = (signature.DeprecatedParameters.Contains(name) ? "09" : "00") + i.ToString("D2"),
                    });
                }
            }
            return items;
        }

        /// <summary>
        /// The model's columns in a KScript.Data call: <c>User->where(‸)</c> offers <c>name:</c>, <c>->order_by(‸)</c>
        /// offers <c>name</c>, and <c>->with(‸)</c> the relations.
        /// </summary>
        private List<CompletionItem> ColumnCompletions(DocumentAnalysis analysis, ExpressionRegion region, ExprNode node, HashSet<string> named, TextSpan replace)
        {
            var items = new List<CompletionItem>();
            if (node is not MethodCallNode m || DataColumns.RecordOf(InferAt(analysis, region, m.Target)) is not TypeShape record)
            {
                return items;
            }
            bool bare = DataColumns.Bare.Contains(m.Name) || DataColumns.Relations.Contains(m.Name);
            bool asNamed = DataColumns.Named.Contains(m.Name) && !bare;
            if (!bare && !asNamed)
            {
                return items;
            }
            foreach (FieldInfo field in record.Fields.Values)
            {
                if (named.Contains(field.Name) || DataColumns.Relations.Contains(m.Name) != DataColumns.IsRelation(field))
                {
                    continue;
                }
                items.Add(new CompletionItem
                {
                    Label = asNamed ? field.Name + ":" : field.Name,
                    Kind = CompletionKind.Field,
                    Detail = field.Shape?.ToString(),
                    Documentation = (field.Description != null ? field.Description + "\n\n" : string.Empty) + $"A {(DataColumns.IsRelation(field) ? "relation" : "field")} of {record.Name}.",
                    InsertText = asNamed ? field.Name + ": " : field.Name,
                    FilterText = field.Name,
                    ReplaceSpan = replace,
                    SortText = "000" + field.Name,
                });
            }
            return items;
        }

        /// <summary>The flag a bare-word argument sets (<c>recursive</c> or <c>not recursive</c>), or null.</summary>
        internal static string FlagName(ArgumentNode a) => a.Name == null ? Analysis.Analyzer.BareWord(a)?.Word : null;

        /// <summary>Inside an array argument (<c>@pack_all([large, ‸])</c>), the values of the enum its parameter is a list of.</summary>
        private List<CompletionItem> ListItemCompletions(DocumentAnalysis analysis, ExpressionRegion region, int wordStart, TextSpan replace)
        {
            string text = region.Source.Value;
            int p = wordStart;
            while (p > 0 && char.IsWhiteSpace(text[p - 1]))
            {
                p--;
            }
            if (p == 0 || (text[p - 1] != '[' && text[p - 1] != ','))
            {
                return new List<CompletionItem>();
            }
            (ExprNode node, ArgumentList args) = InnermostCall(region, wordStart);
            ArgumentNode arg = args?.Arguments.FirstOrDefault(a => a.Value is ListNode && a.Value.Span.Start < wordStart && wordStart <= a.Value.Span.End);
            if (arg == null)
            {
                return new List<CompletionItem>();
            }
            List<SignatureInfo> signatures = SignaturesFor(analysis, region, node, out int piped);
            int position = piped + args.Arguments.TakeWhile(a => a != arg).Count(a => a.Name == null);
            var items = new List<CompletionItem>();
            foreach (SignatureInfo signature in signatures.Where(s => s.ItemValues != null && s.Details != null))
            {
                int index = arg.Name != null ? signature.Details.ToList().FindIndex(d => d.Name == arg.Name) : position;
                if (index < 0 || index >= signature.ItemValues.Count || signature.ItemValues[index] == null)
                {
                    continue;
                }
                string name = signature.Details[index].Name;
                items.AddRange(signature.ItemValues[index].Select(value => new CompletionItem
                {
                    Label = value,
                    Kind = CompletionKind.Value,
                    Detail = name,
                    Documentation = $"A value for the items of `{name}` in `{signature.Label}`.",
                    ReplaceSpan = replace,
                    SortText = "000" + value,
                }));
            }
            return items.GroupBy(i => i.Label).Select(g => g.First()).ToList();
        }

        /// <summary>Right after <c>name:</c> in a call, the enum values that parameter takes.</summary>
        private List<CompletionItem> NamedValueCompletions(DocumentAnalysis analysis, ExpressionRegion region, int wordStart, TextSpan replace)
        {
            string text = region.Source.Value;
            // A value that isn't one word yet (Europe/Lo‸, en-G‸) is replaced whole.
            int rawStart = RawValueStart(text, wordStart);
            if (rawStart < wordStart && text[rawStart] != '$' && text[rawStart] != '@' && text[rawStart] != '\'' && text[rawStart] != '"')
            {
                replace = TextSpan.FromBounds(region.Source.ToSource(rawStart), replace.End);
                wordStart = rawStart;
            }
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
                        InsertText = ArgumentText(value),
                        FilterText = value,
                        ReplaceSpan = replace,
                        SortText = "000" + value,
                    })))
                .GroupBy(item => item.Label)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>
        /// The word in <c>$versions->only(‸)</c>, <c>->without(‸)</c> or <c>$x->is_a(‸)</c>: on a list of one native
        /// type, the filters it declares (releases, prereleases for semver), then the type names.
        /// </summary>
        private List<CompletionItem> WordArgumentCompletions(DocumentAnalysis analysis, ExpressionRegion region, int wordStart, TextSpan replace)
        {
            string text = region.Source.Value;
            int p = wordStart;
            while (p > 0 && char.IsWhiteSpace(text[p - 1]))
            {
                p--;
            }
            if (p == 0 || "(,:".IndexOf(text[p - 1]) < 0 || InnermostCall(region, wordStart).Node is not MethodCallNode { Name: "only" or "without" or "is_a" } call)
            {
                return new List<CompletionItem>();
            }
            var items = new List<CompletionItem>();
            if (call.Name != "is_a" && TypeInference.TypedListReceiver(InferAt(analysis, region, call.Target)) is string list
                && NativeTypes.Get(list[..^2]) is NativeTypeInfo native)
            {
                items.AddRange(native.ListFilters.Select(f => new CompletionItem
                {
                    Label = f.Name,
                    Kind = CompletionKind.Value,
                    Detail = $"{list} filter",
                    Documentation = f.Summary,
                    ReplaceSpan = replace,
                    SortText = "000" + f.Name,
                }));
            }
            IEnumerable<string> typeNames = new[] { KType.Number, KType.String, "int", "float", KType.Bool, KType.Date, KType.List, KType.Map, KType.Null, KType.Error }
                .Concat(NativeTypes.Names).Distinct(StringComparer.Ordinal);
            items.AddRange(typeNames.Select(t => new CompletionItem
            {
                Label = t,
                Kind = CompletionKind.Value,
                Detail = "type",
                Documentation = call.Name == "is_a" ? $"True when the value is {(NativeTypes.Get(t)?.WithArticle ?? "a " + t)}." : $"The items that are {(NativeTypes.Get(t)?.WithArticle ?? "a " + t)}.",
                ReplaceSpan = replace,
                SortText = "001" + t,
            }));
            return items;
        }

        /// <summary>The values a native type offers where it is expected (1h, 30m for a duration), or null for none.</summary>
        private static IReadOnlyList<string> NativeCompletions(string type) =>
            NativeTypes.Get(type)?.Completions is { Count: > 0 } values ? values : null;

        /// <summary>
        /// The start of the value of a variable declared with a native type (<c>wait: duration = ‸</c>, or
        /// <c>&lt;let type="duration" value="‸"&gt;</c>): the values the type offers.
        /// </summary>
        private static List<CompletionItem> DeclaredTypeValueCompletions(ExpressionRegion region, int wordStart, TextSpan replace) =>
            region.Attribute?.Name == "value" && string.IsNullOrWhiteSpace(region.Source.Value.Substring(0, Math.Min(wordStart, region.Source.Value.Length)))
                ? DeclaredTypeValueCompletions(region.Element, replace)
                : new List<CompletionItem>();

        /// <summary>The values the native type a &lt;let&gt; or &lt;const&gt; is declared with offers, or none.</summary>
        private static List<CompletionItem> DeclaredTypeValueCompletions(MarkupElement declaration, TextSpan replace)
        {
            if (declaration == null || ElementInfo.BareName(declaration.Name) is not ("let" or "const")
                || NativeTypes.Get(declaration.GetAttributeValue("type")?.Trim()) is not NativeTypeInfo native)
            {
                return new List<CompletionItem>();
            }
            return native.Completions.Select((value, i) => new CompletionItem
            {
                Label = value,
                Kind = CompletionKind.Value,
                Detail = native.Name,
                Documentation = native.Description,
                ReplaceSpan = replace,
                SortText = "000" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
            }).ToList();
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
                                return new HoverResult($"**{(negated ? "not " : string.Empty)}{word}**: flag, sets `{word}` to {(negated ? "false" : "true")} in `{signature.Label}`.", region.Source.ToSource(node.Span));
                            }
                            var owner = signature.Details.FirstOrDefault(d => d.Values != null && d.Values.Contains(word, StringComparer.OrdinalIgnoreCase));
                            if (owner.Name != null)
                            {
                                return new HoverResult($"**{word}**: a value of `{owner.Name}` ({string.Join(", ", owner.Values.Take(12))}{(owner.Values.Count > 12 ? ", ..." : string.Empty)}) in `{signature.Label}`.", region.Source.ToSource(node.Span));
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
