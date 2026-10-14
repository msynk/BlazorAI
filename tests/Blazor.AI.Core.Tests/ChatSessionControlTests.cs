namespace Blazor.AI.Tests;

public sealed class ChatSessionControlTests
{
    private static ChatSessionOptions Immediate => new() { Pacing = null };

    [Fact]
    public async Task StopCancelsTheTurnAndKeepsThePartialAnswer()
    {
        await using var session = new ChatSession(new HangingTransport(), null, Immediate);

        var turn = session.SubmitAsync("go");
        // Polls the length rather than the text: materialising the text from this thread races the
        // turn appending to the same builder.
        await WaitFor(() => session.Conversation.Messages.Count == 2
            && session.Conversation.Messages[1].Parts is [TextPart { Length: > 0 }, ..]);

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

    [Fact]
    public async Task ATimeoutIsReportedAsATimeoutRatherThanAsTheUserStopping()
    {
        await using var session = new ChatSession(new HangingTransport(), null,
            new ChatSessionOptions { Pacing = null, Timeout = TimeSpan.FromMilliseconds(50) });

        await session.SubmitAsync("go");

        Assert.Equal(AiErrorKind.Timeout, session.LastError!.Kind);
        Assert.True(session.LastError.IsRetryable);
        Assert.Equal(MessageStatus.Failed, session.Conversation.Messages[1].Status);
    }

    [Fact]
    public async Task ATurnSubmittedAfterDisposalDoesNothing()
    {
        var transport = new FakeTransport(new MessageCompletedEvent());
        var session = new ChatSession(transport, null, Immediate);
        await session.DisposeAsync();

        Assert.Null(await session.SubmitAsync("go"));
        Assert.Empty(transport.Requests);
        Assert.Empty(session.Conversation.Messages);
    }

    [Fact]
    public async Task ConcurrentSubmissionsStartExactlyOneTurn()
    {
        await using var session = new ChatSession(new HangingTransport(), null, Immediate);
        using var start = new ManualResetEventSlim();

        var submissions = Enumerable.Range(0, 8)
            .Select(i => Task.Run(async () =>
            {
                start.Wait();
                return await session.SubmitAsync($"message {i}");
            }))
            .ToArray();

        start.Set();
        await WaitFor(() => session.Conversation.Messages.Count >= 2);
        await Task.Delay(50);
        session.Stop();

        var results = await Task.WhenAll(submissions);
        Assert.Single(results, r => r is not null);
        Assert.Single(session.Conversation.Messages, m => m.Role.IsUser);
    }

    [Fact]
    public async Task RegeneratingFromAUserMessageDoesNotDeleteTheQuestion()
    {
        var transport = new FakeTransport(_ => [new TextDeltaEvent("reply"), new MessageCompletedEvent()]);
        await using var session = new ChatSession(transport, null, Immediate);
        await session.SubmitAsync("question");

        var result = await session.RegenerateAsync(session.Conversation.Messages[0].MessageId);

        Assert.Null(result);
        Assert.Equal(2, session.Conversation.Messages.Count);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task AnsweringAnAgentWhileBusyLeavesTheRunWaiting()
    {
        await using var session = new ChatSession(new HangingTransport(), null, Immediate);
        var run = session.Conversation.GetOrAddRun("run1");
        run.RequestInput("When should I take the downtime?");

        var turn = session.SubmitAsync("first");
        await WaitFor(() => session.Conversation.Messages.Count == 2);

        Assert.Null(await session.RespondToAgentAsync("run1", "Tonight"));
        Assert.Equal(AgentRunStatus.WaitingForInput, run.Status);

        session.Stop();
        await turn;
    }

    [Fact]
    public async Task ALateApprovalDoesNotReviveACancelledCall()
    {
        var registry = new ToolRegistry();
        registry.Register(AiTool.Create(
            new AiToolDefinition("delete_all") { Risk = ToolRisk.Destructive },
            (_, _) => ValueTask.FromResult(ToolOutcome.FromText("deleted"))));

        var transport = new FakeTransport(_ =>
        [
            new ToolCallStartedEvent("c1", "delete_all") { Arguments = System.Text.Json.JsonDocument.Parse("{}").RootElement },
            new MessageCompletedEvent(),
        ]);

        await using var session = new ChatSession(transport, null, Immediate, registry);
        ToolCallPart? awaiting = null;
        session.ApprovalRequested += call => awaiting = call;

        var turn = session.SubmitAsync("clean up");
        await WaitFor(() => awaiting is not null);
        session.Stop();
        await turn;

        session.Approve("c1", ApprovalDecision.Approved);

        Assert.Equal(ToolCallStatus.Cancelled, awaiting!.Status);
        Assert.Equal(ApprovalDecision.Undecided, awaiting.Approval);
    }

    [Fact]
    public async Task AToolThatCancelsItselfFailsTheCallNotTheTurn()
    {
        var registry = new ToolRegistry();
        registry.Register(AiTool.Create(
            new AiToolDefinition("fetch"),
            (_, _) => throw new TaskCanceledException("The HTTP request timed out.")));

        var round = 0;
        var transport = new FakeTransport(_ => round++ == 0
            ?
            [
                new ToolCallStartedEvent("c1", "fetch") { Arguments = System.Text.Json.JsonDocument.Parse("{}").RootElement },
                new MessageCompletedEvent(FinishReason.ToolCalls),
            ]
            : [new TextDeltaEvent("The fetch failed."), new MessageCompletedEvent()]);

        await using var session = new ChatSession(transport, null, Immediate, registry);
        var assistant = await session.SubmitAsync("go");

        var call = Assert.IsType<ToolCallPart>(assistant!.Parts[0]);
        Assert.Equal(ToolCallStatus.Failed, call.Status);
        Assert.Equal(MessageStatus.Complete, assistant.Status);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task AToolIsExecutedOnceEvenWhenItsCompletionIsRepeated()
    {
        var executions = 0;
        var registry = new ToolRegistry();
        registry.Register(AiTool.Create(
            new AiToolDefinition("send_email"),
            (_, _) =>
            {
                Interlocked.Increment(ref executions);
                return ValueTask.FromResult(ToolOutcome.FromText("sent"));
            }));

        var arguments = System.Text.Json.JsonDocument.Parse("{}").RootElement;
        var round = 0;
        var transport = new FakeTransport(_ => round++ == 0
            ?
            [
                new ToolCallStartedEvent("c1", "send_email") { Arguments = arguments },
                new ToolCallCompletedEvent("c1", arguments),
                new MessageCompletedEvent(FinishReason.ToolCalls),
            ]
            : [new MessageCompletedEvent()]);

        await using var session = new ChatSession(transport, null, Immediate, registry);
        await session.SubmitAsync("go");

        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task AToolTheServerAlreadyAnsweredIsNotExecutedAgain()
    {
        var executions = 0;
        var registry = new ToolRegistry();
        registry.Register(AiTool.Create(
            new AiToolDefinition("search"),
            (_, _) =>
            {
                executions++;
                return ValueTask.FromResult(ToolOutcome.FromText("client"));
            }));

        var transport = new FakeTransport(_ =>
        [
            new ToolCallStartedEvent("c1", "search") { Arguments = System.Text.Json.JsonDocument.Parse("{}").RootElement },
            new ToolResultEvent("c1", ToolOutcome.FromText("server")),
            new TextDeltaEvent("Found it."),
            new MessageCompletedEvent(),
        ]);

        await using var session = new ChatSession(transport, null, Immediate, registry);
        var assistant = await session.SubmitAsync("go");

        Assert.Equal(0, executions);
        Assert.Single(transport.Requests);
        Assert.Equal("server", assistant!.FindToolCall("c1")!.Result!.Text);
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(5);
        Assert.True(condition(), "The expected condition was not reached in time.");
    }
}
