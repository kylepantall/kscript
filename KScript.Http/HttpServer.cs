using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Http
{
    /// <summary>
    /// <c>kscript serve</c> with routes: requests go to the route files first and then to the site's pages and files.
    /// <c>kscript routes</c> prints the route table.
    /// </summary>
    public static class HttpServer
    {
        /// <summary>
        /// Serves a site. <paramref name="before"/> may answer a request before the routes and pages do (it returns
        /// null to let them); <paramref name="started"/> gets the address once the server is listening. With
        /// <paramref name="production"/> an error is answered with a plain 500 and its details go to the log only.
        /// </summary>
        public static Task<int> RunAsync(string siteFolder, ProjectConfig project, int port, string host, TextWriter log, CancellationToken stop = default,
            Func<Microsoft.AspNetCore.Http.HttpRequest, Task<PageResult>> before = null, Action<string> started = null, bool liveReload = false,
            bool production = false)
        {
            var app = new HttpApp(siteFolder, project, log);
            app.LoadStartupConfig();
            PageSite site = app.PageSite;
            // In production a visitor is not shown an error's message, file and line; the log has them.
            site.HideErrorDetails = production;
            site.RenderPage = app.RenderFilePage;
            if (app.HasRoutes)
            {
                try
                {
                    RouteTable table = app.Table();
                    if (log != null)
                    {
                        var cli = new KScript.Output.CliOutput(log);
                        cli.Info($"Routes: {table.Routes.Count} from {string.Join(", ", app.Files.Select(f => Path.GetFileName(f.Path)))} (kscript routes lists them).");
                        if (app.Settings.OpenApi.Enabled) cli.Info("API docs: [/_api], made from the routes (/_openapi.json and /_openapi.yaml for tools).");
                    }
                }
                catch (KScriptException ex)
                {
                    if (log != null) KScript.Runtime.ErrorReport.Write(log, "The routes have an error; requests get an error page until it is fixed.", ex);
                }
            }
            Func<Microsoft.AspNetCore.Http.HttpRequest, Task<PageResult>> routes = app.HasRoutes || app.Settings.Auth.PasskeyOrigin != null ? app.HandleAsync : null;
            Func<Microsoft.AspNetCore.Http.HttpRequest, Task<PageResult>> handler = before == null ? routes : async request =>
                await before(request) ?? (routes == null ? null : await routes(request));
            LocalApi.Install();
            LocalApi.Serve(app);
            return RunAndForget(app, PageServer.RunAsync(site, port, host, log, handler, stop, onStarted: started,
                reloadRoot: liveReload ? project?.Directory ?? site.Root : null));
        }

        internal static void ProtectStorage(PageSite site, string projectRoot, AuthSettings settings, ConfigValue config = null)
        {
            string Relative(string path) => Path.GetRelativePath(site.Root, path).Replace('\\', '/');
            foreach (string file in Data.DataPackage.LocalDatabaseFiles(projectRoot, config))
                foreach (string suffix in new[] { "", "-journal", "-wal", "-shm" })
                    site.HiddenFiles.Add(Relative(file) + suffix);

            void HideAuthStores(string data, string state)
            {
                foreach (var (folder, store) in new[] { (data, "users"), (data, "_passkeys"), (state, "sessions"), (state, "tokens"), (state, "resets") })
                {
                    string path = Path.Combine(folder, store);
                    string inside = Path.GetRelativePath(path, site.Root).Replace('\\', '/');
                    site.HiddenFolders.Add(!Path.IsPathRooted(inside) && inside != ".." && !inside.StartsWith("../", StringComparison.Ordinal)
                        ? "." : Relative(path));
                }
            }
            var actual = Auth.AuthApp.StorageFolders(projectRoot, AppFolders.AppNameFor(projectRoot, null), settings);
            HideAuthStores(actual.Data, actual.State);
            if (settings.Folder is string configured)
            {
                string previous = Path.GetFullPath(configured, projectRoot);
                HideAuthStores(previous, previous);
            }
        }

        private static async Task<int> RunAndForget(HttpApp app, Task<int> server)
        {
            try
            {
                return await server;
            }
            finally
            {
                LocalApi.Stop(app);
            }
        }

        /// <summary>kscript openapi: the OpenAPI document for a site's routes, to the output or a file.</summary>
        public static int WriteOpenApi(string siteFolder, ProjectConfig project, bool yaml, string outFile, TextWriter output, TextWriter error)
        {
            HttpApp app;
            try
            {
                app = new HttpApp(siteFolder, project);
            }
            catch (FileNotFoundException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return ExitCodes.Usage;
            }
            if (!app.HasRoutes)
            {
                error.WriteLine($"There are no route files in {app.SiteRoot}, so there is no API to describe. Add api.kscript (or routes.kscript) with import KScript.Http and a Routes {{ }} block.");
                return ExitCodes.Usage;
            }
            try
            {
                Dictionary<string, object> document = OpenApi.Document(app, app.LoadRoutes(), project);
                string text = yaml ? OpenApi.ToYaml(document) : OpenApi.ToJson(document);
                if (outFile == null)
                {
                    output.Write(text);
                    return 0;
                }
                File.WriteAllText(outFile, text);
                output.WriteLine($"Wrote {outFile} ({((Dictionary<string, object>)document["paths"]).Count} paths).");
                return 0;
            }
            catch (KScriptException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return 1;
            }
        }

        /// <summary>
        /// kscript routes [folder]: prints every route, its name and what answers it (-v adds middleware and files).
        /// <paramref name="plain"/> prints the plain table coding agents' docs use instead.
        /// </summary>
        public static int ListRoutes(string siteFolder, ProjectConfig project, TextWriter output, TextWriter error, bool verbose = false, bool plain = false, bool json = false)
        {
            HttpApp app;
            try
            {
                app = new HttpApp(siteFolder, project);
            }
            catch (FileNotFoundException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return ExitCodes.Usage;
            }
            if (!json && !app.HasRoutes)
            {
                new KScript.Output.CliOutput(error).Error($"There are no route files in [{app.SiteRoot}]. Add routes.kscript (or api.kscript) with import KScript.Http and a Routes {{ }} block, or routes.ks.xml starting with <KScript.Routes>.");
                return ExitCodes.Usage;
            }
            try
            {
                if (json)
                {
                    app.LoadStartupConfig();
                    output.WriteLine(Values.ToJson(RouteCatalog.Entries(app, app.Table()), indented: true));
                    return 0;
                }
                if (plain)
                {
                    output.Write(HttpApp.Describe(app.LoadRoutes(), app.SiteRoot));
                    return 0;
                }
                var cli = new KScript.Output.CliOutput(output);
                output.Write(HttpApp.Print(app.LoadRoutes(), app.SiteRoot, cli, verbose).Replace("\n", output.NewLine));
                return 0;
            }
            catch (KScriptException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return 1;
            }
        }
    }
}
