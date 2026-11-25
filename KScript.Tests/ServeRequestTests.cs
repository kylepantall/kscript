using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;
using Microsoft.AspNetCore.Http;

namespace KScript.Tests;

/// <summary>
/// What <c>kscript serve</c> answers when a request or a response is out of the ordinary: a status or header that can't
/// be sent, a body that can't be read, a name sent twice, an error in production, a live block on a page with no GET.
/// </summary>
public sealed class ServeRequestTests
{
    /// <summary>Serves the site on a free port and gives the test a client for it (which follows no redirects).</summary>
    private static async Task Serve(TempSite site, Func<HttpClient, Task> test, bool production = false, StringWriter log = null, ProjectConfig project = null)
    {
        using var stop = new CancellationTokenSource();
        var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = HttpServer.RunAsync(site.Root, project, TestServer.FreePort(), "127.0.0.1", log, stop.Token,
            started: url => listening.SetResult(url), production: production);
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { BaseAddress = new Uri(await listening.Task.WaitAsync(TimeSpan.FromSeconds(10))) };
            await test(http);
        }
        finally
        {
            stop.Cancel();
            await server;
        }
    }

    private static string Attribute(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, name + "=\"([^\"]+)\"").Groups[1].Value);

    private const string Counter = """<live id="counter"><state name="n" value="0"/><b>{$n}</b><button on-click="n += 1">Add</button></live>""";

    #region An answer's own status

    [Fact]
    public void Wrapping_an_answer_keeps_its_status_unless_one_is_given()
    {
        using var site = new TempSite(("routes.kscript", """
            import KScript.Http
            Routes {
              Get(path: /wrapped) { return @response(@response('gone', status: 404)) }
              Get(path: /moved) { return @response(@redirect('/new')) }
              Get(path: /restated) { return @response(@response('gone', status: 404), status: 410) }
              Get(path: /plain) { return @response('hi') }
            }
            """));
        var app = new HttpApp(site.Root);
        PageResult Get(string path) => app.Handle(new Dictionary<string, object> { ["method"] = "GET", ["path"] = path });
        Assert.Equal(404, Get("/wrapped").Status);
        Assert.Equal((302, "/new"), (Get("/moved").Status, Get("/moved").Headers["Location"]));
        Assert.Equal(410, Get("/restated").Status);
        Assert.Equal(200, Get("/plain").Status);
    }

    #endregion

    #region Errors in production

    [Fact]
    public async Task In_production_an_error_is_a_plain_500_and_the_log_has_the_details()
    {
        using var site = new TempSite(
            ("broken.html", "<p>{ $nope.x }</p>"),
            ("routes.kscript", """
                import KScript.Http
                Routes {
                  Get(path: /boom) {
                    x = [1, 2]
                    return $x[5].y
                  }
                }
                """),
            ("api.kscript", """
                import KScript.Http
                Routes {
                  Get(path: /boom) {
                    x = [1, 2]
                    return $x[5].y
                  }
                  Get(path: /missing) {
                    throw NotFound "No such thing."
                  }
                }
                """));

        var log = new StringWriter();
        await Serve(site, async http =>
        {
            foreach (string path in new[] { "/broken", "/boom" })
            {
                using var answer = await http.GetAsync(path);
                string body = await answer.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.InternalServerError, answer.StatusCode);
                Assert.Contains(PageServer.HiddenErrorMessage, body);
                Assert.DoesNotContain("nope", body);
                Assert.DoesNotContain(".html", body);
                Assert.DoesNotContain("routes.kscript", body);
            }
            using var api = await http.GetAsync("/api/boom");
            Assert.Equal(HttpStatusCode.InternalServerError, api.StatusCode);
            using var json = System.Text.Json.JsonDocument.Parse(await api.Content.ReadAsStringAsync());
            Assert.Equal(PageServer.HiddenErrorMessage, json.RootElement.GetProperty("error").GetString());

            // An error the site means for the visitor (a status of its own) still says what it says.
            using var missing = await http.GetAsync("/api/missing");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Contains("No such thing.", await missing.Content.ReadAsStringAsync());
        }, production: true, log: log);
        Assert.Contains("nope", log.ToString());
        Assert.Contains("routes.kscript", log.ToString());

        // While working on the site the error page still shows everything.
        await Serve(site, async http =>
        {
            using var answer = await http.GetAsync("/broken");
            Assert.Contains("nope", await answer.Content.ReadAsStringAsync());
            using var route = await http.GetAsync("/boom");
            Assert.Contains("routes.kscript", await route.Content.ReadAsStringAsync());
        });
    }

    #endregion
}
