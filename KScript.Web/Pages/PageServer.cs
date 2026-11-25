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
using Microsoft.AspNetCore.Http.Features;
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

        public string Path
        {
            get
            {
                if (File == null) return null;
                string stem = File[..^System.IO.Path.GetExtension(File).Length];
                if (stem == "index") return "/";
                if (stem.EndsWith("/index", StringComparison.Ordinal)) stem = stem[..^6];
                return "/" + string.Join("/", stem.Split('/').Select(part =>
                    part.Length > 2 && part[0] == '[' && part[^1] == ']' ? "{" + part[1..^1] + "}" : Uri.EscapeDataString(part)));
            }
        }

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
        public KScript.Runtime.ConfigValue StartupConfig { get; set; }

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
        /// Site-relative folders that are never served, such as the views folder of KScript.Http (views are only
        /// rendered by controllers, with their data).
        /// </summary>
        public HashSet<string> HiddenFolders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Site-relative private files, including the project's default database.</summary>
        public HashSet<string> HiddenFiles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether KScript.Http handles this URL itself, including its middleware.</summary>
        public Func<string, bool> IsRoutedPath { get; set; }

        /// <summary>Renders file pages in the host's request and session context.</summary>
        public Func<PageRoute, Dictionary<string, object>, PageResult> RenderPage { get; set; }

        public PageRoute Resolve(string urlPath) => Resolve(urlPath, IsRoutedPath);

        public PageRoute Resolve(string urlPath, Func<string, bool> isRoutedPath)
        {
            string path = Uri.UnescapeDataString((urlPath ?? "/").Split('?')[0]).Replace('\\', '/');
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

            string relative = string.Join("/", segments);
            if (IsHidden(segments, relative)) return NotFound();

            // /fr/about is the about page in French when locales/fr.json (or .yaml, or a locales/fr folder) exists.
            if (segments.Count > 0 && IsLocalePrefix(segments[0]))
            {
                PageRoute inner = Resolve("/" + string.Join("/", segments.Skip(1)), isRoutedPath);
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
                    return CheckPageRoute(new PageRoute { Kind = PageExtensions.Contains(ext) ? PageRoute.RouteKind.Page : PageRoute.RouteKind.Static, File = rel }, relative, isRoutedPath);
                }
            }

            return CheckPageRoute(MatchPage(Root, segments, 0, new Dictionary<string, object>()) ?? NotFound(), relative, isRoutedPath);
        }

        private bool IsHidden(IReadOnlyList<string> segments, string relative)
        {
            if (segments.Any(s => s == ".." || s.StartsWith("_", StringComparison.Ordinal) || s.StartsWith(".", StringComparison.Ordinal)))
                return true;
            if (HiddenFiles.Contains(relative) || HiddenFolders.Any(folder => folder == "." ||
                    relative.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)))
                return true;
            // The project's settings file is not part of the site.
            if (segments.Count > 0 && string.Equals(segments[^1], Projects.ProjectConfig.FileName, StringComparison.OrdinalIgnoreCase))
                return true;
            // Nor are the settings $config reads (config.json, config.staging.yaml), which may hold secrets.
            if (segments.Count > 0 && IsConfigFile(segments[^1]))
                return true;
            // Nor are the notes for coding agents (AGENTS.md, CLAUDE.md), with or without .md.
            if (segments.Count == 1 && new[] { "AGENTS", "AGENTS.md", "CLAUDE", "CLAUDE.md" }.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>Canonical file-backed page patterns, with aliases collapsed and private files excluded.</summary>
        public IEnumerable<(string Path, string File, string[] Parameters)> Pages(Func<string, bool> isRoutedPath)
        {
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return Walk(Root, Array.Empty<string>());

            IEnumerable<(string Path, string File, string[] Parameters)> Walk(string directory, string[] parents)
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*", options)
                    .Where(file => PageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .OrderBy(file => Path.GetFileNameWithoutExtension(file).Equals("index", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(file => IsParam(Path.GetFileNameWithoutExtension(file), out _) ? -1 : Array.IndexOf(PageExtensions, Path.GetExtension(file).ToLowerInvariant()))
                    .ThenBy(file => file, StringComparer.Ordinal))
                {
                    string filename = Path.GetFileName(file);
                    string[] parts = parents.Append(filename).ToArray();
                    string relative = string.Join("/", parts);
                    if (IsHidden(parts, relative)) continue;
                    string stem = Path.GetFileNameWithoutExtension(file);
                    parts = stem.Equals("index", StringComparison.OrdinalIgnoreCase) ? parents : parents.Append(stem).ToArray();
                    var names = new List<string>();
                    string[] pattern = parts.Select(part =>
                    {
                        if (!IsParam(part, out string name)) return Uri.EscapeDataString(part);
                        names.Add(name);
                        return "{" + name + "}";
                    }).ToArray();
                    string path = "/" + string.Join("/", pattern);
                    if (names.Count == 0 && (Resolve(path, isRoutedPath) is not { Kind: PageRoute.RouteKind.Page } match || match.File != relative)) continue;
                    string shape = "/" + string.Join("/", parts.Select(part => IsParam(part, out _) ? "{}" : Uri.EscapeDataString(part)));
                    if (seen.Add(shape)) yield return (path, relative, names.ToArray());
                }
                foreach (string child in Directory.EnumerateDirectories(directory, "*", options).OrderBy(dir => dir, StringComparer.Ordinal))
                {
                    string[] parts = parents.Append(Path.GetFileName(child)).ToArray();
                    if (IsHidden(parts, string.Join("/", parts))) continue;
                    foreach (var page in Walk(child, parts)) yield return page;
                }
            }
        }

        private static PageRoute CheckPageRoute(PageRoute page, string relative, Func<string, bool> isRoutedPath)
        {
            if (page.Kind != PageRoute.RouteKind.Page || isRoutedPath == null) return page;
            // A physical page has several URLs. A protected controller owns the page at all of them,
            // including when the legacy /_ks/live endpoint resolves a posted page name.
            string file = "/" + page.File;
            foreach (var (name, value) in page.Params) file = file.Replace("[" + name + "]", value.ToString(), StringComparison.Ordinal);
            string stem = file[..^Path.GetExtension(file).Length];
            string pretty = stem.EndsWith("/index", StringComparison.OrdinalIgnoreCase) ? stem[..^6] : stem;
            var aliases = new[] { "/" + relative, file, stem, pretty.Length == 0 ? "/" : pretty };
            return aliases.Distinct(StringComparer.Ordinal).Any(isRoutedPath) ? NotFound() : page;
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
        public static async Task<int> RunAsync(PageSite site, int port, string host, TextWriter log, Func<HttpRequest, Task<PageResult>> handler, CancellationToken stop = default, Action<string> onStarted = null, string reloadRoot = null)
        {
            CliOutput cli = log == null ? null : new CliOutput(log);

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = site.Root });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(o =>
            {
                o.AddServerHeader = false;
                if (host == "localhost") o.ListenLocalhost(port);
                else o.Listen(IPAddress.Parse(host == "*" ? "0.0.0.0" : host), port);
            });

            var app = builder.Build();
            using var reload = reloadRoot == null ? null : new BrowserReload(reloadRoot);
            var types = new FileExtensionContentTypeProvider();

            app.Run(async http =>
            {
                if (reload != null && await reload.TryHandleAsync(http, app.Lifetime.ApplicationStopping)) return;
                var liveStream = LiveActionStream.Negotiate(http);
                string revision = reload?.Revision;
                var started = DateTime.UtcNow;
                PageResult result;
                try
                {
                    // The folder kscript serve started in may have been deleted or recreated since: go back to it.
                    KScript.Runtime.WorkingFolder.Check();
                    result = (handler != null ? await handler(http.Request) : null) ?? await HandleAsync(site, http.Request, types);
                }
                catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    result = ErrorPage(ex, !site.HideErrorDetails);
                    if (log != null) KScript.Runtime.ErrorReport.Write(log, $"{http.Request.Method} {http.Request.Path} failed.", ex);
                }

                try
                {
                    if (liveStream?.Started == true) await liveStream.FinishAsync(result);
                    else
                    {
                        if (revision != null) BrowserReload.Inject(result, http.Request, revision);
                        http.Response.StatusCode = result.Status;
                        http.Response.Headers.Server = KScript.Runtime.HttpIdentity.Product;
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
                    }
                }
                catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { return; }

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
            try
            {
                await app.StartAsync(stop);
                // Only once the port is taken: a server that could not start never says it is serving.
                if (onStarted == null && cli != null)
                {
                    cli.NewLine();
                    cli.Info($"Serving [{site.Root}] on [{url}].");
                    cli.NewLine();
                    cli.Comment("  Press Ctrl+C to stop the server");
                    cli.NewLine();
                }
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
                // HttpServer signs every file and controller page for its original URL.
                if (site.RenderPage != null) return new PageResult { Status = 409, Body = "Reload the page to continue." };
                var (built, error) = await ReadPageRequestAsync(request, live: true);
                if (error != null) return error;
                return HandleLive(site, (string)built["body"], built);
            }

            var route = site.Resolve(request.Path.Value);
            bool live = site.RenderPage != null && IsLiveRequest(request);
            if (live && route.Kind != PageRoute.RouteKind.Page) return new PageResult { Status = 409 };

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
                        if (site.HideErrorDetails) throw;
                        return new PageResult { Status = 500, ContentType = "text/css; charset=utf-8", Body = $"/* {KScript.Runtime.ErrorReport.Format(ex).Replace("*/", "* /")} */" };
                    }
                }

                case PageRoute.RouteKind.Page:
                {
                    var (built, error) = await ReadPageRequestAsync(request, live);
                    if (error != null) return error;
                    if (route.Locale != null) built[RequestFields.Locale.Name] = route.Locale;
                    PageResult rendered = site.RenderPage == null
                        ? PageRenderer.RenderFile(new PageContext(site.Root, built) { StartupConfig = site.StartupConfig }, route.File, route.Params)
                        : site.RenderPage(route, built);
                    // A page that started a session (KScript.Auth's @csrf_field(), say) sends its cookie.
                    rendered.Cookies.AddRange(KScript.Runtime.RequestHooks.Finish(built));
                    return rendered;
                }

                default:
                {
                    string notFound = Path.Combine(site.Root, "_404.html");
                    if (File.Exists(notFound))
                    {
                        var ctx = new PageContext(site.Root, await BuildRequestAsync(request)) { StartupConfig = site.StartupConfig };
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

            // Rebuild the page URL and query; the scheme comes from the HTTP request.
            var uri = new Uri(new Uri("http://localhost"), page);
            request[RequestFields.Path.Name] = uri.AbsolutePath;
            request[RequestFields.Url.Name] = uri.PathAndQuery;
            request[RequestFields.Query.Name] = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)
                .ToDictionary(q => q.Key, q => (object)q.Value.ToString());

            var route = site.Resolve(uri.AbsolutePath);
            if (route.Kind != PageRoute.RouteKind.Page) return new PageResult { Status = 404, Body = "Page not found." };
            if (route.Locale != null) request[RequestFields.Locale.Name] = route.Locale;

            var ctx = new PageContext(site.Root, request) { Live = ev, StartupConfig = site.StartupConfig };
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
                return new PageResult { Status = 500, ContentType = "text/plain; charset=utf-8", Body = site.HideErrorDetails ? HiddenErrorMessage : KScript.Runtime.ErrorReport.Format(ex) };
            }
        }

        /// <summary>The <c>$request</c> map a page sees.</summary>
        public static Task<Dictionary<string, object>> BuildRequestAsync(HttpRequest request) => BuildRequestAsync(request, null);

        public const int MaxLiveBodyBytes = 1024 * 1024;
        private const int MaxLiveFileBytes = 8 * 1024 * 1024;
        public const string LiveFilesKey = "__kscriptLiveFiles";

        public static bool IsLiveRequest(HttpRequest request) => HttpMethods.IsPost(request.Method) && request.Headers.ContainsKey("X-KScript-Live");

        public static async Task<(Dictionary<string, object> Request, PageResult Error)> ReadPageRequestAsync(HttpRequest request, bool live)
        {
            if (!live) return (await BuildRequestAsync(request), null);
            if (!System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.ContentType, out var type))
                return (null, new PageResult { Status = 415 });
            if (string.Equals(type.MediaType, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
                return await ReadLiveUploadAsync(request);
            if (!string.Equals(type.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                return (null, new PageResult { Status = 415 });
            if (request.ContentLength > MaxLiveBodyBytes) return (null, new PageResult { Status = 413 });

            // Content-Length is optional for HTTP/2 and chunked requests. Bound the bytes before decoding.
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
            {
                if (bytes.Length + read > MaxLiveBodyBytes) return (null, new PageResult { Status = 413 });
                bytes.Write(buffer, 0, read);
            }
            string body = Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
            return (await BuildRequestAsync(request, body), null);
        }

        private static async Task<(Dictionary<string, object> Request, PageResult Error)> ReadLiveUploadAsync(HttpRequest request)
        {
            const int wireLimit = MaxLiveFileBytes + MaxLiveBodyBytes + 64 * 1024;
            if (request.ContentLength > wireLimit) return (null, new PageResult { Status = 413 });
            var max = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (max != null && !max.IsReadOnly && (max.MaxRequestBodySize == null || max.MaxRequestBodySize > wireLimit))
                max.MaxRequestBodySize = wireLimit;

            IFormCollection form;
            try
            {
                form = await new FormFeature(request, new FormOptions
                {
                    MultipartBodyLengthLimit = MaxLiveFileBytes,
                    ValueCountLimit = 17,
                    ValueLengthLimit = MaxLiveBodyBytes,
                }).ReadFormAsync(request.HttpContext.RequestAborted);
            }
            catch (BadHttpRequestException ex)
            {
                return (null, new PageResult { Status = ex.StatusCode });
            }
            catch (InvalidDataException)
            {
                return (null, new PageResult { Status = 400 });
            }

            if (form.Count != 1 || !form.TryGetValue("state", out var parts) || parts.Count != 1 || form.Files.Count == 0)
                return (null, new PageResult { Status = 400 });
            string body = parts[0] ?? string.Empty;
            if (Encoding.UTF8.GetByteCount(body) > MaxLiveBodyBytes || form.Files.Sum(file => file.Length) > MaxLiveFileBytes)
                return (null, new PageResult { Status = 413 });

            var files = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (IFormFile file in form.Files)
            {
                if (string.IsNullOrEmpty(file.Name)) return (null, new PageResult { Status = 400 });
                var bytes = new byte[(int)file.Length];
                using (Stream content = file.OpenReadStream())
                    await content.ReadExactlyAsync(bytes, request.HttpContext.RequestAborted);
                if (!files.TryGetValue(file.Name, out object entries)) files[file.Name] = entries = new List<object>();
                ((List<object>)entries).Add(new Dictionary<string, object>
                {
                    ["name"] = Path.GetFileName(file.FileName.Replace('\\', '/')),
                    ["type"] = file.ContentType,
                    ["size"] = (double)file.Length,
                    ["bytes"] = Convert.ToBase64String(bytes),
                });
            }
            var result = await BuildRequestAsync(request, body);
            result[LiveFilesKey] = files;
            return (result, null);
        }

        private static async Task<Dictionary<string, object>> BuildRequestAsync(HttpRequest request, string readBody)
        {
            var query = request.Query.ToDictionary(q => q.Key, q => (object)q.Value.ToString());
            var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => (object)h.Value.ToString());
            var cookies = request.Cookies.ToDictionary(c => c.Key, c => (object)c.Value);
            var form = new Dictionary<string, object>();
            string body = readBody ?? string.Empty;

            if (readBody == null && request.HasFormContentType)
            {
                var f = await request.ReadFormAsync();
                foreach (var kv in f) form[kv.Key] = kv.Value.ToString();
            }
            else if (readBody == null && (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding") ||
                     request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true))
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                body = await reader.ReadToEndAsync();
            }

            var built = new Dictionary<string, object>
            {
                [RequestFields.Method.Name] = request.Method,
                [RequestFields.Scheme.Name] = request.Scheme,
                [RequestFields.Path.Name] = request.Path.Value ?? "/",
                [RequestFields.Url.Name] = (request.Path.Value ?? "/") + request.QueryString.Value,
                [RequestFields.Query.Name] = query,
                [RequestFields.Form.Name] = form,
                [RequestFields.Headers.Name] = headers,
                [RequestFields.Cookies.Name] = cookies,
                [RequestFields.Body.Name] = body,
                [RequestFields.Params.Name] = new Dictionary<string, object>(),
                [RequestFields.RouteInfo.Name] = null,
            };
            LiveActionStream.Bind(request, built);
            return built;
        }

        /// <summary>
        /// The page for an error: the KScript error (<c>file:line: Type: message</c>), never a .NET stack trace unless
        /// kscript runs with --debug or KSCRIPT_DEBUG.
        /// </summary>
        public static PageResult ErrorPage(Exception ex) => ErrorPage(ex, details: true);

        /// <summary>What a visitor is told about an error in production; the details are in the server log.</summary>
        public const string HiddenErrorMessage = "Something went wrong on our side. Please try again later.";

        /// <summary>
        /// The page for an error. Without <paramref name="details"/> (the production environment) the visitor gets a
        /// plain 500 page; the error's message, file and line are for the server log.
        /// </summary>
        public static PageResult ErrorPage(Exception ex, bool details)
        {
            if (!details)
                return new PageResult
                {
                    Status = 500,
                    Body = "<!doctype html><meta charset=\"utf-8\"><title>500 Internal Server Error</title>" +
                           $"<h1>500 Internal Server Error</h1><p>{HiddenErrorMessage}</p>",
                };
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
