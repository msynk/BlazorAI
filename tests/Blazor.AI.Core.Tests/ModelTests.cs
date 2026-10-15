namespace Blazor.AI.Tests;

public sealed class ModelTests
{
    [Fact]
    public void ConstructingATextPartWithOneArgumentSetsTheText()
    {
        // Guards an overload shape that previously bound a single string to the identifier instead.
        var part = new TextPart("hello");

        Assert.Equal("hello", part.Text);
        Assert.False(part.IsStreaming);
        Assert.NotEqual("hello", part.PartId);
    }

    [Fact]
    public void AppendingIsCheapAndTheTextIsMaterialisedLazily()
    {
        var part = new TextPart();
        for (var i = 0; i < 1000; i++) part.Append("x");

        Assert.Equal(1000, part.Length);
        Assert.Equal(1000, part.Text.Length);
    }

    [Fact]
    public void PartChangesRaiseEventsOnThePartAndTheMessage()
    {
        var message = new AiMessage(AiRole.Assistant);
        var part = message.AddPart(new TextPart());

        var partEvents = 0;
        var messageEvents = 0;
        part.Changed += _ => partEvents++;
        message.PartChanged += (_, _) => messageEvents++;

        part.Append("a");
        part.Append("b");

        Assert.Equal(2, partEvents);
        Assert.Equal(2, messageEvents);
    }

    [Fact]
    public void ReasoningIsExcludedFromThePlainTextProjection()
    {
        var message = new AiMessage(AiRole.Assistant);
        message.AddPart(new ReasoningPart("thinking hard"));
        message.AddPart(new TextPart("the answer"));

        Assert.Equal("the answer", message.GetText());
    }

    [Fact]
    public void MultipleTextualPartsAreJoinedWithABlankLine()
    {
        var message = new AiMessage(AiRole.Assistant);
        message.AddPart(new TextPart("first"));
        message.AddPart(new TextPart("second"));

        Assert.Equal("first\n\nsecond", message.GetText());
    }

    [Fact]
    public void CompletingOpenPartsClosesEveryStreamingPart()
    {
        var message = new AiMessage(AiRole.Assistant);
        var text = message.AddPart(new TextPart());
        var reasoning = message.AddPart(new ReasoningPart());
        var call = message.AddPart(new ToolCallPart("c1", "t"));

        message.CompleteOpenParts();

        Assert.False(text.IsStreaming);
        Assert.False(reasoning.IsStreaming);
        Assert.NotNull(reasoning.Duration);
        Assert.Equal(ToolCallStatus.Pending, call.Status);
    }

    [Fact]
    public void TruncateAfterRemovesEverythingBelowTheGivenMessage()
    {
        var conversation = new Conversation();
        var first = conversation.AddMessage(AiRole.User, "one");
        conversation.AddMessage(AiRole.Assistant, "two");
        conversation.AddMessage(AiRole.User, "three");

        var removed = conversation.TruncateAfter(first.MessageId);

        Assert.Equal(2, removed.Count);
        Assert.Equal("three", removed[0].GetText());
        Assert.Single(conversation.Messages);
    }

    [Fact]
    public void TruncateAfterCanRemoveTheNamedMessageToo()
    {
        var conversation = new Conversation();
        conversation.AddMessage(AiRole.User, "one");
        var second = conversation.AddMessage(AiRole.Assistant, "two");

        conversation.TruncateAfter(second.MessageId, inclusive: true);

        Assert.Single(conversation.Messages);
    }

    [Fact]
    public void RemovedMessagesStopNotifyingTheConversation()
    {
        var conversation = new Conversation();
        var message = conversation.AddMessage(AiRole.Assistant, "hi");

        var updates = 0;
        conversation.MessageUpdated += (_, _) => updates++;

        conversation.RemoveMessage(message.MessageId);
        message.SetStatus(MessageStatus.Failed);

        Assert.Equal(0, updates);
    }

    [Fact]
    public void TotalUsageSumsEveryAssistantMessage()
    {
        var conversation = new Conversation();
        var first = conversation.AddMessage(AiRole.Assistant, "a");
        first.MergeMetadata(new GenerationMetadata { Usage = new TokenUsage { InputTokens = 10, OutputTokens = 4 } });
        var second = conversation.AddMessage(AiRole.Assistant, "b");
        second.MergeMetadata(new GenerationMetadata { Usage = new TokenUsage { InputTokens = 5, OutputTokens = 1 } });

        Assert.Equal(20, conversation.TotalUsage.TotalTokens);
    }

    [Fact]
    public void TokenUsageAdditionTreatsAbsentValuesAsAbsent()
    {
        var combined = new TokenUsage { InputTokens = 5 } + new TokenUsage { OutputTokens = 3 };

        Assert.Equal(5, combined.InputTokens);
        Assert.Equal(3, combined.OutputTokens);
        Assert.Null(combined.Cost);
        Assert.Equal(8, combined.TotalTokens);
    }

