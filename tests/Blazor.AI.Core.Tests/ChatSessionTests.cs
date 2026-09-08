using System.Text.Json;

namespace Blazor.AI.Tests;

public sealed class ChatSessionTests
{
    private static ChatSession Create(IAiTransport transport, IToolRegistry? tools = null,
        ChatSessionOptions? options = null, IToolApprovalPolicy? policy = null) =>
        new(transport, null, options ?? new ChatSessionOptions { Pacing = null }, tools, policy);

    [Fact]
    public async Task SubmitAppendsAUserMessageAndStreamsAReply()
    {
        var transport = new FakeTransport(
            new MessageStartedEvent(),
            new TextDeltaEvent("Hi "),
            new TextDeltaEvent("there"),
            new MessageCompletedEvent());

        await using var session = Create(transport);
        await session.SubmitAsync("hello");

        Assert.Equal(2, session.Conversation.Messages.Count);
        Assert.Equal("hello", session.Conversation.Messages[0].GetText());
        Assert.Equal("Hi there", session.Conversation.Messages[1].GetText());
        Assert.False(session.IsGenerating);
    }

    [Fact]
    public async Task TheFirstUserMessageBecomesTheConversationTitle()
    {
        await using var session = Create(new FakeTransport(new MessageCompletedEvent()));
        await session.SubmitAsync("Explain quantum tunnelling in plain English");

        Assert.Equal("Explain quantum tunnelling in plain English", session.Conversation.Title);
    }

    [Fact]
    public async Task TheRequestCarriesTheConversationButNotTheEmptyAssistantMessage()
    {
        var transport = new FakeTransport(new MessageCompletedEvent());
        await using var session = Create(transport);

        await session.SubmitAsync("first");
        await session.SubmitAsync("second");

        Assert.Equal(2, transport.Requests.Count);
        Assert.Single(transport.Requests[0].Messages);
        Assert.Equal(3, transport.Requests[1].Messages.Count);
    }

    [Fact]
    public async Task ToolsAreExecutedAndTheirResultsFeedTheNextRound()
    {
        var registry = new ToolRegistry();
        registry.Register(AiTool.Create(
            new AiToolDefinition("add") { Description = "Adds two numbers." },
            (invocation, _) =>
            {
                var args = invocation.Arguments;
                var sum = args.GetProperty("a").GetInt32() + args.GetProperty("b").GetInt32();
                return ValueTask.FromResult(ToolOutcome.Success(new { sum }));
            }));

        var round = 0;
        var transport = new FakeTransport(_ => round++ == 0
            ?
            [
                new ToolCallStartedEvent("c1", "add")
                {
                    Arguments = JsonDocument.Parse("""{"a":2,"b":3}""").RootElement,
                },
                new MessageCompletedEvent(FinishReason.ToolCalls),
            ]
            :
            [
                new TextDeltaEvent("The sum is 5."),
                new MessageCompletedEvent(),
            ]);

        await using var session = Create(transport, registry);
        await session.SubmitAsync("what is 2 plus 3");

        var assistant = session.Conversation.Messages[^1];
        var call = Assert.IsType<ToolCallPart>(assistant.Parts[0]);
        Assert.Equal(ToolCallStatus.Completed, call.Status);
        Assert.Equal(5, call.Result!.Value!.Value.GetProperty("sum").GetInt32());
        Assert.Equal("The sum is 5.", assistant.GetText());
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task AnUnknownToolFailsTheCallWithoutFailingTheTurn()
    {
        var transport = new FakeTransport(_ =>
        [
            new ToolCallStartedEvent("c1", "does_not_exist") { Arguments = JsonDocument.Parse("{}").RootElement },
            new MessageCompletedEvent(),
        ]);

        await using var session = Create(transport, new ToolRegistry(),
            new ChatSessionOptions { Pacing = null, MaxToolRoundtrips = 1 });

        await session.SubmitAsync("go");

        var call = Assert.IsType<ToolCallPart>(session.Conversation.Messages[^1].Parts[0]);
        Assert.Equal(ToolCallStatus.Failed, call.Status);
        Assert.Equal("unknown_tool", call.Result!.Error!.Code);
    }

    [Fact]
    public async Task ADestructiveToolWaitsForApprovalAndRunsOnceApproved()
    {
        var executed = false;
        var registry = new ToolRegistry();
        registry.Register(AiTool.Create(
            new AiToolDefinition("delete_all") { Risk = ToolRisk.Destructive },
            (_, _) =>
            {
                executed = true;
                return ValueTask.FromResult(ToolOutcome.FromText("deleted"));
            }));

        var transport = new FakeTransport(_ =>
        [
            new ToolCallStartedEvent("c1", "delete_all") { Arguments = JsonDocument.Parse("{}").RootElement },
            new MessageCompletedEvent(),
        ]);

        await using var session = Create(transport, registry,
            new ChatSessionOptions { Pacing = null, MaxToolRoundtrips = 1 });

        ToolCallPart? awaiting = null;
        session.ApprovalRequested += call => awaiting = call;

        var turn = session.SubmitAsync("clean up");

        await WaitFor(() => awaiting is not null);
        Assert.False(executed);
        Assert.Equal(ToolCallStatus.AwaitingApproval, awaiting!.Status);

        session.Approve("c1", ApprovalDecision.Approved);
        await turn;

        Assert.True(executed);
        Assert.Equal(ToolCallStatus.Completed, awaiting.Status);
    }

    [Fact]
    public async Task ARejectedToolDoesNotRun()
    {
        var executed = false;
        var registry = new ToolRegistry();
        registry.Register(AiTool.Create(
            new AiToolDefinition("delete_all") { Risk = ToolRisk.Destructive },
            (_, _) =>
            {
                executed = true;
                return ValueTask.FromResult(ToolOutcome.FromText("deleted"));
            }));

        var transport = new FakeTransport(_ =>
        [
            new ToolCallStartedEvent("c1", "delete_all") { Arguments = JsonDocument.Parse("{}").RootElement },
            new MessageCompletedEvent(),
        ]);

        await using var session = Create(transport, registry,
            new ChatSessionOptions { Pacing = null, MaxToolRoundtrips = 1 });

        ToolCallPart? awaiting = null;
        session.ApprovalRequested += call => awaiting = call;

        var turn = session.SubmitAsync("clean up");
        await WaitFor(() => awaiting is not null);
        session.Approve("c1", ApprovalDecision.Rejected);
        await turn;

        Assert.False(executed);
        Assert.Equal(ToolCallStatus.Rejected, awaiting!.Status);
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(5);
        Assert.True(condition(), "The expected condition was not reached in time.");
    }
}
