using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using KScript.Auth;
using KScript.Runtime;

namespace KScript.Http
{
    /// <summary>
    /// The middleware that ship with KScript.Http. Each takes an optional argument after a colon:
    /// <list type="bullet">
    /// <item><c>log</c>: writes each request, the route, what answered it, the status and the time to the server log.</item>
    /// <item><c>cors</c>: answers browser preflight requests and adds the Access-Control headers (http.cors in kscript.json).</item>
    /// <item><c>auth</c>: needs someone signed in (KScript.Auth): a session, an API token, or the shared token in the
    /// API_TOKEN environment variable (http.auth.token_env). <c>auth:session</c> and <c>auth:token</c> take only one
    /// kind; <c>auth:OTHER_VARIABLE</c> takes only the shared token in that variable.</item>
    /// <item><c>guest</c>: only for people who aren't signed in, such as the sign-in page.</item>
    /// <item><c>csrf</c>: forms must send back the session's CSRF token (web routes in files that import KScript.Auth get it).</item>
    /// <item><c>throttle</c>: allows 60 requests a minute per address and route (http.throttle, or <c>throttle:30</c>).</item>
    /// <item><c>etag</c>, <c>cache_control</c> and <c>cache_response</c>: HTTP caching (see <see cref="HttpCaching"/>).</item>
    /// </list>
    /// </summary>
    internal static class BuiltInMiddleware
    {
        public static HttpResponse Run(MiddlewareRef middleware, RequestContext ctx, Dictionary<string, object> request, Func<HttpResponse> next) =>
            middleware.Name switch
            {
                "log" => Log(ctx, request, next),
                "cors" => Cors(ctx, request, next),
                "auth" => Auth(middleware.Argument, ctx, request, next),
                "guest" => Guest(ctx, request, next),
                "csrf" => Csrf(ctx, next),
                "throttle" => Throttle(middleware.Argument, ctx, request, next),
                "etag" => HttpCaching.Etag(middleware.Argument, request, next),
                "cache_control" => HttpCaching.SetCacheControl(middleware.Argument, request, next),
                "cache_response" => HttpCaching.CacheResponse(middleware.Argument, ctx, request, next),
                _ => throw new KScriptException($"There is no built-in middleware '{middleware.Name}'.", "Http"),
            };

        internal static string Header(Dictionary<string, object> request, string name) =>
            request.TryGetValue("headers", out object h) && h is Dictionary<string, object> headers && headers.TryGetValue(name, out object v) ? Values.ToStr(v) : null;

        private static HttpResponse Log(RequestContext ctx, Dictionary<string, object> request, Func<HttpResponse> next)
        {
            var watch = Stopwatch.StartNew();
            HttpResponse response = next();
            Route route = ctx.Route;
            ctx.App.Log?.WriteLine($"  {request["method"]} {request["url"]} -> {route?.Target ?? "-"}{(route?.Name != null ? $" ({route.Name})" : string.Empty)}: {response.Status} in {watch.Elapsed.TotalMilliseconds:0.0} ms");
            return response;
        }

        private static HttpResponse Cors(RequestContext ctx, Dictionary<string, object> request, Func<HttpResponse> next)
        {
            var settings = ctx.Settings;
            string origin = Header(request, "origin");
            bool any = settings.CorsOrigins.Contains("*");
            bool allowed = origin != null && (any || settings.CorsOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase));

            HttpResponse response;
            if (Values.ToStr(request["method"]) == "OPTIONS" && Header(request, "access-control-request-method") != null)
            {
                response = new HttpResponse(204, string.Empty, null);
                if (allowed)
                {
                    response.Headers["Access-Control-Allow-Methods"] = string.Join(", ", settings.CorsMethods);
                    response.Headers["Access-Control-Allow-Headers"] = string.Join(", ", settings.CorsHeaders);
                    response.Headers["Access-Control-Max-Age"] = "600";
                }
            }
            else
            {
                response = next();
            }

            if (allowed)
            {
                // A wildcard can't be used with credentials, so the origin is echoed back then.
                response.Headers["Access-Control-Allow-Origin"] = any && !settings.CorsCredentials ? "*" : origin;
                if (!any || settings.CorsCredentials) response.Headers["Vary"] = "Origin";
                if (settings.CorsCredentials) response.Headers["Access-Control-Allow-Credentials"] = "true";
            }
            return response;
        }

