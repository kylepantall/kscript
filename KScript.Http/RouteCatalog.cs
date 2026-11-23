using System.Xml.Linq;
using System.Text.RegularExpressions;

using KScript.Markdown;
using KScript.Metadata;
using KScript.Pages;
using KScript.Runtime;

namespace KScript.Http
{
    internal static class RouteCatalog
    {
        internal static void Register(KScriptPackage.PackageBuilder p)
        {
            p.Command(new CallableInfo(CallableKind.Command, "routes",
                "Lists declared HTTP routes and canonical file-based pages. Filter by kind or the declared type of attached route data.",
                new[] { new Signature(
                    new ParameterInfo("kind", optional: true, values: new[] { "page", "http" }),
                    new ParameterInfo("data_type", optional: true, type: "type", description: "A declared record type used by the routes' data option.")
                    {
                        TypeConstraint = TypeArgumentConstraint.DeclaredRecord, ResultField = "data",
                    },
                    new ParameterInfo("locale", optional: true, type: KType.String)) })
            {
                ReturnType = KType.List,
                ReturnShape = "list<{ kind: string, path: string, name: string, methods: list<string>, any_method: bool, parameters: list<string>, label_key: string, label: string, data: any }>",
            }, c =>
                {
                    var (app, table) = Current(c);
                    RecordType dataType = c.Arg(1, "data_type") as RecordType;
                    string kind = c.Str(0, null);
                    if (kind is not (null or "page" or "http")) throw c.Error("kind must be page or http.", "Argument");
                    Translator translator = Translator.For(c.Host, app.SiteRoot);
                    string locale = c.Str(2, null);
                    return Entries(app, table, kind, dataType, key =>
                        translator.Has(key, locale) ? translator.Translate(key, null, locale) : null, includeData: true);
                });
            p.Command("route_is", "name_or_pattern: string", "True if the current request matched this route name or canonical pattern.", KType.Bool, c =>
            {
                if (Request(c)?.GetValueOrDefault(RequestFields.RouteInfo.Name) is not Dictionary<string, object> row) return false;
                string value = c.Str(0);
                return Equals(row.GetValueOrDefault("name"), value) || Equals(row.GetValueOrDefault("path"), value);
            });
            p.Command("route_match", "path: string, method?: string", "Looks up a path and method without dispatching it. Returns status, route, parameters, and allowed methods.", KType.Map,
                c => Match(c, c.Str(0), c.Str(1, "GET").ToUpperInvariant()));
            p.Command("asset_url", "path: string", "Builds a public asset URL using assets.base and the optional assets.manifest map in project configuration.", KType.String, c =>
            {
                string path = c.Str(0).TrimStart('/');
                if (path.Split('/').Any(segment => segment is "." or "..") || path.Contains('\\') || path.Any(char.IsControl))
                    throw c.Error("An asset path must be relative to the public asset directory.", "Argument");
                var config = ConfigValue.For(c.Host, c.Module.Directory).Values;
                var assets = config.GetValueOrDefault("assets") as Dictionary<string, object>;
                var manifest = assets?.GetValueOrDefault("manifest") as Dictionary<string, object>;
                string actual = manifest?.GetValueOrDefault(path) as string ?? path;
                string root = assets?.GetValueOrDefault("base") as string ?? "/";
                return root.TrimEnd('/') + "/" + actual.TrimStart('/');
            });
            p.Command("sitemap", "paths: list, origin: string", "An XML sitemap for an explicit list of public paths, with the given HTTP or HTTPS origin.", KType.String, c =>
            {
                if (!Uri.TryCreate(c.Str(1), UriKind.Absolute, out Uri origin) || origin.Scheme is not ("http" or "https")
                    || origin.AbsolutePath != "/" || origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0)
                    throw c.Error("origin must be an HTTP or HTTPS origin, such as https://example.com.", "Argument");
                XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
                var locations = new HashSet<string>(StringComparer.Ordinal);
                foreach (object entry in c.List(0))
                {
                    string path = Values.ToStr(entry);
                    if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\')
                        || path.Contains('{') || path.Contains('}') || !Uri.TryCreate(origin, path, out Uri url))
                        throw c.Error($"'{path}' must be a concrete public path starting with /.", "Argument");
                    locations.Add(url.AbsoluteUri);
                }
                return new XElement(ns + "urlset", locations.Select(url => new XElement(ns + "url", new XElement(ns + "loc", url)))).ToString();
            });
        }

        private static Dictionary<string, object> Request(CallContext call) =>
            RequestContext.Current(call.Host)?.Request ?? call.Host.Items.GetValueOrDefault(Translator.RequestKey) as Dictionary<string, object>;

        internal static (HttpApp App, RouteTable Table) Current(CallContext call)
        {
            RequestContext request = RequestContext.Current(call.Host);
            if (request != null)
                return (request.App, request.Route != null || request.File != null ? request.Table : request.App.Table());
            string folder = call.Host.FileRoot != null ? call.Host.WorkingDirectory : call.Module?.Directory ?? call.Host.WorkingDirectory;
            HttpApp app = LocalApi.AppFor(folder, includePages: true);
            return (app, app.Table());
        }

