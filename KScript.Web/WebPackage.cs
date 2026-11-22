using KScript.Metadata;
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
                new A("id", K.Text, "A name for the block, unique on the page.", required: true))
            {
                Context = ElementContext.Page,
                Example = "<live id=\"counter\">\n  <state name=\"count\" value=\"0\" />\n  <button on-click=\"count += 1\">{$count}</button>\n</live>",
            });
            Page(p, new ElementInfo("state", "A variable of a <live> block that keeps its value between events.", ContentKind.Empty,
                new A("name", K.DeclareVariable, "The variable to declare.", required: true),
                new A("value", K.Expression, "Its starting value.", required: true))
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

            p.Variable("request", KType.Request, "The current HTTP request (pages): method, path, query, form, headers, cookies, params, body.", (i, m) => null);
        }

        private static void Page(PackageBuilder p, ElementInfo info) =>
            p.Element(info, run => throw run.Error($"<{info.Name}> only works in pages served by kscript serve.", null, "Context"));
    }
}
