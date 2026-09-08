# Extensibility

Every extension point is a registry, and every registry is consulted before the built-in behaviour.
Nothing in the library requires a fork to change.

## Renderers

```csharp
services.ConfigureAiRenderers(renderers =>
{
    renderers.RegisterPart<InvoiceCard>("acme:invoice");
    renderers.RegisterTool<WeatherCard>("get_weather");
    renderers.RegisterArtifact(new ArtifactKind("chart"), RenderChart);
    renderers.RegisterStructured("invoice", RenderInvoice);
    renderers.CitationRenderer = RenderCitation;
});
```

Component overloads pass the subject as a parameter: `Part` for parts, `Call` for tools. Delegate
overloads receive a context carrying the subject, the message, the conversation, and the session, so
a custom renderer can act as well as display:

```csharp
renderers.RegisterPart("acme:approval", context => builder =>
{
    builder.OpenComponent<ApprovalCard>(0);
    builder.AddComponentParameter(1, "Part", context.Part);
    builder.AddComponentParameter(2, "Session", context.Session);
    builder.CloseComponent();
});
```

Custom renderers run inside an error boundary. A payload shaped differently from what the renderer
expected produces a small notice, not a blank page, which matters because the payload came from a
model.

## Message parts

```csharp
public sealed class PollPart(Poll poll) : MessagePart
{
    public override string Kind => "acme:poll";

    public Poll Poll { get; } = poll;

    public void Vote(string option)
    {
        Poll.Record(option);
        NotifyChanged();   // the one component drawing this part re-renders
    }
}
```

Calling `NotifyChanged` is the whole contract. Everything else follows: keyed rendering,
virtualization, and per-part re-render granularity.

`CustomPart<T>` and `CustomJsonPart` cover the cases where a class of your own is more ceremony than
the payload deserves.

## Tools and policy

```csharp
services.AddBlazorAI(ai => ai
    .AddTool(AiTool.Create(...))          // a delegate
    .AddTool<IndexSearchTool>()           // a class with dependencies
    .UseApprovalPolicy<BudgetPolicy>());
```

Tools can also be registered at runtime through `IToolRegistry`, which is how an application enables
a capability for some users and not others.

## Transports

```csharp
ai.UseTransport<MyTransport>();                 // scoped, from DI
ai.UseTransport(new FixtureTransport(events));  // an instance
ai.UseTransport(sp => Build(sp));               // a factory
```

Transports compose. Wrap one to add retries, logging, or a recording:

```csharp
public sealed class RecordingTransport(IAiTransport inner, IEventLog log) : IAiTransport
{
    public async IAsyncEnumerable<AiEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in inner.StreamAsync(request, cancellationToken))
        {
            log.Record(evt);
            yield return evt;
        }
    }
}
```

## Markdown

```csharp
services.AddBlazorAIComponents(options => options with
{
    Markdown = MarkdownOptions.Default with
    {
        Math = false,
        CitationMarkers = false,
        AllowRawHtml = false,   // leave off unless you trust the source as much as your own code
    },
});
```

Add a language to the highlighter without touching the library:

```csharp
LanguageRegistry.Register(new LanguageDefinition("kql")
{
    Aliases = ["kusto"],
    Keywords = new HashSet<string>(["where", "project", "summarize", "join"], StringComparer.Ordinal),
    LineComments = ["//"],
});
```

Replacing a built-in definition changes how that language is coloured everywhere.

## Text and presentation

```csharp
public sealed class NorwegianStrings : AiStrings
{
    public override string Send => "Send";
    public override string Stop => "Stopp";
    public override string Thinking => "Tenker";
    public override string ApproveTool(string tool) => $"Tillate {tool}?";
}

services.AddBlazorAIComponents(options => options with { Strings = new NorwegianStrings() });
```

Every string the components render comes from this object, including the formatters for durations,
byte counts, and token counts.

## Persistence and models

```csharp
ai.UseConversationStore<SqlConversationStore>();
ai.AddModels(models, providers, defaultModelId: "my-model");
```

Implement `IModelCatalog` directly when the model list comes from a gateway at runtime rather than
being fixed at startup.

## Scoped overrides

Everything above is a default. `AiTheme` cascades a different set of options to one region:

```razor
<AiTheme UiOptions="_compact" Theme="AiTheme.AiThemeMode.Dark">
    <AiChat />
</AiTheme>
```

Nesting works; an inner provider overrides the outer one.