        internal static List<object> Entries(HttpApp app, RouteTable table, string kind = null, RecordType dataType = null,
            Func<string, string> label = null, bool includeData = false)
        {
            var entries = new List<object>();
            if (kind != "page")
                entries.AddRange(table.Routes
                    .Where(route => dataType == null || (route.Data != null && route.Data.Type.SameDeclaration(dataType)))
                    .Select(route => HttpInfo(route, label, includeData)));
            if (kind != "http" && dataType == null)
                foreach (var page in app.PageSite.Pages(path => OwnsPath(app, table, path)))
                    entries.Add(PageInfo(page.Path, page.Parameters, PageLabelKey(app, page.File), label));
            return entries;
        }

        internal static string PageLabelKey(HttpApp app, string relativePath)
        {
            try
            {
                using var reader = File.OpenText(Path.Combine(app.SiteRoot, relativePath));
                if (Path.GetExtension(relativePath).Equals(".md", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.ReadLine() != "---") return null;
                    var header = new List<string> { "---" };
                    while (header.Count < 129 && reader.ReadLine() is string line)
                    {
                        header.Add(line);
                        if (line == "---") break;
                    }
                    return MarkdownText.SplitFrontMatter(string.Join("\n", header) + "\n").Meta.GetValueOrDefault("label_key") as string;
                }

                char[] buffer = new char[32_768];
                string html = new string(buffer, 0, reader.ReadBlock(buffer));
                html = Regex.Replace(html, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
                foreach (Match tag in Regex.Matches(html, @"<meta\s+[^>]*>", RegexOptions.IgnoreCase))
                {
                    var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match attr in Regex.Matches(tag.Value, """(?<name>[a-z][\w:-]*)\s*=\s*(?<quote>['"])(?<value>.*?)\k<quote>""", RegexOptions.IgnoreCase))
                        attrs[attr.Groups["name"].Value] = attr.Groups["value"].Value;
                    if (string.Equals(attrs.GetValueOrDefault("name"), "kscript:label-key", StringComparison.OrdinalIgnoreCase))
                        return System.Net.WebUtility.HtmlDecode(attrs.GetValueOrDefault("content"));
                }
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        internal static Dictionary<string, object> HttpInfo(Route route, Func<string, string> label, bool includeData = false) =>
            Info("http", route.Pattern.Path, route.Name,
                route.Methods.Contains("GET") && !route.Methods.Contains("HEAD") ? route.Methods.Append("HEAD") : route.Methods,
                route.Pattern.Parameters, route.LabelKey, label, route.Methods.Count == 0, includeData ? route.Data?.Copy() : null);

        internal static Dictionary<string, object> PageInfo(string path, IEnumerable<string> parameters, string key, Func<string, string> label = null) =>
            Info("page", path, null, Array.Empty<string>(), parameters, key, label, true, null);

        private static Dictionary<string, object> Info(string kind, string path, string name, IEnumerable<string> methods,
            IEnumerable<string> parameters, string key, Func<string, string> label, bool any, Record data) => new()
        {
            ["kind"] = kind, ["path"] = path, ["name"] = name, ["methods"] = methods.Cast<object>().ToList(),
            ["any_method"] = any, ["parameters"] = parameters.Cast<object>().ToList(),
            ["label_key"] = key, ["label"] = key == null ? null : label?.Invoke(key), ["data"] = data,
        };

        private static bool OwnsPath(HttpApp app, RouteTable table, string path) => table.Match("GET", path) != null || app.IsApiPath(path);

        private static Dictionary<string, object> Match(CallContext call, string path, string method)
        {
            var (app, table) = Current(call);
            string clean = Uri.UnescapeDataString(path.Split('?')[0]);
            RouteMatch match = table.Match(method, clean);
            if (match != null)
            {
                int status = match.Route != null ? 200 : method == "OPTIONS" ? 204 : 405;
                IEnumerable<string> allowed = match.Allowed ?? Array.Empty<string>();
                if (status == 204) allowed = allowed.Append("OPTIONS");
                return Result(status, HttpInfo(match.Route ?? match.PathRoute, null), match.Parameters, allowed);
            }
            if (app.PageSite.Resolve(path, candidate => OwnsPath(app, table, candidate)) is { Kind: PageRoute.RouteKind.Page } page)
                return Result(200, PageInfo(page.Path, page.Params.Keys, PageLabelKey(app, page.File)), page.Params, Array.Empty<string>());
            return Result(404, null, null, Array.Empty<string>());
        }

        private static Dictionary<string, object> Result(int status, object route, object parameters, IEnumerable<string> allowed) => new()
        {
            ["status"] = (double)status, ["route"] = route, ["parameters"] = parameters,
            ["allowed"] = allowed.Cast<object>().ToList(),
        };
    }
}
