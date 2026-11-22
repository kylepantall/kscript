using System.Globalization;
using System.Text.Json;

using KScript.AI;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>Stands in for Claude: answers from a script of replies and records what it was sent.</summary>
internal sealed class FakeModel : ILanguageModel
{
    private readonly Queue<Func<AiRequest, AiReply>> replies = new Queue<Func<AiRequest, AiReply>>();

    public List<AiRequest> Requests { get; } = new List<AiRequest>();

    /// <summary>Copies of the messages of each request, as they were when it was sent.</summary>
    public List<List<(string Role, string Text)>> Sent { get; } = new List<List<(string Role, string Text)>>();

    public FakeModel Says(string text) => Then(_ => new AiReply { StopReason = "end_turn", Blocks = { AiBlock.FromText(text) } });

    public FakeModel Calls(string tool, object input, string id = "call_1") => Then(_ => new AiReply
    {
        StopReason = "tool_use",
        Blocks = { AiBlock.FromText("Let me check."), new AiBlock { Kind = AiBlockKind.ToolUse, Id = id, Name = tool, Input = JsonSerializer.SerializeToElement(input) } },
    });

    public FakeModel Then(Func<AiRequest, AiReply> reply)
    {
        replies.Enqueue(reply);
        return this;
    }

    public AiReply Send(AiRequest request, CancellationToken cancellation)
    {
        Requests.Add(request);
        Sent.Add(request.Messages.Select(m => (m.Role, string.Join(" | ", m.Blocks.Select(b => b.Kind == AiBlockKind.ToolResult ? $"result {b.Id}: {b.Text}{(b.IsError ? " (error)" : string.Empty)}" : b.Kind == AiBlockKind.ToolUse ? $"call {b.Name}" : b.Text)))).ToList());
        Assert.True(replies.Count > 0, "The script asked the model more often than the test expected.");
        return replies.Dequeue()(request);
    }
}

