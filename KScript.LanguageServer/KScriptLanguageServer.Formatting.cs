using System.Text.Json.Nodes;

using KScript.Language.Formatting;
using KScript.Language.Rules;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.LanguageServer
{
    public sealed partial class KScriptLanguageServer
    {
        /// <summary>
        /// textDocument/formatting: Format Document and format-on-save. The editor's tab settings are the starting
        /// point and the project's kscript.json "format" section wins, so the result matches <c>kscript fmt</c>.
        /// A document with markup errors (or a Markdown page) gets no edits.
        /// </summary>
        private JsonNode Formatting(JsonNode p, bool includeFences = false)
        {
            OpenDocument doc = Document(p);
            if (doc == null)
            {
                return null;
            }

            JsonNode o = p?["options"];
            int? tabSize = o?["tabSize"] is JsonValue t && t.TryGetValue(out int size) ? size : null;
            bool? insertSpaces = o?["insertSpaces"] is JsonValue s && s.TryGetValue(out bool spaces) ? spaces : null;
            KScript.Projects.ProjectConfig project = ProjectFor(doc.Path);
            if (project != null && (!project.Format.Enabled || project.IsFormatExcluded(doc.Path)))
            {
                return includeFences ? null : new JsonArray();
            }
            DocumentRules rules = DocumentRules.ForProject(project?.Directory, workspace);
            FormatOptions options = FormatOptions.From(project, tabSize, insertSpaces, rules);

            // .kscript files (or an unsaved document in the KScript (code) language) have their own formatter.
            bool code = MarkupParser.IsCodePath(doc.Path) || (doc.Path == null && doc.LanguageId == "kscript-code");
            FormatResult result = Formatter.FormatDocument(doc.Text, doc.Path, doc.Kind, code, options, rules);
            if (!result.Succeeded)
            {
                if (doc.Kind != DocumentKind.Markdown)
                {
                    Log($"{doc.Path ?? doc.Uri}: {result.Error}");
                }
                return includeFences ? null : new JsonArray();
            }

            if (includeFences)
            {
                var fences = new JsonArray();
                if (code)
                {
                    foreach (StringBlock block in CodeFormatter.EmbeddableFences(result.Text))
                    {
                        int marginEnd = block.ContentEnd;
                        while (result.Text[marginEnd] is ' ' or '\t') marginEnd++;
                        int openingEnd = block.Start;
                        while (result.Text[openingEnd] == '`') openingEnd++;
                        fences.Add((JsonNode)new JsonObject
                        {
                            ["language"] = block.Language ?? "json",
                            ["text"] = block.Value,
                            ["start"] = block.ContentStart,
                            ["end"] = block.ContentEnd,
                            ["indent"] = result.Text[block.ContentEnd..marginEnd],
                            ["length"] = openingEnd - block.Start,
                        });
                    }
                }
                return new JsonObject { ["text"] = result.Text, ["version"] = doc.Version, ["fences"] = fences };
            }

            TextChange change = MinimalChange(doc.Text, result.Text);
            return change == null ? new JsonArray() : Edits(new SourceText(doc.Text), new[] { change });
        }

        /// <summary>One edit covering only what changed, so the editor keeps the cursor and folds where they were.</summary>
        internal static TextChange MinimalChange(string before, string after)
        {
            if (before == after)
            {
                return null;
            }
            int prefix = 0;
            int max = Math.Min(before.Length, after.Length);
            while (prefix < max && before[prefix] == after[prefix]) prefix++;
            // Don't split a CRLF pair or a surrogate pair.
            while (prefix > 0 && (char.IsHighSurrogate(before[prefix - 1]) || (before[prefix - 1] == '\r' && prefix < before.Length && before[prefix] == '\n'))) prefix--;

            int suffix = 0;
            while (suffix < max - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;
            while (suffix > 0 && (char.IsLowSurrogate(before[before.Length - suffix]) || (before[before.Length - suffix] == '\n' && before.Length - suffix > 0 && before[before.Length - suffix - 1] == '\r'))) suffix--;

            return new TextChange(TextSpan.FromBounds(prefix, before.Length - suffix), after.Substring(prefix, after.Length - suffix - prefix));
        }
    }
}
