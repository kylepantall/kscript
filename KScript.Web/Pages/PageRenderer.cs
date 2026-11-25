using KScript.Syntax.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Runtime;

namespace KScript.Pages
{
    /// <summary>What a page produced: usually HTML, or a response set by &lt;respond&gt;.</summary>
    public sealed class PageResult
    {
        public int Status { get; set; } = 200;
        public string ContentType { get; set; } = "text/html; charset=utf-8";
        public string Body { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Set-Cookie header values, each sent as a header of its own.</summary>
        public List<string> Cookies { get; } = new List<string>();

        /// <summary>For static files: the file to send instead of <see cref="Body"/>.</summary>
        public string FilePath { get; set; }
    }

    /// <summary>Thrown by &lt;respond&gt; to stop rendering and send a specific response.</summary>
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
            SiteRoot = Path.GetFullPath(siteRoot ?? KScript.Runtime.WorkingFolder.Current);
            Request = request ?? new Dictionary<string, object>();
        }

        public string SiteRoot { get; }
        public Dictionary<string, object> Request { get; }

        public KScript.Runtime.ConfigValue StartupConfig { get; init; }
        public Dictionary<string, object> HostItems { get; } = new Dictionary<string, object>();

        /// <summary>Maximum evaluation steps for one request, so an endless loop can't hang the server.</summary>
        public long MaxSteps { get; set; } = 5_000_000;

        /// <summary>Set when re-rendering a &lt;live&gt; block for a browser event.</summary>
        public LiveEvent Live { get; set; }

        /// <summary>For a routed GET view, issues the proof required to replay its route in this session.</summary>
        public Func<string> RoutedLiveProof { get; set; }
        private string routeProof;
        internal string RouteProof => routeProof ??= RoutedLiveProof?.Invoke();
        internal string EntryFile { get; set; }
        internal List<string> LiveParents { get; } = new();
        internal List<object> LiveEffects { get; } = new();
        internal bool InLiveAction { get; set; }
        internal HashSet<string> LiveStreamTargets { get; set; }
        internal string LiveRedirect { get; set; }
        internal Dictionary<string, object> LiveDownload { get; set; }
        internal bool SkipLiveRender { get; set; }
        internal bool ContainsModelForm;
        internal int LiveCounter;
        internal Dictionary<(string File, int Line, int Offset, string Tag), int> LiveComponentOccurrences = new();
        internal int IncludeDepth;

        // A model form contains this session's CSRF token and may contain private record values.
        internal PageResult Finish(PageResult result)
        {
            if (ContainsModelForm || routeProof != null) result.Headers["Cache-Control"] = "private, no-store";
            return result;
        }

        private PageRuntime runtime;

        /// <summary>The KScript interpreter for this request, made when the page first needs it.</summary>
        internal PageRuntime Runtime => runtime ??= new PageRuntime(this);

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

        /// <summary>For paths the renderer builds itself (such as _components/name.html), which stay in the site.</summary>
        internal bool SiteFileExists(string path) => File.Exists(ResolveSitePath(path));

        private string componentsFolder;
        internal string ComponentsFolder => componentsFolder ??=
            Projects.ProjectConfig.FindAndLoad(SiteRoot)?.PathFor("components") ?? "_components";

