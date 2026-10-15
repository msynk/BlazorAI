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
    public void ToolCallsAreRecordedOnTheRunningStep()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new AgentPlanEvent("r1",
        [
            new AgentPlanStep("s1", "Search"),
            new AgentPlanStep("s2", "Summarise"),
        ]));
        processor.Apply(new ToolCallStartedEvent("call_0", "search"));
        processor.Apply(new AgentStepStartedEvent("r1", "s1"));
        processor.Apply(new ToolCallStartedEvent("call_1", "search"));
        processor.Apply(new ToolCallStartedEvent("call_2", "search"));
        processor.Apply(new AgentStepCompletedEvent("r1", "s1"));
        processor.Apply(new ToolCallStartedEvent("call_3", "search"));

        var run = conversation.FindRun("r1")!;
        Assert.Equal(["call_1", "call_2"], run.Steps[0].ToolCallIds);
        Assert.Empty(run.Steps[1].ToolCallIds);
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
    public void RepeatedUsageUpdatesWithinAStreamReplaceRatherThanAdd()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new AgentRunStartedEvent("run1"));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 100, OutputTokens = 1 }));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 100, OutputTokens = 20 }));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 100, OutputTokens = 42, Cost = 0.01m }));
        processor.EndGeneration();

        var expected = new TokenUsage { InputTokens = 100, OutputTokens = 42, Cost = 0.01m };
        Assert.Equal(expected, conversation.Messages[0].Metadata!.Usage);
        Assert.Equal(expected, conversation.FindRun("run1")!.Usage);
    }

    [Fact]
    public void UsageFromSeparateStreamsAddsUpFieldByField()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new AgentRunStartedEvent("run1"));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 100, OutputTokens = 10, ReasoningTokens = 5, Cost = 0.01m }));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 100, OutputTokens = 30, ReasoningTokens = 8, Cost = 0.02m }));

        processor.BeginStream();
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 150, OutputTokens = 5, CachedInputTokens = 100, Cost = 0.005m }));
        processor.Apply(new MetadataEvent(new GenerationMetadata
        {
            ModelId = "m1",
            Usage = new TokenUsage { InputTokens = 150, OutputTokens = 12, CachedInputTokens = 100, Cost = 0.006m },
        }));
        processor.EndGeneration();

        var expected = new TokenUsage
        {
            InputTokens = 250,
            OutputTokens = 42,
            CachedInputTokens = 100,
            ReasoningTokens = 8,
            Cost = 0.026m,
        };

        var metadata = conversation.Messages[0].Metadata!;
        Assert.Equal(expected, metadata.Usage);
        Assert.Null(metadata.Usage.CacheWriteTokens);
        Assert.Equal("m1", metadata.ModelId);
        Assert.Equal(expected, conversation.FindRun("run1")!.Usage);
    }

    [Fact]
    public void ARunSpanningSeveralTurnsSumsTheUsageOfEach()
    {
        var (conversation, processor) = Create();

        processor.BeginGeneration();
        processor.Apply(new AgentRunStartedEvent("run1"));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 10, OutputTokens = 2 }));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 10, OutputTokens = 4 }));
        processor.EndGeneration();

        processor.BeginGeneration();
        processor.Apply(new AgentStepStartedEvent("run1", "s2"));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 20, OutputTokens = 1 }));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 20, OutputTokens = 3 }));
        processor.EndGeneration();

        Assert.Equal(new TokenUsage { InputTokens = 10, OutputTokens = 4 }, conversation.Messages[0].Metadata!.Usage);
        Assert.Equal(new TokenUsage { InputTokens = 20, OutputTokens = 3 }, conversation.Messages[1].Metadata!.Usage);
        Assert.Equal(new TokenUsage { InputTokens = 30, OutputTokens = 7 }, conversation.FindRun("run1")!.Usage);
        Assert.Equal(conversation.TotalUsage, conversation.FindRun("run1")!.Usage);
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

    [Fact]
    public void AToolCallIsReportedReadyOnlyOnce()
    {
        var (_, processor) = Create();
        var ready = 0;
        processor.ToolCallReady += _ => ready++;
        processor.BeginGeneration();

        var arguments = JsonDocument.Parse("""{"q":"x"}""").RootElement;
        processor.Apply(new ToolCallStartedEvent("call1", "search") { Arguments = arguments });
        processor.Apply(new ToolCallCompletedEvent("call1", arguments));
        processor.Apply(new ToolCallStartedEvent("call2", "search"));
        processor.Apply(new ToolCallDeltaEvent("call2", """{"q":"y"}"""));
        processor.Apply(new ToolCallCompletedEvent("call2"));
        processor.Apply(new ToolCallCompletedEvent("call2"));

        Assert.Equal(2, ready);
    }

    [Theory]
    [InlineData(MessageStatus.Cancelled, AgentRunStatus.Cancelled)]
    [InlineData(MessageStatus.Failed, AgentRunStatus.Failed)]
    public void AnInterruptedGenerationEndsTheRunningAgentRun(MessageStatus ending, AgentRunStatus expected)
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new AgentRunStartedEvent("run1"));
        processor.Apply(new AgentStepStartedEvent("run1", "search"));
        processor.EndGeneration(ending);

        Assert.Equal(expected, conversation.Runs["run1"].Status);
    }

    [Fact]
    public void ARunWaitingForInputSurvivesTheEndOfTheMessage()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new AgentRunStartedEvent("run1"));
        processor.Apply(new AgentInputRequestedEvent("run1", "When?"));
        processor.Apply(new MessageCompletedEvent());
        processor.EndGeneration();

        Assert.Equal(AgentRunStatus.WaitingForInput, conversation.Runs["run1"].Status);
    }

    [Fact]
    public void PlannedStepsReportTheirProgressThroughTheRun()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();
        processor.Apply(new AgentRunStartedEvent("run1"));
        processor.Apply(new AgentPlanEvent("run1", [new AgentPlanStep("a", "A"), new AgentPlanStep("b", "B")]));

        var run = conversation.Runs["run1"];
        var changes = 0;
        run.Changed += _ => changes++;

        processor.Apply(new AgentStepStartedEvent("run1", "a"));
        processor.Apply(new AgentStepCompletedEvent("run1", "a"));

        Assert.True(changes >= 2, "A planned step changing must notify observers of the run.");
        Assert.Equal(0.5, run.Progress);
    }

    [Fact]
    public void APlanThatRepeatsAStepIdentifierDoesNotBreakTheNextRevision()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new AgentPlanEvent("run1", [new AgentPlanStep("a", "A"), new AgentPlanStep("a", "A again")]));
        processor.Apply(new AgentStepCompletedEvent("run1", "a"));
        processor.Apply(new AgentPlanEvent("run1", [new AgentPlanStep("a", "A"), new AgentPlanStep("b", "B")]));

        var steps = conversation.Runs["run1"].Steps;
        Assert.Equal(["a", "b"], steps.Select(s => s.StepId));
        Assert.Equal(AgentStepStatus.Completed, steps[0].Status);
    }

    [Fact]
    public void AnArtifactCreatedWithContentAndThenCompletedRecordsOneVersion()
    {
        var (conversation, processor) = Create();
        processor.BeginGeneration();

        processor.Apply(new ArtifactCreatedEvent("art1", ArtifactKind.Document, "Notes") { Content = "body" });
        processor.Apply(new ArtifactCompletedEvent("art1"));

        Assert.Single(conversation.FindArtifact("art1")!.Versions);
    }
}
