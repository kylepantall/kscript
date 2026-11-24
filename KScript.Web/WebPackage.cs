using System;
using System.Collections.Generic;
using System.Linq;

using KScript.Metadata;
using KScript.Pages;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Web
{
    /// <summary>
    /// KScript.Web: the elements of pages served by <c>kscript serve</c> (layouts, components, live blocks). Pages
    /// are rendered by the page engine in KScript.Pages; this package describes its elements to the tools and gives
    /// a clear error when a script uses one outside a page.
    /// </summary>
    public sealed class WebPackage : KScriptPackage
    {
        public override string Name => "KScript.Web";

        public override string Description => "Web pages run by kscript serve: layouts, components and live blocks.";

        protected override void Register(PackageBuilder p)
        {
            p.Requires("KScript.Markdown");

            Page(p, new ElementInfo("live", "A reactive block: its on-* handlers run on the server and only this block is redrawn. No page JavaScript needed.", ContentKind.Markup,
                new A("id", K.Text, "A name for the block, unique on the page. Omit it to assign one in render order."),
                new A("lazy", K.Bool, "Load the block when it enters the viewport."),
                new A("defer", K.Bool, "Load the block after the initial document is ready."))
            {
                Context = ElementContext.Page,
                Example = "<live id=\"counter\">\n  <state name=\"count\" value=\"0\" />\n  <button on-click=\"count += 1\">{$count}</button>\n</live>",
            });
            Page(p, new ElementInfo("placeholder", "What a lazy or deferred live component shows before it loads.", ContentKind.Markup)
            {
                Context = ElementContext.Page,
                MustBeInside = new[] { "live" },
            });
            Page(p, new ElementInfo("state", "A variable of a <live> block that keeps its value between events.", ContentKind.Empty,
                new A("name", K.DeclareVariable, "The variable to declare.", required: true),
                new A("value", K.Expression, "Its starting value.", required: true),
                new A("session", K.Text, "A session key used to keep this state across page visits."),
                new A("locked", K.Bool, "Only actions on the server can change this state."),
                new A("query", K.Text, "A URL query parameter synchronized with this state."))
            {
                Context = ElementContext.Page,
                MustBeInside = new[] { "live" },
                Example = "<live id=\"search\"><state name=\"query\" value=\"''\" /><input bind=\"query\"></live>",
            });
            Page(p, new ElementInfo("include", "Renders another page file here. Other attributes become its variables.", ContentKind.Empty,
                new A("path", K.Path, "The file to include, relative to this page.", required: true))
            {
                Context = ElementContext.Page,
                AllowsExtraAttributes = true,
            });
            Page(p, new ElementInfo("layout", "Wraps the page in a layout file. Extra attributes become variables in the layout (`$title`).", ContentKind.Markup,
                new A("path", K.Path, "The layout file, such as _layout.html.", true))
            {
                AllowsExtraAttributes = true,
                Context = ElementContext.Page,
            });
            Page(p, new ElementInfo("prop", "Declares a value a layout or component receives from its caller.", ContentKind.Empty,
                new A("name", K.Text, "The input name, available as a variable in the template.", required: true),
                new A("required", K.Bool, "Require the caller to supply a non-null value."))
            {
                Context = ElementContext.Page,
                Example = "<prop name=\"title\" required />",
            });
            Page(p, new ElementInfo("slot", "In a layout or component: where the page's (or caller's) content goes.", ContentKind.Empty,
                new A("name", K.Text, "A named slot."))
            {
                Context = ElementContext.Page,
            });
            Page(p, new ElementInfo("respond", "Ends the request with this response.", ContentKind.Template,
                new A("status", K.Expression, "The HTTP status code."),
                new A("json", K.Expression, "Send this value as JSON."),
                new A("redirect", K.Template, "Send the visitor to this address instead. The status is 302 unless status says otherwise."),
                new A("type", K.Text, "The Content-Type."))
            {
                Context = ElementContext.Page,
                Example = "<respond status=\"404\">Not found</respond>\n<respond json=\"$people\" />\n<respond redirect=\"/thanks\" />",
            });
            Page(p, new ElementInfo("markdown", "Renders its Markdown text (with `{ }` values, escaped) to HTML.", ContentKind.Template)
            {
                Context = ElementContext.Page,
            });

            foreach (ValueFieldInfo field in RequestFields.Common)
                p.Field(field.Receiver, field.Name, field.Type, field.Description);

            p.Command("emit_event", "name, data?, target?", "Sends an event from a live action to the page's live blocks. A target limits it to one block ID.", KType.Null, EmitEvent,
                "@emit_event('items-changed', data: $item.id, target: 'summary')");
            p.Command("live_redirect", "path", "Sends the browser to a path on this site after a live action.", KType.Null, RedirectLive,
                "@live_redirect('/account')");
            p.Command("live_download", "name, bytes: base64", "Downloads bytes from a live action as a file. Use a filename without directory separators.",
                KType.Null, DownloadLive, "@live_download('report.txt', bytes: 'Hello'->to_base64())");
            p.Command("skip_render", "", "Keeps the current HTML after a live action while saving its state and sending any effects.", KType.Null, call =>
            {
                LiveAction(call).SkipLiveRender = true;
                return null;
            }, "@skip_render()");
            p.Variable("request", KType.Request, "The current HTTP request on a page.", (i, m) => null);
        }

        private static object EmitEvent(CallContext call)
        {
            PageContext page = LiveAction(call);
            string name = call.Str(0);
            if (name.Length is < 1 or > 80)
                throw new KScriptException("An event name needs 1–80 characters.", "Argument");
            if (page.LiveEffects.Count == 16)
                throw new KScriptException("A live action can emit up to 16 events.", "Limit");

            string data = Values.ToJson(call.Arg(1));
            if (data.Length > 4096) throw new KScriptException("A live event payload can contain up to 4,096 characters.", "Limit");
            page.LiveEffects.Add(new Dictionary<string, object>
            {
                ["name"] = name,
                ["data"] = Values.ParseJson(data),
                ["target"] = call.Arg(2) is null ? null : call.Str(2),
            });
            return null;
        }

        private static object RedirectLive(CallContext call)
        {
            PageContext page = LiveAction(call);
            string path = call.Str(0);
            if (!path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
                path.Contains('\\') || path.Any(char.IsControl))
                throw new KScriptException("A live redirect needs a path on this site, such as /account.", "Argument");
            page.LiveRedirect = path;
            return null;
        }

        private static object DownloadLive(CallContext call)
        {
            PageContext page = LiveAction(call);
            string name = call.Str(0);
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255 ||
                name.Contains('/') || name.Contains('\\') || name.Any(char.IsControl))
                throw new KScriptException("A live download needs a filename without directory separators.", "Argument");
            string bytes = call.Str(1);
            if (bytes.Length > 12 * 1024 * 1024)
                throw new KScriptException("A live download can contain up to 12 MiB of base64 text.", "Limit");
            page.LiveDownload = new Dictionary<string, object> { ["name"] = name, ["bytes"] = bytes };
            return null;
        }

        private static PageContext LiveAction(CallContext call) =>
            call.Host.Items.TryGetValue(PageRuntime.ContextKey, out object host) && host is PageContext { InLiveAction: true } page
                ? page : throw new KScriptException($"{call.Display} needs to run in a live component action.", "Context");

        private static void Page(PackageBuilder p, ElementInfo info) =>
            p.Element(info, run => throw run.Error($"<{info.Name}> only works in pages served by kscript serve.", null, "Context"));
    }
}
