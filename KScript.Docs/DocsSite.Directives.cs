using System.Text;

using KScript.Metadata;
using KScript.Pages;
using KScript.Syntax.Markup;

namespace KScript.Docs
{
    /// <summary>
    /// The page directives page: every @ directive a tag takes in a live page or component (@on-click, @bind,
    /// @live-cloak...), every handler shorthand and modifier, every @bind option, every field of $event and the
    /// spellings that were retired. It is read from the lists the checker and the editor use, so a new directive,
    /// shorthand or event field shows up here, in the A to Z and in search without anyone writing it.
    /// </summary>
    public sealed partial class DocsSite
    {
        internal const string DirectivesPath = "/reference/directives";

        /// <summary>How a directive is written in a tag: <c>@live-use-</c> takes the client directive's name.</summary>
        internal static string DirectiveName(AttributeInfo a) => a.Name switch
        {
            "live-use-" => "@live-use-name",
            ClassDirective.Attribute => "@class",
            _ => "@" + a.Name,
        };

        internal static string DirectiveAnchor(AttributeInfo a) => "directive-" + Html.Slug(a.Name.TrimEnd('-'));

        internal static IEnumerable<AttributeInfo> Handlers => LiveAttributes.All.Where(a => a.Name.StartsWith("on-", StringComparison.Ordinal)).OrderBy(a => a.Name, StringComparer.Ordinal);

        internal static IEnumerable<AttributeInfo> OtherDirectives => LiveAttributes.All.Where(a => !a.Name.StartsWith("on-", StringComparison.Ordinal)).OrderBy(a => a.Name, StringComparer.Ordinal);

        private void AddDirectivesPage()
        {
            const string title = "Page directives and handlers";
            var sb = new StringBuilder();
            var md = new StringBuilder($"# {title}\n\nEvery @ directive a tag takes in a live page or component, every handler shorthand and modifier, every @bind option and every field of $event.\n");
            sb.Append($"<h1>{title}</h1><p class=\"lead\">Every <code>@</code> directive a tag takes in a live page or component, such as <code>@on-click</code>, <code>@bind</code> and <code>@live-cloak</code>, with the shorthands and modifiers a handler takes and the fields of <code>$event</code>. How live pages work is in <a href=\"/guide/components\">Components</a>.</p>");
            var page = new DocPage { Path = DirectivesPath, Title = title, NavTitle = "Page directives", Section = "Reference", Description = "Every @ directive for live pages and components (@on-click, @bind, @live-cloak, ...), handler shorthands, modifiers and $event fields." };

            const string example = "<input @bind($query, debounce: 300ms) @on-keydown(reset: $query, key: escape)>\n<button @on-click(delete: $note, confirm: 'Delete this note?', emit: 'notes-changed')>Delete</button>\n<p @live-loading>Saving...</p>";
            sb.Append("<p>A directive goes on the opening tag. It takes what is in its brackets: statements for a handler, a field and options for <code>@bind</code>, or nothing for a flag such as <code>@live-loading</code>.</p>");
            sb.Append(Html.Code(example, "html"));
            md.Append($"\n```html\n{example}\n```\n");

            Section(sb, md, page, "handlers", "Handlers", "A handler runs KScript on the server when its event happens in the browser. It holds statements, a function to call, or the shorthands below.",
                "Handler", "What it does", Handlers.Select(a => (DirectiveAnchor(a), DirectiveName(a), a.Description)), "handler");

            Section(sb, md, page, "directives", "Other directives", "Directives that bind controls, show and hide tags, give feedback while an action runs, and hand parts of the page to the browser.",
                "Directive", "What it does", OtherDirectives.Select(a => (DirectiveAnchor(a), DirectiveName(a), a.Description)), "directive");

            Section(sb, md, page, "shorthands", "Handler shorthands", "A handler can name what it does instead of spelling out the statement: <code>@on-click(toggle: $open)</code> is <code>@on-click(open = not $open)</code>. Several run left to right in one handler.",
                "Shorthand", "What it does", HandlerShorthands.Actions.Select(a => ("shorthand-" + Html.Slug(a.Name), a.Companion == null ? a.Name + ":" : $"{a.Name}:, {a.Companion}:", a.Description)), "handler shorthand");

            Section(sb, md, page, "modifiers", "Handler modifiers", "Modifiers go with a handler's shorthands and change when or how they run. The browser applies them before it sends anything: <code>@on-keydown(save: $note, key: [ctrl, s], prevent: true)</code>.",
                "Modifier", "What it does", HandlerShorthands.Modifiers.Select(m => ("modifier-" + Html.Slug(m.Name), m.Name + ":", m.Description)), "handler modifier");

            Section(sb, md, page, "bind", "@bind options", "Options go after the field: <code>@bind($title, on: blur, trim: true)</code>.",
                "Option", "What it does", BindDirective.Options.Select(o => ("bind-" + Html.Slug(o.Name), o.Name + ":", o.Description)), "option of @bind");

            Section(sb, md, page, "event", "$event", "Inside a handler, <code>$event</code> is the browser event it runs for. Every handler gets these fields.",
                "Field", "What it holds", EventFields.Common.Select(f => ("event-" + Html.Slug(f.Name), $"$event.{f.Name}", $"`{f.Type}`. {f.Description}")), null);
            foreach (string handler in EventFields.HandlersWithExtraFields.OrderBy(h => h, StringComparer.Ordinal))
            {
                var own = EventFields.ForPage(handler).Where(f => !EventFields.Common.Contains(f)).ToList();
                sb.Append($"<p>In <code>@{Html.H(handler)}</code>, <code>$event</code> also has:</p>");
                md.Append($"\nIn `@{handler}`, `$event` also has:\n");
                Table(sb, md, "Field", "What it holds", own.Select(f => ($"event-{Html.Slug(handler)}-{Html.Slug(f.Name)}", $"$event.{f.Name}", $"`{f.Type}`. {f.Description}")), null);
            }

            sb.Append("<h2 id=\"retired\">Retired spellings</h2><p>These were replaced by a shorthand or an option. They are errors now, with a quick fix in the editor.</p>");
            page.Toc.Add((2, "retired", "Retired spellings"));
            md.Append("\n## Retired spellings\n\nThese were replaced by a shorthand or an option. They are errors now, with a quick fix in the editor.\n");
            Table(sb, md, "Was", "Write instead", LiveAttributes.Retired.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => ("retired-" + Html.Slug(r.Key), r.Key, r.Value)), null);

