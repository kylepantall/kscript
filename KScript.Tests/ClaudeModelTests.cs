using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using KScript.AI;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>The Claude adapter, against a local stand-in for the Messages API. No real requests are made.</summary>
public sealed class ClaudeModelTests : IDisposable
{
    private readonly Queue<(int Status, string Body)> answers = new Queue<(int, string)>();
    private readonly TestServer server;

    public ClaudeModelTests()
    {
        server = new TestServer((request, body) =>
        {
            lock (answers)
            {
                var (status, text) = answers.Count > 0 ? answers.Dequeue() : (500, Error("api_error", "No answer queued."));
                return (status, text, "application/json");
            }
        });
    }

    public void Dispose() => server.Dispose();

    private ClaudeModel Model(string source = "ANTHROPIC_API_KEY") => new ClaudeModel(() => new ClaudeConnection { ApiKey = source == "ANTHROPIC_API_KEY" ? "test-key" : null, AuthToken = source == "ANTHROPIC_API_KEY" ? null : "token", Source = source, BaseUrl = server.Url, MaxRetries = 0 });

    private ClaudeModelTests Answer(params string[] blocks) => Stopped("end_turn", null, blocks);

    private ClaudeModelTests Stopped(string stop, string stopDetails, params string[] blocks)
    {
        string details = stopDetails == null ? string.Empty : $", \"stop_details\": {stopDetails}";
        answers.Enqueue((200, $"{{ \"id\": \"msg_1\", \"type\": \"message\", \"role\": \"assistant\", \"model\": \"claude-opus-5-5\", \"content\": [{string.Join(", ", blocks)}], \"stop_reason\": \"{stop}\", \"stop_sequence\": null{details}, \"usage\": {{ \"input_tokens\": 1, \"output_tokens\": 1 }} }}"));
        return this;
    }

    private static string Text(string text) => $"{{ \"type\": \"text\", \"text\": {JsonSerializer.Serialize(text)} }}";

    private static string Error(string type, string message) => $"{{ \"type\": \"error\", \"error\": {{ \"type\": \"{type}\", \"message\": \"{message}\" }} }}";