public class AITests
{
    private static string Run(FakeModel model, string body)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        host.Items[AIPackage.ModelKey] = model;
        new Interpreter(host, options: new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) })
            .RunText("<kscript><import package=\"KScript.AI\" />" + body + "</kscript>");
        return output.ToString();
    }

    [Fact]
    public void Llm_asks_once_and_returns_the_answer()
    {
        var model = new FakeModel().Says("  Paris  ");
        Assert.Equal("Paris\n", Run(model, "<echo>{ @llm('Capital of France?') }</echo>"));
        AiRequest sent = model.Requests.Single();
        Assert.Equal("claude-opus-5-5", sent.Model);
        Assert.Null(sent.System);
        Assert.Equal(("user", "Capital of France?"), model.Sent[0].Single());
    }

    [Fact]
    public void Llm_takes_a_model_or_options()
    {
        var model = new FakeModel().Says("a").Says("b");
        Run(model, "<let name=\"a\" value=\"@llm('hi', model: 'claude-haiku-5-5')\" /><let name=\"b\" value=\"@llm('hi', model: 'claude-sonnet-5-5', system: 'Be brief.', max_tokens: 500)\" />");
        Assert.Equal("claude-haiku-5-5", model.Requests[0].Model);
        Assert.Equal("claude-sonnet-5-5", model.Requests[1].Model);
        Assert.Equal("Be brief.", model.Requests[1].System);
        Assert.Equal(500, model.Requests[1].MaxTokens);
    }

    [Fact]
    public void The_llm_element_fills_in_its_prompt_and_stores_the_answer()
    {
        var model = new FakeModel().Says("Sorry to hear that, Ann.");
        string output = Run(model, "<let name=\"name\" value=\"'Ann'\" /><llm into=\"reply\" system=\"You are support for {$name}'s shop.\">\n  {$name} wrote: my order is late\n</llm><echo>{$reply}</echo>");
        Assert.Equal("Sorry to hear that, Ann.\n", output);
        Assert.Equal("You are support for Ann's shop.", model.Requests[0].System);
        Assert.Equal("Ann wrote: my order is late", model.Sent[0][0].Text);
    }

    [Fact]
    public void A_schema_gives_back_a_map()
    {
        var model = new FakeModel().Says("{\"category\": \"bug\", \"urgency\": 4, \"tags\": [\"login\"]}");
        string output = Run(model, "<llm into=\"t\" schema=\"{ category: 'bug|feature', urgency: 'int 1-5', tags: ['string'], summary: 'a one-line summary' }\">Triage: login fails</llm><echo>{$t.category} {$t.urgency + 1} {$t.tags[0]}</echo>");
        Assert.Equal("bug 5 login\n", output);

        Dictionary<string, JsonElement> schema = model.Requests[0].Schema;
        Assert.Equal("object", schema["type"].GetString());
        Assert.False(schema["additionalProperties"].GetBoolean());
        Assert.Equal(new[] { "category", "urgency", "tags", "summary" }, schema["required"].EnumerateArray().Select(e => e.GetString()));
        JsonElement props = schema["properties"];
        Assert.Equal(new[] { "bug", "feature" }, props.GetProperty("category").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("integer", props.GetProperty("urgency").GetProperty("type").GetString());
        Assert.Equal("array", props.GetProperty("tags").GetProperty("type").GetString());
        Assert.Equal("a one-line summary", props.GetProperty("summary").GetProperty("description").GetString());
    }

    [Fact]
    public void The_model_can_call_fn_functions_as_tools()
    {
        var model = new FakeModel()
            .Calls("order_status", new { id = 42 })
            .Then(r => new AiReply { StopReason = "end_turn", Blocks = { AiBlock.FromText("Order 42 has shipped.") } });
        string output = Run(model,
            "<fn name=\"order_status\" params=\"id: int\" description=\"The status of an order.\" value=\"$id == 42 ? 'shipped' : 'unknown'\" />" +
            "<llm into=\"answer\" tools=\"order_status\">Where is order 42?</llm><echo>{$answer}</echo>");
        Assert.Equal("Order 42 has shipped.\n", output);

        AiTool tool = model.Requests[0].Tools.Single();
        Assert.Equal("order_status", tool.Name);
        Assert.Equal("The status of an order.", tool.Description);
        Assert.Equal("integer", tool.Properties["id"].GetProperty("type").GetString());
        Assert.Equal(new[] { "id" }, tool.Required);

        // The second request has the model's tool call and the function's result.
        Assert.Equal(new[] { "user", "assistant", "user" }, model.Sent[1].Select(m => m.Role));
        Assert.Equal("result call_1: shipped", model.Sent[1][2].Text);
    }

    [Fact]
    public void A_failing_tool_tells_the_model_instead_of_stopping_the_script()
    {
        var model = new FakeModel().Calls("lookup", new { }).Says("I couldn't look that up.");
        string output = Run(model, "<fn name=\"lookup\" params=\"key\" value=\"$key\" /><echo>{ @llm('Look up something', tools: [@lookup]) }</echo>");
        Assert.Equal("I couldn't look that up.\n", output);
        Assert.Contains("needs a value for 'key'", model.Sent[1][2].Text);
        Assert.EndsWith("(error)", model.Sent[1][2].Text);
    }

    [Fact]
    public void Chat_keeps_the_conversation()
    {
        var model = new FakeModel().Says("Hello Kyle!").Says("Your name is Kyle.");
        string output = Run(model,
            "<let name=\"chat\" value=\"@chat('You are friendly.')\" />" +
            "<echo>{ $chat->ask('Hi, I am Kyle') }</echo><echo>{ $chat->ask('What is my name?') }</echo>" +
            "<echo>{ $chat.history->count() } { $chat.history[3].text }</echo>");
        Assert.Equal("Hello Kyle!\nYour name is Kyle.\n4 Your name is Kyle.\n", output);
        Assert.Equal("You are friendly.", model.Requests[1].System);
        Assert.Equal(new[] { "Hi, I am Kyle", "Hello Kyle!", "What is my name?" }, model.Sent[1].Select(m => m.Text));
    }

    [Fact]
    public void Refusals_and_cut_off_answers_are_errors_scripts_can_catch()
    {
        var model = new FakeModel()
            .Then(_ => new AiReply { StopReason = "refusal", RefusalExplanation = "Not something I can help with." })
            .Then(_ => new AiReply { StopReason = "max_tokens", Blocks = { AiBlock.FromText("Once upon") } });
        string output = Run(model,
            "<try><echo>{ @llm('x') }</echo><catch as=\"e\"><echo>{$e.type}: {$e.message}</echo></catch></try>" +
            "<try><echo>{ @llm('y', max_tokens: 10) }</echo><catch as=\"e\"><echo>{$e.message}</echo></catch></try>");
        Assert.Equal("AI: The model declined to answer: Not something I can help with.\nThe answer was cut off after 10 tokens. Ask for a shorter answer, or raise max_tokens.\n", output);
    }

    [Fact]
    public void A_failed_chat_turn_leaves_the_history_as_it_was()
    {
        var model = new FakeModel().Then(_ => new AiReply { StopReason = "refusal" }).Says("ok");
        string output = Run(model,
            "<let name=\"chat\" value=\"@chat()\" /><try><echo>{ $chat->ask('bad') }</echo><catch><echo>declined</echo></catch></try>" +
            "<echo>{ $chat->ask('good') } { $chat.history->count() }</echo>");
        Assert.Equal("declined\nok 2\n", output);
    }

    [Fact]
    public void Tools_must_be_fn_functions()
    {
        var model = new FakeModel();
        var ex = Assert.Throws<KScriptException>(() => Run(model, "<echo>{ @llm('x', tools: [fn($x) => $x]) }</echo>"));
        Assert.Contains("tools are <fn> functions", ex.Message);
    }

    [Fact]
    public void Ai_needs_an_import()
    {
        var ex = Assert.Throws<KScriptException>(() => ScriptRunner.RunBody("<echo>{ @llm('x') }</echo>"));
        Assert.Contains("KScript.AI", ex.Message);
    }

    [Fact]
    public void Chats_describe_themselves_and_can_start_over()
    {
        var model = new FakeModel().Says("one").Says("two");
        string output = Run(model,
            "<let name=\"chat\" value=\"@chat('Be kind.', model: 'claude-test')\" />" +
            "<echo>{ $chat.model } { $chat.system } { $chat.nothing ?? 'none' } { $chat.model } {$chat}</echo>" +
            "<echo>{ $chat->ask('a') } {$chat} { $chat->reset().history->count() } { $chat->ask('b') }</echo>");
        Assert.Equal("claude-test Be kind. none claude-test chat (0 messages)\none chat (1 messages) 0 two\n", output);
        Assert.Single(model.Sent[1]);
    }

    [Fact]
    public void Tool_problems_go_back_to_the_model_as_errors()
    {
        var model = new FakeModel()
            .Calls("nope", new { })
            .Then(_ => new AiReply
            {
                StopReason = "tool_use",
                Blocks =
                {
                    new AiBlock { Kind = AiBlockKind.ToolUse, Id = "c2", Name = "fail", Input = JsonSerializer.SerializeToElement(new { why = "x", extra = 1 }) },
                    new AiBlock { Kind = AiBlockKind.ToolUse, Id = "c3", Name = "info", Input = JsonSerializer.SerializeToElement(5) },
                },
            })
            .Says("done");
        string output = Run(model,
            "<fn name=\"fail\" params=\"why: string\"><throw message=\"broke: {$why}\" /></fn>" +
            "<fn name=\"info\" params=\"\" value=\"{ ok: true }\" />" +
            "<echo>{ @llm('go', tools: [@fail, @info]) }</echo>");
        Assert.Equal("done\n", output);
        Assert.Equal("result call_1: There is no tool called nope. (error)", model.Sent[1][2].Text);
        Assert.Equal("result c2: broke: x (error) | result c3: {\"ok\":true}", model.Sent[2][4].Text);
    }

    [Fact]
    public void Endless_tool_calls_and_bad_answers_are_stopped()
    {
        var model = new FakeModel();
        for (int i = 0; i <= 25; i++) model.Calls("ping", new { });
        var ex = Assert.Throws<KScriptException>(() => Run(model, "<fn name=\"ping\" value=\"'pong'\" /><echo>{ @llm('go', tools: @ping) }</echo>"));
        Assert.Contains("still calling tools after 25 rounds", ex.Message);

        var bad = Assert.Throws<KScriptException>(() => Run(new FakeModel().Says("not json"), "<echo>{ @llm('go', schema: { a: 'string' }) }</echo>"));
        Assert.Contains("wasn't the JSON the schema asked for", bad.Message);

        var cut = Assert.Throws<KScriptException>(() => Run(new FakeModel().Then(_ => new AiReply { StopReason = "max_tokens" }), "<echo>{ @llm('go', max_tokens: 1000) }</echo>"));
        Assert.Contains("cut off after 1,000 tokens", cut.Message);
    }

    [Fact]
    public void Schemas_and_tool_parameters_become_json_schema()
    {
        var model = new FakeModel().Says("{}").Says("ok");
        Run(model,
            "<let name=\"r\" value=\"@llm('x', schema: { s: 'text', n: 'float', i: 'integer', b: 'boolean', d: 'date', c: 'a | b | a', r: 'number 1.5 to 3', k: 'int 1-5', note: 'a one-line note', list: ['string'], nested: { x: 'int' } })\" />" +
            "<fn name=\"all\" params=\"s: string, i: int, f: float, n: number, b: bool, l: list, m: map, d: date, a, o = 1\" value=\"1\" />" +
            "<llm into=\"x\" tools=\"all\">hi</llm>");
        string schema = JsonSerializer.Serialize(model.Requests[0].Schema);
        Assert.Contains("\"s\":{\"type\":\"string\"}", schema);
        Assert.Contains("\"n\":{\"type\":\"number\"}", schema);
        Assert.Contains("\"i\":{\"type\":\"integer\"}", schema);
        Assert.Contains("\"b\":{\"type\":\"boolean\"}", schema);
        Assert.Contains("\"d\":{\"type\":\"string\",\"description\":\"A date, as YYYY-MM-DD.\"}", schema);
        Assert.Contains("\"c\":{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}", schema);
        Assert.Contains("\"r\":{\"type\":\"number\",\"description\":\"From 1.5 to 3.\"}", schema);
        Assert.Contains("\"k\":{\"type\":\"integer\",\"description\":\"From 1 to 5.\"}", schema);
        Assert.Contains("\"note\":{\"type\":\"string\",\"description\":\"a one-line note\"}", schema);
        Assert.Contains("\"list\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}", schema);
        Assert.Contains("\"additionalProperties\":false", schema);

        AiTool tool = model.Requests[1].Tools[0];
        Assert.Equal("The KScript function all(s, i, f, n, b, l, m, d, a, o).", tool.Description);
        Assert.Equal(new[] { "s", "i", "f", "n", "b", "l", "m", "d", "a" }, tool.Required);
        Assert.Equal("{\"type\":\"array\"}", tool.Properties["l"].GetRawText());
        Assert.Equal("{\"type\":\"object\"}", tool.Properties["m"].GetRawText());
        Assert.Equal("{}", tool.Properties["a"].GetRawText());
        Assert.Contains("YYYY-MM-DD", tool.Properties["d"].GetRawText());
    }

    [Fact]
    public void Bad_schemas_and_tools_say_what_is_wrong()
    {
        string Error(string body) => Assert.Throws<KScriptException>(() => Run(new FakeModel().Says("{}"), body)).Message;
        Assert.Contains("needs a map of fields", Error("<echo>{ @llm('x', schema: 'string') }</echo>"));
        Assert.Contains("describes its items with one value", Error("<echo>{ @llm('x', schema: { a: ['string', 'int'] }) }</echo>"));
        Assert.Contains("can't use a number (5) as a field", Error("<echo>{ @llm('x', schema: { a: 5 }) }</echo>"));
        Assert.Contains("needs a map of fields", Error("<let name=\"c\" value=\"@chat()\" /><echo>{ $c->ask('x', [1]) }</echo>"));
        Assert.Contains("nope", Error("<let name=\"c\" value=\"@chat()\" /><echo>{ $c->nope() }</echo>"));
        Assert.Contains("there is no function called missing", Error("<llm into=\"x\" tools=\"missing\">hi</llm>"));
    }

    [Fact]
    public void Without_a_stand_in_the_model_is_claude()
    {
        Assert.IsType<ClaudeModel>(AIPackage.ModelFor(KScriptHost.ForText(new StringWriter())));
    }
}
