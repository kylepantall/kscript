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

        /// <summary>Model settings whose value is a list of the model's fields.</summary>
        private static readonly HashSet<string> FieldListSettings = new HashSet<string> { "unique", "index", "hidden", "guarded", "key" };

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
                return owner.Name == "model" ? ModelLineCompletions(analysis, owner, span) : TypeLineCompletions(owner, span);
            }

            if (owner.Name == "model" && BodySettingLine.Match(before) is { Success: true } setting && CodeParser.ModelSettings.Contains(setting.Groups[1].Value))
            {
                return ModelSettingValueCompletions(owner, setting.Groups[1].Value, setting.Groups[2].Value, offset);
            }
            return null;
        }

        /// <summary>A new line in a model: the settings it doesn't have yet, then validate and fn.</summary>
        private List<CompletionItem> ModelLineCompletions(DocumentAnalysis analysis, MarkupElement model, TextSpan replace)
        {
            ElementInfo info = Catalog.GetElement("model");
            List<CompletionItem> items = new List<CompletionItem>();
            foreach (string name in CodeParser.ModelSettings)
            {
                MarkupAttribute set = model.GetAttribute(name);
                if (set != null && set.NameSpan.Start != replace.Start)
                {
                    continue;
                }
                AttributeInfo a = info?.GetAttribute(name);
                items.Add(new CompletionItem
                {
                    Label = name,
                    Kind = CompletionKind.Attribute,
                    Detail = "model setting",
                    Documentation = a?.Description,
                    InsertText = name + SettingSnippet(name, a),
                    IsSnippet = true,
                    ReplaceSpan = replace,
                    SortText = "0" + name,
                    SuggestAgain = FieldListSettings.Contains(name),
                });
            }
            items.Add(BodyRuleItem("validate", replace, "1"));
            items.Add(BodyFnItem(replace));
            return items;
        }

        /// <summary>A new line in a type: the rules it can have and doesn't yet, then fn.</summary>
        private List<CompletionItem> TypeLineCompletions(MarkupElement type, TextSpan replace)
        {
            List<CompletionItem> items = new List<CompletionItem>();
            foreach (string rule in type.GetAttribute("with") != null ? RefinementRuleLines : RecordRuleLines)
            {
                MarkupAttribute set = type.GetAttribute(rule);
                if (set != null && set.NameSpan.Start != replace.Start)
                {
                    continue;
                }
                items.Add(BodyRuleItem(rule, replace, "0"));
            }
            items.Add(BodyFnItem(replace));
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

        /// <summary>What a setting's snippet puts after its name: yes/no, a list of fields, or a value to type.</summary>
        private static string SettingSnippet(string name, AttributeInfo a)
        {
            if (a?.Kind == AttributeKind.Bool)
            {
                return ": ${1|yes,no|}";
            }
            return FieldListSettings.Contains(name) && name != "key" ? ": [$1]" : ": $1";
        }

        /// <summary>After <c>timestamps: </c>, yes and no; after <c>hidden: [</c> and the other field lists, the model's fields.</summary>
        private List<CompletionItem> ModelSettingValueCompletions(MarkupElement model, string setting, string valueBefore, int offset)
        {
            int wordLength = 0;
            while (wordLength < valueBefore.Length && Lexer.IsIdentChar(valueBefore[valueBefore.Length - 1 - wordLength]))
            {
                wordLength++;
            }
            TextSpan replace = TextSpan.FromBounds(offset - wordLength, offset);
            AttributeInfo a = Catalog.GetElement("model")?.GetAttribute(setting);
            if (a?.Kind == AttributeKind.Bool)
            {
                return wordLength == valueBefore.Length
                    ? new[] { "yes", "no" }.Select(v => new CompletionItem { Label = v, Kind = CompletionKind.Value, Detail = setting, ReplaceSpan = replace, SortText = v == "yes" ? "0" : "1" }).ToList()
                    : new List<CompletionItem>();
            }
            if (!FieldListSettings.Contains(setting))
            {
                return new List<CompletionItem>();
            }
            string prefix = valueBefore.Substring(0, valueBefore.Length - wordLength).TrimEnd();
            bool namePosition = prefix.Length == 0 || prefix.EndsWith('[') || prefix.EndsWith(',');
            if (!namePosition)
            {
                return new List<CompletionItem>();
            }
            HashSet<string> listed = new HashSet<string>(Regex.Matches(prefix, @"\w+").Select(m => m.Value), StringComparer.Ordinal);
            return ModelFieldNames(model).Where(f => !listed.Contains(f.Name)).Select((f, i) => new CompletionItem
            {
                Label = f.Name,
                Kind = CompletionKind.Field,
                Detail = f.Type,
                Documentation = f.Description,
                ReplaceSpan = replace,
                SortText = i.ToString("D3"),
            }).ToList();
        }

        /// <summary>The fields a model's records have: its own lines, then id, and the columns its settings add.</summary>
        private static List<(string Name, string Type, string Description)> ModelFieldNames(MarkupElement model)
        {
            List<(string Name, string Type, string Description)> fields = model.ChildElements
                .Where(e => e.Name == "field" && !string.IsNullOrEmpty(e.GetAttributeValue("name")))
                .Select(e => (e.GetAttributeValue("name").TrimEnd('?'), e.GetAttributeValue("type") ?? "any", e.GetAttributeValue("description")))
                .ToList();
            void Add(string name, string type, string description)
            {
                if (fields.All(f => f.Name != name))
                {
                    fields.Add((name, type, description));
                }
            }
            Add("id", "int", "The key the database numbers, added when the model doesn't declare it.");
            if (IsYesText(model.GetAttributeValue("timestamps")))
            {
                Add("created_at", "date", "When the record was first saved (timestamps: yes).");
                Add("updated_at", "date", "When the record was last saved (timestamps: yes).");
            }
            if (IsYesText(model.GetAttributeValue("soft_deletes")))
            {
                Add("deleted_at", "date?", "When the record was deleted (soft_deletes: yes).");
            }
            return fields;
        }

        private static bool IsYesText(string value) => value?.Trim() is "yes" or "true";

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
