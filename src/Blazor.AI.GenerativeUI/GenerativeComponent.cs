using System.Text.Json;

namespace Blazor.AI.GenerativeUI;

/// <summary>
/// One bindable property of a registered generative component.
/// </summary>
/// <param name="Name">The parameter name, as the model must spell it.</param>
/// <param name="Type">The CLR type the JSON value is converted to.</param>
/// <param name="Description">What the property means, included in the schema sent to the model.</param>
/// <param name="IsRequired">Whether the model must supply it.</param>
public sealed record GenerativeProperty(string Name, Type Type, string? Description = null, bool IsRequired = false);

/// <summary>
/// A Blazor component that model output is allowed to instantiate.
/// </summary>
/// <remarks>
/// <para>
/// The registration is the security boundary. A model can only name components that appear here,
/// and can only set the properties listed on them. Everything else in the payload is discarded
/// rather than reflected onto the component.
/// </para>
/// <para>
/// It is also the prompting surface: <see cref="Schema"/> is the JSON Schema you give the model so
/// it knows what it may render and with what.
/// </para>
/// </remarks>
public sealed class GenerativeComponent
{
    /// <summary>Creates a registration.</summary>
    /// <param name="name">The name the model uses.</param>
    /// <param name="componentType">The Blazor component type.</param>
    /// <param name="properties">The bindable properties.</param>
    /// <param name="description">What the component is for.</param>
    public GenerativeComponent(
        string name,
        Type componentType,
        IReadOnlyList<GenerativeProperty> properties,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(componentType);
        ArgumentNullException.ThrowIfNull(properties);

        Name = name;
        ComponentType = componentType;
        Properties = properties;
        Description = description;
        Schema = BuildSchema(this);
    }

    /// <summary>The name the model uses.</summary>
    public string Name { get; }

    /// <summary>The Blazor component type.</summary>
    public Type ComponentType { get; }

    /// <summary>What the component is for, included in the schema.</summary>
    public string? Description { get; }

    /// <summary>The properties the model may set.</summary>
    public IReadOnlyList<GenerativeProperty> Properties { get; }

    /// <summary>The JSON Schema describing this component to the model.</summary>
    public JsonElement Schema { get; }

    /// <summary>Finds a property by name, matching case-insensitively as models are inconsistent.</summary>
    /// <param name="name">The property name.</param>
    /// <returns>The property, or <see langword="null"/> when the component has no such property.</returns>
    public GenerativeProperty? FindProperty(string name)
    {
        foreach (var property in Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property;
        }

        return null;
    }

    private static JsonElement BuildSchema(GenerativeComponent component)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        var required = new List<string>();

        foreach (var property in component.Properties)
        {
            properties[property.Name] = SchemaFor(property);
            if (property.IsRequired) required.Add(property.Name);
        }

        var schema = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };

        if (component.Description is not null) schema["description"] = component.Description;
        if (required.Count > 0) schema["required"] = required;

        return JsonSerializer.SerializeToElement(schema);
    }

    /// <summary>
    /// Describes one property as JSON Schema, the way the binder reads it: nested objects with their
    /// own <c>properties</c>, collections with <c>items</c>, enumerations as their names, and dates,
    /// identifiers and addresses as formatted strings. See <see cref="GenerativeSchema"/>.
    /// </summary>
    private static Dictionary<string, object?> SchemaFor(GenerativeProperty property) =>
        GenerativeSchema.Describe(property.Type, property.Description);
}
