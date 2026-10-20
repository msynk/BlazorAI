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
  `Type`, unmatched-attribute catch-alls, anything marked `[GenerativeIgnore]`, and any parameter
  whose type's object graph reaches one of those (a record with an `Action` property is excluded
  whole, as is a `List<>` of it). That exclusion is what stops a payload from wiring up handlers or
  injecting attributes. `GenerativeUiRegistry.IsBindable(Type)` answers the question for any type.
- **Unknown properties are ignored**, not passed through. They are not errors and do not make a
  binding incomplete, since models routinely add a property nobody asked for, but they are listed
  in the bind result's `IgnoredProperties` for diagnostics.
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

### What the schema covers

Every parameter type is described the way the binder reads it, so a model that follows the schema
produces a payload that binds:

| CLR type | Schema |
| --- | --- |
| `string`, `char` | `string` |
| `bool` | `boolean` |
| `int`, `long`, `short`, `byte` and unsigned kin | `integer` |
| `double`, `float`, `decimal` | `number` |
| enums | `string` with `enum` listing the names (integers still bind) |
| `DateTime`, `DateTimeOffset` | `string`, `format: date-time` |
| `DateOnly` / `TimeOnly` / `TimeSpan` | `string` with `format` `date` / `time` / `duration` |
| `Guid` / `Uri` | `string` with `format` `uuid` / `uri` |
| arrays, `List<T>`, `IEnumerable<T>` | `array` with `items` describing `T` |
| dictionaries | `object` with `additionalProperties` describing the value type |
| records and classes | `object` with nested `properties`, `additionalProperties: false`, and `required` for members marked `[Required]` |
| `JsonElement`, `JsonNode`, `object` | an open schema; the value is handed over as it came |

Nested types are walked recursively to a bounded depth, and a type already on the path (a folder
whose children are folders) is cut off as a plain `object`, so a self-referencing model neither
recurses forever nor produces an infinite schema. A `Nullable<T>` is described as `T` and is not
required.

### Attributes

```csharp
[Description("Shows an invoice with its lines.")]       // fallback when Register() gives no description
public partial class InvoiceCard
{
    [Parameter, Required]                                // required without naming it at registration
    public string Customer { get; set; } = string.Empty;

    [Parameter, Description("One row per billed item.")] // carried into the schema
    public Line[] Lines { get; set; } = [];

    [Parameter, GenerativeIgnore]                        // the application sets this, never the model
    public string TenantKey { get; set; } = string.Empty;
}
```

`DescriptionAttribute` on a property, on a nested type's members, or on the component class itself
is carried into the schema. `[Required]` (DataAnnotations) marks a parameter or a nested member
required, in addition to the names passed to `Register`. `[GenerativeIgnore]` removes a parameter
or a nested member from both the schema and the bindable set.

A payload that never became an object, because the stream ended early or sent an array, binds
nothing and reports every required property as missing, so `IsComplete` is never true by accident.

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

`FromJsonSchema` maps the schema onto controls and never throws on a malformed one, since the schema
was written by a model:

- `type`: `string`, `integer`, `number`, `boolean`, `object` (a grouped fieldset of its
  `properties`), `array` (a list editor for scalar `items`, repeatable groups for object `items`).
  A list such as `["string", "null"]` uses the first non-null entry and makes the field optional.
- `format`: `email`, `uri`, `date`, `date-time`, `time`, `password`, and `textarea` as an extension
  for a multi-line box.
- `enum` with `enumNames` or `x-enumNames`, and `oneOf`/`anyOf` of `const` alternatives with
  `title`s, become a picker. `anyOf` with a `{"type":"null"}` branch makes the field optional.
- `const` and `readOnly` render the value without letting it be edited; `default` prefills.
- `minimum`, `maximum`, `exclusiveMinimum`/`exclusiveMaximum` (numeric or the draft-4 flag),
  `multipleOf`, `minLength`/`maxLength`, `minItems`/`maxItems`, `pattern`, and whole numbers only
  for `integer`.
- Local `$ref`s into `#/$defs/...` or `#/definitions/...` are followed; a `title` or `description`
  beside the reference wins over the target's.

```razor
<AiForm Definition="definition" OnSubmit="BookAsync" OnSubmitJson="BookJsonAsync" Submitted="@_submitted" />
```

`OnSubmit` receives the values as flat text keyed by path (`contact.email`, `tags[0]`,
`people[1].name`); `OnSubmitJson` receives a `JsonElement` in the shape of the schema, with nested
objects, arrays, and numbers and booleans encoded as such, which is usually what the tool call
wants. `AiFormDefinition.ToJson(values)` performs the same conversion on demand.

Validation happens in the application, not in the model, because a model asked to check its own
output will sometimes say yes when the answer is no. Numbers are parsed in the invariant culture,
because that is how a number input reports them whatever the page culture. Every control carries
`aria-required`, `aria-invalid` and an `aria-describedby` pointing at its hint and error, and the
error is a live `role="alert"`. Fields appear as the schema streams in, and anything already typed
survives a new field arriving; a field whose definition changes shape, such as a different schema
reusing the name with another type or prefill, is seeded afresh.
