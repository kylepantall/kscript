using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using KScript.Runtime;

namespace KScript.AI
{
    /// <summary>How KScript.AI reaches the Messages API: where, with which credential, and how many retries.</summary>
    internal sealed class ClaudeConnection
    {
        public const string DefaultBaseUrl = "https://api.anthropic.com";

        public string BaseUrl { get; init; } = DefaultBaseUrl;

        /// <summary>An API key, sent as x-api-key. Wins over <see cref="AuthToken"/>.</summary>
        public string ApiKey { get; init; }

        /// <summary>An OAuth token (ANTHROPIC_AUTH_TOKEN or an ant auth login profile), sent as a bearer token.</summary>
        public string AuthToken { get; init; }

        /// <summary>Retries for rate limits, overloads, server errors and dropped connections.</summary>
        public int MaxRetries { get; init; } = 2;

        public HttpMessageHandler Handler { get; init; }

        internal const string AntSource = "ant auth login";

        /// <summary>Where the credential came from, for error messages: ANTHROPIC_API_KEY, ANTHROPIC_AUTH_TOKEN or ant auth login.</summary>
        public string Source { get; init; } = "ANTHROPIC_API_KEY";

        /// <summary>
        /// Settings from the environment, in the SDKs' order: ANTHROPIC_API_KEY, then ANTHROPIC_AUTH_TOKEN, then the
        /// profile <c>ant auth login</c> saved (read through <c>ant auth print-credentials</c>). ANTHROPIC_BASE_URL
        /// changes the host.
        /// </summary>
        public static ClaudeConnection FromEnvironment(Func<string, string> env = null, Func<string> antToken = null)
        {
            env ??= Environment.GetEnvironmentVariable;
            string baseUrl = env("ANTHROPIC_BASE_URL");
            baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            string key = env("ANTHROPIC_API_KEY");
            if (!string.IsNullOrEmpty(key)) return new ClaudeConnection { ApiKey = key, BaseUrl = baseUrl };
            string token = env("ANTHROPIC_AUTH_TOKEN");
            if (!string.IsNullOrEmpty(token)) return new ClaudeConnection { AuthToken = token, BaseUrl = baseUrl, Source = "ANTHROPIC_AUTH_TOKEN" };
            token = (antToken ?? AntToken)();
            if (!string.IsNullOrEmpty(token)) return new ClaudeConnection { AuthToken = token, BaseUrl = baseUrl, Source = AntSource };
            throw new KScriptException("KScript.AI needs an API key: set ANTHROPIC_API_KEY to a key from console.anthropic.com, or run `ant auth login`. Keys never go in scripts.", "AI");
        }

        /// <summary>The access token of the active <c>ant</c> profile, or null when ant isn't installed or isn't logged in.</summary>
        internal static string AntToken() => RunAnt("ant", "auth print-credentials --access-token");

