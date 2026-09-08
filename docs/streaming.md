# Streaming

## The vocabulary

Every event derives from `AiEvent` and carries an optional `MessageId`. Part-scoped events also
carry an optional `PartId`. Omitting both is the common case and means "the message being generated"
and "the open part of a compatible kind".

### Lifecycle

| Event | Effect |
| --- | --- |
| `MessageStartedEvent` | Marks the message streaming. Optional. |
| `MessageCompletedEvent` | Closes open parts, records the finish reason. |
| `PartStartedEvent` | Opens a part explicitly, with attributes such as a code language. |
| `PartCompletedEvent` | Closes one part, which is what lets a code block highlight the instant it ends. |
| `StatusEvent` | A transient status line. Replaces the previous one; cleared on completion. |
| `ErrorEvent` | Terminal by default; a non-terminal error becomes an `ErrorPart`. |

### Content

| Event | Effect |
| --- | --- |
| `TextDeltaEvent` | Appends visible text. |
| `ReasoningDeltaEvent` | Appends reasoning, kept out of the answer. |
| `ImageEvent`, `FileEvent` | Adds media. |
| `CitationEvent` | Adds a citation, deduplicated by source and numbered. |
| `StructuredDataDeltaEvent`, `StructuredDataEvent` | Structured output, readable while partial. |
| `GenerativeUiEvent`, `GenerativeUiDeltaEvent` | A component the model chose. |

### Tools

| Event | Effect |
| --- | --- |
| `ToolCallStartedEvent` | Opens a call. Supply `Arguments` to skip the delta phase. |
| `ToolCallDeltaEvent` | Appends raw JSON argument text. |
| `ToolCallCompletedEvent` | Parses the arguments and makes the call ready to run. |
| `ToolProgressEvent` | Progress from a running tool. |
| `ToolResultEvent` | A result, reconciled onto the originating call. |
| `ToolApprovalRequestedEvent` | For a server that enforces its own policy. |

### Agents

| Event | Effect |
| --- | --- |
| `AgentRunStartedEvent` | Opens a run and attaches it to the message. |
| `AgentPlanEvent` | Replaces the plan, preserving surviving steps. |
| `AgentStepStartedEvent`, `AgentStepCompletedEvent` | Step lifecycle and timing. |
| `AgentInputRequestedEvent` | Blocks the run on the user. |
| `AgentRunCompletedEvent` | Ends the run. |

### Accounting

`UsageUpdatedEvent` and `MetadataEvent` are merged into the message rather than replacing what is
already there, because providers report these piecemeal.

`CustomEvent` carries anything the framework has no concept for.

## Partial JSON

For most of a generation, structured output is not valid JSON. Waiting for the closing brace means a
table appears all at once at the end. `PartialJson` repairs the truncated document instead:

| Input | Repaired |
| --- | --- |
| `{"city":"Seattle","days":3` | `{"city":"Seattle","days":3}` |
| `{"city":"Sea` | `{"city":"Sea"}` |
| `{"city":"Seattle","da` | `{"city":"Seattle"}` |
| `{"a":1,` | `{"a":1}` |
| `{"ok":tru` | `{}` |
| `{"text":"he said \"hi` | `{"text":"he said \"hi"}` |

Open containers are closed, an incomplete trailing key or literal is dropped, and a partially written
string value is kept so text fields grow character by character. Nothing is invented: every value in
the result was present in the input.

```csharp
var value = PartialJson.TryParse(part.RawJson);
var typed = part.As<Options>();   // use nullable members; fields not yet written are absent
```

## Incremental Markdown

Re-parsing the whole response on every delta is quadratic. `IncrementalMarkdownParser` tracks the
point beyond which no future text can change the result, keeps the blocks before it, and re-parses
only the tail.

The stable point is a blank line at fence depth zero, followed by a line at column zero that cannot
continue the preceding block. That last condition matters: a blank line inside a loose list looks
like a boundary and is not one.

Two behaviours make streamed Markdown look settled rather than jittery:

- An **unclosed fence** is marked open, and open code blocks are not highlighted. Colours that shift
  as the lexer changes its mind about an unterminated string are worse than no colours.
- An **unmatched trailing delimiter** is hidden while streaming. Without that, every bold phrase
  flashes a stray asterisk on its way in.

## Pacing

Providers deliver text in uneven bursts: nothing for two hundred milliseconds, then a paragraph.
Rendering that verbatim reads as stuttering, which users perceive as slowness even when the total
time is identical.

```csharp
var options = new ChatSessionOptions
{
    Pacing = new TextPacingOptions
    {
        Interval = TimeSpan.FromMilliseconds(16),
        TargetDrain = TimeSpan.FromMilliseconds(150),
        MaxCharsPerTick = 48,
    },
};
```

The operator buffers text and releases it at a steady rate while passing every other event through
immediately and in order. It adds no latency when the model is slower than the pacer, which is the
common case. Set `Pacing` to `null` to render deltas exactly as the transport produces them.

## Rendering cadence

Pacing evens out *arrival*. `RenderScheduler` bounds *drawing*:

```csharp
builder.Services.AddBlazorAIComponents(options => options with
{
    RenderInterval = TimeSpan.FromMilliseconds(50),
});
```

Any number of change notifications inside one interval collapse into a single render, with
backpressure: while a flush is in flight, further requests set a flag rather than queueing.

## Applying events yourself

`ConversationStreamProcessor` is a pure reducer with no side effects, usable without any UI:

```csharp
var conversation = new Conversation();
var processor = new ConversationStreamProcessor(conversation);

processor.BeginGeneration();
foreach (var evt in recordedEvents) processor.Apply(evt);
processor.EndGeneration();
```

Feeding recorded events through a processor is the simplest way to test rendering, and the demo
replays fixtures exactly this way.
