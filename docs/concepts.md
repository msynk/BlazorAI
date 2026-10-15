# Concepts

## Conversation

An ordered list of messages plus the artifacts and agent runs that belong to the whole thread.
Observable at three grains, and the unit a chat UI binds to.

```csharp
var conversation = new Conversation();
conversation.AddMessage(AiRole.User, "Explain render modes");

conversation.MessageAdded   += (c, m) => { };
conversation.MessageRemoved += (c, m) => { };
conversation.MessageUpdated += (c, m) => { };  // coarse; for auto-save, not for rendering
conversation.Changed        += c => { };       // title, artifacts, runs
```

`TruncateAfter` is how retry, regeneration, and editing rewind the thread. It detaches the removed
messages so they stop notifying.

## Message

A role, an ordered list of heterogeneous parts, attachments, citations, a status, and generation
metadata. Mutable, because streaming updates it hundreds of times a second.

```csharp
var message = new AiMessage(AiRole.Assistant);
message.AddPart(new ReasoningPart("weighing options"));
message.AddPart(new TextPart("The answer is 4."));

message.GetText();   // "The answer is 4." - reasoning is excluded by design
```

`GetText` is the plain-text projection used by copy, export, and the text sent back to a provider on
the next turn. Reasoning is left out of all three.

## Parts

The reason a message is not a string.

| Part | Purpose |
| --- | --- |
| `TextPart` | The body. Rendered as Markdown by default. |
| `MarkdownPart` | Text explicitly marked as Markdown. |
| `ReasoningPart` | Thinking, collapsed and timed. |
| `CodePart` | A standalone code block with a language. |
| `ImagePart`, `FilePart` | Media, with intrinsic size where known. |
| `CitationPart`, `SourcesPart` | Attribution placed where you want it. |
| `ToolCallPart` | A call, its arguments, status, progress, and result. |
| `ToolResultPart` | A result that arrived on its own. |
| `ArtifactPart` | A reference to an artifact the conversation owns. |
| `StructuredDataPart` | JSON, readable while partial. |
| `GenerativeUiPart` | A component the model chose. |
| `ErrorPart` | A failure inside an otherwise useful message. |
| `CustomPart<T>` | Anything else. |

Text-like parts accumulate in a `StringBuilder` and materialise lazily, so appending a token is
amortised constant time rather than the quadratic concatenation naive implementations reach for.

### Custom parts

```csharp
message.AddPart(new CustomPart<Invoice>("acme:invoice", invoice));

renderers.RegisterPart("acme:invoice", context => builder =>
{
    builder.OpenComponent<InvoiceCard>(0);
    builder.AddComponentParameter(1, "Invoice", ((CustomPart<Invoice>)context.Part).Data);
    builder.CloseComponent();
});
```

A custom part participates in message rendering, copying, and virtualization exactly like a built-in
one. Renderers run inside an error boundary, so a bad payload cannot take the page down.

## Roles

`AiRole` wraps a string rather than being an enumeration, so an application can introduce a role the
framework has never heard of:

```csharp
AiRole.User; AiRole.Assistant; AiRole.System; AiRole.Tool; AiRole.Developer;
var critic = new AiRole("critic");
```

Comparison is ordinal and case-insensitive.

## Session

`ChatSession` drives one conversation: submission, streaming, the tool loop, approval, cancellation,
retry, regeneration, and editing.

```csharp
await session.SubmitAsync("What changed?", attachments);
session.Stop();
await session.RetryAsync();
await session.RegenerateAsync();
await session.EditAsync(messageId, "corrected question");
session.Approve(toolCallId, ApprovalDecision.ApprovedAlways);
```

Submitting while a turn is in flight returns `null` rather than throwing, because a double submit is
a UI race, not a programming error.

## Status

```text
Draft -> Pending -> Streaming -> Complete
                              -> Cancelled
                              -> Failed
```

A cancelled message keeps whatever had already streamed. Losing a half-written answer to a network
blip is worse than showing it with a retry button underneath.

## Metadata and usage

`GenerationMetadata` holds the model, provider, finish reason, timings, context window, and
`TokenUsage`. It is merged rather than replaced, because providers report it piecemeal, and it is
kept off the hot path so streaming never touches it.

Usage reports within one transport stream are cumulative, so the latest replaces the earlier ones;
separate streams add up. A message's usage therefore covers every request its tool loop made, and a
run's covers every request made within it. [Streaming](streaming.md#accounting) has the details.

`TokenUsage` is additive, field by field, which is how those sums are formed:

```csharp
var total = first + second;
var cost  = model.EstimateCost(total);
```
