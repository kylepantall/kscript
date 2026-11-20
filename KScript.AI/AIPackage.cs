using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.AI
{
    /// <summary>A conversation that keeps its history: <c>$chat->ask('...')</c>, <c>->history()</c>, <c>->reset()</c>.</summary>
    public sealed class ChatValue : IKObject
    {
        internal ChatValue(string system, string model, List<AiTool> tools, int maxTokens)
        {
            System = system;
            Model = model;
            Tools = tools;
            MaxTokens = maxTokens;
        }

        public string System { get; }
        public string Model { get; }
        internal List<AiTool> Tools { get; }
        internal int MaxTokens { get; }
        internal List<AiMessage> Messages { get; } = new List<AiMessage>();

        public string TypeName => KType.Chat;

        public string ToText() => $"chat ({Messages.Count(m => m.Role == "user" && m.Blocks.Any(b => b.Kind == AiBlockKind.Text))} messages)";

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "model" => Model,
                "system" => System,
                _ => null,
            };
            return value != null;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "ask":
                {
                    object schema = call.Arg(1, "schema");
                    var request = new AiRequest
                    {
                        Model = Model,
                        System = System,
                        Messages = Messages,
                        Tools = Tools,
                        Schema = schema == null ? null : Schemas.FromValue(schema, "->ask() schema"),
                        MaxTokens = MaxTokens,
                    };
                    int before = Messages.Count;
                    Messages.Add(new AiMessage("user", new[] { AiBlock.FromText(Values.ToStr(call.Need(0))) }));
                    try
                    {
                        result = Conversation.Ask(AIPackage.ModelFor(call.Host), request, call.Interpreter, call.Scope, call.Module);
                    }
                    catch
                    {
                        // A failed turn leaves the history as it was, so the conversation can carry on.
                        Messages.RemoveRange(before, Messages.Count - before);
                        throw;
                    }
                    return true;
                }
                case "history":
                    result = Messages
                        .Where(m => m.Blocks.Any(b => b.Kind == AiBlockKind.Text) && m.Text.Trim().Length > 0)
                        .Select(m => (object)new Dictionary<string, object> { ["role"] = m.Role, ["text"] = m.Text.Trim() })
                        .ToList();
                    return true;
                case "reset":
                    Messages.Clear();
                    result = this;
                    return true;
                case "model":
                    result = Model;
                    return true;
            }
            result = null;
            return false;
        }
    }

    /// <summary>KScript.AI: Claude in scripts. <c>@llm('...')</c> asks once; <c>@chat()</c> keeps a conversation.</summary>
    public sealed class AIPackage : KScriptPackage
    {
        /// <summary>Tests put an <see cref="ILanguageModel"/> here to stand in for Claude.</summary>
        public const string ModelKey = "KScript.AI.Model";

        public override string Name => "KScript.AI";

        public override string Description => "Language models (Claude): @llm, @chat and <llm>.";

        private static readonly Lazy<ILanguageModel> Claude = new Lazy<ILanguageModel>(() => new ClaudeModel());

        internal static ILanguageModel ModelFor(KScriptHost host) =>
            host.Items.TryGetValue(ModelKey, out object m) && m is ILanguageModel model ? model : Claude.Value;

        /// <summary>The model to use: the one asked for, KSCRIPT_AI_MODEL, or Claude Opus 5.5.</summary>
        internal static string ModelName(object asked)
        {
            string name = asked == null ? null : Values.ToStr(asked).Trim();
            if (string.IsNullOrEmpty(name)) name = Environment.GetEnvironmentVariable("KSCRIPT_AI_MODEL")?.Trim();
            return string.IsNullOrEmpty(name) ? ClaudeModel.DefaultModel : name;
        }

        protected override void Register(PackageBuilder p)
        {
            p.Command("llm", "prompt, options?",
                "Asks a language model (Claude) and returns its answer. options is a model name, or a map: model, system, schema (a map of fields; the answer is then a map), tools (<fn> functions it may call), max_tokens.",
                KType.Any, Llm,
                "<let name=\"summary\" value=\"@llm('Summarise in one line: ' + $text)\" />");

            p.Command("chat", "system?, options?",
                "A conversation with a language model that keeps its history: $chat->ask(text). options is a map: model, tools, max_tokens.",
                KType.Chat, c =>
                {
                    Dictionary<string, object> options = c.Arg(1, "options") as Dictionary<string, object> ?? new Dictionary<string, object>();
                    string system = c.Arg(0, "system") is object s ? Values.ToStr(s) : null;
                    return new ChatValue(system, ModelName(Option(options, "model")), Tools(Option(options, "tools"), "@chat() tools"), MaxTokens(Option(options, "max_tokens")));
                },
                "<let name=\"chat\" value=\"@chat('You are a helpful assistant.')\" />\n<echo>{ $chat->ask('Hello!') }</echo>");

            p.Method(KType.Chat, "ask", "message, schema?", "Sends a message and returns the answer, keeping the history. With a schema the answer is a map.", KType.Any, Chat("ask"));
            p.Method(KType.Chat, "history", "", "The conversation so far: a list of { role, text }.", KType.List, Chat("history"));
            p.Method(KType.Chat, "reset", "", "Forgets the conversation (keeps the system prompt).", KType.Chat, Chat("reset"));
            p.Method(KType.Chat, "model", "", "The model the chat uses.", KType.String, Chat("model"));

            p.Element(new ElementInfo("llm", "Sends its text as a prompt to a language model (Claude) and stores the answer.", ContentKind.Template,
                new A("to", K.DeclareVariable, "The variable that receives the answer.", true),
                new A("system", K.Template, "The system prompt."),
                new A("schema", K.Expression, "Ask for structured output: a map describing the fields, such as `{ category: 'bug|feature', urgency: 'int 1-5' }`. The answer is then a map."),
                new A("tools", K.FunctionReferences, "<fn> functions the model may call, comma separated."),
                new A("model", K.Text, "The model to use (default: claude-opus-5-5, or KSCRIPT_AI_MODEL)."),
                new A("max-tokens", K.Expression, "The longest answer, in tokens (default 16000)."))
            {
                Context = ElementContext.Script,
                Example = "<llm to=\"reply\" system=\"Answer in Markdown.\">\n  Customer {$customer.name} wrote: {$message}\n</llm>",
            }, run =>
            {
                string name = run.VariableName("to");
                var tools = new List<AiTool>();
                foreach (string fn in (run.Raw("tools") ?? string.Empty).Split(',').Select(t => t.Trim().TrimStart('@', '$')).Where(t => t.Length > 0))
                {
                    if (!run.Scope.TryGet(fn, out object value))
                        throw run.Error($"tools=\"{run.Raw("tools")}\": there is no function called {fn}. Declare it with <fn name=\"{fn}\" ...>.", "tools", "Name");
                    tools.Add(Schemas.Tool(value, "<llm tools>"));
                }
                var request = new AiRequest
                {
                    Model = ModelName(run.Raw("model")),
                    System = run.Template("system"),
                    Tools = tools,
                    Schema = run.Has("schema") ? Schemas.FromValue(run.Eval("schema"), "<llm schema>") : null,
                    MaxTokens = run.Has("max-tokens") ? MaxTokens(run.Eval("max-tokens")) : 16000,
                };
                string prompt = run.Text();
                if (prompt.Trim().Length == 0) throw run.Error("<llm> needs a prompt: write it inside the element, such as <llm to=\"answer\">What is KScript?</llm>.");
                request.Messages.Add(new AiMessage("user", new[] { AiBlock.FromText(prompt) }));
                run.Scope.Declare(name, Conversation.Ask(ModelFor(run.Host), request, run.Interpreter, run.Scope, run.Module));
                return null;
            });
        }

        private static MethodHandler Chat(string name) => (t, c) => ((ChatValue)t).TryCallMethod(c, name, out object r) ? r : null;

        private static object Llm(CallContext c)
        {
            object second = c.Arg(1, "options");
            Dictionary<string, object> options = second as Dictionary<string, object> ?? new Dictionary<string, object>();
            object model = second is string ? second : Option(options, "model");
            object schema = Option(options, "schema");
            var request = new AiRequest
            {
                Model = ModelName(model),
                System = Option(options, "system") is object s ? Values.ToStr(s) : null,
                Tools = Tools(Option(options, "tools"), "@llm() tools"),
                Schema = schema == null ? null : Schemas.FromValue(schema, "@llm() schema"),
                MaxTokens = MaxTokens(Option(options, "max_tokens")),
            };
            string prompt = Values.ToStr(c.Need(0));
            if (prompt.Trim().Length == 0) throw new KScriptException("@llm() needs a prompt.", "Argument");
            request.Messages.Add(new AiMessage("user", new[] { AiBlock.FromText(prompt) }));
            return Conversation.Ask(ModelFor(c.Host), request, c.Interpreter, c.Scope, c.Module);
        }

        private static object Option(Dictionary<string, object> options, string key) => options.TryGetValue(key, out object v) ? v : null;

        private static List<AiTool> Tools(object value, string where) => value switch
        {
            null => new List<AiTool>(),
            List<object> list => list.Select(t => Schemas.Tool(t, where)).ToList(),
            _ => new List<AiTool> { Schemas.Tool(value, where) },
        };

        private static int MaxTokens(object value)
        {
            if (value == null) return 16000;
            int n = (int)Values.ToNumber(value, "max_tokens");
            if (n < 1 || n > 64000) throw new KScriptException($"max_tokens must be between 1 and 64,000, not {n}.", "Argument");
            return n;
        }
    }
}
