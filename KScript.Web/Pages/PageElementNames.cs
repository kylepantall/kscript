using System;
using System.Collections.Generic;

namespace KScript.Pages
{
    /// <summary>
    /// Element names that are never looked up in _components/: standard HTML and SVG elements, and KScript's own page elements.
    /// Any other name (hero, person-card, …) is a component when a matching file exists.
    /// </summary>
    public static class PageElementNames
    {
        public static readonly IReadOnlySet<string> KScript = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "case", "default", "do", "elif", "else", "for", "if", "import", "include", "layout", "let", "live", "markdown",
            "match", "redirect", "respond", "set", "slot", "state", "while",
        };

        public static readonly IReadOnlySet<string> Html = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "abbr", "address", "area", "article", "aside", "audio", "b", "base", "bdi", "bdo", "blockquote", "body", "br",
            "button", "canvas", "caption", "cite", "code", "col", "colgroup", "data", "datalist", "dd", "del", "details", "dfn",
            "dialog", "div", "dl", "dt", "em", "embed", "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3",
            "h4", "h5", "h6", "head", "header", "hgroup", "hr", "html", "i", "iframe", "img", "input", "ins", "kbd", "label",
            "legend", "li", "link", "main", "map", "mark", "menu", "meta", "meter", "nav", "noscript", "object", "ol",
            "optgroup", "option", "output", "p", "param", "picture", "pre", "progress", "q", "rp", "rt", "ruby", "s", "samp",
            "script", "search", "section", "select", "small", "source", "span", "strong", "style", "sub", "summary", "sup",
            "table", "tbody", "td", "template", "textarea", "tfoot", "th", "thead", "time", "title", "tr", "track", "u", "ul",
            "var", "video", "wbr",
            // SVG and MathML roots, and the SVG children people write inline most often
            "svg", "math", "path", "g", "circle", "rect", "line", "polyline", "polygon", "ellipse", "text", "tspan", "defs",
            "use", "symbol", "clippath", "mask", "pattern", "lineargradient", "radialgradient", "stop", "filter", "image",
            "foreignobject", "marker", "view",
            // obsolete but still parsed by browsers
            "center", "font", "marquee", "frame", "frameset", "noframes", "big", "tt", "strike", "acronym", "applet",
        };

        /// <summary>True when &lt;name&gt; may be a component from _components/.</summary>
        public static bool CanBeComponent(string name) =>
            name.Contains('-') || (!Html.Contains(name) && !KScript.Contains(name));
    }
}
