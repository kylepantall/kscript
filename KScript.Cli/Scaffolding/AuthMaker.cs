using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using KScript.Projects;
using KScript.Syntax.Markup;

namespace KScript.Scaffolding
{
    /// <summary>What <c>kscript make auth</c> did: every file it made or changed, in order, and what to do next.</summary>
    public sealed class AuthResult
    {
        public List<(string Action, string Path)> Files { get; } = new List<(string, string)>();
        public List<string> Next { get; } = new List<string>();
    }

    /// <summary>
    /// <c>kscript make auth</c>: sign-in for a web app in one go. It writes a controller (controllers/accounts), the
    /// pages it renders (views/auth/: sign in, sign up, forgot and reset password, the account page with API tokens),
    /// adds their routes to the web routes file (and, with --api, token routes to the API file), and gives
    /// kscript.json a password rule. The files come from Scaffolding/Auth, written once as .kscript and converted with
    /// --format xml like every other template.
    /// </summary>
    public static class AuthMaker
    {
        private const string ResourcePrefix = "KScript.Scaffolding.Auth/";
        private static readonly string[] RouteTemplates = { "routes.kscript", "api-routes.kscript" };

        /// <summary>The routes the sign-in pages use, so a second run (or a project that has its own) is caught.</summary>
        private static readonly string[] Paths = { "/login", "/register", "/logout", "/account" };