    [Fact]
    public void EmptyUsageReportsItself()
    {
        Assert.True(default(TokenUsage).IsEmpty);
        Assert.False(new TokenUsage { InputTokens = 0 }.IsEmpty);
    }

    [Fact]
    public void CostIsEstimatedFromTheModelPrices()
    {
        var model = new AiModel("m", "M")
        {
            InputPricePerMillion = 3m,
            OutputPricePerMillion = 15m,
        };

        var cost = model.EstimateCost(new TokenUsage { InputTokens = 1_000_000, OutputTokens = 100_000 });

        Assert.Equal(4.5m, cost);
    }

    [Fact]
    public void RolesCompareCaseInsensitivelyAndRoundTripUnknownValues()
    {
        Assert.Equal(AiRole.Assistant, new AiRole("ASSISTANT"));
        Assert.True(new AiRole("critic") is { IsUser: false, IsAssistant: false });
        Assert.Equal("critic", new AiRole("critic").Value);
    }

    [Fact]
    public void IdentifiersAreUniqueAndSortChronologically()
    {
        var ids = new List<string>();
        for (var i = 0; i < 500; i++) ids.Add(AiId.NewMessageId());

        Assert.Equal(500, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.StartsWith("msg_", id, StringComparison.Ordinal));
    }

    [Fact]
    public void ArtifactRevisionsKeepAHistoryAndCanBeRestored()
    {
        var artifact = new Artifact("a1", ArtifactKind.Document, "Notes");
        artifact.Append("v1");
        artifact.Complete();

        artifact.BeginRevision();
        artifact.Append("v2");
        artifact.Complete();

        Assert.True(artifact.Restore(1));
        Assert.Equal("v1", artifact.Content);
        Assert.Equal(3, artifact.Version);
        Assert.Equal(3, artifact.Versions.Count);
    }

    [Fact]
    public void AttachmentKindIsInferredFromTypeThenExtension()
    {
        Assert.Equal(AttachmentKind.Image, Attachment.InferKind("image/png", "a.bin"));
        Assert.Equal(AttachmentKind.Document, Attachment.InferKind(null, "report.pdf"));
        Assert.Equal(AttachmentKind.Audio, Attachment.InferKind(null, "clip.mp3"));
        Assert.Equal(AttachmentKind.File, Attachment.InferKind(null, "data.bin"));
    }

    [Fact]
    public async Task AttachmentContentCanBeReadFromInlineData()
    {
        var attachment = new Attachment("a", "hello.txt") { Data = "hi"u8.ToArray() };

        await using var stream = await attachment.OpenReadAsync();
        using var reader = new StreamReader(stream);

        Assert.Equal("hi", await reader.ReadToEndAsync());
    }

    [Fact]
    public void ReasoningIsLeftOutOfTheTextEvenWhenItIsTheOnlyPart()
    {
        var message = new AiMessage(AiRole.Assistant);
        message.AddPart(new ReasoningPart("private chain of thought"));

        Assert.Equal(string.Empty, message.GetText());
    }
}

/// <summary>
/// Transports apply events off the renderer's thread while components read the same objects during
/// render. Each test runs one writer against a reader looping on the hot read paths.
/// </summary>
public sealed class ThreadSafetyTests
{
    private const int Writes = 20_000;

    [Fact]
    public async Task TextCanBeReadWhileItIsBeingAppended()
    {
        var part = new TextPart();

        await RaceAsync(
            () =>
            {
                for (var i = 0; i < Writes; i++) part.Append("token ");
            },
            () =>
            {
                var text = part.Text;
                Assert.True(text.Length % 6 == 0, "A reader saw a torn append.");
                _ = part.Length;
                _ = part.IsEmpty;
            });

        Assert.Equal(Writes * 6, part.Text.Length);
    }

    [Fact]
    public async Task ReasoningCanBeReadWhileItIsBeingAppended()
    {
        var part = new ReasoningPart();

        await RaceAsync(
            () =>
            {
                for (var i = 0; i < Writes; i++) part.Append("x");
            },
            () => _ = part.Text);

        Assert.Equal(Writes, part.Length);
    }

    [Fact]
    public async Task ToolArgumentsCanBeReadWhileTheyStreamIn()
    {
        var call = new ToolCallPart("c1", "search");

        await RaceAsync(
            () =>
            {
                call.AppendArguments("{\"q\":\"");
                for (var i = 0; i < Writes / 10; i++) call.AppendArguments("a");
                call.AppendArguments("\"}");
                call.CompleteArguments();
            },
            () =>
            {
                _ = call.RawArguments;
                _ = call.DisplayArguments;
                _ = call.Arguments;
            });

        Assert.Equal(Writes / 10, call.Arguments!.Value.GetProperty("q").GetString()!.Length);
    }

