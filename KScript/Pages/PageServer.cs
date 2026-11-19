using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;

namespace KScript.Pages
{
    /// <summary>What a URL maps to in a site folder.</summary>
    public sealed class PageRoute
    {
        public enum RouteKind { Page, Static, NotFound }

        public RouteKind Kind { get; init; }

        /// <summary>The file, relative to the site folder, with forward slashes.</summary>
        public string File { get; init; }

        /// <summary>Values captured by [name] segments, e.g. people/[id].html.</summary>
        public Dictionary<string, object> Params { get; init; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// File-based routing for a site folder: <c>/people</c> is people.html, people.md or people/index.html;
    /// <c>/people/42</c> can match people/[id].html. Files and folders starting with _ or . are never served,
    /// and KScript sources (.xml, .ks) are never sent as text.
    /// </summary>
    public sealed class PageSite
    {
        private static readonly string[] PageExtensions = { ".html", ".htm", ".md" };
        private static readonly string[] HiddenExtensions = { ".xml", ".ks", ".kscript" };

        public PageSite(string root)
        {
            Root = Path.GetFullPath(root);
            if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Site folder not found: {root}");
        }

        public string Root { get; }

        public PageRoute Resolve(string urlPath)
        {
            string path = Uri.UnescapeDataString((urlPath ?? "/").Split('?')[0]).Replace('\\', '/');
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

            if (segments.Any(s => s == ".." || s.StartsWith("_", StringComparison.Ordinal) || s.StartsWith(".", StringComparison.Ordinal)))
                return NotFound();

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
        public static async Task<int> RunAsync(string siteFolder, int port, string host, TextWriter log)
        {
            var site = new PageSite(siteFolder);

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
                    result = await HandleAsync(site, http.Request, types);
                }
                catch (Exception ex)
                {
                    result = ErrorPage(ex);
                }

                http.Response.StatusCode = result.Status;
                foreach (var h in result.Headers) http.Response.Headers[h.Key] = h.Value;
                if (result.Status != 304 && !(result.Status >= 300 && result.Status < 400 && result.Body.Length == 0))
                {
                    http.Response.ContentType = result.ContentType;
                    if (result.FilePath != null)
                        await http.Response.SendFileAsync(result.FilePath);
                    else
                        await http.Response.WriteAsync(result.Body, Encoding.UTF8);
                }

                log?.WriteLine($"{http.Request.Method} {http.Request.Path}{http.Request.QueryString} -> {result.Status} ({(DateTime.UtcNow - started).TotalMilliseconds:0} ms)");
            });

            string url = host == "localhost" ? $"http://localhost:{port}" : $"http://{host}:{port}";
            log?.WriteLine($"Serving {site.Root} on {url}");
            log?.WriteLine("Press Ctrl+C to stop.");
            await app.RunAsync();
            return 0;
        }

        /// <summary>Handles one request. Separate from Kestrel so it can be tested directly.</summary>
        public static async Task<PageResult> HandleAsync(PageSite site, HttpRequest request, FileExtensionContentTypeProvider types = null)
        {
            types ??= new FileExtensionContentTypeProvider();
            var route = site.Resolve(request.Path.Value);

            switch (route.Kind)
            {
                case PageRoute.RouteKind.Static:
                {
                    string full = Path.Combine(site.Root, route.File);
                    if (!types.TryGetContentType(full, out string contentType)) contentType = "application/octet-stream";
                    return new PageResult { ContentType = contentType, FilePath = full };
                }

                case PageRoute.RouteKind.Page:
                {
                    var ctx = new PageContext(site.Root, await BuildRequestAsync(request));
                    return PageRenderer.RenderFile(ctx, route.File, route.Params);
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
                    return new PageResult { Status = 404, Body = $"<!doctype html><title>Not found</title><h1>Not found</h1><p>{PageValues.HtmlEscape(request.Path.Value)}</p>" };
                }
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
                ["path"] = request.Path.Value ?? "/",
                ["query"] = query,
                ["form"] = form,
                ["headers"] = headers,
                ["cookies"] = cookies,
                ["body"] = body,
                ["params"] = new Dictionary<string, object>(),
            };
        }

        public static PageResult ErrorPage(Exception ex)
        {
            string location = ex is PageException pe && pe.Location != null ? pe.Location + ": " : string.Empty;
            string detail = ex is PageException ? string.Empty : $"\n\n{ex}";
            return new PageResult
            {
                Status = 500,
                Body = "<!doctype html><meta charset=\"utf-8\"><title>KScript error</title>" +
                       "<style>body{font:15px/1.5 system-ui,sans-serif;margin:40px;color:#222}pre{background:#f6f4f1;padding:16px;border-radius:8px;white-space:pre-wrap}</style>" +
                       $"<h1>KScript error</h1><pre>{PageValues.HtmlEscape(location + ex.Message + detail)}</pre>",
            };
        }
    }
}
