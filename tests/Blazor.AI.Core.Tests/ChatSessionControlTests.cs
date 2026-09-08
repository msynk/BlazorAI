namespace Blazor.AI.Tests;

public sealed class ChatSessionControlTests
{
    private static ChatSessionOptions Immediate => new() { Pacing = null };

    [Fact]
    public async Task StopCancelsTheTurnAndKeepsThePartialAnswer()
    {
        await using var session = new ChatSession(new HangingTransport(), null, Immediate);

        var turn = session.SubmitAsync("go");
        await WaitFor(() => session.Conversation.Messages.Count == 2
            && session.Conversation.Messages[1].GetText().Length > 0);

        session.Stop();
        await turn;

        var assistant = session.Conversation.Messages[1];
        Assert.Equal(MessageStatus.Cancelled, assistant.Status);
        Assert.NotEmpty(assistant.GetText());
        Assert.Equal(AiErrorKind.Cancelled, session.LastError!.Kind);
        Assert.False(session.IsGenerating);
    }

    [Fact]
    public async Task ATransportFailureBecomesARetryableErrorRatherThanAnException()
    {
        var transport = new FakeTransport { Throw = new InvalidOperationException("boom") };
        await using var session = new ChatSession(transport, null, Immediate);

        await session.SubmitAsync("go");

        var assistant = session.Conversation.Messages[1];
        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.Equal("boom", assistant.Error!.Message);
        Assert.True(assistant.Error.IsRetryable);
    }

    [Fact]
    public async Task RetryReplacesTheFailedAnswerAndKeepsTheQuestion()
    {
        var attempts = 0;
        var transport = new FakeTransport(_ => attempts++ == 0
            ? [new ErrorEvent(new AiError("transient") { IsRetryable = true })]
            : [new TextDeltaEvent("second time lucky"), new MessageCompletedEvent()]);

        await using var session = new ChatSession(transport, null, Immediate);
        await session.SubmitAsync("go");
        await session.RetryAsync();

        Assert.Equal(2, session.Conversation.Messages.Count);
        Assert.Equal("go", session.Conversation.Messages[0].GetText());
        Assert.Equal("second time lucky", session.Conversation.Messages[1].GetText());
    }

    [Fact]
    public async Task RegenerateDiscardsTheAnswerAndEverythingAfterIt()
    {
        var replies = 0;
        var transport = new FakeTransport(_ =>
            [new TextDeltaEvent($"reply {++replies}"), new MessageCompletedEvent()]);

        await using var session = new ChatSession(transport, null, Immediate);
        await session.SubmitAsync("one");
        await session.SubmitAsync("two");

        var first = session.Conversation.Messages[1];
        await session.RegenerateAsync(first.MessageId);

        Assert.Equal(2, session.Conversation.Messages.Count);
        Assert.Equal("one", session.Conversation.Messages[0].GetText());
        Assert.Equal("reply 3", session.Conversation.Messages[1].GetText());
    }

    [Fact]
    public async Task EditRewritesTheQuestionAndRegeneratesFromThere()
    {
        var transport = new FakeTransport(request =>
            [new TextDeltaEvent($"answering: {request.Messages[^1].GetText()}"), new MessageCompletedEvent()]);

        await using var session = new ChatSession(transport, null, Immediate);
        await session.SubmitAsync("original");

        var question = session.Conversation.Messages[0];
        await session.EditAsync(question.MessageId, "corrected");

        Assert.Equal(2, session.Conversation.Messages.Count);
        Assert.Equal("corrected", session.Conversation.Messages[0].GetText());
        Assert.Equal("answering: corrected", session.Conversation.Messages[1].GetText());
    }

    [Fact]
    public async Task SubmittingWhileGeneratingIsIgnored()
    {
        await using var session = new ChatSession(new HangingTransport(), null, Immediate);

        var turn = session.SubmitAsync("first");
        await WaitFor(() => session.IsGenerating);

        var second = await session.SubmitAsync("second");
        Assert.Null(second);

        session.Stop();
        await turn;
        Assert.Equal(2, session.Conversation.Messages.Count);
    }

    [Fact]
    public async Task TheToolLoopIsBoundedByTheConfiguredLimit()
    {
        var registry = new ToolRegistry();
        registry.Register(AiTool.Create(
            new AiToolDefinition("loop"),
            (_, _) => ValueTask.FromResult(ToolOutcome.FromText("again"))));

        var calls = 0;
        var transport = new FakeTransport(_ =>
        [
            new ToolCallStartedEvent($"c{++calls}", "loop")
            {
                Arguments = System.Text.Json.JsonDocument.Parse("{}").RootElement,
            },
            new MessageCompletedEvent(FinishReason.ToolCalls),
        ]);

        await using var session = new ChatSession(
            transport, null, new ChatSessionOptions { Pacing = null, MaxToolRoundtrips = 3 }, registry);

        await session.SubmitAsync("go");

        Assert.Equal(4, transport.Requests.Count);
        Assert.Contains(session.Conversation.Messages[1].Parts,
            p => p is ErrorPart { Error.Code: "tool_loop_limit" });
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(5);
        Assert.True(condition(), "The expected condition was not reached in time.");
    }
}
