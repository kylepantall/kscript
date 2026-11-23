using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KScript.Net;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class IsolinkSessionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    // An independent v1 peer reads and writes the documented JSON lines on an ephemeral loopback port.
    private static async Task WithHost(Func<StreamReader, StreamWriter, Task> serve, Action<int> use)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var lifetime = new CancellationTokenSource(Deadline);
        async Task Host()
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(lifetime.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            await serve(reader, writer);
        }
        await Task.WhenAll(Host(), Task.Run(() => use(port))).WaitAsync(lifetime.Token);
    }

    private static async Task<JsonElement> ReadRequest(StreamReader reader, string method)
    {
        using JsonDocument doc = JsonDocument.Parse(await reader.ReadLineAsync());
        Assert.Equal(method, doc.RootElement.GetProperty("m").GetString());
        return doc.RootElement.Clone();
    }

    private static IsolinkSession Open(int port, string token = null, TimeSpan? timeout = null) =>
        IsolinkSession.Connect("example.kscript", "Test App", token, CancellationToken.None, port, timeout ?? Deadline);

    private sealed class LoopbackNetPackage(int port) : KScriptPackage
    {
        public override string Name => "KScript.Net";
        public override string Description => "Connect to the test listener.";
        protected override void Register(PackageBuilder p) =>
            IsolinkValue.Register(p, (id, name, token, ct) => IsolinkSession.Connect(id, name, token, ct, port, Deadline));
    }

    private static void RunImported(int port, StringWriter output, string program)
    {
        var registry = new PackageRegistry();
        registry.Register(new LoopbackNetPackage(port));
        new Interpreter(KScriptHost.ForText(output), registry).RunText(
            "import \"isolink-connect/index.kscript\" as link\n" + program,
            ScriptRunner.RepoFile("libraries", "isolink-connect", "libraries", "client.kscript"));
    }

    private static async Task Hello(StreamReader reader, StreamWriter writer, string token = null, bool issueToken = true)
    {
        JsonElement frame = await ReadRequest(reader, "hello");
        Assert.Equal(1, frame.GetProperty("id").GetInt32());
        JsonElement parameters = frame.GetProperty("p");
        Assert.Equal(1, parameters.GetProperty("protocol").GetInt32());
        Assert.Equal("example.kscript", parameters.GetProperty("app").GetProperty("id").GetString());
        Assert.Equal("Test App", parameters.GetProperty("app").GetProperty("name").GetString());
        Assert.False(parameters.TryGetProperty("capabilities", out _));
        if (token == null) Assert.False(parameters.TryGetProperty("token", out _));
        else Assert.Equal(token, parameters.GetProperty("token").GetString());
        JsonNode reply = JsonNode.Parse("""
            {"id":1,"r":{"protocol":1,"mode":"host","me":{"handle":"h_00000000000000000000000000000001","name":"Joan","me":true},"capabilities":[]}}
            """);
        if (issueToken) reply["r"]["token"] = "renewed";
        await writer.WriteLineAsync(reply.ToJsonString());
    }

    [Fact]
    public async Task ConnectsWithConsentAndMatchesRepliesBetweenEvents()
    {
        await WithHost(async (reader, writer) =>
        {
            await Hello(reader, writer, "remembered");
            JsonElement edit = await ReadRequest(reader, "records.edit");
            Assert.Equal("rent", edit.GetProperty("p").GetProperty("id").GetString());
            await writer.WriteAsync("{\"ev\":\"records.changed\",\"p\":{}}\n{\"id\":");
            await writer.WriteLineAsync("""2,"r":{"id":"rent","type":"bill","fields":{"1":"Rent"}}}""");
            await ReadRequest(reader, "records.get");
            await writer.WriteLineAsync("""{"id":3,"e":{"code":"unknown_space","message":"Unknown space."}}""");
            await ReadRequest(reader, "spaces.list");
            await writer.WriteLineAsync("""{"id":4,"r":[]}""");
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            using var session = Open(port, "remembered");
            Assert.Equal("renewed", session.Hello["token"]);
            var record = new Dictionary<string, object> { ["space"] = "home", ["type"] = "bill", ["id"] = "rent", ["edits"] = new List<object>() };
            var result = Assert.IsType<Dictionary<string, object>>(session.Request("records.edit", record, default));
            Assert.Equal("rent", result["id"]);
            var error = Assert.Throws<KScriptException>(() => session.Request("records.get", record, default));
            Assert.Equal("unknown_space", error.Fields["code"]);
            Assert.Throws<KScriptException>(() => session.Request("contacts.list", null, default));
            Assert.Empty(Assert.IsType<List<object>>(session.Request("spaces.list", null, default)));
        });
    }

    [Theory]
    [InlineData("""{"id":1,"e":{"code":"denied","message":"Access denied."}}""")]
    [InlineData("""{"id":1,"r":{"protocol":2,"mode":"host"}}""")]
    public async Task RejectedHandshakesCloseTheConnection(string reply)
    {
        await WithHost(async (reader, writer) =>
        {
            await ReadRequest(reader, "hello");
            await writer.WriteLineAsync(reply);
            Assert.Null(await reader.ReadLineAsync());
        }, port => Assert.Equal("Network", Assert.Throws<KScriptException>(() => Open(port)).Type));
    }

    [Theory]
    [InlineData("""{"ev":"closed","p":{"reason":"turned_off"}}""")]
    [InlineData("""{"id":123,"r":null}""")]
    [InlineData("oversized")]
    public async Task ClosedMissingAndOversizedRepliesInvalidateTheSession(string reply)
    {
        await WithHost(async (reader, writer) =>
        {
            await Hello(reader, writer);
            await ReadRequest(reader, "status");
            if (reply == "oversized") await writer.WriteAsync(new string('x', 256 * 1024 + 1));
            else await writer.WriteLineAsync(reply);
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            using var session = Open(port);
            Assert.Equal("Network", Assert.Throws<KScriptException>(() => session.Request("status", null, default)).Type);
            KScriptException closed = Assert.Throws<KScriptException>(() => session.Request("status", null, default));
            Assert.Equal("not_connected", closed.Fields["code"]);
        });
    }

    [Fact]
    public async Task StalledRepliesTimeOutAndDisconnect()
    {
        await WithHost(async (reader, writer) =>
        {
            await Hello(reader, writer);
            await ReadRequest(reader, "status");
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            using var session = Open(port, timeout: TimeSpan.FromSeconds(1));
            Assert.Equal("Timeout", Assert.Throws<KScriptException>(() => session.Request("status", null, default)).Type);
        });
    }

    [Fact]
    public async Task CancellingAnInFlightRequestDisconnectsTheClient()
    {
        using var cancellation = new CancellationTokenSource();
        await WithHost(async (reader, writer) =>
        {
            await Hello(reader, writer);
            await ReadRequest(reader, "status");
            cancellation.Cancel();
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            using var session = Open(port);
            Assert.ThrowsAny<OperationCanceledException>(() => session.Request("status", null, cancellation.Token));
            Assert.Equal("Network", Assert.Throws<KScriptException>(() => session.Request("status", null, default)).Type);
        });
    }

    [Fact]
    public async Task OversizedRequestsAreRejectedBeforeSending()
    {
        await WithHost(async (reader, writer) =>
        {
            await Hello(reader, writer);
            JsonElement status = await ReadRequest(reader, "status");
            await writer.WriteLineAsync($$"""{"id":{{status.GetProperty("id").GetInt32()}},"r":true}""");
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            using var session = Open(port);
            var fields = new Dictionary<string, object> { ["id"] = new string('あ', 90_000) };
            Assert.Equal("Argument", Assert.Throws<KScriptException>(() => session.Request("records.edit", fields, default)).Type);
            Assert.Equal(true, session.Request("status", null, default));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedLibraryOffersRecordOperationsAndClosesAfterTheCallback(bool fail)
    {
        await WithHost(async (reader, writer) =>
        {
            await Hello(reader, writer, fail ? null : "saved");
            await ReadRequest(reader, "status");
            await writer.WriteLineAsync("""{"id":2,"r":{"running":true,"reachable":true}}""");
            JsonElement space = (await ReadRequest(reader, "spaces.open")).GetProperty("p");
            Assert.Equal("home", space.GetProperty("key").GetString());
            Assert.Equal("Home", space.GetProperty("name").GetString());
            await writer.WriteLineAsync("""{"id":3,"r":{"id":"space1","name":"Home"}}""");
            JsonElement record = (await ReadRequest(reader, "records.edit")).GetProperty("p");
            Assert.Equal("space1", record.GetProperty("space").GetString());
            Assert.Equal("note", record.GetProperty("type").GetString());
            Assert.Equal("one", record.GetProperty("id").GetString());
            JsonElement edit = Assert.Single(record.GetProperty("edits").EnumerateArray());
            Assert.Equal("setValue", edit.GetProperty("op").GetString());
            Assert.Equal(1, edit.GetProperty("field").GetInt32());
            Assert.Equal("hello", edit.GetProperty("value").GetString());
            await writer.WriteLineAsync("""{"id":4,"r":{"id":"one","type":"note","fields":{"1":"hello"}}}""");
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            using var output = new StringWriter { NewLine = "\n" };
            string program = $$"""
                fn run(session) {
                  status = @link.status($session)
                  echo $status.running
                  echo $session.token
                  space = @link.open_space($session, 'home', 'Home')
                  note = @link.edit_record($session, $space.id, 'note', 'one', [@link.set_value(1, 'hello')])
                  {{(fail ? "throw Validation \"callback failed\"" : "return $note.fields['1']")}}
                }
                echo @link.with_session('example.kscript', 'Test App', @run{{(fail ? "" : ", token: 'saved'")}})
                """;
            if (fail)
            {
                Assert.Contains("callback failed", Assert.Throws<KScriptException>(() => RunImported(port, output, program)).Message);
                Assert.Equal("true\nrenewed\n", output.ToString());
            }
            else
            {
                RunImported(port, output, program);
                Assert.Equal("true\nrenewed\nhello\n", output.ToString());
            }
        });
    }

    [Fact]
    public async Task ImportedLibraryReadsFiltersAndDeletesAppRecords()
    {
        await WithHost(async (reader, writer) =>
        {
            await Hello(reader, writer, "saved", issueToken: false);
            async Task Reply(string method, string parameters, string result)
            {
                JsonElement frame = await ReadRequest(reader, method);
                string actual = frame.GetProperty("p").GetRawText();
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(parameters), JsonNode.Parse(actual)), $"{method} received {actual}");
                await writer.WriteLineAsync($$"""{"id":{{frame.GetProperty("id").GetInt32()}},"r":{{result}}}""");
            }
            const string space = """{"id":"space1","name":"Home","key":"home","people":[],"admin":true}""";
            const string record = """{"id":"one","type":"note","fields":{"2":3,"3":["done"]}}""";
            await Reply("spaces.list", "{}", $"[{space}]");
            await Reply("spaces.get", """{"space":"space1"}""", space);
            await Reply("records.list", """{"space":"space1"}""", $"[{record}]");
            await Reply("records.list", """{"space":"space1","type":"task"}""", "[]");
            await Reply("records.get", """{"space":"space1","id":"missing"}""", "null");
            await Reply("records.edit", """
                {"space":"space1","type":"note","id":"one","edits":[{"op":"addToCounter","field":2,"by":3},
                {"op":"addToSet","field":3,"items":["done"]},{"op":"removeFromSet","field":3,"items":["draft"]}]}
                """, record);
            await Reply("records.delete", """{"space":"space1","id":"one"}""", "null");
            Assert.Null(await reader.ReadLineAsync());
        }, port =>
        {
            using var output = new StringWriter { NewLine = "\n" };
            RunImported(port, output, """
                fn run(session) {
                  echo $session.token
                  spaces = @link.list_spaces($session)
                  space = @link.get_space($session, $spaces[0].id)
                  echo $space.name
                  all = @link.list_records($session, $space.id)
                  echo $all->count()
                  tasks = @link.list_records($session, $space.id, type: 'task')
                  echo $tasks->count()
                  missing = @link.get_record($session, $space.id, 'missing')
                  echo $missing == null
                  edits = [@link.add_to_counter(2, 3), @link.add_to_set(3, ['done']), @link.remove_from_set(3, ['draft'])]
                  note = @link.edit_record($session, $space.id, 'note', 'one', $edits)
                  echo $note.fields['2']
                  removed = @link.delete_record($session, $space.id, $note.id)
                  return $removed == null
                }
                echo @link.with_session('example.kscript', 'Test App', @run, token: 'saved')
                """);
            Assert.Equal("saved\nHome\n1\n0\ntrue\n3\ntrue\n", output.ToString());
        });
    }
}
