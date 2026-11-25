using System.Security.Cryptography;
using System.Text;

using KScript.Caching;
using KScript.Runtime;

namespace KScript.Http
{
    /// <summary>
    /// HTTP caching for routes: ETags with 304 Not Modified answers, Cache-Control headers, and responses kept in
    /// KScript.Cache's memory store.
    /// <list type="bullet">
    /// <item><c>etag</c> (middleware, or <c>etag="yes"</c> on a route): tags each GET answer with a hash of its body and
    /// answers <c>If-None-Match</c> with 304 when the client already has it.</item>
    /// <item><c>cache_control:public;max-age=300</c> (or <c>cache="public, max-age=300"</c>; a number alone is
    /// public for that many seconds): sets Cache-Control.</item>
    /// <item><c>cache_response:5m</c>: keeps whole GET answers in memory and answers again from there.</item>
    /// </list>
    /// </summary>
    internal static class HttpCaching
    {
        private static readonly string[] Flags = { "public", "private", "no-cache", "no-store", "no-transform", "must-revalidate", "proxy-revalidate", "must-understand", "immutable" };
        private static readonly string[] Timed = { "max-age", "s-maxage", "stale-while-revalidate", "stale-if-error" };

        /// <summary>
        /// A Cache-Control value from a short form: directives separated by commas or semicolons, where a number alone
        /// is max-age (and public, unless private or no-store is given): <c>300</c> is <c>public, max-age=300</c>.
        /// </summary>
        public static string CacheControl(string text)
        {
            var parts = new List<string>();
            bool number = false;
            foreach (string raw in (text ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string part = raw.ToLowerInvariant().Replace("max_age", "max-age").Replace(" ", string.Empty);
                if (part.All(char.IsDigit))
                {
                    parts.Add("max-age=" + part);
                    number = true;
                    continue;
                }
                int eq = part.IndexOf('=');
                bool ok = eq > 0 ? Timed.Contains(part[..eq]) && part[(eq + 1)..].Length > 0 && part[(eq + 1)..].All(char.IsDigit) : Flags.Contains(part);
                if (!ok)
                {
                    throw new KScriptException($"'{raw}' isn't a Cache-Control directive. Use a number of seconds (300), {string.Join(", ", Flags)}, or {string.Join(", ", Timed.Select(t => t + "=seconds"))}.", "Http");
                }
                parts.Add(part);
            }
            if (parts.Count == 0) throw new KScriptException("Cache-Control needs at least one directive, such as 300 or no-store.", "Http");
            if (number && !parts.Any(p => p == "public" || p == "private" || p == "no-store")) parts.Insert(0, "public");
            return string.Join(", ", parts.Distinct());
        }

        /// <summary>A strong ETag for some text: a quoted hash. <paramref name="weak"/> gives W/"...".</summary>
        public static string Tag(string text, bool weak = false) => Tag(Encoding.UTF8.GetBytes(text ?? string.Empty), weak);

        private static string Tag(byte[] bytes, bool weak)
        {
            string hash = Convert.ToBase64String(SHA256.HashData(bytes))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_').Substring(0, 27);
            return (weak ? "W/" : string.Empty) + "\"" + hash + "\"";
        }

        public static string Tag(HttpResponse response, bool weak) =>
            response.BodyBytes is byte[] bytes ? Tag(bytes, weak) : Tag(response.Body, weak);

        /// <summary>The ETag for a value from a script: text as it is, anything else as JSON.</summary>
        public static string TagFor(object value, bool weak) => Tag(value is string s ? s : Values.ToJson(value), weak);

        /// <summary>True when an If-None-Match header names the tag (or is *). Weak and strong tags compare equal, as RFC 9110 says for GET.</summary>
        public static bool Matches(string ifNoneMatch, string tag)
        {
            if (string.IsNullOrWhiteSpace(ifNoneMatch) || string.IsNullOrEmpty(tag)) return false;
            static string Opaque(string t) => t.Trim().StartsWith("W/", StringComparison.Ordinal) ? t.Trim().Substring(2) : t.Trim();
            return ifNoneMatch.Split(',').Any(t => t.Trim() == "*" || Opaque(t) == Opaque(tag));
        }

        private static bool IsRead(Dictionary<string, object> request) => Values.ToStr(request["method"]) is "GET" or "HEAD";

        /// <summary>The 304 answer to a response the client already has: no body, but the caching headers.</summary>
        public static HttpResponse NotModified(HttpResponse response)
        {
            var answer = new HttpResponse(304, string.Empty, null);
            foreach (var h in response.Headers) answer.Headers[h.Key] = h.Value;
            return answer;
        }

        public static HttpResponse Etag(string argument, Dictionary<string, object> request, Func<HttpResponse> next)
        {
            bool weak = argument?.Trim().ToLowerInvariant() switch
            {
                null or "" or "strong" => false,
                "weak" => true,
                _ => throw new KScriptException($"etag:{argument}: use etag, etag:strong or etag:weak.", "Http"),
            };
            HttpResponse response = next();
            if (!IsRead(request) || response.Status != 200) return response;
            if (!response.Headers.ContainsKey("ETag")) response.Headers["ETag"] = Tag(response, weak);
            return Matches(BuiltInMiddleware.Header(request, "if-none-match"), Values.ToStr(response.Headers["ETag"])) ? NotModified(response) : response;
        }

        public static HttpResponse SetCacheControl(string argument, Dictionary<string, object> request, Func<HttpResponse> next)
        {
            if (string.IsNullOrWhiteSpace(argument)) throw new KScriptException("cache_control needs directives, such as cache_control:300 or cache_control:private;max-age=60.", "Http");
            string value = CacheControl(argument);
            HttpResponse response = next();
            bool cacheable = (response.Status >= 200 && response.Status < 300) || response.Status == 304;
            if (IsRead(request) && cacheable && !response.Headers.ContainsKey("Cache-Control")) response.Headers["Cache-Control"] = value;
            return response;
        }

        public static HttpResponse CacheResponse(string argument, RequestContext ctx, Dictionary<string, object> request, Func<HttpResponse> next)
        {
            double seconds = CachePackage.Seconds(string.IsNullOrWhiteSpace(argument) ? "60" : argument.Trim()).Value;
            // Requests that carry a login are never answered from (or kept in) the shared cache.
            if (ctx.Live != null || !IsRead(request) || BuiltInMiddleware.Header(request, "authorization") != null) return next();

            CacheStore store = MemoryCacheStore.Named("http:" + ctx.App.SiteRoot);
            string key = Values.ToStr(request["url"]);
            if (store.TryGet(key, out object kept, out _) && kept is Dictionary<string, object> saved)
            {
                var hit = new HttpResponse((int)(double)saved["status"], (string)saved["body"], (string)saved["type"])
                {
                    BodyBytes = saved.TryGetValue("body_bytes", out object bytes) && bytes is string encoded
                        ? Convert.FromBase64String(encoded) : null,
                };
                foreach (var h in (Dictionary<string, object>)saved["headers"]) hit.Headers[h.Key] = h.Value;
                hit.Headers["X-Cache"] = "HIT";
                return Matches(BuiltInMiddleware.Header(request, "if-none-match"), hit.Headers.TryGetValue("ETag", out object tag) ? Values.ToStr(tag) : null) ? NotModified(hit) : hit;
            }

            HttpResponse response = next();
            string control = response.Headers.TryGetValue("Cache-Control", out object c) ? Values.ToStr(c) : string.Empty;
            if (response.Status == 200 && !control.Contains("no-store", StringComparison.OrdinalIgnoreCase) && !control.Contains("private", StringComparison.OrdinalIgnoreCase))
            {
                store.Put(key, new Dictionary<string, object>
                {
                    ["status"] = (double)response.Status,
                    ["body"] = response.Body,
                    ["body_bytes"] = response.BodyBytes is byte[] bytes ? Convert.ToBase64String(bytes) : null,
                    ["type"] = response.ContentType,
                    ["headers"] = response.Headers.ToDictionary(h => h.Key, h => (object)Values.ToStr(h.Value)),
                }, CacheStore.Clock().AddSeconds(seconds));
            }
            response.Headers["X-Cache"] = "MISS";
            return response;
        }

        /// <summary>
        /// <c>@etag(value)</c> in a controller: tags the answer with a hash of the value, and when the client already
        /// has that version, stops here with 304 so the rest of the controller doesn't run.
        /// </summary>
        public static object EtagCommand(CallContext c)
        {
            RequestContext ctx = RequestContext.Current(c.Host) ?? throw c.Error("@etag() works while kscript serve handles a request.", "Http");
            string tag = TagFor(c.Arg(0, "value"), c.Has(1, "weak") && Values.Truthy(c.Arg(1, "weak")));
            ctx.ResponseHeaders["ETag"] = tag;
            if (IsRead(ctx.Request) && Matches(BuiltInMiddleware.Header(ctx.Request, "if-none-match"), tag))
            {
                var answer = new HttpResponse(304, string.Empty, null);
                foreach (var h in ctx.ResponseHeaders) answer.Headers[h.Key] = h.Value;
                throw new HttpAbortSignal(answer);
            }
            return tag;
        }
    }
}