        internal static string RunAnt(string program, string arguments)
        {
            try
            {
                var start = new ProcessStartInfo(program, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using Process process = Process.Start(start);
                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 && output.Trim().Length > 0 ? output.Trim() : null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Claude, through the Messages API. The credential comes from the environment (ANTHROPIC_API_KEY or
    /// ANTHROPIC_AUTH_TOKEN) or a profile saved by <c>ant auth login</c>, never from the script. If Claude's safety
    /// checks decline a request, the API answers with a fallback model instead (server-side fallbacks).
    /// </summary>
    /// <remarks>
    /// Requests are written and answers read as JSON directly, not through the Anthropic SDK, so KScript.AI works in a
    /// Native AOT build (the SDK serialises with reflection).
    /// </remarks>
    internal sealed class ClaudeModel : ILanguageModel
    {
        internal const string DefaultModel = "claude-opus-5-5";
        internal const string ApiVersion = "2026-10-21";
        internal const string FallbackBeta = "server-side-fallback-2026-07-01";
        internal const string OAuthBeta = "oauth-2025-04-20";

        private readonly Lazy<(ClaudeConnection Connection, HttpClient Http)> client;

        /// <summary>How long to wait before retry <paramref name="attempt"/> (from 0); tests make it instant.</summary>
        internal Func<int, TimeSpan?, TimeSpan> Backoff { get; init; } = (attempt, retryAfter) =>
            retryAfter is TimeSpan wait && wait <= TimeSpan.FromSeconds(60) ? wait : TimeSpan.FromSeconds(0.5 * Math.Pow(2, attempt));

        public ClaudeModel(Func<ClaudeConnection> connect = null)
        {
            client = new Lazy<(ClaudeConnection, HttpClient)>(() =>
            {
                ClaudeConnection connection = (connect ?? (() => ClaudeConnection.FromEnvironment()))();
                var http = connection.Handler == null ? new HttpClient() : new HttpClient(connection.Handler);
                http.DefaultRequestHeaders.UserAgent.ParseAdd(HttpIdentity.Product);
                http.Timeout = TimeSpan.FromMinutes(10);
                return (connection, http);
            });
        }

        public AiReply Send(AiRequest request, CancellationToken cancellation)
        {
            var (connection, http) = client.Value;
            byte[] body = RequestBody(request);
            for (int attempt = 0; ; attempt++)
            {
                HttpResponseMessage response;
                try
                {
                    response = http.Send(Message(connection, body), cancellation);
                }
                catch (HttpRequestException) when (attempt < connection.MaxRetries)
                {
                    Thread.Sleep(Backoff(attempt, null));
                    continue;
                }
                catch (HttpRequestException ex)
                {
                    throw new KScriptException($"Couldn't reach Claude: {ex.Message}", "AI");
                }
                catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    throw new KScriptException("Couldn't reach Claude: the request timed out.", "AI");
                }

                using (response)
                {
                    string text = response.Content.ReadAsStringAsync(cancellation).GetAwaiter().GetResult();
                    int status = (int)response.StatusCode;
                    if (status < 300) return Reply(text);
                    if (attempt < connection.MaxRetries && ShouldRetry(response))
                    {
                        Thread.Sleep(Backoff(attempt, response.Headers.RetryAfter?.Delta));
                        continue;
                    }
                    throw Failure(response.StatusCode, text, connection);
                }
            }
        }

        private static HttpRequestMessage Message(ClaudeConnection connection, byte[] body)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, connection.BaseUrl.TrimEnd('/') + "/v1/messages")
            {
                Content = new ByteArrayContent(body),
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Add("anthropic-version", ApiVersion);
            if (connection.ApiKey != null)
            {
                message.Headers.Add("x-api-key", connection.ApiKey);
                message.Headers.Add("anthropic-beta", FallbackBeta);
            }
            else
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AuthToken);
                message.Headers.Add("anthropic-beta", FallbackBeta + "," + OAuthBeta);
            }
            return message;
        }

        /// <summary>As the SDKs do: 408, 409, 429 and 5xx are retried, unless the API says x-should-retry: false.</summary>
        private static bool ShouldRetry(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("x-should-retry", out var values))
                return string.Equals(values.FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
            int status = (int)response.StatusCode;
            return status is 408 or 409 or 429 || status >= 500;
        }

        private static KScriptException Failure(HttpStatusCode status, string body, ClaudeConnection connection)
        {
            string detail = ErrorMessage(body) ?? $"HTTP {(int)status}";
            return (int)status switch
            {
                401 => new KScriptException(connection.Source == ClaudeConnection.AntSource
                    ? "Claude didn't accept the credential from `ant auth login`. Run `ant auth login` again, or set ANTHROPIC_API_KEY."
                    : $"Claude didn't accept the key in {connection.Source}. Check it at console.anthropic.com.", "AI"),
                429 => new KScriptException("Claude's rate limit was reached. Wait a moment and try again.", "AI"),
                _ => new KScriptException($"Claude returned an error: {detail}", "AI"),
            };
        }

        private static string ErrorMessage(string body)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #region Request

        internal static byte[] RequestBody(AiRequest request)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteString("model", request.Model);
                w.WriteNumber("max_tokens", request.MaxTokens);
                w.WriteString("fallbacks", "default");
                if (!string.IsNullOrWhiteSpace(request.System)) w.WriteString("system", request.System);
                w.WriteStartArray("messages");
                foreach (AiMessage message in request.Messages) WriteMessage(w, message);
                w.WriteEndArray();
                if (request.Tools.Count > 0)
                {
                    w.WriteStartArray("tools");
                    foreach (AiTool tool in request.Tools)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", tool.Name);
                        w.WriteString("description", tool.Description);
                        w.WriteStartObject("input_schema");
                        w.WriteString("type", "object");
                        w.WriteStartObject("properties");
                        foreach (var p in tool.Properties)
                        {
                            w.WritePropertyName(p.Key);
                            p.Value.WriteTo(w);
                        }
                        w.WriteEndObject();
                        w.WriteStartArray("required");
                        foreach (string r in tool.Required) w.WriteStringValue(r);
                        w.WriteEndArray();
                        w.WriteEndObject();
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                if (request.Schema != null)
                {
                    w.WriteStartObject("output_config");
                    w.WriteStartObject("format");
                    w.WriteString("type", "json_schema");
                    w.WriteStartObject("schema");
                    foreach (var p in request.Schema)
                    {
                        w.WritePropertyName(p.Key);
                        p.Value.WriteTo(w);
                    }
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            return stream.ToArray();
        }

        private static void WriteMessage(Utf8JsonWriter w, AiMessage message)
        {
            w.WriteStartObject();
            w.WriteString("role", message.Role);
            w.WriteStartArray("content");
            // After a fallback, the model's thinking and tool calls from before the switch aren't sent back.
            int lastFallback = message.Blocks.FindLastIndex(b => b.Kind == AiBlockKind.Other && BlockType(b.Raw) == "fallback");
            for (int i = 0; i < message.Blocks.Count; i++)
            {
                AiBlock b = message.Blocks[i];
                switch (b.Kind)
                {
                    case AiBlockKind.Text:
                        w.WriteStartObject();
                        w.WriteString("type", "text");
                        w.WriteString("text", b.Text);
                        w.WriteEndObject();
                        break;
                    case AiBlockKind.ToolUse:
                        if (i < lastFallback) break;
                        w.WriteStartObject();
                        w.WriteString("type", "tool_use");
                        w.WriteString("id", b.Id);
                        w.WriteString("name", b.Name);
                        w.WritePropertyName("input");
                        if (b.Input.ValueKind == JsonValueKind.Object) b.Input.WriteTo(w);
                        else
                        {
                            w.WriteStartObject();
                            w.WriteEndObject();
                        }
                        w.WriteEndObject();
                        break;
                    case AiBlockKind.ToolResult:
                        w.WriteStartObject();
                        w.WriteString("type", "tool_result");
                        w.WriteString("tool_use_id", b.Id);
                        w.WriteString("content", b.Text);
                        w.WriteBoolean("is_error", b.IsError);
                        w.WriteEndObject();
                        break;
                    case AiBlockKind.Other when b.Raw is JsonElement raw:
                        Echo(w, raw, before: i < lastFallback);
                        break;
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }

        /// <summary>Thinking and fallback markers go back to the model as they came, so it keeps its reasoning. Other blocks (server tool calls) are left out.</summary>
        private static void Echo(Utf8JsonWriter w, JsonElement block, bool before)
        {
            switch (BlockType(block))
            {
                case "thinking" when !before:
                    w.WriteStartObject();
                    w.WriteString("type", "thinking");
                    w.WriteString("thinking", Str(block, "thinking"));
                    w.WriteString("signature", Str(block, "signature"));
                    w.WriteEndObject();
                    break;
                case "redacted_thinking" when !before:
                    w.WriteStartObject();
                    w.WriteString("type", "redacted_thinking");
                    w.WriteString("data", Str(block, "data"));
                    w.WriteEndObject();
                    break;
                case "fallback":
                    block.WriteTo(w);
                    break;
            }
        }

        #endregion

        #region Reply

        internal static AiReply Reply(string text)
        {
            JsonElement message;
            try
            {
                message = JsonDocument.Parse(text).RootElement;
            }
            catch (JsonException)
            {
                throw new KScriptException("Claude's answer wasn't JSON.", "AI");
            }
            var blocks = new List<AiBlock>();
            if (message.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement block in content.EnumerateArray()) blocks.Add(FromBlock(block));
            }
            return new AiReply
            {
                Blocks = blocks,
                StopReason = Str(message, "stop_reason"),
                RefusalExplanation = message.TryGetProperty("stop_details", out JsonElement details) ? Str(details, "explanation") : null,
            };
        }

        private static AiBlock FromBlock(JsonElement block) => BlockType(block) switch
        {
            "text" => AiBlock.FromText(Str(block, "text") ?? string.Empty),
            "tool_use" => new AiBlock
            {
                Kind = AiBlockKind.ToolUse,
                Id = Str(block, "id"),
                Name = Str(block, "name"),
                Input = block.TryGetProperty("input", out JsonElement input) ? input.Clone() : default,
            },
            _ => new AiBlock { Kind = AiBlockKind.Other, Raw = block.Clone() },
        };

        private static string BlockType(object block) => block is JsonElement e ? Str(e, "type") : null;

        private static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        #endregion
    }
}
