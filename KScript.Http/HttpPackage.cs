using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Http
{
    /// <summary>
    /// KScript.Http: routes, controllers and middleware for <c>kscript serve</c>, in the spirit of Laravel's
    /// routes/web.php and routes/api.php. A file whose root is &lt;KScript.Routes&gt; maps HTTP methods and paths to
    /// controller functions, views or redirects. Controllers are ordinary &lt;fn&gt;s; what they return becomes the
    /// response (a map or list is JSON, text is HTML, <c>@view()</c> renders a page). Middleware is a
    /// <c>fn(request, next)</c> or a built-in (log, cors, auth, throttle), applied to the whole file, a
    /// &lt;Group&gt; or one route.
    /// </summary>
    public sealed class HttpPackage : KScriptPackage
    {
        /// <summary>Where the request being handled is kept in <see cref="KScriptHost.Items"/>.</summary>
        public const string ContextKey = "KScript.Http.Request";

        /// <summary>The middleware that ship with KScript.Http.</summary>
        public static readonly string[] BuiltInMiddleware = { "log", "cors", "auth", "guest", "csrf", "throttle", "etag", "cache_control", "cache_response" };

        /// <summary>The method elements: &lt;Get&gt;, &lt;Post&gt;... and the HTTP method each answers.</summary>
        public static readonly (string Element, string Method)[] MethodElements =
        {
            ("Get", "GET"), ("Post", "POST"), ("Put", "PUT"), ("Patch", "PATCH"), ("Delete", "DELETE"), ("Options", "OPTIONS"), ("Any", null),
        };

        public static readonly string[] HttpMethods = { "get", "post", "put", "patch", "delete", "options", "head" };

        // @api('/api/notes') in any script goes to its project's routes once KScript.Http is loaded.
        static HttpPackage() => LocalApi.Install();

        public override string Name => "KScript.Http";

        public override string Description => "Routes, controllers and middleware: <KScript.Routes> files served by kscript serve.";

        private static readonly Signature MiddlewareSignature = Signature.Parse("request: map, next: fn");

        private static A Middleware(string description, string name = "middleware", bool required = false) =>
            new A(name, K.FunctionReferences, description, required, BuiltInMiddleware) { FunctionSignature = MiddlewareSignature };

        protected override void Register(PackageBuilder p)
        {
            p.Element(new ElementInfo("Routes", "A route file: maps HTTP methods and paths to controllers, views and redirects. kscript serve loads routes.ks.xml (or routes.kscript) and api.ks.xml (under /api, with JSON errors). Its <import>, <fn> and <let> children run on every request before the route is matched.", ContentKind.Statements,
                new A("prefix", K.Text, "A path in front of every route in the file, such as /v1.") { Type = "slash_path" },
                Middleware("Middleware for every route in the file, comma separated: built-ins (log, cors, auth, throttle:30) or <fn>s taking ($request, $next)."),
                new A("name", K.Text, "Put in front of every route name, such as admin."),
                new A("api", K.Bool, "true: errors and missing routes are answered with JSON (api.* files are API files already)."))
            {
                Context = ElementContext.Script,
                Example = "<KScript.Routes>\n  <import path=\"controllers/people.ks.xml\" as=\"people\" />\n  <Middleware use=\"log\" />\n  <Get path=\"/\" view=\"home.html\" />\n  <Get path=\"/people/{id}\" to=\"people.show\" name=\"people.show\" />\n</KScript.Routes>",
            }, RunRoutes);

            foreach (var (element, method) in MethodElements)
            {
                string what = method == null ? "any method" : method;
                p.Element(new ElementInfo(element, $"A route for {what} requests to a path. It is answered by a controller (to), a view, a redirect, or the statements inside it (<return value=\"...\" />).", ContentKind.Statements, RouteAttributes())
                {
                    Context = ElementContext.Script,
                    MustBeInside = new[] { "Routes", "Group" },
                    InsideAnyAncestor = true,
                    IsFunctionBody = true,
                    Example = "<KScript.Routes>\n  " + (method == "GET" || method == null
                        ? $"<{element} path=\"/people/{{id}}\" to=\"people.show\" name=\"people.show\" where=\"id: int\" />"
                        : $"<{element} path=\"/people/{{id}}\" to=\"people.{(method == "DELETE" ? "destroy" : method == "POST" ? "store" : "update")}\" middleware=\"auth\" />") + "\n</KScript.Routes>",
                }, run => AddRoute(run, method == null ? Array.Empty<string>() : new[] { method }));
            }

            var routeAttrs = RouteAttributes().ToList();
            routeAttrs.Insert(1, new A("methods", K.Text, "The HTTP methods: [get, post].", true) { NameList = true });
            p.Element(new ElementInfo("Route", "A route for several HTTP methods at once.", ContentKind.Statements, routeAttrs.ToArray())
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "Routes", "Group" },
                    InsideAnyAncestor = true,
                IsFunctionBody = true,
                Example = "<KScript.Routes>\n  <Route methods=\"get, post\" path=\"/contact\" to=\"contact.handle\" />\n</KScript.Routes>",
            }, run =>
            {
                var methods = NameList.Names(run.RequiredRaw("methods"))
                    .Select(m => m.ToUpperInvariant()).ToList();
                foreach (string m in methods)
                {
                    if (!HttpMethods.Contains(m.ToLowerInvariant()))
                        throw run.Error($"'{m}' isn't an HTTP method. Use {string.Join(", ", HttpMethods)}.", "methods", "Http");
                }
                return AddRoute(run, methods);
            });

            p.Element(new ElementInfo("Group", "Routes that share a path prefix, middleware or a name prefix.", ContentKind.Statements,
                new A("prefix", K.Text, "A path in front of every route inside, such as /admin.") { Type = "slash_path" },
                Middleware("Middleware for every route inside, comma separated."),
                new A("name", K.Text, "Put in front of every route name inside, such as admin."))
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "Routes", "Group" },
                    InsideAnyAncestor = true,
                Example = "<KScript.Routes>\n  <Group prefix=\"/admin\" middleware=\"auth\" name=\"admin.\">\n    <Get path=\"/\" to=\"admin.dashboard\" name=\"home\" />\n  </Group>\n</KScript.Routes>",
            }, run =>
            {
                RequestContext ctx = Context(run);
                ctx.Push(run.Raw("prefix"), run.Raw("middleware"), run.Raw("name"), api: null);
                try
                {
                    run.Run(run.Element.Children, run.Scope.Child());
                }
                finally
                {
                    ctx.Pop();
                }
                return null;
            });

            p.Element(new ElementInfo("Middleware", "Middleware for the routes that follow it in this file or group.", ContentKind.Empty,
                Middleware("Built-ins or custom functions to run before the routes that follow.", name: "use", required: true))
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "Routes", "Group" },
                    InsideAnyAncestor = true,
                Example = "<KScript.Routes>\n  <Middleware use=\"log, cors\" />\n</KScript.Routes>",
            }, run =>
            {
                Context(run).Top.Middleware.AddRange(ResolveMiddleware(run, run.RequiredRaw("use"), "use"));
                return null;
            });

            p.Element(new ElementInfo("Resource", "The usual routes of a JSON resource, answered by the functions a controller script exports: index (GET path), store (POST path), show (GET path/{id}), update (PUT and PATCH path/{id}) and destroy (DELETE path/{id}). Functions the controller doesn't have get no route.", ContentKind.Empty,
                new A("path", K.Text, "The resource's path, such as /people.", true) { Literal = true, Type = "slash_path" },
                new A("to", K.Text, "The controller: the name a script is imported as (<import path=\"...\" as=\"people\" />).", true) { IsName = true },
                new A("only", K.Text, "Only these actions: [index, show].") { NameList = true },
                new A("except", K.Text, "All actions but these: [destroy].") { NameList = true },
                new A("name", K.Text, "The route name prefix (people gives people.index, people.show...). The default is the controller's name."),
                new A("param", K.Text, "The name of the path parameter, id by default."),
                Middleware("Middleware for these routes, comma separated."))
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "Routes", "Group" },
                    InsideAnyAncestor = true,
                Example = "<KScript.Routes>\n  <import path=\"controllers/people.ks.xml\" as=\"people\" />\n  <Resource path=\"/people\" to=\"people\" middleware=\"auth\" />\n</KScript.Routes>",
            }, AddResource);

            p.Element(new ElementInfo("ModelUpdate", "Lets a named HTML model form edit selected fields of a record owned by the signed-in user.", ContentKind.Empty,
                new A("name", K.Text, "The binding name used by <form model-update>.", true) { IsName = true },
                new A("model", K.Text, "The model in the project's models folder.", true) { IsName = true },
                new A("owner", K.Text, "The column holding the authenticated user's ID.", true) { IsName = true },
                new A("fields", K.Text, "Writable model fields, for example [title, body].", true) { NameList = true },
                Middleware("Additional authorization or other middleware for this binding."))
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "Routes", "Group" },
                InsideAnyAncestor = true,
                Example = "<KScript.Routes><ModelUpdate name=\"edit_note\" model=\"Note\" owner=\"user_id\" fields=\"title, body\" /></KScript.Routes>",
            }, ModelFormBinding.Declare);

            p.Command("view", "name, data...",
                "Renders a page from the views folder (or the site folder) with its layout, giving it the named values as variables: @view('people/show.html', person: $p). The extension may be left off.",
                KType.HttpResponse, View, "<return value=\"@view('people/show', person: $person)\" />");
            p.Command("response", "body?, status?: number, type?, headers?: map",
                "A response with a status code and headers. A map or list body is sent as JSON, text as HTML (or the type you give).",
                KType.HttpResponse, Response, "<return value=\"@response($person, status: 201)\" />");
            p.Command("redirect", "to, status?: number", "A redirect to another address (302 Found by default; 301 for a permanent move).",
                KType.HttpResponse, c => Redirect(c.Str(0), c.Has(1, "status") ? (int)Values.ToNumber(c.Arg(1, "status"), "status") : 302),
                "<return value=\"@redirect('/people')\" />");
            p.Command("abort", "status: number, message?", "Stops the request with an error status, such as @abort(404) or @abort(403, 'Admins only').",
                KType.Any, c =>
                {
                    int status = (int)Values.ToNumber(c.Arg(0, "status"), "status");
                    string message = c.Has(1, "message") ? Values.ToStr(c.Arg(1, "message")) : null;
                    throw new HttpAbortSignal(RequestContext.Current(c.Host)?.ErrorResponse(status, message) ?? HttpResponse.Text(message ?? ReasonPhrase(status), status));
                }, "<if $person == null><do>@abort(404)</do></if>");
            p.Command("etag", "value, weak?: bool",
                "Tags the answer with an ETag made from a value (such as a record's version or updated time). When the request's If-None-Match already has it, the request stops here with 304 Not Modified, so the rest of the controller doesn't run.",
                KType.String, HttpCaching.EtagCommand, "<do>@etag($person.updated_at)</do>");
            p.Command("route", "name: string(min: 1), params...", "Returns the URL for a named route. Pass the route name as nonempty text and supply the values for its path parameters. For example, @route('people.show', id: 3) returns /people/3. Extra named values become query parameters.",
                KType.String, c =>
                {
                    if (Values.Unwrap(c.Need(0)) is not string name || string.IsNullOrWhiteSpace(name))
                        throw c.Error("@route() needs a nonempty route name as text, for example 'people.show'.", "Argument");
                    RouteTable table = RouteCatalog.Current(c).Table;
                    var values = new Dictionary<string, object>(c.Named, StringComparer.Ordinal);
                    if (c.Count > 1 && Values.Unwrap(c.Arg(1)) is Dictionary<string, object> map) foreach (var kv in map) values[kv.Key] = kv.Value;
                    return table.Url(name, values);
                }, "<a href=\"{@route('people.show', id: $p.id)}\">");

            RouteCatalog.Register(p);

            foreach (var f in HttpResponse.Fields)
                p.Field(KType.HttpResponse, f.Name, f.Type, f.Description);
            foreach (var m in HttpResponse.Methods)
                p.Method(KType.HttpResponse, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((HttpResponse)t).TryCallMethod(c, m.Name, out object r) ? r : null);

            p.Variable("request", KType.Request,
                "The HTTP request. Inside a matched route it also contains the client ip, parsed json, combined input and route name.",
                (i, m) => RequestContext.Current(i.Host)?.Request);
        }

        private static A[] RouteAttributes() => new[]
        {
            new A("path", K.Text, "The path, with {name} parameters: /people/{id}. {name?} is optional and {name*} takes the rest of the path.", true) { Literal = true, Type = "slash_path" },
            new A("to", K.FunctionReference, "The controller: a <fn> (`@show`) or a function of an imported script (`@people.show`); in XML, to=\"people.show\". Its parameters are filled by name: path parameters, request, json, query, form, input, headers."),
            new A("view", K.Text, "Answer with this view instead, from the views folder, with the path parameters as variables."),
            new A("redirect", K.Text, "Answer with a redirect to this address instead."),
            new A("status", K.Expression, "The redirect's status code, 302 by default."),
            new A("name", K.Text, "A name for the route, for @route('name', ...)."),
            new A("label_key", K.Text, "A translation key for the route's display label."),
            new A("data", K.Expression, "Optional route metadata made from a declared record type.") { RequiresDeclaredRecord = true },
            Middleware("Middleware for this route, comma separated."),
            new A("where", K.Text, "Rules for the parameters: where=\"id: int, slug: slug\". Rules: number, int, alpha, alnum, slug, uuid or a regular expression.") { Literal = true },
            new A("etag", K.Bool, "true tags GET answers with an ETag (a hash of the body) and answers If-None-Match with 304 Not Modified when the client already has that version."),
            new A("cache", K.Text, "The Cache-Control header for GET answers: public, max-age=300, or just a number of seconds (300 is public for 5 minutes), private, no-store...") { Literal = true },
        };

        internal static RequestContext Context(ElementRun run) =>
            RequestContext.Current(run.Host) ?? throw run.Error($"<{run.Element.Name}> only works in route files served by kscript serve (or listed by kscript routes).", null, "Context");

        private static bool? RunRoutes(ElementRun run)
        {
            RequestContext ctx = Context(run);
            bool? api = run.Has("api") ? run.Flag("api") : null;
            ctx.Push(run.Raw("prefix"), run.Raw("middleware"), run.Raw("name"), api);
            try
            {
                run.Run(run.Element.Children, run.Scope);
            }
            finally
            {
                ctx.Pop();
            }
            return null;
        }

        private static bool? AddRoute(ElementRun run, IReadOnlyList<string> methods)
        {
            RequestContext ctx = Context(run);
            string to = run.Raw("to")?.Trim().TrimStart('@');
            string view = run.Raw("view")?.Trim();
            string redirect = run.Raw("redirect")?.Trim();
            bool inline = run.Element.ChildElements.Any() || run.Element.Children.OfType<MarkupText>().Any(t => !t.IsWhitespace);
            int answers = (to != null ? 1 : 0) + (view != null ? 1 : 0) + (redirect != null ? 1 : 0) + (inline ? 1 : 0);
            if (answers == 0)
                throw run.Error($"<{run.Element.Name}> needs something to answer with: to=\"controller.action\", view=\"page.html\", redirect=\"/somewhere\", or statements inside it.", null, "Http");
            if (answers > 1)
                throw run.Error($"<{run.Element.Name}> has more than one answer. Use only one of to, view, redirect or statements inside it.", null, "Http");

            RoutePattern pattern;
            try
            {
                pattern = new RoutePattern(RoutePattern.Join(ctx.Prefix, run.RequiredRaw("path")), run.Raw("where"));
            }
            catch (KScriptException ex)
            {
                throw run.Error(ex.Words, "path", "Http");
            }

            var middleware = ctx.MiddlewareFor(run).ToList();
            AddCsrf(run, ctx, middleware);
            middleware.AddRange(ResolveMiddleware(run, run.Raw("middleware")));
            AddCaching(run, middleware);
            string name = run.Raw("name")?.Trim();
            object data = run.Has("data") ? Values.Unwrap(run.Eval("data")) : null;
            if (run.Has("data") && data is not Record { Type.Kind: null })
                throw run.Error("Route data must be a value of a declared record, such as NavigationData(order: 10).", "data", "Type");
            ctx.Add(run, new Route
            {
                Methods = methods,
                Pattern = pattern,
                Name = name != null ? ctx.NamePrefix + name : null,
                LabelKey = run.Raw("label_key"),
                Data = data as Record,
                Middleware = middleware,
                Controller = to != null ? ResolveFunction(run, to, "to") : null,
                ControllerName = to,
                View = view,
                RedirectTo = redirect,
                RedirectStatus = run.Has("status") ? (int)run.Number("status", 302) : 302,
                Inline = inline ? run.Element : null,
                Element = run.Element,
                Module = run.Module,
                Scope = run.Scope,
                Api = ctx.Api,
                File = run.Module.Path,
                Line = run.Module.Source.GetPosition(run.Element.Span.Start).Line + 1,
            });
            return null;
        }

        /// <summary>
        /// Web routes in a file that imports KScript.Auth check the CSRF token of forms (POST, PUT, PATCH, DELETE), so
        /// another site can't submit them with the visitor's session. http.auth.csrf: false turns it off; API routes
        /// don't get it, since they are signed in with tokens, not cookies.
        /// </summary>
        private static void AddCsrf(ElementRun run, RequestContext ctx, List<MiddlewareRef> middleware)
        {
            if (ctx.Api || !ctx.Settings.Auth.Csrf || !run.Module.Imports("KScript.Auth") || middleware.Any(m => m.Function == null && m.Name == "csrf")) return;
            middleware.Add(new MiddlewareRef("csrf", null, null));
        }

        /// <summary>A route's etag and cache attributes, as the etag and cache_control middleware closest to the controller.</summary>
        private static void AddCaching(ElementRun run, List<MiddlewareRef> middleware)
        {
            if (run.Has("cache"))
            {
                try
                {
                    middleware.Add(new MiddlewareRef("cache_control", HttpCaching.CacheControl(run.Raw("cache")), null));
                }
                catch (KScriptException ex)
                {
                    throw run.Error(ex.Words, "cache", "Http");
                }
            }
            if (run.Flag("etag")) middleware.Add(new MiddlewareRef("etag", null, null));
        }

        private static readonly (string Action, string[] Methods, bool Member)[] ResourceActions =
        {
            ("index", new[] { "GET" }, false),
            ("store", new[] { "POST" }, false),
            ("show", new[] { "GET" }, true),
            ("update", new[] { "PUT", "PATCH" }, true),
            ("destroy", new[] { "DELETE" }, true),
        };

        private static bool? AddResource(ElementRun run)
        {
            RequestContext ctx = Context(run);
            string to = run.RequiredRaw("to").Trim();
            if (!run.Module.TryGetAlias(to, out ImportNamespace controller) || controller.Module == null)
                throw run.Error($"<Resource to=\"{to}\"> names a controller script imported with as=\"{to}\": <import path=\"controllers/{to}.ks.xml\" as=\"{to}\" />.", "to", "Http");
            HashSet<string> only = Names(run.Raw("only")), except = Names(run.Raw("except"));
            foreach (string action in only.Concat(except).Where(a => !ResourceActions.Any(r => r.Action == a)))
                throw run.Error($"'{action}' isn't a resource action. Actions: {string.Join(", ", ResourceActions.Select(r => r.Action))}.", only.Contains(action) ? "only" : "except", "Http");

            string param = run.Raw("param")?.Trim() ?? "id";
            string prefix = (run.Raw("name")?.Trim() ?? to) + ".";
            var middleware = ctx.MiddlewareFor(run).ToList();
            AddCsrf(run, ctx, middleware);
            middleware.AddRange(ResolveMiddleware(run, run.Raw("middleware")));
            string basePath = RoutePattern.Join(ctx.Prefix, run.RequiredRaw("path"));
            foreach (var (action, methods, member) in ResourceActions)
            {
                if (only.Count > 0 && !only.Contains(action)) continue;
                if (except.Contains(action)) continue;
                if (!controller.TryGet(action, out object fn) || fn is not ICallable callable)
                {
                    if (only.Contains(action)) throw run.Error($"{controller.Display} has no {action} function for <Resource only=\"...{action}\">.", "only", "Http");
                    continue;
                }
                ctx.Add(run, new Route
                {
                    Methods = methods,
                    Pattern = new RoutePattern(member ? RoutePattern.Join(basePath, "{" + param + "}") : basePath),
                    Name = ctx.NamePrefix + prefix + action,
                    Middleware = middleware,
                    Controller = callable,
                    ControllerName = to + "." + action,
                    Module = run.Module,
                    Scope = run.Scope,
                    Api = ctx.Api,
                    File = run.Module.Path,
                    Line = run.Module.Source.GetPosition(run.Element.Span.Start).Line + 1,
                });
            }
            return null;
        }

        private static HashSet<string> Names(string list) =>
            new HashSet<string>(NameList.Names(list), StringComparer.Ordinal);

        /// <summary>
        /// Finds a controller or middleware function: a <c>&lt;fn&gt;</c> in scope (<c>show</c>), a function of a
        /// script or package imported with as (<c>people.show</c>), or a command.
        /// </summary>
        internal static ICallable ResolveFunction(ElementRun run, string text, string attribute, Signature contract = null)
        {
            string name = text.Trim().TrimStart('@');
            int dot = name.IndexOf('.');
            if (dot > 0)
            {
                string alias = name[..dot], member = name[(dot + 1)..];
                if (!run.Module.TryGetAlias(alias, out ImportNamespace ns))
                {
                    string close = Values.Closest(alias, run.Module.AliasNames);
                    throw run.Error(Wording.Of($"Nothing is imported as '{alias}'. Import the controller with <import path=\"controllers/{alias}{ScriptFiles.Code}\" as=\"{alias}\" />.") + (close != null ? $" Did you mean {close}?" : string.Empty), attribute, "Http");
                }
                if (ns.TryGet(member, out object found))
                {
                    if (found is ICallable fn) return Check(fn);
                    if (found is CommandDefinition cmd) return Check(Command(run.Interpreter, cmd), cmd.Info.Signatures);
                    throw run.Error($"{ns.Display}'s '{member}' is {Values.Describe(found)}, not a function.", attribute, "Http");
                }
                string suggestion = Values.Closest(member, ns.Names);
                throw run.Error($"{ns.Display} has no function '{member}'." + (suggestion != null ? $" Did you mean {alias}.{suggestion}?" : $" It exports: {string.Join(", ", ns.Names)}."), attribute, "Http");
            }

            if (run.Scope.TryGet(name, out object value) && value is ICallable callable) return Check(callable);
            if (run.Module.Commands.TryGetValue(name, out CommandDefinition command))
                return Check(Command(run.Interpreter, command), command.Info.Signatures);
            string near = Values.Closest(name, run.Scope.Names().Where(n => run.Scope.TryGet(n, out object f) && f is ICallable));
            throw run.Error(Wording.Of($"No function named '{name}'. Declare it with <fn name=\"{name}\"> or import a controller (<import path=\"...\" as=\"people\" /> then people.{name}).")
                + (near != null ? $" Did you mean {near}?" : string.Empty), attribute, "Http");

            ICallable Check(ICallable fn, IReadOnlyList<Signature> signatures = null)
            {
                if (contract == null) return fn;
                if (fn is UserFunction user)
                    signatures = new[] { user.PositionalSignature };
                if (signatures != null && !signatures.Any(s => s.AcceptsPositionalCall(contract)))
                    throw run.Error($"Middleware '{name}' must accept {contract.FunctionLabel}.", attribute, "Http");
                return fn;
            }
        }

        private static ICallable Command(Interpreter interpreter, CommandDefinition cmd) =>
            new NativeFunction(cmd.Name, call => interpreter.InvokeCommand(cmd, call));

        /// <summary>Reads a middleware list: <c>[log, cors, auth:ADMIN_TOKEN, throttle:30, my_check]</c> (or the same without brackets).</summary>
        internal static List<MiddlewareRef> ResolveMiddleware(ElementRun run, string list, string attribute = "middleware")
        {
            var result = new List<MiddlewareRef>();
            if (string.IsNullOrWhiteSpace(list)) return result;
            RequestContext ctx = Context(run);
            foreach (string raw in NameList.Names(list))
            {
                int colon = raw.IndexOf(':');
                string name = colon > 0 ? raw[..colon].Trim() : raw;
                string argument = colon > 0 ? raw[(colon + 1)..].Trim() : null;
                if (ctx.Settings.Groups.TryGetValue(name, out List<string> group))
                {
                    result.AddRange(ResolveMiddleware(run, string.Join(",", group), attribute));
                    continue;
                }
                // A <fn> of the same name replaces a built-in; a variable such as $log does not.
                if (BuiltInMiddleware.Contains(name) && !(run.Scope.TryGet(name, out object own) && own is ICallable))
                {
                    result.Add(new MiddlewareRef(name, argument, null));
                    continue;
                }
                result.Add(new MiddlewareRef(name, argument, ResolveFunction(run, name, attribute, MiddlewareSignature)));
            }
            return result;
        }

        private static object View(CallContext c)
        {
            RequestContext ctx = RequestContext.Current(c.Host) ?? throw c.Error("@view() works while kscript serve handles a request.", "Http");
            var data = new Dictionary<string, object>(StringComparer.Ordinal);
            if (c.Count > 1 && Values.Unwrap(c.Arg(1)) is Dictionary<string, object> map)
            {
                foreach (var kv in map) data[kv.Key] = kv.Value;
            }
            foreach (var kv in c.Named) data[kv.Key] = kv.Value;
            return ctx.RenderView(c.Str(0), data);
        }

        private static object Response(CallContext c)
        {
            object body = Values.Unwrap(c.Arg(0, "body"));
            int status = c.Has(1, "status") && c.Arg(1, "status") != null ? (int)Values.ToNumber(c.Arg(1, "status"), "status") : 200;
            HttpResponse response = body is HttpResponse existing ? existing : body is string || body == null
                ? new HttpResponse(status, Values.ToStr(body))
                : HttpResponse.Json(body, status);
            response.Status = status;
            if (c.Has(2, "type") && c.Arg(2, "type") != null) response.ContentType = Values.ToStr(c.Arg(2, "type"));
            if (c.Has(3, "headers") && Values.Unwrap(c.Arg(3, "headers")) is Dictionary<string, object> headers)
            {
                foreach (var kv in headers) response.Headers[kv.Key] = Values.ToStr(kv.Value);
            }
            return response;
        }

        internal static HttpResponse Redirect(string to, int status)
        {
            var response = new HttpResponse(status, string.Empty, null);
            response.Headers["Location"] = to;
            return response;
        }

        internal static string ReasonPhrase(int status) =>
            status == 419 ? "Page Expired" : Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status) is { Length: > 0 } phrase ? phrase : "Error " + status;
    }
}
