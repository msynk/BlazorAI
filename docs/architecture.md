# Architecture

## The shape of the problem

An AI interface is not a form over data. The content arrives incrementally, is frequently invalid
while it arrives, may ask to run code, and may ask a question back. A framework for it has to answer
four questions well:

1. **What is a message?** Not a string. A mixed sequence of text, reasoning, tool calls, images,
   citations, artifacts, and whatever the application invents.
2. **How does content arrive?** As events, from something the UI must not know the shape of.
3. **Who owns side effects?** Tools have to run somewhere, and that somewhere must not be a
   component.
4. **What does it cost to draw?** Every answer above is worthless if a fast model makes the UI
   stutter.

## Layers

```text
   Application
        │  implements IAiTransport, registers tools and renderers
        ▼
┌──────────────────────┐
│  Blazor.AI.Components│  components, design tokens, browser interop
└──────────┬───────────┘
           │ observes
┌──────────▼───────────┐   ┌────────────────────┐   ┌────────────────────┐
│   Blazor.AI.Core     │   │ Blazor.AI.Markdown │   │Blazor.AI.Generative│
│ state, reducer,      │   │ incremental parser │   │ UI registry and    │
│ sessions, tools      │   │ and highlighter    │   │ property binding   │
└──────────┬───────────┘   └────────────────────┘   └────────────────────┘
           │ speaks
┌──────────▼────────────────────────────────────────────────────────────┐
│ Blazor.AI.Abstractions   parts · events · transports · tools · models │
└───────────────────────────────────────────────────────────────────────┘
```

Dependencies point one way. `Core` and `Markdown` do not reference Blazor, so they run in a console
application, a background service, or a test with no renderer. `Abstractions` references nothing but
the base class library and the dependency injection contracts.

## The event boundary

Everything the framework can display is expressible as a stream of `AiEvent`:

```csharp
IAsyncEnumerable<AiEvent> StreamAsync(AiRequest request, CancellationToken cancellationToken);
```

A transport translates whatever its provider sends into that vocabulary. `ConversationStreamProcessor`
translates the vocabulary into conversation state. Components observe the state. Nothing downstream
of the transport references a provider type, which is why swapping providers is a one-class change
and why the entire demo runs with no network at all.

### Identifiers are optional

The simplest transport emits `new TextDeltaEvent("hi")` and lets the framework resolve the target.
A transport driving several parts in parallel addresses each explicitly. Both work, and the same
reducer handles both:

```csharp
yield return new TextDeltaEvent("left ")  { PartId = "a" };
yield return new TextDeltaEvent("right ") { PartId = "b" };
yield return new TextDeltaEvent("side")   { PartId = "a" };
```

## State is mutable and observed at three grains

Streaming produces hundreds of updates a second. Producing a new immutable conversation per token
would allocate a new object graph per token, so the model is mutable and observable instead:

| Grain | Event | Who listens |
| --- | --- | --- |
| Part content | `MessagePart.Changed` | the one component drawing that part |
| Message structure | `AiMessage.Changed` | the message component |
| Thread structure | `Conversation.MessageAdded` and friends | the list |

A token arriving in the last paragraph of a long answer re-renders one paragraph. It does not touch
the tool cards, code blocks, or citations above it.

Each of those types also exposes a monotonic `Version`, so a component can decide whether to
re-render by comparing two integers.

## Sessions own the loop

`ChatSession` is where the parts meet:

```text
SubmitAsync
   └─ append the user message
      └─ for each round, up to MaxToolRoundtrips
           ├─ stream from the transport, applying events
           ├─ collect tool calls whose arguments completed
           ├─ ask the approval policy about each
           ├─ wait for a human where one is required
           ├─ execute the rest concurrently
           └─ record results on the calls, then ask again
      └─ close open parts, stamp timings, raise TurnCompleted
```

Tool results are recorded on the originating `ToolCallPart` rather than appended as synthetic
messages. The transport reads them off the message it is handed on the next request, so there is one
representation of a tool call, and the UI has nothing to deduplicate.

The loop is bounded. A model that keeps calling tools stops after `MaxToolRoundtrips` with a visible
explanation, because the alternative is an application that spends money indefinitely.

## Rendering is deliberately behind

`RenderScheduler` collapses any number of change notifications into one render per interval, with
backpressure: while a flush is in flight, further requests set a flag rather than queueing. A slow
renderer therefore falls behind in *render count*, never in memory.

Fifty milliseconds is the default. On WebAssembly that saves CPU; on Blazor Server it saves circuit
traffic, which matters more, because there every render is a diff sent over a network.

## Where the difficulty actually is

Three problems make up most of the framework's value, and each has a dedicated component:

- **`PartialJson`** repairs truncated JSON into the closest valid document, so structured output and
  tool arguments are readable long before they are complete. It never invents data.
- **`IncrementalMarkdownParser`** tracks the point beyond which no future text can change the result
  and re-parses only the tail, turning quadratic work into linear work.
- **`ConversationStreamProcessor`** is a pure reducer with no side effects, which is why recorded
  events can be replayed into it to test rendering with no model involved.

## Extension points

Every one of these is a registry, resolved before the built-in behaviour:

| Registry | Replaces |
| --- | --- |
| `AiRendererRegistry` | how a part kind, tool, artifact kind, structured schema, or citation draws |
| `IGenerativeUiRegistry` | which components model output may instantiate |
| `IToolRegistry` | what the model may call |
| `IToolApprovalPolicy` | which calls need a human |
| `IModelCatalog` | the models on offer |
| `IConversationStore` | where conversations live |
| `AiStrings` | every piece of user-facing text |
| `AiUiOptions` | presentation defaults |
