using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using KScript.Syntax.Markup;

namespace KScript.Scaffolding
{
    /// <summary>What <c>kscript make route</c> did: every file it made or changed, in order.</summary>
    public sealed class RouteResult
    {
        public List<(string Action, string Path)> Files { get; } = new List<(string, string)>();
        /// <summary>The line added to the route file, in that file's format.</summary>
        public string Route { get; set; }
        public string RoutesFile { get; set; }
        public string Hint { get; set; }
    }

    /// <summary>
    /// <c>kscript make route users</c>: adds a route to the project's route file (routes.kscript, or api.kscript with
    /// --api), making the file if it's missing. The controller it names is made from the controller template if it
    /// doesn't exist, and gets the function the route calls if it doesn't have it; the route file imports it. The
    /// route line itself comes from the route template, so a project can change it in _templates/route.kscript.
    /// </summary>
    public static class RouteMaker
    {
        private static readonly string[] Methods = { "get", "post", "put", "patch", "delete", "options", "any" };

        public static RouteResult Make(MakeRequest request)
        {
            string root = Path.GetFullPath(string.IsNullOrEmpty(request.Root) ? "." : request.Root);
            var options = new Dictionary<string, string>(request.Options ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            var project = KScript.Projects.ProjectConfig.FindAndLoad(root);
            string format = ScriptFormat.Resolve(options, project);
            var result = new RouteResult();

            // The path: users, /users/{id}, people/[id] (page style) or / for the home page.
            string raw = (request.Name ?? "").Trim().Replace('\\', '/');
            if (raw.Length == 0) throw new StubException("Give the route a path, e.g. kscript make route users or kscript make route \"users/{id}\".");
            string[] segments = raw.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Length > 2 && s[0] == '[' && s[^1] == ']' ? "{" + s.Substring(1, s.Length - 2) + "}" : s)
                .ToArray();
            if (segments.Any(s => s == ".." || s == "." || s.Contains(':') || s.Contains('"')))
                throw new StubException($"'{request.Name}' is not a route path. Use words and {{parameters}}, e.g. users/{{id}}.");
            string routePath = "/" + string.Join("/", segments);
            var parameters = segments.Where(IsParam).Select(s => s.Trim('{', '}').TrimEnd('?', '*')).ToList();

            string method = options.TryGetValue("method", out var m) ? m.Trim().ToLowerInvariant() : "get";
            if (!Methods.Contains(method)) throw new StubException($"--method should be one of {string.Join(", ", Methods)}, not '{m}'.");
            bool resource = StubTemplate.IsSet(options.GetValueOrDefault("resource"));
            bool api = StubTemplate.IsSet(options.GetValueOrDefault("api"));
            string view = options.TryGetValue("view", out var v) && StubTemplate.IsSet(v) ? v.Trim().Trim('/') : null;
            if (view == "true") view = segments.Length == 0 ? "home" : string.Join("/", segments.Where(s => !IsParam(s)));
            if (resource && parameters.Count > 0)
                throw new StubException("A resource path has no parameters (Resource adds /{id} itself): kscript make route people --resource.");
            if (resource && view != null) throw new StubException("Use --resource or --view, not both.");

            // The controller and the function that answers: --to people.list, or --controller and --action, or the path.
            string controller = null, action = null;
            if (options.TryGetValue("to", out var to) && StubTemplate.IsSet(to))
            {
                int dot = to.LastIndexOf('.');
                if (dot <= 0 && !resource) throw new StubException($"--to names a controller function, such as people.list, not '{to}'.");
                controller = dot > 0 ? to.Substring(0, dot) : to;
                action = dot > 0 ? to.Substring(dot + 1) : null;
            }
            if (options.TryGetValue("controller", out var c) && StubTemplate.IsSet(c)) controller = c;
            if (options.TryGetValue("action", out var a) && StubTemplate.IsSet(a)) action = a;
            controller = (controller ?? string.Join("/", segments.Where(s => !IsParam(s)))).Replace('\\', '/').Trim('/');
            if (controller.Length == 0) controller = "home";
            controller = string.Join("/", controller.Split('/').Select(StubNames.Kebab));
            action = StubNames.Snake(action ?? method switch
            {
                "post" => "store",
                "put" or "patch" => "update",
                "delete" => "destroy",
                _ => parameters.Count > 0 ? "show" : "index",
            });

            // The route file, and the import path of the controller from it.
            string routesFile = RoutesFile(root, project, api, format);
            string routesFull = Path.Combine(root, routesFile);
            bool routesExists = File.Exists(routesFull);
            bool xml = routesExists ? !MarkupParser.IsCodePath(routesFull) : format == ScriptFormat.Xml;
            string text = routesExists ? File.ReadAllText(routesFull).Replace("\r\n", "\n") : NewRoutesFile(api, xml);

            string controllerFile = null, alias = null;
            if (view == null)
            {
                controllerFile = ExistingScript(root, Folder(project, "controllers") + controller, xml ? ".ks.xml" : ".kscript");
                string import = Path.GetRelativePath(Path.GetDirectoryName(routesFull)!, Path.Combine(root, controllerFile)).Replace('\\', '/');
                alias = ImportedAs(text, import) ?? UniqueAlias(text, controller);
                options["import"] = import;
            }

            // The route line, from the route template.
            options["route_path"] = routePath;
            options["method"] = method == "any" ? "Any" : StubNames.Pascal(method);
            options["action"] = action;
            options["alias"] = alias ?? "";
            if (view != null) options["view"] = view;
            if (options.TryGetValue("name", out var routeName)) { options["route_name"] = routeName; options.Remove("name"); }
            // The template is rendered as .kscript; for an XML route file the lines are converted below.
            var routeOptions = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase) { ["format"] = ScriptFormat.KScript };
            var route = Scaffolder.Make(new MakeRequest { Type = "route", Name = controller, Root = root, Options = routeOptions, Rendering = true });
            string line = route.Content.Trim();
            var lines = new List<string>();
            if (view == null && ImportedAs(text, options["import"]) == null) lines.Add($"import \"{options["import"]}\" as {alias}");
            int importCount = lines.Count;
            lines.AddRange(line.Split('\n').Select(l => l.TrimEnd()));
            if (xml) lines = ToXmlLines(lines);
            result.Route = string.Join("\n", lines.Skip(importCount));

