using System.Text.Json;

using KScript.Runtime;

namespace KScript.AI
{
    /// <summary>What a block of a message holds.</summary>
    public enum AiBlockKind
    {
        Text,
        ToolUse,
        ToolResult,

        /// <summary>Something the model sent that KScript passes back unchanged, such as its thinking.</summary>
        Other,
    }

    /// <summary>One block of a message: text, a tool call, a tool result, or a block passed back as it came.</summary>
    public sealed class AiBlock
    {
        public AiBlockKind Kind { get; init; }
        public string Text { get; init; }

        /// <summary>The tool call's id (tool calls and their results).</summary>
        public string Id { get; init; }

        /// <summary>The tool's name (tool calls).</summary>
        public string Name { get; init; }

        /// <summary>The tool call's arguments, a JSON object (tool calls).</summary>
        public JsonElement Input { get; init; }

        public bool IsError { get; init; }

        /// <summary>For <see cref="AiBlockKind.Other"/>: the model's own block, for the model to read back.</summary>
        public object Raw { get; init; }

        public static AiBlock FromText(string text) => new AiBlock { Kind = AiBlockKind.Text, Text = text };
    }

    public sealed class AiMessage
    {
        public AiMessage(string role, IEnumerable<AiBlock> blocks)
        {
            Role = role;
            Blocks = blocks.ToList();
        }

        /// <summary>user or assistant.</summary>
        public string Role { get; }

        public List<AiBlock> Blocks { get; }

        public string Text => string.Concat(Blocks.Where(b => b.Kind == AiBlockKind.Text).Select(b => b.Text));
    }

    /// <summary>A tool the model may call: a &lt;fn&gt; function, described by its parameters.</summary>
    public sealed class AiTool
    {
        public string Name { get; init; }
        public string Description { get; init; }

        /// <summary>The JSON schema of each parameter.</summary>
        public Dictionary<string, JsonElement> Properties { get; init; } = new Dictionary<string, JsonElement>();

        public List<string> Required { get; init; } = new List<string>();

        internal ICallable Function { get; init; }
    }

    public sealed class AiRequest
    {
        public string Model { get; init; }
        public string System { get; init; }
        public List<AiMessage> Messages { get; init; } = new List<AiMessage>();
        public List<AiTool> Tools { get; init; } = new List<AiTool>();

        /// <summary>A JSON schema the answer must follow, or null for a text answer.</summary>
        public Dictionary<string, JsonElement> Schema { get; init; }

        public int MaxTokens { get; init; } = 16000;
    }

    public sealed class AiReply
    {
        public List<AiBlock> Blocks { get; init; } = new List<AiBlock>();

        /// <summary>end_turn, tool_use, max_tokens, refusal...</summary>
        public string StopReason { get; init; }

        /// <summary>Why the model declined, when <see cref="StopReason"/> is refusal.</summary>
        public string RefusalExplanation { get; init; }
    }

    /// <summary>A language model KScript can talk to. KScript.AI uses Claude; tests use a fake.</summary>
    public interface ILanguageModel
    {
        AiReply Send(AiRequest request, CancellationToken cancellation);
    }

    /// <summary>Sends a conversation to a model and runs the tools it asks for, until it answers.</summary>
    internal static class Conversation
    {
        /// <summary>How many rounds of tool calls one answer may take.</summary>
        internal const int MaxToolRounds = 25;

        /// <summary>
        /// Sends <paramref name="request"/> (adding the model's turns, tool calls and their results to its messages)
        /// and returns the answer: text, or with a schema the parsed value.
        /// </summary>
        public static object Ask(ILanguageModel model, AiRequest request, Interpreter interpreter, Scope scope, Module module)
        {
            for (int round = 0; ; round++)
            {
                AiReply reply = model.Send(request, interpreter.Options.Cancellation);
                var answer = new AiMessage("assistant", reply.Blocks);

                switch (reply.StopReason)
                {
                    case "refusal":
                        throw new KScriptException("The model declined to answer" + (string.IsNullOrWhiteSpace(reply.RefusalExplanation) ? "." : $": {reply.RefusalExplanation}"), "AI");
                    case "max_tokens":
                        throw new KScriptException($"The answer was cut off after {request.MaxTokens:N0} tokens. Ask for a shorter answer, or raise max_tokens.", "AI");
                }

                request.Messages.Add(answer);
                List<AiBlock> calls = reply.Blocks.Where(b => b.Kind == AiBlockKind.ToolUse).ToList();
                if (reply.StopReason != "tool_use" || calls.Count == 0)
                {
                    return request.Schema != null ? ParseStructured(answer.Text) : answer.Text.Trim();
                }

                if (round >= MaxToolRounds)
                    throw new KScriptException($"The model was still calling tools after {MaxToolRounds} rounds, so KScript stopped it.", "AI");

                // Every tool call gets a result, in one message, even when a tool fails.
                request.Messages.Add(new AiMessage("user", calls.Select(c => RunTool(request, c, interpreter, scope, module)).ToList()));
            }
        }

        private static AiBlock RunTool(AiRequest request, AiBlock use, Interpreter interpreter, Scope scope, Module module)
        {
            AiTool tool = request.Tools.FirstOrDefault(t => t.Name == use.Name);
            if (tool == null)
                return new AiBlock { Kind = AiBlockKind.ToolResult, Id = use.Id, Text = $"There is no tool called {use.Name}.", IsError = true };
            try
            {
                var named = new Dictionary<string, object>();
                if (use.Input.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in use.Input.EnumerateObject())
                    {
                        if (tool.Properties.ContainsKey(p.Name)) named[p.Name] = Values.FromJson(p.Value);
                    }
                }
                object result = interpreter.Call(tool.Function, Array.Empty<object>(), named, scope, module);
                string text = result is string s ? s : Values.ToJson(result);
                return new AiBlock { Kind = AiBlockKind.ToolResult, Id = use.Id, Text = text };
            }
            catch (KScriptException ex)
            {
                return new AiBlock { Kind = AiBlockKind.ToolResult, Id = use.Id, Text = ex.Message, IsError = true };
            }
        }

        private static object ParseStructured(string text)
        {
            try
            {
                return Values.ParseJson(text);
            }
            catch (KScriptException)
            {
                throw new KScriptException("The model's answer wasn't the JSON the schema asked for.", "AI");
            }
        }
    }
}
