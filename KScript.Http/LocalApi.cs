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

        private const string TestAppsKey = "KScript.Http.TestApps";

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
            // A test's @acting_as() reaches the routes it calls, as the test database's transaction does.
            if (!request.ContainsKey(KScript.Auth.Policies.ActingKey) && host != null && host.Items.TryGetValue(KScript.Auth.Policies.ActingKey, out object acting))
                request[KScript.Auth.Policies.ActingKey] = acting;
            if (!request.ContainsKey(KScript.Auth.Policies.GuestKey) && host != null && host.Items.TryGetValue(KScript.Auth.Policies.GuestKey, out object guest))
                request[KScript.Auth.Policies.GuestKey] = guest;
            // A Cookie header the call sends itself is read into $request.cookies, as a server does.
            if (!request.ContainsKey("cookies") && ((Dictionary<string, object>)request["headers"]).GetValueOrDefault("cookie") is object cookieHeader)
                request["cookies"] = ParseCookies(Values.ToStr(cookieHeader));

            // An ordinary form post's fields are read from its body into $request.form (and so _token), as a server does.
            if (!request.ContainsKey("form") && PageServer.IsUrlEncodedForm(Values.ToStr(((Dictionary<string, object>)request["headers"]).GetValueOrDefault("content-type"))))
                request["form"] = PageServer.UrlEncodedForm(Values.ToStr(request.GetValueOrDefault("body")));

            bool routesOnly = request.Remove(LocalRequests.RoutesOnlyKey);
            PageResult result = app.Handle(request);
            if (result == null && routesOnly) return null;
            result ??= new PageResult
            {
                Status = 404,
                Body = Values.ToJson(new Dictionary<string, object> { ["error"] = $"No route for {request["method"]} {request["path"]}." }),
                ContentType = "application/json; charset=utf-8",
            };
            var headers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = result.ContentType,
                ["Server"] = HttpIdentity.Product,
            };
            foreach (var kv in result.Headers) headers[kv.Key] = kv.Value;
            // Each cookie is a Set-Cookie header of its own on the wire, so a response that sets two shows a list.
            if (result.Cookies.Count > 0) headers["Set-Cookie"] = result.Cookies.Count == 1 ? result.Cookies[0] : result.Cookies.Select(c => (object)c).ToList();
            return new LocalResponse { Status = result.Status, Reason = ReasonFor(result.Status), Body = result.Body ?? string.Empty, BodyBytes = result.BodyBytes, Headers = headers };
        }

        /// <summary>The app of the project around <paramref name="folder"/>, or null when it has no routes.</summary>
        internal static HttpApp AppFor(string folder, bool includePages = false)
        {
            KScriptHost testHost = AppFolders.TestHost;
            string file = ProjectConfig.Find(folder);
            if (file == null)
            {
                if (!includePages) return null;
                if (testHost == null)
                    for (DirectoryInfo dir = new DirectoryInfo(folder); dir != null; dir = dir.Parent)
                        if (Serving.TryGetValue(dir.FullName, out HttpApp running)) return running;
                return new HttpApp(folder);
            }
            ProjectConfig project = ProjectConfig.Load(file);
            if (testHost == null && Serving.TryGetValue(project.SiteRoot, out HttpApp serving)) return serving;
            var apps = testHost == null ? Apps : TestApps(testHost);
            DateTime stamp = File.GetLastWriteTimeUtc(file);
            if (!apps.TryGetValue(file, out var cached) || cached.Stamp != stamp)
            {
                cached = (stamp, new HttpApp(project.SiteRoot, project));
                apps[file] = cached;
            }
            return includePages || cached.App.HasRoutes ? cached.App : null;
        }

        private static ConcurrentDictionary<string, (DateTime Stamp, HttpApp App)> TestApps(KScriptHost host)
        {
            lock (host.Items)
            {
                if (!host.Items.TryGetValue(TestAppsKey, out object cached))
                {
                    cached = new ConcurrentDictionary<string, (DateTime Stamp, HttpApp App)>(StringComparer.Ordinal);
                    host.Items[TestAppsKey] = cached;
                }
                return (ConcurrentDictionary<string, (DateTime Stamp, HttpApp App)>)cached;
            }
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
            if (from.TryGetValue(KScript.Auth.Policies.ActingKey, out object acting)) to.TryAdd(KScript.Auth.Policies.ActingKey, acting);
            if (from.TryGetValue(KScript.Auth.Policies.GuestKey, out object guest)) to.TryAdd(KScript.Auth.Policies.GuestKey, guest);
        }

        /// <summary>A Cookie header (<c>a=1; b=two</c>) as a map of names to values. The first of a repeated name wins.</summary>
        internal static Dictionary<string, object> ParseCookies(string header)
        {
            var cookies = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string value = part.Substring(eq + 1).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value.Substring(1, value.Length - 2);
                cookies.TryAdd(part.Substring(0, eq).Trim(), value);
            }
            return cookies;
        }

        private static string ReasonFor(int status) => Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
    }
}
