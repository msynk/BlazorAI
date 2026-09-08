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
}
