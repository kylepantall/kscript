using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using KScript.Http;
using KScript.Projects;

namespace KScript.Tests;

public sealed class LiveStreamingTests
{
    [Fact]
    public async Task A_signed_action_flushes_chunks_before_its_route_finishes()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gate = new TestServer((_, _) =>
        {
            entered.TrySetResult();
            return release.Wait(TimeSpan.FromSeconds(10))
                ? (200, "done", "text/plain") : (504, "gate was not released", "text/plain");
        });

        await ServeAsync($$"""
            <import package="KScript.Net" />
            <live id="job">
              <state name="finished" value="''"/>
              <p live-stream="progress">{$finished}</p>
              <button on-click="@stream('progress', text: 'first'); finished = @api('{{gate.Url}}', options: { timeout: 15s })->check().text; @stream('progress', text: '<em>last</em>', mode: 'replace', html: true)">Run</button>
              <button on-click="finished = 'ordinary'">Ordinary</button>
            </live>
            """, async (http, token) =>
        {
            try
            {
                string initial = await http.GetStringAsync("/stream", token);
                using var request = Action(initial);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType.MediaType);
                Assert.True(response.Headers.CacheControl?.NoStore);

                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
                JsonNode first = await Frame(reader, token);
                Assert.Equal("stream", first["type"].GetValue<string>());
                Assert.Equal("progress", first["target"].GetValue<string>());
                Assert.Equal("first", first["text"].GetValue<string>());
                Assert.False(first["html"].GetValue<bool>());
                await entered.Task.WaitAsync(token);

                release.Set();
                JsonNode second = await Frame(reader, token);
                Assert.Equal("stream", second["type"].GetValue<string>());
                Assert.Equal("<em>last</em>", second["text"].GetValue<string>());
                Assert.Equal("replace", second["mode"].GetValue<string>());
                Assert.True(second["html"].GetValue<bool>());
                JsonNode final = await Frame(reader, token);
                Assert.Equal("final", final["type"].GetValue<string>());
                Assert.Equal(200, final["status"].GetValue<int>());
                Assert.True(final["fragment"].GetValue<bool>());
                Assert.Contains(">done</p>", final["html"].GetValue<string>());
                Assert.Null(await reader.ReadLineAsync(token));

                using var ordinaryRequest = Action(initial, handler: 1);
                using var ordinary = await http.SendAsync(ordinaryRequest, token);
                Assert.Equal("text/html", ordinary.Content.Headers.ContentType.MediaType);
                Assert.Contains(">ordinary</p>", await ordinary.Content.ReadAsStringAsync(token));
            }
            finally { release.Set(); }
        });
    }

    [Fact]
    public async Task Invalid_proofs_and_hidden_targets_cannot_start_streaming()
    {
        await ServeAsync("""
            <live id="job">
              <if false><p live-stream="hidden"></p></if>
              <button on-click="@stream('hidden', text: 'secret')">Run</button>
            </live>
            """, async (http, token) =>
        {
            string initial = await http.GetStringAsync("/stream", token);
            using var tampered = Action(initial, arguments: "bad");
            using var unauthorized = await http.SendAsync(tampered, token);
            Assert.Equal(HttpStatusCode.Conflict, unauthorized.StatusCode);
            Assert.NotEqual("application/x-ndjson", unauthorized.Content.Headers.ContentType?.MediaType);

            using var hidden = Action(initial);
            using var rejected = await http.SendAsync(hidden, token);
            Assert.False(rejected.IsSuccessStatusCode);
            Assert.NotEqual("application/x-ndjson", rejected.Content.Headers.ContentType?.MediaType);
        });
    }

    [Fact]
    public async Task A_session_cookie_cannot_change_after_a_stream_starts()
    {
        await ServeAsync("""
            <import package="KScript.Auth" />
            <live id="job">
              <p live-stream="progress"></p>
              <button on-click="@stream('progress', text: 'starting'); @logout()">Run</button>
              <button on-click="@logout(); @stream('progress', text: 'too late')">Log out first</button>
            </live>
            """, async (http, token) =>
        {
            string initial = await http.GetStringAsync("/stream", token);
            using var request = Action(initial);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType.MediaType);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
            Assert.Equal("starting", (await Frame(reader, token))["text"].GetValue<string>());
            var final = await Frame(reader, token);
            Assert.Equal("final", final["type"].GetValue<string>());
            Assert.InRange(final["status"].GetValue<int>(), 400, 599);
            Assert.False(response.Headers.Contains("Set-Cookie"));

            string stillSignedIn = await http.GetStringAsync("/stream", token);
            Assert.Contains("data-ks-live", stillSignedIn);
            using var logoutFirst = Action(stillSignedIn, handler: 1);
            using var rejected = await http.SendAsync(logoutFirst, token);
            Assert.False(rejected.IsSuccessStatusCode);
            Assert.NotEqual("application/x-ndjson", rejected.Content.Headers.ContentType?.MediaType);
            Assert.True(rejected.Headers.Contains("Set-Cookie"));
        });
    }

    private static async Task<JsonNode> Frame(StreamReader reader, CancellationToken token) =>
        JsonNode.Parse(Assert.IsType<string>(await reader.ReadLineAsync(token)));

    private static HttpRequestMessage Action(string rendered, int handler = 0, string arguments = null)
    {
        string Attribute(string name) =>
            WebUtility.HtmlDecode(Regex.Match(rendered, Regex.Escape(name) + "=\"([^\"]+)\"").Groups[1].Value);
        var request = new HttpRequestMessage(HttpMethod.Post, "/stream");
        request.Headers.Add("X-KScript-Live", Attribute("data-ks-route"));
        request.Headers.Add("X-KScript-Stream", "1");
        request.Content = JsonContent.Create(new
        {
            block = "job", handler, state = Attribute("data-ks-state"),
            args = arguments ?? Attribute($"data-ks-args-{handler}"),
        });
        return request;
    }

    private static async Task ServeAsync(string view, Func<HttpClient, CancellationToken, Task> test)
    {
        using var site = new TempSite(
            ("kscript.json", """{"http":{"auth":{"folder":"_private"}}}"""),
            ("routes.kscript", """
                import KScript.Http
                import KScript.Auth
                Routes {
                  Get(path: '/start') {
                    @login({ id: 'stream-user' })
                    return 'ok'
                  }
                  Get(path: '/stream', view: 'stream', middleware: [auth:session])
                }
                """),
            ("views/stream.html", view));
        using var stop = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var config = ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        var server = HttpServer.RunAsync(site.Root, config, TestServer.FreePort(), "127.0.0.1", null, stop.Token,
            started: url => listening.SetResult(url));
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(await listening.Task.WaitAsync(timeout.Token)),
            };
            using var start = await http.GetAsync("/start", timeout.Token);
            start.EnsureSuccessStatusCode();
            await test(http, timeout.Token);
        }
        finally
        {
            stop.Cancel();
            await server;
        }
    }
}
