using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using KScript.Output;

namespace KScript.Pages
{
    /// <summary>What a URL maps to in a site folder.</summary>
    public sealed class PageRoute
    {
        public enum RouteKind { Page, Static, NotFound, Stylesheet }

        public RouteKind Kind { get; init; }

        /// <summary>The file, relative to the site folder, with forward slashes.</summary>
        public string File { get; init; }

        /// <summary>The locale from a /fr/... path prefix, when the site has translations for it.</summary>
        public string Locale { get; init; }

        /// <summary>Values captured by [name] segments, e.g. people/[id].html.</summary>
        public Dictionary<string, object> Params { get; init; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// File-based routing for a site folder: <c>/people</c> is people.html, people.md or people/index.html;
    /// <c>/people/42</c> can match people/[id].html. Files and folders starting with _ or . are never served,
    /// and KScript sources (.kscript, .ks.xml and any other .xml) and kscript.json are never sent as text.
    /// </summary>
    public sealed class PageSite
    {
        private static readonly string[] PageExtensions = { ".html", ".htm", ".md" };
        private static readonly string[] HiddenExtensions = { ".xml", ".kscript" };

        public PageSite(string root)
        {
            Root = Path.GetFullPath(root);
            if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Site folder not found: {root}");
        }

        public string Root { get; }

        /// <summary>The folder of translation files that /fr/... prefixes are checked against.</summary>
        public string LocalesFolder { get; set; } = "locales";

        private bool IsLocalePrefix(string segment)
        {
            if (segment.Length < 2 || segment.Length > 12 || !segment.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')) return false;
            if (File.Exists(Path.Combine(Root, segment)) || Directory.Exists(Path.Combine(Root, segment))) return false;
            string folder = Path.Combine(Root, LocalesFolder);
            return Directory.Exists(Path.Combine(folder, segment)) || new[] { ".json", ".yaml", ".yml" }.Any(e => File.Exists(Path.Combine(folder, segment + e)));
        }

        /// <summary>config.json, config.staging.json, and any .yaml, .yml or .env file: never served.</summary>
        internal static bool IsConfigFile(string name)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext == ".yaml" || ext == ".yml" || ext == ".env") return true;
            return ext == ".json" && name.StartsWith("config.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Top-level folders that are never served, such as the views folder of KScript.Http (views are only
        /// rendered by controllers, with their data).
        /// </summary>
        public HashSet<string> HiddenFolders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public PageRoute Resolve(string urlPath)
        {
            string path = Uri.UnescapeDataString((urlPath ?? "/").Split('?')[0]).Replace('\\', '/');
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

            if (segments.Any(s => s == ".." || s.StartsWith("_", StringComparison.Ordinal) || s.StartsWith(".", StringComparison.Ordinal)))
                return NotFound();
            if (segments.Count > 0 && HiddenFolders.Contains(segments[0]))
                return NotFound();
            // The project's settings file is not part of the site.
            if (segments.Count > 0 && string.Equals(segments[^1], Projects.ProjectConfig.FileName, StringComparison.OrdinalIgnoreCase))
                return NotFound();
            // Nor are the settings $config reads (config.json, config.staging.yaml), which may hold secrets.
            if (segments.Count > 0 && IsConfigFile(segments[^1]))
                return NotFound();
            // Nor are the notes for coding agents (AGENTS.md, CLAUDE.md), with or without .md.
            if (segments.Count == 1 && new[] { "AGENTS", "AGENTS.md", "CLAUDE", "CLAUDE.md" }.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
                return NotFound();

            // /fr/about is the about page in French when locales/fr.json (or .yaml, or a locales/fr folder) exists.
            if (segments.Count > 0 && IsLocalePrefix(segments[0]))
            {
                PageRoute inner = Resolve("/" + string.Join("/", segments.Skip(1)));
                return new PageRoute { Kind = inner.Kind, File = inner.File, Params = inner.Params, Locale = segments[0] };
            }

            // Stylesheets written in KScript are served compiled (theme.css from theme.ks.css), never as written.
            if (segments.Count > 0 && KScript.Style.Stylesheets.IsStylesheet(segments[^1]))
                return NotFound();
            if (segments.Count > 0 && segments[^1].EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                string rel = string.Join("/", segments);
                string source = rel.Substring(0, rel.Length - 4) + ".ks.css";
                if (!File.Exists(Path.Combine(Root, rel)) && File.Exists(Path.Combine(Root, source)))
                    return new PageRoute { Kind = PageRoute.RouteKind.Stylesheet, File = source };
            }

            // Exact file
            if (segments.Count > 0)
            {
                string rel = string.Join("/", segments);
                string full = Path.Combine(Root, rel);
                if (File.Exists(full))
                {
                    string ext = Path.GetExtension(full).ToLowerInvariant();
                    if (HiddenExtensions.Contains(ext)) return NotFound();
                    return new PageRoute { Kind = PageExtensions.Contains(ext) ? PageRoute.RouteKind.Page : PageRoute.RouteKind.Static, File = rel };
                }
            }

            return MatchPage(Root, segments, 0, new Dictionary<string, object>()) ?? NotFound();
        }

        private static PageRoute NotFound() => new PageRoute { Kind = PageRoute.RouteKind.NotFound };

        private PageRoute MatchPage(string dir, List<string> segments, int index, Dictionary<string, object> captured)
        {
            if (index == segments.Count)
            {
                foreach (var ext in PageExtensions)
                {
                    string index_ = Path.Combine(dir, "index" + ext);
                    if (File.Exists(index_)) return Page(index_, captured);
                }
                return null;
            }

            string seg = segments[index];
            bool last = index == segments.Count - 1;

            if (last)
            {
                foreach (var ext in PageExtensions)
                {
                    string file = Path.Combine(dir, seg + ext);
                    if (File.Exists(file)) return Page(file, captured);
                }
            }

            string sub = Path.Combine(dir, seg);
            if (Directory.Exists(sub))
            {
                var found = MatchPage(sub, segments, index + 1, captured);
                if (found != null) return found;
            }

            // Dynamic segments: [name].html or a [name] folder.
            if (last)
            {
                foreach (var file in Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (IsParam(name, out string param) && PageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                        return Page(file, new Dictionary<string, object>(captured) { [param] = seg });
                }
            }
            foreach (var folder in Directory.GetDirectories(dir).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (IsParam(Path.GetFileName(folder), out string param))
                {
                    var found = MatchPage(folder, segments, index + 1, new Dictionary<string, object>(captured) { [param] = seg });
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static bool IsParam(string name, out string param)
        {
            param = null;
            if (name.Length > 2 && name[0] == '[' && name[^1] == ']')
            {
                param = name.Substring(1, name.Length - 2);
                return true;
            }
            return false;
        }

        private PageRoute Page(string fullPath, Dictionary<string, object> captured) => new PageRoute
        {
            Kind = PageRoute.RouteKind.Page,
            File = Path.GetRelativePath(Root, fullPath).Replace('\\', '/'),
            Params = captured,
        };
    }

    /// <summary><c>kscript serve</c>: serves a site folder, rendering KScript pages on each request.</summary>
    public static class PageServer
    {
        public static Task<int> RunAsync(string siteFolder, int port, string host, TextWriter log, CancellationToken stop = default) =>
            RunAsync(new PageSite(siteFolder), port, host, log, handler: null, stop);

        /// <summary>
        /// Serves a site. <paramref name="handler"/> (from KScript.Http) sees each request first; when it returns
        /// null, the request goes to the site's pages and files. <paramref name="onStarted"/> gets the address once the
        /// server is listening.
        /// </summary>
        public static async Task<int> RunAsync(PageSite site, int port, string host, TextWriter log, Func<HttpRequest, Task<PageResult>> handler, CancellationToken stop = default, Action<string> onStarted = null)
        {
            CliOutput cli = log == null ? null : new CliOutput(log);

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = site.Root });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(o =>
            {
                if (host == "localhost") o.ListenLocalhost(port);
                else o.Listen(IPAddress.Parse(host == "*" ? "0.0.0.0" : host), port);
            });

            var app = builder.Build();
            var types = new FileExtensionContentTypeProvider();

            app.Run(async http =>
            {
                var started = DateTime.UtcNow;
                PageResult result;
                try
                {
                    // The folder kscript serve started in may have been deleted or recreated since: go back to it.
                    KScript.Runtime.WorkingFolder.Check();
                    result = (handler != null ? await handler(http.Request) : null) ?? await HandleAsync(site, http.Request, types);
                }
                catch (Exception ex)
                {
                    result = ErrorPage(ex);
                    if (log != null) KScript.Runtime.ErrorReport.Write(log, $"{http.Request.Method} {http.Request.Path} failed.", ex);
                }

                http.Response.StatusCode = result.Status;
                foreach (var h in result.Headers) http.Response.Headers[h.Key] = h.Value;
                foreach (string cookie in result.Cookies) http.Response.Headers.Append("Set-Cookie", cookie);
                if (result.Status != 304 && !(result.Status >= 300 && result.Status < 400 && result.Body.Length == 0))
                {
                    http.Response.ContentType = result.ContentType;
                    if (result.FilePath != null)
                        await http.Response.SendFileAsync(result.FilePath);
                    else
                        await http.Response.WriteAsync(result.Body, Encoding.UTF8);
                }

                if (cli != null)
                {
                    // As artisan serve: the time, the request, dots, then how long it took and the status.
                    CliStyle style = cli.Style;
                    string status = result.Status.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string coloured = result.Status >= 500 ? style.Apply(status, "1;31") : result.Status >= 400 ? style.Apply(status, "1;33") : result.Status >= 300 ? style.Cyan(status) : style.Green(status);
                    cli.TwoColumn($"{style.Gray(DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))} {http.Request.Method} {http.Request.Path}{http.Request.QueryString}",
                        $"{style.Gray($"~ {(DateTime.UtcNow - started).TotalMilliseconds:0}ms")} {coloured}");
                }
            });

            string url = host == "localhost" ? $"http://localhost:{port}" : $"http://{host}:{port}";
            if (onStarted == null && cli != null)
            {
                cli.NewLine();
                cli.Info($"Serving [{site.Root}] on [{url}].");
                cli.NewLine();
                cli.Comment("  Press Ctrl+C to stop the server");
                cli.NewLine();
            }
            try
            {
                await app.StartAsync(stop);
                onStarted?.Invoke(url);
                await app.WaitForShutdownAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Stopped (Ctrl+C) while the server was still starting: that is a normal stop, not an error.
            }
            await app.StopAsync();
            return 0;
        }

        /// <summary>Handles one request. Separate from Kestrel so it can be tested directly.</summary>
        public static async Task<PageResult> HandleAsync(PageSite site, HttpRequest request, FileExtensionContentTypeProvider types = null)
        {
            types ??= new FileExtensionContentTypeProvider();

            if (request.Path.Value == "/_ks/live.js")
                return new PageResult { ContentType = "text/javascript; charset=utf-8", Body = PageLive.Script };

            if (request.Path.Value == "/_ks/live")
            {
                if (!HttpMethods.IsPost(request.Method)) return new PageResult { Status = 405, Body = "POST only" };
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                return HandleLive(site, await reader.ReadToEndAsync(), await BuildRequestAsync(request));
            }

            var route = site.Resolve(request.Path.Value);

            switch (route.Kind)
            {
                case PageRoute.RouteKind.Static:
                {
                    string full = Path.Combine(site.Root, route.File);
                    if (!types.TryGetContentType(full, out string contentType)) contentType = "application/octet-stream";
                    return new PageResult { ContentType = contentType, FilePath = full };
                }

                case PageRoute.RouteKind.Stylesheet:
                {
                    try
                    {
                        string css = KScript.Style.Stylesheets.Compile(Path.Combine(site.Root, route.File), null, h =>
                        {
                            h.FileRoot = site.Root;
                            h.FileRootName = "the site folder";
                            h.CanWriteFiles = false;
                        });
                        return new PageResult { ContentType = "text/css; charset=utf-8", Body = css };
                    }
                    catch (KScript.Runtime.KScriptException ex)
                    {
                        // The browser ignores a broken stylesheet, so the error goes into it as a comment too.
                        return new PageResult { Status = 500, ContentType = "text/css; charset=utf-8", Body = $"/* {KScript.Runtime.ErrorReport.Format(ex).Replace("*/", "* /")} */" };
                    }
                }

                case PageRoute.RouteKind.Page:
                {
                    var built = await BuildRequestAsync(request);
                    if (route.Locale != null) built["locale"] = route.Locale;
                    var ctx = new PageContext(site.Root, built);
                    PageResult rendered = PageRenderer.RenderFile(ctx, route.File, route.Params);
                    // A page that started a session (KScript.Auth's @csrf_field(), say) sends its cookie.
                    rendered.Cookies.AddRange(KScript.Runtime.RequestHooks.Finish(built));
                    return rendered;
                }

                default:
                {
                    string notFound = Path.Combine(site.Root, "_404.html");
                    if (File.Exists(notFound))
                    {
                        var ctx = new PageContext(site.Root, await BuildRequestAsync(request));
                        var page = PageRenderer.RenderFile(ctx, "_404.html");
                        page.Status = 404;
                        return page;
                    }
                    return new PageResult { Status = 404, Body = $"<!doctype html><title>Not found</title><h1>Not found</h1><p>{KScript.Runtime.Values.HtmlEscape(request.Path.Value)}</p>" };
                }
            }
        }

        /// <summary>Re-renders one &lt;live&gt; block after a browser event.</summary>
        public static PageResult HandleLive(PageSite site, string json, Dictionary<string, object> request)
        {
            string page;
            LiveEvent ev;
            try
            {
                (page, ev) = PageLive.ParseRequest(json);
            }
            catch (LiveExpiredException ex)
            {
                return new PageResult { Status = 409, ContentType = "text/plain; charset=utf-8", Body = ex.Message };
            }

            // The handler sees the same $request.query and path as the page it came from.
            var uri = new Uri(new Uri("http://localhost"), page);
            request["path"] = uri.AbsolutePath;
            request["query"] = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)
                .ToDictionary(q => q.Key, q => (object)q.Value.ToString());

            var route = site.Resolve(uri.AbsolutePath);
            if (route.Kind != PageRoute.RouteKind.Page) return new PageResult { Status = 404, Body = "Page not found." };
            if (route.Locale != null) request["locale"] = route.Locale;

            var ctx = new PageContext(site.Root, request) { Live = ev };
            try
            {
                return PageRenderer.RenderFile(ctx, route.File, route.Params);
            }
            catch (LiveExpiredException ex)
            {
                return new PageResult { Status = 409, ContentType = "text/plain; charset=utf-8", Body = ex.Message };
            }
            catch (PageException ex)
            {
                return new PageResult { Status = 500, ContentType = "text/plain; charset=utf-8", Body = KScript.Runtime.ErrorReport.Format(ex) };
            }
        }

        /// <summary>The <c>$request</c> map a page sees.</summary>
        public static async Task<Dictionary<string, object>> BuildRequestAsync(HttpRequest request)
        {
            var query = request.Query.ToDictionary(q => q.Key, q => (object)q.Value.ToString());
            var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => (object)h.Value.ToString());
            var cookies = request.Cookies.ToDictionary(c => c.Key, c => (object)c.Value);
            var form = new Dictionary<string, object>();
            string body = string.Empty;

            if (request.HasFormContentType)
            {
                var f = await request.ReadFormAsync();
                foreach (var kv in f) form[kv.Key] = kv.Value.ToString();
            }
            else if (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                body = await reader.ReadToEndAsync();
            }

            return new Dictionary<string, object>
            {
                ["method"] = request.Method,
                ["scheme"] = request.Scheme,
                ["path"] = request.Path.Value ?? "/",
                ["query"] = query,
                ["form"] = form,
                ["headers"] = headers,
                ["cookies"] = cookies,
                ["body"] = body,
                ["params"] = new Dictionary<string, object>(),
            };
        }

        /// <summary>
        /// The page for an error: the KScript error (<c>file:line: Type: message</c>), never a .NET stack trace unless
        /// kscript runs with --debug or KSCRIPT_DEBUG.
        /// </summary>
        public static PageResult ErrorPage(Exception ex)
        {
            string text = KScript.Runtime.ErrorReport.Format(ex);
            return new PageResult
            {
                Status = 500,
                Body = "<!doctype html><meta charset=\"utf-8\"><title>KScript error</title>" +
                       "<style>body{font:15px/1.5 system-ui,sans-serif;margin:40px;color:#222}pre{background:#f6f4f1;padding:16px;border-radius:8px;white-space:pre-wrap}</style>" +
                       $"<h1>KScript error</h1><pre>{KScript.Runtime.Values.HtmlEscape(text)}</pre>",
            };
        }
    }
}