            page.Body = sb.ToString();
            page.Markdown = md.ToString();
            Add(page);
        }

        private void Section(StringBuilder sb, StringBuilder md, DocPage page, string id, string heading, string intro, string first, string second, IEnumerable<(string Id, string Name, string Text)> rows, string kind)
        {
            sb.Append($"<h2 id=\"{id}\">{Html.H(heading)}</h2><p>{intro}</p>");
            page.Toc.Add((2, id, heading));
            md.Append($"\n## {heading}\n\n{Html.Text(intro)}\n");
            Table(sb, md, first, second, rows, kind);
        }

        private void Table(StringBuilder sb, StringBuilder md, string first, string second, IEnumerable<(string Id, string Name, string Text)> rows, string kind)
        {
            md.Append($"\n| {first} | {second} |\n| --- | --- |\n");
            sb.Append($"<div class=\"table\"><table><thead><tr><th>{first}</th><th>{second}</th></tr></thead><tbody>");
            foreach ((string id, string name, string text) in rows)
            {
                sb.Append($"<tr id=\"{id}\"><td><code>{Html.H(name)}</code></td><td>{Html.Inline(text)}</td></tr>");
                md.Append($"| `{name}` | {text} |\n");
                if (kind != null)
                    Search.Add(new SearchEntry { Title = name, CodeTitle = name, Url = DirectivesPath + "#" + id, Section = "Page directives", Kind = kind, Text = Shorten(Html.Text(Html.Inline(text))) });
            }
            sb.Append("</tbody></table></div>");
        }

        /// <summary>The A to Z entries for the directives page.</summary>
        internal IEnumerable<IndexEntry> DirectiveIndexEntries()
        {
            foreach (AttributeInfo a in LiveAttributes.All)
                yield return new IndexEntry(DirectiveName(a), a.Name.StartsWith("on-", StringComparison.Ordinal) ? "page handler" : "page directive", DirectivesPath + "#" + DirectiveAnchor(a), a.Description);
            foreach (HandlerShorthands.Action a in HandlerShorthands.Actions)
                yield return new IndexEntry(a.Name + ":", "handler shorthand", DirectivesPath + "#shorthand-" + Html.Slug(a.Name), a.Description);
            foreach (HandlerShorthands.Modifier m in HandlerShorthands.Modifiers)
                yield return new IndexEntry(m.Name + ":", "handler modifier", DirectivesPath + "#modifier-" + Html.Slug(m.Name), m.Description);
            foreach (BindDirective.Option o in BindDirective.Options)
                yield return new IndexEntry(o.Name + ":", "option of @bind", DirectivesPath + "#bind-" + Html.Slug(o.Name), o.Description);
            yield return new IndexEntry("$event", "variable in page handlers", DirectivesPath + "#event", "The browser event a handler runs for, with its fields.");
        }
    }
}