        private readonly Dictionary<string, string> componentFiles = new(StringComparer.OrdinalIgnoreCase);
        internal string ComponentFile(string tag)
        {
            if (!componentFiles.TryGetValue(tag, out string file))
                componentFiles[tag] = file = KScript.Syntax.Markup.ComponentScript.FindFile(ComponentsFolder, tag,
                    SiteFileExists, path => ReadSiteFile(path, "component script"));
            return file;
        }
    }

    /// <summary>
    /// Renders KScript pages: HTML (or Markdown) files with KScript control elements written inline,
    /// <c>{ expression }</c> interpolation in text and attributes, layouts and components.
    /// </summary>
    public static class PageRenderer
    {
        private sealed record ComponentCall(string File, int Line, int Offset, string Tag, string LiveId);

        private sealed class RenderState
        {
            public PageContext Ctx;
            public string File;
            public bool Markdown;
            public string Slot;
            public IReadOnlyDictionary<string, object> Inputs;
            public Action<object> Loaded;
            public string Companion;

            /// <summary>Set after an empty slot on its own line, so the line's newline is dropped too.</summary>
            public bool SwallowNewline;

            /// <summary>Inside a &lt;live&gt; block: the block's scope, where its state lives.</summary>
            public Scope LiveScope;
            public string ActionScope;
            public Dictionary<string, object> BoundFields;
            public HashSet<string> VisibleActions;
            public HashSet<string> SelectedOptions;
            public HashSet<string> UploadFields;
            public HashSet<string> StreamTargets;
            public HashSet<string> LockedFields;
            public bool InPlaceholder;

            /// <summary>The file's packages and imported scripts.</summary>
            public Module Module;

            /// <summary>The file's top-level &lt;import&gt; elements, which run before the rest of it renders.</summary>
            public HashSet<PageElement> Imports;

            /// <summary>A state for nested content of the same file (a &lt;live&gt; or &lt;markdown&gt; body).</summary>
            public RenderState With(bool? markdown = null, Scope liveScope = null) => new RenderState
            {
                Ctx = Ctx, File = File, Markdown = markdown ?? Markdown, Slot = Slot, LiveScope = liveScope ?? LiveScope, Module = Module, Imports = Imports,
                ActionScope = ActionScope, BoundFields = BoundFields, VisibleActions = VisibleActions,
                SelectedOptions = SelectedOptions, UploadFields = UploadFields, StreamTargets = StreamTargets,
                LockedFields = LockedFields, InPlaceholder = InPlaceholder,
            };
        }

        /// <summary>Renders a page file (relative to the site folder): .html or .md.</summary>
        public static PageResult RenderFile(PageContext ctx, string relativePath, Dictionary<string, object> parameters = null)
            => RenderFile(ctx, relativePath, parameters, variables: null);

        /// <summary>
        /// Renders a page file with extra <paramref name="variables"/> declared for it, as a controller's
        /// <c>@view('people/show.html', person: $p)</c> does.
        /// </summary>
        public static PageResult RenderFile(PageContext ctx, string relativePath, Dictionary<string, object> parameters, IReadOnlyDictionary<string, object> variables)
        {
            ctx.EntryFile = relativePath;
            var scope = NewScope(ctx, parameters);
            if (variables != null)
            {
                foreach (var kv in variables) scope.Declare(kv.Key, kv.Value);
            }
            try
            {
                string html = RenderFileInScope(ctx, relativePath, scope, slot: null);
                if (ctx.Live != null && ctx.RoutedLiveProof != null) throw new LiveExpiredException();
                if (ctx.Live != null) throw new PageException($"There is no <live id=\"{ctx.Live.BlockId}\"> block on this page any more. Reload the page.");
                return ctx.Finish(new PageResult { Body = AddLiveRuntime(html.TrimStart()) });
            }
            catch (PageResponseSignal signal)
            {
                return ctx.Finish(signal.Result);
            }
        }

        /// <summary>Renders a page from a string. Used by tests and by tools that hold the template in memory.</summary>
        public static PageResult RenderString(string template, PageContext ctx = null, bool markdown = false, string fileName = "page.html")
        {
            ctx ??= new PageContext(KScript.Runtime.WorkingFolder.Current);
            var scope = NewScope(ctx, null);
            try
            {
                string html = markdown
                    ? RenderMarkdownSource(template, fileName, ctx, scope, null)
                    : RenderSource(template, 1, NewState(ctx, fileName, null, false), scope);
                return ctx.Finish(new PageResult { Body = AddLiveRuntime(html.TrimStart()) });
            }
            catch (PageResponseSignal signal)
            {
                return ctx.Finish(signal.Result);
            }
        }

        /// <summary>Live blocks and passkey buttons share one delegated browser runtime.</summary>
        private static string AddLiveRuntime(string html)
        {
            if (!html.Contains("data-ks-live=\"", StringComparison.Ordinal) && !html.Contains("data-ks-passkey=\"", StringComparison.Ordinal)
                && !html.Contains("data-ks-autosave=\"", StringComparison.Ordinal) &&
                !html.Contains(" live-navigate", StringComparison.OrdinalIgnoreCase)) return html;
            const string tag = "<script src=\"/_ks/live.js\" defer></script>";
            int body = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            return body >= 0 ? html.Insert(body, tag + "\n") : html + "\n" + tag;
        }

        private static Scope NewScope(PageContext ctx, Dictionary<string, object> parameters)
        {
            var globals = new Scope();
            var request = ctx.Request;
            if (parameters != null) request["params"] = parameters.ToDictionary(kv => kv.Key, kv => kv.Value);
            globals.Declare("request", request);
            return globals.Child();
        }

        private static string RenderFileInScope(PageContext ctx, string relativePath, Scope scope, string slot,
            IReadOnlyDictionary<string, object> inputs = null, string companion = null, Action<object> loaded = null,
            ComponentCall component = null)
        {
            if (++ctx.IncludeDepth > 32) throw new PageException($"Layouts, components or includes are nested more than 32 deep (at {relativePath}). Does one include itself?");
            try
            {
                string text = ctx.ReadSiteFile(relativePath, "page");
                string name = relativePath.Replace('\\', '/').TrimStart('/');
                if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    return RenderMarkdownSource(text, name, ctx, scope, slot, inputs, companion, loaded, component);
                return RenderSource(text, 1, NewState(ctx, name, slot, false, inputs, companion, loaded), scope, component);
            }
            finally
            {
                ctx.IncludeDepth--;
            }
        }

        private static string RenderMarkdownSource(string text, string name, PageContext ctx, Scope scope, string slot,
            IReadOnlyDictionary<string, object> inputs = null, string companion = null, Action<object> loaded = null,
            ComponentCall component = null)
        {
            var (meta, body, bodyLine) = PageMarkdown.SplitFrontMatter(text);
            var pageScope = scope.Child();
            foreach (var kv in meta) pageScope.Declare(kv.Key, kv.Value);
            pageScope.Declare("page", meta);

            string markdown = RenderSource(body, bodyLine, NewState(ctx, name, slot, true, inputs, companion, loaded), pageScope, component);
            string html = PageMarkdown.ToHtml(markdown);

            if (meta.TryGetValue("layout", out var layout) && layout != null)
            {
                var layoutInputs = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var entry in meta) layoutInputs[TemplateContract.VariableName(entry.Key)] = entry.Value;
                return RenderFileInScope(ctx, Values.ToStr(layout), pageScope, html, layoutInputs);
            }
            return html;
        }

        private static RenderState NewState(PageContext ctx, string file, string slot, bool markdown,
            IReadOnlyDictionary<string, object> inputs = null, string companion = null, Action<object> loaded = null)
        {
            string full;
            try { full = ctx.ResolveSitePath(file); }
            catch (PageException) { full = Path.Combine(ctx.SiteRoot, file); }
            return new RenderState
            {
                Ctx = ctx, File = file, Slot = slot, Markdown = markdown, Module = ctx.Runtime.ModuleFor(file, full),
                Inputs = inputs, Companion = companion, Loaded = loaded,
            };
        }

        private static string RenderSource(string text, int firstLine, RenderState state, Scope scope, ComponentCall component = null,
            IReadOnlySet<string> classFields = null, string css = null)
        {
            List<PageNode> nodes;
            try
            {
                nodes = PageTemplate.Parse(text, firstLine, state.Markdown);
            }
            catch (PageException ex)
            {
                ex.Location ??= state.File;
                throw;
            }
            CheckInputs(nodes, scope, state);
            if (state.Companion != null || state.Loaded != null)
            {
                var props = new Dictionary<string, object>(state.Inputs ?? new Dictionary<string, object>(), StringComparer.Ordinal);
                var exports = scope.Child();
                if (state.Companion != null) state.Ctx.Runtime.ImportCompanion(state.Module, state.Companion, exports);
                object data = exports.HasOwn("load") && exports.TryGet("load", out object hook) && hook is ICallable load
                    ? state.Ctx.Runtime.Call(load, props, exports, state.Module)
                    : null;
                scope = exports.Child();
                scope.Declare("props", props);
                scope.Declare("data", data);
                state.Loaded?.Invoke(data);
            }

            // <import> elements at the top of the file run first, so everything in the file can use them.
            state.Imports = new HashSet<PageElement>(ReferenceEqualityComparer.Instance);
            foreach (var import in nodes.OfType<PageElement>().Where(e => e.Name == "import"))
            {
                state.Imports.Add(import);
                try
                {
                    state.Ctx.Runtime.Import(state.Module, import, scope, state.Ctx, state.File);
                }
                catch (PageException ex)
                {
                    ex.Location ??= $"{state.File}:{import.Line}";
                    throw;
                }
            }

            var sb = new StringBuilder();
            var explicitRoot = nodes.OfType<PageElement>().FirstOrDefault();
            PageText style = string.IsNullOrWhiteSpace(css) ? null : new PageText { Raw = true, Text = "<style>" + css + "</style>" };
            if (component != null && classFields?.Count > 0 && explicitRoot?.Name == "live" &&
                nodes.All(n => n == explicitRoot || n is PageText t && string.IsNullOrWhiteSpace(t.Text)))
            {
                if (style != null) explicitRoot.Children.Insert(0, style);
                RenderLive(explicitRoot, scope, sb, state, component.LiveId ?? (explicitRoot.HasAttr("id") ? null : ComponentLiveId(component, state.Ctx)), classFields);
            }
            else if (component != null && (component.LiveId != null || UsesLiveFeatures(nodes, state.Ctx)))
            {
                var live = new PageElement { Name = "live", Line = firstLine };
                if (style != null) live.Children.Add(style);
                live.Children.AddRange(nodes);
                RenderLive(live, scope, sb, state, ComponentLiveId(component, state.Ctx), classFields);
            }
            else
            {
                if (style != null) sb.Append(style.Text);
                RenderNodes(nodes, scope, sb, state);
            }
            return sb.ToString();
        }

        private static bool UsesLiveFeatures(IEnumerable<PageNode> nodes, PageContext ctx) =>
            nodes.OfType<PageElement>().Any(el => el.Name != "live" &&
                (el.Name == "state" ||
                 (!PageTemplate.ControlElements.Contains(el.Name) &&
                  el.Attributes.Any(attr => TemplateContract.NeedsLiveOwner(attr.Name)) &&
                  (!PageElementNames.CanBeComponent(el.Name) || ctx.ComponentFile(el.Name) == null)) ||
                 UsesLiveFeatures(el.Children, ctx)));

        private static string ComponentLiveId(ComponentCall component, PageContext ctx)
        {
            if (component.LiveId != null) return component.LiveId;

            var site = (component.File, component.Line, component.Offset, component.Tag);
            int occurrence = ctx.LiveComponentOccurrences.GetValueOrDefault(site) + 1;
            ctx.LiveComponentOccurrences[site] = occurrence;
            string identity = FormattableString.Invariant(
                $"{string.Join("\0", ctx.LiveParents)}\0{component.File}\0{component.Line}\0{component.Offset}\0{component.Tag}\0{occurrence}");
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            return "component-" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
        }

        private static void CheckInputs(List<PageNode> nodes, Scope scope, RenderState state)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in nodes.OfType<PageElement>().Where(e => e.Name == "prop"))
            {
                string name = prop.Attr("name");
                string where = $"{state.File}:{prop.Line}";
                if (!TemplateContract.IsPropName(name))
                    throw new PageException("Write a prop name such as title or page-title.") { Location = where };
                string key = TemplateContract.VariableName(name);
                if (!names.Add(key))
                    throw new PageException($"The prop '{name}' is declared more than once.") { Location = where };
                object value = null;
                bool supplied = state.Inputs?.TryGetValue(key, out value) == true;
                if (TemplateContract.IsRequired(prop.HasAttr("required"), prop.Attr("required")) && (!supplied || value == null))
                    throw new PageException($"Pass the required '{name}' prop to {state.File}.") { Location = where };
                scope.Declare(key, value);
            }
        }

        private static void RenderNodes(List<PageNode> nodes, Scope scope, StringBuilder sb, RenderState state)
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
                            sb.Append(Interpolate(t, scope, state, state.Markdown ? EscapeMode.Markdown : EscapeMode.Html));
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
                catch (KScriptException ex)
                {
                    throw new PageException(ex.Message, ex) { Location = $"{state.File}:{node.Line}" };
                }
            }
        }

        private static bool Test(PageElement el, Scope scope, RenderState state)
        {
            string test = el.Attr("test") ?? throw new PageException($"<{el.Name}> needs a condition, e.g. <{el.Name} $count gt 0>.");
            return Values.Truthy(Eval(test, scope, state));
        }

        private static object Eval(string expr, Scope scope, RenderState state) => state.Ctx.Runtime.Evaluate(expr, scope, state.Module);

        private static void RenderElement(PageElement el, Scope scope, StringBuilder sb, RenderState state)
        {
            switch (el.Name)
            {
                case "let":
                case "set":
                    Assign(el, scope, state, declare: el.Name == "let");
                    return;

                case "do":
                    state.Ctx.Runtime.Execute(InnerText(el), scope, state.Module, declare: false);
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

                case "form" when el.HasAttr("model-update"):
                    RenderModelForm(el, scope, sb, state);
                    return;

                case "match":
                    RenderMatch(el, scope, sb, state);
                    return;

                case "case":
                case "default":
                    throw new PageException($"<{el.Name}> must be inside <match>.");

                case "import":
                    if (state.Imports != null && state.Imports.Contains(el)) return;
                    throw new PageException("<import> must be at the top level of the page, not inside another element.");

                case "live":
                    RenderLive(el, scope, sb, state);
                    return;

                case "state":
                    // Handled by the enclosing <live>.
                    if (state.LiveScope == null) throw new PageException("<state> must be inside a <live> block.");
                    return;

                case "placeholder":
                    throw new PageException("<placeholder> belongs directly inside a lazy or deferred <live> block.");

                case "layout":
                {
                    string src = el.Attr("path") ?? throw new PageException(el.HasAttr("src") ? "<layout src> was renamed: write <layout path=\"_layout.html\">." : "<layout> needs a path attribute, e.g. <layout path=\"_layout.html\">.");
                    var inner = scope.Child();
                    var body = new StringBuilder();
                    RenderNodes(el.Children, inner, body, state);
                    var layoutScope = scope.Child();
                    var inputs = BindTemplateInputs(el, scope, layoutScope, state);
                    AppendIndented(sb, RenderFileInScope(state.Ctx, Interpolate(src, scope, state, EscapeMode.None), layoutScope, body.ToString(), inputs), state);
                    return;
                }

                case "prop":
                    return;

                case "slot":
                    if (state.Slot != null) AppendIndented(sb, state.Slot, state);
                    else RenderNodes(el.Children, scope.Child(), sb, state);
                    return;

                case "include":
                {
                    string src = el.Attr("path") ?? throw new PageException(el.HasAttr("src") ? "<include src> was renamed: write <include path=\"...\" />." : "<include> needs a path attribute.");
                    var incScope = scope.Child();
                    var inputs = BindTemplateInputs(el, scope, incScope, state);
                    AppendIndented(sb, RenderFileInScope(state.Ctx, Interpolate(src, scope, state, EscapeMode.None), incScope, null, inputs), state);
                    return;
                }

                case "markdown":
                {
                    var md = new StringBuilder();
                    RenderNodes(el.Children, scope.Child(), md, state.With(markdown: true));
                    sb.Append(PageMarkdown.ToHtml(PageMarkdown.Dedent(md.ToString())));
                    return;
                }

                case "respond":
                {
                    var result = new PageResult();
                    if (el.Attr("status") is string status) result.Status = (int)Values.ToNumber(Eval(status, scope, state), "<respond status>");
                    if (el.Attr("redirect") is string redirect)
                    {
                        if (el.Attr("status") == null) result.Status = 302;
                        result.Body = string.Empty;
                        result.Headers["Location"] = Interpolate(redirect, scope, state, EscapeMode.None);
                    }
                    else if (el.Attr("json") is string json)
                    {
                        result.ContentType = "application/json; charset=utf-8";
                        result.Body = Values.ToJson(Eval(json, scope, state), indented: true);
                    }
                    else
                    {
                        var body = new StringBuilder();
                        RenderNodes(el.Children, scope.Child(), body, state);
                        result.Body = body.ToString().Trim();
                        if (el.Attr("type") is string type) result.ContentType = Interpolate(type, scope, state, EscapeMode.None);
                    }
                    foreach (var a in el.Attributes.Where(a => a.Name.StartsWith("header-", StringComparison.Ordinal)))
                        result.Headers[a.Name.Substring(7)] = Interpolate(a.Value ?? string.Empty, scope, state, EscapeMode.None);
                    throw new PageResponseSignal(result);
                }
            }

            // One way to end a request from a page: <respond>. The old element says so instead of becoming HTML.
            if (el.Name == "redirect" && el.Attr("to") is string oldTo)
                throw new PageException($"<redirect> was removed. Use <respond redirect=\"{oldTo}\" />.");

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

        private static Dictionary<string, object> BindTemplateInputs(PageElement element, Scope caller, Scope target, RenderState state)
        {
            var inputs = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (PageAttribute attribute in element.Attributes.Where(a => a.Name != "path"))
            {
                string name = TemplateContract.VariableName(attribute.Name);
                object value = AttributeValue(attribute, caller, state);
                inputs[name] = value;
                target.Declare(name, value);
            }
            return inputs;
        }

        private static bool TryComponent(PageElement el, Scope scope, StringBuilder sb, RenderState state)
        {
            string path = state.Ctx.ComponentFile(el.Name);
            if (path == null) return false;

            var body = new StringBuilder();
            RenderNodes(el.Children, scope.Child(), body, state);

            // Components only see their own attributes (and $request), not the variables of the page using them.
            var root = scope;
            while (root.Parent != null) root = root.Parent;
            var componentScope = root.Child();
            var inputs = new Dictionary<string, object>(StringComparer.Ordinal);
            Action<object> loaded = null;
            string liveId = null;
            foreach (var a in el.Attributes)
            {
                if (a.Name == "live-id")
                {
                    liveId = Values.ToStr(AttributeValue(a, scope, state));
                    continue;
                }
                object value = AttributeValue(a, scope, state);
                if (TemplateContract.IsLoadCallback(a.Name))
                {
                    if (value is not ICallable callback) throw new PageException("onLoad needs a function such as {fn(data) => @note_loaded($data)}.");
                    if (loaded != null) throw new PageException("Supply one onLoad callback per component.");
                    loaded = data => state.Ctx.Runtime.Call(callback, data, scope, state.Module);
                    continue;
                }
                string name = TemplateContract.VariableName(a.Name);
                inputs[name] = value;
                componentScope.Declare(name, value);
            }

            var call = new ComponentCall(state.File, el.Line, el.Offset, el.Name, liveId);
            if (global::KScript.Syntax.Markup.ScriptFiles.IsScript(path))
            {
                if (loaded != null) throw new PageException("onLoad is available on components with companion load functions.");
                state.Ctx.IncludeDepth++;
                try
                {
                    if (state.Ctx.IncludeDepth > 32)
                        throw new PageException($"Components are nested more than 32 deep (at {path}). Does one include itself?");
                    var component = state.Ctx.Runtime.RenderComponent(state.Ctx, path, el.Name, inputs, out var classFields, out string css);
                    var componentState = NewState(state.Ctx, path, body.ToString(), false);
                    componentState.Module = component.Module;
                    var templateScope = component.Scope.Child();
                    if (!templateScope.TryGet("request", out object request) || request == null)
                        templateScope.Declare("request", state.Ctx.Request);
                    AppendIndented(sb, RenderSource(component.Html, component.FirstLine, componentState, templateScope, call, classFields, css), state);
                }
                finally
                {
                    state.Ctx.IncludeDepth--;
                }
            }
            else
            {
                string companion = TemplateContract.CompanionPath(path);
                if (companion != null && !state.Ctx.SiteFileExists(companion)) companion = null;
                AppendIndented(sb, RenderFileInScope(state.Ctx, path, componentScope, body.ToString(), inputs,
                    companion == null ? null : state.Ctx.ResolveSitePath(companion, "component script"), loaded, call), state);
            }
            return true;
        }

        private static void RenderModelForm(PageElement el, Scope scope, StringBuilder sb, RenderState state)
        {
            state.Ctx.ContainsModelForm = true;
            string action = ModelFormPath.For(el.Attr("model-update"));
            string key = el.Attr("key") ?? throw new PageException("<form model-update> needs the record's key.");
            if (el.HasAttr("action") || el.HasAttr("method"))
                throw new PageException("<form model-update> chooses its POST action from its binding.");

            var form = new PageElement { Name = "form", Line = el.Line };
            form.Attributes.Add(new PageAttribute { Name = "method", Value = "post" });
            form.Attributes.Add(new PageAttribute { Name = "action", Value = action });
            form.Attributes.Add(new PageAttribute { Name = "data-ks-model-form" });
            if (el.HasAttr("autosave"))
            {
                string value = el.Attr("autosave");
                object delay = value == null ? true : TryWholeExpression(value, out string expr, out _) ? Eval(expr, scope, state) : value;
                if (delay is not (null or false))
                {
                    double ms = delay is true ? 500 : Duration.Seconds(delay, "autosave") * 1000;
                    if (!double.IsFinite(ms) || ms < 0 || ms > int.MaxValue)
                        throw new PageException("autosave needs a nonnegative delay within the browser timer range.");
                    form.Attributes.Add(new PageAttribute { Name = "data-ks-autosave", Value = Values.ToStr(Math.Round(ms)) });
                }
            }
            form.Attributes.AddRange(el.Attributes.Where(a => a.Name is not ("key" or "model-update" or "autosave")));
            var request = state.Ctx.Request;
            string back = request.TryGetValue("url", out object url) || request.TryGetValue("path", out url) ? Values.ToStr(url) : "/";
            string Hidden(string name, string value) => $"<input type=\"hidden\" name=\"{name}\" value=\"{Values.HtmlEscape(value)}\">";
            form.Children.Add(new PageText
            {
                Raw = true,
                Text = Hidden("_key", Interpolate(key, scope, state, EscapeMode.None))
                    + Hidden("_token", state.Ctx.Runtime.CsrfToken()) + Hidden("_return", back),
            });
            form.Children.AddRange(el.Children);
            form.Children.Add(new PageText { Raw = true, Text = "<span data-ks-save-status role=\"status\"></span>" });
            RenderHtmlElement(form, scope, sb, state);
        }

        private static void RenderHtmlElement(PageElement el, Scope scope, StringBuilder sb, RenderState state)
        {
            sb.Append('<').Append(el.SourceName ?? el.Name);
            object bound = null;
            bool hasBinding = el.HasAttr("bind");
            bool option = el.Name == "option" && state.SelectedOptions != null;
            string inputType = el.Name == "input" ? Values.ToStr(el.Attr("type")).ToLowerInvariant() : null;
            bool choice = inputType is "checkbox" or "radio";
            bool passkeyButton = el.HasAttr("passkey");
            if (passkeyButton)
            {
                if (el.Name != "button" || el.HasAttr("type") && !string.Equals(el.Attr("type"), "button", StringComparison.OrdinalIgnoreCase))
                    throw new PageException("Use passkey on a <button type=\"button\"> or leave out its type.");
                if (!el.HasAttr("type")) sb.Append(" type=\"button\"");
            }
            bool hasHandler = false;
            foreach (var a in el.Attributes)
            {
                if (a.Name == "passkey")
                {
                    string action = Values.ToStr(AttributeValue(a, scope, state));
                    if (action is not ("login" or "register" or "signup")) throw new PageException("passkey must be login, register or signup.");
                    sb.Append(" data-ks-passkey=\"").Append(action).Append('"');
                    continue;
                }
                if (a.Name == "passkey-redirect" && passkeyButton)
                {
                    sb.Append(" data-ks-passkey-redirect=\"").Append(Values.HtmlEscape(Values.ToStr(AttributeValue(a, scope, state)))).Append('"');
                    continue;
                }
                if (a.Name.StartsWith("on-", StringComparison.Ordinal))
                {
                    if (state.LiveScope == null || a.HandlerIndex < 0)
                        throw new PageException($"{a.Name} only works inside a <live> block, e.g. <live id=\"counter\">…</live>.");
                    sb.Append(" data-ks-").Append(a.Name).Append("=\"").Append(a.HandlerIndex).Append('"');
                    if (state.ActionScope != null)
                    {
                        var locals = LocalVariables(scope, state.LiveScope);
                        string proof = PageLive.Sign(locals, HandlerScope(state.ActionScope, a));
                        if (state.Ctx.RouteProof == null) proof = FileLiveToken(proof);
                        state.VisibleActions?.Add(proof);
                        sb.Append(" data-ks-args-").Append(a.HandlerIndex).Append("=\"").Append(Values.HtmlEscape(proof)).Append('"');
                    }
                    else hasHandler = true;
                    continue;
                }
                if (a.Name == "every" && el.HasAttr("on-tick"))
                {
                    // <span on-tick="..." every="200ms">: how often the tick handler runs. A plain number is seconds.
                    double ms = Duration.Seconds(TryWholeExpression(a.Value ?? string.Empty, out string everyExpr, out _) ? Eval(everyExpr, scope, state) : a.Value, "every") * 1000;
                    sb.Append(" data-ks-every=\"").Append(Values.ToStr(Math.Max(16, Math.Round(ms)))).Append('"');
                    continue;
                }
                if (a.Name == "bind")
                {
                    if (state.LiveScope == null) throw new PageException("bind only works inside a <live> block.");
                    string name = (a.Value ?? string.Empty).Trim().TrimStart('$');
                    string[] path = name.Split('.');
                    if (state.LockedFields?.Contains(path[0]) == true)
                        throw new PageException($"State ${name} is locked. A browser input cannot bind to it.");
                    if (state.BoundFields != null) state.BoundFields[name] = true;
                    if (!TryBinding(state.LiveScope, path, out bound, out _))
                        throw new PageException($"bind=\"{name}\": ${name} is not defined. Declare ${path[0]} as live state and define its bound fields.");
                    sb.Append(" data-ks-bind=\"").Append(Values.HtmlEscape(name)).Append('"');
                    if (inputType == "checkbox")
                    {
                        if (Values.Truthy(bound)) sb.Append(" checked");
                    }
                    else if (inputType == "radio")
                    {
                        if (bound != null && Values.ToStr(bound) == InputChoiceValue(el, scope, state)) sb.Append(" checked");
                    }
                    else if (el.Name != "textarea" && el.Name != "select")
                    {
                        sb.Append(" value=\"").Append(Values.HtmlEscape(Values.ToStr(bound))).Append('"');
                    }
                    if (!el.HasAttr("name")) sb.Append(" name=\"").Append(Values.HtmlEscape(name)).Append('"');
                    continue;
                }
                if (a.Name == "bind-on")
                {
                    if (!hasBinding || !LiveAttributes.Get("bind-on").Options.Contains(a.Value))
                        throw new PageException("Use bind-on with bind and choose input, change, blur or action.");
                    sb.Append(" data-ks-bind-on=\"").Append(a.Value).Append('"');
                    continue;
                }
                if (a.Name == "live-upload")
                {
                    if (state.LiveScope == null || inputType != "file" || el.Attr("name") is not string field)
                        throw new PageException("Use live-upload on a named file input inside a <live> block.");
                    state.UploadFields?.Add(Interpolate(field, scope, state, EscapeMode.None));
                    sb.Append(" data-ks-live-upload");
                    continue;
                }
                if (a.Name == "sort-item")
                {
                    if (state.LiveScope == null) throw new PageException("sort-item only works inside a <live> block.");
                    string key = Values.ToStr(AttributeValue(a, scope, state));
                    if (string.IsNullOrEmpty(key)) throw new PageException("sort-item needs a nonempty item key.");
                    sb.Append(" data-ks-sort-item=\"").Append(Values.HtmlEscape(key)).Append('"');
                    if (!el.HasAttr("draggable")) sb.Append(" draggable=\"true\"");
                    if (!el.HasAttr("tabindex")) sb.Append(" tabindex=\"0\"");
                    continue;
                }
                if (a.Name is "live-stream" or "live-portal" or "live-action" or "live-client")
                {
                    if (state.LiveScope == null) throw new PageException($"{a.Name} only works inside a <live> block.");
                    if (a.Name == "live-portal" && el.Name != "template")
                        throw new PageException("Use live-portal on a <template>.");
                    if (a.Name == "live-action" && !el.HasAttr("on-action"))
                        throw new PageException("A live-action name needs an on-action handler.");
                    string directiveValue = Values.ToStr(AttributeValue(a, scope, state));
                    if (string.IsNullOrWhiteSpace(directiveValue)) throw new PageException($"{a.Name} needs a value.");
                    if (a.Name == "live-stream") state.StreamTargets?.Add(directiveValue);
                    sb.Append(" data-ks-").Append(a.Name).Append("=\"").Append(Values.HtmlEscape(directiveValue)).Append('"');
                    continue;
                }
                if (a.Name.StartsWith("live-use-", StringComparison.Ordinal) && state.LiveScope == null)
                    throw new PageException($"{a.Name} needs a live component or a <live> block.");
                if (a.Name is "live-loading" or "live-offline" or "live-disable" or "live-confirm" or "live-key" or "live-replace"
                    or "live-dirty" or "live-cloak" or "live-ignore" or "live-error")
                {
                    if (state.LiveScope == null) throw new PageException($"{a.Name} only works inside a <live> block.");
                    sb.Append(" data-ks-").Append(a.Name);
                    if (a.Name is "live-confirm" or "live-key" || a.Name == "live-dirty" && a.Value != null)
                        sb.Append("=\"").Append(Values.HtmlEscape(Values.ToStr(AttributeValue(a, scope, state)))).Append('"');
                    continue;
                }
                if (option && a.Name == "selected" || hasBinding && choice && a.Name == "checked" ||
                    hasBinding && inputType != null && !choice && a.Name == "value") continue;
                if (a.Value == null) { sb.Append(' ').Append(a.SourceName ?? a.Name); continue; }

                if (TryWholeExpression(a.Value, out string expr, out bool raw))
                {
                    object v = Eval(expr, scope, state);
                    if (v == null || v is bool b && !b) continue;
                    if (v is bool) { sb.Append(' ').Append(a.SourceName ?? a.Name); continue; }
                    string s = Values.ToStr(v);
                    sb.Append(' ').Append(a.SourceName ?? a.Name).Append("=\"").Append(raw ? s.Replace("\"", "&quot;") : Values.HtmlEscape(s)).Append('"');
                    continue;
                }

                string value = Interpolate(a.Value, scope, state, EscapeMode.Html);
                sb.Append(' ').Append(a.SourceName ?? a.Name).Append("=\"").Append(value.Replace("\"", "&quot;")).Append('"');
            }

            if (hasHandler)
            {
                var locals = LocalVariables(scope, state.LiveScope);
                if (locals.Count > 0) sb.Append(" data-ks-args=\"").Append(Values.HtmlEscape(PageLive.Sign(locals))).Append('"');
            }

            if ((el.HasAttr("live-loading") || el.HasAttr("live-offline") || el.HasAttr("live-dirty") || el.HasAttr("live-cloak") ||
                el.HasAttr("live-error")) &&
                !el.HasAttr("hidden")) sb.Append(" hidden");
            if (option && state.SelectedOptions.Contains(OptionValue(el, scope, state))) sb.Append(" selected");
            if (el.Void) { sb.Append('>'); return; }
            sb.Append('>');
            if (el.Name == "textarea" && hasBinding)
            {
                sb.Append(Values.HtmlEscape(Values.ToStr(bound))).Append("</textarea>");
                return;
            }
            var childrenState = state;
            if (el.Name == "select" && hasBinding)
            {
                childrenState = state.With();
                childrenState.SelectedOptions = bound is List<object> values
                    ? values.Select(Values.ToStr).ToHashSet(StringComparer.Ordinal)
                    : bound == null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal) { Values.ToStr(bound) };
            }
            RenderNodes(el.Children, scope.Child(), sb, childrenState);
            sb.Append("</").Append(el.SourceName ?? el.Name).Append('>');
            if (passkeyButton) sb.Append("<span role=\"status\" data-ks-passkey-status></span>");
        }

        private static string InputChoiceValue(PageElement element, Scope scope, RenderState state) =>
            element.Attr("value") is string value ? Interpolate(value, scope, state, EscapeMode.None) : "on";

        private static string OptionValue(PageElement element, Scope scope, RenderState state) =>
            element.Attr("value") is string value ? Interpolate(value, scope, state, EscapeMode.None) :
                string.Join(" ", string.Concat(element.Children.OfType<PageText>().Select(text =>
                    Interpolate(text.Text, scope, state, EscapeMode.None))).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        private static bool TryBinding(Scope scope, string[] path, out object value, out Dictionary<string, object> owner)
        {
            owner = null;
            value = null;
            if (!scope.HasOwn(path[0]) || !scope.TryGet(path[0], out value)) return false;
            for (int i = 1; i < path.Length; i++)
            {
                owner = Values.Unwrap(value) as Dictionary<string, object>;
                if (owner == null || !owner.TryGetValue(path[i], out value)) return false;
            }
            return true;
        }

        /// <summary>Attribute values for layouts, includes and components: a whole <c>{expr}</c> passes the value itself.</summary>
        private static object AttributeValue(PageAttribute a, Scope scope, RenderState state)
        {
            if (a.Value == null) return true;
            if (TryWholeExpression(a.Value, out string expr, out _)) return Eval(expr, scope, state);
            return Interpolate(a.Value, scope, state, EscapeMode.None);
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

        private static bool RenderFor(PageElement el, Scope scope, StringBuilder sb, RenderState state)
        {
            string each = el.Attr("each") ?? throw new PageException("<for> needs each and in attributes, e.g. <for each=\"p\" in=\"$people\">.");
            string inExpr = el.Attr("in") ?? throw new PageException("<for> needs an in attribute, e.g. <for each=\"p\" in=\"$people\">.");
            string indexName = el.Attr("at");
            var names = each.Split(',').Select(n => n.Trim().TrimStart('$')).ToArray();

            object source = Eval(inExpr, scope, state);
            IEnumerable<(object item, object key)> items = source switch
            {
                null => Enumerable.Empty<(object, object)>(),
                List<object> list => list.Select(x => (x, (object)null)),
                Dictionary<string, object> map => map.Select(kv => (names.Length > 1 ? kv.Value : new Dictionary<string, object> { ["key"] = kv.Key, ["value"] = kv.Value }, (object)kv.Key)),
                double n => Enumerable.Range(0, Math.Max(0, (int)n)).Select(x => ((object)(double)x, (object)null)),
                string s => s.Select(ch => ((object)ch.ToString(), (object)null)),
                _ => throw new PageException($"<for> can't loop over {Values.Describe(source)}."),
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
        private static void RenderLive(PageElement el, Scope scope, StringBuilder sb, RenderState state, string inferredId = null,
            IReadOnlySet<string> classFields = null)
        {
            if (state.InPlaceholder) throw new PageException("A live placeholder cannot contain another live block.");
            var ctx = state.Ctx;
            int depth = ctx.LiveParents.Count;
            if (depth > 0 && inferredId == null && !el.HasAttr("id")) throw new PageException("A nested live component needs an explicit ID.");
            string id = inferredId ?? (el.Attr("id") is string explicitId ? Interpolate(explicitId, scope, state, EscapeMode.None) : $"live-{++ctx.LiveCounter}");
            if (string.IsNullOrWhiteSpace(id) || ctx.LiveParents.Contains(id, StringComparer.Ordinal) || depth == 32)
                throw new PageException("Live components need distinct, nonempty IDs and no more than 32 parents.");
            string proof = ctx.RouteProof;
            bool fileComponent = proof == null && classFields?.Count > 0;
            string ownerProof = fileComponent ? "file-component\0" + ctx.SiteRoot + "\0" + ctx.EntryFile + "\0" +
                (ctx.Request.GetValueOrDefault("path") as string ?? "/" + Path.ChangeExtension(ctx.EntryFile, null)) : proof;
            string actionScope = ownerProof == null ? null : ownerProof + "\0" + state.File + "\0" + id +
                (depth == 0 ? string.Empty : "\0parents\0" + string.Join("\0", ctx.LiveParents));
            bool lazy = TemplateContract.IsRequired(el.HasAttr("lazy"), el.Attr("lazy"));
            bool defer = TemplateContract.IsRequired(el.HasAttr("defer"), el.Attr("defer"));
            if (lazy && defer) throw new PageException("A live block uses either lazy or defer.");
            string loadMode = proof == null ? null : lazy ? "visible" : defer ? "defer" : null;
            var boundFields = actionScope == null ? null : new Dictionary<string, object>();
            Dictionary<string, object> previouslyBound = null;
            var handlers = PageLive.Handlers(el);
            var liveScope = scope.Child();
            bool target = ctx.Live != null && ctx.Live.BlockId == id;
            bool expectedParents = ctx.Live != null && ctx.Live.Parents.Count >= depth &&
                ctx.LiveParents.SequenceEqual(ctx.Live.Parents.Take(depth).Select(parent => parent.Id), StringComparer.Ordinal);
            if (target && (!expectedParents || depth != ctx.Live.Parents.Count)) throw new LiveExpiredException();

            Dictionary<string, object> restored = target ? ctx.Live.State : null;
            if (!target && expectedParents && ctx.Live.Parents.Count > depth && ctx.Live.Parents[depth].Id == id)
            {
                if (actionScope == null) throw new LiveExpiredException();
                var parentState = PageLive.Verify(ctx.Live.Parents[depth].State, actionScope + "\0state");
                if (parentState.GetValueOrDefault("pending") is true) throw new LiveExpiredException();
                restored = (Dictionary<string, object>)parentState["values"];
            }

            if (target && actionScope != null)
            {
                var stateProof = fileComponent ? ctx.Live.State.GetValueOrDefault("component-proof") as string : ctx.Live.StateToken;
                var envelope = PageLive.Verify(stateProof, actionScope + "\0state");
                bool pending = envelope.GetValueOrDefault("pending") is true;
                if (pending != (ctx.Live.Handler == -2) || pending && loadMode == null) throw new LiveExpiredException();
                ctx.Live.State = (Dictionary<string, object>)envelope["values"];
                restored = ctx.Live.State;
                previouslyBound = (Dictionary<string, object>)envelope["binds"];
                if (ctx.Live.Binds.Keys.Any(k => !previouslyBound.ContainsKey(k))) throw new LiveExpiredException();
            }

            bool waiting = loadMode != null && !target && restored == null;
            if (classFields?.Count > 0)
                classFields = classFields.Except(el.Children.OfType<PageElement>().Where(c => c.Name == "state")
                    .Select(c => c.Attr("name")?.TrimStart('$'))).ToHashSet(StringComparer.Ordinal);
            Record instance = classFields?.Count > 0 && scope.TryGet("this", out var self) ? self as Record : null;
            Record initial = instance?.Copy();
            var (stateNames, queryNames, sessionNames, locked) = DeclareLiveState(el, liveScope, state, actionScope, restored, waiting, instance, classFields);

            if (target)
            {
                if (ctx.Live.Handler >= handlers.Count || ctx.Live.Handler < -2 ||
                    ctx.Live.Handler == -2 && actionScope == null) throw new LiveExpiredException();
                if (ctx.Live.Files != null && (actionScope == null || ctx.Live.Handler < 0)) throw new LiveExpiredException();
                if (actionScope != null && (ctx.Live.Handler >= 0 || ctx.Live.Binds.Count > 0))
                {
                    if (ctx.Live.Handler >= 0)
                    {
                        var argsProof = fileComponent ? ctx.Live.Args.GetValueOrDefault("component-proof") as string : ctx.Live.ArgsToken;
                        ctx.Live.Args = PageLive.Verify(argsProof, HandlerScope(actionScope, handlers[ctx.Live.Handler]));
                    }
                    CheckVisibleControls(el, id, state, liveScope, stateNames, actionScope, locked, instance, classFields);
                }
                foreach (var kv in ctx.Live.Binds)
                {
                    string[] path = kv.Key.Split('.');
                    if (!stateNames.Contains(path[0]) || locked.Contains(path[0]) ||
                        !TryBinding(liveScope, path, out object old, out var owner)) continue;
                    bool typed = classFields?.Contains(path[0]) == true;
                    object next = !typed && kv.Value is string text && old is double && Values.TryNumber(text, out double number) ? number : kv.Value;
                    try
                    {
                        if (owner == null) liveScope.Assign(path[0], next);
                        else
                        {
                            if (typed && owner is Record nested) nested.Set(ctx.Runtime.Interpreter, path[^1], next);
                            else owner[path[^1]] = next;
                            if (typed) liveScope.Assign(path[0], instance[path[0]]);
                        }
                    }
                    catch (KScriptException error) when (typed)
                    {
                        throw new PageResponseSignal(new PageResult { Status = 422, ContentType = "text/plain; charset=utf-8", Body = error.Message });
                    }
                }

                if (ctx.Live.Handler >= 0)
                {
                    var handlerScope = liveScope.Child();
                    foreach (var kv in ctx.Live.Args) handlerScope.Declare(kv.Key, kv.Value);
                    ctx.Live.Event["files"] = ctx.Live.Files ?? new Dictionary<string, object>();
                    handlerScope.Declare("event", ctx.Live.Event);
                    ctx.InLiveAction = true;
                    try { ctx.Runtime.Execute(handlers[ctx.Live.Handler].Value ?? string.Empty, handlerScope, state.Module, declare: false); }
                    finally { ctx.InLiveAction = false; }
                    // A list or nested map can be changed in place inside an action without assigning its field.
                    if (instance != null)
                        foreach (string name in classFields)
                            if (instance[name] is List<object> or Dictionary<string, object> && !SameClassValue(initial[name], instance[name]))
                                liveScope.Assign(name, instance[name]);
                }
            }

            var body = new StringBuilder();
            var inner = state.With(liveScope: liveScope);
            inner.ActionScope = actionScope;
            inner.BoundFields = boundFields;
            inner.VisibleActions = null;
            inner.UploadFields = null;
            inner.StreamTargets = null;
            inner.LockedFields = locked;
            bool skipRender = target && ctx.SkipLiveRender;
            if (skipRender) boundFields = previouslyBound;
            else RenderLiveChildren(el, id, liveScope, body, inner, waiting);

            var values = new Dictionary<string, object>();
            foreach (var name in stateNames)
            {
                liveScope.TryGet(name, out var v);
                // The route supplies fresh props on every request. Only persist class fields changed by an action.
                if (instance != null && classFields.Contains(name) && restored?.ContainsKey(name) != true &&
                    SameClassValue(initial[name], v)) continue;
                if (v is ICallable) throw new PageException($"State ${name} holds a lambda, which can't be sent to the browser.");
                values[name] = v;
            }
            string tag = el.Attr("as") ?? "div";
            var html = new StringBuilder();
            html.Append('<').Append(tag).Append(" data-ks-live=\"").Append(Values.HtmlEscape(id)).Append('"');
            string stateToken = actionScope == null ? PageLive.Sign(values) : PageLive.Sign(
                new Dictionary<string, object> { ["values"] = values, ["binds"] = boundFields, ["pending"] = waiting }, actionScope + "\0state");
            if (fileComponent) stateToken = FileLiveToken(stateToken);
            if (proof != null) html.Append(" data-ks-route=\"").Append(Values.HtmlEscape(proof)).Append('"');
            html.Append(" data-ks-state=\"").Append(Values.HtmlEscape(stateToken)).Append('"');
            if (waiting) html.Append(" data-ks-live-lazy=\"").Append(loadMode).Append('"');
            if (skipRender) html.Append(" data-ks-live-preserve");
            if (target && ctx.LiveEffects.Count > 0)
                html.Append(" data-ks-events=\"").Append(Values.HtmlEscape(Values.ToJson(ctx.LiveEffects))).Append('"');
            if (target && ctx.LiveRedirect != null)
                html.Append(" data-ks-redirect=\"").Append(Values.HtmlEscape(ctx.LiveRedirect)).Append('"');
            if (target && ctx.LiveDownload != null)
                html.Append(" data-ks-download=\"").Append(Values.HtmlEscape(Values.ToJson(ctx.LiveDownload))).Append('"');
            if (queryNames.Count > 0)
            {
                var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var entry in queryNames)
                {
                    object value = values[entry.Key];
                    if (value is not (null or string or double or bool))
                        throw new PageException($"State ${entry.Key} must be text, a number, a boolean or null to sync with the URL.");
                    parameters.Add(entry.Value, value);
                }
                html.Append(" data-ks-query=\"").Append(Values.HtmlEscape(Values.ToJson(parameters))).Append('"');
            }
            foreach (var a in el.Attributes.Where(a => a.Name is not ("id" or "as" or "lazy" or "defer")))
            {
                if (a.Value == null) html.Append(' ').Append(a.SourceName ?? a.Name);
                else html.Append(' ').Append(a.SourceName ?? a.Name).Append("=\"").Append(Interpolate(a.Value, scope, state, EscapeMode.Html).Replace("\"", "&quot;")).Append('"');
            }
            html.Append(" id=\"").Append(Values.HtmlEscape(id)).Append("\">");
            if (inferredId != null && state.Markdown) html.Append(PageMarkdown.ToHtml(body.ToString()));
            else html.Append(body);
            html.Append("</").Append(tag).Append('>');

            if (target)
            {
                foreach (var entry in sessionNames)
                    ctx.Runtime.SetSessionState(entry.Value, values[entry.Key]);
                ctx.Live.Output = html.ToString();
                var result = new PageResult { Body = html.ToString() };
                if (proof != null) result.Headers["X-KScript-Fragment"] = "1";
                throw new PageResponseSignal(result);
            }
            sb.Append(html);
        }

        private static (List<string> Names, Dictionary<string, string> Queries, Dictionary<string, string> Sessions, HashSet<string> Locked)
            DeclareLiveState(PageElement el, Scope liveScope, RenderState state, string actionScope,
                Dictionary<string, object> restored, bool waiting, Record instance, IReadOnlySet<string> classFields)
        {
            var ctx = state.Ctx;
            var stateNames = new List<string>();
            var queryNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var sessionNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var locked = new HashSet<string>(StringComparer.Ordinal);
            if (waiting) return (stateNames, queryNames, sessionNames, locked);
            if (instance != null)
            {
                foreach (string name in classFields)
                {
                    stateNames.Add(name);
                    liveScope.BindField(instance, name, ctx.Runtime.Interpreter);
                    if (restored?.TryGetValue(name, out object previous) != true) continue;
                    try { liveScope.Assign(name, previous); }
                    catch (KScriptException) { throw new LiveExpiredException(); }
                }
            }
            foreach (var st in el.Children.OfType<PageElement>().Where(c => c.Name == "state"))
            {
                string name = (st.Attr("name") ?? throw new PageException("<state> needs a name, e.g. <state name=\"count\" value=\"0\" />.")).TrimStart('$');
                stateNames.Add(name);
                if (TemplateContract.IsRequired(st.HasAttr("locked"), st.Attr("locked"))) locked.Add(name);
                string session = st.Attr("session");
                if (st.HasAttr("session"))
                {
                    if (string.IsNullOrWhiteSpace(session) || session.Length > 120 || sessionNames.ContainsValue(session) ||
                        !sessionNames.TryAdd(name, session))
                        throw new PageException("Session-backed live state needs a unique key of 1–120 characters.");
                    if (ctx.RouteProof == null)
                        throw new PageException("Session-backed live state needs a page served by KScript HTTP.");
                }
                string query = st.Attr("query");
                if (query != null && (query.Length == 0 || query.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_') ||
                    queryNames.ContainsValue(query) || !queryNames.TryAdd(name, query)))
                    throw new PageException("Each live state query parameter needs a unique name using letters, digits, hyphens or underscores.");
                if (restored?.TryGetValue(name, out var sent) == true)
                {
                    liveScope.Declare(name, sent);
                    continue;
                }
                string expr = st.Attr("value") ?? (st.Children.OfType<PageText>().Any() ? string.Concat(st.Children.OfType<PageText>().Select(t => t.Text)) : null);
                object initial = expr == null || expr.Trim().Length == 0 ? null : Eval(expr, liveScope, state);
                if (session != null && ctx.Runtime.TryGetSessionState(session, out object stored)) initial = stored;
                if (query != null && ctx.Request.GetValueOrDefault("query") is Dictionary<string, object> parameters &&
                    parameters.TryGetValue(query, out object parameter))
                {
                    string text = Values.ToStr(parameter);
                    initial = initial switch
                    {
                        double when Values.TryNumber(text, out double number) && double.IsFinite(number) => number,
                        bool when bool.TryParse(text, out bool boolean) => boolean,
                        null or string => text,
                        _ => initial,
                    };
                }
                liveScope.Declare(name, initial);
            }
            return (stateNames, queryNames, sessionNames, locked);
        }

        // An old screen may contain controls or loop rows the current user can no longer see. Render its
        // bindings against today's server data before accepting input. Each loop action signs its current
        // locals. The isolated copy keeps template assignments during this check out of the next render.
        private static void RenderLiveChildren(PageElement el, string id, Scope scope, StringBuilder output, RenderState state, bool waiting = false)
        {
            var parents = state.Ctx.LiveParents;
            var occurrences = state.Ctx.LiveComponentOccurrences;
            state.Ctx.LiveComponentOccurrences = new();
            parents.Add(id);
            try
            {
                var placeholder = el.Children.OfType<PageElement>().FirstOrDefault(child => child.Name == "placeholder");
                if (waiting)
                {
                    if (placeholder == null) return;
                    var preview = state.With();
                    preview.InPlaceholder = true;
                    RenderNodes(placeholder.Children, scope.Child(), output, preview);
                }
                else
                {
                    var children = placeholder == null ? el.Children :
                        el.Children.Where(child => child is not PageElement { Name: "placeholder" }).ToList();
                    RenderNodes(children, scope, output, state);
                }
            }
            finally
            {
                parents.RemoveAt(parents.Count - 1);
                state.Ctx.LiveComponentOccurrences = occurrences;
            }
        }

        private static void CheckVisibleControls(PageElement el, string id, RenderState state, Scope liveScope, List<string> stateNames, string actionScope,
            HashSet<string> lockedFields, Record instance, IReadOnlySet<string> classFields)
        {
            var probeScope = liveScope.Child();
            var values = new Dictionary<string, object>();
            foreach (string name in stateNames)
            {
                if (instance != null && classFields.Contains(name)) continue;
                liveScope.TryGet(name, out object value);
                values[name] = value;
            }
            foreach (var kv in (Dictionary<string, object>)Values.ParseJson(Values.ToJson(values))) probeScope.Declare(kv.Key, kv.Value);
            if (instance != null)
            {
                var copy = instance.Copy();
                probeScope.Declare("this", copy);
                foreach (string name in classFields) probeScope.BindField(copy, name, state.Ctx.Runtime.Interpreter);
            }
            var probe = state.With(liveScope: probeScope);
            probe.ActionScope = actionScope;
            probe.BoundFields = new Dictionary<string, object>();
            probe.VisibleActions = new HashSet<string>(StringComparer.Ordinal);
            probe.UploadFields = new HashSet<string>(StringComparer.Ordinal);
            probe.StreamTargets = new HashSet<string>(StringComparer.Ordinal);
            probe.LockedFields = lockedFields;
            RenderLiveChildren(el, id, probeScope, new StringBuilder(), probe);
            var ev = state.Ctx.Live;
            if ((ev.Handler >= 0 && !probe.VisibleActions.Contains(ev.ArgsToken)) || ev.Binds.Keys.Any(k => !probe.BoundFields.ContainsKey(k)) ||
                ev.Files?.Keys.Any(k => !probe.UploadFields.Contains(k)) == true)
                throw new LiveExpiredException();
            state.Ctx.LiveStreamTargets = probe.StreamTargets;
        }

        private static string HandlerScope(string blockScope, PageAttribute handler) =>
            blockScope + "\0action:" + handler.HandlerIndex + "\0" + handler.Name + "\0" + handler.Value;

        // The file server authenticates its request before resolving the page. Carry the class-component proof
        // inside that existing signed envelope, then verify it against the resolved component and visible controls.
        private static string FileLiveToken(string proof) =>
            PageLive.Sign(new Dictionary<string, object> { ["component-proof"] = proof });

        // Server props can contain cycles, callbacks or opaque secrets. Compare in memory so an unchanged
        // value never needs to be serialised or compared through user-facing operators.
        private static bool SameClassValue(object previous, object current, HashSet<(object, object)> visited = null)
        {
            if (ReferenceEquals(previous, current)) return true;
            if (previous is Dictionary<string, object> map && current is Dictionary<string, object> otherMap)
            {
                if (map.Count != otherMap.Count) return false;
                visited ??= new();
                return !visited.Add((previous, current)) || map.All(pair => otherMap.TryGetValue(pair.Key, out var value) &&
                    SameClassValue(pair.Value, value, visited));
            }
            if (previous is List<object> list && current is List<object> otherList)
            {
                if (list.Count != otherList.Count) return false;
                visited ??= new();
                return !visited.Add((previous, current)) || list.Zip(otherList).All(pair => SameClassValue(pair.First, pair.Second, visited));
            }
            return Equals(previous, current);
        }

        /// <summary>Variables declared between a handler's element and its live block, e.g. the loop item.</summary>
        private static Dictionary<string, object> LocalVariables(Scope scope, Scope liveScope)
        {
            var locals = new Dictionary<string, object>();
            for (var s = scope; s != null && s != liveScope; s = s.Parent)
            {
                foreach (var kv in s.OwnVariables)
                {
                    if (kv.Value is ICallable || locals.ContainsKey(kv.Key)) continue;
                    locals[kv.Key] = kv.Value;
                }
            }
            return locals;
        }

        private static void RenderMatch(PageElement el, Scope scope, StringBuilder sb, RenderState state)
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
                    string isExpr = c.Attr("is"), when = c.Attr("when");
                    if (isExpr == null && when == null) throw new PageException("<case> needs an is or when attribute.");
                    hit = (isExpr == null || state.Ctx.Runtime.EvaluateList(isExpr, matchScope, state.Module).Any(o => Values.LooseEquals(o, value)))
                        && (when == null || Values.Truthy(Eval(when, matchScope, state)));
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

        private static void Assign(PageElement el, Scope scope, RenderState state, bool declare)
        {
            string name = el.Attr("name");
            if (name == null)
            {
                string statements = InnerText(el);
                if (statements.Trim().Length == 0) throw new PageException($"<{el.Name}> needs a name and value, e.g. <{el.Name} name=\"count\" value=\"0\" />, or a statement like <{el.Name}>count = 0</{el.Name}>.");
                state.Ctx.Runtime.Execute(statements, scope, state.Module, declare);
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
                value = state.Ctx.Runtime.Combine(current, op, value, state.Module);
            }

            if (declare) scope.Declare(name, value);
            else scope.Assign(name, value);
        }

        internal enum EscapeMode { Html, Markdown, None }

        /// <summary>Replaces <c>{ expr }</c> in text. <c>{{</c> and <c>}}</c> are literal braces; <c>{! expr }</c> is not escaped.</summary>
        private static string Interpolate(string text, Scope scope, RenderState state, EscapeMode mode)
        {
            if (text.IndexOf('{') < 0 && text.IndexOf('}') < 0) return text;
            Func<string, string> escape = mode switch
            {
                EscapeMode.Html => Values.HtmlEscape,
                EscapeMode.Markdown => PageMarkdown.Escape,
                _ => null,
            };
            return state.Ctx.Runtime.Interpolate(text, scope, state.Module, escape);
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
                    if (Quotes.IsEscape(text, i, text.Length, quote)) i++;
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
