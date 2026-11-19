using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KScript.LanguageServer
{
    /// <summary>
    /// JSON-RPC 2.0 framing as used by the Language Server Protocol: each message is a <c>Content-Length</c>
    /// header, a blank line and a UTF-8 JSON body.
    /// </summary>
    public sealed class JsonRpcConnection
    {
        private readonly Stream input;
        private readonly Stream output;
        private readonly object writeLock = new object();
        private readonly byte[] one = new byte[1];

        public JsonRpcConnection(Stream input, Stream output)
        {
            this.input = input;
            this.output = output;
        }

        /// <summary>Reads the next message, or returns null at the end of the input.</summary>
        public async Task<JsonObject> ReadAsync(CancellationToken cancellation = default)
        {
            int contentLength = -1;
            while (true)
            {
                string line = await ReadHeaderLineAsync(cancellation).ConfigureAwait(false);
                if (line == null)
                {
                    return null;
                }
                if (line.Length == 0)
                {
                    if (contentLength >= 0)
                    {
                        break;
                    }
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon > 0 && line.Substring(0, colon).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line.Substring(colon + 1).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            byte[] body = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int n = await input.ReadAsync(body.AsMemory(read, contentLength - read), cancellation).ConfigureAwait(false);
                if (n == 0)
                {
                    return null;
                }
                read += n;
            }

            return JsonNode.Parse(body) as JsonObject;
        }

        private async Task<string> ReadHeaderLineAsync(CancellationToken cancellation)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                int n = await input.ReadAsync(one.AsMemory(0, 1), cancellation).ConfigureAwait(false);
                if (n == 0)
                {
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                char c = (char)one[0];
                if (c == '\n')
                {
                    return sb.ToString().TrimEnd('\r');
                }
                sb.Append(c);
            }
        }

        public void Write(JsonNode message)
        {
            byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
            byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            lock (writeLock)
            {
                output.Write(header, 0, header.Length);
                output.Write(body, 0, body.Length);
                output.Flush();
            }
        }

        public void SendResponse(JsonNode id, JsonNode result) =>
            Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result });

        public void SendError(JsonNode id, int code, string message) =>
            Write(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
            });

        public void SendNotification(string method, JsonNode parameters) =>
            Write(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });
    }

    public static class ErrorCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
        public const int ServerNotInitialized = -32002;
        public const int RequestFailed = -32803;
    }
}
