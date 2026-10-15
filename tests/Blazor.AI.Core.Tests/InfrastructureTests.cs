using System.Text.Json;
using Blazor.AI.GenerativeUI;

namespace Blazor.AI.Tests;

public sealed class RenderSchedulerTests
{
    [Fact]
    public async Task ManyRequestsInsideOneIntervalProduceOneRender()
    {
        var renders = 0;
        await using var scheduler = new RenderScheduler(
            () => { Interlocked.Increment(ref renders); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(40));

        for (var i = 0; i < 200; i++) scheduler.Request();
        await Task.Delay(120);

        Assert.InRange(renders, 1, 3);
        Assert.True(scheduler.CoalescedCount > 100);
    }

    [Fact]
    public async Task FlushRendersImmediatelyWithoutWaitingForTheInterval()
    {
        var renders = 0;
        await using var scheduler = new RenderScheduler(
            () => { Interlocked.Increment(ref renders); return Task.CompletedTask; },
            TimeSpan.FromSeconds(10));

        scheduler.Request();
        await scheduler.FlushAsync();

        Assert.Equal(1, renders);
    }

    [Fact]
    public async Task RequestsAfterDisposalAreIgnored()
    {
        var renders = 0;
        var scheduler = new RenderScheduler(
            () => { Interlocked.Increment(ref renders); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(10));

        await scheduler.DisposeAsync();
        scheduler.Request();
        await Task.Delay(40);

        Assert.Equal(0, renders);
    }

    [Fact]
    public async Task AFlushRequestedWhileAnotherIsRunningIsNotDropped()
    {
        var renders = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new RenderScheduler(
            async () =>
            {
                if (Interlocked.Increment(ref renders) == 1) await gate.Task;
            },
            TimeSpan.FromMilliseconds(10));

        var first = scheduler.FlushAsync();
        await scheduler.FlushAsync();
        gate.SetResult();
        await first;

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (Volatile.Read(ref renders) < 2 && DateTime.UtcNow < deadline) await Task.Delay(5);

        Assert.Equal(2, Volatile.Read(ref renders));
    }

    [Fact]
    public async Task RequestsRacingDisposalNeverThrow()
    {
        for (var i = 0; i < 200; i++)
        {
            var scheduler = new RenderScheduler(() => Task.CompletedTask, TimeSpan.FromMilliseconds(1));
            var requests = Task.Run(() =>
            {
                for (var j = 0; j < 50; j++) scheduler.Request();
            });

            await scheduler.DisposeAsync();
            await requests;
        }
    }
}

public sealed class DependencyInjectionTests
{
    [Fact]
    public void TheApprovalPolicyIsScopedSoOneUsersAlwaysAllowDoesNotApplyToAnother()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddBlazorAI();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IToolApprovalPolicy));
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped,
            Assert.Single(services, d => d.ServiceType == typeof(ChatSessionFactory)).Lifetime);
    }

    [Fact]
    public void AReplacementApprovalPolicyIsScopedSoItCanDependOnTheCurrentUser()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddBlazorAI().UseApprovalPolicy<DefaultToolApprovalPolicy>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IToolApprovalPolicy));
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task AlwaysApprovalsCanBeRecordedWhileCallsAreBeingChecked()
    {
        var policy = new DefaultToolApprovalPolicy();
        var invocation = new ToolInvocation("c", "t", default);

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 5000; i++) policy.RememberAlwaysApproved($"tool_{i}");
        });

        while (!writer.IsCompleted)
        {
            await policy.RequiresApprovalAsync(new AiToolDefinition("tool_1") { Risk = ToolRisk.Destructive }, invocation);
        }

        await writer;
        Assert.False(await policy.RequiresApprovalAsync(
            new AiToolDefinition("tool_4999") { Risk = ToolRisk.Destructive }, invocation));
    }
}

public sealed class TextPacingTests
{
    [Fact]
    public async Task PacingPreservesTheCompleteTextAndTheOrderOfOtherEvents()
    {
        var source = new AiEvent[]
        {
            new MessageStartedEvent(),
            new TextDeltaEvent("Hello there, "),
            new TextDeltaEvent("this is a longer burst of output."),
            new ToolCallStartedEvent("c1", "t"),
            new TextDeltaEvent("And more after the tool."),
            new MessageCompletedEvent(),
        };

        var options = new TextPacingOptions
        {
            Interval = TimeSpan.FromMilliseconds(1),
            TargetDrain = TimeSpan.FromMilliseconds(4),
            MaxCharsPerTick = 8,
        };

        var received = new List<AiEvent>();
        await foreach (var evt in Replay(source).Paced(options)) received.Add(evt);

        var text = string.Concat(received.OfType<TextDeltaEvent>().Select(e => e.Delta));
        Assert.Equal("Hello there, this is a longer burst of output.And more after the tool.", text);

        Assert.IsType<MessageStartedEvent>(received[0]);
        Assert.IsType<MessageCompletedEvent>(received[^1]);

        var toolIndex = received.FindIndex(e => e is ToolCallStartedEvent);
        var afterTool = received.Skip(toolIndex).OfType<TextDeltaEvent>().Select(e => e.Delta);
        Assert.Equal("And more after the tool.", string.Concat(afterTool));
    }

