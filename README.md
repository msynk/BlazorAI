# Blazor.AI

The AI UI foundation for Blazor: conversations, streaming, tool calling, agents, citations,
artifacts, structured output, and generative UI — as a coherent framework rather than a pile of
components.

Native C# and Blazor throughout. No JavaScript AI library is wrapped, no provider SDK is referenced,
and no component in the library knows which model produced what it is drawing.

```razor
<AiChat />
```

That is a working chat: transcript, composer, streaming, Markdown, tool cards, error recovery, and
keyboard and screen-reader support. Everything below it is available when you need it.

## Why this exists

Chat is the easy part. What takes the time is everything around it, and it is the same work in every
application:

- Markdown that is **still being written** — an unclosed fence, an unmatched `**`, a half-written
  table row.
- JSON that is **not valid yet**, because the model is four characters into a string.
- A tool call that has to be **shown, approved, run, timed, and answered**.
- An agent whose activity has to be **legible** rather than hidden behind a spinner.
- A conversation of five hundred messages that must not **re-render the world** on every token.

Blazor.AI solves each of those once, in the open, with tests.

## Install

```bash
dotnet add package Blazor.AI.Components
```

```csharp
builder.Services.AddBlazorAI(ai => ai.UseTransport<MyTransport>());
builder.Services.AddBlazorAIComponents();
```

```html
<link rel="stylesheet" href="_content/Blazor.AI.Components/blazor-ai.css" />
```

## The one interface you implement

Everything the framework does is downstream of a single boundary. Implement it over a provider SDK,
an HTTP endpoint, a local model, an orchestrator, or a fixture in a test:

```csharp
public sealed class MyTransport : IAiTransport
{
    public async IAsyncEnumerable<AiEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new MessageStartedEvent();

        await foreach (var token in _model.StreamAsync(request, cancellationToken))
        {
            yield return new TextDeltaEvent(token);
        }

        yield return new UsageUpdatedEvent(new TokenUsage { OutputTokens = 128 });
        yield return new MessageCompletedEvent();
    }
}
```

The UI cannot tell the difference between that and anything else, which is the point.

## Packages

| Package | Contains | Depends on |
| --- | --- | --- |
| `Blazor.AI.Abstractions` | Contracts: parts, events, transports, tools, models | nothing but the BCL |
| `Blazor.AI.Core` | Conversation state, the streaming reducer, sessions, tools | Abstractions |
| `Blazor.AI.Markdown` | Incremental Markdown parser and syntax highlighter | Abstractions |
| `Blazor.AI.GenerativeUI` | Component registry and safe property binding | Core |
| `Blazor.AI.Components` | The component library and design system | all of the above |

No package references a provider. Core and Markdown do not reference Blazor at all, so a server that
only produces events can use them without pulling in a renderer.

## What is in the box

**Conversation** — `AiChat`, `MessageList`, `ChatMessage`, `MessageActions`, `ConversationList`,
`ScrollToLatest`, `EmptyState`.

**Input** — `PromptInput` with attachments, drag and drop, paste, slash commands, mentions,
dictation, submission state, and cancellation; `ModelSelector`; `VoiceInput`.

**Agents and tools** — `AgentRunView`, `AgentStepView`, `AgentStatusBadge`, `ToolCallView`,
`ToolApproval`, progress, timing, and per-tool custom renderers.

**Content** — streaming Markdown, syntax highlighting for fourteen languages, tables, mathematics,
JSON trees, images, files, `SourceCard`, `SourceList`, inline `CitationMarker`.

**Artifacts** — versioned documents, code, HTML in a sandboxed frame, JSON, tables, charts, images,
with copy, download, version history, and full-screen.

**Generative UI and forms** — a registry of components model output may instantiate, a binder that
refuses everything else, and `AiForm` built from a JSON Schema with real validation.

**Operations** — `TokenUsageView`, `ContextMeter`, cost estimation, throughput, latency.

## Documentation

| Guide | Covers |
| --- | --- |
| [Getting started](docs/getting-started.md) | Installation, wiring, a first transport |
| [Architecture](docs/architecture.md) | Layers, the event model, why it is shaped this way |
| [Concepts](docs/concepts.md) | Conversations, messages, parts, sessions |
| [Streaming](docs/streaming.md) | The event vocabulary, pacing, partial JSON |
| [Tools](docs/tools.md) | Defining, approving, rendering, and executing tools |
| [Agents](docs/agents.md) | Runs, plans, steps, and asking the user |
| [Citations and artifacts](docs/citations-and-artifacts.md) | Anchored attribution and versioned output |
| [Generative UI](docs/generative-ui.md) | Registries, binding, and the security boundary |
| [Extensibility](docs/extensibility.md) | Every registry and how to replace built-in behaviour |
| [Theming](docs/theming.md) | Design tokens, dark mode, density |
| [Performance](docs/performance.md) | Where the render budget goes and how it is kept |
| [Accessibility](docs/accessibility.md) | What is guaranteed and what you must supply |
| [Testing](docs/testing.md) | Testing streaming UI without a model |

## The demo

Twenty scenarios, from a three-line chat to a five-hundred-message virtualized transcript. It runs
with no API key, because the transports are scripted C#.

```bash
dotnet run --project samples/Blazor.AI.Demo
```

The demo is a Blazor Web App using **interactive auto**: pages are prerendered on the server and
then take over in WebAssembly, with one service registration shared by both hosts. That is the
clearest available proof that nothing in the framework is host-specific.

## Building

```bash
dotnet build BlazorAI.slnx
dotnet test  BlazorAI.slnx
```

## Licence

MIT. See [LICENSE](LICENSE).
