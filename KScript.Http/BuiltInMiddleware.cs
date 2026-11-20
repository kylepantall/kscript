using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using KScript.Runtime;

namespace KScript.Http
{
    /// <summary>
    /// The middleware that ship with KScript.Http. Each takes an optional argument after a colon:
    /// <list type="bullet">
    /// <item><c>log</c>: writes each request, the route, what answered it, the status and the time to the server log.</item>
    /// <item><c>cors</c>: answers browser preflight requests and adds the Access-Control headers (http.cors in kscript.json).</item>
    /// <item><c>auth</c>: needs <c>Authorization: Bearer &lt;token&gt;</c> matching the API_TOKEN environment variable
    /// (http.auth.token_env, or <c>auth:OTHER_VARIABLE</c>).</item>
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
            string variable = string.IsNullOrWhiteSpace(argument) ? ctx.Settings.AuthTokenEnv : argument;
            string expected = ctx.App.Environment(variable);
            if (string.IsNullOrEmpty(expected))
            {
                // Fail closed: a missing token never lets requests through.
                return ctx.ErrorResponse(500, $"The auth middleware needs the {variable} environment variable to hold the API token.");
            }

            string header = Header(request, "authorization") ?? string.Empty;
            string token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header.Substring(7).Trim() : null;
            if (token == null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
            {
                HttpResponse denied = ctx.ErrorResponse(401, token == null ? "This needs an Authorization: Bearer token." : "The token isn't right.");
                denied.Headers["WWW-Authenticate"] = "Bearer";
                return denied;
            }
            request["authenticated"] = true;
            return next();
        }

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
