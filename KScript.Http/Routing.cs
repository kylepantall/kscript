using System.Text;
using System.Text.RegularExpressions;

using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Http
{
    /// <summary>
    /// A route path such as <c>/people/{id}</c>: literal segments, <c>{name}</c> parameters, optional
    /// <c>{name?}</c> parameters and a final catch-all <c>{name*}</c>, with optional constraints from
    /// <c>where="id: number"</c>.
    /// </summary>
    public sealed class RoutePattern
    {
        private sealed class Segment
        {
            public string Literal;
            public string Param;
            public bool Optional;
            public bool CatchAll;
            public Regex Constraint;
        }

        /// <summary>Words that name a common constraint in <c>where</c>.</summary>
        public static readonly IReadOnlyDictionary<string, string> NamedConstraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["number"] = @"-?\d+(\.\d+)?",
            ["int"] = @"-?\d+",
            ["alpha"] = @"[A-Za-z]+",
            ["alnum"] = @"[A-Za-z0-9]+",
            ["slug"] = @"[a-z0-9]+(-[a-z0-9]+)*",
            ["uuid"] = @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        };

        private readonly List<Segment> segments = new List<Segment>();

        public RoutePattern(string path, string where = null)
        {
            Path = Normalise(path);
            Dictionary<string, Regex> constraints = ParseWhere(where, rules);
            var parts = Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length > 2 && part[0] == '{' && part[^1] == '}')
                {
                    string name = part.Substring(1, part.Length - 2).Trim();
                    var seg = new Segment();
                    if (name.EndsWith("?", StringComparison.Ordinal)) { seg.Optional = true; name = name[..^1]; }
                    else if (name.EndsWith("*", StringComparison.Ordinal)) { seg.CatchAll = true; name = name[..^1]; }
                    if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
                        throw new KScriptException($"'{part}' in the route {Path} isn't a parameter. Write {{name}}, {{name?}} or {{name*}}.", "Http");
                    if (seg.CatchAll && i != parts.Length - 1)
                        throw new KScriptException($"{{{name}*}} takes the rest of the path, so it must come last in {Path}.", "Http");
                    if (seg.Optional && parts.Skip(i + 1).Any(p => !p.EndsWith("?}", StringComparison.Ordinal)))
                        throw new KScriptException($"Only the last parts of {Path} can be optional.", "Http");
                    if (Parameters.Contains(name))
                        throw new KScriptException($"{{{name}}} is used twice in {Path}.", "Http");
                    seg.Param = name;
                    seg.Constraint = constraints.TryGetValue(name, out Regex r) ? r : null;
                    Parameters.Add(name);
                    segments.Add(seg);
                }
                else
                {
                    if (part.Contains('{') || part.Contains('}'))
                        throw new KScriptException($"'{part}' in the route {Path}: a parameter is a whole part of the path, such as /people/{{id}}.", "Http");
                    segments.Add(new Segment { Literal = part });
                }
            }
            foreach (string name in constraints.Keys.Where(k => !Parameters.Contains(k)))
                throw new KScriptException($"where names '{name}', but {Path} has no {{{name}}}.", "Http");
        }

        public string Path { get; }

        public List<string> Parameters { get; } = new List<string>();

        private readonly Dictionary<string, string> rules = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The <c>where</c> rule of each parameter that has one, as written: int, slug or a regular expression.</summary>
        public IReadOnlyDictionary<string, string> Rules => rules;

        /// <summary>True for <c>{name?}</c>.</summary>
        public bool IsOptional(string name) => segments.Any(s => s.Param == name && s.Optional);

        /// <summary>True for <c>{name*}</c>, which takes the rest of the path.</summary>
        public bool IsCatchAll(string name) => segments.Any(s => s.Param == name && s.CatchAll);

        public static string Normalise(string path)
        {
            path = (path ?? "/").Trim().Replace('\\', '/');
            path = "/" + string.Join("/", path.Split('/', StringSplitOptions.RemoveEmptyEntries));
            return path;
        }

        /// <summary>Joins a group prefix and a path: /admin + /people = /admin/people, /admin + / = /admin.</summary>
        public static string Join(string prefix, string path)
        {
            string a = Normalise(prefix), b = Normalise(path);
            if (a == "/") return b;
            if (b == "/") return a;
            return a + b;
        }

        private static Dictionary<string, Regex> ParseWhere(string where, Dictionary<string, string> rules)
        {
            var result = new Dictionary<string, Regex>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(where)) return result;
            // "id: number, slug: [a-z-]+": a comma inside a regex needs the regex in quotes.
            foreach (string entry in SplitTopLevel(where))
            {
                int colon = entry.IndexOf(':');
                if (colon <= 0) throw new KScriptException($"where=\"{where}\": write name: rule pairs, such as where=\"id: number\".", "Http");
                string name = entry[..colon].Trim();
                string rule = entry[(colon + 1)..].Trim().Trim('\'', '"');
                string pattern = NamedConstraints.TryGetValue(rule, out string named) ? named : rule;
                rules[name] = rule;
                try
                {
                    result[name] = new Regex("^(?:" + pattern + ")$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                }
                catch (ArgumentException ex)
                {
                    throw new KScriptException($"where: '{rule}' for {name} isn't a valid pattern ({ex.Message}). Use number, int, alpha, alnum, slug, uuid or a regular expression.", "Http");
                }
            }
            return result;
        }

        private static IEnumerable<string> SplitTopLevel(string text)
        {
            var sb = new StringBuilder();
            char quote = '\0';
            int depth = 0;
            foreach (char c in text)
            {
                if (quote != '\0') { if (c == quote) quote = '\0'; sb.Append(c); continue; }
                if (c == '\'' || c == '"') { quote = c; sb.Append(c); continue; }
                if (c == '[' || c == '(' || c == '{') depth++;
                if (c == ']' || c == ')' || c == '}') depth--;
                if (c == ',' && depth == 0) { yield return sb.ToString(); sb.Clear(); continue; }
                sb.Append(c);
            }
            if (sb.ToString().Trim().Length > 0) yield return sb.ToString();
        }

        /// <summary>Matches a request path, giving the parameter values (missing optional ones are null).</summary>
        public bool TryMatch(string requestPath, out Dictionary<string, object> values)
        {
            values = null;
            var parts = Normalise(requestPath).Split('/', StringSplitOptions.RemoveEmptyEntries);
            var found = new Dictionary<string, object>(StringComparer.Ordinal);
            int i = 0;
            foreach (Segment seg in segments)
            {
                if (seg.CatchAll)
                {
                    string rest = string.Join("/", parts.Skip(i));
                    if (seg.Constraint != null && !seg.Constraint.IsMatch(rest)) return false;
                    found[seg.Param] = rest;
                    i = parts.Length;
                    continue;
                }
                if (i >= parts.Length)
                {
                    if (!seg.Optional) return false;
                    found[seg.Param] = null;
                    continue;
                }
                string part = parts[i];
                if (seg.Literal != null)
                {
                    if (!string.Equals(seg.Literal, part, StringComparison.OrdinalIgnoreCase)) return false;
                }
                else
                {
                    if (seg.Constraint != null && !seg.Constraint.IsMatch(part)) return false;
                    found[seg.Param] = part;
                }
                i++;
            }
            if (i != parts.Length) return false;
            values = found;
            return true;
        }

        /// <summary>Fills in the parameters: /people/{id} with id 3 is /people/3. Values that aren't parameters become the query.</summary>
        public string Url(IReadOnlyDictionary<string, object> values)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (Segment seg in segments)
            {
                if (seg.Literal != null)
                {
                    sb.Append('/').Append(seg.Literal);
                    continue;
                }
                used.Add(seg.Param);
                if (values == null || !values.TryGetValue(seg.Param, out object v) || v == null)
                {
                    if (seg.Optional || seg.CatchAll) continue;
                    throw new KScriptException($"The route {Path} needs a value for {seg.Param}.", "Argument");
                }
                string text = Values.ToStr(v);
                sb.Append('/').Append(seg.CatchAll ? string.Join("/", text.Split('/').Select(Uri.EscapeDataString)) : Uri.EscapeDataString(text));
            }
            string url = sb.Length == 0 ? "/" : sb.ToString();
            var query = values?.Where(kv => !used.Contains(kv.Key) && kv.Value != null).ToList();
            if (query != null && query.Count > 0)
                url += "?" + string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(Values.ToStr(kv.Value))));
            return url;
        }

        public override string ToString() => Path;
    }

    /// <summary>One piece of middleware: a built-in (log, cors, auth, throttle) with an optional argument, or a function.</summary>
    public sealed class MiddlewareRef
    {
        public MiddlewareRef(string name, string argument, ICallable function)
        {
            Name = name;
            Argument = argument;
            Function = function;
        }

        public string Name { get; }
        public string Argument { get; }

        /// <summary>The function, or null for a built-in.</summary>
        public ICallable Function { get; }

        public override string ToString() => Argument != null ? Name + ":" + Argument : Name;
    }

    /// <summary>One route: the HTTP methods and path it answers, and what answers it.</summary>
    public sealed class Route
    {
        /// <summary>Upper case: GET, POST... Empty means any method.</summary>
        public IReadOnlyList<string> Methods { get; init; }
        public RoutePattern Pattern { get; init; }
        public string Name { get; init; }
        public IReadOnlyList<MiddlewareRef> Middleware { get; init; }

        /// <summary>The controller function, or null when the route has a view, a redirect or an inline body.</summary>
        public ICallable Controller { get; init; }

        /// <summary>How the controller was written, such as people.show.</summary>
        public string ControllerName { get; init; }

        public string View { get; init; }
        public string RedirectTo { get; init; }
        public int RedirectStatus { get; init; } = 302;

        /// <summary>The element whose content is the controller (<c>&lt;Get path="/ping"&gt;&lt;return value="'pong'" /&gt;&lt;/Get&gt;</c>).</summary>
        public MarkupElement Inline { get; init; }

        /// <summary>The element that declared the route (Get, Route...); null for the routes of a Resource.</summary>
        public MarkupElement Element { get; init; }
        public Module Module { get; init; }
        public Scope Scope { get; init; }

        /// <summary>True for routes from an API route file: errors are JSON.</summary>
        public bool Api { get; init; }

        public string File { get; init; }
        public int Line { get; init; }

        public bool Allows(string method) =>
            Methods.Count == 0 || Methods.Contains(method, StringComparer.OrdinalIgnoreCase)
            || (string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) && Methods.Contains("GET"));

        /// <summary>What answers the route, for <c>kscript routes</c>.</summary>
        public string Target =>
            ControllerName ?? (View != null ? "view " + View : RedirectTo != null ? "redirect " + RedirectTo : Inline != null ? "inline" : "-");

        public override string ToString() => $"{(Methods.Count == 0 ? "ANY" : string.Join("|", Methods))} {Pattern.Path}";
    }

    /// <summary>The result of looking up a request in a route table.</summary>
    public sealed class RouteMatch
    {
        public Route Route { get; init; }
        public Dictionary<string, object> Parameters { get; init; }

        /// <summary>When the path matched but not the method: the methods it allows (for 405 and OPTIONS).</summary>
        public IReadOnlyList<string> Allowed { get; init; }

        /// <summary>The first route whose path matched, even if its method didn't.</summary>
        public Route PathRoute { get; init; }
    }

    /// <summary>The routes of a site, in the order they were declared. The first route that matches wins.</summary>
    public sealed class RouteTable
    {
        private readonly List<Route> routes = new List<Route>();
        private readonly Dictionary<string, Route> named = new Dictionary<string, Route>(StringComparer.Ordinal);

        public IReadOnlyList<Route> Routes => routes;

        public void Add(Route route)
        {
            if (route.Name != null)
            {
                if (named.TryGetValue(route.Name, out Route other))
                    throw new KScriptException($"Two routes are named {route.Name}: {other} and {route}. Route names must be unique.", "Http");
                named[route.Name] = route;
            }
            routes.Add(route);
        }

        public RouteMatch Match(string method, string path)
        {
            Route pathRoute = null;
            Dictionary<string, object> pathParams = null;
            var allowed = new List<string>();
            foreach (Route route in routes)
            {
                if (!route.Pattern.TryMatch(path, out Dictionary<string, object> values)) continue;
                if (route.Allows(method)) return new RouteMatch { Route = route, Parameters = values, PathRoute = route };
                if (pathRoute == null)
                {
                    pathRoute = route;
                    pathParams = values;
                }
                foreach (string m in route.Methods) if (!allowed.Contains(m)) allowed.Add(m);
            }
            if (pathRoute == null) return null;
            if (allowed.Contains("GET") && !allowed.Contains("HEAD")) allowed.Add("HEAD");
            return new RouteMatch { Allowed = allowed, PathRoute = pathRoute, Parameters = pathParams };
        }

        public string Url(string name, IReadOnlyDictionary<string, object> values)
        {
            if (!named.TryGetValue(name, out Route route))
            {
                string close = Values.Closest(name, named.Keys);
                throw new KScriptException($"There is no route named '{name}'." + (close != null ? $" Did you mean {close}?" : string.Empty), "Argument");
            }
            return route.Pattern.Url(values);
        }
    }
}
