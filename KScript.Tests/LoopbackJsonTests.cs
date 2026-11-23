using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using KScript.Net;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class LoopbackJsonTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static async Task WithPeer(Func<NetworkStream, Task> serve, Action<int> use)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(Limit);
        async Task Peer()
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = client.GetStream();
            await serve(stream);
        }
        await Task.WhenAll(Peer(), Task.Run(() => use(port))).WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task KScriptCallbacksSendReadAndReturnResults()
    {
        await WithPeer(async stream =>
        {
            using var reader = new StreamReader(stream);
            using var sent = JsonDocument.Parse(await reader.ReadLineAsync());
            Assert.Equal("café", sent.RootElement.GetProperty("text").GetString());
            await stream.WriteAsync("{\"first\":1}\n{\"second\":2}\n"u8.ToArray());
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            var output = new StringWriter();
            new Interpreter(KScriptHost.ForText(output)).RunText($$"""
                import KScript.Net
                fn exchange(stream) {
                  $stream->send({ text: 'café' })
                  first = $stream->read()
                  second = $stream->read()
                  return $first.first + $second.second
                }
                fn use_connection(connection) => $connection->transaction(@exchange)
                echo @with_loopback_json({{port}}, @use_connection, timeout: 5s)
                """, "client.kscript");
            Assert.Equal("3", output.ToString().Trim());
        });
    }

    [Fact]
    public async Task StreamsExpireAndAbortedTransactionsCloseTheirConnection()
    {
        await WithPeer(async stream => Assert.Equal(0, await stream.ReadAsync(new byte[1])), port =>
        {
            using var connection = LoopbackJsonConnection.Connect(port, Limit, default);
            var expired = (LoopbackJsonConnection.TransactionStream)connection.Transaction(stream => stream, default);
            Assert.Equal("not_connected", Assert.Throws<KScriptException>(() => expired.Read()).Fields["code"]);
            connection.Transaction(stream =>
            {
                var nested = Assert.Throws<KScriptException>(() => connection.Transaction(_ => null, default));
                Assert.Equal("Argument", nested.Type);
                return null;
            }, default);
            var failure = new KScriptException("Rejected frame");
            Assert.Same(failure, Assert.Throws<KScriptException>(() => connection.Transaction(_ => throw failure, default)));
            Assert.Equal("not_connected", Assert.Throws<KScriptException>(() => connection.Transaction(_ => 1, default)).Fields["code"]);
        });
    }

    [Theory]
    [InlineData("eof")]
    [InlineData("array")]
    [InlineData("bad UTF-8")]
    [InlineData("oversized")]
    public async Task InvalidFramesCloseTheConnection(string problem)
    {
        await WithPeer(async stream =>
        {
            byte[] body = problem switch
            {
                "eof" => "{\"partial\":"u8.ToArray(),
                "array" => "[1]\n"u8.ToArray(),
                "bad UTF-8" => new byte[] { 123, 34, 97, 34, 58, 34, 255, 34, 125, 10 },
                _ => Encoding.UTF8.GetBytes(new string(' ', 256 * 1024 + 1) + "\n"),
            };
            await stream.WriteAsync(body);
        }, port =>
        {
            using var connection = LoopbackJsonConnection.Connect(port, Limit, default);
            var ex = Assert.Throws<KScriptException>(() => connection.Transaction(s => s.Read(), default));
            Assert.Equal("Network", ex.Type);
            if (problem == "eof") Assert.Equal("not_connected", ex.Fields["code"]);
            Assert.Equal("not_connected", Assert.Throws<KScriptException>(() => connection.Transaction(_ => null, default)).Fields["code"]);
        });
    }

    [Fact]
    public async Task OversizedUtf8FramesLeaveNoPartialLine()
    {
        await WithPeer(async stream =>
        {
            using var reader = new StreamReader(stream);
            Assert.Equal("{\"ok\":true}", await reader.ReadLineAsync());
            await stream.WriteAsync("{\"ok\":true}\n"u8.ToArray());
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            using var connection = LoopbackJsonConnection.Connect(port, Limit, default);
            connection.Transaction(stream =>
            {
                var ex = Assert.Throws<KScriptException>(() => stream.Send(new() { ["text"] = new string('あ', 90_000) }));
                Assert.Equal("Argument", ex.Type);
                // JSON Lines is full duplex, including when a callback uses a worker to receive its reply.
                var reply = Task.Run(stream.Read);
                stream.Send(new() { ["ok"] = true });
                Assert.Equal(true, Assert.IsType<Dictionary<string, object>>(reply.GetAwaiter().GetResult())["ok"]);
                return null;
            }, default);
        });
    }

    [Fact]
    public async Task AllFramesInATransactionShareOneDeadline()
    {
        await WithPeer(async stream =>
        {
            await stream.WriteAsync("{}\n{}\n"u8.ToArray());
            Assert.Equal(0, await stream.ReadAsync(new byte[1]));
        }, port =>
        {
            using var connection = LoopbackJsonConnection.Connect(port, TimeSpan.FromMilliseconds(150), default);
            var ex = Assert.Throws<KScriptException>(() => connection.Transaction(stream =>
            {
                stream.Read();
                Thread.Sleep(300);
                return stream.Read(); // The second frame is already in the connection's buffer.
            }, default));
            Assert.Equal("Timeout", ex.Type);
            Assert.Equal("not_connected", Assert.Throws<KScriptException>(() => connection.Transaction(_ => null, default)).Fields["code"]);
        });
    }

    [Fact]
    public async Task ConcurrentCallsUseSeparateTransactionsAndCancelAnActiveRead()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var stop = new CancellationTokenSource();
        await WithPeer(async stream =>
        {
            using var reader = new StreamReader(stream);
            Assert.Equal("{\"id\":1}", await reader.ReadLineAsync());
            stop.Cancel();
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            using var connection = LoopbackJsonConnection.Connect(port, Limit, default);
            var first = Task.Run(() => connection.Transaction(_ =>
            {
                entered.Set();
                Assert.True(release.Wait(Limit));
                return "done";
            }, default));
            Assert.True(entered.Wait(Limit));
            try
            {
                using var queued = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                Assert.ThrowsAny<OperationCanceledException>(() => connection.Transaction(_ => "too early", queued.Token));
            }
            finally { release.Set(); }
            Assert.Equal("done", first.GetAwaiter().GetResult());
            Assert.ThrowsAny<OperationCanceledException>(() => connection.Transaction(stream =>
            {
                stream.Send(new() { ["id"] = 1d });
                return stream.Read();
            }, stop.Token));
            Assert.Equal("not_connected", Assert.Throws<KScriptException>(() => connection.Transaction(_ => null, default)).Fields["code"]);
        });
    }
}