        private static HttpResponse Auth(string argument, RequestContext ctx, Dictionary<string, object> request, Func<HttpResponse> next)
        {
            string mode = string.IsNullOrWhiteSpace(argument) ? null : argument.Trim();
            if (mode != null && mode != "session" && mode != "token") return SharedToken(mode, ctx, request, next);

            AuthRequest auth = ctx.Auth;
            Dictionary<string, object> user = mode == "session" ? auth.SessionUser : mode == "token" ? auth.TokenUser : auth.User;
            if (user != null)
            {
                request["user"] = user;
                request["authenticated"] = true;
                return next();
            }

            // The shared token (API_TOKEN) still works, for scripts and machines that aren't users.
            string token = auth.BearerToken;
            string expected = mode == "session" ? null : ctx.App.Environment(ctx.Settings.AuthTokenEnv);
            if (token != null && !string.IsNullOrEmpty(expected) && AuthApp.SameSecret(token, expected))
            {
                request["authenticated"] = true;
                return next();
            }

            string login = ctx.Settings.Auth.Login;
            string path = Values.ToStr(request["path"]);
            if (WantsJson(ctx, request) || mode == "token" || string.IsNullOrEmpty(login) || path == login)
            {
                HttpResponse denied = ctx.ErrorResponse(401, token == null ? "Sign in first, or send an Authorization: Bearer token." : "The token isn't right.");
                denied.Headers["WWW-Authenticate"] = "Bearer";
                return denied;
            }
            // A person in a browser is sent to sign in, and comes back here afterwards (@intended()).
            if (Values.ToStr(request["method"]) == "GET") auth.RememberIntended(Values.ToStr(request["url"]));
            return HttpPackage.Redirect(login, 302);
        }

        /// <summary>True for API routes, and for requests that ask for JSON or send a bearer token: they get a 401, not a redirect.</summary>
        private static bool WantsJson(RequestContext ctx, Dictionary<string, object> request)
        {
            if (ctx.Route?.Api ?? false) return true;
            if (Header(request, "authorization") != null) return true;
            string accept = Header(request, "accept") ?? string.Empty;
            return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary><c>auth:VARIABLE</c>: only the shared bearer token in that environment variable.</summary>
        private static HttpResponse SharedToken(string variable, RequestContext ctx, Dictionary<string, object> request, Func<HttpResponse> next)
        {
            string expected = ctx.App.Environment(variable);
            if (string.IsNullOrEmpty(expected))
            {
                // Fail closed: a missing token never lets requests through.
                return ctx.ErrorResponse(500, $"The auth:{variable} middleware needs the {variable} environment variable to hold the API token.");
            }

            string token = ctx.Auth.BearerToken;
            if (token == null || !AuthApp.SameSecret(token, expected))
            {
                HttpResponse denied = ctx.ErrorResponse(401, token == null ? "This needs an Authorization: Bearer token." : "The token isn't right.");
                denied.Headers["WWW-Authenticate"] = "Bearer";
                return denied;
            }
            request["authenticated"] = true;
            return next();
        }

        private static HttpResponse Guest(RequestContext ctx, Dictionary<string, object> request, Func<HttpResponse> next)
        {
            if (ctx.Auth.User == null) return next();
            if (WantsJson(ctx, request)) return ctx.ErrorResponse(403, "You're already signed in.");
            return HttpPackage.Redirect(ctx.Settings.Auth.Home, 302);
        }

        private static HttpResponse Csrf(RequestContext ctx, Func<HttpResponse> next) =>
            ctx.Auth.CsrfValid() ? next() : ctx.ErrorResponse(419, "This form has expired. Go back, reload the page and try again.");

        private static HttpResponse Throttle(string argument, RequestContext ctx, Dictionary<string, object> request, Func<HttpResponse> next)
        {
            int limit = ctx.Settings.ThrottleLimit;
            if (!string.IsNullOrWhiteSpace(argument) && (!int.TryParse(argument, out limit) || limit <= 0))
                throw new KScriptException($"throttle:{argument}: the limit is a whole number of requests, such as throttle:30.", "Http");
            string key = Values.ToStr(request["ip"]) + " " + (ctx.Route?.Pattern.Path ?? Values.ToStr(request["path"]));
            bool ok = ctx.App.Throttle(key, limit, ctx.Settings.ThrottleSeconds, out int remaining, out double retryAfter);
            HttpResponse response = ok ? next() : ctx.ErrorResponse(429, "Too many requests. Try again in a moment.");
            response.Headers["X-RateLimit-Limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            response.Headers["X-RateLimit-Remaining"] = remaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!ok) response.Headers["Retry-After"] = Math.Ceiling(retryAfter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return response;
        }
    }
}
