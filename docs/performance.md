# Performance

A fast model is what makes an AI interface slow. Hundreds of updates a second, each one a change to a
growing document, is a workload that punishes every naive choice. Five decisions carry most of the
weight.

## 1. Render granularity

Change notification is **per part**, not per message.

```text
AiMessage.Changed      structural: a part added, the status moved
MessagePart.Changed    content: a token appended to this part
```

A message component subscribes to the first, a part component to the second. A token arriving in the
last paragraph of a long answer re-renders that paragraph. It does not touch the reasoning panel,
tool cards, code blocks, or citations above it.

Every observable type also exposes a monotonic `Version`, so a component can decide whether to
re-render by comparing two integers instead of comparing content.

## 2. Coalesced rendering

`RenderScheduler` collapses any number of requests inside an interval into one render:

```csharp
options with { RenderInterval = TimeSpan.FromMilliseconds(50) }
```

It applies backpressure rather than queueing: while a flush is in flight, further requests set a
dirty flag and one more flush runs when the first completes. A slow renderer falls behind in render
count, never in memory.

Fifty milliseconds is twenty renders a second, fast enough to read as continuous. On WebAssembly this
saves CPU; on Blazor Server it saves circuit traffic, which matters more, because there every render
is a diff sent over a network.

## 3. Text accumulation

Text parts accumulate in a `StringBuilder` and materialise lazily. Appending a token is amortised
constant time, and `Length` is available without building the string, which matters in hot paths such
as auto-scroll checks.

The naive alternative, reassigning a string per token, is quadratic, and on a long answer it is the
single largest cost in the system.

## 4. Incremental parsing

`IncrementalMarkdownParser` keeps the blocks before the last confirmed boundary and re-parses only
the tail. Repeated full parses are quadratic in total; this is linear. The library has a test that
fails if the incremental parser is not measurably faster than repeated full parses over a
four-hundred-paragraph document, and another that asserts the incremental result matches a one-shot
parse at **every prefix**.

Partial JSON is re-parsed on each delta, but only when read: `DisplayArguments`, `Value`, and `Props`
recompute at most once per change and only if something asks for them. A renderer that ignores
partial arguments pays nothing for them.

## 5. Virtualization, past a threshold

```csharp
options with { VirtualizeThreshold = 60 }
```

Below the threshold every message is rendered. Virtualization costs correctness in small lists, where
the variable heights of chat messages make the scrollbar jump; below sixty, rendering everything is
both simpler and faster. Above it, only what fits on screen is rendered.

During static prerendering a bounded tail is rendered instead, because `Virtualize` needs a measured
viewport that does not exist yet, and an empty transcript is a worse first paint than a partial one.

## Auto-scroll

Scroll position is observed in the browser and reported to .NET only when it crosses the threshold,
so scrolling produces at most one interop call per transition rather than one per frame. New content
follows the view only while the user is already at the bottom.

## Cancellation

`Stop` cancels a linked token that reaches the transport, the pacer, and any running tool. The partial
answer is kept and marked cancelled. Nothing is left running.

`ChatSessionOptions.Timeout` bounds a whole turn including tool round trips, and `MaxToolRoundtrips`
bounds the loop, because a model that keeps calling tools will otherwise spend money indefinitely.

## Choosing settings per host

| Setting | Blazor Server | WebAssembly |
| --- | --- | --- |
| `RenderInterval` | 50 to 80 ms; every render is network traffic | 16 to 50 ms |
| `Pacing` | keep it; it reduces render count as well as jitter | optional |
| `VirtualizeThreshold` | lower, to keep diffs small | default |
| Attachments | upload out of band, not into circuit memory | inline is fine |

## Measuring

`RenderScheduler` exposes `RenderCount` and `CoalescedCount`. `IncrementalMarkdownParser` exposes
`StableLength` and `StableBlockCount`, which measure how much parsing work is being reused. Both are
used by the test suite and both are useful in a diagnostics panel.
