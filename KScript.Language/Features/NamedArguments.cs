using KScript.Syntax.Markup;
using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Metadata;
using TypeRef = KScript.Runtime.TypeRef;
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
        private List<SignatureInfo> SignaturesFor(DocumentAnalysis analysis, ExpressionRegion region, ExprNode node, out int piped, bool suggestValues = true)
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
                        Details = s.Parameters.Select(p => (p.Name, p.Values.Count > 0 ? p.Values : suggestValues && p.Type == KType.Type ? TypeNames(analysis) : ValuesOf(p.Type), p.IsFlag)).ToList(),
                        ParameterTypes = s.Parameters.Select(p => p.Type).ToList(),
                        ModelParameters = s.Parameters.Where(p => p.AcceptsModel).Select(p => p.Name).ToList(),
                        DeprecatedParameters = s.Parameters.Where(p => p.Deprecated != null).Select(p => p.Name).ToList(),
                    });
                }
            }

            // Binding bare flags needs enum names. Ranking variables needs no native value suggestions.
            IReadOnlyList<string> ValuesOf(string type) => suggestValues ? ParameterValues(analysis, type)
                : type == null ? null : EnumValuesOf(analysis, type);

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
                    Documentation = fn.DocumentationWithErrors(includeParams: false),
                    Parameters = fn.Parameters.Select(p => (p.Label, WithAccepts(doc?.ParamText(p.Name), p.Type))).ToList(),
                    Details = fn.Parameters.Select(p => (p.Name, ValuesOf(p.Type), p.Type == KType.Bool)).ToList(),
                    ParameterTypes = fn.Parameters.Select(p => p.Type).ToList(),
                });
            }

            void AddErrorConstructor(string name, TypeShape instance)
            {
                var parameters = new List<(string Label, string Documentation)>
                {
                    ("message?: string", "The error message. You can also pass it as the first positional argument."),
                    ("cause?", "The earlier error, usually the value bound by a catch. Leave it out for a new error."),
                };
                if (instance == null)
                {
                    parameters.Add(("value?: any", "An extra payload field. Read it with the caught error's ['value'] lookup."));
                    parameters.Add(("fields...", "Built-in errors accept additional named fields."));
                }
                else
                {
                    parameters.AddRange(instance.Fields.Values.Where(field => !Analyzer.BaseErrorFields.Contains(field.Name))
                        .Select(field => ($"{field.Name}: {field.DeclaredType ?? field.Shape.Display()}", field.Description)));
                }
                signatures.Add(new SignatureInfo
                {
                    Label = $"{name}({string.Join(", ", parameters.Select(parameter => parameter.Label))})",
                    Documentation = "Creates an error value. Use throw to raise it when this statement runs. Pass fields by name.",
                    Parameters = parameters,
                    Details = parameters.Select(parameter => (ParameterName(parameter.Label), (IReadOnlyList<string>)null, false)).ToList(),
                    ParameterTypes = parameters.Select(parameter =>
                    {
                        int colon = parameter.Label.IndexOf(':');
                        return colon < 0 ? null : parameter.Label.Substring(colon + 1).Trim();
                    }).ToList(),
                });
            }

            void AddMethods(TypeShape receiver, string name)
            {
                if (analysis.DeclaredMethod(receiver, name) is Symbol declaration)
                {
                    AddFunction(declaration);
                    return;
                }
                string type = new TypeInference(Catalog, analysis).ReceiverType(name, receiver);
                foreach (CallableInfo method in Catalog.GetMethods(name, type).Where(x => KType.AcceptsMethod(x.ReceiverType, x.Name, type)))
                    AddCallable(method);
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
                    int offset = region.Source.ToSource(c.NameSpan.Start);
                    Symbol fn = analysis.ScopeAt(offset).Lookup(c.Name, offset, includeMethods: true);
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
                    AddMethods(InferAt(analysis, region, m.Target), m.Name);
                    break;
                }

                case CallNode call when call.Callee is IdentifierNode id:
                {
                    int offset = region.Source.ToSource(id.Span.Start);
                    Symbol fn = analysis.ScopeAt(offset).Lookup(id.Name, offset, includeMethods: true);
                    PipeNode pipe = PipeInto(call);
                    piped = pipe != null ? 1 : 0;
                    if (fn?.Kind == SymbolKind.Function)
                    {
                        AddFunction(fn);
                    }
                    else if (fn?.Shape.Type == KType.ErrorType)
                    {
                        AddErrorConstructor(id.Name, fn.Shape.Instance);
                    }
                    else if (fn == null && pipe == null && KScript.Runtime.ErrorTypes.BuiltIn.TryGetValue(id.Name, out var error)
                        && error.Name == id.Name)
                    {
                        AddErrorConstructor(id.Name, null);
                    }
                    else if (pipe != null)
                    {
                        piped = 0; // A method's receiver isn't one of its parameters.
                        AddMethods(InferAt(analysis, region, pipe.Left), id.Name);
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
            value.Length > 0 && (Lexer.IsIdentStart(value[0]) || char.IsDigit(value[0])) && value.All(c => Lexer.IsIdentChar(c) || c == '.') ? null : QuotedValue(value);

        private static bool QuoteArgumentChoices(DocumentAnalysis analysis, string type) =>
            type != null && IsFiniteRefinement(analysis, type) && IsTextualChoice(analysis, type);

        /// <summary>Scalar choices offered by the parameter's declared type. Lists offer their choices inside brackets.</summary>
        private IReadOnlyList<string> ParameterValues(DocumentAnalysis analysis, string type)
        {
            if (type == null || TypeRef.Parse(type) is not { ListDepth: 0 })
            {
                return null;
            }
            List<string> values = AcceptedValues(analysis, type).Select(v => v.Value).ToList();
            return values.Count > 0 ? values : null;
        }

        private static IEnumerable<CompletionItem> ParameterValueItems(DocumentAnalysis analysis, SignatureInfo signature, int index, TextSpan replace)
        {
            (string name, IReadOnlyList<string> values, _) = signature.Details[index];
            bool quoted = QuoteArgumentChoices(analysis, signature.ParameterTypes[index]);
            return (values ?? Array.Empty<string>()).Select(value => new CompletionItem
            {
                Label = value,
                Kind = CompletionKind.Value,
                Detail = name,
                Documentation = $"A value of `{name}` in `{signature.Label}`.",
                InsertText = quoted ? QuotedValue(value) : ArgumentText(value),
                FilterText = value,
                ReplaceSpan = replace,
                SortText = "000" + value,
            });
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
        private List<CompletionItem> ParameterNameCompletions(DocumentAnalysis analysis, ExpressionRegion region, int wordStart, TextSpan replace, out bool acceptsModel)
        {
            acceptsModel = false;
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
            if (node == null || args.Arguments.Any(a => a.Value?.Span.ContainsStrict(p - 1) == true))
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
            foreach (SignatureInfo signature in signatures.Where(sig => sig.Details != null))
            {
                int index = PositionalParameter(signature, args, wordStart, piped);
                if (index >= signature.Details.Count) continue;
                acceptsModel |= signature.ModelParameters.Contains(signature.Details[index].Name);
                items.AddRange(ParameterValueItems(analysis, signature, index, replace).Where(item => seen.Add("=" + item.Label)));
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
                        SuggestAgain = signature.ModelParameters.Contains(name) || (signature.Details != null && i < signature.Details.Count && (signature.Details[i].Values?.Count > 0 || signature.Details[i].Flag)),
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

        /// <summary>After a named argument, positional values fill the remaining slots in order.</summary>
        private static int PositionalParameter(SignatureInfo signature, ArgumentList args, int at, int piped)
        {
            var reserved = args.Arguments.Where(a => a.Name != null).Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
            int slot = piped;
            bool afterNamed = false;
            void SkipReserved()
            {
                while (afterNamed && slot < signature.Details.Count && reserved.Contains(signature.Details[slot].Name)) slot++;
            }
            foreach (ArgumentNode previous in args.Arguments.TakeWhile(a => a.Span.Start < at))
            {
                if (previous.Name != null)
                {
                    afterNamed = true;
                    continue;
                }
                SkipReserved();
                if (Analyzer.BareWord(previous) is var (word, negated, _) && signature.Details.Any(d => d.Flag && d.Name == word)
                    && (negated || afterNamed || slot >= signature.Details.Count || signature.Details[slot].Values?.Contains(word, StringComparer.OrdinalIgnoreCase) != true))
                {
                    reserved.Add(word);
                    continue;
                }
                slot++;
            }
            SkipReserved();
            return slot;
        }

        private static int BoundParameterIndex(SignatureInfo signature, ArgumentList args, ArgumentNode arg, int piped) =>
            arg.Name != null ? signature.Details.ToList().FindIndex(d => d.Name == arg.Name)
                : PositionalParameter(signature, args, arg.Span.Start, piped);

        /// <summary>The type a call expects where a variable or list item starts. Overloads must agree.</summary>
        private string CallArgumentTypeAt(DocumentAnalysis analysis, int offset)
        {
            ExpressionRegion region = analysis.RegionAt(offset);
            if (region == null) return null;
            int index = region.Source.FromSource(offset);
            bool AtVariable(ExprNode value) => value is VariableNode or ErrorNode && value.Span.Start == index;
            (ExprNode call, ArgumentList args) = InnermostCall(region, index);
            ArgumentNode arg = args?.Arguments.FirstOrDefault(a => AtVariable(a.Value) || a.Value is ListNode list && list.Items.Any(AtVariable));
            if (arg == null) return null;

            var signatures = SignaturesFor(analysis, region, call, out int piped, suggestValues: false);
            var types = signatures.Select(s => s.ParameterTypes.ElementAtOrDefault(BoundParameterIndex(s, args, arg, piped))).Distinct().Take(2).ToList();
            if (types.Count != 1 || TypeRef.Parse(types[0]) is not TypeRef type) return null;
            return arg.Value is ListNode ? type.ListDepth > 0 ? type.Item.ToString() : null : types[0];
        }

        /// <summary>Promote compatible variables, retaining the existing proximity order within each group.</summary>
        private List<CompletionItem> RankVariables(DocumentAnalysis analysis, int offset, List<CompletionItem> variables, string type, bool matchingOnly = false)
        {
            if (type == null || new TypeInference(Catalog, analysis).TypeNamed(type, offset) is not TypeShape expected || expected.Type == KType.Any)
                return variables;
            Scope scope = analysis.ScopeAt(offset);
            TypeRef target = TypeRef.Parse(type);
            string name = target?.Name;
            bool isEnum = name != null && scope.Lookup(name, offset)?.EnumValues != null;
            var matching = scope.VisibleAt(offset)
                .Where(s => !s.IsTypeDeclaration && s.Kind is not (SymbolKind.Function or SymbolKind.Import) && !s.HoldsSeveralTypes
                    && MatchesValueShape(expected, s.Shape))
                .Where(s => !isEnum || TypeRef.Parse(SymbolValueType(analysis, s))?.Name == name)
                .Where(s => !matchingOnly || target?.Optional == true || TypeRef.Parse(SymbolValueType(analysis, s))?.Optional != true)
                .Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            if (matchingOnly) return variables.Where(i => matching.Contains(i.FilterText ?? i.Label)).ToList();
            if (matching.Count == 0) return variables;
            return variables.Select(i => matching.Contains(i.Label) ? WithSort(i, "!" + i.SortText) : i)
                .OrderByDescending(i => i.SortText.StartsWith('!')).ToList();
        }

        private static bool MatchesValueShape(TypeShape expected, TypeShape actual) => actual != null
            && expected.Type == actual.Type && (expected.Name == null || expected.Name == actual.Name)
            && (expected.Element == null || MatchesValueShape(expected.Element, actual.Element));

        private static string SymbolValueType(DocumentAnalysis analysis, Symbol symbol) =>
            symbol.Kind == SymbolKind.Parameter && symbol.DeclaringElement?.Name == "fn"
                ? analysis.Symbols.FirstOrDefault(f => f.Kind == SymbolKind.Function && f.DeclaringElement == symbol.DeclaringElement)?.Parameters.FirstOrDefault(p => p.Name == symbol.Name)?.Type
                : symbol.DeclaringElement?.GetAttributeValue("type");

        /// <summary>At the start of a list item, ask the bound parameter's element type for its available values.</summary>
        private List<CompletionItem> ListItemCompletions(DocumentAnalysis analysis, ExpressionRegion region, int wordStart, TextSpan replace)
        {
            string text = region.Source.Value;
            int start = RawValueStart(text, wordStart);
            int p = start;
            while (p > 0 && char.IsWhiteSpace(text[p - 1]))
            {
                p--;
            }
            if (p == 0 || (text[p - 1] != '[' && text[p - 1] != ','))
            {
                return new List<CompletionItem>();
            }
            (ExprNode node, ArgumentList args) = InnermostCall(region, wordStart);
            int delimiter = p - 1;
            ArgumentNode arg = args?.Arguments.FirstOrDefault(a => a.Value is ListNode list && list.Span.Contains(delimiter)
                && list.Span.Contains(wordStart) && !list.Items.Any(item => item.Span.ContainsStrict(delimiter)));
            if (arg == null)
            {
                return new List<CompletionItem>();
            }
            int caret = Math.Max(wordStart, region.Source.FromSource(replace.End));
            if (start < caret && text[start] is '$' or '@' or '\'' or '"')
            {
                return new List<CompletionItem>();
            }
            string typed = text.Substring(start, caret - start);
            replace = region.Source.ToSource(TextSpan.FromBounds(start, RawValueEnd(text, caret)));
            List<SignatureInfo> signatures = SignaturesFor(analysis, region, node, out int piped);
            var items = new List<CompletionItem>();
            foreach (SignatureInfo signature in signatures.Where(s => s.ParameterTypes != null))
            {
                int index = BoundParameterIndex(signature, args, arg, piped);
                if (index < 0 || index >= signature.ParameterTypes.Count || TypeRef.Parse(signature.ParameterTypes[index]) is not { ListDepth: 1 } type)
                {
                    continue;
                }
                string name = signature.Details[index].Name;
                string itemType = type.Item.ToString();
                bool quoted = QuoteArgumentChoices(analysis, itemType);
                items.AddRange(AcceptedValues(analysis, itemType, typed).Select(value => new CompletionItem
                {
                    Label = value.Value,
                    Kind = CompletionKind.Value,
                    Detail = value.Detail ?? name,
                    Documentation = value.Documentation ?? $"A value for the items of `{name}` in `{signature.Label}`.",
                    InsertText = quoted ? QuotedValue(value.Value) : ArgumentText(value.Value),
                    ReplaceSpan = replace,
                    FilterText = value.Value,
                    SortText = "000" + value.Value,
                }));
            }
            return items.GroupBy(i => i.Label).Select(g => g.First()).ToList();
        }

        /// <summary>Right after <c>name:</c> in a call, the enum values that parameter takes.</summary>
        private List<CompletionItem> NamedValueCompletions(DocumentAnalysis analysis, ExpressionRegion region, int wordStart, TextSpan replace, out bool acceptsModel)
        {
            acceptsModel = false;
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
            List<SignatureInfo> signatures = SignaturesFor(analysis, region, node, out _);
            acceptsModel = signatures.Any(sig => sig.ModelParameters.Contains(name));
            return signatures.Where(sig => sig.Details != null)
                .SelectMany(signature => Enumerable.Range(0, signature.Details.Count).Where(i => signature.Details[i].Name == name)
                    .SelectMany(i => ParameterValueItems(analysis, signature, i, replace)))
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
