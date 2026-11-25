using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace KScript.Pages
{
    internal sealed class BrowserReload : IDisposable
    {
        internal const string EventsPath = "/_kscript/reload";
        internal const string ScriptPath = EventsPath + ".js";
        private static readonly Lazy<string> Script = new(() =>
        {
            using var stream = typeof(BrowserReload).Assembly.GetManifestResourceStream("KScript.Pages.browser-reload.js");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });

        private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".kscript", ".xml", ".html", ".htm", ".md", ".css", ".js", ".mjs", ".json", ".yaml", ".yml",
            ".svg", ".png", ".gif", ".jpeg", ".jpg", ".ico", ".webp", ".avif", ".woff", ".woff2", ".ttf",
        };
        private static readonly HashSet<string> GeneratedFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules",
        };
        private readonly string root;
        private readonly FileSystemWatcher watcher;
        private readonly Timer debounce;
        private readonly object gate = new();
        private readonly string instance = Guid.NewGuid().ToString("N");
        private long revision;
        private TaskCompletionSource change = NewChange();
        private bool disposed;

        internal BrowserReload(string root)
        {
            this.root = Path.GetFullPath(root);
            debounce = new Timer(_ => Advance());
            watcher = new FileSystemWatcher(this.root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Changed += (_, e) => OnFile(e.FullPath);
            watcher.Created += (_, e) => OnFile(e.FullPath);
            watcher.Deleted += (_, e) => OnFile(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                OnFile(e.OldFullPath);
                OnFile(e.FullPath);
            };
            watcher.Error += (_, _) => Schedule();
            watcher.EnableRaisingEvents = true;
        }

        private void OnFile(string path)
        {
            var segments = Path.GetRelativePath(root, path).Replace('\\', '/').Split('/');
            foreach (string segment in segments)
                if (segment.StartsWith('.') || GeneratedFolders.Contains(segment)) return;
            string name = segments[^1];
            if (name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase) || name.Equals("CLAUDE.md", StringComparison.OrdinalIgnoreCase)) return;
            if (SourceExtensions.Contains(Path.GetExtension(name))) Schedule();
        }

        private void Schedule()
        {
            lock (gate)
                if (!disposed) debounce.Change(150, Timeout.Infinite);
        }

        private static TaskCompletionSource NewChange() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void Advance()
        {
            lock (gate)
            {
                if (disposed) return;
                revision++;
                change.TrySetResult();
                change = NewChange();
            }
        }

        private (string Revision, Task Change) Snapshot()
        {
            lock (gate) return ($"{instance}-{revision}", change.Task);
        }

        internal string Revision => Snapshot().Revision;

        internal async Task<bool> TryHandleAsync(HttpContext http, CancellationToken stopping)
        {
            if (!HttpMethods.IsGet(http.Request.Method)) return false;
            if (http.Request.Path == ScriptPath)
            {
                http.Response.ContentType = "text/javascript; charset=utf-8";
                http.Response.Headers.CacheControl = "no-store";
                await http.Response.WriteAsync(Script.Value, http.RequestAborted);
                return true;
            }
            if (http.Request.Path != EventsPath) return false;

            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(stopping, http.RequestAborted);
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var (current, next) = Snapshot();
                    await http.Response.WriteAsync($"data: {current}\n\n", stop.Token);
                    await http.Response.Body.FlushAsync(stop.Token);
                    try
                    {
                        await next.WaitAsync(TimeSpan.FromSeconds(15), stop.Token);
                    }
                    catch (TimeoutException)
                    {
                        // Sending the current revision again keeps idle connections open through proxies.
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            return true;
        }

        internal static void Inject(PageResult result, HttpRequest request, string revision)
        {
            if (!HttpMethods.IsGet(request.Method) || result.FilePath != null || result.BodyBytes != null || result.Body.Length == 0 ||
                result.Status < 200 || result.Status is >= 300 and < 400 || result.Status is 204 or 205 ||
                !string.Equals(result.ContentType?.Split(';', 2)[0].Trim(), "text/html", StringComparison.OrdinalIgnoreCase) ||
                result.Headers.ContainsKey("Content-Encoding") || result.Headers.ContainsKey("Content-Disposition"))
                return;

            string script = $"<script src=\"{ScriptPath}\" data-revision=\"{revision}\" defer></script>";
            int beforeBodyEnd = result.Body.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            result.Body = beforeBodyEnd < 0 ? result.Body + script : result.Body.Insert(beforeBodyEnd, script);
            result.Headers.Remove("Content-Length");
            result.Headers.Remove("ETag");
            result.Headers["Cache-Control"] = "no-store";
        }

        public void Dispose()
        {
            lock (gate) disposed = true;
            watcher.Dispose();
            debounce.Dispose();
        }
    }
}
