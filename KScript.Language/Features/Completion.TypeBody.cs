using System.Text.RegularExpressions;
using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Metadata;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    /// <summary>
    /// Completion and hover inside the block of a .kscript <c>type</c> or <c>model</c>, where a line is a field, a rule
    /// or (in a model) a setting: <c>hidden: [password]</c>, <c>min: 0</c>, <c>validate: ...</c>. Also the rules a type
    /// takes where it is used: <c>title: string(min: 1, max: 200)</c>.
    /// </summary>
    public sealed partial class LanguageService
    {
        /// <summary>Rule lines a type's block can start with, by whether the type is made from one other type (<c>with [int]</c>).</summary>
        private static readonly string[] RefinementRuleLines = { "min", "max", "pattern", "one_of", "not", "validate", "normalize" };
        private static readonly string[] RecordRuleLines = { "pattern", "parse", "validate" };

        private static readonly Regex BodyWordLine = new Regex(@"^[ \t]*(\w*)$", RestrictOptions);
        private static readonly Regex BodySettingLine = new Regex(@"^[ \t]*(\w+)[ \t]*:[ \t]*(.*)$", RestrictOptions);

        private const RegexOptions RestrictOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;

        /// <summary>The type or model whose block holds this offset directly (not inside one of its fns). Callers check the file is .kscript.</summary>
        private static MarkupElement BlockOwnerAt(DocumentAnalysis analysis, int offset)
        {
            MarkupElement e = analysis.Markup.FindElementAt(offset);
            if (e?.Name is "field" or "validate" or "normalize")
            {
                e = e.Parent;
            }
            return e?.Name is "type" or "model" && offset > e.ContentSpan.Start && offset <= e.ContentSpan.End && offset >= e.StartTagSpan.End ? e : null;
        }

        /// <summary>Completions for a line of a type or model block, or null when the offset isn't one this handles.</summary>
        private List<CompletionItem> TypeBodyCompletions(DocumentAnalysis analysis, int offset)
        {
            string text = analysis.Source.Text;
            int lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
            string before = text.Substring(lineStart, offset - lineStart);

            if (TypeArgumentAt(analysis, offset) is (string typeName, TextSpan replace, HashSet<string> written))
            {
                return TypeArgumentCompletions(typeName, replace, written);
            }

            MarkupElement owner = BlockOwnerAt(analysis, offset);
            if (owner == null)
            {
                return null;
            }

            if (BodyWordLine.Match(before) is { Success: true } word)
            {
                TextSpan span = TextSpan.FromBounds(offset - word.Groups[1].Length, offset);
                return owner.Name == "model" ? ModelLineCompletions(analysis, owner, span)
                    : TypeLineCompletions(owner, span, analysis.DeclaredTypes.GetValueOrDefault(owner)?.RefinementBase != null);
            }

            return null;
        }

        /// <summary>
        /// A new line in a model: validate and fn. Its options (table, timestamps, unique...) go in the brackets,
        /// model User(timestamps) { }, so they aren't offered as lines.
        /// </summary>
        private List<CompletionItem> ModelLineCompletions(DocumentAnalysis analysis, MarkupElement model, TextSpan replace)
        {
            List<CompletionItem> items = new List<CompletionItem>();
            items.Add(BodyRuleItem("validate", replace, "1"));
            items.Add(BodyFnItem(replace));
            return items;
        }

        /// <summary>Complete a model option at the caret, in the header or on a legacy setting line in the body.</summary>
        private List<CompletionItem> ModelOptionCompletionsAt(DocumentAnalysis analysis, int offset)
        {
            if (analysis.Markup.Syntax != SourceSyntax.Code || analysis.Markup.FindElementAt(offset) is not { Name: "model" } model
                || model.GetAttribute("name")?.Span.End is not int nameEnd || offset <= nameEnd)
            {
                return null;
            }
            string text = analysis.Source.Text;
            if (offset > model.StartTagSpan.End)
            {
                int lineStart = text.LastIndexOf('\n', offset - 1) + 1;
                Match setting = BodySettingLine.Match(text[lineStart..offset]);
                if (!setting.Success || !CodeParser.ModelSettings.Contains(setting.Groups[1].Value)
                    || model.Children.OfType<MarkupComment>().Any(c => offset > c.Span.Start && offset <= c.Span.End))
                    return null;
                return ModelSettingValueCompletions(analysis, model, setting.Groups[1].Value, offset - setting.Groups[2].Length, offset);
            }
            int open = nameEnd;
            while (open < offset && char.IsWhiteSpace(text[open])) open++;
            if (open >= offset || text[open] != '(' || InComment(text, offset)) return null;

            foreach (MarkupAttribute option in model.Attributes)
            {
                if (!option.IsArgument || offset <= option.NameSpan.End || offset > option.Span.End) continue;
                int start = option.NameSpan.End;
                while (start < offset && char.IsWhiteSpace(text[start])) start++;
                if (start < offset && text[start] is ':' or '=')
                {
                    return ModelSettingValueCompletions(analysis, model, option.Name, start + 1, offset);
                }
            }
            int word = WordStart(text, offset);
            int before = word;
            while (before > open && char.IsWhiteSpace(text[before - 1])) before--;
            if (before <= open || text[before - 1] is not ('(' or ',')) return null;
            int end = offset;
            while (end < text.Length && Lexer.IsIdentChar(text[end])) end++;
            TextSpan replace = TextSpan.FromBounds(word, end);
            HashSet<string> have = model.Attributes.Where(a => a.NameSpan.Start != word).Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
            return ModelOptionCompletions(have, replace);
        }

        /// <summary>The options a model takes in its brackets and doesn't have yet.</summary>
        private List<CompletionItem> ModelOptionCompletions(HashSet<string> have, TextSpan replace)
        {
            ElementInfo info = Catalog.GetElement("model");
            List<CompletionItem> items = new List<CompletionItem>();
            foreach (string name in CodeParser.ModelSettings.Where(n => !have.Contains(n)))
            {
                AttributeInfo a = info?.GetAttribute(name);
                items.Add(new CompletionItem
                {
                    Label = name,
                    Kind = CompletionKind.Attribute,
                    Detail = "model option",
                    Documentation = a?.Description,
                    // A flag is on by being named: model User(timestamps).
                    InsertText = a?.Kind == AttributeKind.Bool ? name : a?.ModelFields is ModelFieldKind.List or ModelFieldKind.Groups ? name + ": [$1]" : name + ": \"$1\"",
                    IsSnippet = a?.Kind != AttributeKind.Bool,
                    SuggestAgain = a?.ModelFields is ModelFieldKind.Single or ModelFieldKind.List or ModelFieldKind.Groups,
                    ReplaceSpan = replace,
                    SortText = "0" + name,
                });
            }
            return items;
        }

        /// <summary>A new line in a type: the rules it can have and a method declaration.</summary>
        private List<CompletionItem> TypeLineCompletions(MarkupElement type, TextSpan replace, bool refined)
        {
            List<CompletionItem> items = new List<CompletionItem>();
            foreach (string rule in refined ? RefinementRuleLines : RecordRuleLines)
            {
                MarkupAttribute set = type.GetAttribute(rule);
                if (set != null && set.NameSpan.Start != replace.Start)
                {
                    continue;
                }
                items.Add(BodyRuleItem(rule, replace, "0"));
            }
            items.Add(BodyFnItem(replace));
            if (!refined && TypeBody.Constructor(type) == null)
            {
                items.Add(new CompletionItem
                {
                    Label = CodeParser.ConstructWord,
                    Kind = CompletionKind.Keyword,
                    Detail = "constructor",
                    Documentation = Catalog.GetElement(CodeParser.ConstructWord)?.Description,
                    InsertText = "construct({\n  ${1:name}: ${2:string},\n}) {\n  $0\n}",
                    IsSnippet = true,
                    ReplaceSpan = replace,
                    SortText = "1construct",
                });
            }
            if (ComponentScript.IsComponentType(type))
            {
                // A component keeps the types only it uses inside its own block.
                foreach (string word in ComponentScript.NestedDeclarations)
                {
                    items.Add(new CompletionItem
                    {
                        Label = word,
                        Kind = CompletionKind.Keyword,
                        Detail = $"a {word} only this component uses",
                        Documentation = Catalog.GetElement(word)?.Description,
                        InsertText = word switch
                        {
                            "enum" => "enum ${1:Name} { ${2:first}, ${3:second} }",
                            "error" => "error ${1:Name}(message: \"${2:What went wrong}\")",
                            _ => word + " ${1:Name} {\n  ${2:name}: ${3:string}\n}",
                        },
                        IsSnippet = true,
                        ReplaceSpan = replace,
                        SortText = "2" + word,
                    });
                }
            }
            return items;
        }

        private CompletionItem BodyRuleItem(string rule, TextSpan replace, string sortGroup)
        {
            string doc = rule is "validate" or "normalize" ? Catalog.GetElement(rule)?.Description : Catalog.GetElement("type")?.GetAttribute(rule)?.Description;
            string snippet = rule switch
            {
                "validate" => "validate: ${1:\\$value > 0}, '${2:message}'",
                "normalize" => "normalize: ${1:\\$value->trim()}",
                "pattern" => "pattern: /${1:^[a-z]+\\$}/",
                "one_of" or "not" => rule + ": [$1]",
                _ => rule + ": $1",
            };
            return new CompletionItem
            {
                Label = rule,
                Kind = CompletionKind.Keyword,
                Detail = "rule",
                Documentation = doc,
                InsertText = snippet,
                IsSnippet = true,
                ReplaceSpan = replace,
                SortText = sortGroup + rule,
            };
        }

        private CompletionItem BodyFnItem(TextSpan replace) => new CompletionItem
        {
            Label = "fn",
            Kind = CompletionKind.Keyword,
            Detail = "method",
            Documentation = Catalog.GetElement("fn")?.Description,
            InsertText = CodeStatementSnippets["fn"],
            IsSnippet = true,
            ReplaceSpan = replace,
            SortText = "2fn",
        };

        /// <summary>After <c>timestamps: </c>, true and false; after <c>hidden: [</c> and the other field lists, the model's fields.</summary>
        private List<CompletionItem> ModelSettingValueCompletions(DocumentAnalysis analysis, MarkupElement model, string setting, int start, int offset)
        {
            string text = analysis.Source.Text;
            while (start < offset && char.IsWhiteSpace(text[start])) start++;
            int wordStart = Math.Max(start, WordStart(text, offset));
            int wordEnd = offset;
            while (wordEnd < text.Length && Lexer.IsIdentChar(text[wordEnd])) wordEnd++;
            TextSpan replace = TextSpan.FromBounds(wordStart, wordEnd);
            AttributeInfo option = Catalog.GetElement("model")?.GetAttribute(setting);
            if (option?.Kind == AttributeKind.Bool)
            {
                return wordStart == start ? new[] { "true", "false" }.Select(v => new CompletionItem
                {
                    Label = v, Kind = CompletionKind.Value, Detail = setting, ReplaceSpan = replace, SortText = v == "true" ? "0" : "1",
                }).ToList() : new List<CompletionItem>();
            }
            if (option == null || option.ModelFields == ModelFieldKind.None) return new List<CompletionItem>();

            int beforeWord = wordStart;
            if (beforeWord > start && text[beforeWord - 1] is '\'' or '"') beforeWord--;
            while (beforeWord > start && char.IsWhiteSpace(text[beforeWord - 1])) beforeWord--;
            bool single = option.ModelFields == ModelFieldKind.Single;
            bool startList = !single && wordStart == start;
            if (startList)
            {
                // Before the opening bracket, accepting a field should create a valid list.
                int next = wordEnd;
                while (next < text.Length && text[next] is ' ' or '\t') next++;
                if (text.AsSpan(next).StartsWith("[]", StringComparison.Ordinal))
                    replace = TextSpan.FromBounds(wordStart, next + 2);
                else if (model.GetAttribute(setting)?.Value?.SourceSpan is { Length: > 0 } value && value.Start >= offset)
                    return new List<CompletionItem>();
                else if (next < text.Length && text[next] is not (',' or ')' or '}' or ';' or '\r' or '\n'))
                    return new List<CompletionItem>();
            }
            else if (single ? beforeWord != start : beforeWord <= start || text[beforeWord - 1] is not ('[' or ','))
                return new List<CompletionItem>();

            // Composite indexes may reuse a column in another group. Exclude only siblings of the current item.
            HashSet<string> listed = new(StringComparer.Ordinal);
            if (!single && !startList)
            {
                int depth = 0;
                for (int i = start; i < offset; i++)
                {
                    if (text[i] == '[') depth++;
                    else if (text[i] == ']') depth--;
                }
                if (depth < 1 || depth > (option.ModelFields == ModelFieldKind.Groups ? 2 : 1)) return new List<CompletionItem>();
                MarkupAttribute attr = model.GetAttribute(setting);
                int limit = attr?.Span.End ?? offset;
                int groupStart = depth == 1 ? start : text.LastIndexOf('[', beforeWord - 1);
                int level = depth - 1;
                for (int i = groupStart; i < limit;)
                {
                    char ch = text[i++];
                    if (ch == '[') level++;
                    else if (ch == ']' && --level < depth) break;
                    else if (level == depth && Lexer.IsIdentStart(ch))
                    {
                        int first = i - 1;
                        while (i < limit && Lexer.IsIdentChar(text[i])) i++;
                        if (first != wordStart || i != wordEnd) listed.Add(text[first..i]);
                    }
                }
            }
            return analysis.ModelOptionFields(model, setting).Where(f => !listed.Contains(f.Name)).Select((f, i) => new CompletionItem
            {
                Label = f.Name, Kind = CompletionKind.Field, Detail = f.DeclaredType ?? f.Shape.Display(), Documentation = f.Description,
                InsertText = startList ? "[" + f.Name + "]" : null,
                ReplaceSpan = replace, SortText = i.ToString("D3"),
            }).ToList();
        }

        /// <summary>
        /// The argument name being typed in a type used with rules, <c>string(m‸</c> or <c>uuid(min: 1, ‸</c>: the type,
        /// the name typed so far, and the names already given. Null elsewhere.
        /// </summary>
        private (string Type, TextSpan Replace, HashSet<string> Written)? TypeArgumentAt(DocumentAnalysis analysis, int offset)
        {
            string text = analysis.Source.Text;
            int wordStart = WordStart(text, offset);
            int p = wordStart;
            while (p > 0 && text[p - 1] is ' ' or '\t')
            {
                p--;
            }
            if (p == 0 || text[p - 1] is not ('(' or ','))
            {
                return null;
            }
            // Back to the '(' this argument list opened with, on the same line.
            int depth = 0;
            int open = -1;
            for (int i = p - 1; i >= 0 && text[i] != '\n'; i--)
            {
                char c = text[i];
                if (c is ')' or ']' or '}')
                {
                    depth++;
                }
                else if (c is '(' or '[' or '{')
                {
                    if (depth == 0)
                    {
                        open = c == '(' ? i : -1;
                        break;
                    }
                    depth--;
                }
                else if (c is '\'' or '"')
                {
                    return null;
                }
            }
            if (open <= 0)
            {
                return null;
            }
            int nameEnd = open;
            int nameStart = nameEnd;
            while (nameStart > 0 && Lexer.IsIdentChar(text[nameStart - 1]))
            {
                nameStart--;
            }
            string type = text.Substring(nameStart, nameEnd - nameStart);
            int q = nameStart;
            while (q > 0 && text[q - 1] is ' ' or '\t')
            {
                q--;
            }
            // A type is written after ':' (a field, a parameter) or in a with [...] list; a call never is.
            if (type.Length == 0 || q == 0 || text[q - 1] is not (':' or '[' or ',') || !IsRuleType(analysis, type))
            {
                return null;
            }
            string args = text.Substring(open + 1, wordStart - open - 1);
            HashSet<string> written = new HashSet<string>(Regex.Matches(args, @"(\w+)[ \t]*:").Select(m => m.Groups[1].Value), StringComparer.Ordinal);
            return (type, TextSpan.FromBounds(wordStart, offset), written);
        }

        private bool IsRuleType(DocumentAnalysis analysis, string type) =>
            (Catalog.GetElement("field")?.GetAttribute("type")?.Options ?? Array.Empty<string>()).Contains(type)
            || NativeTypes.IsNative(type)
            || TypeNames(analysis).Contains(type);

        /// <summary>The rules every type takes (min, max...), then the options of a native type (a uuid's v).</summary>
        private List<CompletionItem> TypeArgumentCompletions(string type, TextSpan replace, HashSet<string> written)
        {
            ElementInfo field = Catalog.GetElement("field");
            List<CompletionItem> items = KScript.Runtime.TypeRules.Attributes.Where(r => !written.Contains(r)).Select(r => new CompletionItem
            {
                Label = r,
                Kind = CompletionKind.Parameter,
                Detail = "rule",
                Documentation = field?.GetAttribute(r)?.Description,
                InsertText = r + ": ",
                FilterText = r,
                ReplaceSpan = replace,
                SortText = "0" + r,
            }).ToList();
            foreach (string option in NativeTypes.Get(type)?.Options ?? Array.Empty<string>())
            {
                if (!written.Contains(option))
                {
                    items.Add(new CompletionItem
                    {
                        Label = option,
                        Kind = CompletionKind.Parameter,
                        Detail = type + " option",
                        InsertText = option + ": ",
                        FilterText = option,
                        ReplaceSpan = replace,
                        SortText = "1" + option,
                    });
                }
            }
            return items;
        }

        /// <summary>Hover for a setting or rule line in a type or model block (<c>hidden:</c>, <c>min:</c>) and a rule given to a type (<c>string(min: 1)</c>).</summary>
        private HoverResult TypeBodyHover(DocumentAnalysis analysis, int offset)
        {
            if (analysis.Markup.Syntax != SourceSyntax.Code)
            {
                return null;
            }
            MarkupElement owner = BlockOwnerAt(analysis, offset);
            MarkupAttribute line = owner?.Attributes.FirstOrDefault(a => a.NameSpan.Length > 0 && offset >= a.NameSpan.Start && offset <= a.NameSpan.End && a.NameSpan.Start >= owner.StartTagSpan.End);
            if (line != null && Catalog.GetElement(owner.Name)?.GetAttribute(line.Name) is AttributeInfo a)
            {
                string what = owner.Name == "model" && CodeParser.ModelSettings.Contains(line.Name) ? "model setting" : "rule";
                return new HoverResult($"**{a.Name}** ({what})\n\n{a.Description}{OptionsLine(a)}", line.NameSpan);
            }

            string text = analysis.Source.Text;
            int start = WordStart(text, offset);
            int end = offset;
            while (end < text.Length && Lexer.IsIdentChar(text[end]))
            {
                end++;
            }
            int colon = end;
            while (colon < text.Length && text[colon] is ' ' or '\t')
            {
                colon++;
            }
            if (start == end || colon >= text.Length || text[colon] != ':' || TypeArgumentAt(analysis, start) is not (string type, _, _))
            {
                return null;
            }
            string name = text.Substring(start, end - start);
            TextSpan span = TextSpan.FromBounds(start, end);
            if (KScript.Runtime.TypeRules.Attributes.Contains(name))
            {
                return new HoverResult($"**{name}** (rule)\n\n{Catalog.GetElement("field")?.GetAttribute(name)?.Description}", span);
            }
            return NativeTypes.Get(type)?.Options.Contains(name) == true ? new HoverResult($"**{name}** ({type} option)", span) : null;
        }
    }
}
