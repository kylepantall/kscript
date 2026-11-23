using System.Net.Http.Headers;
using System.Text;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Net
{
    /// <summary>The answer to <c>@api()</c>: <c>.status</c>, <c>.is_ok</c>, <c>.text</c>, <c>->json()</c>, <c>->header(name)</c>.</summary>
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
                "reason" => Reason,
                "is_ok" => Ok,
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
                case "header":
                    string key = call.Str(0);
                    result = Headers.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
                    return true;
                case "json":
                    object data;
                    try
                    {
                        data = Values.ParseJson(Body);
                    }
                    catch (KScriptException ex)
                    {
                        throw new KScriptException($"The response from {Url} isn't JSON ({ex.Message}).", "Format");
                    }
                    object type = call.Arg(0, "as");
                    result = type == null ? data : Shapes.MapAs(call.Interpreter, data, type, $"The response from {Url}", call.Scope, call.Module);
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

        internal static readonly (string Name, string Type, string Description)[] Fields =
        {
            ("status", KType.Number, "The HTTP status code, such as 200 or 404."),
            ("reason", KType.String, "The status text, such as OK or Not Found."),
            ("is_ok", KType.Bool, "True when the status is 2xx."),
            ("url", KType.String, "The address that was fetched."),
            ("text", KType.String, "The body as text."),
            ("headers", KType.Map, "The response headers as a map."),
        };

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("json", "as?: type", "The body read as JSON. With as: a <type> (or 'Repo[]'), the data is checked and turned into records; a list is mapped item by item.", KType.Any),
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpIdentity.Product);
            return client;
        });

        public override string Name => "KScript.Net";

        public override string Description => "HTTP requests (@api) and downloads.";

        protected override void Register(PackageBuilder p)
        {
            LoopbackJsonConnection.Register(p);
            foreach (var f in ResponseValue.Fields)
                p.Field(KType.Response, f.Name, f.Type, f.Description);
            foreach (var m in ResponseValue.Methods)
                p.Method(KType.Response, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((ResponseValue)t).TryCallMethod(c, m.Name, out object r) ? r : null);

            p.LogProvider("http", "Sends log entries to a web service as a JSON array (url, headers, batch).", c => new HttpLogProvider(c, Client.Value));

            p.Command(new CallableInfo(CallableKind.Command, "api",
                "Makes an HTTP request and returns the response. Give an absolute HTTP(S) URL or a path such as `'/api/notes'` to call the project's own routes in this process.",
                new[] { new Signature(
                    new ParameterInfo("url", type: KType.String, description: "The absolute URL or project route to request."),
                    new ParameterInfo("options", optional: true, type: KType.Map, description: "Configure this request with the fields below.")
                    {
                        Fields = new[]
                        {
                            new ParameterInfo("method", optional: true, type: KType.String, description: "HTTP method. KScript converts it to uppercase.")
                                { DefaultValue = "'GET'", Example = "'POST'" },
                            new ParameterInfo("headers", optional: true, type: KType.Map, description: "Header names and values. Set `User-Agent` to identify the client or `Content-Type` to override the body format header.")
                                { DefaultValue = "{ 'User-Agent': 'kscript' }", Example = "{ Accept: 'application/json' }" },
                            new ParameterInfo("body", optional: true, type: KType.Any, description: "A map or list is sent as JSON with `application/json`. Other non-null values are sent as text with `text/plain; charset=utf-8`.")
                                { DefaultValue = "null", Example = "{ title: 'Ship it' }" },
                            new ParameterInfo("timeout", optional: true, type: Duration.TypeName, description: "Maximum wait for an outbound HTTP(S) request. A number is seconds. In-process requests to project routes ignore this field.")
                                { DefaultValue = "100s", Example = "30s" },
                        },
                    }) })
            {
                ReturnType = KType.Response,
                Example = """
                    <kscript>
                      <import package="KScript.Net" />
                      <let name="url" value="$args->first() ?? 'https://httpbin.org/anything'" />
                      <let name="reply" value="@api($url, options: { method: 'POST', headers: { Accept: 'application/json' }, body: { title: 'Ship it' }, timeout: 30s })" />
                      <echo>{ $reply->check().status }</echo>
                    </kscript>
                    """,
            }, Api);
            p.Command("query_string", "map", "A map as a URL query: a=1&b=two.", KType.String, c =>
                string.Join("&", c.Map(0).Where(kv => kv.Value != null).Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(Values.ToStr(kv.Value)))));

            p.Command("download", "url, to, overwrite?: bool", "Downloads a URL to a file and returns the file's full path. Folders are created as needed, and a relative path is from the script's folder. overwrite: false keeps a file that is already there.", KType.String, c =>
            {
                string url = c.Str(0);
                string to = c.Host.ResolvePath(c.Str(1), c.Module?.Directory ?? c.Host.WorkingDirectory);
                c.Host.CheckWrite("@download()");
                if (File.Exists(to) && c.Has(2, "overwrite") && !Values.Truthy(c.Arg(2, "overwrite"))) return to;
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw c.Error($"'{url}' isn't an http:// or https:// address.", "Argument");
                using HttpResponseMessage response = Client.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, c.Interpreter.Options.Cancellation).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    throw c.Error($"{url} answered {(int)response.StatusCode} {response.ReasonPhrase}.", "Network");
                string dir = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (Stream body = response.Content.ReadAsStream())
                using (FileStream file = File.Create(to))
                {
                    body.CopyTo(file);
                }
                return to;
            }, "<do>@download('https://example.com/data.csv', 'data/data.csv')</do>");

            p.AgentNotes(
                ("@api", new AgentNotes(pitfall: "A 4xx or 5xx status doesn't throw: chain ->check() (throws Network) or test .is_ok.",
                    prefer: "@api($url)->check()->json(as: Type) to fail fast and check the data's shape.")));
        }

        private static object Api(CallContext c)
        {
            string url = c.Str(0);
            if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)) return Local(c, url);
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw c.Error($"@api() needs an http:// or https:// address, or a path on this app such as '/api/notes', but got '{url}'.", "Argument");

            Dictionary<string, object> options = c.Has(1) && c.Arg(1) != null ? c.Map(1) : new Dictionary<string, object>();
            string method = options.TryGetValue("method", out object m) && m != null ? Values.ToStr(m).ToUpperInvariant() : "GET";
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);

            string contentType = null;
            if (options.TryGetValue("headers", out object h) && Values.Unwrap(h) is Dictionary<string, object> headers)
            {
                foreach (var kv in headers)
                {
                    if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = Values.ToStr(kv.Value);
                    else request.Headers.TryAddWithoutValidation(kv.Key, Values.ToStr(kv.Value));
                }
            }
            if (options.TryGetValue("body", out object body) && body != null)
            {
                bool json = Values.Unwrap(body) is Dictionary<string, object> or List<object>;
                request.Content = new StringContent(json ? Values.ToJson(body) : Values.ToStr(body), Encoding.UTF8);
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? (json ? "application/json" : "text/plain; charset=utf-8"));
            }

            double seconds = options.TryGetValue("timeout", out object t) && t != null ? Duration.Seconds(t, "timeout") : 100;
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

        /// <summary>
        /// @api('/api/notes'): a request to the routes of the script's own project, answered in this process. The
        /// method, headers and body options are used; outbound timeout is inapplicable here.
        /// </summary>
        private static object Local(CallContext c, string url)
        {
            Dictionary<string, object> options = c.Has(1) && c.Arg(1) != null ? c.Map(1) : new Dictionary<string, object>();
            string method = options.TryGetValue("method", out object m) && m != null ? Values.ToStr(m).ToUpperInvariant() : "GET";
            var headers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["user-agent"] = HttpIdentity.Product };
            if (options.TryGetValue("headers", out object h) && Values.Unwrap(h) is Dictionary<string, object> given)
            {
                foreach (var kv in given) headers[kv.Key.ToLowerInvariant()] = Values.ToStr(kv.Value);
            }
            string body = string.Empty;
            if (options.TryGetValue("body", out object b) && b != null)
            {
                bool json = Values.Unwrap(b) is Dictionary<string, object> or List<object>;
                body = json ? Values.ToJson(b) : Values.ToStr(b);
                headers.TryAdd("content-type", json ? "application/json" : "text/plain; charset=utf-8");
            }

            int question = url.IndexOf('?');
            string path = question >= 0 ? url.Substring(0, question) : url;
            var query = new Dictionary<string, object>(StringComparer.Ordinal);
            if (question >= 0)
            {
                foreach (string pair in url.Substring(question + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    string key = Uri.UnescapeDataString((eq >= 0 ? pair.Substring(0, eq) : pair).Replace('+', ' '));
                    query[key] = eq >= 0 ? Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')) : string.Empty;
                }
            }

            var request = new Dictionary<string, object>
            {
                ["method"] = method,
                ["path"] = path,
                ["url"] = url,
                ["query"] = query,
                ["headers"] = headers,
                ["body"] = body,
            };
            LocalResponse response = LocalRequests.Send(c.Host, c.Module?.Directory ?? c.Host.WorkingDirectory, request);
            if (response == null)
                throw c.Error($"@api('{url}') asks this app for a path, but this script isn't in a project with routes. Give a full http:// address instead.", "Network");
            return new ResponseValue(url, response.Status, response.Reason, response.Body,
                new Dictionary<string, object>(response.Headers, StringComparer.OrdinalIgnoreCase));
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