        public static AuthResult Make(string rootFolder, IDictionary<string, string> given, bool force, bool dryRun)
        {
            string root = Path.GetFullPath(string.IsNullOrEmpty(rootFolder) ? "." : rootFolder);
            var options = new Dictionary<string, string>(given ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            ProjectConfig project = ProjectConfig.FindAndLoad(root);
            string format = ScriptFormat.Resolve(options, project);
            bool api = StubTemplate.IsSet(options.GetValueOrDefault("api"));
            var result = new AuthResult();

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in options) values[kv.Key.Replace('-', '_')] = kv.Value;
            ScriptFormat.AddValues(values, format);
            values["api"] = api ? "true" : "false";
            values["paths.controllers"] = Folder(project, "controllers");
            values["paths.views"] = Folder(project, "views");
            values["api_prefix"] = (project?.Http.ApiPrefix ?? "/api").TrimEnd('/');
            bool hasLayout = File.Exists(Path.Combine(root, "_layout.html"));
            values["layout"] = "/_layout.html";
            values["new_layout"] = hasLayout ? "false" : "true";

            string controllerFile = Join(values["paths.controllers"], "accounts" + values["ext"]);
            string webRoutes = RoutesFile(root, project, api: false, format);
            string apiRoutes = RoutesFile(root, project, api: true, format);
            values["routes_file"] = webRoutes;
            values["controller_import"] = ImportPath(root, webRoutes, controllerFile);
            values["api_controller_import"] = ImportPath(root, apiRoutes, controllerFile);

            // The files: the controller, the views and (when the site has none) a layout.
            var template = new ProjectTemplate { Name = "auth", Files = Resources().Where(f => !RouteTemplates.Contains(f.Path)).ToList() };
            Dictionary<string, string> files = template.Render(values);
            if (!force)
            {
                var existing = files.Keys.Where(f => File.Exists(Path.Combine(root, f))).OrderBy(f => f, StringComparer.Ordinal).ToList();
                if (existing.Count > 0)
                    throw new StubException($"This project already has {string.Join(", ", existing)}. Use --force to replace {(existing.Count == 1 ? "it" : "them")}.");
            }

            // The routes, added to the route files in their own format.
            var routeEdits = new List<(string File, string Text, bool Existed)>();
            foreach (var (name, file) in new[] { ("routes.kscript", webRoutes), ("api-routes.kscript", apiRoutes) })
            {
                var (header, body) = Scaffolder.SplitHeader(Resources().First(f => f.Path == name).Source, name);
                if (header.TryGetValue("when", out var when) && !StubTemplate.IsSet(values.GetValueOrDefault(when.Trim()))) continue;
                string full = Path.Combine(root, file);
                bool existed = File.Exists(full);
                bool xml = existed ? !MarkupParser.IsCodePath(full) : format == ScriptFormat.Xml;
                bool isApi = name.StartsWith("api", StringComparison.Ordinal);
                string text = existed ? File.ReadAllText(full).Replace("\r\n", "\n") : RouteMaker.NewRoutesFile(isApi, xml);
                if (!force)
                {
                    string taken = (isApi ? new[] { "/tokens", "/me" } : Paths).FirstOrDefault(p => HasPath(text, p, xml));
                    if (taken != null) throw new StubException($"{file} already has a {taken} route. Remove it, or use --force to add the sign-in routes anyway.");
                }
                var lines = StubTemplate.Render(body, values).Trim('\n').Split('\n').Select(l => l.TrimEnd()).ToList();
                if (xml) lines = RouteMaker.ToXmlLines(lines);
                var imports = lines.TakeWhile(l => Regex.IsMatch(l, xml ? @"^\s*<import\b" : @"^\s*import\b")).ToList();
                var routes = lines.Skip(imports.Count).SkipWhile(l => l.Trim().Length == 0).ToList();
                imports = imports.Where(i => !text.Split('\n').Any(l => l.Trim() == i.Trim())).ToList();
                routeEdits.Add((file, RouteMaker.Insert(text, imports, routes, xml, file), existed));
            }

            // kscript.json: a rule for new passwords, which @register() and @reset_password() check.
            string configFile = project?.FilePath;
            string config = configFile != null ? File.ReadAllText(configFile).Replace("\r\n", "\n") : null;
            string newConfig = config != null && !Regex.IsMatch(config, @"""types""\s*:") ? AddPasswordRule(config) : null;

            if (!dryRun)
            {
                foreach (var (rel, content) in files)
                {
                    string full = Path.Combine(root, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllText(full, content);
                }
                foreach (var (file, text, _) in routeEdits)
                {
                    string full = Path.Combine(root, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllText(full, text);
                }
                if (newConfig != null) File.WriteAllText(configFile, newConfig);
            }

            foreach (var (file, _, existed) in routeEdits) result.Files.Add((existed ? "Added to" : "Created", file));
            foreach (string rel in files.Keys.OrderBy(f => f.EndsWith(".html", StringComparison.Ordinal) ? 1 : 0).ThenBy(f => f, StringComparer.Ordinal))
                result.Files.Add(("Created", rel));
            if (newConfig != null) result.Files.Add(("Added a password rule to", Path.GetRelativePath(root, configFile).Replace('\\', '/')));

            result.Next.Add("kscript serve, then open /register to make an account and /account to see it.");
            result.Next.Add("Password reset links are written to the server's log until you email them (see send_reset in " + controllerFile + ").");
            if (api) result.Next.Add($"POST {values["api_prefix"]}/tokens with {{ \"email\": …, \"password\": … }} gives a token for Authorization: Bearer.");
            if (project?.Http.Routes == null)
            {
                foreach (var (file, _, _) in routeEdits.Where(e => !HttpSettings.DefaultRouteNames.Contains(Regex.Replace(e.File, @"\.(kscript|ks\.xml)$", ""))))
                    result.Next.Add($"kscript serve only finds {file} once kscript.json lists it in http.routes.");
            }
            return result;
        }

        /// <summary>The kit's files, by their path inside Scaffolding/Auth.</summary>
        private static List<(string Path, string Source)> Resources()
        {
            var asm = typeof(AuthMaker).Assembly;
            return asm.GetManifestResourceNames()
                .Select(n => (Name: n, Normal: n.Replace('\\', '/')))
                .Where(n => n.Normal.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .Select(n =>
                {
                    using var stream = asm.GetManifestResourceStream(n.Name)!;
                    using var reader = new StreamReader(stream);
                    return (n.Normal.Substring(ResourcePrefix.Length), reader.ReadToEnd().Replace("\r\n", "\n"));
                })
                .OrderBy(f => f.Item1, StringComparer.Ordinal)
                .ToList();
        }

        private static string Folder(ProjectConfig project, string name)
        {
            string folder = (project?.PathFor(name) ?? ProjectConfig.DefaultPaths[name]).Replace('\\', '/').Trim('/');
            return folder.Length == 0 ? "." : folder;
        }

        private static string Join(string folder, string file) => folder == "." ? file : folder + "/" + file;

        private static string RoutesFile(string root, ProjectConfig project, bool api, string format)
        {
            var listed = project?.Http?.Routes?.FirstOrDefault(r => r.Api == api && !string.IsNullOrEmpty(r.File));
            if (listed != null) return listed.File.Replace('\\', '/');
            string ext = format == ScriptFormat.Xml ? ".ks.xml" : ".kscript";
            foreach (string name in api ? new[] { "api", "routes/api" } : new[] { "routes", "routes/web" })
                foreach (string e in new[] { ".kscript", ".ks.xml" })
                    if (File.Exists(Path.Combine(root, name + e))) return name + e;
            string routes = Folder(project, "routes");
            return Join(routes, (api ? "api" : "web") + ext);
        }

        private static string ImportPath(string root, string routesFile, string target) =>
            Path.GetRelativePath(Path.GetDirectoryName(Path.Combine(root, routesFile))!, Path.Combine(root, target)).Replace('\\', '/');

        private static bool HasPath(string text, string path, bool xml) =>
            Regex.IsMatch(text, xml ? $@"\bpath=""{Regex.Escape(path)}""" : $@"\bpath:\s*""?{Regex.Escape(path)}""?\s*[,)]");

        /// <summary>Adds "types": { "password": { "min": 10 } } as kscript.json's first setting, keeping its comments and layout.</summary>
        internal static string AddPasswordRule(string json)
        {
            int open = json.IndexOf('{');
            if (open < 0) return null;
            int lineEnd = json.IndexOf('\n', open);
            bool empty = Regex.IsMatch(json.Substring(open + 1), @"^\s*\}");
            string rule = "  // New passwords (@register, @reset_password) must be at least this long.\n  \"types\": { \"password\": { \"min\": 10 } }" + (empty ? "\n" : ",\n");
            if (lineEnd < 0 || empty) return json.Substring(0, open + 1) + "\n" + rule + json.Substring(open + 1).TrimStart();
            // After the $schema line, when the file starts with one.
            int after = lineEnd + 1;
            string next = json.Substring(after);
            Match schema = Regex.Match(next, @"^\s*""\$schema""[^\n]*\n");
            if (schema.Success) after += schema.Length;
            return json.Substring(0, after) + rule + json.Substring(after);
        }
    }
}
