using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using KScript.Runtime;

namespace KScript.Net
{
    /// <summary>A desktop Isolink Connect v1 connection, scoped by the host's interactive app consent.</summary>
    internal sealed class IsolinkSession : IDisposable
    {
        private readonly TcpClient client = new(AddressFamily.InterNetwork);
        private readonly TimeSpan timeout;
        private readonly SemaphoreSlim requests = new(1);
        private readonly JsonLineFrames frames = new();
        private int sequence, closed;

        private IsolinkSession(TimeSpan timeout) => this.timeout = timeout;

        internal Dictionary<string, object> Hello { get; private set; }

        internal static IsolinkSession Connect(string appId, string appName, string token, CancellationToken cancellation)
            => Connect(appId, appName, token, cancellation, 47231, TimeSpan.FromMinutes(2));

        internal static IsolinkSession Connect(string appId, string appName, string token, CancellationToken cancellation, int port, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appName))
                throw new KScriptException("Isolink needs a non-empty app id and name.", "Argument");
            var hello = new Dictionary<string, object>
            {
                ["protocol"] = 1.0,
                ["app"] = new Dictionary<string, object> { ["id"] = appId, ["name"] = appName },
            };
            if (token != null) hello["token"] = token;

            var session = new IsolinkSession(timeout);
            try
            {
                object result = session.WithDeadline(cancellation, ct =>
                {
                    using var dial = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    dial.CancelAfter(TimeSpan.FromSeconds(1));
                    session.client.ConnectAsync(IPAddress.Loopback, port, dial.Token).AsTask().GetAwaiter().GetResult();
                    return session.Exchange("hello", hello, ct);
                });
                if (result is not Dictionary<string, object> info || !Equals(info.GetValueOrDefault("protocol"), 1.0)
                    || !Equals(info.GetValueOrDefault("mode"), "host") || info.GetValueOrDefault("me") is not Dictionary<string, object>
                    || info.GetValueOrDefault("token") is not (null or string))
                    throw new KScriptException("Isolink didn't answer with a version 1 host handshake.", "Network");
                if (info.GetValueOrDefault("token") == null) info["token"] = token;
                session.Hello = info;
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        internal object Request(string method, Dictionary<string, object> parameters, CancellationToken cancellation)
        {
            if (method is not ("status" or "spaces.list" or "spaces.get" or "spaces.open" or
                "records.list" or "records.get" or "records.edit" or "records.delete"))
                throw new KScriptException($"Unknown Isolink operation '{method}'.", "Argument");
            // A single reader owns the socket buffer and consumes each request's matching reply.
            if (!requests.Wait(timeout, cancellation)) throw new KScriptException("Timed out waiting for the pending Isolink request.", "Timeout");
            try
            {
                if (Volatile.Read(ref closed) != 0) throw Disconnected("The Isolink connection is closed.");
                return WithDeadline(cancellation, ct => Exchange(method, parameters, ct));
            }
            finally { requests.Release(); }
        }

        private object WithDeadline(CancellationToken cancellation, Func<CancellationToken, object> operation)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(timeout);
            try { return operation(deadline.Token); }
            catch (OperationCanceledException)
            {
                Dispose();
                cancellation.ThrowIfCancellationRequested();
                throw new KScriptException("The Isolink host didn't answer in time.", "Timeout");
            }
            catch (EndOfStreamException ex)
            {
                Dispose();
                throw Disconnected(ex.Message);
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or JsonException or InvalidDataException or FormatException)
            {
                Dispose();
                throw new KScriptException($"Couldn't communicate with the local Isolink host: {ex.Message}", "Network");
            }
        }

        private static KScriptException Disconnected(string message) =>
            new KScriptException(message, "Network") { Fields = { ["code"] = "not_connected" } };

        private object Exchange(string method, Dictionary<string, object> parameters, CancellationToken ct)
        {
            int id = ++sequence;
            var request = new Dictionary<string, object>
            {
                ["id"] = (double)id, ["m"] = method, ["p"] = parameters ?? new Dictionary<string, object>(),
            };
            NetworkStream stream = client.GetStream();
            JsonLineFrames.Write(stream, request, ct);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                using JsonDocument frame = frames.Read(stream, ct);
                JsonElement root = frame.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected an Isolink frame object.");
                if (root.TryGetProperty("ev", out var ev) && ev.ValueKind == JsonValueKind.String)
                {
                    if (ev.GetString() == "closed") throw new EndOfStreamException("Isolink closed this session.");
                    continue;
                }
                if (!root.TryGetProperty("id", out var replyId) || replyId.ValueKind != JsonValueKind.Number
                    || !replyId.TryGetInt32(out int n) || n != id)
                    throw new InvalidDataException("The Isolink reply didn't match the pending request.");
                if (root.TryGetProperty("e", out var error))
                {
                    if (error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String
                        || !error.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("Isolink sent a malformed error.");
                    throw new KScriptException($"Isolink refused {method}: {message.GetString()}", "Network") { Fields = { ["code"] = code.GetString() } };
                }
                if (root.TryGetProperty("r", out var result)) return Values.FromJson(result);
                throw new InvalidDataException("The Isolink reply is missing its result.");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) == 0) client.Dispose();
        }
    }
}
