using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using Microsoft.AspNetCore.Http;

namespace KScript.Tests;

public sealed class PasskeyEndpointTests
{
    [Theory]
    [InlineData("http://accounts.example.com", null)]
    [InlineData("https://accounts.example.com/account", null)]
    [InlineData("https://name@accounts.example.com", null)]
    [InlineData("https://accounts.example.com", "other.example.com")]
    [InlineData("https://example.com", "com")]
    public void Invalid_settings_disable_passkeys(string origin, string domain)
    {
        using var site = new TempSite();
        var settings = new System.Text.Json.Nodes.JsonObject { ["passkey_origin"] = origin };
        if (domain != null) settings["passkey_rp_id"] = domain;
        string json = new System.Text.Json.Nodes.JsonObject
        {
            ["http"] = new System.Text.Json.Nodes.JsonObject { ["auth"] = settings },
        }.ToJsonString();
        ProjectConfig project = ProjectConfig.Parse(json, Path.Combine(site.Root, "kscript.json"));
        Assert.NotEmpty(project.Warnings);
        Assert.Null(project.Http.Auth.PasskeyOrigin);
    }

    [Fact]
    public async Task Serving_a_project_keeps_auth_storage_and_the_default_database_private()
    {
        using var site = new TempSite(
            ("kscript.json", """{"http":{"auth":{"folder":"storage/private"}}}"""),
            ("storage/private/sessions/session.json", "{}"), ("storage/private/users/user.json", "{}"),
            ("storage/private/_passkeys/passkeys.sqlite", "database"), ("storage/public.txt", "asset"),
            ("data/app.sqlite", "database"), ("data/app.sqlite-wal", "journal"));
        var project = ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        using var stop = new CancellationTokenSource();
        var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = HttpServer.RunAsync(site.Root, project, TestServer.FreePort(), "127.0.0.1", null, stop.Token, started: url => listening.SetResult(url));
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(await listening.Task.WaitAsync(TimeSpan.FromSeconds(10))) };
            foreach (string path in new[] { "storage/private/sessions/session.json", "storage/private/users/user.json",
                         "storage/private/_passkeys/passkeys.sqlite", "data/app.sqlite", "data/app.sqlite-wal" })
            {
                using var response = await http.GetAsync(path);
                Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            }
            Assert.Equal("asset", await http.GetStringAsync("storage/public.txt"));
        }
        finally
        {
            stop.Cancel();
            await server;
        }
    }

    [Theory]
    [InlineData("GET", "https://accounts.example.com", "application/json", "login/options", 405)]
    [InlineData("POST", null, "application/json", "login/options", 403)]
    [InlineData("POST", "https://other.example.com", "application/json", "login/options", 403)]
    [InlineData("POST", "https://accounts.example.com", "text/plain", "login/options", 415)]
    [InlineData("POST", "https://accounts.example.com", "application/json", "other/options", 404)]
    [InlineData("POST", "https://accounts.example.com", "application/json", "register/options", 401)]
    [InlineData("POST", "https://accounts.example.com", "application/json", "login/options", 200)]
    public async Task Built_in_routes_check_the_request_even_without_route_files(string method, string origin, string type, string path, int status)
    {
        using var site = new TempSite(("kscript.json", """
            {"http":{"auth":{"folder":"_private","passkey_origin":"https://accounts.example.com","passkey_rp_id":"example.com"}}}
            """));
        var project = ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        Assert.Empty(project.Warnings);
        var app = new HttpApp(site.Root, project);
        var request = new DefaultHttpContext().Request;
        request.Method = method;
        request.Path = "/_ks/passkeys/" + path;
        request.Headers.Host = "untrusted.example";
        if (origin != null) request.Headers.Origin = origin;
        request.ContentType = type;
        request.Body = new MemoryStream("{}"u8.ToArray());
        request.ContentLength = 2;
        PageResult response = await app.HandleAsync(request);
        Assert.Equal(status, response.Status);
        Assert.Equal("no-store", response.Headers["Cache-Control"]);
        Assert.StartsWith("application/json", response.ContentType);
        if (status == 200)
        {
            Assert.Contains("challenge", response.Body);
            Assert.Contains("example.com", response.Body);
            Assert.Single(response.Cookies);
        }
    }
}
