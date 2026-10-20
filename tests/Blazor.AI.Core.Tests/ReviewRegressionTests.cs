using System.Runtime.CompilerServices;
using System.Text.Json;
using Blazor.AI.Serialization;
using Xunit;

namespace Blazor.AI.Core.Tests;

/// <summary>
/// Regression tests for defects found in the first full review. Each test name states the
/// guarantee that was missing.
/// </summary>
public class ReviewRegressionTests
{
    [Fact]
    public void AToolCallAnnouncedWithArgumentsAndThenCompletedIsReadyOnlyOnce()
    {
        var conversation = new Conversation();
        var processor = new ConversationStreamProcessor(conversation);
        var fired = 0;
        processor.ToolCallReady += _ => fired++;

        processor.BeginGeneration();
        var args = JsonDocument.Parse("""{"q":1}""").RootElement;
        processor.Apply(new ToolCallStartedEvent("c1", "search") { Arguments = args });
        processor.Apply(new ToolCallCompletedEvent("c1", args));
        processor.Apply(new ToolCallCompletedEvent("c1"));

        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task ACallTheTransportAlreadyAnsweredIsNotExecutedAgainLocally()
    {
        var executions = 0;
        var tools = new ToolRegistry([AiTool.Create<JsonElement, string>("lookup", "look", (_, _) =>
        {
            executions++;
            return ValueTask.FromResult("local");
        })]);

        var transport = new ScriptedTransport(
        [
            new MessageStartedEvent(),
            new ToolCallStartedEvent("c1", "lookup") { Arguments = JsonDocument.Parse("{}").RootElement },
            new ToolResultEvent("c1", ToolOutcome.FromText("remote")),
            new MessageCompletedEvent(),
        ]);

        var session = new ChatSession(transport, tools: tools, options: new ChatSessionOptions { Pacing = null });
        var reply = await session.SubmitAsync("go");

        Assert.Equal(0, executions);
        Assert.Equal("remote", reply!.FindToolCall("c1")!.Result!.Text);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public void CreatedWithContentThenCompletedProducesOneVersion()
    {
        var conversation = new Conversation();
        var processor = new ConversationStreamProcessor(conversation);
        processor.BeginGeneration();

        processor.Apply(new ArtifactCreatedEvent("a1", ArtifactKind.Code, "t") { Content = "x" });
        processor.Apply(new ArtifactCompletedEvent("a1") { Summary = "first draft" });

        var version = Assert.Single(conversation.Artifacts["a1"].Versions);
        Assert.Equal("first draft", version.Summary);
    }

    [Fact]
    public void AMessageHoldingOnlyReasoningHasNoPlainText()
    {
        var message = new AiMessage(AiRole.Assistant);
        message.AddPart(new ReasoningPart("private"));
        Assert.Equal(string.Empty, message.GetText());
    }

    [Fact]
    public void PiecemealUsageReportsAreMergedFieldByField()
    {
        var conversation = new Conversation();
        var processor = new ConversationStreamProcessor(conversation);
        processor.BeginGeneration();
        processor.Apply(new AgentRunStartedEvent("r1"));

        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 100 }));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { OutputTokens = 40 }));
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { OutputTokens = 50 }));

        var usage = conversation.Messages[0].Metadata!.Usage;
        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(50, usage.OutputTokens);
        Assert.Equal(150, conversation.Runs["r1"].Usage.TotalTokens);
    }

    [Fact]
    public void StepsCreatedByAPlanNotifyTheRunWhenTheyChange()
    {
        var run = new AgentRun("r1");
        run.SetPlan([new AgentPlanStep("s1", "First")]);
        var before = run.Version;

        run.Steps[0].Start();

        Assert.True(run.Version > before);
    }

    [Fact]
    public async Task PacingKeepsSmoothingWhenCompletionArrivesRightAfterTheText()
    {
        var deltas = 0;
        var sawCompletedLast = false;
        await foreach (var evt in Burst().Paced(new TextPacingOptions { Interval = TimeSpan.FromMilliseconds(1) }))
        {
            if (evt is TextDeltaEvent) deltas++;
            sawCompletedLast = evt is MessageCompletedEvent;
        }

        Assert.True(deltas > 3, $"expected several paced deltas, got {deltas}");
        Assert.True(sawCompletedLast);
    }

    [Fact]
    public async Task ARenderRequestedDuringAManualFlushIsNotLost()
    {
        var renders = 0;
        var gate = new TaskCompletionSource();
        RenderScheduler? scheduler = null;
        scheduler = new RenderScheduler(async () =>
        {
            renders++;
            if (renders == 1)
            {
                scheduler!.Request();
                await gate.Task;
            }
        }, TimeSpan.FromMilliseconds(5));

        var flush = scheduler.FlushAsync();
        gate.SetResult();
        await flush;

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (renders < 2 && DateTime.UtcNow < deadline) await Task.Delay(10);

        Assert.Equal(2, renders);
        await scheduler.DisposeAsync();
    }

    [Fact]
    public void ANumberEndingInADecimalPointIsNotTreatedAsComplete()
    {
        var repaired = PartialJson.TryParse("""{"a": 1.""");
        Assert.NotNull(repaired);
        Assert.Equal(JsonValueKind.Object, repaired!.Value.ValueKind);
    }

    [Fact]
    public async Task TheTimeoutCoversTheTransportOnlyAndBecomesARetryableError()
    {
        var transport = new HangingTransport();
        var session = new ChatSession(transport, options: new ChatSessionOptions
        {
            Pacing = null,
            Timeout = TimeSpan.FromMilliseconds(50),
        });

        var reply = await session.SubmitAsync("hello");

        Assert.Equal(MessageStatus.Failed, reply!.Status);
        Assert.Equal(AiErrorKind.Timeout, session.LastError!.Kind);
        Assert.True(session.LastError.IsRetryable);
    }

    [Fact]
    public void RemovingMessagesPrunesTheArtifactsAndRunsTheyOwned()
    {
        var conversation = new Conversation();
        var processor = new ConversationStreamProcessor(conversation);
        conversation.AddMessage(AiRole.User, "write");
        var assistant = processor.BeginGeneration();
        processor.Apply(new AgentRunStartedEvent("r1"));
        processor.Apply(new ArtifactCreatedEvent("a1", ArtifactKind.Document, "Doc") { Content = "hi" });
        processor.Apply(new MessageCompletedEvent());
        processor.EndGeneration();

        Assert.Single(conversation.Artifacts);
        Assert.Single(conversation.Runs);

        conversation.TruncateAfter(assistant.MessageId, inclusive: true);

        Assert.Empty(conversation.Artifacts);
        Assert.Empty(conversation.Runs);
    }

    [Fact]
    public void AConversationRoundTripsThroughJson()
    {
        var conversation = new Conversation("conv_1");
        conversation.SetTitle("Round trip");
        var user = conversation.AddMessage(AiRole.User, "Draft a README");
        user.AddAttachment(new Attachment("att_1", "notes.txt") { ContentType = "text/plain", Size = 12, Url = "https://x/notes.txt" });

        var processor = new ConversationStreamProcessor(conversation);
        processor.BeginGeneration();
        processor.Apply(new AgentRunStartedEvent("r1") { AgentName = "writer", Goal = "README" });
        processor.Apply(new AgentPlanEvent("r1", [new AgentPlanStep("s1", "Outline"), new AgentPlanStep("s2", "Write")]));
        processor.Apply(new AgentStepStartedEvent("r1", "s1"));
        processor.Apply(new ReasoningDeltaEvent("thinking..."));
        processor.Apply(new TextDeltaEvent("Here is a draft. "));
        processor.Apply(new ToolCallStartedEvent("c1", "search") { Arguments = JsonDocument.Parse("""{"q":"readme"}""").RootElement });
        processor.Apply(new ToolResultEvent("c1", ToolOutcome.Success(new { hits = 3 })));
        processor.Apply(new CitationEvent(new Citation("cit1", new CitationSource("src1", "Docs") { Url = "https://docs" }) { StartIndex = 0, EndIndex = 4 }));
        processor.Apply(new ArtifactCreatedEvent("a1", ArtifactKind.Document, "README") { Content = "# Hello" });
        processor.Apply(new StructuredDataEvent(JsonDocument.Parse("""{"score":9}""").RootElement) { SchemaName = "score" });
        processor.Apply(new GenerativeUiEvent("card", JsonDocument.Parse("""{"city":"Oslo"}""").RootElement));
        processor.Apply(new ImageEvent("https://img/1.png") { Alt = "one" });
        processor.Apply(new UsageUpdatedEvent(new TokenUsage { InputTokens = 10, OutputTokens = 20 }));
        processor.Apply(new AgentStepCompletedEvent("r1", "s1"));
        processor.Apply(new AgentRunCompletedEvent("r1"));
        processor.Apply(new MessageCompletedEvent());
        processor.EndGeneration();
        conversation.Messages[1].SetFeedback(FeedbackKind.Positive, "nice");
        conversation.Messages[1].AddPart(new CustomJsonPart("acme:invoice", JsonDocument.Parse("""{"total":5}""").RootElement));

        var json = ConversationSerializer.ToJson(conversation);
        var restored = ConversationSerializer.FromJson(json);

        Assert.Equal("conv_1", restored.ConversationId);
        Assert.Equal("Round trip", restored.Title);
        Assert.Equal(2, restored.Messages.Count);
        Assert.Equal("notes.txt", restored.Messages[0].Attachments[0].Name);

        var assistant = restored.Messages[1];
        Assert.Equal(MessageStatus.Complete, assistant.Status);
        Assert.Equal(FeedbackKind.Positive, assistant.Feedback);
        Assert.Equal(30, assistant.Metadata!.Usage.TotalTokens);
        Assert.Equal(conversation.Messages[1].Parts.Count, assistant.Parts.Count);
        Assert.Equal(conversation.Messages[1].Parts.Select(p => p.Kind), assistant.Parts.Select(p => p.Kind));
        Assert.Equal("thinking...", assistant.PartsOf<ReasoningPart>().Single().Text);
        Assert.Equal(ToolCallStatus.Completed, assistant.FindToolCall("c1")!.Status);
        Assert.Equal(3, assistant.FindToolCall("c1")!.Result!.Value!.Value.GetProperty("hits").GetInt32());
        Assert.Equal(1, assistant.Citations.Single().Number);
        Assert.Equal(9, assistant.PartsOf<StructuredDataPart>().Single().Value!.Value.GetProperty("score").GetInt32());
        Assert.Equal("Oslo", assistant.PartsOf<GenerativeUiPart>().Single().Props!.Value.GetProperty("city").GetString());
        Assert.Equal("acme:invoice", assistant.PartsOf<CustomJsonPart>().Single().Kind);
        Assert.Same(restored.Runs["r1"], assistant.Run);

        var artifact = restored.Artifacts["a1"];
        Assert.Equal("# Hello", artifact.Content);
        Assert.False(artifact.IsStreaming);
        Assert.Single(artifact.Versions);

        var run = restored.Runs["r1"];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal(2, run.Steps.Count);
        Assert.Equal(AgentStepStatus.Completed, run.Steps[0].Status);

        // Serialising the restored conversation again yields the same document.
        Assert.Equal(json, ConversationSerializer.ToJson(restored));
    }

    [Fact]
    public void AnInterruptedMessageIsRestoredAsCancelled()
    {
        var conversation = new Conversation();
        var processor = new ConversationStreamProcessor(conversation);
        processor.BeginGeneration();
        processor.Apply(new TextDeltaEvent("half"));
        processor.Apply(new ToolCallStartedEvent("c1", "slow"));

        var restored = ConversationSerializer.ToConversation(ConversationSerializer.ToSnapshot(conversation));

        Assert.Equal(MessageStatus.Cancelled, restored.Messages[0].Status);
        Assert.Equal(ToolCallStatus.Cancelled, restored.Messages[0].FindToolCall("c1")!.Status);
    }

    [Fact]
    public void RolesAndKindsSerializeAsStrings()
    {
        var options = ConversationSerializerOptions.CreateJsonOptions();
        Assert.Equal("\"assistant\"", JsonSerializer.Serialize(AiRole.Assistant, options));
        Assert.Equal("\"html\"", JsonSerializer.Serialize(ArtifactKind.Html, options));
        Assert.Equal(AiRole.Tool, JsonSerializer.Deserialize<AiRole>("\"tool\"", options));
    }

    private static async IAsyncEnumerable<AiEvent> Burst([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new MessageStartedEvent();
        for (var i = 0; i < 60; i++) yield return new TextDeltaEvent("word ");
        yield return new MessageCompletedEvent();
        await Task.CompletedTask;
    }

    private sealed class HangingTransport : IAiTransport
    {
        public async IAsyncEnumerable<AiEvent> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new MessageStartedEvent();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class ScriptedTransport(IReadOnlyList<AiEvent> firstRound, IReadOnlyList<AiEvent>? secondRound = null) : IAiTransport
    {
        public int Calls { get; private set; }

        public async IAsyncEnumerable<AiEvent> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            var events = Calls == 1 || secondRound is null ? firstRound : secondRound;
            foreach (var evt in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return evt;
            }

            await Task.CompletedTask;
        }
    }
}
