using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Net
{
    /// <summary>A callback-scoped connection to a JSON Lines service on IPv4 loopback.</summary>
    internal sealed class LoopbackJsonConnection : IKObject, IDisposable
    {
        private readonly TcpClient client = new(AddressFamily.InterNetwork);
        private readonly JsonLineFrames frames = new();
        private readonly TimeSpan timeout;
        private readonly SemaphoreSlim gate = new(1);
        private readonly AsyncLocal<TransactionStream> current = new();
        private int closed;

        private LoopbackJsonConnection(TimeSpan timeout) => this.timeout = timeout;
        public string TypeName => KType.LoopbackJsonConnection;
        public string ToText() => "local JSON Lines connection";
        public bool TryGetMember(string name, out object value) { value = null; return false; }
        public bool TryCallMethod(CallContext call, string name, out object result) { result = null; return false; }

        internal static void Register(KScriptPackage.PackageBuilder p)
        {
            p.Command("with_loopback_json", "port: int, action: fn, timeout?: duration",
                "Connects to a JSON Lines service on IPv4 loopback, returns action(connection), and closes it. Each transaction has one timeout (2m by default).",
                KType.Any, c =>
                {
                    var action = c.Fn(1);
                    int port = c.Int(0);
                    if (port is < 1 or > 65535) throw c.Error("port must be between 1 and 65535.", "Argument");
                    double seconds = c.Arg(2) == null ? 120 : Duration.Seconds(c.Arg(2), "timeout");
                    if (!(seconds > 0 && seconds <= (uint.MaxValue - 1) / 1000d))
                        throw c.Error("timeout must be a positive duration within the system timer range.", "Argument");
                    using var connection = Connect(port, TimeSpan.FromSeconds(seconds), c.Interpreter.Options.Cancellation);
                    return c.Call(action, connection);
                });
            p.Method(KType.LoopbackJsonConnection, "transaction", "action: fn",
                "Gives action exclusive use of the connection and returns its result. If action throws, the connection closes.",
                KType.Any, (t, c) =>
                {
                    var action = c.Fn(0);
                    return ((LoopbackJsonConnection)t).Transaction(s => c.Call(action, s), c.Interpreter.Options.Cancellation);
                });
            p.Method(KType.LoopbackJsonStream, "send", "frame: map", "Writes one newline-terminated UTF-8 JSON object (up to 256 KiB).",
                KType.Null, (t, c) => { ((TransactionStream)t).Send(c.Map(0)); return null; });
            p.Method(KType.LoopbackJsonStream, "read", "", "Reads one UTF-8 JSON object (up to 256 KiB) within the active transaction's deadline.",
                KType.Map, (t, c) => ((TransactionStream)t).Read());
            p.Examples(("@with_loopback_json", "@with_loopback_json(8765, fn(connection) => $connection->transaction(fn(stream) => $stream->read()))"));
        }

        internal static LoopbackJsonConnection Connect(int port, TimeSpan timeout, CancellationToken cancellation)
        {
            var connection = new LoopbackJsonConnection(timeout);
            try
            {
                using var dial = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                dial.CancelAfter(timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1));
                connection.client.ConnectAsync(IPAddress.Loopback, port, dial.Token).AsTask().GetAwaiter().GetResult();
                return connection;
            }
            catch (OperationCanceledException)
            {
                connection.Dispose();
                cancellation.ThrowIfCancellationRequested();
                throw new KScriptException("Connecting to the local JSON service timed out.", "Timeout");
            }
            catch (SocketException ex)
            {
                connection.Dispose();
                throw new KScriptException($"Couldn't connect to the local JSON service: {ex.Message}", "Network");
            }
        }

        internal object Transaction(Func<TransactionStream, object> action, CancellationToken cancellation)
        {
            CheckOpen();
            if (current.Value?.Active == true) throw new KScriptException("A transaction cannot start another transaction on the same connection.", "Argument");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(timeout);
            try
            {
                gate.Wait(deadline.Token);
                try
                {
                    CheckOpen();
                    var stream = new TransactionStream(this, cancellation, deadline.Token);
                    current.Value = stream;
                    try
                    {
                        object result = action(stream);
                        deadline.Token.ThrowIfCancellationRequested();
                        return result;
                    }
                    catch { Dispose(); throw; }
                    finally { stream.End(); current.Value = null; }
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellation.IsCancellationRequested)
            {
                throw TimedOut();
            }
        }

        private static KScriptException TimedOut() => new("The local JSON transaction timed out.", "Timeout");
        private static KScriptException Disconnected(string message) => new(message, "Network") { Fields = { ["code"] = "not_connected" } };
        private void CheckOpen()
        {
            if (Volatile.Read(ref closed) != 0) throw Disconnected("The local JSON connection is closed.");
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) == 0) client.Dispose();
        }

        internal sealed class TransactionStream(LoopbackJsonConnection owner, CancellationToken cancellation, CancellationToken deadline) : IKObject
        {
            private readonly object lifetime = new();
            private readonly SemaphoreSlim readers = new(1), writers = new(1);
            private int operations;
            private int active = 1;
            internal bool Active => Volatile.Read(ref active) != 0;
            internal void End()
            {
                lock (lifetime)
                {
                    Volatile.Write(ref active, 0);
                    // An unawaited operation must never write to, or consume the reply of, the next transaction.
                    if (operations != 0) owner.Dispose();
                }
            }
            public string TypeName => KType.LoopbackJsonStream;
            public string ToText() => "local JSON transaction stream";
            public bool TryGetMember(string name, out object value) { value = null; return false; }
            public bool TryCallMethod(CallContext call, string name, out object result) { result = null; return false; }

            internal void Send(Dictionary<string, object> frame) =>
                Use(writers, s => { JsonLineFrames.Write(s, frame, deadline); return null; });

            internal object Read() => Use(readers, s =>
            {
                using var frame = owner.frames.Read(s, deadline);
                if (frame.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a JSON object.");
                return Values.FromJson(frame.RootElement);
            });

            private object Use(SemaphoreSlim gate, Func<NetworkStream, object> operation)
            {
                lock (lifetime)
                {
                    if (!Active || owner.current.Value != this)
                        throw Disconnected("The JSON stream can only be used inside its transaction callback.");
                    owner.CheckOpen();
                    operations++;
                }
                try
                {
                    gate.Wait(deadline);
                    try { return operation(owner.client.GetStream()); }
                    finally { gate.Release(); }
                }
                catch (OperationCanceledException)
                {
                    owner.Dispose();
                    cancellation.ThrowIfCancellationRequested();
                    throw TimedOut();
                }
                catch (EndOfStreamException ex)
                {
                    owner.Dispose();
                    throw Disconnected(ex.Message);
                }
                catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or JsonException or InvalidDataException or FormatException)
                {
                    bool closed = Volatile.Read(ref owner.closed) != 0;
                    owner.Dispose();
                    if (closed) throw Disconnected("The local JSON connection is closed.");
                    throw new KScriptException($"The local JSON transaction failed: {ex.Message}", "Network");
                }
                finally { lock (lifetime) operations--; }
            }
        }
    }
}
