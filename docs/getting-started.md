# Getting started

## Install

```bash
dotnet add package Blazor.AI.Components
```

That brings `Core`, `Markdown`, `GenerativeUI`, and `Abstractions` with it. A project that only
produces events — a server-side orchestrator, say — can reference `Blazor.AI.Core` alone and skip
the renderer entirely.

## Register the services

```csharp
using Blazor.AI;
using Blazor.AI.Components;

builder.Services.AddBlazorAI(ai => ai
    .UseTransport<MyTransport>()
    .AddModels(models, defaultModelId: "my-model"));

builder.Services.AddBlazorAIComponents();
```

`AddBlazorAI` registers the state and transport side; `AddBlazorAIComponents` registers browser
interop, the renderer registries, and presentation defaults. They are separate so a project can use
one without the other.

## Reference the stylesheet

```html
<link rel="stylesheet" href="_content/Blazor.AI.Components/blazor-ai.css" />
```

Everything the library draws reads from custom properties defined in that file, so this is also
where theming starts. See [Theming](theming.md).

There is no script tag. The browser helpers are an ES module imported on first use, so a page that
renders a transcript without a composer never downloads them.

## Write a transport

This is the only interface an application must implement.

```csharp
using System.Runtime.CompilerServices;
using Blazor.AI;

public sealed class MyTransport(IMyProviderClient client) : IAiTransport
{
    public async IAsyncEnumerable<AiEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new MessageStartedEvent();

        await foreach (var chunk in client.StreamAsync(Translate(request), cancellationToken))
        {
            if (chunk.Text is { Length: > 0 } text) yield return new TextDeltaEvent(text);
            if (chunk.Reasoning is { Length: > 0 } thought) yield return new ReasoningDeltaEvent(thought);
        }

        yield return new MessageCompletedEvent();
    }
}
```

Three rules keep a transport well behaved:

1. **Honour the cancellation token promptly.** The stop button is only as responsive as you make it.
2. **Prefer `ErrorEvent` to throwing** for failures you expect. An exception that escapes is caught
   and translated, but you lose the chance to say anything useful about it.
3. **Do not run tools.** Emit the call and let the session decide. That is what makes approval,
   timing, and rendering work without you implementing them.

## Add the chat

```razor
@page "/chat"

<AiChat EmptyTitle="How can I help?" />
```

`AiChat` creates a session from the registered transport when you do not give it one. Hold the
session yourself when you need to drive it from outside the component:

```razor
<AiChat Session="_session" />

@code {
    private ChatSession? _session;

    [Inject] private ChatSessionFactory Sessions { get; set; } = default!;

    protected override void OnInitialized() => _session = Sessions.Create();

    private Task AskAsync() => _session!.SubmitAsync("What changed in this release?");
}
```

## Add a tool

```csharp
builder.Services.AddBlazorAI(ai => ai
    .UseTransport<MyTransport>()
    .AddTool(AiTool.Create<WeatherArgs, WeatherResult>(
        "get_weather",
        "Look up the current weather for a city.",
        (args, token) => weather.GetAsync(args.City, token))));

public sealed record WeatherArgs(
    [property: Description("The city to look up.")] string City);
```

The JSON Schema for `WeatherArgs` is generated and travels on the request, so your transport can
hand it straight to a provider. A destructive tool asks for approval automatically:

```csharp
AiTool.Create<DeleteArgs, DeleteResult>(
    "delete_records",
    "Permanently delete records matching a filter.",
    DeleteAsync,
    definition => definition with { Risk = ToolRisk.Destructive });
```

See [Tools](tools.md).

## Choose the render mode

The library works unchanged on Blazor Server, WebAssembly, and interactive auto. Two settings are
worth revisiting per mode:

```csharp
builder.Services.AddBlazorAIComponents(options => options with
{
    // Higher on Server, where every render is a diff over a network.
    RenderInterval = TimeSpan.FromMilliseconds(60),

    // Below this, every message is rendered; above it, only what fits on screen.
    VirtualizeThreshold = 60,
});
```

During static prerendering a bounded tail of the transcript is rendered rather than an empty box,
because `Virtualize` needs a measured viewport that does not exist yet.

## Next

- [Concepts](concepts.md) — what a message actually is
- [Streaming](streaming.md) — the full event vocabulary
- [Extensibility](extensibility.md) — replacing any part of the rendering
