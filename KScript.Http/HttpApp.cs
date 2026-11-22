using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Auth;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;

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
        private DateTime[] stamps;
        private readonly Dictionary<string, (DateTime Start, int Count)> throttle = new Dictionary<string, (DateTime, int)>(StringComparer.Ordinal);

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
        public AuthApp Auth => auth ??= AuthApp.For(projectRoot, Settings.Auth);
        public HttpSettings Settings { get; }

        /// <summary>The project, or null; the OpenAPI document takes its title and version from it.</summary>
        public ProjectConfig Project { get; }
        public TextWriter Log { get; }
        public IReadOnlyList<RouteFileInfo> Files { get; }
        public bool HasRoutes => Files.Count > 0;

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
            var request = new Dictionary<string, object>
            {
                ["method"] = "GET", ["path"] = "/", ["url"] = "/", ["query"] = new Dictionary<string, object>(), ["form"] = new Dictionary<string, object>(),
                ["headers"] = new Dictionary<string, object>(), ["cookies"] = new Dictionary<string, object>(), ["body"] = string.Empty,
                ["params"] = new Dictionary<string, object>(), ["json"] = null, ["input"] = new Dictionary<string, object>(), ["ip"] = "127.0.0.1",
            };
            RouteTable table = null;
            OnBigStack(() =>
            {
                var ctx = new RequestContext(this, request);
                RunRouteFiles(ctx, NewInterpreter(ctx));
                table = ctx.Table;
            });
            return table;
        }

        /// <summary>The route table, built again when a route file changes.</summary>
        public RouteTable Table()
        {
            lock (gate)
            {
                DateTime[] now = Files.Select(f => File.GetLastWriteTimeUtc(f.Path)).ToArray();
                if (cached == null || stamps == null || !now.SequenceEqual(stamps))
                {
                    cached = LoadRoutes();
                    stamps = now;
                }
                return cached;
            }
        }

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
            return new Interpreter(host, options: new InterpreterOptions { MaxSteps = MaxSteps, MaxCallDepth = 200 });
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
            if (!HasRoutes) return null;
            string path = request.Path.Value ?? "/";
            if (path.StartsWith("/_ks/", StringComparison.Ordinal)) return null;

            RouteTable table;
            try
            {
                table = Table();
            }
            catch (KScriptException ex)
            {
                return ErrorPage(ex);
            }

            if (Settings.OpenApi.Enabled && OpenApiPage(path, table) is PageResult docs) return docs;

            if (table.Match(request.Method, path) == null && !IsApiPath(path)) return null;

            Dictionary<string, object> map = await PageServer.BuildRequestAsync(request);
            map["ip"] = request.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            map["url"] = request.Path.Value + request.QueryString.Value;
            return Handle(map);
        }

        /// <summary>
        /// Answers a request given as the <c>$request</c> map (method, path, query, form, headers, cookies, body).
        /// Returns null when no route matches and the path isn't under an API prefix.
        /// </summary>
        public PageResult Handle(Dictionary<string, object> request)
        {
            Complete(request);
            PageResult result = null;
            OnBigStack(() => result = HandleHere(request));
            // The session (KScript.Auth) is saved once the request is answered, and its cookie sent.
            List<string> cookies = RequestHooks.Finish(request);
            result?.Cookies.AddRange(cookies);
            return result;
        }

        private static void Complete(Dictionary<string, object> request)
        {
            request.TryAdd("method", "GET");
            request.TryAdd("path", "/");
            request.TryAdd("url", request["path"]);
            request.TryAdd("query", new Dictionary<string, object>());
            request.TryAdd("form", new Dictionary<string, object>());
            request.TryAdd("headers", new Dictionary<string, object>());
            request.TryAdd("cookies", new Dictionary<string, object>());
            request.TryAdd("body", string.Empty);
            request.TryAdd("ip", "127.0.0.1");
            request["params"] = new Dictionary<string, object>();
            request["method"] = Values.ToStr(request["method"]).ToUpperInvariant();
        }

        private PageResult HandleHere(Dictionary<string, object> request)
        {
            var ctx = new RequestContext(this, request);
            string method = (string)request["method"];
            string path = Values.ToStr(request["path"]);
            try
            {
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
                request["route"] = route.Name;

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
            catch (KScriptException ex) when (ErrorTypes.StatusOf(ex) is int status)
            {
                return ToPage(ErrorAnswer(ctx, ex, status), method);
            }
            catch (KScriptException ex)
            {
                return ToPage(Failure(ctx, ex.Location != null ? $"{ex.Location}: {ex.Message}" : ex.Message), method);
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

        private HttpResponse Failure(RequestContext ctx, string message)
        {
            Log?.WriteLine("  error: " + message);
            return ctx.ErrorResponse(500, message);
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
            request["json"] = json;
            var input = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string part in new[] { "query", "form" })
            {
                if (request[part] is Dictionary<string, object> values) foreach (var kv in values) input[kv.Key] = kv.Value;
            }
            if (json is Dictionary<string, object> fields) foreach (var kv in fields) input[kv.Key] = kv.Value;
            request["input"] = input;
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
                ["json"] = request["json"],
                ["body"] = request["json"] ?? request["body"],
                ["query"] = request["query"],
                ["form"] = request["form"],
                ["input"] = request["input"],
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
            if (wanted == null) return interpreter.Call(route.Controller, new object[] { request });
            var named = wanted.Where(available.ContainsKey).ToDictionary(n => n, n => available[n], StringComparer.Ordinal);
            if (route.Controller is UserFunction typed) CheckTypedParameters(ctx, interpreter, typed, named, parameters);
            return interpreter.Call(route.Controller, Array.Empty<object>(), named);
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
                    continue;
                }
                try
                {
                    named[p.Name] = Shapes.Convert(interpreter, value, p.Type, what, fn.Closure, fn.Module);
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
                ContentType = response.ContentType ?? "text/plain; charset=utf-8",
            };
            foreach (var h in response.Headers) page.Headers[h.Key] = Values.ToStr(h.Value);
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

        private static PageResult ErrorPage(KScriptException ex) =>
            PageServer.ErrorPage(new PageException(ex.Location != null ? $"{ex.Location}: {ex.Message}" : ex.Message));

        internal bool Throttle(string key, int limit, double seconds, out int remaining, out double retryAfter)
        {
            lock (throttle)
            {
                DateTime now = Clock();
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