            string routeLine = lines.Skip(importCount).First();
            if (text.Split('\n').Any(l => Normalise(l) == Normalise(routeLine)))
                throw new StubException($"{routesFile} already has that route: {routeLine.Trim()}");
            if (!resource && HasRoute(text, method, routePath, xml))
                throw new StubException($"{routesFile} already has a {method.ToUpperInvariant()} {routePath} route. Pick another path or --method, or edit {routesFile}.");

            text = Insert(text, lines.Take(importCount).ToList(), lines.Skip(importCount).ToList(), xml, routesFile);

            // The controller: made if it's missing, or given the function if it doesn't have it.
            if (view == null)
            {
                string full = Path.Combine(root, controllerFile);
                var controllerOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["format"] = xml ? "xml" : "kscript" };
                if (resource) controllerOptions["resource"] = "true";
                else controllerOptions["actions"] = action;
                if (parameters.Count > 0) controllerOptions["params"] = string.Join(",", parameters);
                if (!File.Exists(full))
                {
                    if (!request.DryRun)
                        Scaffolder.Make(new MakeRequest { Type = "controller", Name = controller, Root = root, Options = controllerOptions, To = controllerFile });
                    result.Files.Add(("Created", controllerFile));
                }
                else
                {
                    string source = File.ReadAllText(full).Replace("\r\n", "\n");
                    var missing = (resource ? new[] { "index", "show", "store", "update", "destroy" } : new[] { action })
                        .Where(f => !Regex.IsMatch(source, $@"\bfn\s+{Regex.Escape(f)}\s*\(|<fn\s+name=""{Regex.Escape(f)}""")).ToList();
                    // A resource only routes to the functions the controller has, so a partial one is left as it is.
                    if (missing.Count > 0 && !resource)
                    {
                        controllerOptions["append"] = "true";
                        controllerOptions["actions"] = string.Join(",", missing);
                        string added = Scaffolder.Make(new MakeRequest { Type = "controller", Name = controller, Root = root, Options = controllerOptions, To = controllerFile, Rendering = true }).Content;
                        string updated = AppendFunctions(source, added, xml);
                        if (Regex.IsMatch(added, @"@(response|view|redirect|abort)\(") && !Regex.IsMatch(source, @"^\s*(import\s+KScript\.Http\b|<import\s+package=""KScript\.Http"")", RegexOptions.Multiline)) updated = AddHttpImport(updated, xml);
                        if (!request.DryRun) File.WriteAllText(full, updated);
                        result.Files.Add(("Added " + string.Join(", ", missing.Select(f => f + "()")) + " to", controllerFile));
                    }
                }
            }
            else
            {
                string viewFile = Folder(project, "views") + view;
                if (!File.Exists(Path.Combine(root, viewFile + ".html")) && !File.Exists(Path.Combine(root, viewFile + ".md")))
                {
                    var made = Scaffolder.Make(new MakeRequest { Type = "view", Name = view, Root = root, DryRun = request.DryRun });
                    result.Files.Add(("Created", made.RelativePath));
                }
            }

