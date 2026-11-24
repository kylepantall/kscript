using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using KScript.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace KScript.Pages
{
    /// <summary>The incremental response of one negotiated, signed live action.</summary>
    internal sealed class LiveActionStream
    {
        private static readonly object HttpKey = new();
        private static readonly ConditionalWeakTable<Dictionary<string, object>, LiveActionStream> Requests = new();
        private readonly HttpContext http;

        private LiveActionStream(HttpContext http) => this.http = http;

        internal bool Started { get; private set; }

        internal static LiveActionStream Negotiate(HttpContext http)
        {
            if (!PageServer.IsLiveRequest(http.Request) || http.Request.Headers["X-KScript-Stream"] != "1")
                return null;
            var stream = new LiveActionStream(http);
            http.Items[HttpKey] = stream;
            return stream;
        }

        internal static void Bind(HttpRequest http, Dictionary<string, object> request)
        {
            if (http.HttpContext.Items.TryGetValue(HttpKey, out object value) && value is LiveActionStream stream)
                Requests.Add(request, stream);
        }

        internal static CancellationToken Cancellation(Dictionary<string, object> request) =>
            Requests.TryGetValue(request, out var stream) ? stream.http.RequestAborted : default;

        // Page handlers are synchronous. Waiting for each flush bounds the amount of output in flight
        // and lets request cancellation stop a handler at its next stream write.
        internal static void Emit(Dictionary<string, object> request, string target, string text, string mode, bool html = false)
        {
            if (!Requests.TryGetValue(request, out var stream)) return;
            stream.RenderAsync(request, target, text, mode, html).GetAwaiter().GetResult();
        }

        private async Task RenderAsync(Dictionary<string, object> request, string target, string text, string mode, bool html)
        {
            http.RequestAborted.ThrowIfCancellationRequested();
            if (!Started)
            {
                RequestHooks.CommitHeaders(request);
                http.Response.StatusCode = 200;
                http.Response.ContentType = "application/x-ndjson; charset=utf-8";
                http.Response.Headers.Server = HttpIdentity.Product;
                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers["X-Accel-Buffering"] = "no";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
                Started = true;
            }
            await WriteFrameAsync(new Dictionary<string, object>
            {
                ["type"] = "stream", ["target"] = target, ["text"] = text, ["mode"] = mode, ["html"] = html,
            });
        }

        internal Task FinishAsync(PageResult result)
        {
            // A custom response may try to return a cookie directly, outside KScript.Auth.
            if (result.Cookies.Count != 0 || result.Headers.ContainsKey("Set-Cookie"))
                return WriteFrameAsync(new Dictionary<string, object>
                {
                    ["type"] = "final", ["status"] = 500d, ["fragment"] = false,
                    ["html"] = "A streamed action cannot change a response cookie.",
                });
            return WriteFrameAsync(new Dictionary<string, object>
            {
                ["type"] = "final",
                ["status"] = (double)result.Status,
                ["html"] = result.Body,
                ["fragment"] = result.Headers.GetValueOrDefault("X-KScript-Fragment") == "1",
                ["location"] = result.Headers.GetValueOrDefault("Location"),
            });
        }

        private async Task WriteFrameAsync(Dictionary<string, object> frame)
        {
            await http.Response.WriteAsync(Values.ToJson(frame) + "\n", Encoding.UTF8, http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
        }
    }
}
