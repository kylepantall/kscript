using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Auth;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;
using KScript.Runtime.Dependencies;

using Microsoft.AspNetCore.Http;

namespace KScript.Http
{
    /// <summary>
    /// The routes of a site and how a request is answered. Each request gets a fresh interpreter that runs the route
    /// files (so their imports and functions are new for every request, like pages), matches the route, runs its
    /// middleware and calls its controller. The route table is also kept between requests, so requests that no route
    /// matches (static files, pages) go straight to the pages without running anything.
    /// </summary>
    public sealed class HttpApp
    {
        /// <summary>A route file, where its routes are mounted, and whether they are API routes.</summary>
        public sealed class RouteFileInfo
        {
            public string Path { get; init; }
            public string Prefix { get; init; } = string.Empty;
            public bool Api { get; init; }
        }

        private static readonly string[] RouteExtensions = { ".kscript", ".ks.xml" };
        private static readonly string[] ViewExtensions = { ".html", ".htm", ".md" };

        private readonly object gate = new object();
        private RouteTable cached;
        private Dictionary<string, DateTime> stamps;
        private readonly Dictionary<string, (DateTime Start, int Count)> throttle = new Dictionary<string, (DateTime, int)>(StringComparer.Ordinal);
        private DateTime throttlePruned;
        private double longestThrottle;

        /// <summary>How many throttle counts are kept. Tests read it.</summary>
        internal int ThrottleEntries { get { lock (throttle) return throttle.Count; } }

        public HttpApp(string siteRoot, ProjectConfig project = null, TextWriter log = null)
        {
            SiteRoot = Path.GetFullPath(siteRoot);
            Settings = project?.Http ?? new HttpSettings();
            Project = project;
            projectRoot = project?.FilePath != null ? Path.GetDirectoryName(Path.GetFullPath(project.FilePath)) : SiteRoot;
            Log = log;
            Files = FindRouteFiles();
        }

        public string SiteRoot { get; }

        private readonly string projectRoot;
        private AuthApp auth;

        /// <summary>The app's users, sessions and tokens (KScript.Auth), with http.auth's settings.</summary>
        public AuthApp Auth => AppFolders.TestHost != null
            ? AuthApp.For(projectRoot, Settings.Auth)
            : auth ??= AuthApp.For(projectRoot, Settings.Auth);
        public HttpSettings Settings { get; }

        /// <summary>The project, or null; the OpenAPI document takes its title and version from it.</summary>
        public ProjectConfig Project { get; }
        public TextWriter Log { get; }
        private PageSite pageSite;

        internal PageSite PageSite => LazyInitializer.EnsureInitialized(ref pageSite, CreatePageSite);

        private PageSite CreatePageSite()
        {
            if (StartupConfig == null) LoadStartupConfig();
            var site = new PageSite(SiteRoot) { StartupConfig = StartupConfig };
            if (StartupConfig != null)
                foreach (string path in StartupConfig.FileSources)
                    site.HiddenFiles.Add(Path.GetRelativePath(site.Root, path).Replace('\\', '/'));
            if (ViewsFolder is string views) site.HiddenFolders.Add(views.Split('/', '\\')[0]);
            HttpServer.ProtectStorage(site, projectRoot, Settings.Auth, StartupConfig);
            if (HasRoutes) site.IsRoutedPath = path => Table().Match("GET", path) != null || IsApiPath(path);
            return site;
        }
        internal ConfigValue StartupConfig { get; private set; }
        public IReadOnlyList<RouteFileInfo> Files { get; }
        public bool HasRoutes => Files.Count > 0;

        internal void LoadStartupConfig()
        {
            if (Project?.FilePath == null) return;
            var host = new KScriptHost(Log ?? TextWriter.Null, Console.Error)
            {
                WorkingDirectory = projectRoot,
                FileRoot = projectRoot,
                FileRootName = "the project folder",
                CanWriteFiles = false,
                Config = Project.ConfigOptions(),
            };
            ConfigValue config = ConfigValue.For(host, projectRoot);
            _ = config.Values;
            StartupConfig = config;
        }

        /// <summary>Reads environment variables (the auth middleware's token). Tests replace it.</summary>
        public Func<string, string> Environment { get; set; } = System.Environment.GetEnvironmentVariable;

        /// <summary>The clock for the throttle middleware. Tests replace it.</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>The steps one request may take, so an endless loop can't hang the server.</summary>
        public long MaxSteps { get; set; } = 5_000_000;

