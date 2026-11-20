using System.Net.Http.Headers;
using System.Text;

using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Net
{
    /// <summary>The answer to <c>@fetch()</c>: <c>->status()</c>, <c>->ok()</c>, <c>->text()</c>, <c>->json()</c>, <c>->header(name)</c>.</summary>
    public sealed class ResponseValue : IKObject
    {
        public ResponseValue(string url, int status, string reason, string body, Dictionary<string, object> headers)
        {
            Url = url;
            Status = status;
            Reason = reason;
            Body = body;
            Headers = headers;
        }

        public string Url { get; }
        public int Status { get; }
        public string Reason { get; }
        public string Body { get; }
        public Dictionary<string, object> Headers { get; }

        public bool Ok => Status >= 200 && Status < 300;

        public string TypeName => KType.Response;

        public string ToText() => Body;

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "status" => (double)Status,
                "ok" => Ok,
                "url" => Url,
                "headers" => Headers,
                "text" => Body,
                _ => null,
            };
            return value != null;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "status": result = (double)Status; return true;
                case "reason": result = Reason; return true;
                case "ok": result = Ok; return true;
                case "url": result = Url; return true;
                case "text": result = Body; return true;
                case "headers": result = Headers; return true;
                case "header":
                    string key = call.Str(0);
                    result = Headers.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
                    return true;
                case "json":
                    try
                    {
                        result = Values.ParseJson(Body);
                    }
                    catch (KScriptException ex)
                    {
                        throw new KScriptException($"The response from {Url} isn't JSON ({ex.Message}).", "Format");
                    }
                    return true;
                case "check":
                    if (!Ok) throw new KScriptException($"{Url} answered {Status} {Reason}.", "Network") { Fields = { ["status"] = (double)Status } };
                    result = this;
                    return true;
                default:
                    result = null;
                    return false;
            }
        }

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("status", "", "The HTTP status code, such as 200 or 404.", KType.Number),
            ("reason", "", "The status text, such as OK or Not Found.", KType.String),
            ("ok", "", "True for a 2xx status.", KType.Bool),
            ("url", "", "The address that was fetched.", KType.String),
            ("text", "", "The body as text.", KType.String),
            ("json", "", "The body read as JSON.", KType.Any),
            ("headers", "", "The response headers as a map.", KType.Map),
            ("header", "name", "One response header (or null).", KType.String),
            ("check", "", "Throws a Network error unless the status is 2xx; otherwise returns the response.", KType.Response),
        };
    }

    /// <summary>KScript.Net: HTTP requests and downloads.</summary>
    public sealed class NetPackage : KScriptPackage
    {
        private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KScript/2.0");
            return client;
        });

        public override string Name => "KScript.Net";

        public override string Description => "HTTP requests (@fetch) and downloads.";

        protected override void Register(PackageBuilder p)
        {
            foreach (var m in ResponseValue.Methods)
                p.Method(KType.Response, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((ResponseValue)t).TryCallMethod(c, m.Name, out object r) ? r : null);

            p.LogProvider("http", "Sends log entries to a web service as a JSON array (url, headers, batch).", c => new HttpLogProvider(c, Client.Value));

            p.Command("fetch", "url, options?",
                "Makes an HTTP request and returns the response. options is a map: method, headers, body (a map is sent as JSON), timeout (seconds, default 100).",
                KType.Response, Fetch,
                "<let name=\"repo\" value=\"@fetch('https://api.github.com/repos/kylepantall/kscript')->check()->json()\" />");
            p.Command("url_encode", "text", "Escapes text for use in a URL.", KType.String, c => Uri.EscapeDataString(c.Str(0)));
            p.Command("url_decode", "text", "Reverses @url_encode().", KType.String, c => Uri.UnescapeDataString(c.Str(0)));
            p.Command("query_string", "map", "A map as a URL query: a=1&b=two.", KType.String, c =>
                string.Join("&", c.Map(0).Where(kv => kv.Value != null).Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(Values.ToStr(kv.Value)))));

            p.Element(new ElementInfo("download", "Downloads a URL to a file. Folders are created as needed.", ContentKind.Empty,
                new A("url", K.Expression, "The address to download.", true),
                new A("to", K.Expression, "The file to save to. Relative paths are from the script's folder.", true),
                new A("overwrite", K.Bool, "no keeps a file that is already there (the default is yes)."))
            {
                Context = ElementContext.Script,
                Example = "<download url=\"'https://example.com/data.csv'\" to=\"'data/data.csv'\" />",
            }, run =>
            {
                string url = Values.ToStr(run.Eval("url"));
                string to = Path.GetFullPath(Path.Combine(run.Module.Directory, Values.ToStr(run.Eval("to"))));
                if (File.Exists(to) && !run.Flag("overwrite", true)) return null;
                CheckUrl(url, run.Error);
                using HttpResponseMessage response = Client.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, run.Interpreter.Options.Cancellation).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    throw run.Error($"{url} answered {(int)response.StatusCode} {response.ReasonPhrase}.", "url", "Network");
                string dir = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (Stream body = response.Content.ReadAsStream())
                using (FileStream file = File.Create(to))
                {
                    body.CopyTo(file);
                }
                return null;
            });
        }

        private static void CheckUrl(string url, Func<string, string, string, KScriptException> error)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw error($"'{url}' isn't an http:// or https:// address.", "url", "Argument");
        }

        private static object Fetch(CallContext c)
        {
            string url = c.Str(0);
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw c.Error($"@fetch() needs an http:// or https:// address, but got '{url}'.", "Argument");

            Dictionary<string, object> options = c.Has(1) && c.Arg(1) != null ? c.Map(1) : new Dictionary<string, object>();
            string method = options.TryGetValue("method", out object m) && m != null ? Values.ToStr(m).ToUpperInvariant() : "GET";
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);

            string contentType = null;
            if (options.TryGetValue("headers", out object h) && h is Dictionary<string, object> headers)
            {
                foreach (var kv in headers)
                {
                    if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = Values.ToStr(kv.Value);
                    else request.Headers.TryAddWithoutValidation(kv.Key, Values.ToStr(kv.Value));
                }
            }
            if (options.TryGetValue("body", out object body) && body != null)
            {
                bool json = body is Dictionary<string, object> or List<object>;
                request.Content = new StringContent(json ? Values.ToJson(body) : Values.ToStr(body), Encoding.UTF8);
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? (json ? "application/json" : "text/plain; charset=utf-8"));
            }

            double seconds = options.TryGetValue("timeout", out object t) && t != null ? Values.ToNumber(t, "timeout") : 100;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.Interpreter.Options.Cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            try
            {
                using HttpResponseMessage response = Client.Value.SendAsync(request, timeout.Token).GetAwaiter().GetResult();
                string text = response.Content.ReadAsStringAsync(timeout.Token).GetAwaiter().GetResult();
                var responseHeaders = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in response.Headers.Concat(response.Content.Headers)) responseHeaders[header.Key] = string.Join(", ", header.Value);
                return new ResponseValue(url, (int)response.StatusCode, response.ReasonPhrase, text, responseHeaders);
            }
            catch (OperationCanceledException) when (!c.Interpreter.Options.Cancellation.IsCancellationRequested)
            {
                throw new KScriptException($"{url} didn't answer within {seconds} seconds.", "Timeout");
            }
            catch (HttpRequestException ex)
            {
                throw new KScriptException($"Couldn't reach {url}: {ex.Message}", "Network");
            }
        }
    }

    /// <summary>
    /// A log provider that POSTs entries to a URL as a JSON array, <c>batch</c> at a time (default 20) and the rest
    /// when the script ends. Each entry is an object of time, level, message, category, script, line and the fields.
    /// </summary>
    public sealed class HttpLogProvider : ILogProvider
    {
        private readonly HttpClient client;
        private readonly Uri url;
        private readonly Dictionary<string, string> headers = new Dictionary<string, string>();
        private readonly int batch;
        private readonly List<string> pending = new List<string>();

        public HttpLogProvider(LogProviderContext c, HttpClient client)
        {
            this.client = client;
            string address = c.Need("url");
            if (!Uri.TryCreate(address, UriKind.Absolute, out url) || (url.Scheme != "http" && url.Scheme != "https"))
                throw new KScriptException($"{c.Where}: url should be an http or https address, but is '{address}'.", "Config");
            if (c.Get("headers") is object h)
            {
                if (h is not Dictionary<string, object> map) throw new KScriptException($"{c.Where}: headers should be a map, such as {{ Authorization: 'Bearer ...' }}.", "Config");
                foreach (var kv in map) headers[kv.Key] = Values.ToStr(kv.Value);
            }
            double size = c.Get("batch") is object b ? Values.ToNumber(b, $"{c.Where} batch") : 20;
            if (size < 1) throw new KScriptException($"{c.Where}: batch should be 1 or more.", "Config");
            batch = (int)size;
        }

        public void Write(LogEntry entry)
        {
            bool full;
            lock (pending)
            {
                pending.Add(entry.ToJson());
                full = pending.Count >= batch;
            }
            if (full) Flush();
        }

        public void Flush()
        {
            string body;
            lock (pending)
            {
                if (pending.Count == 0) return;
                body = "[" + string.Join(",", pending) + "]";
                pending.Clear();
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            foreach (var kv in headers) request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using HttpResponseMessage response = client.Send(request, cancel.Token);
            if (!response.IsSuccessStatusCode) throw new KScriptException($"{url} answered {(int)response.StatusCode} {response.ReasonPhrase}.", "Http");
        }
    }
}
