using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using KScript.Cli;
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
    /// pages it renders (views/auth/: sign in, sign up, forgot and reset password, the account page), the components
    /// those pages are made of (_components/: the forms, the account details, passkeys and API tokens), adds their routes to the web routes file (and, with --api, token routes to the API file), and gives
    /// kscript.json a password rule. The files come from Scaffolding/Auth, written once as .kscript and converted with
    /// --syntax xml like every other template.
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
            bool passkeys = StubTemplate.IsSet(options.GetValueOrDefault("passkeys"));
            if (!passkeys && (options.ContainsKey("origin") || options.ContainsKey("rp-id")))
                throw new StubException("--origin and --rp-id need --passkeys.");
            var result = new AuthResult();

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in options) values[kv.Key.Replace('-', '_')] = kv.Value;
            ScriptFormat.AddValues(values, format);
            values["api"] = api ? "true" : "false";
            values["passkeys"] = passkeys ? "true" : "false";
            values["paths.controllers"] = Folder(project, "controllers");
            values["paths.views"] = Folder(project, "views");
            values["paths.requests"] = Folder(project, "requests");
            values["paths.components"] = Folder(project, "components");
            values["api_prefix"] = (project?.Http.ApiPrefix ?? "/api").TrimEnd('/');
            bool hasLayout = File.Exists(Path.Combine(root, "_layout.html"));
            values["layout"] = "/_layout.html";
            values["new_layout"] = hasLayout ? "false" : "true";

            string controllerFile = Join(values["paths.controllers"], "accounts" + values["ext"]);
            string webRoutes = RoutesFile(root, project, api: false, format);
            string apiRoutes = RoutesFile(root, project, api: true, format);
            var mounted = passkeys ? PasskeyRouteFiles(root, project) : null;
            if (mounted != null)
            {
                var web = mounted.FirstOrDefault(r => !r.Api && r.Prefix.Trim('/').Length == 0);
                var rest = mounted.FirstOrDefault(r => r.Api);
                if (project?.Http.Routes != null && (web == null || (api && rest == null)))
                    throw new StubException("The passkey kit needs an unprefixed web route file in http.routes, and an API route file when --api is used.");
                webRoutes = web?.File ?? webRoutes;
                apiRoutes = rest?.File ?? apiRoutes;
                if (rest != null) values["api_prefix"] = rest.Prefix.TrimEnd('/');
                // A refresh without --api must keep any generated /me handler still routed to this controller.
                string prior = Path.Combine(root, apiRoutes);
                if (force && !api && File.Exists(prior) && KitRoutes(File.ReadAllText(prior), !MarkupParser.IsCodePath(prior))
                    .Any(r => r.Handler?.TrimStart('@') == "accounts.me"))
                    api = true;
                values["api"] = api ? "true" : "false";
            }
            values["routes_file"] = webRoutes;
            values["controller_import"] = ImportPath(root, webRoutes, controllerFile);
            values["api_controller_import"] = ImportPath(root, apiRoutes, controllerFile);
            // The sign-up form's type has its own file, which the controller imports.
            values["sign_up_import"] = ImportPath(root, controllerFile, Join(values["paths.requests"], "sign-up" + values["ext"]));

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
                string[] paths = isApi ? (passkeys ? new[] { "/me" } : new[] { "/tokens", "/me" }) : Paths;
                var lines = StubTemplate.Render(body, values).Trim('\n').Split('\n').Select(l => l.TrimEnd()).ToList();
                if (xml) lines = RouteMaker.ToXmlLines(lines);
                if (passkeys)
                {
                    // --force can refresh this kit's files, but an existing handler on the same path
                    // can still receive requests ahead of the generated one.
                    string rendered = string.Join("\n", lines);
                    var expected = KitRoutes(xml ? "<KScript.Routes>\n" + rendered + "\n</KScript.Routes>" : "Routes {\n" + rendered + "\n}", xml);
                    var reserved = expected.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (string legacy in isApi ? new[] { "/tokens" } : new[] { "/forgot-password", "/reset-password", "/reset-password/{token}" })
                        reserved.Add(legacy);
                    string prefix = mounted.FirstOrDefault(r => r.File == file)?.Prefix ?? (isApi ? values["api_prefix"] : "");
                    foreach (var other in mounted.Where(r => r.File != file && r.Prefix.Trim('/') == prefix.Trim('/')))
                    {
                        string src = Path.Combine(root, other.File);
                        if (!File.Exists(src)) continue;
                        var clash = KitRoutes(File.ReadAllText(src), !MarkupParser.IsCodePath(src)).FirstOrDefault(r => reserved.Contains(r.Path));
                        if (clash.Path != null) throw new StubException($"{other.File} already has a {clash.Path} route on the same prefix.");
                    }
                    var overlap = KitRoutes(text, xml).Where(r => reserved.Contains(r.Path)).ToList();
                    if (overlap.Count > 0)
                    {
                        if (force && overlap.Count == expected.Count && expected.All(overlap.Contains)) continue;
                        throw new StubException($"{file} already has a {overlap[0].Path} route. Remove the overlapping sign-in routes before adding the passkey kit.");
                    }
                }
                else
                {
                    // Preserve the password kit's existing --force behavior on partially overlapping files.
                    if (force && paths.All(p => HasPath(text, p, xml)))
                    {
                        if (!isApi && !text.Contains("accounts.sign_in", StringComparison.Ordinal))
                            throw new StubException("This project already has different sign-in routes. Remove the existing auth routes before switching sign-in kits.");
                        continue;
                    }
                    string taken = paths.FirstOrDefault(p => HasPath(text, p, xml));
                    if (!force && taken != null) throw new StubException($"{file} already has a {taken} route. Remove it, or use --force to add the sign-in routes anyway.");
                }
                var imports = lines.TakeWhile(l => Regex.IsMatch(l, xml ? @"^\s*<import\b" : @"^\s*import\b")).ToList();
                var routes = lines.Skip(imports.Count).SkipWhile(l => l.Trim().Length == 0).ToList();
                imports = imports.Where(i => !text.Split('\n').Any(l => l.Trim() == i.Trim())).ToList();
                routeEdits.Add((file, ScriptFormat.Tidy(full, RouteMaker.Insert(text, imports, routes, xml, file), global::KScript.Language.Formatting.FormatOptions.From(project)), existed));
            }

            // kscript.json: a rule for new passwords, which @register() and @reset_password() check.
            string configFile = project?.FilePath;
            string config = configFile != null ? File.ReadAllText(configFile).Replace("\r\n", "\n") : null;
            if (passkeys) configFile ??= Path.Combine(root, "kscript.json");
            string newConfig = passkeys ? PasskeyConfig(config ?? "{}", configFile, options, project) : config != null && !Regex.IsMatch(config, @"""types""\s*:") ? AddPasswordRule(config) : null;

            if (newConfig == config) newConfig = null;
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
            if (newConfig != null) result.Files.Add((passkeys ? "Configured passkeys in" : "Added a password rule to", Path.GetRelativePath(root, configFile).Replace('\\', '/')));

            result.Next.Add("kscript serve, then open /register to make an account and /account to see it.");
            if (passkeys) result.Next.Add("Set http.auth.passkey_origin to your public HTTPS origin before deploying. Add a backup passkey on /account.");
            else result.Next.Add("Password reset links are written to the server's log until you email them (see send_reset in " + controllerFile + ").");
            if (api && passkeys) result.Next.Add($"Create a token on /account to call {values["api_prefix"]}/me.");
            if (api && !passkeys) result.Next.Add($"POST {values["api_prefix"]}/tokens with {{ \"email\": …, \"password\": … }} gives a token for Authorization: Bearer.");
            if (project?.Http.Routes == null)
            {
                foreach (var (file, _, _) in routeEdits.Where(e => !HttpSettings.DefaultRouteNames.Contains(Regex.Replace(e.File, @"\.(kscript|ks\.xml)$", ""))))
                    result.Next.Add($"kscript serve only finds {file} once kscript.json lists it in http.routes.");
            }
            return result;
        }

        private static string PasskeyConfig(string json, string file, Dictionary<string, string> options, ProjectConfig project)
        {
            string origin = options.GetValueOrDefault("origin") ?? project?.Http.Auth.PasskeyOrigin ?? $"http://localhost:{project?.Serve.Port ?? 8080}";
            json = SetJsonProperty(json, new[] { "http", "auth", "passkey_origin" }, JsonConfigEdit.Quote(origin));
            json = SetJsonProperty(json, new[] { "http", "auth", "passkey_signup" }, "true");
            if (options.TryGetValue("rp-id", out string rp))
                json = SetJsonProperty(json, new[] { "http", "auth", "passkey_rp_id" }, JsonConfigEdit.Quote(rp));
            var config = ProjectConfig.Parse(json, file);
            if (config.Http.Auth.PasskeyOrigin == null)
                throw new StubException(string.Join(" ", config.Warnings.Where(w => w.Contains("passkey", StringComparison.Ordinal))));
            return json;
        }

        private static string SetJsonProperty(string json, string[] path, string value) =>
            JsonConfigEdit.SetProperty(json, path, value, () => new StubException("The http and auth settings in kscript.json must be objects."));

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

        private static List<HttpSettings.RouteFile> PasskeyRouteFiles(string root, ProjectConfig project)
        {
            if (project?.Http.Routes != null)
                return project.Http.Routes.Select(r => new HttpSettings.RouteFile
                {
                    File = r.File.Replace('\\', '/'), Prefix = r.Prefix ?? string.Empty,
                    Api = r.Api || Path.GetFileName(r.File) is "api.kscript" or "api.ks.xml",
                }).ToList();

            var files = new List<HttpSettings.RouteFile>();
            foreach (string stem in HttpSettings.DefaultRouteNames)
            {
                string file = new[] { stem + ".kscript", stem + ".ks.xml" }.FirstOrDefault(f => File.Exists(Path.Combine(root, f)));
                if (file == null) continue;
                bool api = Path.GetFileName(stem) == "api";
                files.Add(new HttpSettings.RouteFile { File = file, Api = api, Prefix = api ? project?.Http.ApiPrefix ?? "/api" : "" });
            }
            return files;
        }

        private static List<(string Path, string Method, string Handler, string Middleware)> KitRoutes(string text, bool xml)
        {
            var document = xml ? MarkupParser.Parse(text, DocumentKind.Script) : CodeParser.Parse(text);
            CodeArguments.Normalise(document, global::KScript.Language.Catalog.LanguageCatalog.Default.GetElement);
            var routes = new List<(string, string, string, string)>();
            foreach (var element in document.DescendantElements())
            {
                string method = KScript.Metadata.ElementInfo.BareName(element.Name);
                if (method is not ("Get" or "Post" or "Put" or "Patch" or "Delete" or "Head" or "Options" or "Route" or "Any" or "Resource")) continue;
                string path = element.GetAttributeValue("path");
                if (path == null) continue;
                var middleware = new Stack<string>();
                for (var node = element; node != null; node = node.Parent)
                {
                    if (node.GetAttributeValue("middleware") is string names) middleware.Push(names);
                    if (KScript.Metadata.ElementInfo.BareName(node.Name) == "Group" && node.GetAttributeValue("prefix") is string prefix)
                        path = prefix.TrimEnd('/') + "/" + path.TrimStart('/');
                }
                routes.Add(("/" + path.Trim('/'), method, element.GetAttributeValue("to"), string.Join(",", middleware)));
            }
            return routes;
        }

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
            // A file on one line ({ "name": "x" }) gets the rule straight after its brace, not after the line.
            bool braceOnItsOwnLine = lineEnd >= 0 && json.Substring(open + 1, lineEnd - open - 1).Trim().Length == 0;
            if (!braceOnItsOwnLine || empty) return json.Substring(0, open + 1) + "\n" + rule + json.Substring(open + 1).TrimStart();
            // After the $schema line, when the file starts with one.
            int after = lineEnd + 1;
            string next = json.Substring(after);
            Match schema = Regex.Match(next, @"^\s*""\$schema""[^\n]*\n");
            if (schema.Success) after += schema.Length;
            return json.Substring(0, after) + rule + json.Substring(after);
        }
    }
}
