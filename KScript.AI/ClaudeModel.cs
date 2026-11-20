using System.Text.Json;

using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

using KScript.Runtime;

namespace KScript.AI
{
    /// <summary>
    /// Claude, through the Anthropic SDK. The API key comes from the environment (ANTHROPIC_API_KEY) or a profile
    /// saved by <c>ant auth login</c>, never from the script. If Claude's safety checks decline a request, the API
    /// answers with a fallback model instead (server-side fallbacks).
    /// </summary>
    internal sealed class ClaudeModel : ILanguageModel
    {
        internal const string DefaultModel = "claude-opus-5-5";

        private readonly Lazy<AnthropicClient> client;

        public ClaudeModel(Func<AnthropicClient> create = null)
        {
            client = new Lazy<AnthropicClient>(create ?? (() => new AnthropicClient()));
        }

        public AiReply Send(AiRequest request, CancellationToken cancellation)
        {
            var parameters = new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                Messages = request.Messages.Select(ToParam).ToList(),
            };
            if (!string.IsNullOrWhiteSpace(request.System)) parameters = parameters with { System = request.System };
            if (request.Tools.Count > 0)
            {
                parameters = parameters with
                {
                    Tools = request.Tools.Select(t => (BetaToolUnion)new BetaTool
                    {
                        Name = t.Name,
                        Description = t.Description,
                        InputSchema = new() { Properties = t.Properties, Required = t.Required },
                    }).ToList(),
                };
            }
            if (request.Schema != null)
            {
                parameters = parameters with { OutputConfig = new BetaOutputConfig { Format = new BetaJsonOutputFormat { Schema = request.Schema } } };
            }

            BetaMessage message;
            try
            {
                message = client.Value.Beta.Messages.Create(parameters, cancellation).GetAwaiter().GetResult();
            }
            catch (AnthropicUnauthorizedException)
            {
                bool hasKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN"));
                throw new KScriptException(hasKey
                    ? "Claude didn't accept the API key in ANTHROPIC_API_KEY. Check it at console.anthropic.com."
                    : "KScript.AI needs an API key: set ANTHROPIC_API_KEY to a key from console.anthropic.com, or run `ant auth login`. Keys never go in scripts.", "AI");
            }
            catch (AnthropicRateLimitException)
            {
                throw new KScriptException("Claude's rate limit was reached. Wait a moment and try again.", "AI");
            }
            catch (AnthropicApiException ex)
            {
                throw new KScriptException($"Claude returned an error: {ex.Message}", "AI");
            }
            catch (AnthropicIOException ex)
            {
                throw new KScriptException($"Couldn't reach Claude: {ex.Message}", "AI");
            }
            catch (Exception ex) when (ex is not KScriptException && ex is not OperationCanceledException && LooksLikeMissingKey(ex))
            {
                throw new KScriptException("KScript.AI needs an API key: set ANTHROPIC_API_KEY (or run `ant auth login`). Keys never go in scripts.", "AI");
            }

            return new AiReply
            {
                Blocks = message.Content.Select(FromBlock).ToList(),
                StopReason = message.StopReason?.Raw(),
                RefusalExplanation = message.StopDetails?.Explanation,
            };
        }

        private static bool LooksLikeMissingKey(Exception ex) =>
            ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("credential", StringComparison.OrdinalIgnoreCase);

        private static AiBlock FromBlock(BetaContentBlock block)
        {
            if (block.TryPickText(out BetaTextBlock text)) return AiBlock.FromText(text.Text);
            if (block.TryPickToolUse(out BetaToolUseBlock use))
            {
                return new AiBlock { Kind = AiBlockKind.ToolUse, Id = use.ID, Name = use.Name, Input = JsonSerializer.SerializeToElement(use.Input, AiJsonContext.Default.IReadOnlyDictionaryStringJsonElement) };
            }
            return new AiBlock { Kind = AiBlockKind.Other, Raw = block };
        }

        private static BetaMessageParam ToParam(AiMessage message)
        {
            var content = new List<BetaContentBlockParam>();
            // After a fallback, the model's thinking and tool calls from before the switch aren't sent back.
            int lastFallback = message.Blocks.FindLastIndex(b => b.Raw is BetaContentBlock r && r.TryPickFallback(out _));
            for (int i = 0; i < message.Blocks.Count; i++)
            {
                AiBlock b = message.Blocks[i];
                switch (b.Kind)
                {
                    case AiBlockKind.Text:
                        content.Add(new BetaTextBlockParam { Text = b.Text });
                        break;
                    case AiBlockKind.ToolUse:
                        if (i < lastFallback) break;
                        content.Add(new BetaToolUseBlockParam { ID = b.Id, Name = b.Name, Input = b.Input.Deserialize(AiJsonContext.Default.DictionaryStringJsonElement) });
                        break;
                    case AiBlockKind.ToolResult:
                        content.Add(new BetaToolResultBlockParam { ToolUseID = b.Id, Content = b.Text, IsError = b.IsError });
                        break;
                    case AiBlockKind.Other when b.Raw is BetaContentBlock raw:
                        BetaContentBlockParam echoed = Echo(raw, before: i < lastFallback);
                        if (echoed != null) content.Add(echoed);
                        break;
                }
            }
            return message.Role == "assistant"
                ? new BetaAssistantMessageParam { Content = content }
                : new BetaUserMessageParam { Content = content };
        }

        /// <summary>Thinking and fallback markers go back to the model as they came, so it keeps its reasoning.</summary>
        private static BetaContentBlockParam Echo(BetaContentBlock block, bool before)
        {
            // Kept apart from the returns below: "before ? null : new ...Param" would turn null into an empty block.
            bool isThinking = block.TryPickThinking(out BetaThinkingBlock thinking);
            bool isRedacted = block.TryPickRedactedThinking(out BetaRedactedThinkingBlock redacted);
            if ((isThinking || isRedacted) && before) return null;
            if (isThinking) return new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature };
            if (isRedacted) return new BetaRedactedThinkingBlockParam { Data = redacted.Data };
            if (block.TryPickFallback(out BetaFallbackBlock fallback))
                return BetaFallbackBlockParam.FromRawUnchecked(fallback.RawData);
            return null;
        }
    }
}
