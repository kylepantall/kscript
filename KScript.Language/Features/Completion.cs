using KScript.Language.Analysis;
using KScript.Language.Catalog;
using TypeRef = KScript.Runtime.TypeRef;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        private static readonly (string Word, string Doc)[] ExpressionKeywords =
        {
            ("and", "Logical and: `$a and $b`."),
            ("or", "Logical or: `$a or $b`."),
            ("not", "Logical not: `not $a`."),
            ("eq", "Loose equality, the XML spelling of `==`: `'3' eq 3` is true."),
            ("ne", "Loose inequality, the XML spelling of `!=`."),
            ("lt", "Less than, the XML spelling of `<`: `$a lt $b`."),
            ("le", "Less than or equal, the XML spelling of `<=`."),
            ("gt", "Greater than, the XML spelling of `>`."),
            ("ge", "Greater than or equal, the XML spelling of `>=`."),
            ("as", "Checked conversion: `$value as Type`."),
            ("in", "Membership: `$ext in ['.cs', '.xml']`."),
            ("true", "The boolean true."),
            ("false", "The boolean false."),
            ("null", "No value."),
            ("fn", "A lambda: `fn(x) => $x * 2`."),
            ("match", "Match expression: `match $x { 1 => 'one', _ => 'other' }`."),
        };

        /// <summary>The comparison words, which are the XML spellings: a .kscript file writes the symbols.</summary>
        private static readonly HashSet<string> ComparisonWords = new HashSet<string> { "eq", "ne", "lt", "le", "gt", "ge" };

        /// <summary>
        /// Completions at an offset. <paramref name="trigger"/> is the character the editor asked after, if any: after
        /// <c>(</c> or <c>,</c> the list only shows inside a call that has named arguments or values to offer, so typing
        /// <c>(1 + 2)</c> or <c>[1, 2]</c> doesn't open it.
        /// </summary>
        public List<CompletionItem> GetCompletions(DocumentAnalysis analysis, int offset, string trigger = null)
        {
            // Typing the function's opening brace should leave Enter free to start its first statement.
            if (trigger == "{" && analysis.Markup.Syntax == SourceSyntax.Code && offset > 0 && analysis.Source.Text[offset - 1] == '{'
                && analysis.Markup.FindElementAt(offset - 1) is { Name: "fn" } fn && fn.StartTagSpan.End == offset - 1)
            {
                return new List<CompletionItem>();
            }
            List<CompletionItem> declarationOptions = InheritanceCompletionsAt(analysis, offset) ?? ModelOptionCompletionsAt(analysis, offset);
            if (analysis.MarkupAt(offset).Kind != DocumentKind.Script)
            {
                Cursor cursor = Locate(analysis, offset);
                if (cursor.InAttributeValue && ComponentPropValues(analysis, cursor) is List<CompletionItem> propValues)
                    return propValues;
                if (cursor.InAttributeValue && LiveAttributeValues(analysis, cursor) is List<CompletionItem> liveValues)
                    return liveValues;
                if (cursor.InStartTag && !cursor.InElementName && !cursor.InAttributeValue)
                    return PageAttributePosition(analysis.Source.Text, offset)
                        ? AttributeCompletions(analysis, cursor.TagElement, offset) : new List<CompletionItem>();
            }
            if (trigger == " " && declarationOptions == null && ExpectedSlotAt(analysis, offset) is not { IsField: true })
                return new List<CompletionItem>();
            List<CompletionItem> items = declarationOptions ?? GetCompletionsAt(analysis, offset);
            bool typedField = trigger is "[" or "," && ExpectedSlotAt(analysis, offset) is { IsField: true };
            if (trigger == "[" && offset > 0 && analysis.Source.Text[offset - 1] == '['
                && declarationOptions == null && !typedField && !items.Any(i => i.Kind == CompletionKind.Value))
            {
                return new List<CompletionItem>();
            }
            if (declarationOptions == null && !typedField && (trigger == "(" || trigger == ",") && !items.Any(i => i.Kind is CompletionKind.Parameter or CompletionKind.Value or CompletionKind.Attribute
                || (i.Kind == CompletionKind.Keyword && i.Detail == "flag")))
            {
                return new List<CompletionItem>();
            }
            if (trigger == "," && offset > 0 && analysis.Source.Text[offset - 1] == ',')
            {
                // Straight after the comma: the pick goes after a space, as the formatter writes it (f(1, size: ...)).
                return items.Select(i => new CompletionItem
                {
                    Label = i.Label, Kind = i.Kind, Detail = i.Detail, Documentation = i.Documentation, IsSnippet = i.IsSnippet,
                    InsertText = " " + (i.InsertText ?? i.Label),
                    ReplaceSpan = i.ReplaceSpan, SortText = i.SortText, FilterText = i.FilterText ?? i.Label, Deprecated = i.Deprecated,
                    SuggestAgain = i.SuggestAgain, AdditionalEdits = i.AdditionalEdits,
                }).ToList();
            }
            return items;
        }

        private List<CompletionItem> GetCompletionsAt(DocumentAnalysis analysis, int offset)
        {
            Cursor cursor = Locate(analysis, offset);
            string text = analysis.Source.Text;
            if (FenceTagCompletions(analysis, offset) is List<CompletionItem> fence)
            {
                return fence;
            }
            if (DocCommentCompletions(analysis, offset) is List<CompletionItem> doc)
            {
                return doc;
            }
            if (InComment(text, offset))
            {
                return new List<CompletionItem>();
            }
            if (DateFormatCompletions(analysis, offset) is List<CompletionItem> dateFormat)
            {
                return dateFormat;
            }
            if (cursor.InCode && cursor.Region != null
                && RouteNameCompletions(analysis, cursor.Region, cursor.LocalIndex) is List<CompletionItem> routeNames)
            {
                return routeNames;
            }
            if (cursor.InCode && cursor.Region != null && InQuotedText(cursor.Region, cursor.LocalIndex))
            {
                return ExpectedValueCompletions(analysis, offset) ?? new List<CompletionItem>();
            }
            if (AliasMemberCompletions(analysis, offset) is List<CompletionItem> members)
            {
                return members;
            }
            if (SigilCompletions(analysis, offset) is List<CompletionItem> sigil)
            {
                return sigil;
            }
            if (ExpectedValueCompletions(analysis, offset) is List<CompletionItem> expected)
            {
                return expected;
            }

            int wordStart = WordStart(text, offset);
            int before = wordStart;
            while (before > 0 && text[before - 1] is ' ' or '\t')
            {
                before--;
            }
            if (before < wordStart && before >= 2 && text.AsSpan(before - 2, 2).SequenceEqual("as")
                && analysis.Regions.Any(r => r.Nodes.SelectMany(n => n.DescendantsAndSelf()).OfType<CastNode>()
                    .Any(c => r.Source.ToSource(c.OperatorSpan.End) == before)))
            {
                return TypeCompletions(analysis, TextSpan.FromBounds(wordStart, offset));
            }

            if (analysis.Kind == DocumentKind.Markdown && analysis.Markup.FrontMatter is FrontMatter fm && offset <= fm.Span.End && offset > 3)
            {
                return FrontMatterCompletions(analysis, offset);
            }

            // Positions the markup and expression parsers don't see: imports, statement starts, KScript.X, and empty expressions.
            if (ContextCompletions(analysis, cursor) is List<CompletionItem> context)
            {
                return context;
            }

            if (cursor.InEndTagName)
            {
                return new List<CompletionItem>
                {
                    new CompletionItem { Label = cursor.TagElement.Name, Kind = CompletionKind.Element, ReplaceSpan = cursor.TagElement.EndNameSpan.Value },
                };
            }

            if (cursor.InElementName)
            {
                return ElementCompletions(analysis, cursor.TagElement.Parent, cursor.TagElement.NameSpan, previous: PreviousSibling(cursor.TagElement),
                    snippets: !cursor.TagElement.Attributes.Any() && !cursor.TagElement.StartTagClosed);
            }

            if (cursor.InAttributeValue)
            {
                List<CompletionItem> values = AttributeValueCompletions(analysis, cursor);
                if (values != null)
                {
                    return values;
                }
            }
            else if (cursor.InStartTag)
            {
                return AttributeCompletions(analysis, cursor.TagElement, offset);
            }

            // In text: a '<' just typed (the parser sees it as text until a name follows), or '</'.
            if (offset > 0 && text[offset - 1] == '<')
            {
                MarkupElement parent = analysis.MarkupAt(offset).FindElementAt(offset);
                return ElementCompletions(analysis, parent, new TextSpan(offset, 0), previous: PreviousElementBefore(text, parent, offset - 1), snippets: true);
            }
            if (offset > 1 && text[offset - 1] == '/' && text[offset - 2] == '<')
            {
                MarkupElement open = analysis.MarkupAt(offset - 2).FindElementAt(offset - 2);
                return open == null ? new List<CompletionItem>() : new List<CompletionItem>
                {
                    new CompletionItem { Label = open.Name, Kind = CompletionKind.Element, InsertText = open.Name + ">", ReplaceSpan = new TextSpan(offset, 0) },
                };
            }

            if (cursor.Region != null)
            {
                return ExpressionCompletions(analysis, cursor.Region, cursor.LocalIndex, cursor.InCode);
            }

            // Plain script text: bare $ and @ references still complete.
            if (wordStart > 0 && (text[wordStart - 1] == '$' || text[wordStart - 1] == '@') && analysis.Kind == DocumentKind.Script)
            {
                return text[wordStart - 1] == '$'
                    ? VariableCompletions(analysis, offset, TextSpan.FromBounds(wordStart, offset), withSigil: false)
                    : CommandCompletions(analysis, TextSpan.FromBounds(wordStart, offset), withSigil: false);
            }
            return new List<CompletionItem>();
        }

        private static MarkupElement PreviousSibling(MarkupElement e)
        {
            IList<MarkupNode> siblings = e.Parent?.Children;
            if (siblings == null)
            {
                return null;
            }
            int index = siblings.IndexOf(e);
            for (int i = index - 1; i >= 0; i--)
            {
                if (siblings[i] is MarkupElement el)
                {
                    return el;
                }
                if (siblings[i] is MarkupText t && !t.IsWhitespace)
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>The element just before an offset among the parent's children, if only whitespace and comments lie between.</summary>
        private static MarkupElement PreviousElementBefore(string text, MarkupElement parent, int offset)
        {
            MarkupElement previous = parent?.Children.OfType<MarkupElement>().LastOrDefault(el => el.Span.End <= offset);
            if (previous == null)
            {
                return null;
            }
            string gap = System.Text.RegularExpressions.Regex.Replace(text.Substring(previous.Span.End, offset - previous.Span.End), "<!--.*?-->", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
            return string.IsNullOrWhiteSpace(gap) ? previous : null;
        }

        private static int WordStart(string text, int offset)
        {
            int i = offset;
            while (i > 0 && Lexer.IsIdentChar(text[i - 1]))
            {
                i--;
            }
            return i;
        }

        #region Markup

        private List<CompletionItem> ElementCompletions(DocumentAnalysis analysis, MarkupElement parent, TextSpan replace, MarkupElement previous, bool snippets, string openBracket = "")
        {
            List<CompletionItem> items = new List<CompletionItem>();
            ElementContext context = analysis.Kind == DocumentKind.Script && analysis.MarkupAt(replace.Start) == analysis.Markup
                ? ElementContext.Script : ElementContext.Page;

            foreach (ElementInfo e in Catalog.Elements)
            {
                // Only what this file can use: built-ins and the packages it imports.
                if (!e.Context.HasFlag(context) || e.Planned || (context == ElementContext.Page && e.HtmlWins) || (context == ElementContext.Script && NeedsImport(analysis, e.Package)))
                {
                    continue;
                }

                // Only offer elements that would be valid here, mirroring the analyzer's placement checks.
                if (!FitsHere(e, parent, previous))
                {
                    continue;
                }
                items.Add(new CompletionItem
                {
                    Label = e.Name,
                    Kind = CompletionKind.Element,
                    Detail = e.Deprecated != null ? "KScript element (deprecated)" : "KScript element",
                    Documentation = ElementMarkdown(e, analysis.Markup.Syntax == SourceSyntax.Code),
                    Deprecated = e.Deprecated != null,
                    InsertText = snippets ? openBracket + ElementSnippet(e) : null,
                    IsSnippet = snippets,
                    ReplaceSpan = replace,
                    FilterText = openBracket.Length > 0 ? e.Name : null,
                    SortText = (e.Deprecated != null ? "9" : "0") + e.Name,
                });
            }

            if (context == ElementContext.Page && analysis.Path != null)
            {
                string dir = ComponentFiles.Folder(analysis.Path, Files);
                if (dir != null)
                {
                    foreach (string file in ComponentFiles.List(dir, Files))
                    {
                        string name = ComponentScript.Tag(file);
                        List<string> props = ComponentProps(file);
                        int n = 1;
                        var declared = TemplateContracts.Read(Files, file).ToList();
                        IEnumerable<string> snippetProps = declared.Count == 0 ? props : declared.Where(p => p.Required).Select(p => p.Name);
                        string propSnippet = string.Concat(snippetProps.Select(p => $" {p}=\"${n++}\""));
                        items.Add(new CompletionItem
                        {
                            Label = name,
                            Kind = CompletionKind.Element,
                            Detail = "Component (" + Path.GetFileName(file) + ")",
                            Documentation = props.Count > 0 ? "Props: " + string.Join(", ", props) : null,
                            InsertText = snippets ? (propSnippet.Length > 0 ? name + propSnippet + " />" : name + " $1/>") : null,
                            IsSnippet = snippets,
                            ReplaceSpan = replace,
                            SortText = "0" + name,
                        });
                    }
                }
            }
            return items;
        }

        private bool FitsHere(ElementInfo e, MarkupElement parent, MarkupElement previous)
        {
            if (parent?.Name == "match" && e.MustBeInside?.Contains("match") != true)
            {
                return false;
            }
            if (e.MustBeInside != null && e.MustBeInside.Count > 0)
            {
                bool inside = (e.Name != "catch" && e.MustBeInside.Contains("fn")) || e.InsideAnyAncestor
                    ? parent != null && (e.MustBeInside.Contains(ElementInfo.BareName(parent.Name)) || parent.Ancestors().Any(a => e.MustBeInside.Contains(ElementInfo.BareName(a.Name))))
                    : parent != null && e.MustBeInside.Contains(parent.Name);
                if (!inside && !(e.Name == "return" && parent != null && parent.Ancestors().Prepend(parent).Any(a => a.Name == "catch" && a.Parent?.Name is "type" or "model")))
                {
                    return false;
                }
            }
            if (e.MustFollow != null && e.MustFollow.Count > 0 && (previous == null || !e.MustFollow.Contains(previous.Name)))
            {
                return false;
            }
            if (e.Name == "break" || e.Name == "continue")
            {
                for (MarkupElement a = parent; a != null && a.Name != "fn"; a = a.Parent)
                {
                    if (Catalog.GetElement(a.Name)?.IsLoop == true)
                    {
                        return true;
                    }
                }
                return false;
            }
            return true;
        }

        /// <summary>Just after the opening fence of a ``` block in a .kscript file: the languages it can name. Null elsewhere.</summary>
        private static List<CompletionItem> FenceTagCompletions(DocumentAnalysis analysis, int offset)
        {
            if (analysis.Markup.Syntax != SourceSyntax.Code)
            {
                return null;
            }
            string text = analysis.Source.Text;
            int start = offset;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '-' or '+' or '#' or '.'))
            {
                start--;
            }
            if (start < 3 || string.CompareOrdinal(text, start - 3, "```", 0, 3) != 0)
            {
                return null;
            }
            TextSpan replace = TextSpan.FromBounds(start, offset);
            return StringBlock.Languages.Select((language, i) => new CompletionItem
            {
                Label = language,
                Kind = CompletionKind.Keyword,
                Detail = language == "text" ? "plain text" : "language",
                Documentation = language == "text" ? "Plain text, with no highlighting." : $"Highlights the block as {language}. It changes nothing at run time.",
                ReplaceSpan = replace,
                SortText = i.ToString("D2"),
            }).ToList();
        }

        private static bool InComment(string text, int offset)
        {
            int open = text.LastIndexOf("<!--", Math.Max(0, offset - 1), StringComparison.Ordinal);
            if (open < 0 || offset < open + 4)
            {
                return false;
            }
            int close = text.IndexOf("-->", open + 4, StringComparison.Ordinal);
            return close < 0 || close >= offset;
        }

        /// <summary>The component file for a tag such as &lt;person-card&gt;, found in the nearest _components folder.</summary>
        internal string ComponentFile(string documentPath, string tag)
        {
            if (string.IsNullOrEmpty(documentPath) || string.IsNullOrEmpty(tag) || !global::KScript.Pages.PageElementNames.CanBeComponent(tag))
            {
                return null;
            }
            string dir = ComponentFiles.Folder(documentPath, Files);
            if (dir == null)
            {
                return null;
            }
            return TemplateContracts.ComponentFile(Files, dir, tag);
        }

        /// <summary>A component's props, in the order the component first uses them. $slot and the like are left out.</summary>
        internal List<string> ComponentProps(string componentFile)
        {
            string text = Files.ReadText(componentFile);
            if (text == null)
            {
                return new List<string>();
            }
            var declared = TemplateContracts.Read(Files, componentFile).Select(p => p.Name);
            return ScriptFiles.IsScript(componentFile) ? declared.ToList()
                : declared.Concat(Analyze(text, componentFile).Inputs.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static string ElementSnippet(ElementInfo e)
        {
            int n = 1;
            string attrs = string.Concat(e.Attributes.Where(a => a.Required || (e.Name == "let" && a.Name == "value") || (e.Name == "set" && a.Name == "value"))
                .Select(a => $" {a.Name}=\"${n++}\""));
            string shorthand = ShorthandCondition.AttributeFor(e.Name);
            if (shorthand != null && attrs == $" {shorthand}=\"$1\"")
            {
                // <if $1>: the condition goes straight in the start tag.
                attrs = " $1";
            }
            switch (e.Content)
            {
                case ContentKind.Empty:
                    return $"{e.Name}{attrs} />";
                case ContentKind.Expression when attrs.Contains("value"):
                    return $"{e.Name}{attrs} />";
                case ContentKind.Statements:
                case ContentKind.Markup:
                    return $"{e.Name}{attrs}>\n\t$0\n</{e.Name}>";
                default:
                    return $"{e.Name}{attrs}>$0</{e.Name}>";
            }
        }

        private List<CompletionItem> AttributeCompletions(DocumentAnalysis analysis, MarkupElement e, int offset)
        {
            ElementInfo info = Catalog.GetElement(e.Name);
            List<CompletionItem> items = new List<CompletionItem>();

            int start = offset;
            string text = analysis.Source.Text;
            while (start > 0 && (Lexer.IsIdentChar(text[start - 1]) || text[start - 1] == '-'))
            {
                start--;
            }

            if (analysis.MarkupAt(offset).Kind != DocumentKind.Script)
            {
                bool wrapper = e.Name is "layout" or "include";
                string file = wrapper ? LayoutFile(e.GetAttributeValue("path")) : ComponentFile(analysis.Path, e.Name);
                if (start > 0 && text[start - 1] == '@') start--;
                AddLiveAttributeCompletions(analysis, e, file != null && !wrapper, start, offset, items);
                if (start < offset && text[start] == '@') return items;
                if (file != null)
                {
                    var declared = TemplateContracts.Read(Files, file).DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    IEnumerable<string> inferredProps = wrapper ? Array.Empty<string>() : ComponentProps(file);
                    foreach (string inferred in inferredProps)
                        if (!declared.Any(p => p.Name.Equals(inferred, StringComparison.OrdinalIgnoreCase))) declared.Add((inferred, false));

                    foreach ((string name, bool required) in declared)
                    {
                        if (e.Attributes.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && a.NameSpan.Start != start)) continue;
                        items.Add(new CompletionItem
                        {
                            Label = name, Kind = CompletionKind.Attribute,
                            Detail = required ? "Required template prop" : "Template prop",
                            Documentation = $"Available in {Path.GetFileName(file)} as ${KScript.Pages.TemplateContract.VariableName(name)} or $.{KScript.Pages.TemplateContract.VariableName(name)}.",
                            InsertText = name + "=\"$1\"", IsSnippet = true,
                            ReplaceSpan = TextSpan.FromBounds(start, offset), SortText = (required ? "0" : "1") + name,
                        });
                    }

                    if (!wrapper && !ScriptFiles.IsScript(file) && !e.Attributes.Any(a => global::KScript.Pages.TemplateContract.IsLoadCallback(a.Name) && a.NameSpan.Start != start))
                        items.Add(new CompletionItem
                        {
                            Label = "onLoad", Kind = CompletionKind.Attribute, Detail = "Component lifecycle callback",
                            Documentation = "Runs on the server with the result of the component's load function.",
                            InsertText = "onLoad={fn(data) => $1}", IsSnippet = true,
                            ReplaceSpan = TextSpan.FromBounds(start, offset), SortText = "1onLoad",
                        });
                }
            }
            if (info == null || analysis.MarkupAt(offset).Kind != DocumentKind.Script &&
                (!info.Context.HasFlag(ElementContext.Page) || info.HtmlWins)) return items;

            string LayoutFile(string relative)
            {
                if (analysis.Path == null || string.IsNullOrWhiteSpace(relative) || relative.IndexOfAny(new[] { '{', '\0' }) >= 0) return null;
                string full = Path.GetFullPath(Path.Combine(TemplateContracts.SiteRoot(Files, Path.GetDirectoryName(Path.GetFullPath(analysis.Path))), relative.Trim().TrimStart('/', '\\')));
                return Files.FileExists(full) ? full : null;
            }

            bool code = analysis.MarkupAt(offset).Syntax == SourceSyntax.Code;
            foreach (AttributeInfo a in code ? CodeStatementHelp.Options(info) : info.Attributes)
            {
                if (e.Attributes.Any(x => x.Name == a.Name && !(x.NameSpan.Start == start)))
                {
                    continue;
                }
                bool quoted = a.Kind is AttributeKind.Template or AttributeKind.Path or AttributeKind.Text;
                items.Add(new CompletionItem
                {
                    Label = a.Name,
                    Kind = CompletionKind.Attribute,
                    Detail = (a.Required ? "required " : string.Empty) + KindLabel(a.Kind) + (a.Deprecated != null ? " (deprecated)" : string.Empty),
                    Documentation = DeprecationLine(a.Deprecated, a.Name) + a.Description + OptionsLine(a),
                    Deprecated = a.Deprecated != null,
                    InsertText = !code ? a.Name + "=\"$1\"" : quoted ? a.Name + ": \"$1\"" : a.Name + ": $1",
                    IsSnippet = true,
                    ReplaceSpan = TextSpan.FromBounds(start, offset),
                    SortText = (a.Deprecated != null ? "9" : a.Required ? "0" : "1") + a.Name,
                });
            }
            return items;
        }

        private List<CompletionItem> AttributeValueCompletions(DocumentAnalysis analysis, Cursor cursor)
        {
            ElementInfo info = Catalog.GetElement(cursor.TagElement.Name);
            AttributeInfo a = info?.GetAttribute(cursor.Attribute.Name);
            DecodedText value = cursor.Attribute.Value;
            int index = value.FromSource(cursor.Offset);
            string before = index >= 0 ? value.Value.Substring(0, index) : string.Empty;
            int wordStart = WordStart(before, before.Length);
            TextSpan replace = TextSpan.FromBounds(value.ToSource(wordStart), cursor.Offset);

            if (a == null)
            {
                return cursor.Region != null ? null : new List<CompletionItem>();
            }

            switch (a.Kind)
            {
                case AttributeKind.Enum:
                    return a.Options.Select(o => new CompletionItem { Label = o, Kind = CompletionKind.Value, ReplaceSpan = TextSpan.FromBounds(value.SourceSpan.Start, value.SourceSpan.End) }).ToList();

                case AttributeKind.TypeName:
                case AttributeKind.TypeNames:
                    return a.Options.Concat(TypeNames(analysis)).Distinct().Select(o => new CompletionItem { Label = o, Kind = CompletionKind.Value, ReplaceSpan = TextSpan.FromBounds(value.SourceSpan.Start, value.SourceSpan.End) }).ToList();

                case AttributeKind.ErrorType:
                    var errors = ErrorTypeCompletions(analysis, cursor.Offset, replace);
                    if (analysis.Markup.Syntax == SourceSyntax.Code && cursor.TagElement.Name == "throw")
                        errors.Add(ConditionalThrowCompletion(replace, cursor.TagElement.Parent, includeThrow: false));
                    return errors;

                case AttributeKind.Bool:
                    // A boolean is true or false (KS6011); the flag on its own is offered with the argument names.
                    return new[] { "true", "false" }.Select(o => new CompletionItem { Label = o, Kind = CompletionKind.Value, ReplaceSpan = TextSpan.FromBounds(value.SourceSpan.Start, value.SourceSpan.End) }).ToList();

                case AttributeKind.FunctionReference:
                case AttributeKind.FunctionReferences:
                {
                    // Functions, functions of imported scripts (people.show), and built-in middleware (KScript.Http).
                    List<CompletionItem> items = FunctionSymbolCompletions(analysis, replace, a.FunctionSignature);
                    foreach (Symbol import in analysis.Symbols.Where(s => s.Kind == SymbolKind.Import))
                    {
                        foreach (FieldInfo field in import.Shape.Fields.Values.Where(f => f.Shape.Type == KType.Fn))
                        {
                            if (a.FunctionSignature is Signature contract)
                            {
                                if (import.Members.TryGetValue(field.Name, out Symbol function) && function.Kind == SymbolKind.Function
                                    && !function.PositionalSignature.AcceptsPositionalCall(contract)) continue;
                                if (import.Package is string package && Catalog.GetCommand(field.Name) is CallableInfo command && command.Package == package
                                    && !command.Signatures.Any(s => s.AcceptsPositionalCall(contract))) continue;
                            }
                            items.Add(new CompletionItem { Label = import.Name + "." + field.Name, Kind = CompletionKind.Function, Detail = field.Description, ReplaceSpan = replace });
                        }
                    }
                    if (analysis.Markup.Syntax == SourceSyntax.Code && CodeArguments.TakesFunctions(ElementInfo.BareName(cursor.TagElement.Name), a)
                        && !(wordStart > 0 && before[wordStart - 1] == '@'))
                    {
                        // In .kscript a function is named as @name: fn: @send, to: @people.show.
                        items = items.Select(i => new CompletionItem { Label = i.Label, Kind = i.Kind, Detail = i.Detail, InsertText = "@" + i.Label, ReplaceSpan = i.ReplaceSpan }).ToList();
                    }
                    items.AddRange(a.Options.Select(o => new CompletionItem { Label = o, Kind = CompletionKind.Value, Detail = "built-in middleware", ReplaceSpan = replace }));
                    return items;
                }

                case AttributeKind.VariableReference:
                    return VariableCompletions(analysis, cursor.Offset, replace, withSigil: false);

                case AttributeKind.Parameters:
                    return ParameterValueCompletions(analysis, before, cursor.Offset, replace);

                case AttributeKind.Path:
                    return PathCompletions(analysis, value, cursor.Offset, scriptsOnly: cursor.TagElement.Name == "import",
                        relativeToSite: analysis.Kind != DocumentKind.Script && cursor.TagElement.Name is "layout" or "include");

                case AttributeKind.Expression:
                case AttributeKind.ExpressionList:
                case AttributeKind.Statements:
                case AttributeKind.Template:
                    if (cursor.Region == null && a.Name == "value" && string.IsNullOrWhiteSpace(before)
                        && DeclaredTypeValueCompletions(cursor.TagElement, replace) is { Count: > 0 } typed)
                    {
                        // <let type="duration" value="‸">: the values a native type offers.
                        return typed.Concat(ExpressionStartCompletions(analysis, cursor.Offset, replace, template: false)).ToList();
                    }
                    return cursor.Region != null ? null : ExpressionStartCompletions(analysis, cursor.Offset, replace, a.Kind == AttributeKind.Template);

                default:
                    return new List<CompletionItem>();
            }
        }

        /// <summary>The error types for a catch, throw, extends or expect_error: declared ones first, then the built-ins.</summary>
        private static List<CompletionItem> ErrorTypeCompletions(DocumentAnalysis analysis, int offset, TextSpan replace)
        {
            List<CompletionItem> items = analysis.Symbols.Where(s => s.Shape.Type == KType.ErrorType).GroupBy(s => s.Name).Select(g => new CompletionItem
            {
                Label = g.Key,
                Kind = CompletionKind.Value,
                Detail = "error type",
                Documentation = g.First().Description,
                ReplaceSpan = replace,
                SortText = "0" + g.Key,
            }).ToList();
            foreach (KScript.Runtime.ErrorType t in KScript.Runtime.ErrorTypes.BuiltIn.Values.Where(t => items.All(i => i.Label != t.Name)))
            {
                items.Add(new CompletionItem
                {
                    Label = t.Name,
                    Kind = CompletionKind.Value,
                    Detail = "built-in error" + (t.Parent != null && t.Parent.Name != KScript.Runtime.ErrorTypes.Root ? ", extends " + t.Parent.Name : string.Empty) + (t.Status is int s ? $", status {s}" : string.Empty),
                    Documentation = t.Description,
                    ReplaceSpan = replace,
                    SortText = "1" + t.Name,
                });
            }
            return items;
        }

        /// <summary>Built-in type names, then the document's own types and enums.</summary>
        private List<CompletionItem> TypeCompletions(DocumentAnalysis analysis, TextSpan replace) =>
            (Catalog.GetElement("field")?.GetAttribute("type")?.Options ?? Array.Empty<string>()).Select(t => (t, "0"))
                .Concat(TypeNames(analysis).Select(t => (t, "1")))
                .Select(t => new CompletionItem { Label = t.Item1, Kind = CompletionKind.Value, Detail = "type", ReplaceSpan = replace, SortText = t.Item2 + t.Item1 })
                .ToList();

        /// <summary>The &lt;type&gt;s and &lt;enum&gt;s a document declares or imports.</summary>
        internal static IReadOnlyList<string> TypeNames(DocumentAnalysis analysis) =>
            analysis.Symbols.Where(s => s.EnumValues != null || s.Shape.Type == KType.Type).Select(s => s.Name).Distinct().ToList();

        private static readonly string[] ScriptExtensions = ScriptFiles.Extensions;
        private static readonly string[] PageExtensions = { ".html", ".md" };
        private static readonly string[] SkippedFolders = { "node_modules", "bin", "obj", "_templates" };
        private const int MaxProjectFiles = 300;

        /// <summary>
        /// Paths relative to the current file: the files and folders in the folder typed so far, and, before any
        /// <c>/</c> is typed, every script in the project (the folder of the nearest kscript.json, or this file's
        /// folder). An import only offers scripts; other path attributes also offer .html and .md pages.
        /// </summary>
        private List<CompletionItem> PathCompletions(DocumentAnalysis analysis, DecodedText value, int offset, bool scriptsOnly, bool relativeToSite = false)
        {
            List<CompletionItem> items = new List<CompletionItem>();
            if (analysis.Path == null)
            {
                return items;
            }

            string self = Path.GetFullPath(analysis.Path);
            string dir = Path.GetDirectoryName(self);
            if (relativeToSite) dir = TemplateContracts.SiteRoot(Files, dir);
            int index = value.FromSource(offset);
            string typed = index >= 0 ? value.Value.Substring(0, index) : string.Empty;
            int slash = typed.LastIndexOf('/');
            string sub = slash >= 0 ? typed.Substring(0, slash) : string.Empty;
            string folder = Path.GetFullPath(Path.Combine(dir, relativeToSite ? sub.TrimStart('/', '\\') : sub));
            TextSpan replace = TextSpan.FromBounds(value.ToSource(slash + 1), offset);
            string[] extensions = scriptsOnly ? ScriptExtensions : ScriptExtensions.Concat(PageExtensions).ToArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string d in Files.ListDirectories(folder).OrderBy(d => d, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(d);
                if (name.StartsWith('.') || SkippedFolders.Contains(name))
                {
                    continue;
                }
                items.Add(new CompletionItem { Label = name + "/", Kind = CompletionKind.Folder, ReplaceSpan = replace, SortText = "1" + name });
            }
            foreach (string file in FilesWith(folder, extensions))
            {
                if (PathEquals(file, self))
                {
                    continue;
                }
                string name = Path.GetFileName(file);
                seen.Add(Path.GetRelativePath(dir, file).Replace('\\', '/'));
                items.Add(new CompletionItem { Label = name, Kind = CompletionKind.File, ReplaceSpan = replace, SortText = "0" + name });
            }

            if (slash < 0)
            {
                TextSpan whole = TextSpan.FromBounds(value.ToSource(0), offset);
                foreach (string file in ProjectScripts(ProjectRoot(dir), extensions))
                {
                    string relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
                    if (PathEquals(file, self) || !seen.Add(relative))
                    {
                        continue;
                    }
                    items.Add(new CompletionItem
                    {
                        Label = relative,
                        Kind = CompletionKind.File,
                        ReplaceSpan = whole,
                        SortText = (relative.StartsWith("../", StringComparison.Ordinal) ? "3" : "2") + relative,
                    });
                }
            }
            return items;
        }

        private IEnumerable<string> FilesWith(string folder, string[] extensions) =>
            Files.ListFiles(folder, "*")
                .Where(f => extensions.Any(x => f.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => f, StringComparer.Ordinal);

        /// <summary>The folder of the nearest kscript.json above <paramref name="dir"/>, or the folder itself.</summary>
        private string ProjectRoot(string dir)
        {
            for (string d = dir; d != null; d = Path.GetDirectoryName(d))
            {
                if (Files.FileExists(Path.Combine(d, KScript.Projects.ProjectConfig.FileName)))
                {
                    return d;
                }
            }
            return dir;
        }

        /// <summary>Script files under a folder, breadth first, skipping hidden and build folders.</summary>
        private IEnumerable<string> ProjectScripts(string root, string[] extensions)
        {
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((root, 0));
            int count = 0;
            while (queue.Count > 0 && count < MaxProjectFiles)
            {
                (string d, int depth) = queue.Dequeue();
                foreach (string file in FilesWith(d, extensions))
                {
                    if (++count > MaxProjectFiles)
                    {
                        yield break;
                    }
                    yield return file;
                }
                if (depth >= 8)
                {
                    continue;
                }
                foreach (string child in Files.ListDirectories(d).OrderBy(c => c, StringComparer.Ordinal))
                {
                    string name = Path.GetFileName(child);
                    if (!name.StartsWith('.') && !SkippedFolders.Contains(name))
                    {
                        queue.Enqueue((child, depth + 1));
                    }
                }
            }
        }

        private static bool PathEquals(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        private List<CompletionItem> FrontMatterCompletions(DocumentAnalysis analysis, int offset)
        {
            int lineStart = analysis.Source.GetOffset(analysis.Source.GetPosition(offset).Line, 0);
            string line = analysis.Source.Text.Substring(lineStart, offset - lineStart);
            if (line.Contains(':'))
            {
                return new List<CompletionItem>();
            }
            return new[]
            {
                ("title", "The page title, available as $title."),
                ("layout", "The layout file that wraps the page, such as _layout.html."),
            }.Select(k => new CompletionItem
            {
                Label = k.Item1,
                Kind = CompletionKind.Field,
                Documentation = k.Item2,
                InsertText = k.Item1 + ": ",
                ReplaceSpan = TextSpan.FromBounds(lineStart, offset),
            }).ToList();
        }

        private static string KindLabel(AttributeKind kind) => kind switch
        {
            AttributeKind.Expression => "expression",
            AttributeKind.ExpressionList => "expressions",
            AttributeKind.Template => "text with { }",
            AttributeKind.DeclareVariable => "name (declares)",
            AttributeKind.DeclareVariables => "names (declares)",
            AttributeKind.VariableReference => "variable name",
            AttributeKind.Parameters => "parameters",
            AttributeKind.DeclareFunction => "function name (declares)",
            AttributeKind.FunctionReference => "function name",
            AttributeKind.FunctionReferences => "function names",
            AttributeKind.Path => "file path",
            AttributeKind.Selector => "CSS selector",
            AttributeKind.Enum => "one of a set",
            AttributeKind.Bool => "true/false",
            _ => "text",
        };

        private static string OptionsLine(AttributeInfo a)
        {
            string contract = a.FunctionSignature is Signature signature ? $"\n\nCustom function: `{signature.FunctionLabel}`." : string.Empty;
            return a.Options.Count > 0
                ? contract + (a.FunctionSignature != null ? "\n\nBuilt-ins: " : "\n\nOne of: ") + string.Join(", ", a.Options.Select(o => "`" + o + "`"))
                : contract;
        }

        #endregion

        #region Expressions

        private List<CompletionItem> ExpressionCompletions(DocumentAnalysis analysis, ExpressionRegion region, int index, bool inCode)
        {
            string text = region.Source.Value;
            index = Math.Clamp(index, 0, text.Length);
            int wordStart = WordStart(text, index);
            TextSpan replace = region.Source.ToSource(TextSpan.FromBounds(wordStart, index));
            int offset = region.Source.ToSource(index);
            char before = wordStart > 0 ? text[wordStart - 1] : '\0';

            if (!inCode)
            {
                // Literal text of a template: only bare references (in scripts) and the start of { }.
                bool allowBare = region.Kind == RegionKind.Template && analysis.Kind == DocumentKind.Script && region.Attribute == null;
                if (allowBare && before == '$')
                {
                    return VariableCompletions(analysis, offset, replace, withSigil: false);
                }
                if (allowBare && before == '@')
                {
                    return CommandCompletions(analysis, replace, withSigil: false);
                }
                if (allowBare && before == '>' && wordStart >= 2 && text[wordStart - 2] == '-')
                {
                    return BareMemberCompletions(analysis, region, wordStart - 2, replace, method: true);
                }
                if (allowBare && before == '.')
                {
                    return BareMemberCompletions(analysis, region, wordStart - 1, replace, method: false);
                }
                if (allowBare && wordStart == index)
                {
                    // Asked for in the text itself (Ctrl+Space): the $variables and @commands it can show.
                    return ExpressionStartCompletions(analysis, offset, replace, template: false).Where(i => i.Kind != CompletionKind.Keyword).ToList();
                }
                return new List<CompletionItem>();
            }

            if (before == '$')
            {
                return VariableCompletions(analysis, offset, replace, withSigil: false);
            }
            if (before == '@' && wordStart >= 2 && Lexer.IsIdentChar(text[wordStart - 2]))
            {
                // joan@exa‸: an email address being written, not a command.
                return new List<CompletionItem>();
            }
            if (before == '@')
            {
                return CommandCompletions(analysis, replace, withSigil: false, suggestRouteImport: true);
            }
            if (before == '>' && wordStart >= 2 && text[wordStart - 2] == '-')
            {
                return MethodCompletions(analysis, region, wordStart - 2, replace, pipe: false);
            }
            if (before == '!' && wordStart >= 3 && text[wordStart - 2] == '>' && text[wordStart - 3] == '-')
            {
                return MethodCompletions(analysis, region, wordStart - 3, replace, pipe: false);
            }
            if (before == '.' && wordStart >= 2 && text[wordStart - 2] != '.')
            {
                return FieldCompletions(analysis, region, wordStart - 1, replace);
            }

            int p = wordStart;
            while (p > 0 && char.IsWhiteSpace(text[p - 1]))
            {
                p--;
            }
            if (p >= 2 && text[p - 1] == '>' && text[p - 2] == '|')
            {
                return MethodCompletions(analysis, region, p - 2, replace, pipe: true);
            }

            List<CompletionItem> parameterNames = ParameterNameCompletions(analysis, region, wordStart, replace, out bool positionalModel);
            List<CompletionItem> namedValues = NamedValueCompletions(analysis, region, wordStart, replace, out bool namedModel);
            bool preferModels = positionalModel || namedModel;
            List<CompletionItem> start = ExpressionStartCompletions(analysis, offset, replace, template: false, preferModels: preferModels);
            start.InsertRange(0, parameterNames);
            start.InsertRange(0, namedValues);
            start.InsertRange(0, DeclaredTypeValueCompletions(region, wordStart, replace));
            start.InsertRange(0, WordArgumentCompletions(analysis, region, wordStart, replace));
            start.InsertRange(0, ListItemCompletions(analysis, region, wordStart, replace));
            return preferModels ? start.OrderBy(i => i.SortText ?? i.Label, StringComparer.Ordinal).ToList() : start;
        }

        private List<CompletionItem> ExpressionStartCompletions(DocumentAnalysis analysis, int offset, TextSpan replace, bool template, bool preferModels = false)
        {
            List<CompletionItem> items = new List<CompletionItem>();
            if (template)
            {
                return items;
            }
            items.AddRange(VariableCompletions(analysis, offset, replace, withSigil: true, preferModels: preferModels));
            items.AddRange(CommandCompletions(analysis, replace, withSigil: true));
            bool codeFile = analysis.Markup.Syntax == KScript.Syntax.Markup.SourceSyntax.Code;
            items.AddRange(ExpressionKeywords.Where(k => !(codeFile && ComparisonWords.Contains(k.Word))).Select(k => new CompletionItem
            {
                Label = k.Word,
                Kind = CompletionKind.Keyword,
                Documentation = k.Doc,
                InsertText = k.Word == "fn" ? "fn(${1:x}) => $0" : null,
                IsSnippet = k.Word == "fn",
                ReplaceSpan = replace,
                SortText = "3" + k.Word,
            }));
            return items;
        }

        private List<CompletionItem> VariableCompletions(DocumentAnalysis analysis, int offset, TextSpan replace, bool withSigil, bool preferModels = false, bool textOnly = false)
        {
            Scope scope = analysis.ScopeAt(offset);
            // The script's own variables come first, the one declared nearest above the cursor at the top; the
            // variables KScript provides ($args, $env...) follow by name.
            List<Symbol> visible = scope.VisibleAt(offset)
                .Where(s => s.Kind != SymbolKind.Function && s.Kind != SymbolKind.Import
                    && (!s.IsTypeDeclaration || (withSigil && scope.Lookup(s.Name, offset) == s))
                    && (!textOnly || (!s.IsTypeDeclaration && s.Shape.Type == KType.String))).ToList();
            if (preferModels)
            {
                visible.AddRange(scope.VisibleAt(offset)
                    .Where(s => s.Kind == SymbolKind.Import && scope.Lookup(s.Name, offset) == s)
                    .SelectMany(s => s.Members.Values).Where(m => m.IsTypeDeclaration && m.Shape.Kind == KType.Model));
            }
            Dictionary<Symbol, int> nearest = visible.Where(s => s.Kind != SymbolKind.BuiltIn && s.Kind != SymbolKind.FrontMatter)
                .OrderByDescending(s => s.DeclarationSpan.Start).Select((s, i) => (s, i)).ToDictionary(p => p.s, p => p.i);
            return visible.Select(s =>
            {
                bool modelValue = preferModels && s.IsTypeDeclaration && s.Shape.Kind == KType.Model;
                string name = s.Alias == null ? s.Name : s.Alias.Name + "." + s.Name;
                return new CompletionItem
                {
                    Label = withSigil && !s.IsTypeDeclaration ? "$" + name : name,
                    Kind = modelValue ? CompletionKind.Value : s.Kind == SymbolKind.Parameter ? CompletionKind.Parameter : s.IsConst ? CompletionKind.Constant : CompletionKind.Variable,
                    Detail = s.Shape.Display(),
                    Documentation = SymbolMarkdown(analysis, s),
                    ReplaceSpan = replace,
                    // The span replaced is the bare word (the $ isn't typed yet, or sits before it), so filter on the bare name.
                    FilterText = name,
                    SortText = modelValue ? "!" + name
                        : nearest.TryGetValue(s, out int rank) ? "0" + rank.ToString("D4") + s.Name : "1" + s.Name,
                };
            }).ToList();
        }

        private List<CompletionItem> CommandCompletions(DocumentAnalysis analysis, TextSpan replace, bool withSigil, bool suggestRouteImport = false)
        {
            string sigil = withSigil ? "@" : string.Empty;
            List<CompletionItem> items = analysis.VisibleFunctions(replace.Start)
                .Select(s => new CompletionItem
                {
                    Label = sigil + s.Name,
                    Kind = CompletionKind.Function,
                    Detail = s.TypedSignature,
                    Documentation = SymbolMarkdown(analysis, s),
                    InsertText = sigil + s.Name + (s.Parameters.Count == 0 ? "()" : "($1)"),
                    IsSnippet = true,
                    ReplaceSpan = replace,
                    FilterText = s.Name,
                    SortText = "1" + s.Name,
                }).ToList();

            var packages = new HashSet<string>(analysis.UnqualifiedCommandPackages, StringComparer.OrdinalIgnoreCase);
            if (analysis.Kind == DocumentKind.Script && analysis.MarkupAt(replace.Start).Kind == DocumentKind.Page)
                packages.UnionWith(global::KScript.Pages.PageElementNames.DefaultPackages.SelectMany(Catalog.WithRequired));
            foreach (CallableInfo command in Catalog.Commands.Where(c => !c.Planned))
            {
                TextChange import = null;
                if (command.Package != null && !packages.Contains(command.Package))
                {
                    if (!suggestRouteImport || analysis.Kind != DocumentKind.Script
                        || command is not { Package: "KScript.Http", Name: "route" }
                        || items.Any(i => i.FilterText == command.Name))
                        continue;
                    import = Analyzer.ImportChange(analysis, command.Package);
                    if (import == null || replace.Contains(import.Span.Start)) continue;
                }
                items.Add(new CompletionItem
                {
                    Label = sigil + command.Name,
                    Kind = CompletionKind.Command,
                    Detail = command.Labels.FirstOrDefault() + (import != null ? $" (import {command.Package})" : ""),
                    Documentation = CallableMarkdown(command),
                    Deprecated = command.Deprecated != null,
                    InsertText = sigil + command.Name + (command.MaxArgs == 0 ? "()" : "($1)"),
                    IsSnippet = true,
                    ReplaceSpan = replace,
                    FilterText = command.Name,
                    SortText = (command.Deprecated != null ? "9" : import != null ? "4" : "2") + command.Name,
                    AdditionalEdits = import != null ? new[] { import } : null,
                });
            }
            return items;
        }

        private List<CompletionItem> FunctionSymbolCompletions(DocumentAnalysis analysis, TextSpan replace, Signature contract) =>
            analysis.VisibleFunctions(replace.Start)
                .Where(s => contract == null || s.PositionalSignature.AcceptsPositionalCall(contract))
                .Select(s => new CompletionItem { Label = s.Name, Kind = CompletionKind.Function, Detail = s.TypedSignature, ReplaceSpan = replace })
                .ToList();

        /// <summary>Methods after <c>-&gt;</c> or <c>|&gt;</c>, for the type of the value on the left.</summary>
        private List<CompletionItem> MethodCompletions(DocumentAnalysis analysis, ExpressionRegion region, int operatorIndex, TextSpan replace, bool pipe)
        {
            TypeShape receiver = TypeShape.Unknown;
            ExprNode owner = region.Nodes.SelectMany(n => n.DescendantsAndSelf())
                .FirstOrDefault(n => (n is MethodCallNode m && m.NameSpan.Start >= operatorIndex && m.NameSpan.Start <= operatorIndex + 3 + (replace.Length))
                                     || (n is PipeNode pn && pn.Left.Span.End <= operatorIndex && pn.Right.Span.Start >= operatorIndex));
            if (owner is MethodCallNode mc)
            {
                receiver = InferAt(analysis, region, mc.Target);
            }
            else if (owner is PipeNode pipeNode)
            {
                receiver = InferAt(analysis, region, pipeNode.Left);
            }

            List<CompletionItem> items = MethodItems(analysis, receiver, replace);

            if (pipe)
            {
                items.AddRange(analysis.VisibleFunctions(replace.Start)
                    .Select(s => new CompletionItem
                    {
                        Label = s.Name,
                        Kind = CompletionKind.Function,
                        Detail = s.TypedSignature,
                        InsertText = s.Name + (s.Parameters.Count <= 1 ? "()" : "($1)"),
                        IsSnippet = true,
                        ReplaceSpan = replace,
                        SortText = "0" + s.Name,
                    }));
                items.AddRange(CommandCompletions(analysis, replace, withSigil: true));
            }
            return items;
        }

        /// <summary>
        /// The methods a value of this shape has. The value's own type's methods come first, then the ones every
        /// value has, then the ones it gets by conversion (a string's number methods, for text that holds a number).
        /// </summary>
        private List<CompletionItem> MethodItems(DocumentAnalysis analysis, TypeShape receiver, TextSpan replace)
        {
            // A <type>'s own fn methods and the methods extend blocks add (this file's, imports', the project's);
            // an override replaces the built-in method of the same name.
            var extensions = analysis.ExtensionMethodsFor(receiver);
            List<Symbol> added = (receiver.RefinementBase != null ? receiver.Methods.Values.Concat(extensions) : extensions.Concat(receiver.Methods.Values))
                .GroupBy(m => m.Name, StringComparer.Ordinal).Select(g => g.First()).ToList();
            var names = new HashSet<string>(added.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            List<CompletionItem> items = added.Select(m => new CompletionItem
            {
                Label = m.Name,
                Kind = CompletionKind.Method,
                Detail = m.TypedSignature + (m.SourcePath != null ? "  (" + Path.GetFileName(m.SourcePath) + ")" : string.Empty),
                Documentation = m.Description,
                InsertText = m.Name + (m.Parameters.Count == 0 ? "()" : "($1)"),
                IsSnippet = true,
                ReplaceSpan = replace,
                SortText = "0" + m.Name,
            }).ToList();
            items.AddRange(BuiltInMethodItems(analysis, receiver, replace).Where(i => !names.Contains(i.Label)));
            return items;
        }

        private List<CompletionItem> BuiltInMethodItems(DocumentAnalysis analysis, TypeShape receiver, TextSpan replace) =>
            BuiltInMethods(receiver).Where(m => !NeedsImport(analysis, m.Package))
                .Select(m => new CompletionItem
                {
                    Label = m.Name,
                    Kind = CompletionKind.Method,
                    Detail = m.Labels.First() + ": " + MethodResult(m, receiver).Display()
                             + (m.ReceiverType != KType.Any && m.ReceiverType != receiver.Type ? "  (" + m.ReceiverType + ")" : string.Empty),
                    Documentation = CallableMarkdown(m, receiver.Type != KType.Any ? receiver : null),
                    Deprecated = m.Deprecated != null,
                    InsertText = m.Name + (m.MaxArgs == 0 ? "()" : "($1)"),
                    IsSnippet = true,
                    ReplaceSpan = replace,
                    SortText = m.Deprecated != null ? "9" + m.Name
                        : receiver.Type == KType.Any || m.ReceiverType == receiver.Type || m.ReceiverType == TypeInference.TypedListReceiver(receiver) ? "0" + Ranking.Method(m.ReceiverType, m.Name)
                        : (m.ReceiverType == KType.Any ? "1" : "2") + m.Name,
                }).ToList();

        /// <summary>
        /// The catalog's methods for a value of this shape. A list of one native type (semver[]) has that type's list
        /// methods (->latest()) first, on top of a list's.
        /// </summary>
        private IEnumerable<CallableInfo> BuiltInMethods(TypeShape receiver) =>
            Catalog.MethodsFor(TypeInference.TypedListReceiver(receiver) ?? receiver.Type);

        /// <summary>What a method returns on this receiver: list methods such as filter keep the item type.</summary>
        private TypeShape MethodResult(CallableInfo m, TypeShape receiver) => TypeInference.MethodResult(m, receiver);

        /// <summary>
        /// <c>$name-&gt;</c> or <c>$name.</c> in an element's plain text (bare references, no braces): the parser stops
        /// at the reference, so the receiver is the code that ends just before the operator.
        /// </summary>
        private List<CompletionItem> BareMemberCompletions(DocumentAnalysis analysis, ExpressionRegion region, int operatorIndex, TextSpan replace, bool method)
        {
            ExprNode receiverNode = region.Nodes.SelectMany(n => n.DescendantsAndSelf())
                .Where(n => n.Span.End == operatorIndex && !(n is ArgumentNode))
                .OrderByDescending(n => n.Span.Length)
                .FirstOrDefault();
            if (receiverNode == null)
            {
                return new List<CompletionItem>();
            }
            TypeShape receiver = InferAt(analysis, region, receiverNode);
            if (method)
            {
                return MethodItems(analysis, receiver, replace);
            }
            return FieldItems(receiver, replace);
        }

        /// <summary>The fields of a value: a map's keys or a record's fields, and the ones its type has (<c>$response.status</c>).</summary>
        private List<CompletionItem> FieldItems(TypeShape shape, TextSpan replace)
        {
            List<CompletionItem> items = shape.Fields.Values.Select(f => new CompletionItem
            {
                Label = f.Name,
                Kind = CompletionKind.Field,
                Detail = f.Shape.Display(),
                Documentation = f.Description,
                ReplaceSpan = replace,
            }).ToList();
            foreach (string type in new[] { shape.Kind, shape.Type })
            {
                foreach (ValueFieldInfo f in Catalog.FieldsOf(type).Values.OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    if (shape.Fields.ContainsKey(f.Name) || items.Any(i => i.Label == f.Name)) continue;
                    items.Add(new CompletionItem
                    {
                        Label = f.Name,
                        Kind = CompletionKind.Field,
                        Detail = f.Type ?? KType.Any,
                        Documentation = f.Description,
                        ReplaceSpan = replace,
                    });
                }
            }
            return items;
        }

        /// <summary>
        /// Straight after <c>@</c> only commands and functions, and after <c>$</c> only variables, wherever the parser
        /// is (a value it can't read yet, such as <c>tz: timezone = @</c>, included). When the type the value must have
        /// is known, the commands that give it come first. Null when the word isn't after a sigil.
        /// </summary>
        private List<CompletionItem> SigilCompletions(DocumentAnalysis analysis, int offset)
        {
            string text = analysis.Source.Text;
            int wordStart = WordStart(text, offset);
            if (wordStart == 0 || (wordStart < offset && !Lexer.IsIdentStart(text[wordStart]))) return null;
            bool scoped = wordStart >= 2 && text[wordStart - 2] == '$' && text[wordStart - 1] == '.';
            int sigilOffset = wordStart - (scoped ? 2 : 1);
            if (analysis.MarkupAt(offset).Kind != DocumentKind.Script)
            {
                if (!scoped) return null; // Use page-aware suggestions for the ordinary sigils.
                var region = analysis.RegionAt(sigilOffset);
                int local = region?.Source.FromSource(sigilOffset) ?? -1;
                if (region == null || !region.CodeSpans.Any(span => span.Start <= local && local <= span.End)) return null;
            }
            char sigil = text[sigilOffset];
            // joan@example.com and a$b are text, not a command or a variable.
            if ((sigil != '@' && sigil != '$') || (sigilOffset > 0 && (Lexer.IsIdentChar(text[sigilOffset - 1]) || text[sigilOffset - 1] == '@' || text[sigilOffset - 1] == '$')))
            {
                return null;
            }
            TextSpan replace = TextSpan.FromBounds(wordStart, offset);
            ExpectedValueSlot? slot = ExpectedSlotAt(analysis, sigilOffset);
            if (sigil == '$')
            {
                List<CompletionItem> variables = VariableCompletions(analysis, offset, replace, withSigil: false);
                string expectedType = slot?.Type ?? CallArgumentTypeAt(analysis, sigilOffset);
                return RankVariables(analysis, offset, variables, expectedType, matchingOnly: slot?.IsField == true);
            }
            if (slot is { Type: string expected } && SplitType(expected).Name == "handle")
            {
                return null;
            }
            List<CompletionItem> commands = CommandCompletions(analysis, replace, withSigil: false, suggestRouteImport: true);
            if (slot is { IsField: true } field)
                return MatchingFieldCommands(analysis, offset, field.Type, commands);
            if (slot is { Type: string type })
            {
                string want = SplitType(type).Name;
                HashSet<string> giving = new HashSet<string>(Catalog.Commands.Where(c => c.ReturnType == want).Select(c => c.Name), StringComparer.Ordinal);
                giving.UnionWith(analysis.VisibleFunctions(offset).Where(f => f.ReturnShape.Type == want || f.ReturnShape.Name == want).Select(f => f.Name));
                commands = commands.Select(i => giving.Contains(i.FilterText ?? i.Label) ? WithSort(i, "0" + i.SortText) : i).ToList();
            }
            return commands;
        }

        private static CompletionItem WithSort(CompletionItem i, string sort) => new CompletionItem
        {
            Label = i.Label, Kind = i.Kind, Detail = i.Detail, Documentation = i.Documentation, IsSnippet = i.IsSnippet, InsertText = i.InsertText,
            ReplaceSpan = i.ReplaceSpan, SortText = sort, FilterText = i.FilterText, Deprecated = i.Deprecated, SuggestAgain = i.SuggestAgain,
            AdditionalEdits = i.AdditionalEdits,
        };

        private static readonly System.Text.RegularExpressions.Regex TypedLetBefore = new System.Text.RegularExpressions.Regex(
            @"^[ \t]*(?:const[ \t]+)?\$?([A-Za-z_]\w*)[ \t]*:[ \t]*([^=]+?)[ \t]*=[ \t]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex AssignBefore = new System.Text.RegularExpressions.Regex(
            @"^[ \t]*\$?([A-Za-z_]\w*)[ \t]*=[ \t]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// The type the value starting at <paramref name="valueStart"/> must have, when the line says: <c>tz: timezone = ‸</c>,
        /// or <c>tz = ‸</c> for a variable declared with a type. Null when it isn't known.
        /// </summary>
        private string AssignmentTypeAt(DocumentAnalysis analysis, int valueStart)
        {
            if (analysis.Markup.Syntax != SourceSyntax.Code)
            {
                return null;
            }
            string text = analysis.Source.Text;
            int lineStart = text.LastIndexOf('\n', Math.Max(0, valueStart - 1)) + 1;
            if (valueStart < lineStart)
            {
                return null;
            }
            string before = text.Substring(lineStart, valueStart - lineStart);
            if (TypedLetBefore.Match(before) is { Success: true } typed)
            {
                return typed.Groups[2].Value.Trim();
            }
            if (AssignBefore.Match(before) is { Success: true } assign
                && analysis.ScopeAt(valueStart).Lookup(assign.Groups[1].Value, valueStart) is Symbol { Kind: SymbolKind.Variable or SymbolKind.Parameter } v
                && v.Shape.Type != KType.Any)
            {
                return v.DeclaringElement?.GetAttributeValue("type") ?? v.Shape.Name ?? v.Shape.Type;
            }
            return null;
        }

        /// <summary>
        /// The value of a typed variable (<c>tz: timezone = Eu‸</c>): the values the type accepts first (time zones, an
        /// enum's values, durations), then the variables and commands that could give one. Null when no type is known.
        /// </summary>
        private List<CompletionItem> ExpectedValueCompletions(DocumentAnalysis analysis, int offset)
        {
            string text = analysis.Source.Text;
            int start = RawValueStart(text, offset);
            MarkupAttribute fieldAttribute = XmlFieldValueAt(analysis, offset);
            if (fieldAttribute != null) start = Math.Max(start, fieldAttribute.Value.SourceSpan.Start);
            bool quoted = false;
            string typed = text.Substring(start, offset - start);
            TextSpan replace = TextSpan.FromBounds(start, Math.Min(RawValueEnd(text, offset), fieldAttribute?.Value.SourceSpan.End ?? text.Length));
            ExpectedValueSlot? expected = ExpectedSlotAt(analysis, start);
            if ((expected == null || typed.StartsWith('\'') || typed.StartsWith('"')) && QuotedValueAt(analysis, offset) is Token token)
            {
                start = token.Span.Start;
                expected = ExpectedSlotAt(analysis, start);
                typed = Quotes.Decode(text, start + 1, offset, text[start]);
                replace = TextSpan.FromBounds(start, token.Error == null ? token.Span.End : offset);
                quoted = true;
            }
            if (expected is not ExpectedValueSlot slot || (!quoted && typed.Length > 0 && typed[0] is '$' or '@')) return null;
            List<CompletionItem> items = ValueItems(analysis, slot, typed, replace, quoted);
            if (quoted) return items;
            TextSpan word = TextSpan.FromBounds(WordStart(text, offset), offset);
            if (TypeRef.Parse(slot.Type)?.Optional == true && !items.Any(i => i.Label == "null"))
                items.Add(new CompletionItem { Label = "null", Kind = CompletionKind.Value, ReplaceSpan = replace, SortText = "001null" });
            if (EnumValuesOf(analysis, SplitType(slot.Type).Name) != null || IsFiniteRefinement(analysis, slot.Type))
                return items;
            if (slot.IsField && new TypeInference(Catalog, analysis).TypeNamed(slot.Type, offset) is { Type: not KType.Any })
            {
                items.AddRange(RankVariables(analysis, offset, VariableCompletions(analysis, offset, word, withSigil: true), slot.Type, matchingOnly: true));
                items.AddRange(MatchingFieldCommands(analysis, offset, slot.Type, CommandCompletions(analysis, word, withSigil: true)));
                return items;
            }
            if (items.Count == 0) return null;
            items.AddRange(ExpressionStartCompletions(analysis, offset, word, template: false).Where(i => i.Kind != CompletionKind.Keyword || i.Label == "null"));
            return items;
        }

        private List<CompletionItem> MatchingFieldCommands(DocumentAnalysis analysis, int offset, string type, List<CompletionItem> commands)
        {
            var inference = new TypeInference(Catalog, analysis);
            TypeShape expected = inference.TypeNamed(type, offset);
            if (expected == null || expected.Type == KType.Any) return commands;
            TypeRef target = TypeRef.Parse(type);
            bool isEnum = target != null && EnumValuesOf(analysis, target.Name) != null;
            Scope scope = analysis.ScopeAt(offset);
            return commands.Where(item =>
            {
                string name = item.FilterText ?? item.Label;
                Symbol fn = item.Kind == CompletionKind.Function ? scope.Lookup(name, offset) : null;
                string result = item.Kind == CompletionKind.Function
                    ? fn?.DeclaringElement?.GetAttributeValue("returns") : Catalog.GetCommand(name)?.ReturnType;
                TypeRef declared = TypeRef.Parse(result);
                if ((isEnum && declared?.Name != target.Name) || (declared?.Optional == true && target?.Optional != true)) return false;
                return MatchesValueShape(expected, fn?.ReturnShape ?? inference.TypeNamed(result, offset));
            }).ToList();
        }

        /// <summary>Completion items for the values a slot accepts, sorted before other expressions.</summary>
        private List<CompletionItem> ValueItems(DocumentAnalysis analysis, ExpectedValueSlot slot, string typed, TextSpan replace, bool quoted)
        {
            string name = SplitType(slot.Type).Name;
            bool finite = IsFiniteRefinement(analysis, slot.Type);
            bool quote = quoted || (slot.InMap || finite) && IsTextualChoice(analysis, slot.Type);
            MarkupAttribute attribute = XmlFieldValueAt(analysis, replace.Start) ?? (analysis.Markup.Syntax == SourceSyntax.Code ? null
                : analysis.Markup.FindElementAt(replace.Start)?.GetAttribute("value"));
            string Insertion(string value)
            {
                string insert = quote ? QuotedValue(value) : slot.InContainer ? ArgumentText(value) : null;
                if (attribute == null) return insert;
                string escaped = (insert ?? value).Replace("&", "&amp;").Replace("<", "&lt;");
                escaped = attribute.Quote == '\'' ? escaped.Replace("'", "&apos;") : escaped.Replace("\"", "&quot;");
                return insert != null || escaped != value ? escaped : null;
            }
            return AcceptedValues(analysis, slot.Type, typed).Select((v, i) => new CompletionItem
            {
                Label = v.Value,
                Kind = CompletionKind.Value,
                Detail = v.Detail ?? name,
                Documentation = v.Documentation ?? $"A value {name} accepts.",
                InsertText = Insertion(v.Value),
                ReplaceSpan = replace,
                FilterText = v.Value,
                SortText = "000" + i.ToString("D4"),
            }).ToList();
        }

        /// <summary>Where an unquoted value under the cursor starts: Europe/Lo‸ starts at E, en-G‸ at e.</summary>
        internal static int RawValueStart(string text, int offset)
        {
            int i = offset;
            while (i > 0 && !char.IsWhiteSpace(text[i - 1]) && "(),=[]{}:".IndexOf(text[i - 1]) < 0)
            {
                i--;
            }
            return i;
        }

        private static int RawValueEnd(string text, int offset)
        {
            int i = offset;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && "(),=[]{}".IndexOf(text[i]) < 0)
            {
                i++;
            }
            return i;
        }

        /// <summary>
        /// After <c>@accounts.</c> or <c>accounts.</c>, where accounts is an import alias: what the imported script
        /// exports (its functions with their signatures and docs, then its constants and types), or the commands of
        /// an imported package. Null anywhere else. It reads the text, so it works while the line doesn't parse yet.
        /// </summary>
        private List<CompletionItem> AliasMemberCompletions(DocumentAnalysis analysis, int offset)
        {
            string text = analysis.Source.Text;
            int wordStart = WordStart(text, offset);
            if (wordStart < 2 || text[wordStart - 1] != '.')
            {
                return null;
            }
            int aliasEnd = wordStart - 1, aliasStart = aliasEnd;
            while (aliasStart > 0 && Lexer.IsIdentChar(text[aliasStart - 1]))
            {
                aliasStart--;
            }
            if (aliasStart == aliasEnd || !Lexer.IsIdentStart(text[aliasStart]))
            {
                return null;
            }
            char before = aliasStart > 0 ? text[aliasStart - 1] : '\0';
            if (before == '$' || before == '.' || before == '>' || Lexer.IsIdentChar(before))
            {
                return null;
            }
            string aliasName = text.Substring(aliasStart, aliasEnd - aliasStart);
            if (!(analysis.ScopeAt(offset).Lookup(aliasName, offset) is Symbol import) || import.Kind != SymbolKind.Import)
            {
                return null;
            }

            int wordEnd = offset;
            while (wordEnd < text.Length && Lexer.IsIdentChar(text[wordEnd]))
            {
                wordEnd++;
            }
            TextSpan replace = TextSpan.FromBounds(wordStart, wordEnd);
            // A function named without a call: a route's to: @accounts.show_login, or a name already followed by (.
            int p = before == '@' ? aliasStart - 1 : aliasStart;
            while (p > 0 && char.IsWhiteSpace(text[p - 1]))
            {
                p--;
            }
            bool reference = (wordEnd < text.Length && text[wordEnd] == '(')
                || (p >= 3 && text.Substring(p - 3, 3) == "to:") || (p >= 4 && text.Substring(p - 4, 4) == "to=\"");

            List<CompletionItem> items = new List<CompletionItem>();
            foreach (Symbol m in import.Members.Values)
            {
                bool fn = m.Kind == SymbolKind.Function;
                items.Add(new CompletionItem
                {
                    Label = m.Name,
                    Kind = fn ? CompletionKind.Function : CompletionKind.Constant,
                    Detail = fn ? m.TypedSignature : m.Shape.Type != KType.Any ? m.Shape.Display() : null,
                    Documentation = SymbolMarkdown(analysis, m),
                    InsertText = fn && !reference ? m.Name + (m.Parameters.Count == 0 ? "()" : "($1)") : null,
                    IsSnippet = fn && !reference,
                    ReplaceSpan = replace,
                    FilterText = m.Name,
                    SortText = (fn ? "0" : "1") + m.Name,
                });
            }
            if (import.Package != null)
            {
                foreach (CallableInfo c in Catalog.Commands.Where(c => c.Package == import.Package && !c.Planned))
                {
                    items.Add(new CompletionItem
                    {
                        Label = c.Name,
                        Kind = CompletionKind.Command,
                        Detail = c.Labels.FirstOrDefault(),
                        Documentation = CallableMarkdown(c),
                        Deprecated = c.Deprecated != null,
                        InsertText = reference ? null : c.Name + (c.MaxArgs == 0 ? "()" : "($1)"),
                        IsSnippet = !reference,
                        ReplaceSpan = replace,
                        FilterText = c.Name,
                        SortText = (c.Deprecated != null ? "9" : "0") + c.Name,
                    });
                }
                foreach (FieldInfo f in import.Shape.Fields.Values.Where(f => f.Shape.Type != KType.Fn))
                {
                    items.Add(new CompletionItem { Label = f.Name, Kind = CompletionKind.Variable, Detail = f.Shape.Display(), Documentation = f.Description, ReplaceSpan = replace, SortText = "1" + f.Name });
                }
            }
            return items;
        }

        /// <summary>Fields after <c>.</c>: keys of a map literal, an import's functions, or a built-in value's fields.</summary>
        private List<CompletionItem> FieldCompletions(DocumentAnalysis analysis, ExpressionRegion region, int dotIndex, TextSpan replace)
        {
            ExprNode target = region.Nodes.SelectMany(n => n.DescendantsAndSelf())
                .Where(n => n.Span.End == dotIndex && !(n is ArgumentNode))
                .OrderByDescending(n => n.Span.Length)
                .FirstOrDefault();

            if (target is IdentifierNode alias)
            {
                Symbol import = analysis.ScopeAt(region.Source.ToSource(alias.Span.Start)).Lookup(alias.Name, int.MaxValue);
                if (import?.Kind == SymbolKind.Import)
                {
                    return import.Shape.Fields.Values.Select(f => new CompletionItem
                    {
                        Label = f.Name,
                        Kind = f.Shape.Type == KType.Fn ? CompletionKind.Function : CompletionKind.Constant,
                        Documentation = f.Description,
                        InsertText = f.Shape.Type == KType.Fn ? f.Name + "($1)" : null,
                        IsSnippet = f.Shape.Type == KType.Fn,
                        ReplaceSpan = replace,
                    }).ToList();
                }
            }

            // An import is lib.name, not $lib.name.
            if (target == null || (target is VariableNode v && analysis.ScopeAt(region.Source.ToSource(v.Span.Start)).Lookup(v.Name, int.MaxValue)?.Kind == SymbolKind.Import))
            {
                return new List<CompletionItem>();
            }

            return FieldItems(InferAt(analysis, region, target), replace);
        }

        #endregion
    }
}
