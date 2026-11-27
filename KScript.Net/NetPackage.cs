using System.Net.Http.Headers;
using System.Text;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Net
{
    /// <summary>The answer to <c>@api()</c>: <c>.status</c>, <c>.is_ok</c>, <c>.text</c>, <c>->json()</c>, <c>->header(name)</c>.</summary>
    public sealed class ResponseValue : IKObject
    {
        public ResponseValue(string url, int status, string reason, string body, Dictionary<string, object> headers, byte[] bytes = null)
        {
            Url = url;
            Status = status;
            Reason = reason;
            Body = body;
            Bytes = bytes;
            Headers = headers;
        }

        public string Url { get; }
        public int Status { get; }
        public string Reason { get; }
        public string Body { get; }
        private byte[] Bytes { get; }
        internal byte[] BinaryBody => Bytes ?? Encoding.UTF8.GetBytes(Body ?? string.Empty);
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
                "cookies" => Cookies(),
                "text" => Body,
                "bytes" => Base64Type.EncodeBytes(BinaryBody),
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
                    // A header sent more than once (Set-Cookie) is a list in .headers; ->header() gives the first.
                    if (Values.Unwrap(result) is List<object> repeated) result = repeated.FirstOrDefault();
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

        /// <summary>The cookies the response sets: each Set-Cookie header's name and value, without its attributes.</summary>
        private Dictionary<string, object> Cookies()
        {
            var cookies = new Dictionary<string, object>(StringComparer.Ordinal);
            object header = Headers.FirstOrDefault(kv => string.Equals(kv.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase)).Value;
            IEnumerable<object> lines = Values.Unwrap(header) switch
            {
                List<object> list => list,
                null => Enumerable.Empty<object>(),
                object one => new[] { one },
            };
            foreach (object line in lines)
            {
                string text = Values.ToStr(line);
                string pair = text.Split(';')[0];
                int eq = pair.IndexOf('=');
                if (eq > 0) cookies[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
            }
            return cookies;
        }

        /// <summary>
        /// Response headers as the map <c>.headers</c> shows: one value per name, except a header sent more than once
        /// (Set-Cookie), which is a list of its values, the way a repeated query value is a list.
        /// </summary>
        internal static void AddHeader(Dictionary<string, object> headers, string name, IEnumerable<string> values)
        {
            List<string> all = values.ToList();
            if (string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                if (headers.TryGetValue(name, out object existing))
                    all.InsertRange(0, Values.Unwrap(existing) is List<object> list ? list.Select(Values.ToStr) : new[] { Values.ToStr(existing) });
                headers[name] = all.Count == 1 ? all[0] : all.Select(v => (object)v).ToList();
                return;
            }
            headers[name] = string.Join(", ", all);
        }

        internal static readonly (string Name, string Type, string Description)[] Fields =
        {
            ("status", KType.Number, "The HTTP status code, such as 200 or 404."),
            ("reason", KType.String, "The status text, such as OK or Not Found."),
            ("is_ok", KType.Bool, "True when the status is 2xx."),
            ("url", KType.String, "The address that was fetched."),
            ("text", KType.String, "The body as text."),
            ("bytes", "base64", "The bytes of the response body, held as a base64 value."),
            ("headers", KType.Map, "The response headers as a map. A header sent more than once, such as Set-Cookie, is a list of its values."),
            ("cookies", KType.Map, "The cookies the response sets (its Set-Cookie headers), as a map of names to values: $reply.cookies.session."),
        };

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("json", "as?: type", "The body read as JSON. With as: a <type> (or 'Repo[]'), the data is checked and turned into records; a list is mapped item by item.", KType.Any),
            ("header", "name", "One response header (or null). For a header sent more than once, such as Set-Cookie, the first; .headers has them all.", KType.String),
            ("check", "", "Throws a Network error unless the status is 2xx; otherwise returns the response.", KType.Response),
        };
    }

    /// <summary>KScript.Net: HTTP requests and downloads.</summary>
    public sealed partial class NetPackage : KScriptPackage
    {
        private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(() =>
        {
            // No cookie jar: the client is shared by every script and request of the process, so a cookie one
            // answer sets must never ride along on a later call. A script that wants a cookie sends the header.
            var client = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpIdentity.Product);
            return client;
        });

        public override string Name => "KScript.Net";

        public override string Description => "HTTP requests (@api) and downloads.";

        protected override void Register(PackageBuilder p)
        {
            LoopbackJsonConnection.Register(p);
            RegisterHttpHelpers(p);
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
                            new ParameterInfo("headers", optional: true, type: KType.Map, description: "Header names and values. Set `User-Agent` to identify the client or `Content-Type` to override the body format header.")
                                { DefaultValue = "{ 'User-Agent': 'kscript' }", Example = "{ Accept: 'application/json' }" },
                            new ParameterInfo("timeout", optional: true, type: Duration.TypeName, description: "Maximum wait for an outbound HTTP(S) request. A number is seconds. In-process requests to project routes ignore this field.")
                                { DefaultValue = "100s", Example = "30s" },
                            new ParameterInfo("retries", optional: true, type: KType.Number, description: "Additional attempts on transient outbound failures for GET, HEAD and OPTIONS.")
                                { DefaultValue = "0", Example = "2" },
                            new ParameterInfo("retry_delay", optional: true, type: Duration.TypeName, description: "Time between retry attempts.")
                                { DefaultValue = "100ms", Example = "250ms" },
                        },
                    },
                    new ParameterInfo("post", optional: true, type: KType.Any, description: "Sends this body with POST: `@api('/notes', post: { title: 'Ship it' })`. Maps and lists are sent as JSON, base64 values as binary data, and other values as UTF-8 text. Without post:, put:, patch: or delete:, the request is a GET."),
                    new ParameterInfo("put", optional: true, type: KType.Any, description: "Sends this body with PUT."),
                    new ParameterInfo("patch", optional: true, type: KType.Any, description: "Sends this body with PATCH."),
                    new ParameterInfo("delete", optional: true, type: KType.Any, description: "Sends a DELETE. `delete: true` sends no body; anything else is sent as the body."),
                    new ParameterInfo("bearer", optional: true, type: KType.String, description: "An Authorization: Bearer header with this token."),
                    new ParameterInfo("timeout", optional: true, type: Duration.TypeName, description: "The same as options.timeout: 5s."),
                    new ParameterInfo("retries", optional: true, type: KType.Number, description: "The same as options.retries: 3."),
                    new ParameterInfo("as", optional: true, type: KType.Type, description: "Checks the response succeeded and reads its JSON as this type. `@api($url, as: Note)` is `@api($url)->check()->json(as: Note)`.")) })
            {
                ReturnType = KType.Response,
                Example = """
                    <kscript>
                      <import package="KScript.Net" />
                      <let name="url" value="$args->first() ?? 'https://httpbin.org/anything'" />
                      <let name="reply" value="@api($url, post: { title: 'Ship it' }, options: { headers: { Accept: 'application/json' } }, timeout: 30s)" />
                      <echo>{ $reply->check().status }</echo>
                    </kscript>
                    """,
            }, Api);
            p.Command("query_string", "map", "A map as a URL query: a=1&b=two. A list is written once per item (tag=a&tag=b), the form @parse_query reads back.", KType.String, c =>
                string.Join("&", c.Map(0).Where(kv => kv.Value != null)
                    .SelectMany(kv => Values.Unwrap(kv.Value) is List<object> list ? list.Where(item => item != null).Select(item => (kv.Key, Value: item)) : new[] { (kv.Key, kv.Value) })
                    .Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(Values.ToStr(kv.Value)))));

            p.Command("download", "url, to, overwrite?: bool", "Downloads a URL to a file and returns the file's full path. Folders are created as needed, and a relative path is from the script's folder. overwrite: false keeps a file that is already there.", KType.String, c =>
            {
                string url = c.Str(0);
                string to = c.Host.ResolvePath(c.Str(1), c.Module?.Directory ?? c.Host.WorkingDirectory);
                c.Host.CheckWrite("@download()");
                if (File.Exists(to) && c.Has(2, "overwrite") && !Values.Truthy(c.Arg(2, "overwrite"))) return to;
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw c.Error($"'{url}' isn't an http:// or https:// address.", "Argument");
                if (c.Host.Items.GetValueOrDefault(HttpStubKey) is ICallable stub)
                {
                    ResponseValue reply = StubResponse(c, stub, uri, "GET", new Dictionary<string, object>());
                    if (!reply.Ok) throw c.Error($"{url} answered {reply.Status} {reply.Reason}.", "Network");
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.WriteAllBytes(to, reply.BinaryBody);
                    return to;
                }
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
                    prefer: "@api($url, as: Type) (the same as ->check()->json(as: Type)) to fail fast and check the data's shape.")));

            p.Examples(
                ("response->check", "<let name=\"order\" value=\"@api('https://api.example.com/orders/42')->check()->json()\" />\n<echo>{ $order.id }</echo>"),
                ("response->header", "<let name=\"response\" value=\"@api('https://example.com')\" />\n<echo>{ $response->header('Content-Type') }</echo>"),
                ("response->json", "<type name=\"Todo\" fields=\"id: int, title: string, completed: bool\" />\n<let name=\"todo\" value=\"@api('https://jsonplaceholder.typicode.com/todos/1')->json(as: Todo)\" />\n<echo>{ $todo.title }</echo>"),
                ("@parse_query", "<let name=\"query\" value=\"@parse_query('page=2&amp;tag=a&amp;tag=b')\" />\n<echo>{ $query.page } { $query.tag }</echo>"),
                ("@query_string", "<let name=\"filters\" value=\"{ page: 2, tag: ['a', 'b'] }\" />\n<echo>{ @query_string($filters) }</echo>"),
                ("@http_client", "<let name=\"github\" value=\"@http_client('https://api.github.com', { headers: { Accept: 'application/vnd.github+json' } })\" />\n<let name=\"repo\" value=\"$github('/repos/dotnet/runtime')->json()\" />\n<echo>{ $repo.full_name }</echo>"),
                ("@with_http_stub", "<let name=\"name\" value=\"@with_http_stub(fn(request) => { status: 200, body: { name: 'stub' } }, fn() => @api('https://api.example.com/me')->json().name)\" />\n<echo>{ $name }</echo>"));
        }

        /// <summary>The named shorthands of @api, after url and options, in their places in the signature.</summary>
        private static readonly string[] ApiShorthands = { "post", "put", "patch", "delete", "bearer", "timeout", "retries", "as" };

        private static object Api(CallContext c)
        {
            var options = c.Arg(1) == null ? new Dictionary<string, object>(StringComparer.Ordinal) : new Dictionary<string, object>(c.Map(1), StringComparer.Ordinal);
            // The method and body are named arguments now: @api($url, post: $note), not options: { method: 'POST', body: $note }.
            if (options.ContainsKey("method") || options.ContainsKey("body"))
                throw c.Error("@api() takes the method and body as one argument: post: $body, put: $body, patch: $body or delete: true, not options: { method:, body: }.", "Argument");
            object Named(string name) => c.Arg(2 + Array.IndexOf(ApiShorthands, name), name);
            bool Given(string name) => c.Has(2 + Array.IndexOf(ApiShorthands, name), name);
            void Set(string option, object value, string shorthand)
            {
                if (options.ContainsKey(option)) throw c.Error($"@api() was given {option} twice: in options and as {shorthand}:. Give it once.", "Argument");
                options[option] = value;
            }
            // post: null and delete: false send nothing, so a helper can pass its own arguments straight on.
            bool Sent(string method) => Given(method) && Values.Unwrap(Named(method)) is not (null or false);
            string[] methods = new[] { "post", "put", "patch", "delete" }.Where(Sent).ToArray();
            if (methods.Length > 1) throw c.Error($"@api() takes one of post:, put:, patch: and delete:, and was given {string.Join(" and ", methods.Select(m => m + ":"))}.", "Argument");
            foreach (string method in methods)
            {
                Set("method", method.ToUpperInvariant(), method);
                object body = Named(method);
                if (!(method == "delete" && body is bool)) Set("body", body, method);
            }
            if (Given("bearer"))
            {
                var headers = options.GetValueOrDefault("headers") is Dictionary<string, object> given ? new Dictionary<string, object>(given, StringComparer.Ordinal) : new Dictionary<string, object>(StringComparer.Ordinal);
                if (headers.Keys.Any(k => k.Equals("Authorization", StringComparison.OrdinalIgnoreCase)))
                    throw c.Error("@api() was given an Authorization header and bearer:. Give it once.", "Argument");
                headers["Authorization"] = "Bearer " + Values.ToStr(Named("bearer"));
                options["headers"] = headers;
            }
            if (Given("timeout")) Set("timeout", Named("timeout"), "timeout");
            if (Given("retries")) Set("retries", Named("retries"), "retries");
            ResponseValue response = Api(c, c.Str(0), options);
            if (!Given("as")) return response;
            if (!response.Ok) throw new KScriptException($"{response.Url} answered {response.Status} {response.Reason}.", "Network") { Fields = { ["status"] = (double)response.Status } };
            object data;
            try
            {
                data = Values.ParseJson(response.Body);
            }
            catch (KScriptException ex)
            {
                throw new KScriptException($"The response from {response.Url} isn't JSON ({ex.Message}).", "Format");
            }
            return Shapes.MapAs(c.Interpreter, data, Named("as"), $"The response from {response.Url}", c.Scope, c.Module);
        }

        private static ResponseValue Api(CallContext c, string url, Dictionary<string, object> options)
        {
            if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)) return Local(c, url, options);
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw c.Error($"@api() needs an http:// or https:// address, or a path on this app such as '/api/notes', but got '{url}'.", "Argument");

            string method = options.TryGetValue("method", out object m) && m != null ? Values.ToStr(m).ToUpperInvariant() : "GET";
            if (!IsToken(method)) throw c.Error($"method '{method}' isn't an HTTP method. Use GET, POST, PUT, PATCH or DELETE.", "Argument");
            double retries = options.GetValueOrDefault("retries") is object count ? Values.ToNumber(count, "retries") : 0;
            if (retries < 0 || retries > 10 || retries != Math.Truncate(retries))
                throw c.Error("retries must be a whole number from 0 to 10.", "Argument");
            if (retries > 0 && method is not ("GET" or "HEAD" or "OPTIONS"))
                throw c.Error("Automatic retries require GET, HEAD, or OPTIONS.", "Argument");
            double seconds = options.TryGetValue("timeout", out object t) && t != null ? Duration.Seconds(t, "timeout") : 100;
            double delay = options.GetValueOrDefault("retry_delay") is object wait ? Duration.Seconds(wait, "retry_delay") : 0.1;
            if (delay < 0 || seconds <= 0 || !double.IsFinite(seconds) || !double.IsFinite(delay))
                throw c.Error("timeout must be positive and retry_delay must be nonnegative.", "Argument");
            if (seconds > MaxWait || delay > MaxWait) throw c.Error("timeout and retry_delay can be at most 24 days.", "Argument");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.Interpreter.Options.Cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    try
                    {
                        ResponseValue response = Send(c, uri, method, options, timeout.Token);
                        if (attempt >= retries || response.Status is not (408 or 429 or 502 or 503 or 504)) return response;
                    }
                    catch (KScriptException ex) when (ex.Type == "Network" && attempt < retries) { }
                    Task.Delay(TimeSpan.FromSeconds(delay), timeout.Token).GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException) when (!c.Interpreter.Options.Cancellation.IsCancellationRequested)
            {
                throw new KScriptException($"{url} didn't answer within {seconds} seconds.", "Timeout");
            }
        }

        /// <summary>The longest wait a timer takes: a little under 25 days.</summary>
        private const double MaxWait = 24 * 86400;

        /// <summary>Whether text can be an HTTP method: one or more letters, digits or the few signs a token allows.</summary>
        private static bool IsToken(string text) =>
            text.Length > 0 && text.All(ch => ch < 127 && (char.IsAsciiLetterOrDigit(ch) || "!#$%&'*+-.^_`|~".Contains(ch)));

        private static ResponseValue Send(CallContext c, Uri uri, string method, Dictionary<string, object> options, CancellationToken stop)
        {
            if (c.Host.Items.GetValueOrDefault(HttpStubKey) is ICallable stub)
                return StubResponse(c, stub, uri, method, options);

            using var request = new HttpRequestMessage(new HttpMethod(method), uri);
            string contentType = null;
            if (options.GetValueOrDefault("headers") is Dictionary<string, object> headers)
            {
                foreach (var kv in headers)
                {
                    if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = Values.ToStr(kv.Value);
                    else request.Headers.TryAddWithoutValidation(kv.Key, Values.ToStr(kv.Value));
                }
            }
            if (options.GetValueOrDefault("body") is object body)
            {
                string defaultType;
                if (Base64Type.TryDecodeBytes(body, out byte[] binary))
                {
                    request.Content = new ByteArrayContent(binary);
                    defaultType = "application/octet-stream";
                }
                else
                {
                    bool json = Values.Unwrap(body) is Dictionary<string, object> or List<object>;
                    request.Content = new StringContent(json ? Values.ToJson(body) : Values.ToStr(body), Encoding.UTF8);
                    defaultType = json ? "application/json" : "text/plain; charset=utf-8";
                }
                if (!MediaTypeHeaderValue.TryParse(contentType ?? defaultType, out MediaTypeHeaderValue type))
                    throw c.Error($"Content-Type '{contentType}' isn't a content type. Write one such as application/json.", "Argument");
                request.Content.Headers.ContentType = type;
            }
            try
            {
                using HttpResponseMessage response = Client.Value.SendAsync(request, stop).GetAwaiter().GetResult();
                byte[] bytes = response.Content.ReadAsByteArrayAsync(stop).GetAwaiter().GetResult();
                string text = response.Content.ReadAsStringAsync(stop).GetAwaiter().GetResult();
                var responseHeaders = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in response.Headers.Concat(response.Content.Headers)) ResponseValue.AddHeader(responseHeaders, header.Key, header.Value);
                return new ResponseValue(uri.OriginalString, (int)response.StatusCode, response.ReasonPhrase, text, responseHeaders, bytes);
            }
            catch (HttpRequestException ex)
            {
                throw new KScriptException($"Couldn't reach {uri}: {ex.Message}", "Network");
            }
        }

        /// <summary>
        /// @api('/api/notes'): a request to the routes of the script's own project, answered in this process. The
        /// method, headers and body options are used; outbound timeout is inapplicable here.
        /// </summary>
        private static ResponseValue Local(CallContext c, string url, Dictionary<string, object> options)
        {
            string method = options.TryGetValue("method", out object m) && m != null ? Values.ToStr(m).ToUpperInvariant() : "GET";
            var headers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["user-agent"] = HttpIdentity.Product };
            if (options.TryGetValue("headers", out object h) && Values.Unwrap(h) is Dictionary<string, object> given)
            {
                foreach (var kv in given) headers[kv.Key.ToLowerInvariant()] = Values.ToStr(kv.Value);
            }
            string body = string.Empty;
            if (options.TryGetValue("body", out object b) && b != null)
            {
                if (Base64Type.TryDecodeBytes(b, out _))
                    throw c.Error("Binary request bodies are supported for outbound HTTP requests.", "Argument");
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
                new Dictionary<string, object>(response.Headers, StringComparer.OrdinalIgnoreCase), response.BodyBytes);
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
