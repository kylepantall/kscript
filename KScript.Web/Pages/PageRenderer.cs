using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KScript.Pages
{
    /// <summary>What a page produced: usually HTML, or a response set by &lt;respond&gt; / &lt;redirect&gt;.</summary>
    public sealed class PageResult
    {
        public int Status { get; set; } = 200;
        public string ContentType { get; set; } = "text/html; charset=utf-8";
        public string Body { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>For static files: the file to send instead of <see cref="Body"/>.</summary>
        public string FilePath { get; set; }
    }

    /// <summary>Thrown by &lt;respond&gt; and &lt;redirect&gt; to stop rendering and send a specific response.</summary>
    internal sealed class PageResponseSignal : Exception
    {
        public PageResponseSignal(PageResult result) : base("response") { Result = result; }
        public PageResult Result { get; }
    }

    /// <summary>Per-request state: the site folder, the request, and limits that stop runaway pages.</summary>
    public sealed class PageContext
    {
        public PageContext(string siteRoot, Dictionary<string, object> request = null)
        {
            SiteRoot = Path.GetFullPath(siteRoot ?? Directory.GetCurrentDirectory());
            Request = request ?? new Dictionary<string, object>();
        }

        public string SiteRoot { get; }
        public Dictionary<string, object> Request { get; }

        /// <summary>Maximum evaluation steps for one request, so an endless loop can't hang the server.</summary>
        public long MaxSteps { get; set; } = 5_000_000;

        /// <summary>Set when re-rendering a &lt;live&gt; block for a browser event.</summary>
        public LiveEvent Live { get; set; }

        private PageScripts scripts;

        /// <summary>The scripts pages import with &lt;import src&gt;, run by the KScript interpreter.</summary>
        internal PageScripts Scripts => scripts ??= new PageScripts(this);

        internal int LiveCounter;
        internal long Steps;
        internal int CallDepth;
        internal int IncludeDepth;

        internal void Step()
        {
            if (++Steps > MaxSteps)
                throw new PageException($"The page took more than {MaxSteps:N0} steps. Is there an endless loop?");
        }

        /// <summary>Resolves a path inside the site folder. Paths that would leave it are refused.</summary>
        public string ResolveSitePath(string path, string where = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new PageException($"Missing file path{(where != null ? $" ({where})" : string.Empty)}.");
            string full = Path.GetFullPath(Path.Combine(SiteRoot, path.TrimStart('/', '\\')));
            string root = SiteRoot.EndsWith(Path.DirectorySeparatorChar) ? SiteRoot : SiteRoot + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.Ordinal) && full != SiteRoot)
                throw new PageException($"'{path}' is outside the site folder{(where != null ? $" ({where})" : string.Empty)}.");
            return full;
        }

        internal string ReadSiteFile(string path, string where)
        {
            string full = ResolveSitePath(path, where);
            if (!File.Exists(full)) throw new PageException($"File not found: {path} ({where}).");
            return File.ReadAllText(full);
        }

        internal bool SiteFileExists(string path)
        {
            try { return File.Exists(ResolveSitePath(path)); }
            catch (PageException) { return false; }
        }

        internal List<object> ListSiteFiles(string dir, string pattern, string where)
        {
            string full = ResolveSitePath(dir, where);
            if (!Directory.Exists(full)) return new List<object>();
            return Directory.GetFiles(full, pattern)
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => (object)Path.GetRelativePath(SiteRoot, f).Replace('\\', '/'))
                .ToList();
        }
    }

    /// <summary>
    /// Renders KScript pages: HTML (or Markdown) files with KScript control elements written inline,
    /// <c>{ expression }</c> interpolation in text and attributes, layouts and components.
    /// </summary>
    public static class PageRenderer
    {
        private sealed class RenderState
        {
            public PageContext Ctx;
            public string File;
            public bool Markdown;
            public string Slot;

            /// <summary>Set after an empty slot on its own line, so the line's newline is dropped too.</summary>
            public bool SwallowNewline;

            /// <summary>Inside a &lt;live&gt; block: the block's scope, where its state lives.</summary>
            public PageScope LiveScope;
        }

        /// <summary>Renders a page file (relative to the site folder): .html or .md.</summary>
        public static PageResult RenderFile(PageContext ctx, string relativePath, Dictionary<string, object> parameters = null)
        {
            var scope = NewPageScope(ctx, parameters);
            try
            {
                string html = RenderFileInScope(ctx, relativePath, scope, slot: null);
                if (ctx.Live != null) throw new PageException($"There is no <live id=\"{ctx.Live.BlockId}\"> block on this page any more. Reload the page.");
                return new PageResult { Body = AddLiveRuntime(html.TrimStart()) };
            }
            catch (PageResponseSignal signal)
            {
                return signal.Result;
            }
        }

        /// <summary>Renders a page from a string. Used by tests and by tools that hold the template in memory.</summary>
        public static PageResult RenderString(string template, PageContext ctx = null, bool markdown = false, string fileName = "page.html")
        {
            ctx ??= new PageContext(Directory.GetCurrentDirectory());
            var scope = NewPageScope(ctx, null);
            try
            {
                string html = markdown
                    ? RenderMarkdownSource(template, fileName, ctx, scope, null)
                    : RenderSource(template, 1, new RenderState { Ctx = ctx, File = fileName }, scope);
                return new PageResult { Body = AddLiveRuntime(html.TrimStart()) };
            }
            catch (PageResponseSignal signal)
            {
                return signal.Result;
            }
        }

        /// <summary>Pages with &lt;live&gt; blocks get the small runtime that sends their events to the server.</summary>
        private static string AddLiveRuntime(string html)
        {
            if (!html.Contains("data-ks-live=\"", StringComparison.Ordinal)) return html;
            const string tag = "<script src=\"/_ks/live.js\" defer></script>";
            int body = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            return body >= 0 ? html.Insert(body, tag + "\n") : html + "\n" + tag;
        }

        private static PageScope NewPageScope(PageContext ctx, Dictionary<string, object> parameters)
        {
            var globals = new PageScope();
            var request = ctx.Request;
            if (parameters != null) request["params"] = parameters.ToDictionary(kv => kv.Key, kv => kv.Value);
            globals.Declare("request", request);
            return globals.Child();
        }

        private static string RenderFileInScope(PageContext ctx, string relativePath, PageScope scope, string slot)
        {
            if (++ctx.IncludeDepth > 32) throw new PageException($"Layouts, components or includes are nested more than 32 deep (at {relativePath}). Does one include itself?");
            try
            {
                string text = ctx.ReadSiteFile(relativePath, "page");
                string name = relativePath.Replace('\\', '/').TrimStart('/');
                if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    return RenderMarkdownSource(text, name, ctx, scope, slot);
                return RenderSource(text, 1, new RenderState { Ctx = ctx, File = name, Slot = slot }, scope);
            }
            finally
            {
                ctx.IncludeDepth--;
            }
        }

        private static string RenderMarkdownSource(string text, string name, PageContext ctx, PageScope scope, string slot)
        {
            var (meta, body, bodyLine) = PageMarkdown.SplitFrontMatter(text);
            var pageScope = scope.Child();
            foreach (var kv in meta) pageScope.Declare(kv.Key, kv.Value);
            pageScope.Declare("page", meta);

            string markdown = RenderSource(body, bodyLine, new RenderState { Ctx = ctx, File = name, Markdown = true, Slot = slot }, pageScope);
            string html = PageMarkdown.ToHtml(markdown);

            if (meta.TryGetValue("layout", out var layout) && layout != null)
                return RenderFileInScope(ctx, PageValues.ToStr(layout), pageScope, html);
            return html;
        }

        private static string RenderSource(string text, int firstLine, RenderState state, PageScope scope)
        {
            List<PageNode> nodes;
            try
            {
                nodes = PageTemplate.Parse(text, firstLine);
            }
            catch (PageException ex)
            {
                ex.Location ??= state.File;
                throw;
            }
            var sb = new StringBuilder();
            RenderNodes(nodes, scope, sb, state);
            return sb.ToString();
        }

        private static void RenderNodes(List<PageNode> nodes, PageScope scope, StringBuilder sb, RenderState state)
        {
            // State of the current if / elif / else (or for / else) chain: null when there is no chain.
            bool? chainMatched = null;

            foreach (var node in nodes)
            {
                try
                {
                    if (node is PageText text)
                    {
                        string t = text.Text;
                        if (state.SwallowNewline)
                        {
                            state.SwallowNewline = false;
                            int nl = t.IndexOf('\n');
                            if (nl >= 0 && t.Substring(0, nl).Trim().Length == 0) t = t.Substring(nl + 1);
                        }
                        if (text.Raw) sb.Append(t);
                        else
                        {
                            sb.Append(Interpolate(t, scope, state.Ctx, state.Markdown ? EscapeMode.Markdown : EscapeMode.Html));
                            if (text.Text.Trim().Length > 0) chainMatched = null;
                        }
                        continue;
                    }

                    var el = (PageElement)node;
                    switch (el.Name)
                    {
                        case "if":
                        {
                            bool ok = Test(el, scope, state);
                            if (ok) RenderNodes(el.Children, scope.Child(), sb, state);
                            chainMatched = ok;
                            continue;
                        }
                        case "elif":
                        {
                            if (chainMatched == null) throw new PageException("<elif> must come right after an <if> or <elif>.");
                            if (chainMatched == false && Test(el, scope, state))
                            {
                                RenderNodes(el.Children, scope.Child(), sb, state);
                                chainMatched = true;
                            }
                            continue;
                        }
                        case "else":
                        {
                            if (chainMatched == null) throw new PageException("<else> must come right after an <if>, <elif> or <for>.");
                            if (chainMatched == false) RenderNodes(el.Children, scope.Child(), sb, state);
                            chainMatched = null;
                            continue;
                        }
                        case "for":
                            chainMatched = RenderFor(el, scope, sb, state);
                            continue;
                    }

                    chainMatched = null;
                    RenderElement(el, scope, sb, state);
                }
                catch (PageException ex)
                {
                    ex.Location ??= $"{state.File}:{node.Line}";
                    throw;
                }
            }
        }

        private static bool Test(PageElement el, PageScope scope, RenderState state)
        {
            string test = el.Attr("test") ?? throw new PageException($"<{el.Name}> needs a test attribute, e.g. <{el.Name} test=\"$count gt 0\">.");
            return PageValues.Truthy(Eval(test, scope, state));
        }

        private static object Eval(string expr, PageScope scope, RenderState state) => PageEvaluator.Evaluate(expr, scope, state.Ctx);

        private static void RenderElement(PageElement el, PageScope scope, StringBuilder sb, RenderState state)
        {
            switch (el.Name)
            {
                case "let":
                case "set":
                    Assign(el, scope, state, declare: el.Name == "let");
                    return;

                case "do":
                    RunStatements(InnerText(el), scope, state, declare: false);
                    return;

                case "while":
                {
                    int guard = 0;
                    while (Test(el, scope, state))
                    {
                        if (++guard > 100_000) throw new PageException("<while> ran more than 100,000 times. Is the test ever false?");
                        RenderNodes(el.Children, scope.Child(), sb, state);
                    }
                    return;
                }

                case "match":
                    RenderMatch(el, scope, sb, state);
                    return;

                case "case":
                case "default":
                    throw new PageException($"<{el.Name}> must be inside <match>.");

                case "live":
                    RenderLive(el, scope, sb, state);
                    return;

                case "import":
                {
                    if (el.Attr("package") != null)
                        throw new PageException("Pages can't import packages. Put the code that needs KScript.Math, KScript.IO and so on in a script and import that: <import src=\"lib/tools.ks.xml\" />.");
                    string src = el.Attr("src") ?? throw new PageException("<import> in a page needs src, a script relative to the site folder: <import src=\"lib/life.ks.xml\" />.");
                    state.Ctx.Scripts.Import(src, el.Attr("names"), el.Attr("as"), scope);
                    return;
                }

                case "state":
                    // Handled by the enclosing <live>.
                    if (state.LiveScope == null) throw new PageException("<state> must be inside a <live> block.");
                    return;

                case "layout":
                {
                    string src = el.Attr("src") ?? throw new PageException("<layout> needs a src attribute, e.g. <layout src=\"_layout.html\">.");
                    var inner = scope.Child();
                    var body = new StringBuilder();
                    RenderNodes(el.Children, inner, body, state);
                    var layoutScope = scope.Child();
                    foreach (var a in el.Attributes.Where(a => a.Name != "src")) layoutScope.Declare(VarName(a.Name), AttributeValue(a, scope, state));
                    AppendIndented(sb, RenderFileInScope(state.Ctx, Interpolate(src, scope, state.Ctx, EscapeMode.None), layoutScope, body.ToString()), state);
                    return;
                }

                case "slot":
                    if (state.Slot != null) AppendIndented(sb, state.Slot, state);
                    else RenderNodes(el.Children, scope.Child(), sb, state);
                    return;

                case "include":
                {
                    string src = el.Attr("src") ?? throw new PageException("<include> needs a src attribute.");
                    var incScope = scope.Child();
                    foreach (var a in el.Attributes.Where(a => a.Name != "src")) incScope.Declare(VarName(a.Name), AttributeValue(a, scope, state));
                    AppendIndented(sb, RenderFileInScope(state.Ctx, Interpolate(src, scope, state.Ctx, EscapeMode.None), incScope, null), state);
                    return;
                }

                case "markdown":
                {
                    var md = new StringBuilder();
                    RenderNodes(el.Children, scope.Child(), md, new RenderState { Ctx = state.Ctx, File = state.File, Markdown = true, Slot = state.Slot, LiveScope = state.LiveScope });
                    sb.Append(PageMarkdown.ToHtml(PageMarkdown.Dedent(md.ToString())));
                    return;
                }

                case "respond":
                {
                    var result = new PageResult();
                    if (el.Attr("status") is string status) result.Status = (int)PageValues.ToNumber(Eval(status, scope, state), "<respond status>");
                    if (el.Attr("json") is string json)
                    {
                        result.ContentType = "application/json; charset=utf-8";
                        result.Body = PageValues.ToJsonString(Eval(json, scope, state), indented: true);
                    }
                    else
                    {
                        var body = new StringBuilder();
                        RenderNodes(el.Children, scope.Child(), body, state);
                        result.Body = body.ToString().Trim();
                        if (el.Attr("type") is string type) result.ContentType = Interpolate(type, scope, state.Ctx, EscapeMode.None);
                    }
                    foreach (var a in el.Attributes.Where(a => a.Name.StartsWith("header-", StringComparison.Ordinal)))
                        result.Headers[a.Name.Substring(7)] = Interpolate(a.Value ?? string.Empty, scope, state.Ctx, EscapeMode.None);
                    throw new PageResponseSignal(result);
                }

                case "redirect":
                {
                    string to = el.Attr("to") ?? throw new PageException("<redirect> needs a to attribute, e.g. <redirect to=\"/thanks\" />.");
                    var result = new PageResult { Status = 302, Body = string.Empty };
                    if (el.Attr("status") is string status) result.Status = (int)PageValues.ToNumber(Eval(status, scope, state), "<redirect status>");
                    result.Headers["Location"] = Interpolate(to, scope, state.Ctx, EscapeMode.None);
                    throw new PageResponseSignal(result);
                }
            }

            if (PageElementNames.CanBeComponent(el.Name) && TryComponent(el, scope, sb, state)) return;

            RenderHtmlElement(el, scope, sb, state);
        }

        /// <summary>
        /// Writes slot content at the slot's indentation, so layouts and components produce tidy HTML.
        /// Content with &lt;pre&gt; or &lt;textarea&gt; is left exactly as it is, since whitespace matters there.
        /// </summary>
        private static void AppendIndented(StringBuilder sb, string content, RenderState state)
        {
            int lineStart = 0;
            for (int i = sb.Length - 1; i >= 0; i--) { if (sb[i] == '\n') { lineStart = i + 1; break; } }
            string indent = sb.ToString(lineStart, sb.Length - lineStart);
            bool ownLine = indent.Trim().Length == 0;

            if (ownLine && content.Trim().Length == 0)
            {
                // An empty slot on its own line leaves no blank line behind.
                sb.Length = lineStart;
                state.SwallowNewline = true;
                return;
            }

            if (!ownLine || content.Contains("<pre", StringComparison.OrdinalIgnoreCase) || content.Contains("<textarea", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(content.Trim('\n'));
                return;
            }

            string dedented = PageMarkdown.Dedent(content);
            sb.Append(string.Join("\n" + indent, dedented.Split('\n').Select(l => l.TrimEnd())));
        }

        private static string VarName(string attributeName) => attributeName.Replace('-', '_');

        private static bool TryComponent(PageElement el, PageScope scope, StringBuilder sb, RenderState state)
        {
            string path = $"_components/{el.Name}.html";
            if (!state.Ctx.SiteFileExists(path))
            {
                path = $"_components/{el.Name}.md";
                if (!state.Ctx.SiteFileExists(path)) return false;
            }

            var body = new StringBuilder();
            RenderNodes(el.Children, scope.Child(), body, state);

            // Components only see their own attributes (and $request), not the variables of the page using them.
            var root = scope;
            while (root.Parent != null) root = root.Parent;
            var componentScope = root.Child();
            foreach (var a in el.Attributes) componentScope.Declare(VarName(a.Name), AttributeValue(a, scope, state));
            AppendIndented(sb, RenderFileInScope(state.Ctx, path, componentScope, body.ToString()), state);
            return true;
        }

        private static void RenderHtmlElement(PageElement el, PageScope scope, StringBuilder sb, RenderState state)
        {
            sb.Append('<').Append(el.SourceName ?? el.Name);
            bool hasHandler = false;
            foreach (var a in el.Attributes)
            {
                if (a.Name.StartsWith("on-", StringComparison.Ordinal))
                {
                    if (state.LiveScope == null || a.HandlerIndex < 0)
                        throw new PageException($"{a.Name} only works inside a <live> block, e.g. <live id=\"counter\">…</live>.");
                    sb.Append(" data-ks-").Append(a.Name).Append("=\"").Append(a.HandlerIndex).Append('"');
                    hasHandler = true;
                    continue;
                }
                if (a.Name == "every" && el.HasAttr("on-tick"))
                {
                    // <span on-tick="..." every="200">: how often, in milliseconds, the tick handler runs.
                    double ms = PageValues.ToNumber(TryWholeExpression(a.Value ?? string.Empty, out string everyExpr, out _) ? Eval(everyExpr, scope, state) : a.Value, "every");
                    sb.Append(" data-ks-every=\"").Append(PageValues.FormatNumber(Math.Max(16, ms))).Append('"');
                    continue;
                }
                if (a.Name == "bind")
                {
                    if (state.LiveScope == null) throw new PageException("bind only works inside a <live> block.");
                    string name = (a.Value ?? string.Empty).Trim().TrimStart('$');
                    if (!scope.TryGet(name, out var bound)) throw new PageException($"bind=\"{name}\": ${name} is not defined. Declare it with <state name=\"{name}\" value=\"''\" />.");
                    sb.Append(" data-ks-bind=\"").Append(PageValues.HtmlEscape(name)).Append('"');
                    if (el.Name == "input" && string.Equals(el.Attr("type"), "checkbox", StringComparison.OrdinalIgnoreCase))
                    {
                        if (PageValues.Truthy(bound)) sb.Append(" checked");
                    }
                    else if (el.Name != "textarea" && el.Name != "select")
                    {
                        sb.Append(" value=\"").Append(PageValues.HtmlEscape(PageValues.ToStr(bound))).Append('"');
                    }
                    if (!el.HasAttr("name")) sb.Append(" name=\"").Append(PageValues.HtmlEscape(name)).Append('"');
                    continue;
                }
                if (a.Value == null) { sb.Append(' ').Append(a.SourceName ?? a.Name); continue; }

                if (TryWholeExpression(a.Value, out string expr, out bool raw))
                {
                    object v = Eval(expr, scope, state);
                    if (v == null || v is bool b && !b) continue;
                    if (v is bool) { sb.Append(' ').Append(a.SourceName ?? a.Name); continue; }
                    string s = PageValues.ToStr(v);
                    sb.Append(' ').Append(a.SourceName ?? a.Name).Append("=\"").Append(raw ? s.Replace("\"", "&quot;") : PageValues.HtmlEscape(s)).Append('"');
                    continue;
                }

                string value = Interpolate(a.Value, scope, state.Ctx, EscapeMode.Html);
                sb.Append(' ').Append(a.SourceName ?? a.Name).Append("=\"").Append(value.Replace("\"", "&quot;")).Append('"');
            }

            if (hasHandler)
            {
                var locals = LocalVariables(scope, state.LiveScope);
                if (locals.Count > 0) sb.Append(" data-ks-args=\"").Append(PageValues.HtmlEscape(PageLive.Sign(locals))).Append('"');
            }

            if (el.Void) { sb.Append('>'); return; }
            sb.Append('>');
            if (el.Name == "textarea" && el.Attr("bind") is string tb && scope.TryGet(tb.Trim().TrimStart('$'), out var tv))
            {
                sb.Append(PageValues.HtmlEscape(PageValues.ToStr(tv))).Append("</textarea>");
                return;
            }
            RenderNodes(el.Children, scope.Child(), sb, state);
            sb.Append("</").Append(el.SourceName ?? el.Name).Append('>');
        }

        /// <summary>Attribute values for layouts, includes and components: a whole <c>{expr}</c> passes the value itself.</summary>
        private static object AttributeValue(PageAttribute a, PageScope scope, RenderState state)
        {
            if (a.Value == null) return true;
            if (TryWholeExpression(a.Value, out string expr, out _)) return Eval(expr, scope, state);
            return Interpolate(a.Value, scope, state.Ctx, EscapeMode.None);
        }

        /// <summary>True when the text is exactly one <c>{ expression }</c> (or <c>{! expression }</c>).</summary>
        private static bool TryWholeExpression(string text, out string expr, out bool raw)
        {
            expr = null;
            raw = false;
            string t = text.Trim();
            if (t.Length < 3 || t[0] != '{' || t[1] == '{') return false;
            int end = FindClose(t, 1);
            if (end != t.Length - 1) return false;
            expr = t.Substring(1, end - 1);
            if (expr.StartsWith("!", StringComparison.Ordinal) && !expr.StartsWith("!=", StringComparison.Ordinal)) { raw = true; expr = expr.Substring(1); }
            return expr.Trim().Length > 0;
        }

        private static bool RenderFor(PageElement el, PageScope scope, StringBuilder sb, RenderState state)
        {
            string each = el.Attr("each") ?? throw new PageException("<for> needs each and in attributes, e.g. <for each=\"p\" in=\"$people\">.");
            string inExpr = el.Attr("in") ?? throw new PageException("<for> needs an in attribute, e.g. <for each=\"p\" in=\"$people\">.");
            string indexName = el.Attr("index");
            var names = each.Split(',').Select(n => n.Trim().TrimStart('$')).ToArray();

            object source = Eval(inExpr, scope, state);
            IEnumerable<(object item, object key)> items = source switch
            {
                null => Enumerable.Empty<(object, object)>(),
                List<object> list => list.Select(x => (x, (object)null)),
                Dictionary<string, object> map => map.Select(kv => (names.Length > 1 ? kv.Value : new Dictionary<string, object> { ["key"] = kv.Key, ["value"] = kv.Value }, (object)kv.Key)),
                double n => Enumerable.Range(0, Math.Max(0, (int)n)).Select(x => ((object)(double)x, (object)null)),
                string s => s.Select(ch => ((object)ch.ToString(), (object)null)),
                _ => throw new PageException($"<for> can't loop over {PageValues.Describe(source)}."),
            };

            int index = 0;
            foreach (var (item, key) in items)
            {
                var iter = scope.Child();
                if (names.Length > 1 && key != null)
                {
                    iter.Declare(names[0], key);
                    iter.Declare(names[1], item);
                }
                else
                {
                    iter.Declare(names[0], item);
                }
                if (indexName != null) iter.Declare(indexName.TrimStart('$'), (double)index);
                iter.Declare("loop", new Dictionary<string, object> { ["index"] = (double)index, ["first"] = index == 0 });
                RenderNodes(el.Children, iter, sb, state);
                index++;
            }
            return index > 0;
        }

        /// <summary>
        /// A &lt;live&gt; block is re-rendered on the server when its handlers run in the browser.
        /// Its &lt;state&gt; travels with the page, signed, so the server keeps nothing between requests.
        /// </summary>
        private static void RenderLive(PageElement el, PageScope scope, StringBuilder sb, RenderState state)
        {
            if (state.LiveScope != null) throw new PageException("<live> blocks can't be nested.");
            var ctx = state.Ctx;
            string id = el.Attr("id") ?? $"live-{++ctx.LiveCounter}";
            var handlers = PageLive.Handlers(el);
            var liveScope = scope.Child();
            bool target = ctx.Live != null && ctx.Live.BlockId == id;

            // State: defaults from <state>, or the values the browser sent back.
            var stateNames = new List<string>();
            foreach (var st in el.Children.OfType<PageElement>().Where(c => c.Name == "state"))
            {
                string name = (st.Attr("name") ?? throw new PageException("<state> needs a name, e.g. <state name=\"count\" value=\"0\" />.")).TrimStart('$');
                stateNames.Add(name);
                if (target && ctx.Live.State.TryGetValue(name, out var sent))
                {
                    liveScope.Declare(name, sent);
                    continue;
                }
                string expr = st.Attr("value") ?? (st.Children.OfType<PageText>().Any() ? string.Concat(st.Children.OfType<PageText>().Select(t => t.Text)) : null);
                liveScope.Declare(name, expr == null || expr.Trim().Length == 0 ? null : Eval(expr, liveScope, state));
            }

            if (target)
            {
                foreach (var kv in ctx.Live.Binds)
                {
                    if (!stateNames.Contains(kv.Key)) continue;
                    liveScope.Assign(kv.Key, kv.Value is string sv && liveScope.TryGet(kv.Key, out var old) && old is double && PageValues.TryNumber(sv, out double num) ? num : kv.Value);
                }

                if (ctx.Live.Handler >= 0)
                {
                    if (ctx.Live.Handler >= handlers.Count) throw new LiveExpiredException();
                    var handlerScope = liveScope.Child();
                    foreach (var kv in ctx.Live.Args) handlerScope.Declare(kv.Key, kv.Value);
                    handlerScope.Declare("event", ctx.Live.Event);
                    RunStatements(handlers[ctx.Live.Handler].Value ?? string.Empty, handlerScope, state, declare: false);
                }
            }

            var body = new StringBuilder();
            var inner = new RenderState { Ctx = ctx, File = state.File, Markdown = state.Markdown, Slot = state.Slot, LiveScope = liveScope };
            RenderNodes(el.Children, liveScope, body, inner);

            var values = new Dictionary<string, object>();
            foreach (var name in stateNames)
            {
                liveScope.TryGet(name, out var v);
                if (v is PageLambda || v is KScript.Runtime.ICallable) throw new PageException($"State ${name} holds a function, which can't be sent to the browser.");
                values[name] = v;
            }

            string tag = el.Attr("as") ?? "div";
            var html = new StringBuilder();
            html.Append('<').Append(tag).Append(" data-ks-live=\"").Append(PageValues.HtmlEscape(id)).Append('"');
            html.Append(" data-ks-state=\"").Append(PageValues.HtmlEscape(PageLive.Sign(values))).Append('"');
            foreach (var a in el.Attributes.Where(a => a.Name != "id" && a.Name != "as"))
            {
                if (a.Value == null) html.Append(' ').Append(a.SourceName ?? a.Name);
                else html.Append(' ').Append(a.SourceName ?? a.Name).Append("=\"").Append(Interpolate(a.Value, scope, ctx, EscapeMode.Html).Replace("\"", "&quot;")).Append('"');
            }
            html.Append(" id=\"").Append(PageValues.HtmlEscape(id)).Append("\">");
            html.Append(body).Append("</").Append(tag).Append('>');

            if (target)
            {
                ctx.Live.Output = html.ToString();
                throw new PageResponseSignal(new PageResult { Body = html.ToString() });
            }
            sb.Append(html);
        }

        /// <summary>Variables declared between a handler's element and its live block, e.g. the loop item.</summary>
        private static Dictionary<string, object> LocalVariables(PageScope scope, PageScope liveScope)
        {
            var locals = new Dictionary<string, object>();
            for (var s = scope; s != null && s != liveScope; s = s.Parent)
            {
                foreach (var kv in s.OwnVariables)
                {
                    if (kv.Value is PageLambda || kv.Value is KScript.Runtime.ICallable || locals.ContainsKey(kv.Key)) continue;
                    locals[kv.Key] = kv.Value;
                }
            }
            return locals;
        }

        private static void RenderMatch(PageElement el, PageScope scope, StringBuilder sb, RenderState state)
        {
            string valueExpr = el.Attr("value") ?? throw new PageException("<match> needs a value attribute.");
            object value = Eval(valueExpr, scope, state);
            var matchScope = scope.Child();
            matchScope.Declare("it", value);

            foreach (var c in el.Children.OfType<PageElement>())
            {
                bool hit;
                if (c.Name == "default") hit = true;
                else if (c.Name == "case")
                {
                    if (c.Attr("is") is string isExpr)
                    {
                        var options = Eval("[" + isExpr + "]", matchScope, state) as List<object>;
                        hit = options.Any(o => PageValues.LooseEquals(o, value));
                    }
                    else if (c.Attr("when") is string when) hit = PageValues.Truthy(Eval(when, matchScope, state));
                    else throw new PageException("<case> needs an is or when attribute.");
                }
                else throw new PageException($"Only <case> and <default> can go inside <match>, not <{c.Name}>.");

                if (hit)
                {
                    RenderNodes(c.Children, matchScope.Child(), sb, state);
                    return;
                }
            }
        }

        private static string InnerText(PageElement el) =>
            string.Concat(el.Children.Select(c => c is PageText t ? t.Text : throw new PageException($"<{el.Name}> can only contain text, not <{((PageElement)c).Name}>.")));

        private static void Assign(PageElement el, PageScope scope, RenderState state, bool declare)
        {
            string name = el.Attr("name");
            if (name == null)
            {
                string statements = InnerText(el);
                if (statements.Trim().Length == 0) throw new PageException($"<{el.Name}> needs a name and value, e.g. <{el.Name} name=\"count\" value=\"0\" />, or a statement like <{el.Name}>count = 0</{el.Name}>.");
                RunStatements(statements, scope, state, declare);
                return;
            }

            name = name.TrimStart('$');
            string expr = el.Attr("value");
            if (expr == null)
            {
                string inner = InnerText(el);
                expr = inner.Trim().Length > 0 ? inner : null;
            }
            object value = expr == null ? null : Eval(expr, scope, state);

            string op = el.Attr("op");
            if (op != null && op != "=")
            {
                if (!scope.TryGet(name, out var current)) throw new PageException($"${name} is not defined, so op=\"{op}\" can't update it.");
                value = Combine(current, op, value);
            }

            if (declare) scope.Declare(name, value);
            else scope.Assign(name, value);
        }

        private static readonly Regex Statement = new Regex(@"^\s*\$?([A-Za-z_]\w*)\s*(\+=|-=|\*=|/=|\?\?=|=)(?!=)\s*(.+?)\s*$", RegexOptions.Singleline);

        /// <summary>Runs <c>x = 3; $total += $price</c> style statements.</summary>
        private static void RunStatements(string text, PageScope scope, RenderState state, bool declare)
        {
            foreach (string stmt in SplitStatements(text))
            {
                var m = Statement.Match(stmt);
                if (!m.Success) throw new PageException($"Expected an assignment like 'x = 3' or '$x += 1', but got: {stmt.Trim()}");
                string name = m.Groups[1].Value, op = m.Groups[2].Value;
                object value = Eval(m.Groups[3].Value, scope, state);
                if (op != "=")
                {
                    if (!scope.TryGet(name, out var current)) throw new PageException($"${name} is not defined, so '{op}' can't update it.");
                    value = Combine(current, op, value);
                }
                if (declare) scope.Declare(name, value);
                else scope.Assign(name, value);
            }
        }

        private static object Combine(object current, string op, object value)
        {
            switch (op)
            {
                case "??=": return current ?? value;
                case "+=":
                    if (current is double a && value is double b) return a + b;
                    if (current is List<object> list) return value is List<object> more ? list.Concat(more).ToList() : list.Append(value).ToList();
                    if (current is string || value is string) return PageValues.ToStr(current) + PageValues.ToStr(value);
                    return PageValues.ToNumber(current, "+=") + PageValues.ToNumber(value, "+=");
                case "-=": return PageValues.ToNumber(current, "-=") - PageValues.ToNumber(value, "-=");
                case "*=": return PageValues.ToNumber(current, "*=") * PageValues.ToNumber(value, "*=");
                case "/=":
                    double d = PageValues.ToNumber(value, "/=");
                    if (d == 0) throw new PageException("Division by zero in '/='.");
                    return PageValues.ToNumber(current, "/=") / d;
            }
            throw new PageException($"Unknown assignment operator '{op}'.");
        }

        private static IEnumerable<string> SplitStatements(string text)
        {
            int depth = 0, start = 0;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if ((c == ';' || c == '\n') && depth == 0)
                {
                    string part = text.Substring(start, i - start);
                    if (part.Trim().Length > 0) yield return part;
                    start = i + 1;
                }
            }
            string last = text.Substring(start);
            if (last.Trim().Length > 0) yield return last;
        }

        internal enum EscapeMode { Html, Markdown, None }

        /// <summary>Replaces <c>{ expr }</c> in text. <c>{{</c> and <c>}}</c> are literal braces; <c>{! expr }</c> is not escaped.</summary>
        internal static string Interpolate(string text, PageScope scope, PageContext ctx, EscapeMode mode)
        {
            if (text.IndexOf('{') < 0 && text.IndexOf('}') < 0) return text;
            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{' && i + 1 < text.Length && text[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { sb.Append('}'); i += 2; continue; }
                if (c == '{')
                {
                    int end = FindClose(text, i + 1);
                    if (end < 0) throw new PageException($"Missing '}}' to close '{{' in: {Snippet(text, i)}. Write {{{{ for a literal brace.");
                    string expr = text.Substring(i + 1, end - i - 1);
                    bool raw = expr.StartsWith("!", StringComparison.Ordinal) && !expr.StartsWith("!=", StringComparison.Ordinal);
                    if (raw) expr = expr.Substring(1);
                    if (expr.Trim().Length == 0) throw new PageException($"Empty {{ }} in: {Snippet(text, i)}. Write {{{{ }}}} for literal braces.");
                    string value = PageValues.ToStr(PageEvaluator.Evaluate(expr, scope, ctx));
                    sb.Append(raw || mode == EscapeMode.None ? value : mode == EscapeMode.Markdown ? PageValues.MarkdownEscape(value) : PageValues.HtmlEscape(value));
                    i = end + 1;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string Snippet(string text, int at)
        {
            int start = Math.Max(0, at - 10);
            string s = text.Substring(start, Math.Min(50, text.Length - start)).Replace('\n', ' ').Trim();
            return s;
        }

        /// <summary>Finds the '}' that closes a '{' opened just before <paramref name="from"/>, skipping strings and nested braces.</summary>
        private static int FindClose(string text, int from)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = from; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\') i++;
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return -1;
        }
    }
}
