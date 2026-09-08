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

    private static async IAsyncEnumerable<AiEvent> Replay(IEnumerable<AiEvent> events)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }
}
