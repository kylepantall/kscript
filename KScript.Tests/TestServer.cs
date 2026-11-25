using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KScript.Tests;

/// <summary>A small HTTP server on localhost for tests: each request is answered by <see cref="Handler"/>.</summary>
public sealed class TestServer : IDisposable
{
    private readonly HttpListener listener = new HttpListener();
    private readonly Task loop;

    /// <summary>A cookie every answer sets, for tests of what a client sends back.</summary>
    public string SetCookie { get; set; }

    public TestServer(Func<HttpListenerRequest, string, (int Status, string Body, string ContentType)> handler)
    {
        Port = FreePort();
        listener.Prefixes.Add($"http://localhost:{Port}/");
        listener.Start();
        loop = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    return;
                }
                _ = Task.Run(() =>
                {
                    string body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
                    lock (Requests) Requests.Add((ctx.Request.HttpMethod, ctx.Request.Url.PathAndQuery, body, ctx.Request.Headers.AllKeys.ToDictionary(k => k, k => ctx.Request.Headers[k], StringComparer.OrdinalIgnoreCase)));
                    try
                    {
                        var (status, text, type) = handler(ctx.Request, body);
                        ctx.Response.StatusCode = status;
                        if (type != null) ctx.Response.ContentType = type;
                        if (SetCookie != null) ctx.Response.Headers.Add("Set-Cookie", SetCookie);
                        byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
                        ctx.Response.OutputStream.Write(bytes);
                        ctx.Response.Close();
                    }
                    catch (Exception)
                    {
                        ctx.Response.Abort();
                    }
                });
            }
        });
    }

    public int Port { get; }

    public string Url => $"http://localhost:{Port}";

    public List<(string Method, string Path, string Body, Dictionary<string, string> Headers)> Requests { get; } = new();

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        listener.Stop();
        listener.Close();
        try { loop.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }
}
