using System.Globalization;
using System.Text.Json;

using Anthropic;

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

    private ClaudeModel Model() => new ClaudeModel(() => new AnthropicClient { ApiKey = "test-key", BaseUrl = server.Url, MaxRetries = 0 });

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
        string output = Run("<echo>{ @llm('Capital of France?', { system: 'Be brief.', schema: { city: 'string' }, max_tokens: 50 }).city }</echo>");
        Assert.Equal("Paris\n", output);

        var (method, path, _, headers) = server.Requests[0];
        Assert.Equal("POST", method);
        Assert.StartsWith("/v1/messages", path);
        Assert.Contains("server-side-fallback-2026-07-01", headers["anthropic-beta"]);
        Assert.True(headers.ContainsKey("x-api-key") || headers.ContainsKey("Authorization"));
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
            "<llm to=\"answer\" tools=\"order_status\">Where is order 42?</llm><echo>{$answer}</echo>");
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
        Assert.Contains("needs an API key", Fails("<echo>{ @llm('x') }</echo>").Message);

        string saved = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "set-for-test");
        try
        {
            answers.Enqueue((401, Error("authentication_error", "invalid x-api-key")));
            Assert.Contains("didn't accept the API key", Fails("<echo>{ @llm('x') }</echo>").Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", saved);
        }
    }

    [Fact]
    public void Unreachable_and_keyless_clients_say_what_to_do()
    {
        var offline = new ClaudeModel(() => new AnthropicClient { ApiKey = "k", BaseUrl = $"http://127.0.0.1:{TestServer.FreePort()}", MaxRetries = 0 });
        Assert.Contains("Couldn't reach Claude", Fails("<echo>{ @llm('x') }</echo>", offline).Message);
        var keyless = new ClaudeModel(() => throw new InvalidOperationException("No API key or credentials were found."));
        Assert.Contains("needs an API key", Fails("<echo>{ @llm('x') }</echo>", keyless).Message);
        var broken = new ClaudeModel(() => throw new InvalidOperationException("Something else."));
        Assert.DoesNotContain("API key", Fails("<echo>{ @llm('x') }</echo>", broken).Message);
    }
}