            if (!request.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(routesFull)!);
                File.WriteAllText(routesFull, text);
            }
            result.Files.Insert(0, (routesExists ? "Added to" : "Created", routesFile));
            result.RoutesFile = routesFile;
            string url = (api ? (project?.Http?.ApiPrefix ?? "/api").TrimEnd('/') : "") + routePath;
            result.Hint = resource
                ? $"kscript routes lists the routes it adds ({url}, {url}/{{id}}). kscript serve answers them."
                : $"Run kscript serve and {(method == "get" ? "open" : "send " + method.ToUpperInvariant())} {Regex.Replace(url, @"\{[^}]*\}", "1")}. kscript routes lists every route.";
            string stem = Regex.Replace(routesFile, @"\.(kscript|ks\.xml)$", "");
            if (project?.Http?.Routes == null && !KScript.Projects.HttpSettings.DefaultRouteNames.Contains(stem))
                result.Hint += $"\nkscript serve only finds {routesFile} once kscript.json lists it: \"http\": {{ \"routes\": [{{ \"file\": \"{routesFile}\"{(api ? ", \"api\": true" : "")} }}] }}.";
            return result;
        }

        private static bool IsParam(string segment) => segment.Length > 2 && segment[0] == '{' && segment[^1] == '}';

        /// <summary>routes.* (or api.* with --api) in the site folder, or the first such file kscript.json's http.routes lists.</summary>
        private static string RoutesFile(string root, KScript.Projects.ProjectConfig project, bool api, string format)
        {
            var listed = project?.Http?.Routes?.FirstOrDefault(r => r.Api == api && !string.IsNullOrEmpty(r.File));
            if (listed != null) return listed.File.Replace('\\', '/');
            string ext = format == ScriptFormat.Xml ? ".ks.xml" : ".kscript";
            // A project that keeps its routes at the top (routes.kscript, api.kscript) goes on doing so.
            string top = ExistingScript(root, api ? "api" : "routes", ext);
            if (File.Exists(Path.Combine(root, top))) return top;
            return ExistingScript(root, Folder(project, "routes") + (api ? "api" : "web"), ext);
        }

        /// <summary>The folder for a kind of file with a trailing /, or "" for the project folder itself.</summary>
        private static string Folder(KScript.Projects.ProjectConfig project, string name)
        {
            string folder = (project?.PathFor(name) ?? KScript.Projects.ProjectConfig.DefaultPaths[name]).Replace('\\', '/').Trim('/');
            return folder.Length == 0 || folder == "." ? "" : folder + "/";
        }

        /// <summary>The script that exists as name.kscript or name.ks.xml, or name + the given ending for a new one.</summary>
        private static string ExistingScript(string root, string name, string ext)
        {
            foreach (string e in new[] { ext, ext == ".kscript" ? ".ks.xml" : ".kscript" })
                if (File.Exists(Path.Combine(root, name + e))) return name + e;
            return name + ext;
        }

        internal static string NewRoutesFile(bool api, bool xml)
        {
            string comment = api
                ? "API routes, served by kscript serve under /api with JSON errors (see the Routes and APIs guide). kscript routes lists them."
                : "Web routes, served by kscript serve (see the Routes and APIs guide). kscript routes lists them.";
            return xml ? $"<!-- {comment} -->\n<KScript.Routes>\n</KScript.Routes>\n" : $"// {comment}\nKScript.Routes {{\n}}\n";
        }

        private static readonly Regex CodeImport = new Regex(@"^\s*import\s+""(?<src>[^""]+)""(\s+as\s+(?<as>\w+))?", RegexOptions.Multiline);
        private static readonly Regex XmlImport = new Regex(@"<import\s+[^>]*?src=""(?<src>[^""]+)""[^>]*?>", RegexOptions.Multiline);

        /// <summary>The name a route file already imports this script as, or null.</summary>
        private static string ImportedAs(string text, string src)
        {
            foreach (Match match in CodeImport.Matches(text))
                if (match.Groups["src"].Value == src) return match.Groups["as"].Success ? match.Groups["as"].Value : null;
            foreach (Match match in XmlImport.Matches(text))
                if (match.Groups["src"].Value == src && Regex.Match(match.Value, @"\bas=""(\w+)""") is { Success: true } asMatch) return asMatch.Groups[1].Value;
            return null;
        }

        /// <summary>users for controllers/users, or admin_users when users is taken by another import.</summary>
        private static string UniqueAlias(string text, string controller)
        {
            string last = StubNames.Snake(controller.Split('/').Last());
            string full = StubNames.Snake(controller.Replace('/', '-'));
            foreach (string candidate in new[] { last, full })
                if (!Regex.IsMatch(text, $@"\bas\s*=?\s*""?{Regex.Escape(candidate)}\b")) return candidate;
            for (int i = 2; ; i++)
                if (!Regex.IsMatch(text, $@"\bas\s*=?\s*""?{full}{i}\b")) return full + i;
        }

        private static bool HasRoute(string text, string method, string path, bool xml)
        {
            string element = method == "any" ? "Any" : StubNames.Pascal(method);
            string pattern = xml ? $@"<{element}\s+[^>]*path=""{Regex.Escape(path)}""" : $@"\b{element}\s*\(\s*path:\s*""?{Regex.Escape(path)}""?\s*[,)]";
            return Regex.IsMatch(text, pattern);
        }

        private static string Normalise(string line) => Regex.Replace(line.Trim(), @"\s+", " ");

        /// <summary>The import and route lines as XML elements, converted the way --format xml converts a script.</summary>
        internal static List<string> ToXmlLines(List<string> lines)
        {
            string code = "KScript.Routes {\n" + string.Join("\n", lines.Select(l => "  " + l)) + "\n}\n";
            var (_, xml) = ScriptFormat.Apply("route.kscript", "routes.kscript", code, ScriptFormat.Xml);
            var all = xml.Replace("\r\n", "\n").Split('\n').ToList();
            int open = all.FindIndex(l => l.TrimStart().StartsWith("<KScript.Routes", StringComparison.Ordinal));
            int close = all.FindLastIndex(l => l.Trim() == "</KScript.Routes>");
            if (open < 0 || close <= open) throw new StubException("The route template didn't give a route that converts to XML:\n" + string.Join("\n", lines));
            return all.Skip(open + 1).Take(close - open - 1).Where(l => l.Trim().Length > 0).Select(l => l.StartsWith("  ", StringComparison.Ordinal) ? l.Substring(2) : l).ToList();
        }

        /// <summary>
        /// Adds the imports after the route file's last import (or at the top of the routes block) and the route at the
        /// end of the block, indented like the block's other lines.
        /// </summary>
        internal static string Insert(string text, List<string> imports, List<string> routes, bool xml, string file)
        {
            var lines = text.Split('\n').ToList();
            int open = lines.FindIndex(l => xml ? l.TrimStart().StartsWith("<KScript.Routes", StringComparison.Ordinal) : Regex.IsMatch(l, @"^\s*KScript\.Routes\b.*\{\s*(//.*)?$"));
            int close = -1;
            if (open >= 0)
            {
                string indent = Regex.Match(lines[open], @"^\s*").Value;
                close = lines.FindLastIndex(l => xml ? l.Trim() == "</KScript.Routes>" : l.TrimEnd() == indent + "}");
            }
            if (open < 0 || close <= open)
                throw new StubException($"{file} has no {(xml ? "<KScript.Routes> … </KScript.Routes>" : "KScript.Routes { … }")} block to add the route to.");

            string inner = Regex.Match(lines[open], @"^\s*").Value + "  ";
            var body = lines.Skip(open + 1).Take(close - open - 1).ToList();
            var existing = body.FirstOrDefault(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith("<!--", StringComparison.Ordinal));
            if (existing != null) inner = Regex.Match(existing, @"^\s*").Value;

            // The route goes last, after a blank line when the block has other routes.
            var routeLines = routes.Select(l => inner + l).ToList();
            if (body.Any(l => l.Trim().Length > 0) && lines[close - 1].Trim().Length > 0 && !IsRouteLine(lines[close - 1])) routeLines.Insert(0, "");
            lines.InsertRange(close, routeLines);

            if (imports.Count > 0)
            {
                int lastImport = -1;
                for (int i = open + 1; i < close; i++)
                    if (Regex.IsMatch(lines[i], xml ? @"^\s*<import\b" : @"^\s*import\b")) lastImport = i;
                var importLines = imports.Select(l => inner + l).ToList();
                if (lastImport >= 0) lines.InsertRange(lastImport + 1, importLines);
                else
                {
                    // A new import at the top of the block, apart from the routes below it.
                    bool routesFollow = lines[open + 1].Trim().Length > 0 && lines[open + 1].Trim() != (xml ? "</KScript.Routes>" : "}");
                    if (routesFollow) importLines.Add("");
                    lines.InsertRange(open + 1, importLines);
                }
            }
            return string.Join("\n", lines);
        }

        /// <summary>A one-line route such as Get(path: …) or &lt;Get path="…" /&gt;, so the new route joins the list without a blank line.</summary>
        private static bool IsRouteLine(string line) =>
            Regex.IsMatch(line.Trim(), @"^(<(Get|Post|Put|Patch|Delete|Options|Any|Route|Resource)\b.*/>|(Get|Post|Put|Patch|Delete|Options|Any|Route|Resource)\s*\(.*\))$");

        /// <summary>Adds functions at the end of a controller: before &lt;/kscript&gt; in XML, or inside a kscript { } block.</summary>
        private static string AppendFunctions(string source, string functions, bool xml)
        {
            string body = functions.Trim('\n');
            if (xml)
            {
                int end = source.LastIndexOf("</kscript>", StringComparison.Ordinal);
                if (end < 0) return source.TrimEnd('\n') + "\n" + body + "\n";
                string inner = string.Join("\n", ExtractXmlBody(body).Select(l => "  " + l));
                return source.Substring(0, end).TrimEnd('\n', ' ') + "\n\n" + inner + "\n" + source.Substring(end);
            }
            var lines = source.TrimEnd('\n').Split('\n').ToList();
            int first = lines.FindIndex(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("//", StringComparison.Ordinal));
            if (first >= 0 && Regex.IsMatch(lines[first], @"^kscript\s*\{\s*$") && lines[^1].Trim() == "}")
            {
                lines.RemoveAt(lines.Count - 1);
                return string.Join("\n", lines).TrimEnd('\n') + "\n\n" + string.Join("\n", body.Split('\n').Select(l => l.Length > 0 ? "  " + l : l)) + "\n}\n";
            }
            return string.Join("\n", lines) + "\n\n" + body + "\n";
        }

        /// <summary>Adds import KScript.Http after a controller's opening comments, for @response() in a function just added.</summary>
        private static string AddHttpImport(string source, bool xml)
        {
            var lines = source.Split('\n').ToList();
            if (xml)
            {
                int open = lines.FindIndex(l => Regex.IsMatch(l, @"^\s*<kscript\b[^>]*>\s*$"));
                if (open >= 0) lines.Insert(open + 1, "  <import package=\"KScript.Http\" />");
                return string.Join("\n", lines);
            }
            // After the comments at the top of the file (but not a /// comment, which belongs to the function below it).
            int at = 0;
            while (at < lines.Count && lines[at].StartsWith("//", StringComparison.Ordinal) && !lines[at].StartsWith("///", StringComparison.Ordinal)) at++;
            lines.Insert(at, "import KScript.Http");
            if (at + 1 < lines.Count && lines[at + 1].Trim().Length > 0 && !lines[at + 1].TrimStart().StartsWith("import", StringComparison.Ordinal)) lines.Insert(at + 1, "");
            return string.Join("\n", lines);
        }

        /// <summary>The lines inside &lt;kscript&gt; … &lt;/kscript&gt; of a converted controller, without its indent.</summary>
        private static IEnumerable<string> ExtractXmlBody(string xml)
        {
            var all = xml.Split('\n').ToList();
            int open = all.FindIndex(l => l.TrimStart().StartsWith("<kscript", StringComparison.Ordinal));
            int close = all.FindLastIndex(l => l.Trim() == "</kscript>");
            if (open < 0 || close <= open) return all;
            return all.Skip(open + 1).Take(close - open - 1).Select(l => l.StartsWith("  ", StringComparison.Ordinal) ? l.Substring(2) : l);
        }
    }
}
