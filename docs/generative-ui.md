# Generative UI

Generative UI lets a response produce typed, interactive components instead of only text. The hard
part is not rendering — it is making sure a model cannot render whatever it likes.

```text
model output                {"component":"forecast_card","props":{"city":"Oslo"}}
      │
      ▼
registry lookup             is "forecast_card" registered?            no  -> placeholder
      │ yes
      ▼
property binding            is "city" a declared bindable parameter?  no  -> discarded
      │ yes
      ▼
typed conversion            can it be read as string?                 no  -> reported, skipped
      │ yes
      ▼
Blazor component            <ForecastCard City="Oslo" />
```

## Registering

```csharp
services.ConfigureGenerativeUi(registry => registry.Register<ForecastCard>(
    "forecast_card",
    "Shows the current weather for one city.",
    required: "City"));
```

The component is an ordinary Blazor component. Nothing about it knows it is generative:

```razor
<div class="forecast-card">
    <div>@City</div>
    <div>@Degrees&deg;</div>
</div>

@code {
    [Parameter, Description("The city name.")]
    public string City { get; set; } = string.Empty;

    [Parameter, Description("Temperature in degrees Celsius.")]
    public int Degrees { get; set; }
}
```

`DescriptionAttribute` text is carried into the schema, which is what the model reads when deciding
what to pass.

## The security boundary

Registration is the boundary, and it is deliberately narrow.

- **Only registered names resolve.** A name that is not registered renders a small placeholder. The
  type name in the payload is never used to look anything up, so a model cannot reach an arbitrary
  type.
- **Only declared parameters bind.** The bindable set is derived from public `[Parameter]`
  properties, minus the ones no model should ever set: delegates, `EventCallback`, `RenderFragment`,
  and unmatched-attribute catch-alls. That exclusion is what stops a payload from wiring up handlers
  or injecting attributes.
- **Unknown properties are discarded**, not passed through, and reported in the bind result.
- **A value of the wrong shape is skipped**, not thrown. One bad field should not lose the card.
- **Rendering runs inside an error boundary**, so a component that throws on unexpected input
  degrades to a notice rather than taking the page down.

## Telling the model what exists

```csharp
var schema = registry.DescribeAll();
```

That produces a JSON Schema with one variant per registered component, each pinning the component
name and describing its properties. Put it in the system prompt or use it as the schema of a render
tool. Without it the model guesses names, and every guess renders a placeholder.

## Streaming properties

Properties can stream, and partial payloads still render:

```csharp
yield return new GenerativeUiDeltaEvent("forecast_card", """{"city":"Osl""");
```

The part re-parses leniently on every delta, so the card fills in as the payload arrives rather than
appearing whole at the end. Components should therefore tolerate defaults for values that have not
arrived yet — which they do naturally, since an unset `int` parameter is zero.

## Structured output

`StructuredDataPart` is the untyped sibling. When you want data rather than a component, render it
with a schema-specific renderer:

```csharp
services.ConfigureAiRenderers(renderers =>
    renderers.RegisterStructured("invoice", context => builder =>
    {
        builder.OpenComponent<InvoiceView>(0);
        builder.AddComponentParameter(1, "Data", context.Part.Value);
        builder.CloseComponent();
    }));
```

Without a registered renderer the framework reads the shape: an array of objects becomes a table
whose columns are the union of the keys seen, an object becomes a labelled key and value list, and
anything else becomes a JSON tree. All three cope with partial input.

```csharp
var typed = part.As<Invoice>();   // nullable members; absent fields stay null
```

## Forms

An AI form is confirmation rather than guessing: instead of calling a tool with arguments it
inferred, the model proposes a form, the user corrects it, and the corrected values execute.

```csharp
var definition = AiFormDefinition.FromJsonSchema(schema);
```

`FromJsonSchema` maps `type` and `format` onto controls — `email`, `date`, `date-time`, `uri`,
`textarea`, `enum` to a picker, `boolean` to a checkbox, numbers with `minimum` and `maximum`.

```razor
<AiForm Definition="definition" OnSubmit="BookAsync" Submitted="@_submitted" />
```

Validation happens in the application, not in the model, because a model asked to check its own
output will sometimes say yes when the answer is no. Fields appear as the schema streams in, and
anything already typed survives a new field arriving.
