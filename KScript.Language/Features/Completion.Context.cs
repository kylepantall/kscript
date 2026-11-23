using System.Text.RegularExpressions;
using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    /// <summary>
    /// Completion where the parsers leave no element or expression under the cursor: the start of a statement (typed or
    /// blank), <c>KScript.</c> package elements, <c>import</c> lines, and an expression that hasn't been started yet
    /// (<c>x = ‸</c>, <c>echo ‸</c>, <c>$list |&gt; ‸</c>). Items from a package the script doesn't import add the import.
    /// </summary>
    public sealed partial class LanguageService
    {
        /// <summary>Snippets for statements a .kscript file writes its own way; other elements get <see cref="CodeElementSnippet"/>.</summary>
        private static readonly Dictionary<string, string> CodeStatementSnippets = new Dictionary<string, string>
        {
            ["if"] = "if ${1:condition} {\n\t$0\n}",
            ["elif"] = "elif ${1:condition} {\n\t$0\n}",
            ["else"] = "else {\n\t$0\n}",
            ["while"] = "while ${1:condition} {\n\t$0\n}",
            ["for"] = "for ${1:item} in ${2:\\$list} {\n\t$0\n}",
            ["fn"] = "fn ${1:name}($2) {\n\t$0\n}",
            ["try"] = "try {\n\t$0\n} catch err {\n\t\n}",
            ["catch"] = "catch err {\n\t$0\n}",
            ["finally"] = "finally {\n\t$0\n}",
            ["match"] = "match ${1:\\$value} {\n\t$0\n}",
            ["type"] = "type ${1:Name} {\n\t${2:field}: ${3:string}\n}",
            ["enum"] = "enum ${1:Name} { ${2:a}, ${3:b} }",
            ["echo"] = "echo $0",
            ["return"] = "return $0",
            ["throw"] = "throw '$0'",
            ["import"] = "import $0",
            ["const"] = "const ${1:name} = $0",
        };

        /// <summary>Elements a .kscript statement can't start with: they're written another way (x = 3, name: type).</summary>
        private static readonly HashSet<string> NotCodeStatements = new HashSet<string> { "kscript", "do", "let", "set", "field", "case", "default" };

        /// <summary>Words after which an expression starts (<c>echo ‸</c>, <c>if ‸</c>, <c>$a and ‸</c>).</summary>
        private static readonly HashSet<string> ExpressionKeywordsBefore = new HashSet<string>
        {
            "echo", "return", "throw", "exit", "if", "elif", "while", "in", "step", "and", "or", "not", "eq", "ne", "lt", "le", "gt", "ge", "when", "const",
        };

        private static readonly Regex ImportNamesBefore = new Regex(@"^[ \t]*import[ \t]+((?:\w+[ \t]*,[ \t]*)*)\w*$", RegexOptions.Compiled);
        private static readonly Regex ImportFromAfter = new Regex(@"^\w*(?:[ \t]*,[ \t]*\w+)*[ \t]+from[ \t]+""([^""]*)""", RegexOptions.Compiled);

        private static readonly Regex ImportLine = new Regex(@"^[ \t]*import[ \t]+([\w.]*)$", RegexOptions.Compiled);

        private List<CompletionItem> ContextCompletions(DocumentAnalysis analysis, Cursor cursor)
        {
            if (analysis.Kind != DocumentKind.Script)
            {
                return null;
            }
            string text = analysis.Source.Text;
            int offset = cursor.Offset;
            bool code = analysis.Markup.Syntax == SourceSyntax.Code;

            if (code && analysis.Markup.DescendantNodes().OfType<MarkupComment>().Any(c => offset > c.Span.Start && offset <= c.Span.End))
            {
                return new List<CompletionItem>();
            }

            // A line of a type or model block (min: ...), and rules given to a type: string(m‸).
            if (code && TypeBodyCompletions(analysis, offset) is List<CompletionItem> body)
            {
                return body;
            }

            // <import package="‸"> and import ‸ (in a .kscript file).
            if (cursor.InAttributeValue && cursor.TagElement.Name == "import" && cursor.Attribute.Name == "package")
            {
                return PackageCompletions(analysis, cursor.Attribute.Value.SourceSpan);
            }
            if (cursor.InAttributeValue && cursor.TagElement.Name == "import" && cursor.Attribute.Name == "names")
            {
                string typed = cursor.Attribute.Value.Value.Substring(0, Math.Max(0, cursor.Attribute.Value.FromSource(offset)));
                return ImportNameCompletions(analysis, cursor.TagElement.GetAttributeValue("path"), TextSpan.FromBounds(WordStart(text, offset), offset), typed.Split(','));
            }
            int lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
            int lineEnd = text.IndexOf('\n', offset) is int nl && nl >= 0 ? nl : text.Length;
            if (code && ImportNamesBefore.Match(text.Substring(lineStart, offset - lineStart)) is { Success: true } names
                && ImportFromAfter.Match(text.Substring(offset, lineEnd - offset)) is { Success: true } from)
            {
                // import a, ‸ from "lib.kscript": the functions, constants, types and enums the file declares.
                return ImportNameCompletions(analysis, from.Groups[1].Value, TextSpan.FromBounds(WordStart(text, offset), offset), names.Groups[1].Value.Split(','));
            }
            if (code && ImportLine.Match(text.Substring(lineStart, offset - lineStart)) is { Success: true } import)
            {
                // import ‸ (the parser has no package yet); a written name is the package attribute, above.
                return PackageCompletions(analysis, TextSpan.FromBounds(lineStart + import.Groups[1].Index, offset));
            }

            // <KScript.R‸: the package elements, by their full name.
            if (!code && cursor.InElementName && cursor.TagElement.Name.StartsWith("KScript.", StringComparison.Ordinal))
            {
                return PackageElementCompletions(cursor.TagElement.NameSpan, xml: true,
                    snippets: !cursor.TagElement.Attributes.Any() && !cursor.TagElement.StartTagClosed);
            }

            if (cursor.Region != null || cursor.InAttributeValue || cursor.InEndTagName)
            {
                return null;
            }
            if (cursor.InStartTag && !cursor.InElementName)
            {
                return code ? StartTagGapCompletions(analysis, cursor) : null;
            }

            int wordStart = WordStart(text, offset);
            if (wordStart < offset && !Lexer.IsIdentStart(text[wordStart]))
            {
                return null;
            }

            if (AtStatementBoundary(analysis, wordStart))
            {
                return StatementCompletions(analysis, wordStart, offset);
            }
            if (code && FollowsBlockEnd(text, wordStart))
            {
                return FollowOnCompletions(analysis, wordStart, offset);
            }
            if (code)
            {
                return ExpressionGapCompletions(analysis, wordStart, offset, keywordsOnly: false);
            }

            // Ctrl+Space in the empty text of <echo>, <log>...: what { } and $ references can show.
            MarkupElement holder = analysis.Markup.FindElementAt(offset);
            bool spaced = offset == 0 || char.IsWhiteSpace(text[offset - 1]) || text[offset - 1] == '>';
            return wordStart == offset && spaced && holder != null && Catalog.GetElement(holder.Name)?.Content == ContentKind.Template
                ? ExpressionStartCompletions(analysis, offset, new TextSpan(offset, 0), template: false).Where(i => i.Kind != CompletionKind.Keyword).ToList()
                : null;
        }

        /// <summary>
        /// Inside a .kscript statement's header where no value is written yet: <c>if ‸ {</c>, <c>for x in ‸</c>,
        /// <c>fn f(a: ‸)</c>.
        /// </summary>
        private List<CompletionItem> StartTagGapCompletions(DocumentAnalysis analysis, Cursor cursor)
        {
            string text = analysis.Source.Text;
            int offset = cursor.Offset;
            int wordStart = WordStart(text, offset);
            if (cursor.TagElement.Name == "match" && cursor.TagElement.ContentSpan.Contains(offset) && AtStatementBoundary(analysis, wordStart))
            {
                // A .kscript match block is one statement, cases and all: a new line in it starts a case.
                return StatementCompletions(analysis, wordStart, offset);
            }
            // wait: duration = ‸ (no value yet): the values a native type offers.
            if (wordStart > 0 && text.Substring(0, wordStart).TrimEnd().EndsWith('=') && cursor.TagElement.GetAttribute("value")?.Value == null
                && DeclaredTypeValueCompletions(cursor.TagElement, TextSpan.FromBounds(wordStart, offset)) is { Count: > 0 } typed)
            {
                return typed;
            }
            foreach (MarkupAttribute a in cursor.TagElement.Attributes.Where(a => a.Value != null && a.Value.SourceSpan.End <= wordStart))
            {
                string gap = text.Substring(a.Value.SourceSpan.End, wordStart - a.Value.SourceSpan.End);
                if (gap.Length > 0 && string.IsNullOrWhiteSpace(gap) && Catalog.GetElement(cursor.TagElement.Name)?.GetAttribute(a.Name)?.Kind == AttributeKind.Parameters)
                {
                    return ParameterValueCompletions(analysis, a.ValueText + gap, offset, TextSpan.FromBounds(wordStart, offset));
                }
            }
            return ExpressionGapCompletions(analysis, wordStart, offset, keywordsOnly: true);
        }

        /// <summary>A parameter list up to the cursor (<c>a, b: ‸</c>): a type after ':', an expression after '=', nothing for a new name.</summary>
        private List<CompletionItem> ParameterValueCompletions(DocumentAnalysis analysis, string before, int offset, TextSpan replace)
        {
            string param = before.Substring(before.LastIndexOf(',') + 1);
            if (param.Contains('='))
            {
                return ExpressionStartCompletions(analysis, offset, replace, template: false);
            }
            return param.Contains(':') ? TypeCompletions(analysis, replace) : new List<CompletionItem>();
        }

        /// <summary>
        /// True when a statement can start at <paramref name="start"/>: at the start of a line (or after '{' or ';') in a
        /// .kscript file, outside comments; on its own line (or straight after a tag) in an XML script, where statements go.
        /// </summary>
        private bool AtStatementBoundary(DocumentAnalysis analysis, int start)
        {
            string text = analysis.Source.Text;
            int p = start;
            while (p > 0 && (text[p - 1] == ' ' || text[p - 1] == '\t'))
            {
                p--;
            }
            char before = p == 0 ? '\n' : text[p - 1];
            if (analysis.Markup.Syntax == SourceSyntax.Code)
            {
                return before is '{' or ';' || (before is '\n' or '\r' && !ContinuesExpression(analysis, p - 1));
            }
            if (!(before is '\n' or '\r' or '>'))
            {
                return false;
            }
            // Only where statements go: the root, or an element whose content is statements (not <echo>'s text).
            MarkupElement parent = analysis.Markup.FindElementAt(start);
            return parent == null || parent.Name == "kscript" || parent.Name.StartsWith("KScript.", StringComparison.Ordinal)
                || analysis.Markup.RootElements.FirstOrDefault() == parent
                || Catalog.GetElement(parent.Name)?.Content is ContentKind.Statements;
        }

        /// <summary>True when the line before <paramref name="newline"/> ends with an operator, so the next line continues its expression.</summary>
        private static bool ContinuesExpression(DocumentAnalysis analysis, int newline)
        {
            string text = analysis.Source.Text;
            int p = newline;
            while (p > 0 && char.IsWhiteSpace(text[p - 1]))
            {
                p--;
            }
            // A comment, or front matter's closing ---, ends the line however it ends.
            bool commented = analysis.Markup.DescendantNodes().OfType<MarkupComment>().Any(c => p > c.Span.Start && p <= c.Span.End);
            bool frontMatter = p >= 3 && text.Substring(p - 3, 3) == "---";
            return p > 0 && !commented && !frontMatter && "+-*/%=,([|&?:<>.".IndexOf(text[p - 1]) >= 0;
        }

        /// <summary><c>} e‸</c>: the word after the closing brace of a block, where else, elif, catch and finally go.</summary>
        private static bool FollowsBlockEnd(string text, int start)
        {
            int p = start;
            while (p > 0 && (text[p - 1] == ' ' || text[p - 1] == '\t'))
            {
                p--;
            }
            return p > 0 && p < start && text[p - 1] == '}';
        }

        private List<CompletionItem> StatementCompletions(DocumentAnalysis analysis, int wordStart, int offset)
        {
            TextSpan replace = TextSpan.FromBounds(wordStart, offset);
            MarkupElement typed = wordStart < offset ? analysis.Markup.DescendantElements().FirstOrDefault(e => e.NameSpan.Start == wordStart && e.NameSpan.Length > 0) : null;
            MarkupElement parent = typed != null ? typed.Parent : analysis.Markup.FindElementAt(wordStart);
            MarkupElement previous = typed != null ? PreviousSibling(typed) : PreviousElementBefore(analysis.Source.Text, parent, wordStart);

            if (analysis.Markup.Syntax != SourceSyntax.Code)
            {
                // XML: the word becomes an element, so the '<' comes with it.
                return ElementCompletions(analysis, parent, replace, previous, snippets: true, openBracket: "<");
            }

            if (parent?.Name == "match")
            {
                // A match block holds only its arms: pattern => result.
                return new List<CompletionItem>
                {
                    new CompletionItem { Label = "value =>", Kind = CompletionKind.Keyword, Detail = "arm", Documentation = "An arm taken when the value is one of these: `1, 2 => echo \"low\"`, or `1, 2 => { }` for several statements.", InsertText = "${1:value} => $0", IsSnippet = true, ReplaceSpan = replace, SortText = "0a" },
                    new CompletionItem { Label = "_ when", Kind = CompletionKind.Keyword, Detail = "arm", Documentation = "An arm taken when its test is true: `_ when $n > 10 => { }`.", InsertText = "_ when ${1:condition} => $0", IsSnippet = true, ReplaceSpan = replace, SortText = "0b" },
                    new CompletionItem { Label = "_ =>", Kind = CompletionKind.Keyword, Detail = "arm", Documentation = "The arm taken when no other matched: `_ => { }`.", InsertText = "_ => $0", IsSnippet = true, ReplaceSpan = replace, SortText = "0c" },
                };
            }
            List<CompletionItem> items = CodeStatementItems(analysis, parent, previous, replace, followOn: false);

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

        private List<CompletionItem> FollowOnCompletions(DocumentAnalysis analysis, int wordStart, int offset)
        {
            TextSpan replace = TextSpan.FromBounds(wordStart, offset);
            int brace = analysis.Source.Text.LastIndexOf('}', wordStart - 1);
            MarkupElement previous = analysis.Markup.DescendantElements().Where(e => e.Span.End == brace + 1).OrderBy(e => e.Span.Length).FirstOrDefault();
            return previous == null ? new List<CompletionItem>() : CodeStatementItems(analysis, previous.Parent, previous, replace, followOn: true);
        }

        /// <summary>The statements a .kscript file can write here: elements that fit, in their code form.</summary>
        private List<CompletionItem> CodeStatementItems(DocumentAnalysis analysis, MarkupElement parent, MarkupElement previous, TextSpan replace, bool followOn)
        {
            List<CompletionItem> items = new List<CompletionItem>();
            foreach (ElementInfo e in Catalog.Elements)
            {
                bool follows = e.MustFollow?.Count > 0;
                if (!e.Context.HasFlag(ElementContext.Script) || e.Planned || NotCodeStatements.Contains(e.Name) || follows != followOn || !FitsHere(e, parent, previous)
                    || NeedsImport(analysis, e.Package))
                {
                    continue;
                }
                string snippet = CodeStatementSnippets.TryGetValue(e.Name, out string s) ? s : CodeElementSnippet(e);
                items.Add(new CompletionItem
                {
                    Label = e.Name,
                    Kind = CompletionKind.Keyword,
                    Detail = e.Deprecated != null ? "statement (deprecated)" : "statement",
                    Documentation = ElementMarkdown(e, analysis.Markup.Syntax == SourceSyntax.Code),
                    Deprecated = e.Deprecated != null,
                    InsertText = snippet,
                    IsSnippet = snippet != null,
                    ReplaceSpan = replace,
                    SortText = e.Deprecated != null ? "9" + e.Name : "0" + Ranking.Statement(e.Name, KScript.Projects.TestSettings.IsTestFileName(analysis.Path)),
                });
            }
            return items;
        }

        /// <summary>An element as a .kscript statement: <c>describe(name: "$1") { }</c>, <c>wait(duration: $1)</c>; null for a bare word.</summary>
        internal static string CodeElementSnippet(ElementInfo e)
        {
            int n = 1;
            List<string> required = e.Attributes.Where(a => a.Required).Select(a => $"{a.Name}: " + (a.Kind is AttributeKind.Template or AttributeKind.Path or AttributeKind.Text ? $"\"${{{n++}}}\"" : $"${{{n++}}}")).ToList();
            string args = required.Count > 0 ? "(" + string.Join(", ", required) + ")" : string.Empty;
            return e.Content switch
            {
                ContentKind.Statements or ContentKind.Markup => $"{e.Name}{args} {{\n\t$0\n}}",
                _ when args.Length > 0 => e.Name + args,
                _ => null,
            };
        }

        /// <summary>The package elements (<c>KScript.Routes</c>, <c>KScript.Window</c>) that can be a file's root and import their package.</summary>
        private List<CompletionItem> PackageElementCompletions(TextSpan replace, bool xml, bool snippets)
        {
            List<CompletionItem> items = new List<CompletionItem>();
            foreach (ElementInfo e in Catalog.Elements.Where(e => e.Package != null && e.Package != "KScript.Core" && e.Context.HasFlag(ElementContext.Script) && (e.MustBeInside == null || e.MustBeInside.Count == 0)))
            {
                string snippet = null;
                if (snippets)
                {
                    snippet = xml ? "KScript." + ElementSnippet(e).Replace("</" + e.Name + ">", "</KScript." + e.Name + ">", StringComparison.Ordinal) : CodeElementSnippet(e);
                }
                items.Add(new CompletionItem
                {
                    Label = xml ? "KScript." + e.Name : e.Name,
                    Kind = CompletionKind.Element,
                    Detail = e.Package,
                    Documentation = ElementMarkdown(e, code: !xml),
                    InsertText = snippet,
                    IsSnippet = snippet != null,
                    ReplaceSpan = replace,
                    SortText = "0" + e.Name,
                });
            }
            return items;
        }

        /// <summary>What an <c>import a, b from "file"</c> can name: the file's functions, constants, types and enums.</summary>
        private List<CompletionItem> ImportNameCompletions(DocumentAnalysis analysis, string src, TextSpan replace, IEnumerable<string> already)
        {
            string full = string.IsNullOrWhiteSpace(src) || analysis.Path == null ? null : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(analysis.Path)), src.Trim()));
            string source = full != null && Files.FileExists(full) ? Files.ReadText(full) : null;
            if (source == null)
            {
                return new List<CompletionItem>();
            }
            HashSet<string> taken = new HashSet<string>(already.Select(n => n.Trim()), StringComparer.Ordinal);
            DocumentAnalysis imported = Analyze(source, full);
            return imported.Symbols
                .Where(s => (s.Kind is SymbolKind.Function or SymbolKind.Constant || s.EnumValues != null || s.Shape.Type == KType.Type) && !taken.Contains(s.Name))
                .GroupBy(s => s.Name).Select(g => g.First())
                .Select(s => new CompletionItem
                {
                    Label = s.Name,
                    Kind = s.Kind == SymbolKind.Function ? CompletionKind.Function : CompletionKind.Constant,
                    Detail = s.Kind == SymbolKind.Function ? s.TypedSignature : s.Shape.Display(),
                    Documentation = s.Description,
                    ReplaceSpan = replace,
                    SortText = "0" + s.Name,
                }).ToList();
        }

        private List<CompletionItem> PackageCompletions(DocumentAnalysis analysis, TextSpan replace) =>
            Catalog.Packages.Where(p => p.Key != "KScript.Core").Select(p => new CompletionItem
            {
                Label = p.Key,
                Kind = CompletionKind.Module,
                Detail = analysis.ImportedPackages.Contains(p.Key) ? "imported" : "package",
                Documentation = p.Value,
                ReplaceSpan = replace,
                // Packages the file doesn't import yet come first.
                SortText = (analysis.ImportedPackages.Contains(p.Key) ? "1" : "0") + p.Key,
            }).ToList();

        /// <summary>
        /// <c>x = ‸</c>, <c>echo ‸</c>, <c>[1, ‸]</c>, <c>$list |&gt; ‸</c>: an expression is due but nothing of it is
        /// written yet, so no expression region covers the cursor.
        /// </summary>
        private List<CompletionItem> ExpressionGapCompletions(DocumentAnalysis analysis, int wordStart, int offset, bool keywordsOnly)
        {
            string text = analysis.Source.Text;
            int p = wordStart;
            while (p > 0 && char.IsWhiteSpace(text[p - 1]))
            {
                p--;
            }
            TextSpan replace = TextSpan.FromBounds(wordStart, offset);
            if (!keywordsOnly && p >= 2 && text[p - 1] == '>' && text[p - 2] == '|' && analysis.Regions.FirstOrDefault(r => r.SourceSpan.End == p) is ExpressionRegion piped)
            {
                return MethodCompletions(analysis, piped, piped.Source.FromSource(p - 2), replace, pipe: true);
            }
            int w = p;
            while (w > 0 && Lexer.IsIdentChar(text[w - 1]))
            {
                w--;
            }
            bool afterKeyword = w < p && ExpressionKeywordsBefore.Contains(text.Substring(w, p - w));
            bool afterOperator = !keywordsOnly && p > 0 && "=+-*/%<>!?:,([&|".IndexOf(text[p - 1]) >= 0;
            return afterKeyword || afterOperator ? ExpressionStartCompletions(analysis, offset, replace, template: false) : null;
        }

        /// <summary>
        /// True for an item from a package this script doesn't import: completion leaves it out, since it wouldn't
        /// work here. Using it anyway is reported (KS MissingImport) with a quick fix that adds the import.
        /// </summary>
        private static bool NeedsImport(DocumentAnalysis analysis, string package) =>
            package != null && analysis.Kind == DocumentKind.Script && !analysis.ImportedPackages.Contains(package);
    }
}
