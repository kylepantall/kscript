using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using Microsoft.AspNetCore.Http;

namespace KScript.Tests;

public sealed class LiveTransportTests
{
    [Fact]
    public async Task Live_request_body_limits_apply_before_decoding_or_reading_forms()
    {
        using var site = new TempSite(("routes.kscript", "import KScript.Http\nRoutes {\n Get(path: '/edit') { return 'ok' }\n}"));
        var app = new HttpApp(site.Root);
        var request = new DefaultHttpContext().Request;
        request.Method = "POST";
        request.Path = "/edit";
        request.Headers["X-KScript-Live"] = "stale";
        request.Body = new MemoryStream(new byte[PageServer.MaxLiveBodyBytes * 2]);
        request.ContentType = "application/x-www-form-urlencoded";
        Assert.Equal(415, (await app.HandleAsync(request)).Status);
        Assert.Equal(0, request.Body.Position);
        request.ContentType = "application/json";
        Assert.Null(request.ContentLength);
        Assert.Equal(413, (await app.HandleAsync(request)).Status);
        Assert.True(request.Body.Position < request.Body.Length);
    }

    [Fact]
    public async Task Served_file_pages_use_the_same_signed_session_as_controller_views()
    {
        using var site = new TempSite(
            ("kscript.json", """{"http":{"auth":{"folder":"_private"}}}"""),
            ("counter.html", """<live id="counter"><state name="n" value="0"/><b>{$n}</b><button on-click="n += 1">Add</button></live>"""),
            ("routes.kscript", "import KScript.Http\nRoutes {\n Get(path: '/secure', middleware: [auth:session]) { return 'private' }\n}"),
            ("secure/index.html", "<p>Controller only</p>"));
        using var stop = new CancellationTokenSource();
        var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var config = ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        var server = HttpServer.RunAsync(site.Root, config, TestServer.FreePort(), "127.0.0.1", null, stop.Token, started: url => listening.SetResult(url));
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(await listening.Task.WaitAsync(TimeSpan.FromSeconds(10))) };
            using var page = await http.GetAsync("/counter?x=1");
            string html = await page.Content.ReadAsStringAsync();
            string Attribute(string name) => WebUtility.HtmlDecode(Regex.Match(html, name + "=\"([^\"]+)\"").Groups[1].Value);
            string proof = Attribute("data-ks-route");
            Assert.NotEmpty(proof);
            Assert.True(page.Headers.CacheControl?.NoStore);
            var click = new { block = "counter", handler = 0, state = Attribute("data-ks-state"), args = Attribute("data-ks-args-0") };
            http.DefaultRequestHeaders.Add("X-KScript-Live", proof);
            using var saved = await http.PostAsJsonAsync("/counter?x=1", click);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Contains("<b>1</b>", await saved.Content.ReadAsStringAsync());
            Assert.True(saved.Headers.Contains("X-KScript-Fragment"));
            using var changed = await http.PostAsJsonAsync("/counter?x=2", click);
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            using var other = new HttpClient { BaseAddress = http.BaseAddress };
            other.DefaultRequestHeaders.Add("X-KScript-Live", proof);
            using var foreign = await other.PostAsJsonAsync("/counter?x=1", click);
            Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
            using var legacy = await http.PostAsJsonAsync("/_ks/live", new { page = "/secure", block = "counter" });
            Assert.Equal(HttpStatusCode.Conflict, legacy.StatusCode);
            using var alias = await http.GetAsync("/secure/index.html");
            Assert.Equal(HttpStatusCode.NotFound, alias.StatusCode);
        }
        finally
        {
            stop.Cancel();
            await server;
        }
    }
}
