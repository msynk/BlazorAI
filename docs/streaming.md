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
already there, because providers report these piecemeal. Within one stream each usage report is a
snapshot: fields it carries replace the earlier value, fields it omits keep theirs
(`TokenUsage.Merge`). Across the streams of one turn, such as the request after each tool round
trip, usage adds up (`TokenUsage` addition), and the same sum is kept on the agent run.

Usage follows one rule: it is cumulative within a stream and additive across streams. A stream is
one call to `IAiTransport.StreamAsync`.

- Within a stream, each usage report replaces the previous one, so a transport can forward a
  provider's running totals as they arrive without anything being counted twice. Usage carried on a
  `MetadataEvent` counts as a report too.
- A message's usage is the sum of the final report of every stream that contributed to it. A turn
  with tool calls makes one request per round trip, so its answer reports what all of them cost,
  not only the last.
- An agent run's usage is the sum of the final report of every stream that ran within it, including
  streams from later turns that continue the same run.

Every field adds separately, and a field no stream reported stays `null` rather than becoming zero.
`ChatSession` marks each stream itself. When driving a processor by hand, call `BeginStream()`
before applying each further stream's events; `BeginGeneration()` begins the first.

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

The stable point is a blank line followed by a line at column zero that cannot continue the
preceding block, and on which the parser, reading the tail, does start a top-level block. That
last condition leaves every question of context to the parser itself: whether a fence or a `$$`
block is still open, whether a blank line sits inside a loose list. The prefix grows as text
arrives a token at a time; a decision never changes once made, so the blocks before a stable point
are kept exactly as parsed. A document that contains a link reference or footnote definition is
parsed whole on every call, since text anywhere may then depend on text anywhere else.

Three behaviours make streamed Markdown look settled rather than jittery:

- An **unclosed fence** is marked open, and open code blocks are not highlighted. Colours that shift
  as the lexer changes its mind about an unterminated string are worse than no colours.
- An **unmatched delimiter** (`*`, `_`, `~~`, or a backtick) outside any closed span is hidden while
  the paragraph it sits in is still open. Without that, every bold phrase flashes a stray asterisk
  on its way in. Turn this off with `MarkdownOptions.HideDanglingDelimiters`.
- Only the **last list item** is open while a list streams, so a finished item never hides or
  re-flows its content as the next item arrives.

### What the parser accepts

A CommonMark subset with the GitHub extensions models emit: ATX and setext headings, fenced and
indented code, block quotes, bullet, ordered, and task lists, pipe tables, thematic breaks,
emphasis by the CommonMark delimiter algorithm, strikethrough, inline and angle autolinks, inline
and reference links (`[text][ref]` with `[ref]: url "title"` anywhere in the document), images,
footnotes (`[^1]` with `[^1]: note`, listed on `MarkdownDocument.Footnotes` in citation order),
HTML entities and numeric character references, `$...$` and `$$...$$` mathematics by pandoc's
rules (so `$5 and $10` is a price), and bracketed citation markers such as `[3]`. One deliberate
leniency: `**bold **text`, which the specification leaves as literal asterisks, renders bold.

`MarkdownOptions` controls the extensions and the limits:

| Option | Default | Effect |
| --- | --- | --- |
| `Tables`, `Strikethrough`, `TaskLists`, `AutoLinks`, `Math`, `Footnotes`, `CitationMarkers` | on | Enable each extension. |
| `AllowRawHtml` | off | Keep raw HTML as markup. Leave off for model output. |
| `HeadingIds` | on | Give headings ids of the form `bai-h-<slug>`, numbered `-2`, `-3` when the same text repeats, so they cannot clobber the host page's own anchors. |
| `HideDanglingDelimiters` | on | Hide unmatched delimiters while a paragraph streams. |
| `SoftBreaksAsHardBreaks` | off | Render a single newline inside a paragraph as a line break, as chat clients often do. |
| `MaxNestingDepth` | 32 | How deep quotes, lists, and inline spans may nest before further markers are read as text. Bounds both parse time and the depth of the tree a renderer walks. |

Parsing is linear in the length of the input, including on hostile input: tens of thousands of
unmatched `*`, nested `[`, or `>` characters parse in milliseconds, never recurse past
`MaxNestingDepth`, and never take the circuit down with a stack overflow.

### Rendering hooks

`StreamingMarkdown` builds its parser from `AiUiOptions.Markdown`, so the parser and the renderer
always agree on what is enabled. `MarkdownView` consults `AiRendererRegistry` for two kinds of
content before rendering them itself:

```csharp
services.ConfigureAiRenderers(renderers =>
{
    // Fenced code in a registered language, such as a diagram. Wait for IsStreaming to turn
    // false before drawing: a half-written diagram is a syntax error.
    renderers.RegisterCodeLanguage("mermaid", ctx => builder =>
    {
        builder.OpenComponent<MermaidDiagram>(0);
        builder.AddComponentParameter(1, "Source", ctx.Code);
        builder.AddComponentParameter(2, "Pending", ctx.IsStreaming);
        builder.CloseComponent();
    });

    // Mathematics. Without this, expressions render in monospace with a data-math attribute,
    // which a KaTeX or MathJax auto-render call in the browser can pick up instead.
    renderers.MathRenderer = ctx => builder => { /* ctx.Expression, ctx.IsBlock */ };
});
```

Images follow `AiUiOptions.Images`, an `AiImagePolicy`: `Allow` (the default), `Block`,
`ClickToLoad`, or `AllowListedHosts("cdn.example.com", "*.trusted.org")`. A model can be talked
into emitting an image URL, and a browser fetching it tells whoever controls the host that the
message was read; a host handling sensitive conversations should prefer one of the stricter
modes. Script-capable URL schemes are neutralised whatever the policy, and inline `data:` images,
which involve no request, are shown under every mode but `Block`.

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

Settings that would stall the stream are rejected with an `ArgumentOutOfRangeException`: fewer than
one character per tick or a negative interval as the value is set, and a `MinCharsPerTick` above
`MaxCharsPerTick` when the settings are assigned to `ChatSessionOptions.Pacing` or passed to
`Paced`.

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

When a recording spans several requests, as a tool loop does, call `processor.BeginStream()` where
each new request begins so usage is summed across them rather than replaced.

Feeding recorded events through a processor is the simplest way to test rendering, and the demo
replays fixtures exactly this way.
