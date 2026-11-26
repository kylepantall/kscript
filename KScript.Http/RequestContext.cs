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

        /// <summary>The records the route's path parameters name, once found: a typed controller parameter or @inject() finds each once.</summary>
        public Dictionary<string, Record> Bound { get; } = new Dictionary<string, Record>(StringComparer.Ordinal);

        /// <summary>
        /// The route's source for <see cref="Injection"/>: the record already found under the name, else the record
        /// the path parameter of that name is the key of, else (when no name was written) the route's one record of
        /// the model.
        /// </summary>
        public static bool Inject(InjectRequest request, out object record)
        {
            record = null;
            RequestContext ctx = Current(request.Call.Host);
            if (ctx?.Route == null) return false;
            string model = request.Type.Name;
            string name = request.Name;
            var parameters = (Dictionary<string, object>)ctx.Request["params"];
            if (ctx.Bound.TryGetValue(name, out Record held) && !SameModel(held.Type, request.Type))
                throw request.Call.Error($"The route's {{{name}}} is a {held.Type.Name}, not a {model}.", "Context");
            if (held == null && !parameters.ContainsKey(name))
            {
                if (request.Named)
                    throw request.Call.Error($"The route {ctx.Route.Pattern} has no {{{name}}} to take a {model} from.", "Context");
                var same = ctx.Bound.Where(b => SameModel(b.Value.Type, request.Type)).ToList();
                if (same.Count != 1)
                {
                    throw request.Call.Error(same.Count > 1
                        ? $"The route {ctx.Route.Pattern} holds more than one {model} ({string.Join(", ", same.Select(b => b.Key))}). Say which with @inject(from: {same[0].Key})."
                        : $"The route {ctx.Route.Pattern} holds no {model}: it has no {{{name}}}, and no parameter typed {model}. Pass one, such as {model}->find($id).", "Context");
                }
                (name, held) = (same[0].Key, same[0].Value);
            }
            // A view runs apart from its controller, so there the model is declared again: the record is found for it.
            if (held != null && held.Type.IsA(request.Type)) record = held;
            else if (ctx.found.TryGetValue((name, request.Type), out Record again)) record = again;
            else
            {
                record = request.Find(parameters[name]);
                ctx.found[(name, request.Type)] = (Record)record;
                ctx.Bound.TryAdd(name, (Record)record);
            }
            return true;
        }

        private readonly Dictionary<(string, RecordType), Record> found = new Dictionary<(string, RecordType), Record>();

        /// <summary>True for one model, also when a view's own run declared it again from the same file.</summary>
        private static bool SameModel(RecordType held, RecordType wanted) =>
            held.IsA(wanted) || (held.Name == wanted.Name && held.Module?.Path != null && held.Module.Path == wanted.Module?.Path);

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
            HostItems = { [HttpPackage.ContextKey] = this },
            StartupConfig = App.StartupConfig,
            RoutedLiveProof = () => PageLive.Sign(new Dictionary<string, object>(), LiveProofScope),
            LiveUnavailable = LiveUnavailable,
        };

        /// <summary>True for a page file answered by its address: any method renders it, so a GET can always render it again.</summary>
        public bool FilePage { get; init; }

        /// <summary>
        /// A live block calls back by rendering the page's GET again. Why that can't work for this request, or null:
        /// a view answered for a POST (or PUT, DELETE...) at an address with no GET route has nothing to render again.
        /// </summary>
        private string LiveUnavailable()
        {
            string method = Values.ToStr(Request.GetValueOrDefault("method"));
            string path = Values.ToStr(Request.GetValueOrDefault("path"));
            if (FilePage || method is "GET" or "HEAD" || Table.Match("GET", path)?.Route != null) return null;
            return $"This page answers {method} {path}, and <live> blocks call back with GET {path}, which has no route. " +
                   $"Redirect to a GET page after handling the {method}, or add a GET route for {path} that shows the same view.";
        }

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
        public HttpResponse RenderView(string name, IReadOnlyDictionary<string, object> data, bool fromController = false)
        {
            string file = App.FindView(name)
                ?? throw new KScriptException($"There is no view '{name}'. Looked for {string.Join(", ", App.ViewCandidates(name))} in the site folder.", "FileNotFound");
            Page.CanWriteLiveFiles = fromController && Settings.WriteFiles;
            PageResult result = PageRenderer.RenderFile(Page, file, Route == null ? null : (Request["params"] as Dictionary<string, object>), data);
            var response = new HttpResponse(result.Status, result.Body, result.ContentType) { BodyBytes = result.BodyBytes };
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
