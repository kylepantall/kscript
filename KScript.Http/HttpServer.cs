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
        /// null to let them); <paramref name="started"/> gets the address once the server is listening.
        /// </summary>
        public static Task<int> RunAsync(string siteFolder, ProjectConfig project, int port, string host, TextWriter log, CancellationToken stop = default,
            Func<Microsoft.AspNetCore.Http.HttpRequest, Task<PageResult>> before = null, Action<string> started = null)
        {
            var site = new PageSite(siteFolder);
            var app = new HttpApp(site.Root, project, log);
            if (app.ViewsFolder is string views) site.HiddenFolders.Add(views.Split('/', '\\')[0]);
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
                    log?.WriteLine($"Route error: {(ex.Location != null ? ex.Location + ": " : string.Empty)}{ex.Message}");
                }
            }
            Func<Microsoft.AspNetCore.Http.HttpRequest, Task<PageResult>> routes = app.HasRoutes ? app.HandleAsync : null;
            Func<Microsoft.AspNetCore.Http.HttpRequest, Task<PageResult>> handler = before == null ? routes : async request =>
                await before(request) ?? (routes == null ? null : await routes(request));
            LocalApi.Install();
            LocalApi.Serve(app);
            return RunAndForget(app, PageServer.RunAsync(site, port, host, log, handler, stop, onStarted: started));
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
                error.WriteLine(ex.Message);
                return 1;
            }
            if (!app.HasRoutes)
            {
                error.WriteLine($"There are no route files in {app.SiteRoot}, so there is no API to describe. Add api.kscript (or routes.kscript) starting with KScript.Routes {{ }}.");
                return 1;
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
                error.WriteLine((ex.Location != null ? ex.Location + ": " : string.Empty) + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// kscript routes [folder]: prints every route, its name and what answers it (-v adds middleware and files).
        /// <paramref name="plain"/> prints the plain table coding agents' docs use instead.
        /// </summary>
        public static int ListRoutes(string siteFolder, ProjectConfig project, TextWriter output, TextWriter error, bool verbose = false, bool plain = false)
        {
            HttpApp app;
            try
            {
                app = new HttpApp(siteFolder, project);
            }
            catch (FileNotFoundException ex)
            {
                error.WriteLine(ex.Message);
                return 1;
            }
            if (!app.HasRoutes)
            {
                new KScript.Output.CliOutput(error).Error($"There are no route files in [{app.SiteRoot}]. Add routes.ks.xml (or routes.kscript, api.ks.xml) starting with <KScript.Routes>.");
                return 1;
            }
            try
            {
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
                error.WriteLine((ex.Location != null ? ex.Location + ": " : string.Empty) + ex.Message);
                return 1;
            }
        }
    }
}
