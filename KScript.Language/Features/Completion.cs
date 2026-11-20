using KScript.Language.Analysis;
using KScript.Language.Catalog;
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
            ("eq", "Loose equality, the same as `==`: `'3' eq 3` is true."),
            ("ne", "Loose inequality, the same as `!=`."),
            ("lt", "Less than: `$a lt $b`. (A raw `<` isn't allowed in XML attributes.)"),
            ("le", "Less than or equal."),
            ("gt", "Greater than (`>` also works)."),
            ("ge", "Greater than or equal (`>=` also works)."),
            ("in", "Membership: `$ext in ['.cs', '.xml']`."),
            ("true", "The boolean true."),
            ("false", "The boolean false."),
            ("null", "No value."),
            ("fn", "A lambda: `fn($x) => $x * 2`."),
            ("match", "Match expression: `match $x { 1 => 'one', _ => 'other' }`."),
        };

        /// <summary>Completions at an offset.</summary>
        public List<CompletionItem> GetCompletions(DocumentAnalysis analysis, int offset)
        {
            Cursor cursor = Locate(analysis, offset);
            string text = analysis.Source.Text;
            if (InComment(text, offset))
            {
                return new List<CompletionItem>();
            }

            if (analysis.Kind == DocumentKind.Markdown && analysis.Markup.FrontMatter is FrontMatter fm && offset <= fm.Span.End && offset > 3)
            {
                return FrontMatterCompletions(analysis, offset);
            }

            // Typing a word where a statement starts: offer keywords, variables, functions and commands without a trigger character.
            if (cursor.Region == null && !cursor.InAttributeValue && !cursor.InEndTagName && StatementWordStart(analysis, offset) is int statementStart)
            {
                return StatementCompletions(analysis, statementStart, offset);
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
                MarkupElement parent = analysis.Markup.FindElementAt(offset);
                return ElementCompletions(analysis, parent, new TextSpan(offset, 0), previous: PreviousElementBefore(text, parent, offset - 1), snippets: true);
            }
            if (offset > 1 && text[offset - 1] == '/' && text[offset - 2] == '<')
            {
                MarkupElement open = analysis.Markup.FindElementAt(offset - 2);
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
            int wordStart = WordStart(text, offset);
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

        private static readonly Dictionary<string, string> CodeStatementSnippets = new Dictionary<string, string>
        {
            ["if"] = "if ${1:condition} {\n\t$0\n}",
            ["while"] = "while ${1:condition} {\n\t$0\n}",
            ["for"] = "for ${1:item} in ${2:\\$list} {\n\t$0\n}",
            ["fn"] = "fn ${1:name}($2) {\n\t$0\n}",
            ["try"] = "try {\n\t$0\n} catch err {\n\t\n}",
            ["match"] = "match ${1:\\$value} {\n\t$0\n}",
            ["echo"] = "echo $0",
            ["return"] = "return $0",
        };

        /// <summary>Elements a .kscript statement can't start with: they're written another way (x = 3, name: type, } else {).</summary>
        private static readonly HashSet<string> NotCodeStatements = new HashSet<string> { "kscript", "do", "let", "set", "field" };

        /// <summary>
        /// The start of the word before the cursor when that word begins a statement: at the start of a line (or after '{' or ';')
        /// in a .kscript file, or on its own line in the body of an XML script. Null anywhere else.
        /// </summary>
        private static int? StatementWordStart(DocumentAnalysis analysis, int offset)
        {
            string text = analysis.Source.Text;
            int wordStart = WordStart(text, offset);
            if (wordStart == offset || !Lexer.IsIdentStart(text[wordStart]) || analysis.Kind != DocumentKind.Script)
            {
                return null;
            }
            int p = wordStart;
            while (p > 0 && (text[p - 1] == ' ' || text[p - 1] == '\t'))
            {
                p--;
            }
            char before = p == 0 ? '\n' : text[p - 1];
            if (analysis.Markup.Syntax == SourceSyntax.Code)
            {
                bool inComment = analysis.Markup.DescendantNodes().OfType<MarkupComment>().Any(c => offset > c.Span.Start && offset <= c.Span.End);
                return !inComment && (before is '\n' or '\r' or '{' or ';') ? wordStart : null;
            }
            return before is '\n' or '\r' or '>' ? wordStart : null;
        }

        private List<CompletionItem> StatementCompletions(DocumentAnalysis analysis, int wordStart, int offset)
        {
            TextSpan replace = TextSpan.FromBounds(wordStart, offset);
            MarkupElement typed = analysis.Markup.DescendantElements().FirstOrDefault(e => e.NameSpan.Start == wordStart && e.NameSpan.Length > 0);
            MarkupElement parent = typed != null ? typed.Parent : analysis.Markup.FindElementAt(offset);
            MarkupElement previous = typed != null ? PreviousSibling(typed) : PreviousElementBefore(analysis.Source.Text, parent, wordStart);

            if (analysis.Markup.Syntax != SourceSyntax.Code)
            {
                // XML: the word becomes an element, so the '<' comes with it.
                return ElementCompletions(analysis, parent, replace, previous, snippets: true, openBracket: "<");
            }

            List<CompletionItem> items = new List<CompletionItem>();
            foreach (ElementInfo e in Catalog.Elements)
            {
                if (!e.Context.HasFlag(ElementContext.Script) || NotCodeStatements.Contains(e.Name) || e.MustFollow?.Count > 0 || !FitsHere(e, parent, previous))
                {
                    continue;
                }
                CodeStatementSnippets.TryGetValue(e.Name, out string snippet);
                items.Add(new CompletionItem
                {
                    Label = e.Name,
                    Kind = CompletionKind.Keyword,
                    Detail = e.Planned ? "KScript 2 (planned)" : "statement",
                    Documentation = ElementMarkdown(e),
                    InsertText = snippet,
                    IsSnippet = snippet != null,
                    ReplaceSpan = replace,
                    SortText = "0" + e.Name,
                });
            }

            // x = 3 assigns: offer the variables in scope by their bare names.
            Scope scope = analysis.ScopeAt(offset);
            items.AddRange(scope.VisibleAt(offset)
                .Where(s => s.Kind is SymbolKind.Variable or SymbolKind.Parameter or SymbolKind.LoopVariable)
                .GroupBy(s => s.Name).Select(g => g.First())
                .Select(s => new CompletionItem
                {
                    Label = s.Name,
                    Kind = s.Kind == SymbolKind.Parameter ? CompletionKind.Parameter : CompletionKind.Variable,
                    Detail = s.Shape.Display(),
                    Documentation = SymbolMarkdown(analysis, s),
                    ReplaceSpan = replace,
                    SortText = "1" + s.Name,
                }));
            items.AddRange(CommandCompletions(analysis, replace, withSigil: true));
            return items;
        }

        #region Markup

        private List<CompletionItem> ElementCompletions(DocumentAnalysis analysis, MarkupElement parent, TextSpan replace, MarkupElement previous, bool snippets, string openBracket = "")
        {
            List<CompletionItem> items = new List<CompletionItem>();
            ElementContext context = analysis.Kind == DocumentKind.Script ? ElementContext.Script : ElementContext.Page;

            foreach (ElementInfo e in Catalog.Elements)
            {
                if (!e.Context.HasFlag(context) || (context == ElementContext.Page && e.HtmlWins))
                {
                    continue;
                }

                // Only offer elements that would be valid here, mirroring the analyzer's placement checks.
                if (!FitsHere(e, parent, previous))
                {
                    continue;
                }
                bool fits = true;

                items.Add(new CompletionItem
                {
                    Label = e.Name,
                    Kind = CompletionKind.Element,
                    Detail = e.Planned ? "KScript 2 (planned)" : "KScript element",
                    Documentation = ElementMarkdown(e),
                    InsertText = snippets ? openBracket + ElementSnippet(e) : null,
                    IsSnippet = snippets,
                    ReplaceSpan = replace,
                    FilterText = openBracket.Length > 0 ? e.Name : null,
                    SortText = (fits ? "0" : "1") + e.Name,
                });
            }

            if (context == ElementContext.Page && analysis.Path != null)
            {
                string dir = FindComponentsFolder(analysis.Path);
                if (dir != null)
                {
                    foreach (string file in Files.ListFiles(dir, "*.html"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        List<string> props = ComponentProps(file);
                        int n = 1;
                        string propSnippet = string.Concat(props.Select(p => $" {p}=\"${n++}\""));
                        items.Add(new CompletionItem
                        {
                            Label = name,
                            Kind = CompletionKind.Element,
                            Detail = "Component (_components/" + name + ".html)",
                            Documentation = props.Count > 0 ? "Props: " + string.Join(", ", props) : null,
                            InsertText = snippets ? (props.Count > 0 ? name + propSnippet + " />" : name + " $1/>") : null,
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
            if (e.MustBeInside != null && e.MustBeInside.Count > 0)
            {
                bool inside = e.MustBeInside.Contains("fn") || e.InsideAnyAncestor
                    ? parent != null && (e.MustBeInside.Contains(ElementInfo.BareName(parent.Name)) || parent.Ancestors().Any(a => e.MustBeInside.Contains(ElementInfo.BareName(a.Name))))
                    : parent != null && e.MustBeInside.Contains(parent.Name);
                if (!inside)
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

        private string FindComponentsFolder(string path)
        {
            for (string dir = Path.GetDirectoryName(Path.GetFullPath(path)); dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "_components");
                if (Files.DirectoryExists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>The component file for a tag such as &lt;person-card&gt;, found in the nearest _components folder.</summary>
        internal string ComponentFile(string documentPath, string tag)
        {
            if (string.IsNullOrEmpty(documentPath) || string.IsNullOrEmpty(tag))
            {
                return null;
            }
            string dir = FindComponentsFolder(documentPath);
            if (dir == null)
            {
                return null;
            }
            string file = Path.Combine(dir, tag.ToLowerInvariant() + ".html");
            return Files.FileExists(file) ? file : null;
        }

        /// <summary>A component's props, in the order the component first uses them. $slot and the like are left out.</summary>
        internal List<string> ComponentProps(string componentFile)
        {
            string text = Files.ReadText(componentFile);
            if (text == null)
            {
                return new List<string>();
            }
            return Analyze(text, componentFile).Inputs.Keys.ToList();
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

            if (info == null)
            {
                // A component's props: the variables its file uses without declaring.
                string file = ComponentFile(analysis.Path, e.Name);
                if (file != null)
                {
                    foreach (string prop in ComponentProps(file))
                    {
                        if (e.Attributes.Any(x => x.Name == prop && x.NameSpan.Start != start))
                        {
                            continue;
                        }
                        items.Add(new CompletionItem
                        {
                            Label = prop,
                            Kind = CompletionKind.Attribute,
                            Detail = "prop of <" + e.Name + ">",
                            Documentation = $"Available in _components/{e.Name}.html as ${prop}. Pass a value with {{ }}, e.g. {prop}=\"{{$value}}\".",
                            InsertText = prop + "=\"$1\"",
                            IsSnippet = true,
                            ReplaceSpan = TextSpan.FromBounds(start, offset),
                            SortText = "0" + prop,
                        });
                    }
                }
                return items;
            }

            foreach (AttributeInfo a in info.Attributes)
            {
                if (e.Attributes.Any(x => x.Name == a.Name && !(x.NameSpan.Start == start)))
                {
                    continue;
                }
                items.Add(new CompletionItem
                {
                    Label = a.Name,
                    Kind = CompletionKind.Attribute,
                    Detail = (a.Required ? "required " : string.Empty) + KindLabel(a.Kind),
                    Documentation = a.Description + OptionsLine(a),
                    InsertText = a.Name + "=\"$1\"",
                    IsSnippet = true,
                    ReplaceSpan = TextSpan.FromBounds(start, offset),
                    SortText = (a.Required ? "0" : "1") + a.Name,
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

                case AttributeKind.Bool:
                    return new[] { "yes", "no" }.Select(o => new CompletionItem { Label = o, Kind = CompletionKind.Value, ReplaceSpan = TextSpan.FromBounds(value.SourceSpan.Start, value.SourceSpan.End) }).ToList();

                case AttributeKind.FunctionReference:
                case AttributeKind.FunctionReferences:
                {
                    // Functions, functions of imported scripts (people.show), and built-in middleware (KScript.Http).
                    List<CompletionItem> items = FunctionSymbolCompletions(analysis, replace);
                    foreach (Symbol import in analysis.Symbols.Where(s => s.Kind == SymbolKind.Import))
                    {
                        foreach (FieldInfo field in import.Shape.Fields.Values.Where(f => f.Shape.Type == KType.Fn))
                        {
                            items.Add(new CompletionItem { Label = import.Name + "." + field.Name, Kind = CompletionKind.Function, Detail = field.Description, ReplaceSpan = replace });
                        }
                    }
                    items.AddRange(a.Options.Select(o => new CompletionItem { Label = o, Kind = CompletionKind.Value, Detail = "built-in middleware", ReplaceSpan = replace }));
                    return items;
                }

                case AttributeKind.VariableReference:
                    return VariableCompletions(analysis, cursor.Offset, replace, withSigil: false);

                case AttributeKind.Path:
                    return PathCompletions(analysis, value, cursor.Offset, scriptsOnly: cursor.TagElement.Name == "import");

                case AttributeKind.Expression:
                case AttributeKind.ExpressionList:
                case AttributeKind.Statements:
                case AttributeKind.Template:
                    return cursor.Region != null ? null : ExpressionStartCompletions(analysis, cursor.Offset, replace, a.Kind == AttributeKind.Template);

                default:
                    return new List<CompletionItem>();
            }
        }

        /// <summary>The &lt;type&gt;s and &lt;enum&gt;s a document declares or imports.</summary>
        internal static IReadOnlyList<string> TypeNames(DocumentAnalysis analysis) =>
            analysis.Symbols.Where(s => s.EnumValues != null || s.Shape.Type == KType.Type).Select(s => s.Name).Distinct().ToList();

        private static readonly string[] ScriptExtensions = { ".ks.xml", ".ks", ".kscript", ".xml" };
        private static readonly string[] PageExtensions = { ".html", ".md" };
        private static readonly string[] SkippedFolders = { "node_modules", "bin", "obj", "_templates" };
        private const int MaxProjectFiles = 300;

        /// <summary>
        /// Paths relative to the current file: the files and folders in the folder typed so far, and, before any
        /// <c>/</c> is typed, every script in the project (the folder of the nearest kscript.json, or this file's
        /// folder). An import only offers scripts; other path attributes also offer .html and .md pages.
        /// </summary>
        private List<CompletionItem> PathCompletions(DocumentAnalysis analysis, DecodedText value, int offset, bool scriptsOnly)
        {
            List<CompletionItem> items = new List<CompletionItem>();
            if (analysis.Path == null)
            {
                return items;
            }

            string self = Path.GetFullPath(analysis.Path);
            string dir = Path.GetDirectoryName(self);
            int index = value.FromSource(offset);
            string typed = index >= 0 ? value.Value.Substring(0, index) : string.Empty;
            int slash = typed.LastIndexOf('/');
            string sub = slash >= 0 ? typed.Substring(0, slash) : string.Empty;
            string folder = Path.GetFullPath(Path.Combine(dir, sub));
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
                .Where(f => !f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) || LooksLikeScript(f))
                .OrderBy(f => f, StringComparer.Ordinal);

        /// <summary>A plain .xml file is offered only when it is a script (starts with &lt;kscript&gt;).</summary>
        private bool LooksLikeScript(string path)
        {
            string text = Files.ReadText(path);
            return text != null && text.AsSpan(0, Math.Min(text.Length, 512)).IndexOf("<kscript", StringComparison.Ordinal) >= 0;
        }

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
            AttributeKind.Bool => "yes/no",
            _ => "text",
        };

        private static string OptionsLine(AttributeInfo a) =>
            a.Options.Count > 0 ? "\n\nOne of: " + string.Join(", ", a.Options.Select(o => "`" + o + "`")) : string.Empty;

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
                return new List<CompletionItem>();
            }

            if (before == '$')
            {
                return VariableCompletions(analysis, offset, replace, withSigil: false);
            }
            if (before == '@')
            {
                return CommandCompletions(analysis, replace, withSigil: false);
            }
            if (before == '>' && wordStart >= 2 && text[wordStart - 2] == '-')
            {
                return MethodCompletions(analysis, region, wordStart - 2, replace, pipe: false);
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

            List<CompletionItem> start = ExpressionStartCompletions(analysis, offset, replace, template: false);
            start.InsertRange(0, ParameterNameCompletions(analysis, region, wordStart, replace));
            start.InsertRange(0, NamedValueCompletions(analysis, region, wordStart, replace));
            return start;
        }

        private List<CompletionItem> ExpressionStartCompletions(DocumentAnalysis analysis, int offset, TextSpan replace, bool template)
        {
            List<CompletionItem> items = new List<CompletionItem>();
            if (template)
            {
                return items;
            }
            items.AddRange(VariableCompletions(analysis, offset, replace, withSigil: true));
            items.AddRange(CommandCompletions(analysis, replace, withSigil: true));
            items.AddRange(ExpressionKeywords.Select(k => new CompletionItem
            {
                Label = k.Word,
                Kind = CompletionKind.Keyword,
                Documentation = k.Doc,
                InsertText = k.Word == "fn" ? "fn(\\$${1:x}) => $0" : null,
                IsSnippet = k.Word == "fn",
                ReplaceSpan = replace,
                SortText = "3" + k.Word,
            }));
            return items;
        }

        private List<CompletionItem> VariableCompletions(DocumentAnalysis analysis, int offset, TextSpan replace, bool withSigil)
        {
            Scope scope = analysis.ScopeAt(offset);
            return scope.VisibleAt(offset)
                .Where(s => s.Kind != SymbolKind.Function && s.Kind != SymbolKind.Import)
                .Select(s => new CompletionItem
                {
                    Label = (withSigil ? "$" : string.Empty) + s.Name,
                    Kind = s.Kind == SymbolKind.Parameter ? CompletionKind.Parameter : s.IsConst ? CompletionKind.Constant : CompletionKind.Variable,
                    Detail = s.Shape.Display(),
                    Documentation = SymbolMarkdown(analysis, s),
                    ReplaceSpan = replace,
                    // The span replaced is the bare word (the $ isn't typed yet, or sits before it), so filter on the bare name.
                    FilterText = s.Name,
                    SortText = "0" + s.Name,
                })
                .ToList();
        }

        private List<CompletionItem> CommandCompletions(DocumentAnalysis analysis, TextSpan replace, bool withSigil)
        {
            string sigil = withSigil ? "@" : string.Empty;
            List<CompletionItem> items = analysis.Symbols.Where(s => s.Kind == SymbolKind.Function)
                .GroupBy(s => s.Name).Select(g => g.First())
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

            items.AddRange(Catalog.Commands.Select(c => new CompletionItem
            {
                Label = sigil + c.Name,
                Kind = CompletionKind.Command,
                Detail = c.Labels.FirstOrDefault(),
                Documentation = CallableMarkdown(c),
                InsertText = sigil + c.Name + (c.MaxArgs == 0 ? "()" : "($1)"),
                IsSnippet = true,
                ReplaceSpan = replace,
                FilterText = c.Name,
                SortText = "2" + c.Name,
            }));
            return items;
        }

        private List<CompletionItem> FunctionSymbolCompletions(DocumentAnalysis analysis, TextSpan replace) =>
            analysis.Symbols.Where(s => s.Kind == SymbolKind.Function).GroupBy(s => s.Name).Select(g => g.First())
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

            List<CompletionItem> items = MethodItems(receiver, replace);

            if (pipe)
            {
                items.AddRange(analysis.Symbols.Where(s => s.Kind == SymbolKind.Function).GroupBy(s => s.Name).Select(g => g.First())
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
        private List<CompletionItem> MethodItems(TypeShape receiver, TextSpan replace) =>
            Catalog.MethodsFor(receiver.Type)
                .Select(m => new CompletionItem
                {
                    Label = m.Name,
                    Kind = CompletionKind.Method,
                    Detail = m.Labels.First() + ": " + MethodResult(m, receiver).Display()
                             + (m.ReceiverType != KType.Any && m.ReceiverType != receiver.Type ? "  (" + m.ReceiverType + ")" : string.Empty),
                    Documentation = CallableMarkdown(m, receiver.Type != KType.Any ? receiver : null),
                    InsertText = m.Name + (m.MaxArgs == 0 ? "()" : "($1)"),
                    IsSnippet = true,
                    ReplaceSpan = replace,
                    SortText = (receiver.Type == KType.Any || m.ReceiverType == receiver.Type ? "0" : m.ReceiverType == KType.Any ? "1" : "2") + m.Name,
                }).ToList();

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
                return MethodItems(receiver, replace);
            }
            return receiver.Fields.Values.Select(f => new CompletionItem
            {
                Label = f.Name,
                Kind = CompletionKind.Field,
                Detail = f.Shape.Display(),
                Documentation = f.Description,
                ReplaceSpan = replace,
            }).ToList();
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
                        Kind = CompletionKind.Function,
                        Documentation = f.Description,
                        InsertText = f.Name + "($1)",
                        IsSnippet = true,
                        ReplaceSpan = replace,
                    }).ToList();
                }
            }

            if (target == null)
            {
                return new List<CompletionItem>();
            }

            TypeShape shape = InferAt(analysis, region, target);
            return shape.Fields.Values.Select(f => new CompletionItem
            {
                Label = f.Name,
                Kind = CompletionKind.Field,
                Detail = f.Shape.Display(),
                Documentation = f.Description,
                ReplaceSpan = replace,
            }).ToList();
        }

        #endregion
    }
}
