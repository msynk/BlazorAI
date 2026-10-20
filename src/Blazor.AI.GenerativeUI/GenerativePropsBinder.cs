using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazor.AI.GenerativeUI;

/// <summary>
/// The outcome of binding model-produced JSON onto a registered component.
/// </summary>
/// <param name="Parameters">The parameters to pass to the component.</param>
/// <param name="Errors">
/// Properties that could not be bound, with the reason. Non-fatal: the component still renders with
/// whatever did bind, because a partially usable card beats an error message.
/// </param>
public sealed record GenerativeBindResult(
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Supplied property names the component does not declare as bindable, and which were therefore
    /// ignored.
    /// </summary>
    /// <remarks>
    /// Not errors: models routinely add a property nobody asked for, and the card is no less complete
    /// for leaving it out. Listed for diagnostics, such as noticing that a model guesses property
    /// names because the schema it was given is out of date.
    /// </remarks>
    public IReadOnlyList<string> IgnoredProperties { get; init; } = [];

    /// <summary>
    /// <see langword="true"/> when every declared property supplied bound cleanly and every required
    /// one was present. Ignored properties do not count against it.
    /// </summary>
    public bool IsComplete => Errors.Count == 0;
}

/// <summary>
/// Converts model-produced JSON into Blazor component parameters.
/// </summary>
/// <remarks>
/// <para>
/// The binder is intentionally strict about what it accepts and forgiving about what it does with
/// the rest. Unknown property names are ignored rather than passed through, so a hallucinated
/// property cannot reach the component, and are listed in
/// <see cref="GenerativeBindResult.IgnoredProperties"/> rather than reported as errors; a value of
/// the wrong shape is reported and skipped rather than thrown, so one bad field does not lose the
/// whole card.
/// </para>
/// <para>
/// It tolerates partial input by design, since props stream in and a component is rendered several
/// times before the payload is finished.
/// </para>
/// </remarks>
public static class GenerativePropsBinder
{
    /// <remarks>
    /// Enums are read from their names because that is what the component schema tells the model to
    /// send; integers are still accepted.
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Binds a JSON object onto the parameters of a registered component.
    /// </summary>
    /// <param name="component">The registration, which defines what may be set.</param>
    /// <param name="props">
    /// The model-produced properties. Anything but an object binds nothing, and leaves every
    /// required property reported as missing.
    /// </param>
    /// <returns>The parameters to render with, and any problems found.</returns>
    public static GenerativeBindResult Bind(GenerativeComponent component, JsonElement? props)
    {
        ArgumentNullException.ThrowIfNull(component);

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var errors = new List<string>();
        var ignored = new List<string>();

        // A payload that is not an object binds nothing, but the required check still runs: a
        // component whose props never arrived is as incomplete as one whose props left a field out.
        foreach (var property in props is { ValueKind: JsonValueKind.Object } payload ? payload.EnumerateObject() : [])
        {
            var declared = component.FindProperty(property.Name);
            if (declared is null)
            {
                ignored.Add(property.Name);
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (IsNullable(declared.Type)) parameters[declared.Name] = null;
                continue;
            }

            try
            {
                parameters[declared.Name] = property.Value.Deserialize(declared.Type, Options);
            }
            catch (JsonException exception)
            {
                errors.Add($"'{property.Name}' could not be read as {declared.Type.Name}: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                errors.Add($"'{property.Name}' could not be read as {declared.Type.Name}: {exception.Message}");
            }
            catch (InvalidOperationException exception)
            {
                // Raised for parameter types the serializer cannot construct, such as a record whose
                // constructor parameters do not match its properties.
                errors.Add($"'{property.Name}' could not be read as {declared.Type.Name}: {exception.Message}");
            }
        }

        foreach (var required in component.Properties)
        {
            if (required.IsRequired && !parameters.ContainsKey(required.Name))
            {
                errors.Add($"'{required.Name}' is required by '{component.Name}'.");
            }
        }

        return new GenerativeBindResult(parameters, errors) { IgnoredProperties = ignored };
    }

    private static bool IsNullable(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
