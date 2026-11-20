using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KScript.Docs.Mcp
{
    /// <summary>
    /// A Model Context Protocol server for the KScript docs, so coding assistants can read the docs of the installed
    /// kscript while they write KScript. It speaks JSON-RPC 2.0, one message per line over stdin and stdout (the
    /// usual way an MCP client starts a local server), or over HTTP on localhost. Everything comes from the same
    /// guides and catalog as <c>kscript docs</c> and the editor, so it works offline and can't disagree with them.
    /// </summary>
    public sealed class McpServer
    {
        /// <summary>The protocol versions this server speaks, newest first.</summary>
        public static readonly IReadOnlyList<string> ProtocolVersions = new[] { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

        public const string ServerName = "kscript-docs";

        private const string GuideTemplate = DocsMarkdown.UriPrefix + "/guide/{slug}";
        private const string PackageTemplate = DocsMarkdown.UriPrefix + "/reference/{package}";

        public McpServer(DocsSite site = null, string format = "kscript", string workingDirectory = null)
        {
            Docs = new DocsMarkdown(site ?? DocsSite.Build());
            Tools = new DocsTools(Docs, format ?? "kscript", workingDirectory ?? Directory.GetCurrentDirectory());
        }

        public DocsMarkdown Docs { get; }

        public DocsTools Tools { get; }

        /// <summary>What the server tells the client about itself, shown to the model.</summary>
        public string Instructions =>
            $"Docs for KScript {Docs.Site.Version}, the version installed here. KScript scripts are XML (.ks.xml) or the .kscript format; " +
            $"examples are shown as {(Tools.Format == "xml" ? "XML" : ".kscript")} unless a call passes format. " +
            "Before writing KScript, look names up (lookup) rather than guessing: method and command names, parameters and packages differ from other languages. " +
            "search_docs finds guides and reference entries, read_doc reads a whole page. Run check_code on code before giving it to the user, and run_code to try small scripts.";

        #region Messages

        /// <summary>Answers one line of JSON-RPC: the response to send, or null for a notification.</summary>
        public string HandleLine(string line)
        {
            JsonNode message;
            try
            {
                message = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                return Error(null, -32700, "Parse error: each message must be one line of JSON.").ToJsonString();
            }
            return Handle(message)?.ToJsonString();
        }

        /// <summary>Answers a message, or a batch of them; null when nothing needs an answer.</summary>
        public JsonNode Handle(JsonNode message)
        {
            if (message is JsonArray batch)
            {
                var answers = new JsonArray();
                foreach (JsonNode item in batch)
                {
                    JsonNode answer = Handle(item);
                    if (answer != null) answers.Add(answer);
                }
                return answers.Count > 0 ? answers : null;
            }
            if (message is not JsonObject request || DocsTools.Text(request, "method") is not string method)
            {
                // A response from the client (to a request this server never sends), or not JSON-RPC at all.
                return message is JsonObject o && (o.ContainsKey("result") || o.ContainsKey("error")) ? null : Error(null, -32600, "Invalid request.");
            }

            JsonNode id = request["id"]?.DeepClone();
            bool notification = !request.ContainsKey("id");
            JsonObject parameters = request["params"] as JsonObject ?? new JsonObject();
            JsonNode result;
            try
            {
                result = method switch
                {
                    "initialize" => Initialize(parameters),
                    "ping" => new JsonObject(),
                    "tools/list" => ListTools(),
                    "tools/call" => CallTool(parameters),
                    "resources/list" => ListResources(),
                    "resources/templates/list" => ListResourceTemplates(),
                    "resources/read" => ReadResource(parameters),
                    "prompts/list" => new JsonObject { ["prompts"] = new JsonArray() },
                    _ when notification => null,
                    _ => throw new McpException(-32601, $"Method not found: {method}."),
                };
            }
            catch (McpException ex)
            {
                return notification ? null : Error(id, ex.Code, ex.Message);
            }
            if (notification) return null;
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        }

        private static JsonObject Error(JsonNode id, int code, string message) =>
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

        private sealed class McpException : Exception
        {
            public McpException(int code, string message) : base(message) => Code = code;

            public int Code { get; }
        }

        private JsonObject Initialize(JsonObject parameters)
        {
            string asked = DocsTools.Text(parameters, "protocolVersion");
            return new JsonObject
            {
                ["protocolVersion"] = ProtocolVersions.Contains(asked) ? asked : ProtocolVersions[0],
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject { ["listChanged"] = false },
                    ["resources"] = new JsonObject { ["listChanged"] = false, ["subscribe"] = false },
                },
                ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["title"] = "KScript docs", ["version"] = Docs.Site.Version },
                ["instructions"] = Instructions,
            };
        }

        #endregion

        #region Tools

        private JsonObject ListTools()
        {
            var tools = new JsonArray();
            foreach (Tool t in Tools.All)
            {
                tools.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["title"] = t.Title,
                    ["description"] = t.Description,
                    ["inputSchema"] = t.InputSchema.DeepClone(),
                    ["annotations"] = new JsonObject { ["title"] = t.Title, ["readOnlyHint"] = t.ReadOnly, ["openWorldHint"] = false },
                });
            }
            return new JsonObject { ["tools"] = tools };
        }

        private JsonObject CallTool(JsonObject parameters)
        {
            string name = DocsTools.Text(parameters, "name");
            Tool tool = Tools.All.FirstOrDefault(t => t.Name == name) ?? throw new McpException(-32602, $"Unknown tool: {name}. tools/list names them.");
            ToolResult result = tool.Run(parameters["arguments"] as JsonObject ?? new JsonObject());
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
                ["isError"] = result.IsError,
            };
        }

        #endregion

        #region Resources

        private JsonObject ListResources()
        {
            var resources = new JsonArray();
            foreach (DocPage page in Docs.Pages)
            {
                resources.Add(new JsonObject
                {
                    ["uri"] = DocsMarkdown.UriOf(page.Path),
                    ["name"] = page.Path == "/" ? "home" : page.Path.Trim('/'),
                    ["title"] = page.Title,
                    ["description"] = page.Description,
                    ["mimeType"] = "text/markdown",
                });
            }
            return new JsonObject { ["resources"] = resources };
        }

        private static JsonObject ListResourceTemplates() => new JsonObject
        {
            ["resourceTemplates"] = new JsonArray(
                new JsonObject { ["uriTemplate"] = GuideTemplate, ["name"] = "guide", ["title"] = "A KScript guide", ["description"] = "A guide by its slug: language, code-format, web, http, testing...", ["mimeType"] = "text/markdown" },
                new JsonObject { ["uriTemplate"] = PackageTemplate, ["name"] = "package", ["title"] = "A KScript package", ["description"] = "A package's reference by its name without KScript.: core, io, math, net...", ["mimeType"] = "text/markdown" }),
        };

        private JsonObject ReadResource(JsonObject parameters)
        {
            string uri = DocsTools.Text(parameters, "uri");
            string path = uri != null && uri.StartsWith(DocsMarkdown.UriPrefix, StringComparison.OrdinalIgnoreCase) ? Docs.Resolve(uri) : null;
            string text = Docs.Page(path, Tools.Format) ?? throw new McpException(-32002, $"Resource not found: {uri}.");
            return new JsonObject
            {
                ["contents"] = new JsonArray(new JsonObject { ["uri"] = uri, ["mimeType"] = "text/markdown", ["text"] = text }),
            };
        }

        #endregion

        #region Transports

        /// <summary>Serves over stdio: one JSON-RPC message per line in, one per line out, until the input ends.</summary>
        public async Task RunStdioAsync(TextReader input, TextWriter output, CancellationToken stop = default)
        {
            string line;
            while ((line = await input.ReadLineAsync(stop)) != null)
            {
                if (line.Trim().Length == 0) continue;
                string answer = HandleLine(line);
                if (answer == null) continue;
                await output.WriteAsync(answer + "\n");
                await output.FlushAsync();
            }
        }

        /// <summary>True for a request a local client sent: no Origin, or a localhost one (so no web page can call it).</summary>
        internal static bool IsLocalOrigin(HttpRequest request)
        {
            string origin = request.Headers.Origin.ToString();
            if (origin.Length == 0) return true;
            return Uri.TryCreate(origin, UriKind.Absolute, out Uri o) && (o.Host == "localhost" || o.Host == "127.0.0.1" || o.Host == "[::1]");
        }

        /// <summary>Answers one HTTP request to the MCP endpoint (Streamable HTTP, JSON responses only).</summary>
        internal async Task HandleHttpAsync(HttpContext http)
        {
            HttpResponse response = http.Response;
            if (http.Request.Path != "/mcp")
            {
                response.StatusCode = 404;
                await response.WriteAsync("The MCP endpoint is /mcp.");
                return;
            }
            if (!IsLocalOrigin(http.Request))
            {
                response.StatusCode = 403;
                await response.WriteAsync("Only local clients can use this server.");
                return;
            }
            if (!HttpMethods.IsPost(http.Request.Method))
            {
                // No server-to-client stream: every answer comes back on the POST it answers.
                response.StatusCode = 405;
                response.Headers.Allow = "POST";
                return;
            }
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();
            string answer = HandleLine(body);
            if (answer == null)
            {
                response.StatusCode = 202;
                return;
            }
            response.ContentType = "application/json";
            await response.WriteAsync(answer, Encoding.UTF8);
        }

        /// <summary>Serves over HTTP on localhost at /mcp until stopped. <paramref name="started"/> gets the endpoint's URL.</summary>
        public async Task RunHttpAsync(int port, CancellationToken stop, Action<string> started = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(o => o.ListenLocalhost(port));
            await using var app = builder.Build();
            app.Run(HandleHttpAsync);
            try
            {
                await app.StartAsync(stop);
                started?.Invoke($"http://localhost:{port}/mcp");
                await app.WaitForShutdownAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Stopped while still starting: a normal stop.
            }
        }

        #endregion
    }
}
