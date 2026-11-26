using KScript.Language.Analysis;
using KScript.Pages;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        private static bool PageAttributePosition(string text, int offset)
        {
            int start = offset;
            while (start > 0 && (Lexer.IsIdentChar(text[start - 1]) || text[start - 1] == '-')) start--;
            if (start > 0 && text[start - 1] == '@') start--;
            return start > 0 && char.IsWhiteSpace(text[start - 1]);
        }

        private void AddLiveAttributeCompletions(DocumentAnalysis analysis, MarkupElement element, bool component,
            int start, int offset, List<CompletionItem> items)
        {
            string tag = element.Name.ToLowerInvariant();
            if (!component && (PageElementNames.KScript.Contains(tag) || tag == "redirect")) return;
            bool live = !component && HasLiveOwner(analysis, element, offset);
            string prefix = analysis.Source.Text[start..offset];
            MarkupAttribute current = element.Attributes.FirstOrDefault(a => a.NameSpan.Start == start);
            int end = current?.NameSpan.End ?? offset;
            bool keepValue = current != null && current.Span.End > current.NameSpan.End;
            foreach (AttributeInfo attribute in LiveAttributes.All)
            {
                string name = attribute.Name;
                if (!FitsLiveElement(name, element, tag, component, live) || element.Attributes.Any(a => a != current && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                bool directive = prefix.StartsWith('@') || (prefix.Length == 0 && current == null);
                string label = directive ? "@" + name : name;
                string insert = name;
                var kind = LiveAttributes.ArgumentFor(name);
                if (directive)
                {
                    insert = kind switch
                    {
                        LiveDirectiveArgument.Flag => label + "()",
                        LiveDirectiveArgument.Statements => label + "($1)",
                        LiveDirectiveArgument.OptionalText => label + "()",
                        _ => label + "('$1')",
                    };
                    if (name == "live-use-") insert = "@live-use-${1:name}()";
                    if (name == "live-action") insert = label + "('${1:name}')";
                }
                else if (kind != LiveDirectiveArgument.Flag && name != "live-dirty")
                    insert = name == "live-use-" ? "live-use-${1:name}=\"$2\"" : name + "=\"$1\"";
                if (name == "live-action" && element.GetAttribute("on-action") == null)
                    insert += directive ? " @on-action(@${2:handler}(\\$event.data))" : " on-action=\"@${2:handler}(\\$event.data)\"";
                if (!label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new CompletionItem
                {
                    Label = label, Kind = CompletionKind.Attribute, Detail = "KScript live attribute",
                    Documentation = attribute.Description + OptionsLine(attribute),
                    InsertText = keepValue ? label : insert, IsSnippet = !keepValue && insert.Contains('$'),
                    ReplaceSpan = TextSpan.FromBounds(start, end), SortText = "0" + label,
                    SuggestAgain = !keepValue && (name == "bind" || attribute.Kind == AttributeKind.Enum),
                });
            }
        }

        private bool HasLiveOwner(DocumentAnalysis analysis, MarkupElement element, int offset) =>
            element.Ancestors().Any(a => a.Name.Equals("live", StringComparison.OrdinalIgnoreCase))
            || analysis.MarkupAt(offset) != analysis.Markup
            || analysis.Path != null && Path.GetDirectoryName(Path.GetFullPath(analysis.Path)) == ComponentFiles.Folder(analysis.Path, Files);

        private static bool FitsLiveElement(string name, MarkupElement element, string tag, bool component, bool live)
        {
            if (component) return name == "live-id";
            if (name == "live-navigate") return tag == "a";
            if (name == "live-navigate-once") return tag == "script";
            if (!live) return false;
            return name switch
            {
                "live-id" => false,
                "bind" => tag is "input" or "textarea" or "select" && !string.Equals(element.GetAttributeValue("type"), "file", StringComparison.OrdinalIgnoreCase),
                "live-upload" => tag == "input" && element.GetAttributeValue("type")?.ToLowerInvariant() == "file" && element.GetAttribute("name") != null,
                "live-portal" => tag == "template",
                "on-submit" => tag == "form",
                "on-cancel" or "on-close" => tag == "dialog",
                "every" => element.GetAttribute("on-tick") != null,
                "event-name" => element.GetAttribute("on-event") != null,
                "sort-axis" => element.GetAttribute("on-sort") != null,
                "live-disable" => tag is "button" or "fieldset" or "input" or "optgroup" or "option" or "select" or "textarea",
                _ => true,
            };
        }

        private List<CompletionItem> LiveAttributeValues(DocumentAnalysis analysis, Cursor cursor)
        {
            var attribute = cursor.Attribute;
            var info = LiveAttributes.Get(attribute.Name);
            if (info == null || PageElementNames.KScript.Contains(cursor.TagElement.Name) || ComponentFile(analysis.Path, cursor.TagElement.Name) != null)
                return null;
            if (info.Kind != AttributeKind.Enum && info.Name != "bind") return null;
            if (!FitsLiveElement(info.Name, cursor.TagElement, cursor.TagElement.Name.ToLowerInvariant(), false,
                HasLiveOwner(analysis, cursor.TagElement, cursor.Offset))) return new List<CompletionItem>();
            CompletionItem Choice(string label)
            {
                bool unquotedDirective = attribute.Quote == '\0' && analysis.Source.Text[attribute.NameSpan.Start] == '@';
                bool closed = analysis.Source.Text[attribute.Span.End - 1] == ')';
                return new CompletionItem
                {
                    Label = label, Kind = CompletionKind.Value, ReplaceSpan = attribute.Value.SourceSpan,
                    InsertText = unquotedDirective ? "'" + label + "'" + (closed ? "" : ")") : null,
                };
            }
            if (info.Kind == AttributeKind.Enum) return info.Options.Select(Choice).ToList();

            string path = analysis.Source.Text[attribute.Value.SourceSpan.Start..cursor.Offset].TrimStart('$');
            var states = ComponentBindings.Writable(analysis, analysis.ScopeAt(cursor.Offset), cursor.TagElement, cursor.Offset);
            int dot = path.LastIndexOf('.');
            IEnumerable<string> names;
            if (dot < 0) names = states.Select(s => s.Name);
            else
            {
                string[] segments = path[..dot].Split('.');
                TypeShape shape = states.FirstOrDefault(s => s.Name == segments[0])?.Shape;
                foreach (string segment in segments.Skip(1))
                    shape = shape != null && shape.Fields.TryGetValue(segment, out FieldInfo field) ? field.Shape : null;
                names = shape?.Fields.Keys.Select(key => path[..(dot + 1)] + key) ?? Enumerable.Empty<string>();
            }
            // @bind names its field with $: complete the path only, so the options after it stay.
            int pathStart = attribute.Value.SourceSpan.Start, pathEnd = pathStart;
            string text = analysis.Source.Text;
            while (pathEnd < attribute.Value.SourceSpan.End && (char.IsLetterOrDigit(text[pathEnd]) || text[pathEnd] is '_' or '.' or '$' or '-'))
                pathEnd++;
            bool bindClosed = text[attribute.Span.End - 1] == ')' || attribute.Quote != '\0' || text[attribute.NameSpan.Start] != '@';
            return names.Where(n => n.StartsWith(path, StringComparison.Ordinal)).Select(n => new CompletionItem
            {
                Label = n, Kind = CompletionKind.Value, ReplaceSpan = TextSpan.FromBounds(pathStart, pathEnd),
                InsertText = "$" + n + (bindClosed ? "" : ")"),
            }).ToList();
        }
    }
}
