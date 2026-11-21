using System.Collections.Concurrent;

using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Http
{
    /// <summary>
    /// Answers <c>@api('/api/notes')</c>: a request from a page, test or route to the routes of its own project,
    /// handled in this process. A request made while answering another (a page or route) sends on its cookies and
    /// Authorization header, so it is signed in as the same person.
    /// </summary>
    public static class LocalApi
    {
        private static readonly ConcurrentDictionary<string, (DateTime Stamp, HttpApp App)> Apps =
            new ConcurrentDictionary<string, (DateTime, HttpApp)>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, HttpApp> Serving = new ConcurrentDictionary<string, HttpApp>(StringComparer.Ordinal);

        /// <summary>Makes KScript.Net's local requests come here. Safe to call more than once.</summary>
        public static void Install() => LocalRequests.Handler ??= Handle;

        /// <summary>kscript serve's app for its site, so local requests share its log, throttle counts and auth.</summary>
        public static void Serve(HttpApp app) => Serving[app.SiteRoot] = app;

        /// <summary>Stops using an app <see cref="Serve"/> was given, once the server stops.</summary>
        public static void Stop(HttpApp app) => Serving.TryRemove(new KeyValuePair<string, HttpApp>(app.SiteRoot, app));

        internal static LocalResponse Handle(KScriptHost host, string folder, Dictionary<string, object> request)
        {
            HttpApp app = RequestContext.Current(host)?.App ?? AppFor(folder);
            if (app == null) return null;

            Dictionary<string, object> current = RequestContext.Current(host)?.Request
                ?? (host != null && host.Items.TryGetValue(Translator.RequestKey, out object r) ? r as Dictionary<string, object> : null);
            if (current != null) Forward(current, request);

            PageResult result = app.Handle(request);
            if (result == null)
            {
                return new LocalResponse
                {
                    Status = 404,
                    Reason = "Not Found",
                    Body = Values.ToJson(new Dictionary<string, object> { ["error"] = $"No route for {request["method"]} {request["path"]}." }),
                    Headers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/json; charset=utf-8" },
                };
            }
            var headers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = result.ContentType };
            foreach (var kv in result.Headers) headers[kv.Key] = kv.Value;
            if (result.Cookies.Count > 0) headers["Set-Cookie"] = string.Join(", ", result.Cookies);
            return new LocalResponse { Status = result.Status, Reason = ReasonFor(result.Status), Body = result.Body ?? string.Empty, Headers = headers };
        }

        /// <summary>The app of the project around <paramref name="folder"/>, or null when it has no routes.</summary>
        private static HttpApp AppFor(string folder)
        {
            string file = ProjectConfig.Find(folder);
            if (file == null) return null;
            ProjectConfig project = ProjectConfig.Load(file);
            if (Serving.TryGetValue(project.SiteRoot, out HttpApp serving)) return serving;
            DateTime stamp = File.GetLastWriteTimeUtc(file);
            if (!Apps.TryGetValue(file, out var cached) || cached.Stamp != stamp)
            {
                cached = (stamp, new HttpApp(project.SiteRoot, project));
                Apps[file] = cached;
            }
            return cached.App.HasRoutes ? cached.App : null;
        }

        /// <summary>Sends on who is asking: the cookies and Authorization header, unless the call gave its own.</summary>
        private static void Forward(Dictionary<string, object> from, Dictionary<string, object> to)
        {
            var headers = (Dictionary<string, object>)to["headers"];
            bool ownCookies = headers.ContainsKey("cookie");
            if (from.GetValueOrDefault("headers") is Dictionary<string, object> given)
            {
                foreach (string name in new[] { "cookie", "authorization" })
                {
                    object value = given.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
                    if (value != null) headers.TryAdd(name, value);
                }
            }
            if (!ownCookies && from.GetValueOrDefault("cookies") is Dictionary<string, object> cookies)
                to["cookies"] = new Dictionary<string, object>(cookies, StringComparer.Ordinal);
            if (from.GetValueOrDefault("ip") is object ip) to["ip"] = ip;
        }

        private static string ReasonFor(int status) => Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
    }
}
