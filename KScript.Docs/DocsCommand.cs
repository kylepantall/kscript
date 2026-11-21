using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using KScript.Pages;

using Microsoft.AspNetCore.Http;

namespace KScript.Docs
{
    /// <summary>
    /// <c>kscript docs</c>: builds the docs site, serves it with kscript serve (its pages are answered by a
    /// KScript.Http route file) on localhost, and opens it in the browser. The "try it" boxes post to /api/run, which
    /// runs the code in the <see cref="Playground"/> sandbox.
    /// </summary>
    public static class DocsCommand
    {
        public const int DefaultPort = 4180;

        /// <summary>The header the docs pages send with every run, so no other web page can run code here.</summary>
        public const string RunHeader = "X-KScript-Docs";

        public static int Run(string[] args, TextWriter output, TextWriter error, CancellationToken stop = default, Action<string> open = null)
        {
            int? port = null;
            bool openBrowser = true;
            string format = null;
            var words = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                    case "-h":
                        output.WriteLine("Usage: kscript docs [search words] [--port 4180] [--format kscript|xml] [--no-open] | kscript docs --project");
                        output.WriteLine("Opens the KScript docs in your browser: guides, the reference for every package, and examples you can run.");
                        output.WriteLine("Examples show in the project's make.format (kscript.json), or .kscript outside a project; --format picks one.");
                        output.WriteLine("kscript docs --project writes .kscript/docs/project.md, the docs of this project's own code, and keeps it up to date from then on.");
                        return 0;
                    case "--port":
                    case "-p":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out int p) || p < 1 || p > 65535)
                        {
                            error.WriteLine("--port needs a number, e.g. --port 4180");
                            return 1;
                        }
                        port = p;
                        break;
                    case "--format":
                    case "-f":
                        format = i + 1 < args.Length ? NormalizeFormat(args[++i]) : null;
                        if (format == null)
                        {
                            error.WriteLine("--format needs kscript or xml, e.g. --format xml");
                            return 1;
                        }
                        break;
                    case "--no-open":
                        openBrowser = false;
                        break;
                    case "--project":
                        return ProjectDocsOutput.Start(KScript.Runtime.WorkingFolder.Current, output, error);
                    default:
                        if (args[i].StartsWith("-", StringComparison.Ordinal) && !args[i].StartsWith("->", StringComparison.Ordinal))
                        {
                            error.WriteLine($"Unknown option '{args[i]}'. Run kscript docs --help for usage.");
                            return 1;
                        }
                        words.Add(args[i]);
                        break;
                }
            }

            int chosen = port ?? FreePort(DefaultPort);
            if (port != null && !IsFree(chosen))
            {
                error.WriteLine($"Port {chosen} is in use. Pick another with --port, or leave it out to use a free one.");
                return 1;
            }

            CleanUpOldSites();
            string folder = Path.Combine(Path.GetTempPath(), "kscript-docs-" + Guid.NewGuid().ToString("N"));
            try
            {
                DocsSite site = DocsSite.Build();
                site.Format = format ?? ProjectFormat(KScript.Runtime.WorkingFolder.Current);
                site.WriteTo(folder);
                string query = words.Count == 0 ? string.Empty : "/?q=" + Uri.EscapeDataString(string.Join(" ", words));
                return KScript.Http.HttpServer.RunAsync(folder, null, chosen, "localhost", null, stop,
                    before: request => HandleApiAsync(request, chosen),
                    started: url =>
                    {
                        output.WriteLine($"KScript docs: {url}{query}");
                        output.WriteLine("Press Ctrl+C to stop.");
                        if (openBrowser) (open ?? OpenBrowser)(url + query);
                    }).GetAwaiter().GetResult();
            }
            catch (IOException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, "Could not start the docs server.", ex);
                return 1;
            }
            finally
            {
                TryDelete(folder);
            }
        }

        /// <summary>
        /// <c>kscript serve --with-docs</c>: the docs, with a page about the project (<see cref="ProjectGuide"/>), on
        /// localhost next to the site until <paramref name="stop"/>. The project page is read again when a script
        /// changes. Returns 1 if the docs couldn't start (the site still runs).
        /// </summary>
        public static async Task<int> ServeProjectAsync(string folder, KScript.Projects.ProjectConfig project, int? port, int sitePort, TextWriter output, TextWriter error, CancellationToken stop)
        {
            int chosen = port ?? (DefaultPort == sitePort ? AnyFreePort() : FreePort(DefaultPort));
            if (port != null && (port == sitePort || !IsFree(chosen)))
            {
                error.WriteLine($"Docs: port {chosen} is in use, so the docs aren't served. Pick another with --docs-port (or serve.docs_port in kscript.json).");
                return 1;
            }

            CleanUpOldSites();
            string site = Path.Combine(Path.GetTempPath(), "kscript-docs-" + Guid.NewGuid().ToString("N"));
            try
            {
                var docs = new ProjectDocs(DocsSite.Build(), folder, project, site);
                return await KScript.Http.HttpServer.RunAsync(site, null, chosen, "localhost", null, stop,
                    before: request =>
                    {
                        docs.Refresh(request.Path.Value);
                        return HandleApiAsync(request, chosen);
                    },
                    started: url => output.WriteLine($"Docs for {docs.Guide.Name}: {url}{DocsSite.ProjectPath} (the KScript docs, with a page about this project)"));
            }
            catch (IOException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, "Could not start the docs server.", ex);
                return 1;
            }
            finally
            {
                TryDelete(site);
            }
        }

        /// <summary>The docs site of <c>kscript serve --with-docs</c>, whose project page follows the project as it changes.</summary>
        internal sealed class ProjectDocs
        {
            private readonly object gate = new object();
            private readonly DocsSite site;
            private readonly string folder;
            private readonly KScript.Projects.ProjectConfig project;
            private readonly string output;

            public ProjectDocs(DocsSite site, string folder, KScript.Projects.ProjectConfig project, string output)
            {
                this.site = site;
                this.folder = folder;
                this.project = project;
                this.output = output;
                Guide = ProjectGuide.Read(folder, project);
                site.Format = Guide.Format;
                site.SetProject(Guide);
                site.WriteTo(output);
            }

            public ProjectGuide Guide { get; private set; }

            /// <summary>Before the project page or the search index is sent: reads the project again if a script changed.</summary>
            public bool Refresh(string path)
            {
                if (path != DocsSite.ProjectPath && path != "/assets/search.json") return false;
                lock (gate)
                {
                    if (ProjectGuide.Changes(folder, project) == Guide.Stamp) return false;
                    Guide = ProjectGuide.Read(folder, project);
                    site.SetProject(Guide);
                    File.WriteAllText(Path.Combine(output, "_pages", DocsSite.PageFile(DocsSite.ProjectPath)), site.Render(site.Find(DocsSite.ProjectPath)));
                    File.WriteAllText(Path.Combine(output, "assets", "search.json"), site.SearchJson());
                    return true;
                }
            }
        }

        /// <summary>"kscript" or "xml" for the names kscript.json and kscript make accept; null for anything else.</summary>
        internal static string NormalizeFormat(string format)
        {
            switch ((format ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "kscript": case ".kscript": case "ks": case "code": return "kscript";
                case "xml": case "ks.xml": case ".ks.xml": return "xml";
                default: return null;
            }
        }

        /// <summary>The make.format of the project a folder is in, or .kscript outside a project (or if kscript.json can't be read).</summary>
        internal static string ProjectFormat(string folder)
        {
            try
            {
                return NormalizeFormat(KScript.Projects.ProjectConfig.FindAndLoad(folder)?.Make.Format) ?? "kscript";
            }
            catch (KScript.Projects.ProjectConfigException)
            {
                return "kscript";
            }
        }

        #region Ports and folders

        internal static bool IsFree(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>The preferred port if nothing is using it, otherwise any free one.</summary>
        internal static int FreePort(int preferred)
        {
            return IsFree(preferred) ? preferred : AnyFreePort();
        }

        internal static int AnyFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>Removes docs sites left behind by a kscript docs that was killed rather than stopped.</summary>
        internal static void CleanUpOldSites()
        {
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(Path.GetTempPath(), "kscript-docs-*"))
                {
                    if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1)) TryDelete(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        /// <summary>Deletes a folder and everything in it; tests replace it to make deleting fail.</summary>
        internal static Action<string> DeleteFolder = folder => Directory.Delete(folder, recursive: true);

        private static void TryDelete(string folder)
        {
            try { if (Directory.Exists(folder)) DeleteFolder(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        /// <summary>Starts a process; tests replace it so no real browser opens.</summary>
        internal static Func<ProcessStartInfo, Process> StartProcess = Process.Start;

        /// <summary>The command that opens a URL in the default browser on this OS.</summary>
        internal static ProcessStartInfo BrowserCommand(string url, bool windows, bool mac) =>
            windows ? new ProcessStartInfo(url) { UseShellExecute = true }
            : new ProcessStartInfo(mac ? "open" : "xdg-open", url) { UseShellExecute = false };

        internal static void OpenBrowser(string url)
        {
            try
            {
                StartProcess(BrowserCommand(url, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()))?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                // No browser to open: the address is printed, so it can be opened by hand.
            }
        }

        #endregion

        #region The run API

        /// <summary>True if a request came from the docs pages themselves: a localhost address, and no other site's origin.</summary>
        internal static bool IsLocal(HttpRequest request, int port)
        {
            string host = request.Host.Host;
            bool local = host == "localhost" || host == "127.0.0.1" || host == "[::1]" || host == "::1";
            if (!local) return false;
            string origin = request.Headers.Origin.ToString();
            if (origin.Length == 0) return true;
            return origin == $"http://localhost:{port}" || origin == $"http://127.0.0.1:{port}" || origin == $"http://[::1]:{port}";
        }

        /// <summary>Answers /api/run and /api/convert; anything else goes on to the pages (null).</summary>
        internal static async Task<PageResult> HandleApiAsync(HttpRequest request, int port)
        {
            string path = request.Path.Value ?? "/";
            if (!path.StartsWith("/api/", StringComparison.Ordinal)) return null;
            if (path != "/api/run" && path != "/api/convert") return Json(404, new JsonObject { ["error"] = "There is no such API." });
            if (!HttpMethods.IsPost(request.Method)) return Json(405, new JsonObject { ["error"] = "POST only." });
            if (!IsLocal(request, port) || request.Headers[RunHeader].ToString() != "1")
                return Json(403, new JsonObject { ["error"] = "Examples can only be run from the docs pages." });

            JsonElement body;
            try
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                char[] buffer = new char[Playground.MaxCode * 2 + 1024];
                int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                if (read == buffer.Length) return Json(413, new JsonObject { ["error"] = "The example is too long to run here." });
                body = JsonDocument.Parse(new string(buffer, 0, read)).RootElement;
            }
            catch (JsonException)
            {
                return Json(400, new JsonObject { ["error"] = "Send JSON: { \"code\": \"...\" }." });
            }

            string code = Field(body, "code");
            if (code == null) return Json(400, new JsonObject { ["error"] = "Send JSON: { \"code\": \"...\" }." });

            if (path == "/api/convert")
            {
                string to = Field(body, "to") == "kscript" ? "kscript" : "xml";
                string converted = Playground.Convert(code, to);
                return converted == null
                    ? Json(422, new JsonObject { ["error"] = $"This code can't be converted to {(to == "kscript" ? ".kscript" : "XML")} until it parses. Run it to see the problem." })
                    : Json(200, new JsonObject { ["code"] = converted });
            }

            PlaygroundResult result = await Task.Run(() => Playground.Run(code, Field(body, "format"), Field(body, "input")));
            return Json(200, new JsonObject { ["output"] = result.Output, ["error"] = result.Error, ["exitCode"] = result.ExitCode, ["ms"] = result.Milliseconds, ["truncated"] = result.Truncated });
        }

        private static string Field(JsonElement body, string name) =>
            body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static PageResult Json(int status, JsonObject value)
        {
            var result = new PageResult { Status = status, ContentType = "application/json; charset=utf-8", Body = value.ToJsonString() };
            result.Headers["Cache-Control"] = "no-store";
            return result;
        }

        #endregion
    }
}