    [Fact]
    public async Task PacingSplitsABurstIntoSeveralDeltas()
    {
        var source = new AiEvent[] { new TextDeltaEvent(new string('x', 200)) };
        var options = new TextPacingOptions
        {
            Interval = TimeSpan.FromMilliseconds(1),
            TargetDrain = TimeSpan.FromMilliseconds(20),
            MaxCharsPerTick = 16,
        };

        var count = 0;
        await foreach (var _ in Replay(source).Paced(options)) count++;

        Assert.True(count > 1, "A 200 character burst should be released over several ticks.");
    }

    [Fact]
    public async Task PacingNeverSplitsASurrogatePair()
    {
        var source = new AiEvent[] { new TextDeltaEvent(string.Concat(Enumerable.Repeat("a\U0001F600", 20))) };
        var options = new TextPacingOptions
        {
            Interval = TimeSpan.FromMilliseconds(1),
            TargetDrain = TimeSpan.FromMilliseconds(100),
            MaxCharsPerTick = 2,
        };

        var deltas = new List<string>();
        await foreach (var evt in Replay(source).Paced(options))
        {
            if (evt is TextDeltaEvent text) deltas.Add(text.Delta);
        }

        Assert.True(deltas.Count > 1);
        Assert.All(deltas, delta =>
        {
            Assert.False(char.IsHighSurrogate(delta[^1]), "A delta ended with half a surrogate pair.");
            Assert.False(char.IsLowSurrogate(delta[0]), "A delta started with half a surrogate pair.");
        });
    }

    [Fact]
    public void PacingSettingsThatWouldStallTheStreamAreRejected()
    {
        // Zero characters per tick never drains the buffer; minus one millisecond is Task.Delay's "forever".
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPacingOptions { MaxCharsPerTick = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPacingOptions { MinCharsPerTick = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPacingOptions { Interval = TimeSpan.FromMilliseconds(-1) });
    }

    [Fact]
    public void AMinimumAboveTheMaximumIsRejectedWhereTheSettingsAreUsed()
    {
        var inverted = new TextPacingOptions { MinCharsPerTick = 10, MaxCharsPerTick = 5 };

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Replay([]).Paced(inverted));
        Assert.Contains(nameof(TextPacingOptions.MaxCharsPerTick), error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChatSessionOptions { Pacing = inverted });
    }

    [Fact]
    public async Task AMinimumAboveTheDefaultMaximumIsFineOnceTheMaximumIsRaised()
    {
        // Set in this order, the minimum briefly exceeds the default maximum, which must not throw.
        var options = new TextPacingOptions
        {
            MinCharsPerTick = 60,
            MaxCharsPerTick = 100,
            Interval = TimeSpan.FromMilliseconds(1),
        };

        var text = new System.Text.StringBuilder();
        await foreach (var evt in Replay([new TextDeltaEvent(new string('x', 250))]).Paced(options))
        {
            if (evt is TextDeltaEvent delta) text.Append(delta.Delta);
        }

        Assert.Equal(250, text.Length);
        Assert.Equal(options, new ChatSessionOptions { Pacing = options }.Pacing);
    }

    [Fact]
    public async Task AConsumerThatStopsEarlyAlsoStopsTheSource()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await foreach (var _ in Endless(stopped).Paced(TextPacingOptions.Default))
        {
            break;
        }

        var finished = await Task.WhenAny(stopped.Task, Task.Delay(2000));
        Assert.Same(stopped.Task, finished);
    }

    private static async IAsyncEnumerable<AiEvent> Endless(
        TaskCompletionSource stopped,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        try
        {
            while (true)
            {
                await Task.Delay(5, cancellationToken);
                yield return new StatusEvent("working");
            }
        }
        finally
        {
            stopped.TrySetResult();
        }
    }

    private static async IAsyncEnumerable<AiEvent> Replay(IEnumerable<AiEvent> events)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }
}