        private List<RouteFileInfo> FindRouteFiles()
        {
            var files = new List<RouteFileInfo>();
            if (Settings.Routes != null)
            {
                foreach (HttpSettings.RouteFile r in Settings.Routes)
                {
                    string full = Path.GetFullPath(Path.Combine(SiteRoot, r.File));
                    if (!File.Exists(full)) throw new FileNotFoundException($"http.routes: there is no route file {r.File} in {SiteRoot}.");
                    bool api = r.Api || IsApiName(full);
                    files.Add(new RouteFileInfo { Path = full, Prefix = r.Prefix ?? (api ? Settings.ApiPrefix : string.Empty), Api = api });
                }
                return files;
            }
            foreach (string name in HttpSettings.DefaultRouteNames)
            {
                string found = RouteExtensions.Select(ext => Path.Combine(SiteRoot, name + ext)).FirstOrDefault(File.Exists);
                if (found == null) continue;
                bool api = name == "api" || name.EndsWith("/api", StringComparison.Ordinal);
                files.Add(new RouteFileInfo { Path = found, Prefix = api ? Settings.ApiPrefix : string.Empty, Api = api });
            }
            return files;
        }

        private static bool IsApiName(string path)
        {
            string name = Path.GetFileName(path);
            return RouteExtensions.Any(ext => string.Equals(name, "api" + ext, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True if the path is under an API route file's prefix.</summary>
        public bool IsApiPath(string path) =>
            Files.Any(f => f.Api && RoutePattern.Normalise(f.Prefix) != "/" &&
                           (RoutePattern.Normalise(path) + "/").StartsWith(RoutePattern.Normalise(f.Prefix) + "/", StringComparison.OrdinalIgnoreCase));

        #region Views

        /// <summary>The views folder, relative to the site folder, if it exists.</summary>
        public string ViewsFolder
        {
            get
            {
                string views = (Settings.Views ?? "views").Trim().Trim('/', '\\');
                return views.Length > 0 && Directory.Exists(Path.Combine(SiteRoot, views)) ? views : null;
            }
        }

        internal IEnumerable<string> ViewCandidates(string name)
        {
            name = (name ?? string.Empty).Replace('\\', '/').TrimStart('/');
            bool hasExtension = ViewExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
            IEnumerable<string> names = hasExtension ? new[] { name } : ViewExtensions.Select(e => name + e);
            string views = (Settings.Views ?? "views").Trim().Trim('/', '\\');
            return (views.Length > 0 ? names.Select(n => views + "/" + n) : Enumerable.Empty<string>()).Concat(names);
        }

        internal string FindView(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Replace('\\', '/').Split('/').Any(s => s == ".."))
                throw new KScriptException($"'{name}' isn't a view name. Views are paths inside the site folder, such as people/show.", "Argument");
            return ViewCandidates(name).FirstOrDefault(c => File.Exists(Path.Combine(SiteRoot, c)));
        }

        #endregion

        #region Route table

        /// <summary>
        /// Runs the route files to build the route table, without a request. Used by <c>kscript routes</c> and to
        /// decide quickly whether a request is for a route at all. Throws <see cref="KScriptException"/> for mistakes.
        /// </summary>
        public RouteTable LoadRoutes()
        {
            var request = new Dictionary<string, object>();
            Complete(request);
            request[RouteRequestFields.Json.Name] = null;
            request[RouteRequestFields.Input.Name] = new Dictionary<string, object>();
            RouteTable table = null;
            OnBigStack(() =>
            {
                var ctx = new RequestContext(this, request);
                RunRouteFiles(ctx, NewInterpreter(ctx));
                table = ctx.Table;
            });
            return table;
        }

        /// <summary>
        /// The route table, built again when a route file changes, or any script or file building it reached (a
        /// controller, a group of routes made by a function in another script, config).
        /// </summary>
        public RouteTable Table()
        {
            lock (gate)
            {
                if (cached == null || stamps == null || Files.Any(f => !stamps.ContainsKey(f.Path)) || stamps.Any(s => Stamp(s.Key) != s.Value))
                {
                    DependencySet outer = DependencyTrace.Current;
                    var reached = new DependencySet();
                    DependencyTrace.Current = reached;
                    try
                    {
                        cached = LoadRoutes();
                    }
                    finally
                    {
                        DependencyTrace.Current = outer;
                        outer?.AddAll(reached);
                    }
                    stamps = Files.Select(f => f.Path).Concat(reached.Files.Keys).Distinct(StringComparer.Ordinal).ToDictionary(p => p, Stamp, StringComparer.Ordinal);
                }
                return cached;
            }
        }

        private static DateTime Stamp(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

        private Interpreter NewInterpreter(RequestContext ctx)
        {
            // Controllers write what they echo to the server's log; files stay inside the site folder.
            var host = new KScriptHost(Log ?? TextWriter.Null, Console.Error)
            {
                WorkingDirectory = SiteRoot,
                FileRoot = SiteRoot,
                FileRootName = "the site folder",
                CanWriteFiles = Settings.WriteFiles,
            };
            host.Items[HttpPackage.ContextKey] = ctx;
            host.Items[AuthRequest.HostKey] = ctx.Auth;
            StartupConfig?.CopyTo(host);
            // Each request has its own interpreter, but scripts are parsed once for the whole server, and again only when they change.
            return new Interpreter(host, options: new InterpreterOptions { MaxSteps = MaxSteps, MaxCallDepth = 200, Scripts = KScript.Runtime.Dependencies.ScriptCache.Server });
        }

        private static void RunRouteFiles(RequestContext ctx, Interpreter interpreter)
        {
            foreach (RouteFileInfo file in ctx.App.Files)
            {
                Module module = interpreter.Load(file.Path);
                if (module.MainElementName != "Routes")
                {
                    throw new KScriptException($"{Path.GetFileName(file.Path)} is a route file, so it holds Routes {{ }} after import KScript.Http (or <KScript.Routes> in an XML file), not {(module.MainElement ?? module.Root).Name}.", "Http")
                    { File = file.Path, Line = 1 };
                }
                ctx.File = file;
                try
                {
                    interpreter.RunModule(module);
                }
                catch (KScriptException ex)
                {
                    throw Interpreter.WithLocation(ex, module);
                }
                finally
                {
                    ctx.File = null;
                }
            }
        }


        #endregion

        #region Requests

        /// <summary>
        /// Answers a request if a route matches it (or it is under an API prefix); returns null to let the pages answer.
        /// </summary>
        public async Task<PageResult> HandleAsync(HttpRequest request)
        {
            string path = request.Path.Value ?? "/";
            if (path.StartsWith(PasskeyEndpoints.Prefix, StringComparison.Ordinal))
                return await PasskeyEndpoints.HandleAsync(this, request);
            if (!HasRoutes) return null;
            if (path.StartsWith("/_ks/", StringComparison.Ordinal)) return null;

            RouteTable table;
            try
            {
                table = Table();
            }
            catch (KScriptException ex)
            {
                if (PageSite.HideErrorDetails) Log?.WriteLine("  error: " + (ex.Location != null ? $"{ex.Location}: {ex.Message}" : ex.Message));
                return ErrorPage(ex);
            }

            if (Settings.OpenApi.Enabled && OpenApiPage(path, table) is PageResult docs) return docs;

            bool live = PageServer.IsLiveRequest(request);
            if (table.Match(live ? "GET" : request.Method, path) == null && !IsApiPath(path)) return null;

            var (map, error) = await PageServer.ReadPageRequestAsync(request, live);
            // A body that could not be read (too large, cut short): API paths say so in JSON, like their other errors.
            if (error != null && error.Body.Length > 0 && IsApiPath(path))
                return ToPage(HttpResponse.Json(new Dictionary<string, object>
                {
                    ["error"] = PageServer.BodyErrorMessage(error.Status), ["status"] = (double)error.Status,
                }, error.Status), request.Method);
            if (error != null) return error;
            map[RouteRequestFields.Ip.Name] = request.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            return Handle(map);
        }

        internal PageResult RenderFilePage(PageRoute route, Dictionary<string, object> request)
        {
            request[RequestFields.RouteInfo.Name] = RouteCatalog.PageInfo(route.Path, route.Params.Keys, RouteCatalog.PageLabelKey(this, route.File));
            var ctx = new RequestContext(this, request) { FilePage = true };
            try
            {
                return RestoreLiveRequest(ctx) ?? PageRenderer.RenderFile(ctx.Page, route.File, route.Params);
            }
            catch (LiveExpiredException ex)
            {
                return Expired(ex);
            }
        }

        private static PageResult Expired(LiveExpiredException ex) =>
            new PageResult { Status = 409, Body = ex.Message, ContentType = "text/plain; charset=utf-8" };

        private static PageResult RestoreLiveRequest(RequestContext ctx)
        {
            var request = ctx.Request;
            if (Values.ToStr(request["method"]) != "POST" || BuiltInMiddleware.Header(request, "x-kscript-live") is not string proof)
                return null;
            if (ctx.Auth.SessionId == null) throw new LiveExpiredException();
            PageLive.Verify(proof, ctx.LiveProofScope);
            request.Remove(PageServer.LiveFilesKey, out object uploads);
            string type = BuiltInMiddleware.Header(request, "content-type");
            if (uploads == null && !string.Equals(type?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
                return new PageResult { Status = 415 };
            string body = Values.ToStr(request["body"]);
            if (Encoding.UTF8.GetByteCount(body) > PageServer.MaxLiveBodyBytes) return new PageResult { Status = 413 };
            try { ctx.Live = PageLive.ParseRequest(body, routed: true).Event; }
            catch (PageException) { return new PageResult { Status = 400 }; }
            ctx.Live.Files = uploads as Dictionary<string, object>;
            // Render the original GET again. Event payloads reach only the signed component.
            request["method"] = "GET";
            request["body"] = string.Empty;
            request["form"] = new Dictionary<string, object>();
            return null;
        }

        /// <summary>
        /// Answers a request given as the <c>$request</c> map (method, path, query, form, headers, cookies, body).
        /// Returns null when no route matches and the path isn't under an API prefix.
        /// </summary>
        public PageResult Handle(Dictionary<string, object> request)
        {
            Complete(request);
            if (Values.ToStr(request["path"]).StartsWith(PasskeyEndpoints.Prefix, StringComparison.Ordinal))
                return PasskeyEndpoints.Handle(this, request);
            PageResult result = null;
            OnBigStack(() => result = HandleHere(request));
            // The session (KScript.Auth) is saved once the request is answered, and its cookie sent.
            List<string> cookies = RequestHooks.Finish(request);
            result?.Cookies.AddRange(cookies);
            return result;
        }

        private static void Complete(Dictionary<string, object> request)
        {
            request.TryAdd(RequestFields.Method.Name, "GET");
            request.TryAdd(RequestFields.Scheme.Name, "http");
            request.TryAdd(RequestFields.Path.Name, "/");
            request.TryAdd(RequestFields.Url.Name, request[RequestFields.Path.Name]);
            request.TryAdd(RequestFields.Query.Name, new Dictionary<string, object>());
            request.TryAdd(RequestFields.Form.Name, new Dictionary<string, object>());
            request.TryAdd(RequestFields.Headers.Name, new Dictionary<string, object>());
            request.TryAdd(RequestFields.Cookies.Name, new Dictionary<string, object>());
            request.TryAdd(RequestFields.Body.Name, string.Empty);
            request.TryAdd(RouteRequestFields.Ip.Name, "127.0.0.1");
            request[RequestFields.Params.Name] = new Dictionary<string, object>();
            request[RequestFields.Method.Name] = Values.ToStr(request[RequestFields.Method.Name]).ToUpperInvariant();
        }

        private PageResult HandleHere(Dictionary<string, object> request)
        {
            var ctx = new RequestContext(this, request);
            string method = (string)request["method"];
            string path = Values.ToStr(request["path"]);
            try
            {
                if (RestoreLiveRequest(ctx) is PageResult error) return error;
                method = (string)request["method"];
                Interpreter interpreter = NewInterpreter(ctx);
                RunRouteFiles(ctx, interpreter);

                RouteMatch match = ctx.Table.Match(method, path);
                if (match == null)
                {
                    return IsApiPath(path) ? ToPage(ctx.ErrorResponse(404, $"No route for {method} {path}."), method) : null;
                }

                if (!ReadJson(ctx, request, out HttpResponse bad)) return ToPage(bad, method);

                Route route = match.Route ?? match.PathRoute;
                ctx.Route = route;
                foreach (var kv in match.Parameters) ((Dictionary<string, object>)request["params"])[kv.Key] = kv.Value;
                request[RouteRequestFields.Route.Name] = route.Name;
                request[RequestFields.RouteInfo.Name] = RouteCatalog.HttpInfo(route, null);

                Func<Dictionary<string, object>, object> answer;
                if (match.Route != null)
                {
                    answer = req => CallController(ctx, interpreter, route, req);
                }
                else if (method == "OPTIONS")
                {
                    // A preflight or OPTIONS request to a path that has routes for other methods.
                    answer = req =>
                    {
                        var r = new HttpResponse(204, string.Empty, null);
                        r.Headers["Allow"] = string.Join(", ", match.Allowed.Append("OPTIONS"));
                        return r;
                    };
                }
                else
                {
                    HttpResponse notAllowed = ctx.ErrorResponse(405, $"{path} answers {string.Join(", ", match.Allowed)}, not {method}.");
                    notAllowed.Headers["Allow"] = string.Join(", ", match.Allowed);
                    return ToPage(notAllowed, method);
                }

                HttpResponse response = Pipeline(ctx, interpreter, route.Middleware, 0, request, answer);
                return ToPage(response, method);
            }
            catch (HttpAbortSignal abort)
            {
                return ToPage(abort.Response, method);
            }
            catch (LiveExpiredException ex)
            {
                return Expired(ex);
            }
            catch (KScriptException ex) when (ErrorTypes.StatusOf(ex) is int status)
            {
                return ToPage(StatusAnswer(ctx, request, ex, status), method);
            }
            catch (KScriptException ex)
            {
                return ToPage(Failure(ctx, ex.Location != null ? $"{ex.Location}: {ex.Message}" : ex.Message), method);
            }
            catch (PageException ex) when (StatusError(ex) is KScriptException inner)
            {
                // An error with a status raised while a view was drawn is the client's answer there too.
                return ToPage(StatusAnswer(ctx, request, inner, ErrorTypes.StatusOf(inner).Value), method);
            }
            catch (PageException ex)
            {
                return ToPage(Failure(ctx, (ex.Location != null ? ex.Location + ": " : string.Empty) + ex.Message), method);
            }
        }

        /// <summary>
        /// An error whose type carries a status (<c>error NotFound(status: 404)</c>, or a built-in such as Validation) is
        /// the client's answer, not a crash: that status and the message, without the file and line. API routes answer
        /// JSON with the error's type and fields too: <c>{ "error": "...", "status": 404, "type": "NotFound", "id": 42 }</c>.
        /// </summary>
        private HttpResponse ErrorAnswer(RequestContext ctx, KScriptException ex, int status)
        {
            Log?.WriteLine($"  {status} {ex.Type}: {ex.Message}");
            HttpResponse response = ctx.ErrorResponse(status, ex.Message);
            if (response.ContentType.StartsWith("application/json", StringComparison.Ordinal))
            {
                var body = new Dictionary<string, object> { ["error"] = ex.Message, ["status"] = (double)status, ["type"] = ex.Type };
                foreach (var kv in ex.Fields) body.TryAdd(kv.Key, kv.Value);
                response = HttpResponse.Json(body, status);
            }
            // A TooManyRequests error with retry_after says when to come back; a 401 says how to sign in.
            if (ex.Fields.TryGetValue("retry_after", out object wait)) response.Headers["Retry-After"] = Values.ToStr(wait);
            if (status == 401) response.Headers["WWW-Authenticate"] = "Bearer";
            return response;
        }

        /// <summary>The error with a status that a page error wraps, however deep, or null.</summary>
        private static KScriptException StatusError(Exception ex)
        {
            for (Exception e = ex.InnerException; e != null; e = e.InnerException)
            {
                if (e is KScriptException inner && ErrorTypes.StatusOf(inner) != null) return inner;
            }
            return null;
        }

        /// <summary>
        /// The answer for an error with a status. A guest a policy turned away is treated as the auth middleware
        /// treats one: a person in a browser is sent to sign in, and anything else gets the 401.
        /// </summary>
        private HttpResponse StatusAnswer(RequestContext ctx, Dictionary<string, object> request, KScriptException ex, int status) =>
            status == 401 && ex.Message == Policies.SignIn && BuiltInMiddleware.SignIn(ctx, request) is HttpResponse signIn
                ? signIn : ErrorAnswer(ctx, ex, status);

        private HttpResponse Failure(RequestContext ctx, string message)
        {
            Log?.WriteLine("  error: " + message);
            return ctx.ErrorResponse(500, PageSite.HideErrorDetails ? PageServer.HiddenErrorMessage : message);
        }

        /// <summary>A JSON body is read into <c>$request.json</c>; <c>$request.input</c> holds the query, form and JSON fields together.</summary>
        private static bool ReadJson(RequestContext ctx, Dictionary<string, object> request, out HttpResponse bad)
        {
            bad = null;
            object json = null;
            string body = Values.ToStr(request.TryGetValue("body", out object b) ? b : string.Empty);
            string type = request["headers"] is Dictionary<string, object> h && h.TryGetValue("content-type", out object t) ? Values.ToStr(t) : string.Empty;
            if (body.Trim().Length > 0 && type.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    json = Values.ParseJson(body);
                }
                catch (KScriptException ex)
                {
                    bad = ctx.ErrorResponse(400, "The request body isn't valid JSON: " + ex.Message);
                    return false;
                }
            }
            request[RouteRequestFields.Json.Name] = json;
            var input = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string part in new[] { "query", "form" })
            {
                if (request[part] is Dictionary<string, object> values) foreach (var kv in values) input[kv.Key] = kv.Value;
            }
            if (json is Dictionary<string, object> fields) foreach (var kv in fields) input[kv.Key] = kv.Value;
            request[RouteRequestFields.Input.Name] = input;
            return true;
        }

        /// <summary>Runs middleware <paramref name="index"/> and the rest after it; the last step answers the request.</summary>
        private HttpResponse Pipeline(RequestContext ctx, Interpreter interpreter, IReadOnlyList<MiddlewareRef> chain, int index,
            Dictionary<string, object> request, Func<Dictionary<string, object>, object> answer)
        {
            try
            {
                if (index == chain.Count)
                {
                    HttpResponse answered = HttpResponse.From(answer(request));
                    foreach (var h in ctx.ResponseHeaders) answered.Headers.TryAdd(h.Key, h.Value);
                    return answered;
                }

                MiddlewareRef middleware = chain[index];
                HttpResponse Next(Dictionary<string, object> req) => Pipeline(ctx, interpreter, chain, index + 1, req ?? request, answer);

                if (middleware.Function == null) return BuiltInMiddleware.Run(middleware, ctx, request, () => Next(request));

                var next = new NativeFunction("next", call => Next(call.Count > 0 ? call.Arg(0) as Dictionary<string, object> : null));
                object result = interpreter.Call(middleware.Function, new object[] { request, next });
                if (result == null)
                    throw new KScriptException($"The middleware {middleware.Name} returned nothing. Return $next($request) to carry on, or a response to stop here.", "Http");
                return HttpResponse.From(result);
            }
            catch (HttpAbortSignal abort)
            {
                // @abort() answers here, so the middleware around it still sees (and can change) the response.
                return abort.Response;
            }
        }

        /// <summary>The parameter names a controller can ask for, besides the route's {parameters}.</summary>
        public static readonly string[] RequestParameters = { "request", "json", "body", "query", "form", "input", "headers", "cookies" };

        private static object CallController(RequestContext ctx, Interpreter interpreter, Route route, Dictionary<string, object> request)
        {
            var parameters = (Dictionary<string, object>)request["params"];
            if (route.View != null) return ctx.RenderView(route.View, parameters);
            if (route.RedirectTo != null) return HttpPackage.Redirect(route.RedirectTo, route.RedirectStatus);

            var available = new Dictionary<string, object>(parameters, StringComparer.Ordinal)
            {
                ["request"] = request,
                ["json"] = request[RouteRequestFields.Json.Name],
                ["body"] = request["json"] ?? request["body"],
                ["query"] = request["query"],
                ["form"] = request["form"],
                ["input"] = request[RouteRequestFields.Input.Name],
                ["headers"] = request["headers"],
                ["cookies"] = request["cookies"],
            };
            foreach (var kv in parameters) available[kv.Key] = kv.Value;

            if (route.Inline != null)
            {
                Scope scope = route.Scope.Child();
                foreach (var kv in available) scope.Declare(kv.Key, kv.Value);
                try
                {
                    interpreter.ExecuteChildren(route.Inline, scope, route.Module);
                }
                catch (ReturnSignal r)
                {
                    return r.Value;
                }
                return null;
            }

            IEnumerable<string> wanted = route.Controller switch
            {
                UserFunction fn => fn.Parameters.Select(p => p.Name),
                Lambda lambda => lambda.Parameters,
                _ => null,
            };
            var named = wanted?.Where(available.ContainsKey).ToDictionary(n => n, n => available[n], StringComparer.Ordinal);
            if (route.Controller is UserFunction typed) CheckTypedParameters(ctx, interpreter, typed, named, parameters);
            CheckCan(ctx, interpreter, route, parameters);
            return named == null ? interpreter.Call(route.Controller, new object[] { request }) : interpreter.Call(route.Controller, Array.Empty<object>(), named);
        }

        /// <summary>
        /// A route's <c>can:</c>, asked before the controller runs: about the one record its typed path parameters
        /// name, or for a Resource with a model about the record its key names (the model itself for create).
        /// </summary>
        private static void CheckCan(RequestContext ctx, Interpreter interpreter, Route route, Dictionary<string, object> pathParams)
        {
            if (route.Can == null) return;
            var call = new CallContext(interpreter, "authorize", Array.Empty<object>(), scope: route.Scope, module: route.Module);
            object target;
            if (route.CanModel != null && route.CanParameter == null)
            {
                target = route.CanModel;
            }
            else if (route.CanModel != null && ctx.Bound.TryGetValue(route.CanParameter, out Record held) && held.Type.IsA(route.CanModel))
            {
                target = held;
            }
            else if (route.CanModel != null)
            {
                ((IRecordKind)route.CanModel.Tag).TryFind(call, route.CanModel, pathParams[route.CanParameter], out target);
                ctx.Bound[route.CanParameter] = (Record)target;
            }
            else
            {
                var records = ctx.Bound.Values.Distinct().ToList();
                if (records.Count != 1)
                {
                    throw new KScriptException(records.Count == 0
                        ? $"can: {route.Can} needs a model in the route {route.Pattern}: type a path parameter of the controller, such as fn {route.Can}(post: Post)."
                        : $"can: {route.Can} can't tell which record to ask about: the route {route.Pattern} holds {string.Join(" and ", ctx.Bound.Keys)}. Call @authorize({route.Can}, ...) in the controller for the one you mean.", "Http");
                }
                target = records[0];
            }
            Policies.Authorize(call, route.Can, target);
        }

        /// <summary>
        /// Maps request data into a controller's typed parameters before it runs: <c>params="json: NewPerson"</c>
        /// turns the body into a NewPerson record. Data that doesn't fit is the client's mistake, so it is answered
        /// with 422 and every problem (or 404 for a path parameter), instead of a 500 from inside the controller.
        /// </summary>
        private static void CheckTypedParameters(RequestContext ctx, Interpreter interpreter, UserFunction fn, Dictionary<string, object> named, Dictionary<string, object> pathParams)
        {
            foreach (Parameter p in fn.Parameters)
            {
                if (p.Type == null || !named.TryGetValue(p.Name, out object value)) continue;
                bool fromPath = pathParams.ContainsKey(p.Name);
                string what = fromPath ? $"The {{{p.Name}}} in the path" : p.Name switch
                {
                    "json" or "body" => "The request body",
                    "query" => "The query string",
                    "form" => "The form",
                    "input" => "The request's input",
                    _ => $"The request's {p.Name}",
                };
                // A path parameter typed with a model is the record its key names: fn show(post: Post) on /posts/{post}.
                if (fromPath && TypeRef.Parse(p.Type) is { IsSimple: true, IsBuiltIn: false } t && Shapes.Resolve(t, fn.Closure, fn.Module) is RecordType { Tag: IRecordKind kind } type
                    && kind.TryFind(new CallContext(interpreter, p.Name, Array.Empty<object>(), scope: fn.Closure, module: fn.Module), type, value, out object found))
                {
                    named[p.Name] = found;
                    if (found is Record record) ctx.Bound[p.Name] = record;
                    continue;
                }
                try
                {
                    named[p.Name] = Shapes.ConvertRequest(interpreter, value, p.Type, what, fn.Closure, fn.Module);
                }
                catch (KScriptException ex) when (ex.Type == "Shape" || ex.Type == "Type" || ex.Type == "Argument")
                {
                    if (fromPath) throw new HttpAbortSignal(ctx.ErrorResponse(404, ex.Message));
                    HttpResponse response = ctx.ErrorResponse(422, ex.Message);
                    if (response.ContentType.StartsWith("application/json", StringComparison.Ordinal))
                    {
                        var body = new Dictionary<string, object> { ["error"] = ex.Message, ["status"] = 422.0 };
                        if (ex.Fields.TryGetValue("problems", out object problems)) body["problems"] = problems;
                        response = HttpResponse.Json(body, 422);
                    }
                    throw new HttpAbortSignal(response);
                }
            }
        }

        private static PageResult ToPage(HttpResponse response, string method)
        {
            var page = new PageResult
            {
                Status = response.Status,
                Body = method == "HEAD" ? string.Empty : response.Body ?? string.Empty,
                BodyBytes = method == "HEAD" ? null : response.BodyBytes,
                ContentType = response.ContentType ?? "text/plain; charset=utf-8",
            };
            foreach (var h in response.Headers)
            {
                // Set-Cookie goes out once per cookie, so it joins the cookies the session adds.
                if (string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    page.Cookies.AddRange(Values.Unwrap(h.Value) is List<object> list ? list.Select(Values.ToStr) : new[] { Values.ToStr(h.Value) });
                else page.Headers[h.Key] = Values.ToStr(h.Value);
            }
            return page;
        }

        /// <summary>The paths of the OpenAPI document and its page, served while http.openapi is on.</summary>
        public static readonly string[] OpenApiPaths = { "/_openapi.json", "/_openapi.yaml", "/_api" };

        /// <summary>/_openapi.json, /_openapi.yaml or the /_api page; null for any other path.</summary>
        public PageResult OpenApiPage(string path, RouteTable table)
        {
            path = path.TrimEnd('/');
            if (!OpenApiPaths.Contains(path, StringComparer.Ordinal)) return null;
            if (path == "/_api") return new PageResult { Body = OpenApiViewer.Page(Settings.OpenApi.Title ?? Project?.Name) };
            Dictionary<string, object> document = OpenApi.Document(this, table, Project);
            var result = path.EndsWith(".json", StringComparison.Ordinal)
                ? new PageResult { ContentType = "application/json; charset=utf-8", Body = OpenApi.ToJson(document) }
                : new PageResult { ContentType = "application/yaml; charset=utf-8", Body = OpenApi.ToYaml(document) };
            result.Headers["Access-Control-Allow-Origin"] = "*";
            result.Headers["Cache-Control"] = "no-cache";
            return result;
        }

        private PageResult ErrorPage(KScriptException ex) =>
            PageServer.ErrorPage(new PageException(ex.Location != null ? $"{ex.Location}: {ex.Message}" : ex.Message), !PageSite.HideErrorDetails);

        internal bool Throttle(string key, int limit, double seconds, out int remaining, out double retryAfter)
        {
            lock (throttle)
            {
                DateTime now = Clock();
                // Counts whose window ended are dropped now and then, so one entry per visitor doesn't stay forever.
                longestThrottle = Math.Max(longestThrottle, seconds);
                if ((now - throttlePruned).TotalSeconds >= 60 || now < throttlePruned)
                {
                    throttlePruned = now;
                    foreach (string old in throttle.Where(e => (now - e.Value.Start).TotalSeconds >= longestThrottle).Select(e => e.Key).ToList())
                        throttle.Remove(old);
                }
                if (!throttle.TryGetValue(key, out var entry) || (now - entry.Start).TotalSeconds >= seconds) entry = (now, 0);
                entry.Count++;
                throttle[key] = entry;
                remaining = Math.Max(0, limit - entry.Count);
                retryAfter = Math.Max(0, seconds - (now - entry.Start).TotalSeconds);
                return entry.Count <= limit;
            }
        }

        private const int StackSize = 64 * 1024 * 1024;

        /// <summary>Runs on a thread with a large stack, so deep recursion in a controller reaches MaxCallDepth instead of crashing.</summary>
        private static void OnBigStack(Action work)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
            var thread = new Thread(() =>
            {
                try { work(); }
                catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
            }, StackSize);
            thread.Start();
            thread.Join();
            failure?.Throw();
        }

        #endregion

        #region Listing

        /// <summary>The route table as a plain table (method, path, name, what answers it, middleware, file), for coding agents' docs.</summary>
        public static string Describe(RouteTable table, string siteRoot)
        {
            var rows = table.Routes.Select(r => new[]
            {
                r.Methods.Count == 0 ? "ANY" : string.Join("|", r.Methods),
                r.Pattern.Path,
                r.Name ?? string.Empty,
                r.Target,
                string.Join(", ", r.Middleware),
                r.File != null ? Path.GetRelativePath(siteRoot, r.File).Replace('\\', '/') + ":" + r.Line : string.Empty,
            }).ToList();
            var header = new[] { "METHOD", "PATH", "NAME", "ANSWERED BY", "MIDDLEWARE", "FILE" };
            int[] widths = header.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
            var sb = new StringBuilder();
            void Line(string[] cells) => sb.AppendLine(string.Join("  ", cells.Select((c, i) => i == cells.Length - 1 ? c : c.PadRight(widths[i]))).TrimEnd());
            Line(header);
            foreach (var row in rows) Line(row);
            return sb.ToString();
        }

        /// <summary>
        /// The route table as text, for <c>kscript routes</c>, laid out like artisan route:list: the method, the path,
        /// dots, then the route's name and what answers it. <paramref name="verbose"/> adds each route's middleware and
        /// the file and line it is declared on.
        /// </summary>
        public static string Print(RouteTable table, string siteRoot, KScript.Output.CliOutput cli = null, bool verbose = true)
        {
            var text = new StringWriter { NewLine = "\n" };
            cli = cli == null ? new KScript.Output.CliOutput(text, colour: false) : new KScript.Output.CliOutput(text, cli.Style.Enabled, cli.Width);
            var style = cli.Style;
            string Methods(Route r) => r.Methods.Count == 0 ? "ANY" : string.Join("|", r.Methods);
            int methodWidth = table.Routes.Count == 0 ? 3 : table.Routes.Max(r => Methods(r).Length);

            string Colour(string method) => method switch
            {
                "GET" => style.Blue(method),
                "HEAD" or "OPTIONS" => style.Gray(method),
                "POST" or "PUT" or "PATCH" => style.Yellow(method),
                _ => style.Red(method),
            };

            cli.NewLine();
            foreach (Route r in table.Routes)
            {
                string methods = KScript.Output.CliStyle.Pad(string.Join(style.Gray("|"), Methods(r).Split('|').Select(Colour)), methodWidth);
                string path = Regex.Replace(r.Pattern.Path, @"\{[^}]+\}", m => style.Yellow(m.Value));
                string answer = r.Name != null ? $"{r.Name} {style.Gray("›")} {style.Gray(r.Target)}" : style.Gray(r.Target);
                cli.TwoColumn($"{methods}  {path}", answer);
                if (!verbose) continue;
                string pad = new string(' ', methodWidth + 4);
                if (r.Middleware.Count > 0) cli.Line($"{pad}{style.Gray("⇂ " + string.Join(", ", r.Middleware))}");
                if (r.File != null) cli.Line($"{pad}{style.Gray("⇂ " + Path.GetRelativePath(siteRoot, r.File).Replace('\\', '/') + ":" + r.Line)}");
            }
            cli.NewLine();
            string count = $"Showing [{table.Routes.Count}] route{(table.Routes.Count == 1 ? string.Empty : "s")}";
            cli.Line(KScript.Output.CliStyle.PadLeft(style.Bold(count), cli.Width - 2));
            return text.ToString();
        }

        #endregion
    }
}