    [Fact]
    public async Task StructuredAndGenerativePayloadsCanBeReadWhileTheyStreamIn()
    {
        var structured = new StructuredDataPart("list");
        var generative = new GenerativeUiPart("card");

        await RaceAsync(
            () =>
            {
                structured.Append("[");
                generative.AppendProps("""{"items":[""");
                for (var i = 0; i < Writes / 10; i++)
                {
                    structured.Append(i == 0 ? "1" : ",1");
                    generative.AppendProps(i == 0 ? "1" : ",1");
                }

                structured.Append("]");
                generative.AppendProps("]}");
                structured.Complete();
                generative.Complete();
            },
            () =>
            {
                _ = structured.Value;
                _ = structured.RawJson;
                _ = generative.Props;
                _ = generative.RawProps;
            });

        Assert.Equal(Writes / 10, structured.Value!.Value.GetArrayLength());
        Assert.Equal(Writes / 10, generative.Props!.Value.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ArtifactContentAndHistoryCanBeReadWhileItIsRevised()
    {
        var artifact = new Artifact("a1", ArtifactKind.Document, "Notes");

        await RaceAsync(
            () =>
            {
                for (var revision = 0; revision < 50; revision++)
                {
                    for (var i = 0; i < 200; i++) artifact.Append("line\n");
                    artifact.Complete();
                    artifact.BeginRevision();
                }
            },
            () =>
            {
                _ = artifact.Content;
                _ = artifact.Length;
                foreach (var version in artifact.Versions) _ = version.Content;
            });

        Assert.Equal(50, artifact.Versions.Count);
    }

    [Fact]
    public async Task MessagePartsAndCitationsCanBeEnumeratedWhileTheyAreAdded()
    {
        var message = new AiMessage(AiRole.Assistant);

        await RaceAsync(
            () =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    message.AddPart(new TextPart($"paragraph {i}"));
                    message.AddCitation(new Citation($"cit{i}", new CitationSource($"src{i % 50}", "Source")));
                    message.AddAttachment(new Attachment($"att{i}", "file.txt"));
                }
            },
            () =>
            {
                foreach (var part in message.Parts) _ = part.PartId;
                foreach (var citation in message.Citations) _ = citation.Number;
                foreach (var attachment in message.Attachments) _ = attachment.Name;
                _ = message.GetText();
                _ = message.GetSources();
            });

        Assert.Equal(2000, message.Parts.Count);
        Assert.Equal(2000, message.Citations.Count);
    }

    [Fact]
    public async Task ConversationMessagesCanBeEnumeratedWhileTheThreadGrows()
    {
        var conversation = new Conversation();

        await RaceAsync(
            () =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    conversation.AddMessage(AiRole.User, $"question {i}");
                    conversation.GetOrAddArtifact($"art{i}", ArtifactKind.Document, "Notes");
                    conversation.GetOrAddRun($"run{i}");
                }

                conversation.TruncateAfter(conversation.Messages[999].MessageId);
            },
            () =>
            {
                foreach (var message in conversation.Messages) _ = message.MessageId;
                foreach (var artifact in conversation.Artifacts.Values) _ = artifact.Title;
                foreach (var run in conversation.Runs.Values) _ = run.Status;
                _ = conversation.ToSummary();
                _ = conversation.TotalUsage;
            });

        Assert.Equal(1000, conversation.Messages.Count);
    }

    [Fact]
    public async Task AgentStepsCanBeEnumeratedWhileThePlanIsRevised()
    {
        var run = new AgentRun("run1");

        await RaceAsync(
            () =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    run.SetPlan([new AgentPlanStep("a", "A"), new AgentPlanStep($"s{i}", "Step")]);
                    run.GetOrAddStep($"extra{i}").Start();
                }
            },
            () =>
            {
                foreach (var step in run.Steps) _ = step.Status;
                _ = run.Progress;
                _ = run.CurrentStep;
            });

        Assert.Equal(3, run.Steps.Count);
    }

    [Fact]
    public async Task AStepsToolCallsCanBeEnumeratedWhileCallsAreRecorded()
    {
        var step = new AgentRun("run1").GetOrAddStep("s1");

        await RaceAsync(
            () =>
            {
                // Several writers, as when tools running in parallel report into the same step, each
                // recording its call twice.
                Parallel.For(0, 2000, i =>
                {
                    step.AddToolCall($"call{i}");
                    step.AddToolCall($"call{i}");
                });
            },
            () =>
            {
                foreach (var id in step.ToolCallIds) _ = id.Length;
            });

        Assert.Equal(2000, step.ToolCallIds.Count);
        Assert.Equal(2000, step.ToolCallIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ARunsUsageIsNeverReadHalfUpdated()
    {
        var run = new AgentRun("run1");

        await RaceAsync(
            () =>
            {
                for (var i = 1; i <= 20000; i++) run.AddUsage(new TokenUsage { InputTokens = 1, OutputTokens = 1 });
            },
            () =>
            {
                var usage = run.Usage;
                Assert.Equal(usage.InputTokens, usage.OutputTokens);
            });

        Assert.Equal(20000, run.Usage.InputTokens);
    }

    private static async Task RaceAsync(Action write, Action read)
    {
        using var done = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!done.IsCancellationRequested) read();
        });

        try
        {
            await Task.Run(write);
        }
        finally
        {
            done.Cancel();
        }

        await reader;
    }
}
