using System.Text.Json;

namespace Blazor.AI.Demo.Client.Services;

/// <summary>
/// Builds conversations for demos that need history rather than a live model.
/// </summary>
public static class DemoConversations
{
    /// <summary>
    /// Builds a long, heterogeneous conversation: prose, code, tables, tool calls, and citations.
    /// </summary>
    /// <param name="messageCount">How many messages to build.</param>
    /// <returns>The conversation.</returns>
    /// <remarks>
    /// Deliberately varied. A thousand identical paragraphs would virtualize beautifully and prove
    /// nothing; the hard case is mixed content with wildly different heights.
    /// </remarks>
    public static Conversation Long(int messageCount)
    {
        var conversation = new Conversation();
        conversation.SetTitle($"{messageCount} message conversation");

        var source = new CitationSource("s1", "Performance notes")
        {
            Kind = SourceKind.Document,
            SiteName = "Internal wiki",
            Url = "https://example.com/perf",
        };

        for (var i = 0; i < messageCount; i += 2)
        {
            conversation.AddMessage(AiRole.User, $"Question {i / 2 + 1}: what happens at scale here?");

            var assistant = new AiMessage(AiRole.Assistant);

            switch ((i / 2) % 4)
            {
                case 0:
                    assistant.AddPart(new TextPart(
                        $"Answer {i / 2 + 1}. The cost is dominated by rendering, not by parsing. " +
                        "Coalescing updates removes most of it, and virtualization removes the rest."));
                    break;

                case 1:
                    assistant.AddPart(new MarkdownPart(
                        "Here is the shape of it:\n\n" +
                        "```csharp\n" +
                        $"var budget = TimeSpan.FromMilliseconds({16 + (i % 40)});\n" +
                        "scheduler.Request();\n" +
                        "```\n\n" +
                        "The interval is the only dial that matters."));
                    break;

                case 2:
                    var call = assistant.AddPart(new ToolCallPart(AiId.NewToolCallId(), "search_docs"));
                    call.CompleteArguments(JsonDocument.Parse($$"""{"query":"scale {{i}}"}""").RootElement);
                    call.SetResult(ToolOutcome.Success(new
                    {
                        hits = new[]
                        {
                            new { title = "Render modes", score = 0.94 },
                            new { title = "Circuit lifetime", score = 0.81 },
                        },
                    }));
                    assistant.AddPart(new TextPart("Two passages matched, and they say the same thing."));
                    break;

                default:
                    assistant.AddPart(new MarkdownPart(
                        "| Messages | Rendered | Notes |\n" +
                        "| --- | ---: | --- |\n" +
                        "| 60 | all | below the threshold |\n" +
                        "| 500 | visible only | virtualized |\n"));
                    assistant.AddCitation(new Citation(AiId.New("cit"), source));
                    break;
            }

            assistant.MergeMetadata(new GenerationMetadata
            {
                ModelId = "demo-model-1",
                ContextWindow = 200_000,
                Usage = new TokenUsage { InputTokens = 120 + i, OutputTokens = 60 + (i % 90) },
            });

            conversation.AddMessage(assistant);
        }

        return conversation;
    }
}