    private string Run(string body, ClaudeModel model = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        host.Items[AIPackage.ModelKey] = model ?? Model();
        new Interpreter(host, options: new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) })
            .RunText("<kscript><import package=\"KScript.AI\" />" + body + "</kscript>");
        return output.ToString();
    }

    private KScriptException Fails(string body, ClaudeModel model = null) => Assert.Throws<KScriptException>(() => Run(body, model));

    private JsonElement Sent(int i) => JsonDocument.Parse(server.Requests[i].Body).RootElement;

    [Fact]
    public void Sends_the_prompt_system_schema_and_fallbacks()
    {
        Answer(Text("{\"city\": \"Paris\"}"));
        string output = Run("<echo>{ @llm('Capital of France?', system: 'Be brief.', schema: { city: 'string' }, max_tokens: 50).city }</echo>");
        Assert.Equal("Paris\n", output);

        var (method, path, _, headers) = server.Requests[0];
        Assert.Equal("POST", method);
        Assert.StartsWith("/v1/messages", path);
        Assert.Contains("server-side-fallback-2026-07-01", headers["anthropic-beta"]);
        Assert.Equal("test-key", headers["x-api-key"]);
        Assert.Equal(ClaudeModel.ApiVersion, headers["anthropic-version"]);
        JsonElement sent = Sent(0);
        Assert.Equal("claude-opus-5-5", sent.GetProperty("model").GetString());
        Assert.Equal(50, sent.GetProperty("max_tokens").GetInt32());
        Assert.Equal("default", sent.GetProperty("fallbacks").GetString());
        Assert.Equal("Be brief.", sent.GetProperty("system").GetString());
        Assert.Equal("json_schema", sent.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("Capital of France?", sent.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.False(sent.TryGetProperty("tools", out _));
    }

    [Fact]
    public void Tool_calls_and_results_go_back_and_forth()
    {
        Stopped("tool_use", null, Text("Checking."), "{ \"type\": \"tool_use\", \"id\": \"toolu_1\", \"name\": \"order_status\", \"input\": { \"id\": 42 } }");
        Answer(Text("Order 42 has shipped."));
        string output = Run(
            "<fn name=\"order_status\" params=\"id: int\" description=\"The status of an order.\" value=\"$id == 42 ? 'shipped' : 'unknown'\" />" +
            "<llm into=\"answer\" tools=\"order_status\">Where is order 42?</llm><echo>{$answer}</echo>");
        Assert.Equal("Order 42 has shipped.\n", output);

        JsonElement tool = Sent(0).GetProperty("tools")[0];
        Assert.Equal("order_status", tool.GetProperty("name").GetString());
        Assert.Equal("The status of an order.", tool.GetProperty("description").GetString());
        Assert.Equal("id", tool.GetProperty("input_schema").GetProperty("required")[0].GetString());

        JsonElement messages = Sent(1).GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        JsonElement call = messages[1].GetProperty("content")[1];
        Assert.Equal("tool_use", call.GetProperty("type").GetString());
        Assert.Equal(42, call.GetProperty("input").GetProperty("id").GetInt32());
        JsonElement result = messages[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_1", result.GetProperty("tool_use_id").GetString());
        Assert.Equal("shipped", result.GetProperty("content").GetString());
    }

    [Fact]
    public void Thinking_before_a_fallback_is_left_out_when_the_history_goes_back()
    {
        Answer(
            "{ \"type\": \"thinking\", \"thinking\": \"early\", \"signature\": \"s1\" }",
            "{ \"type\": \"redacted_thinking\", \"data\": \"r1\" }",
            "{ \"type\": \"tool_use\", \"id\": \"toolu_0\", \"name\": \"x\", \"input\": {} }",
            "{ \"type\": \"fallback\", \"from\": { \"model\": \"claude-opus-5-5\" }, \"to\": { \"model\": \"claude-opus-5\" }, \"trigger\": \"refusal\" }",
            "{ \"type\": \"thinking\", \"thinking\": \"late\", \"signature\": \"s2\" }",
            "{ \"type\": \"redacted_thinking\", \"data\": \"r2\" }",
            "{ \"type\": \"server_tool_use\", \"id\": \"srvtoolu_1\", \"name\": \"web_search\", \"input\": {} }",
            Text("Hello."));
        Answer(Text("Still here."));
        string output = Run("<let name=\"chat\" value=\"@chat()\" /><echo>{ $chat->ask('Hi') } { $chat->ask('And now?') }</echo>");
        Assert.Equal("Hello. Still here.\n", output);

        JsonElement echoed = Sent(1).GetProperty("messages")[1].GetProperty("content");
        var kinds = echoed.EnumerateArray().Select(b => b.GetProperty("type").GetString() + (b.TryGetProperty("thinking", out JsonElement t) ? ":" + t.GetString() : b.TryGetProperty("data", out JsonElement d) ? ":" + d.GetString() : string.Empty)).ToList();
        Assert.Equal(new[] { "fallback", "thinking:late", "redacted_thinking:r2", "text" }, kinds);
    }

    [Fact]
    public void A_refusal_carries_the_explanation()
    {
        Stopped("refusal", "{ \"type\": \"refusal\", \"category\": null, \"explanation\": \"Not something I can help with.\" }");
        Assert.Contains("declined to answer: Not something I can help with.", Fails("<echo>{ @llm('x') }</echo>").Message);
    }

    [Fact]
    public void Api_errors_become_ai_errors()
    {
        answers.Enqueue((429, Error("rate_limit_error", "Slow down.")));
        Assert.Contains("rate limit was reached", Fails("<echo>{ @llm('x') }</echo>").Message);

        answers.Enqueue((400, Error("invalid_request_error", "Bad request.")));
        var bad = Fails("<echo>{ @llm('x') }</echo>");
        Assert.Equal("AI", bad.Type);
        Assert.Contains("Claude returned an error", bad.Message);

        answers.Enqueue((401, Error("authentication_error", "invalid x-api-key")));
        Assert.Contains("didn't accept the key in ANTHROPIC_API_KEY", Fails("<echo>{ @llm('x') }</echo>").Message);

        answers.Enqueue((401, Error("authentication_error", "expired")));
        Assert.Contains("Run `ant auth login` again", Fails("<echo>{ @llm('x') }</echo>", Model(ClaudeConnection.AntSource)).Message);

        answers.Enqueue((500, "not json"));
        Assert.Contains("Claude returned an error: HTTP 500", Fails("<echo>{ @llm('x') }</echo>").Message);
        answers.Enqueue((500, "{ \"error\": \"text\" }"));
        Assert.Contains("HTTP 500", Fails("<echo>{ @llm('x') }</echo>").Message);

        answers.Enqueue((200, "not json"));
        Assert.Contains("wasn't JSON", Fails("<echo>{ @llm('x') }</echo>").Message);
    }

    [Fact]
    public void Tokens_go_as_bearer_with_the_oauth_beta()
    {
        Answer(Text("Hi."));
        Assert.Equal("Hi.\n", Run("<echo>{ @llm('x') }</echo>", Model("ANTHROPIC_AUTH_TOKEN")));
        var headers = server.Requests[0].Headers;
        Assert.Equal("Bearer token", headers["Authorization"]);
        Assert.Contains(ClaudeModel.OAuthBeta, headers["anthropic-beta"]);
        Assert.False(headers.ContainsKey("x-api-key"));
    }

    [Fact]
    public void Unreachable_and_keyless_clients_say_what_to_do()
    {
        var offline = new ClaudeModel(() => new ClaudeConnection { ApiKey = "k", BaseUrl = $"http://127.0.0.1:{TestServer.FreePort()}", MaxRetries = 1 }) { Backoff = (_, _) => TimeSpan.Zero };
        Assert.Contains("Couldn't reach Claude", Fails("<echo>{ @llm('x') }</echo>", offline).Message);
        var keyless = new ClaudeModel(() => ClaudeConnection.FromEnvironment(_ => null, () => null));
        Assert.Contains("needs an API key", Fails("<echo>{ @llm('x') }</echo>", keyless).Message);
        var slow = new ClaudeModel(() => new ClaudeConnection { ApiKey = "k", Handler = new StubHandler(_ => throw new TaskCanceledException()) });
        Assert.Contains("timed out", Fails("<echo>{ @llm('x') }</echo>", slow).Message);
    }

    [Fact]
    public void The_environment_picks_the_credential_like_the_sdks()
    {
        var env = new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "key", ["ANTHROPIC_AUTH_TOKEN"] = "token", ["ANTHROPIC_BASE_URL"] = " http://localhost:1 " };
        ClaudeConnection c = ClaudeConnection.FromEnvironment(n => env.GetValueOrDefault(n), () => "ant");
        Assert.Equal(("key", null, "ANTHROPIC_API_KEY", "http://localhost:1"), (c.ApiKey, c.AuthToken, c.Source, c.BaseUrl));

        env.Remove("ANTHROPIC_API_KEY");
        c = ClaudeConnection.FromEnvironment(n => env.GetValueOrDefault(n), () => "ant");
        Assert.Equal(("token", "ANTHROPIC_AUTH_TOKEN"), (c.AuthToken, c.Source));

        env.Clear();
        c = ClaudeConnection.FromEnvironment(n => env.GetValueOrDefault(n), () => "ant");
        Assert.Equal(("ant", ClaudeConnection.AntSource, ClaudeConnection.DefaultBaseUrl), (c.AuthToken, c.Source, c.BaseUrl));

        // ant itself: missing, failing, and a program that prints something and exits 0.
        Assert.Null(ClaudeConnection.RunAnt("kscript-no-such-program-" + Guid.NewGuid().ToString("N"), "auth print-credentials --access-token"));
        Assert.Null(ClaudeConnection.RunAnt("dotnet", "no-such-command-here"));
        Assert.NotNull(ClaudeConnection.RunAnt("dotnet", "--version"));
        _ = ClaudeConnection.AntToken();
    }

    [Fact]
    public void The_default_connection_reads_the_environment()
    {
        string savedKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        string savedUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "k");
        Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", $"http://127.0.0.1:{TestServer.FreePort()}");
        try
        {
            var model = new ClaudeModel { Backoff = (_, _) => TimeSpan.Zero };
            Assert.Contains("Couldn't reach Claude", Fails("<echo>{ @llm('x') }</echo>", model).Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", savedKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", savedUrl);
        }
    }

    [Fact]
    public void Overloads_and_rate_limits_are_retried_unless_the_api_says_not_to()
    {
        var replies = new Queue<HttpResponseMessage>();
        HttpResponseMessage Reply(int status, string body, string shouldRetry = null)
        {
            var r = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (shouldRetry != null) r.Headers.Add("x-should-retry", shouldRetry);
            return r;
        }
        string ok = "{ \"content\": [{ \"type\": \"text\", \"text\": \"done\" }], \"stop_reason\": \"end_turn\" }";
        var waits = new List<int>();
        var model = new ClaudeModel(() => new ClaudeConnection { ApiKey = "k", MaxRetries = 2, Handler = new StubHandler(_ => replies.Dequeue()) })
        {
            Backoff = (attempt, _) => { waits.Add(attempt); return TimeSpan.Zero; },
        };

        replies.Enqueue(Reply(529, Error("overloaded_error", "Overloaded.")));
        replies.Enqueue(Reply(409, "{}"));
        replies.Enqueue(Reply(200, ok));
        Assert.Equal("done\n", Run("<echo>{ @llm('x') }</echo>", model));
        Assert.Equal(new[] { 0, 1 }, waits);

        replies.Enqueue(Reply(500, Error("api_error", "No."), shouldRetry: "false"));
        Assert.Contains("Claude returned an error: No.", Fails("<echo>{ @llm('x') }</echo>", model).Message);

        replies.Enqueue(Reply(400, Error("invalid_request_error", "Bad."), shouldRetry: "true"));
        replies.Enqueue(Reply(408, "{}"));
        replies.Enqueue(Reply(429, Error("rate_limit_error", "Slow.")));
        Assert.Contains("rate limit", Fails("<echo>{ @llm('x') }</echo>", model).Message);
    }

    [Fact]
    public void Backoff_follows_retry_after_up_to_a_minute()
    {
        var model = new ClaudeModel();
        Assert.Equal(TimeSpan.FromSeconds(3), model.Backoff(0, TimeSpan.FromSeconds(3)));
        Assert.Equal(TimeSpan.FromSeconds(1), model.Backoff(1, TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromSeconds(0.5), model.Backoff(0, null));
    }

    [Fact]
    public void Requests_and_replies_keep_odd_blocks_safe()
    {
        var request = new AiRequest { Model = "m" };
        request.Messages.Add(new AiMessage("assistant", new[] { new AiBlock { Kind = AiBlockKind.ToolUse, Id = "t", Name = "n" } }));
        JsonElement sent = JsonDocument.Parse(ClaudeModel.RequestBody(request)).RootElement;
        Assert.Equal(JsonValueKind.Object, sent.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("input").ValueKind);

        AiReply reply = ClaudeModel.Reply("{ \"stop_reason\": \"end_turn\" }");
        Assert.Empty(reply.Blocks);
        Assert.Null(reply.RefusalExplanation);
        reply = ClaudeModel.Reply("{ \"content\": [{ \"type\": \"text\" }, 5], \"stop_reason\": 1 }");
        Assert.Equal("", reply.Blocks[0].Text);
        Assert.Equal(AiBlockKind.Other, reply.Blocks[1].Kind);
        Assert.Null(reply.StopReason);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => answer(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
    }
}
