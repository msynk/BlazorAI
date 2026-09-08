# Testing

The framework is designed so that AI UI can be tested without a model, a key, or a network. Two
properties make that possible: the transport boundary is an interface, and the reducer is pure.

## A transport is a test double

```csharp
internal sealed class FakeTransport(params AiEvent[] events) : IAiTransport
{
    public async IAsyncEnumerable<AiEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var evt in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return evt;
        }
    }
}
```

That is enough to test submission, streaming, tool execution, approval, cancellation, retry,
regeneration, and editing:

```csharp
[Fact]
public async Task SubmitAppendsAUserMessageAndStreamsAReply()
{
    var transport = new FakeTransport(
        new MessageStartedEvent(),
        new TextDeltaEvent("Hi "),
        new TextDeltaEvent("there"),
        new MessageCompletedEvent());

    await using var session = new ChatSession(transport, options: new ChatSessionOptions { Pacing = null });
    await session.SubmitAsync("hello");

    Assert.Equal("Hi there", session.Conversation.Messages[1].GetText());
}
```

Turn pacing off in tests. It is a presentation feature, and leaving it on only makes tests slower and
less deterministic.

## Testing the reducer directly

`ConversationStreamProcessor` has no side effects, so recorded events can be replayed into it:

```csharp
var conversation = new Conversation();
var processor = new ConversationStreamProcessor(conversation);

processor.BeginGeneration();
processor.Apply(new ToolCallStartedEvent("c1", "search"));
processor.Apply(new ToolCallDeltaEvent("c1", """{"query":"bla"""));

var call = conversation.Messages[0].FindToolCall("c1")!;
Assert.Equal("bla", call.DisplayArguments!.Value.GetProperty("query").GetString());
```

This is the cheapest way to pin down behaviour that would otherwise need a rendered component.

## Testing components

The component tests use bUnit. Registering the framework services is the only setup:

```csharp
public abstract class AiTestBase : BunitContext
{
    protected AiTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddBlazorAIComponents();
    }
}
```

Streaming is testable because the state is observable. Mutate a part and wait for the assertion,
rather than sleeping:

```csharp
[Fact]
public void StreamingATokenUpdatesTheRenderedText()
{
    var part = new TextPart();
    var message = Assistant(part);
    var component = Render<ChatMessage>(p => p.Add(c => c.Message, message));

    part.Append("streamed in");

    component.WaitForAssertion(
        () => Assert.Contains("streamed in", component.Markup, StringComparison.Ordinal),
        TimeSpan.FromSeconds(2));
}
```

`WaitForAssertion` is necessary because renders are coalesced. A test that asserts immediately after
a mutation is asserting against a render that has deliberately not happened yet.

## What is worth testing

The library's own suite is a reasonable template:

| Area | Example |
| --- | --- |
| Partial JSON | every prefix of a payload parses or yields nothing, and the final one is correct |
| Incremental Markdown | the incremental result matches a one-shot parse at every prefix |
| Reducer | text after a tool call starts a new part; citations deduplicate and number |
| Session | approval blocks execution; rejection prevents it; the tool loop is bounded |
| Cancellation | stopping keeps the partial answer and marks it cancelled |
| Rendering | script URLs are neutralised; raw HTML is escaped; open code blocks are not highlighted |
| Generative UI | unregistered names render nothing; undeclared properties never bind |
| Accessibility | alerts, busy state, progress values, labels bound to controls |

## Running

```bash
dotnet test BlazorAI.slnx
```
