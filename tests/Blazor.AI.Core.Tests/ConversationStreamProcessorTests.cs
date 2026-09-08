using System.Text.Json;

namespace Blazor.AI.Tests;

public sealed class ConversationStreamProcessorTests
{
    private static (Conversation Conversation, ConversationStreamProcessor Processor) Create()
    {
        var conversation = new Conversation();
        return (conversation, new ConversationStreamProcessor(conversation));
    }

    [Fact]
    public void TextDeltasAccumulateIntoOnePart()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new TextDeltaEvent("Hello"));
        processor.Apply(new TextDeltaEvent(", world"));
        processor.EndGeneration();

        var message = Assert.Single(conversation.Messages);
        var part = Assert.IsType<TextPart>(Assert.Single(message.Parts));
        Assert.Equal("Hello, world", part.Text);
        Assert.False(part.IsStreaming);
        Assert.Equal(MessageStatus.Complete, message.Status);
    }

    [Fact]
    public void TextAfterAToolCallStartsANewPart()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new TextDeltaEvent("Looking that up."));
        processor.Apply(new ToolCallStartedEvent("call1", "search"));
        processor.Apply(new ToolCallCompletedEvent("call1", JsonDocument.Parse("""{"q":"x"}""").RootElement));
        processor.Apply(new ToolResultEvent("call1", ToolOutcome.FromText("found")));
        processor.Apply(new TextDeltaEvent("Here it is."));
        processor.EndGeneration();

        var message = Assert.Single(conversation.Messages);
        Assert.Collection(
            message.Parts,
            p => Assert.Equal("Looking that up.", Assert.IsType<TextPart>(p).Text),
            p => Assert.Equal("call1", Assert.IsType<ToolCallPart>(p).ToolCallId),
            p => Assert.Equal("Here it is.", Assert.IsType<TextPart>(p).Text));
    }

    [Fact]
    public void ReasoningGoesToItsOwnPartAndOutOfTheTextProjection()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new ReasoningDeltaEvent("weighing options"));
        processor.Apply(new TextDeltaEvent("The answer is 4."));
        processor.EndGeneration();

        var message = Assert.Single(conversation.Messages);
        Assert.Equal(2, message.Parts.Count);
        Assert.IsType<ReasoningPart>(message.Parts[0]);
        Assert.Equal("The answer is 4.", message.GetText());
    }

    [Fact]
    public void ToolCallArgumentsStreamAndParseOnCompletion()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new ToolCallStartedEvent("c1", "get_weather"));
        processor.Apply(new ToolCallDeltaEvent("c1", """{"city":"Sea"""));

        var call = conversation.Messages[0].FindToolCall("c1");
        Assert.NotNull(call);
        Assert.Equal("Sea", call!.DisplayArguments!.Value.GetProperty("city").GetString());

        processor.Apply(new ToolCallDeltaEvent("c1", """ttle"}"""));
        processor.Apply(new ToolCallCompletedEvent("c1"));

        Assert.Equal(ToolCallStatus.Pending, call.Status);
        Assert.Equal("Seattle", call.Arguments!.Value.GetProperty("city").GetString());
    }

    [Fact]
    public void ToolCallReadyFiresOnceArgumentsComplete()
    {
        var (_, processor) = Create();
        processor.BeginGeneration();

        ToolCallPart? ready = null;
        processor.ToolCallReady += part => ready = part;

        processor.Apply(new ToolCallStartedEvent("c1", "search"));
        Assert.Null(ready);

        processor.Apply(new ToolCallCompletedEvent("c1"));
        Assert.NotNull(ready);
        Assert.Equal("search", ready!.ToolName);
    }

    [Fact]
    public void CitationsAreNumberedAndDeduplicatedBySource()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        var source = new CitationSource("s1", "Docs");
        processor.Apply(new CitationEvent(new Citation("c1", source)));
        processor.Apply(new CitationEvent(new Citation("c2", new CitationSource("s2", "Other"))));
        processor.Apply(new CitationEvent(new Citation("c3", source)));

        var message = conversation.Messages[0];
        Assert.Equal(3, message.Citations.Count);
        Assert.Equal(1, message.Citations[0].Number);
        Assert.Equal(2, message.Citations[1].Number);
        Assert.Equal(1, message.Citations[2].Number);
        Assert.Equal(2, message.GetSources().Count);
    }

    [Fact]
    public void ArtifactsStreamOntoTheConversationAndAreReferencedByTheMessage()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new ArtifactCreatedEvent("a1", ArtifactKind.Code, "Solver") { Language = "csharp" });
        processor.Apply(new ArtifactDeltaEvent("a1", "int Add("));
        processor.Apply(new ArtifactDeltaEvent("a1", "int a, int b) => a + b;"));
        processor.Apply(new ArtifactCompletedEvent("a1") { Summary = "first draft" });

        var artifact = conversation.FindArtifact("a1");
        Assert.NotNull(artifact);
        Assert.Equal("int Add(int a, int b) => a + b;", artifact!.Content);
        Assert.False(artifact.IsStreaming);
        Assert.Single(artifact.Versions);
        Assert.Contains(conversation.Messages[0].Parts, p => p is ArtifactPart { ArtifactId: "a1" });
    }

    [Fact]
    public void RevisingAnArtifactKeepsTheEarlierVersion()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new ArtifactCreatedEvent("a1", ArtifactKind.Document, "Notes"));
        processor.Apply(new ArtifactDeltaEvent("a1", "first"));
        processor.Apply(new ArtifactCompletedEvent("a1"));

        processor.Apply(new ArtifactCreatedEvent("a1", ArtifactKind.Document, "Notes"));
        processor.Apply(new ArtifactDeltaEvent("a1", "second"));
        processor.Apply(new ArtifactCompletedEvent("a1"));

        var artifact = conversation.FindArtifact("a1")!;
        Assert.Equal(2, artifact.Version);
        Assert.Equal("second", artifact.Content);
        Assert.Equal("first", artifact.Versions[0].Content);
    }

    [Fact]
    public void AgentPlanAndStepsBuildARun()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new AgentRunStartedEvent("r1") { AgentName = "Researcher", Goal = "Find sources" });
        processor.Apply(new AgentPlanEvent("r1",
        [
            new AgentPlanStep("s1", "Search"),
            new AgentPlanStep("s2", "Summarise"),
        ]));
        processor.Apply(new AgentStepStartedEvent("r1", "s1"));
        processor.Apply(new AgentStepCompletedEvent("r1", "s1") { Summary = "8 results" });
        processor.Apply(new AgentRunCompletedEvent("r1"));

        var run = conversation.FindRun("r1")!;
        Assert.Equal("Researcher", run.AgentName);
        Assert.Equal(2, run.Steps.Count);
        Assert.Equal(AgentStepStatus.Completed, run.Steps[0].Status);
        Assert.Equal("8 results", run.Steps[0].Summary);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal(0.5, run.Progress);
    }

    [Fact]
    public void PlanRevisionPreservesStateOfSurvivingSteps()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new AgentPlanEvent("r1", [new AgentPlanStep("s1", "Search")]));
        processor.Apply(new AgentStepStartedEvent("r1", "s1"));
        processor.Apply(new AgentStepCompletedEvent("r1", "s1"));
        processor.Apply(new AgentPlanEvent("r1",
        [
            new AgentPlanStep("s1", "Search the web"),
            new AgentPlanStep("s2", "Summarise"),
        ]));

        var run = conversation.FindRun("r1")!;
        Assert.Equal("Search the web", run.Steps[0].Title);
        Assert.Equal(AgentStepStatus.Completed, run.Steps[0].Status);
        Assert.Equal(AgentStepStatus.Pending, run.Steps[1].Status);
    }

    [Fact]
    public void TerminalErrorFailsTheMessageAndClosesOpenParts()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new TextDeltaEvent("partial"));
        processor.Apply(new ErrorEvent(new AiError("rate limited") { Kind = AiErrorKind.RateLimit, IsRetryable = true }));

        var message = conversation.Messages[0];
        Assert.Equal(MessageStatus.Failed, message.Status);
        Assert.Equal("rate limited", message.Error!.Message);
        Assert.False(Assert.IsType<TextPart>(message.Parts[0]).IsStreaming);
    }

    [Fact]
    public void NonTerminalErrorBecomesAPartAndLeavesTheMessageAlive()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new TextDeltaEvent("one tool failed"));
        processor.Apply(new ErrorEvent(new AiError("tool blew up"), isTerminal: false));
        processor.EndGeneration();

        var message = conversation.Messages[0];
        Assert.Equal(MessageStatus.Complete, message.Status);
        Assert.Contains(message.Parts, p => p is ErrorPart);
    }

    [Fact]
    public void UsageAndMetadataAreMergedNotReplaced()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new MetadataEvent(new GenerationMetadata { ModelId = "m1" }));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 10, OutputTokens = 5 }));
        processor.Apply(new MessageCompletedEvent(FinishReason.Stop));
        processor.EndGeneration();

        var metadata = conversation.Messages[0].Metadata!;
        Assert.Equal("m1", metadata.ModelId);
        Assert.Equal(15, metadata.Usage.TotalTokens);
        Assert.Equal(FinishReason.Stop, metadata.FinishReason);
        Assert.NotNull(metadata.CompletedAt);
    }

    [Fact]
    public void StatusTextIsTransientAndClearedOnCompletion()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new StatusEvent("Searching the web"));
        Assert.Equal("Searching the web", conversation.Messages[0].StatusText);

        processor.Apply(new MessageCompletedEvent());
        Assert.Null(conversation.Messages[0].StatusText);
    }

    [Fact]
    public void ExplicitPartIdsKeepParallelPartsSeparate()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new TextDeltaEvent("left ") { PartId = "p1" });
        processor.Apply(new TextDeltaEvent("right ") { PartId = "p2" });
        processor.Apply(new TextDeltaEvent("side") { PartId = "p1" });

        var message = conversation.Messages[0];
        Assert.Equal(2, message.Parts.Count);
        Assert.Equal("left side", message.FindPart<TextPart>("p1")!.Text);
        Assert.Equal("right ", message.FindPart<TextPart>("p2")!.Text);
    }

    [Fact]
    public void StructuredDataStreamsAndIsReadableWhilePartial()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new StructuredDataDeltaEvent("""{"name":"Ada""") { SchemaName = "person" });
        var part = Assert.IsType<StructuredDataPart>(conversation.Messages[0].Parts[0]);
        Assert.Equal("Ada", part.Value!.Value.GetProperty("name").GetString());

        processor.Apply(new StructuredDataDeltaEvent("\",\"age\":36}"));
        processor.Apply(new PartCompletedEvent { PartId = part.PartId });

        Assert.False(part.IsStreaming);
        Assert.Equal(36, part.Value!.Value.GetProperty("age").GetInt32());
        Assert.Equal("person", part.SchemaName);
    }
}
