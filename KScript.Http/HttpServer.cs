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
        public static Task<int> RunAsync(string siteFolder, ProjectConfig project, int port, string host, TextWriter log)
        {
            var site = new PageSite(siteFolder);
            var app = new HttpApp(site.Root, project, log);
            if (app.ViewsFolder is string views) site.HiddenFolders.Add(views.Split('/', '\\')[0]);
            if (app.HasRoutes)
            {
                try
                {
                    RouteTable table = app.Table();
                    log?.WriteLine($"Routes: {table.Routes.Count} from {string.Join(", ", app.Files.Select(f => Path.GetFileName(f.Path)))} (kscript routes lists them).");
                }
                catch (KScriptException ex)
                {
                    log?.WriteLine($"Route error: {(ex.Location != null ? ex.Location + ": " : string.Empty)}{ex.Message}");
                }
            }
            return PageServer.RunAsync(site, port, host, log, app.HasRoutes ? app.HandleAsync : null);
        }

        /// <summary>kscript routes [folder]: prints every route, its name, what answers it and its middleware.</summary>
        public static int ListRoutes(string siteFolder, ProjectConfig project, TextWriter output, TextWriter error)
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
                error.WriteLine($"There are no route files in {app.SiteRoot}. Add routes.ks.xml (or routes.kscript, api.ks.xml) starting with <KScript.Routes>.");
                return 1;
            }
            try
            {
                output.Write(HttpApp.Describe(app.LoadRoutes(), app.SiteRoot));
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
