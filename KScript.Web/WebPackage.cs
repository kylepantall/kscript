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
                Example = "<live id=\"stats\" lazy>\n  <placeholder><p>Loading the numbers…</p></placeholder>\n  <state name=\"visits\" value=\"@visits_today()\" />\n  <p>{$visits} visits today</p>\n</live>",
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
                Example = "<live id=\"search\"><state name=\"query\" value=\"''\" /><input @bind($query)></live>",
            });
            Page(p, new ElementInfo("include", "Renders another page file here. Other attributes become its variables.", ContentKind.Empty,
                new A("path", K.Path, "The file to include, relative to this page.", required: true))
            {
                Example = "<include path=\"_partials/card.html\" title=\"Welcome\" body=\"Thanks for signing up.\" />",
                Context = ElementContext.Page,
                AllowsExtraAttributes = true,
            });
            Page(p, new ElementInfo("layout", "Wraps the page in a layout file. Extra attributes become variables in the layout (`$title`).", ContentKind.Markup,
                new A("path", K.Path, "The layout file, such as _layout.html.", true))
            {
                Example = "<layout path=\"_layouts/main.html\" title=\"About us\">\n  <h1>About us</h1>\n  <p>We make tools for people who write scripts.</p>\n</layout>",
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
                Example = "<markdown>\n# Hello, {$name}\n\nYou have **{$count}** new messages.\n</markdown>",
                Context = ElementContext.Page,
            });

            foreach (ValueFieldInfo field in RequestFields.Common)
                p.Field(field.Receiver, field.Name, field.Type, field.Description);

            p.Command("visit", "path: string, options?: map, bearer?: string", "For tests: renders a page of this project as a browser gets it, the way kscript serve answers it: its routes first (a view: route or a controller, with their middleware), then the file pages under serve.root. `options` takes `headers`, as @api() does (`{ headers: { Cookie: 'guest=abc' } }`), and @acting_as() signs the visit in. `->click('Add')` runs a live handler, `->fill('draft', 'milk')` types into a bound input, and `.text`, `.html` and `->classes('Buy milk')` show the result.",
                KType.PageVisit, call => PageVisit.Open(call, Values.ToStr(call.Arg(0)), Values.Unwrap(call.Arg(1, "options")) as Dictionary<string, object>, call.Arg(2, "bearer") is object bearer ? Values.ToStr(bearer) : null), "<let name=\"page\" value=\"@visit('/tasks')->fill('draft', 'Buy milk')->click('Add')\" />\n<echo>{ $page.text }</echo>");
            VisitMethod(p, "click", "text: string", "Runs the live on-click handler of the element with this text (or aria-label or title) and redraws its block. Returns the page, so clicks chain.", KType.PageVisit);
            VisitMethod(p, "change", "text: string", "Runs the on-change handler of the input with this label (the text of the <label> around it), such as a checkbox, and redraws its block.", KType.PageVisit);
            VisitMethod(p, "press", "key, in?: string", "Presses a key on the element with on-keydown (the one with this text, label or placeholder, when given in:): press('enter'), press(['ctrl', 's']).", KType.PageVisit);
            VisitMethod(p, "submit", "text?: string", "Runs the on-submit handler of the form (the one with this text, when given) and redraws its block.", KType.PageVisit);
            VisitMethod(p, "fill", "field: string, value", "Types a value into the input bound to this field (bind=\"$draft\"), sent with the next click or submit.", KType.PageVisit);
            VisitMethod(p, "classes", "text: string", "The classes of the first element whose own text is this: classes('Buy milk').", KType.List);
            p.Field(KType.PageVisit, "html", KType.String, "The page's HTML as it is now, after any clicks.");
            p.Field(KType.PageVisit, "text", KType.String, "The words on the page as it is now, without tags and with runs of spaces made one.");
            p.Field(KType.PageVisit, "path", KType.String, "The address visited.");
            p.Field(KType.PageVisit, "status", KType.Number, "The HTTP status of the last render.");
            p.Examples(
                ("page_visit->click", "<let name=\"page\" value=\"@visit('/counter')->click('Add one')->click('Add one')\" />\n<expect value=\"$page.text\" contains=\"'2'\" />"),
                ("page_visit->change", "<let name=\"page\" value=\"@visit('/settings')->change('Email me updates')\" />\n<expect value=\"$page.text\" contains=\"'Saved'\" />"),
                ("page_visit->press", "<let name=\"page\" value=\"@visit('/tasks')->fill('draft', 'Buy milk')->press('enter', in: 'New task')\" />\n<expect value=\"$page.text\" contains=\"'Buy milk'\" />"),
                ("page_visit->submit", "<let name=\"page\" value=\"@visit('/signup')->fill('email', 'joan@example.com')->submit('Sign up')\" />\n<expect value=\"$page.text\" contains=\"'Check your email'\" />"),
                ("page_visit->fill", "<let name=\"page\" value=\"@visit('/tasks')->fill('draft', 'Buy milk')->click('Add')\" />\n<expect value=\"$page.text\" contains=\"'Buy milk'\" />"),
                ("page_visit->classes", "<let name=\"page\" value=\"@visit('/tasks')->click('Buy milk')\" />\n<expect value=\"$page->classes('Buy milk')\" contains=\"'done'\" />"));

            p.Command("emit_event", "name, data?, target?", "Sends an event from a live action to the page's live blocks. A target limits it to one block ID.", KType.Null, EmitEvent,
                "@emit_event('items-changed', data: $item.id, target: 'summary')");
            p.Command("live_redirect", "path", "Sends the browser to a path on this site after a live action.", KType.Null, RedirectLive,
                "@live_redirect('/account')");
            p.Command("live_download", "name, bytes: base64", "Downloads bytes from a live action as a file. Use a filename without directory separators.",
                KType.Null, DownloadLive, "@live_download('report.txt', bytes: 'Hello'->to_base64())");
            p.Command("stream", "target, text, mode?, html?: bool", "Sends an update to a visible live-stream target before the action finishes. Text is literal by default. Use mode: 'replace' to replace the target's contents, or html: true for trusted HTML.",
                KType.Null, StreamLive, "@stream('progress', 'Working…', mode: 'replace')");
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

        private static object StreamLive(CallContext call)
        {
            PageContext page = LiveAction(call);
            string target = call.Str(0);
            if (page.LiveStreamTargets?.Contains(target) != true)
                throw new KScriptException("The stream needs a visible live-stream target in this component.", "Argument");
            string mode = call.Str(2, "append");
            if (mode is not ("append" or "replace"))
                throw new KScriptException("A live stream mode must be append or replace.", "Argument");
            LiveActionStream.Emit(page.Request, target, call.Str(1), mode, call.Bool(3));
            return null;
        }

        private static void VisitMethod(PackageBuilder p, string name, string parameters, string description, string returns) =>
            p.Method(KType.PageVisit, name, parameters, description, returns, (target, call) =>
                target is PageVisit visit && visit.TryCallMethod(call, name, out object result) ? result : throw call.Error($"->{name}() doesn't work on {Values.Describe(target)}.", "Type"));

        private static PageContext LiveAction(CallContext call) =>
            call.Host.Items.TryGetValue(PageRuntime.ContextKey, out object host) && host is PageContext { InLiveAction: true } page
                ? page : throw new KScriptException($"{call.Display} needs to run in a live component action.", "Context");

        private static void Page(PackageBuilder p, ElementInfo info) =>
            p.Element(info, run => throw run.Error($"<{info.Name}> only works in pages served by kscript serve.", null, "Context"));
    }
}
