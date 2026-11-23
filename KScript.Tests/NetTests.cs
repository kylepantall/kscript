using System.Globalization;

using KScript.Net;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class NetTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-net-" + Guid.NewGuid().ToString("N"));
    private readonly TestServer server;

    public NetTests()
    {
        Directory.CreateDirectory(dir);
        server = new TestServer((request, body) => request.Url.AbsolutePath switch
        {
            "/json" => (200, "{ \"name\": \"Ann\", \"tags\": [1, 2] }", "application/json"),
            "/echo" => (201, $"{request.HttpMethod} {request.ContentType} {body} {request.Headers["X-Key"]}", "text/plain"),
            "/missing" => (404, "nope", "text/plain"),
            "/slow" => Slow(),
            "/file.txt" => (200, "file body", "text/plain"),
            _ => (200, "hello", null),
        });
    }

    private static (int, string, string) Slow()
    {
        Thread.Sleep(3000);
        return (200, "late", null);
    }

    public void Dispose()
    {
        server.Dispose();
        Directory.Delete(dir, recursive: true);
    }

    private string Run(string body, Action<KScriptHost> setup = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        setup?.Invoke(host);
        new Interpreter(host, options: new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) })
            .RunText("<kscript><import package=\"KScript.Net\" />" + body + "</kscript>", Path.Combine(dir, "net.ks.xml"));
        return output.ToString();
    }

    private KScriptException Fails(string body, Action<KScriptHost> setup = null) => Assert.Throws<KScriptException>(() => Run(body, setup));

    [Fact]
    public void Api_reads_text_json_status_and_headers()
    {
        string output = Run(
            $"<let name=\"r\" value=\"@api('{server.Url}/json')\" />" +
            "<echo>{ $r.status } { $r.is_ok } { $r.reason } { $r->json().name } { $r->json().tags[1] } { $r->header('content-type') } { $r->header('X-None') ?? 'none' }</echo>" +
            "<echo>{ $r.status } { $r.is_ok } { $r.text->count() } { $r.headers['Content-Type'] } { $r.url == $r.url } { $r.text == $r.text } { $r.headers->count() > 0 } { $r.nothing ?? 'n' }</echo>" +
            "<echo>{ $r->check().status } {$r}</echo>");
        Assert.Equal("200 true OK Ann 2 application/json none\n200 true 33 application/json true true true n\n200 { \"name\": \"Ann\", \"tags\": [1, 2] }\n", output);
    }

    [Fact]
    public void Api_sends_methods_headers_and_bodies()
    {
        string output = Run(
            $"<echo>{{ @api('{server.Url}/echo', {{ method: 'post', body: {{ a: 1 }}, headers: {{ 'X-Key': 'k' }} }}).text }}</echo>" +
            $"<echo>{{ @api('{server.Url}/echo', {{ method: 'PUT', body: 'plain', headers: {{ 'user-agent': 'example-app/1.0' }} }}).text }}</echo>" +
            $"<echo>{{ @api('{server.Url}/echo', {{ method: 'PATCH', body: 'a=1', headers: {{ 'Content-Type': 'application/x-www-form-urlencoded' }} }}).text }}</echo>" +
            $"<echo>{{ @api('{server.Url}/echo', null).status }}</echo>");
        Assert.Equal("POST application/json {\"a\":1} k\nPUT text/plain; charset=utf-8 plain\nPATCH application/x-www-form-urlencoded a=1\n201\n", output);
        Assert.Equal(new[] { "kscript", "example-app/1.0", "kscript", "kscript" }, server.Requests.Select(r => r.Headers["User-Agent"]));
    }

    [Fact]
    public void Http_log_provider_sends_the_default_user_agent()
    {
        Run($"<log_provider type=\"http\" url=\"{server.Url}/logs\" batch=\"1\" replace=\"yes\" /><log>ready</log>");
        Assert.Equal("kscript", Assert.Single(server.Requests).Headers["User-Agent"]);
    }

    [Fact]
    public void Api_errors_are_typed()
    {
        Assert.Contains("needs an http:// or https:// address", Fails("<do>@api('ftp://x')</do>").Message);
        Assert.Contains("needs an http:// or https:// address", Fails("<do>@api('not a url')</do>").Message);
        var missing = Fails($"<do>@api('{server.Url}/missing')->check()</do>");
        Assert.Equal("Network", missing.Type);
        Assert.Contains("answered 404 Not Found", missing.Message);
        Assert.Equal(404.0, missing.Fields["status"]);
        Assert.Contains("isn't JSON", Fails($"<do>@api('{server.Url}/missing')->json()</do>").Message);
        var timeout = Fails($"<do>@api('{server.Url}/slow', options: {{ timeout: 200ms }})</do>");
        Assert.Equal("Timeout", timeout.Type);
        Assert.Contains("didn't answer within 0.2 seconds", timeout.Message);
        Assert.Equal("Network", Fails($"<do>@api('http://localhost:{TestServer.FreePort()}/')</do>").Type);

        // A server that accepts the connection and hangs up without answering.
        var tcp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((System.Net.IPEndPoint)tcp.LocalEndpoint).Port;
        var hangUp = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var client = await tcp.AcceptTcpClientAsync();
                    await client.GetStream().ReadAsync(new byte[4096]);
                }
                catch (Exception) { return; }
            }
        });
        try
        {
            Assert.Equal("Network", Fails($"<do>@api('http://127.0.0.1:{port}/')</do>").Type);
        }
        finally
        {
            tcp.Stop();
        }
    }

    [Fact]
    public void Url_helpers()
    {
        Assert.Equal("a%20b%26c a b&c a=1&b=two%20words\n", Run("<echo>{ 'a b&amp;c'->url_encode() } { 'a%20b%26c'->url_decode() } { @query_string({ a: 1, b: 'two words', c: null }) }</echo>"));
    }

    [Fact]
    public void Download_saves_files_and_respects_overwrite_and_the_sandbox()
    {
        Run($"<do>@download('{server.Url}/file.txt', 'out/a.txt')</do>");
        Assert.Equal("file body", File.ReadAllText(Path.Combine(dir, "out", "a.txt")));
        Assert.Equal("kscript", Assert.Single(server.Requests).Headers["User-Agent"]);
        File.WriteAllText(Path.Combine(dir, "keep.txt"), "mine");
        Run($"<do>@download('{server.Url}/file.txt', 'keep.txt', overwrite: false)</do>");
        Assert.Equal("mine", File.ReadAllText(Path.Combine(dir, "keep.txt")));
        Run($"<do>@download('{server.Url}/file.txt', 'keep.txt')</do>");
        Assert.Equal("file body", File.ReadAllText(Path.Combine(dir, "keep.txt")));

        Assert.Contains("answered 404", Fails($"<do>@download('{server.Url}/missing', 'x.txt')</do>").Message);
        Assert.Contains("isn't an http:// or https:// address", Fails("<do>@download('file:///etc/passwd', 'x.txt')</do>").Message);
        Assert.Contains("can't change files", Fails($"<do>@download('{server.Url}/file.txt', 'y.txt')</do>", h => h.CanWriteFiles = false).Message);
        Assert.Contains("is outside", Fails($"<do>@download('{server.Url}/file.txt', '../y.txt')</do>", h => h.FileRoot = dir).Message);
    }

    [Fact]
    public void Response_values_work_without_a_server()
    {
        var r = new ResponseValue("u", 500, "Oops", "body", new Dictionary<string, object>());
        Assert.False(r.Ok);
        Assert.Equal("response", r.TypeName);
        Assert.False(r.TryCallMethod(null, "nope", out _));
        Assert.Equal("body", r.ToText());
    }
}
