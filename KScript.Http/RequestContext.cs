using KScript.Auth;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Http
{
    /// <summary>
    /// One request (or one listing of the routes): the route table the route files build, the group stack while they
    /// run, and the request itself. Kept in <see cref="KScriptHost.Items"/> under <see cref="HttpPackage.ContextKey"/>.
    /// </summary>
    internal sealed class RequestContext
    {
        internal sealed class Frame
        {
            public string Prefix = "/";
            public List<MiddlewareRef> Middleware = new List<MiddlewareRef>();
            public string NamePrefix = string.Empty;
            public bool? Api;

            /// <summary>
            /// Middleware names not looked up yet. They are resolved at the first route inside, after the file's
            /// imports have run, so <c>&lt;KScript.Routes middleware="my_check"&gt;</c> can use an imported function.
            /// </summary>
            public string Pending;
        }

        private readonly List<Frame> frames = new List<Frame>();

        public RequestContext(HttpApp app, Dictionary<string, object> request)
        {
            App = app;
            Request = request;
        }

        public static RequestContext Current(KScriptHost host) =>
            host != null && host.Items.TryGetValue(HttpPackage.ContextKey, out object value) ? value as RequestContext : null;

        public HttpApp App { get; }

        private AuthRequest auth;

        /// <summary>The request's session and signed-in user (KScript.Auth), read when first needed.</summary>
        public AuthRequest Auth => auth ??= AuthRequest.For(Request, App.Auth);
        public HttpSettings Settings => App.Settings;
        public RouteTable Table { get; } = new RouteTable();
        public Dictionary<string, object> Request { get; }

        /// <summary>Headers for the controller's answer, set before it exists: @etag() puts the ETag here.</summary>
        public Dictionary<string, object> ResponseHeaders { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The route being answered, once one matched.</summary>
        public Route Route { get; set; }

        /// <summary>The route file being run, with its prefix and whether it holds API routes.</summary>
        public HttpApp.RouteFileInfo File { get; set; }

        private PageContext page;

        public LiveEvent Live { get; set; }

        /// <summary>The page engine's state for this request, for @view().</summary>
        public PageContext Page => page ??= new PageContext(App.SiteRoot, Request)
        {
            Live = Live,
            StartupConfig = App.StartupConfig,
            RoutedLiveProof = Values.ToStr(Request.GetValueOrDefault("method")) == "GET"
                ? () => PageLive.Sign(new Dictionary<string, object>(), LiveProofScope) : null,
        };

        internal string LiveProofScope => "GET\0" + App.SiteRoot + "\0" + Values.ToStr(Request["url"]) + "\0" + Auth.CsrfToken;

        public Frame Top => frames.Count > 0 ? frames[^1] : throw new KScriptException($"Routes must be inside <KScript.Routes>.", "Http");

        public void Push(string prefix, string middleware, string namePrefix, bool? api)
        {
            if (frames.Count == 0)
            {
                // The file itself: api.* files sit under /api and answer errors with JSON. The middleware in
                // kscript.json's http.middleware runs first, for every route.
                frames.Add(new Frame { Prefix = RoutePattern.Normalise(File?.Prefix), Api = File?.Api, Pending = string.Join(",", Settings.Middleware) });
            }
            frames.Add(new Frame
            {
                Prefix = RoutePattern.Join(frames.Count > 0 ? Top.Prefix : "/", prefix ?? "/"),
                Pending = middleware,
                NamePrefix = (frames.Count > 0 ? Top.NamePrefix : string.Empty) + (namePrefix?.Trim() ?? string.Empty),
                Api = api,
            });
        }

        public void Pop()
        {
            frames.RemoveAt(frames.Count - 1);
            if (frames.Count == 1) frames.Clear();
        }

        public string Prefix => Top.Prefix;

        public string NamePrefix => Top.NamePrefix;

        /// <summary>The middleware of every enclosing file and group, outermost first.</summary>
        public IEnumerable<MiddlewareRef> MiddlewareFor(ElementRun run)
        {
            foreach (Frame frame in frames.Where(f => f.Pending != null))
            {
                string names = frame.Pending;
                frame.Pending = null;
                frame.Middleware.InsertRange(0, HttpPackage.ResolveMiddleware(run, names));
            }
            return frames.SelectMany(f => f.Middleware).ToList();
        }

        public bool Api => frames.AsEnumerable().Reverse().Select(f => f.Api).FirstOrDefault(a => a != null) ?? false;

        public void Add(ElementRun run, Route route)
        {
            try
            {
                Table.Add(route);
            }
            catch (KScriptException ex)
            {
                throw run.Error(ex.Words, route.Name != null ? "name" : null, "Http");
            }
        }

        /// <summary>
        /// Renders a view: <c>people/show</c> is views/people/show.html (or .md), else people/show.html in the site
        /// folder. The data become the page's variables; the page still sees <c>$request</c>.
        /// </summary>
        public HttpResponse RenderView(string name, IReadOnlyDictionary<string, object> data)
        {
            string file = App.FindView(name)
                ?? throw new KScriptException($"There is no view '{name}'. Looked for {string.Join(", ", App.ViewCandidates(name))} in the site folder.", "FileNotFound");
            PageResult result = PageRenderer.RenderFile(Page, file, Route == null ? null : (Request["params"] as Dictionary<string, object>), data);
            var response = new HttpResponse(result.Status, result.Body, result.ContentType);
            foreach (var h in result.Headers) response.Headers[h.Key] = h.Value;
            return response;
        }

        /// <summary>An error answer: JSON for API routes, a short HTML page otherwise.</summary>
        public HttpResponse ErrorResponse(int status, string message)
        {
            bool api = Route?.Api ?? App.IsApiPath(Values.ToStr(Request.TryGetValue("path", out object p) ? p : "/"));
            string text = message ?? HttpPackage.ReasonPhrase(status);
            if (api)
                return HttpResponse.Json(new Dictionary<string, object> { ["error"] = text, ["status"] = (double)status }, status);
            return new HttpResponse(status,
                "<!doctype html><meta charset=\"utf-8\"><title>" + Values.HtmlEscape(HttpPackage.ReasonPhrase(status)) + "</title>"
                + "<style>body{font:15px/1.5 system-ui,sans-serif;margin:40px;color:#222}</style>"
                + $"<h1>{status} {Values.HtmlEscape(HttpPackage.ReasonPhrase(status))}</h1>"
                + (message != null ? $"<p>{Values.HtmlEscape(message)}</p>" : string.Empty));
        }
    }
}
