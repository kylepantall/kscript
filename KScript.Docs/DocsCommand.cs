using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

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
            var words = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                    case "-h":
                        output.WriteLine("Usage: kscript docs [search words] [--port 4180] [--no-open]");
                        output.WriteLine("Opens the KScript docs in your browser: guides, the reference for every package, and examples you can run.");
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
                    case "--no-open":
                        openBrowser = false;
                        break;
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
                DocsSite.Build().WriteTo(folder);
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
                error.WriteLine($"Could not start the docs server: {ex.Message}");
                return 1;
            }
            finally
            {
                TryDelete(folder);
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
            if (IsFree(preferred)) return preferred;
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
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void TryDelete(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
            if (path != "/api/run" && path != "/api/convert") return Json(404, new { error = "There is no such API." });
            if (!HttpMethods.IsPost(request.Method)) return Json(405, new { error = "POST only." });
            if (!IsLocal(request, port) || request.Headers[RunHeader].ToString() != "1")
                return Json(403, new { error = "Examples can only be run from the docs pages." });

            JsonElement body;
            try
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                char[] buffer = new char[Playground.MaxCode * 2 + 1024];
                int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                if (read == buffer.Length) return Json(413, new { error = "The example is too long to run here." });
                body = JsonDocument.Parse(new string(buffer, 0, read)).RootElement;
            }
            catch (JsonException)
            {
                return Json(400, new { error = "Send JSON: { \"code\": \"...\" }." });
            }

            string code = Field(body, "code");
            if (code == null) return Json(400, new { error = "Send JSON: { \"code\": \"...\" }." });

            if (path == "/api/convert")
            {
                string to = Field(body, "to") == "kscript" ? "kscript" : "xml";
                string converted = Playground.Convert(code, to);
                return converted == null
                    ? Json(422, new { error = $"This code can't be converted to {(to == "kscript" ? ".kscript" : "XML")} until it parses. Run it to see the problem." })
                    : Json(200, new { code = converted });
            }

            PlaygroundResult result = await Task.Run(() => Playground.Run(code, Field(body, "format"), Field(body, "input")));
            return Json(200, new { output = result.Output, error = result.Error, exitCode = result.ExitCode, ms = result.Milliseconds, truncated = result.Truncated });
        }

        private static string Field(JsonElement body, string name) =>
            body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static PageResult Json(int status, object value)
        {
            var result = new PageResult { Status = status, ContentType = "application/json; charset=utf-8", Body = JsonSerializer.Serialize(value) };
            result.Headers["Cache-Control"] = "no-store";
            return result;
        }

        #endregion
    }
}
