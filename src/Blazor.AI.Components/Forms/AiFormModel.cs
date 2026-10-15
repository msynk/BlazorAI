using System.Text.Json;

namespace Blazor.AI.Components;

/// <summary>
/// The input control a form field renders as.
/// </summary>
public enum AiFieldType
{
    /// <summary>A single-line text box.</summary>
    Text = 0,

    /// <summary>A multi-line text box.</summary>
    MultilineText = 1,

    /// <summary>A numeric input.</summary>
    Number = 2,

    /// <summary>A checkbox.</summary>
    Boolean = 3,

    /// <summary>A picker over <see cref="AiFormField.Options"/>.</summary>
    Select = 4,

    /// <summary>A date picker.</summary>
    Date = 5,

    /// <summary>A date and time picker.</summary>
    DateTime = 6,

    /// <summary>An email input.</summary>
    Email = 7,

    /// <summary>A URL input.</summary>
    Url = 8,

    /// <summary>A masked input.</summary>
    Password = 9,
}

/// <summary>
/// One field of a model-generated form.
/// </summary>
/// <param name="Name">The key the value is submitted under.</param>
/// <param name="Label">The visible label. Falls back to a humanised name.</param>
public sealed record AiFormField(string Name, string? Label = null)
{
    /// <summary>The control to render.</summary>
    public AiFieldType Type { get; init; } = AiFieldType.Text;

    /// <summary>Help text under the control.</summary>
    public string? Description { get; init; }

    /// <summary>Placeholder text inside the control.</summary>
    public string? Placeholder { get; init; }

    /// <summary>The initial value, which a model may prefill.</summary>
    public string? Value { get; init; }

    /// <summary>Whether the field must be filled in.</summary>
    public bool Required { get; init; }

    /// <summary>Choices for a select field.</summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>The smallest accepted value, for numbers and dates.</summary>
    public double? Minimum { get; init; }

    /// <summary>The largest accepted value, for numbers and dates.</summary>
    public double? Maximum { get; init; }

    /// <summary>A regular expression the value must match.</summary>
    public string? Pattern { get; init; }

    /// <summary>The label shown for the field.</summary>
    public string DisplayLabel => Label ?? Humanize(Name);

    internal static string Humanize(string name)
    {
        if (name.Length == 0) return name;

        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '_' or '-') { builder.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) builder.Append(' ');
            builder.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }

        return builder.ToString();
    }
}

/// <summary>
/// A form the model asked for, which the user fills in and submits back.
/// </summary>
/// <remarks>
/// The point of an AI form is confirmation: rather than a model calling a tool with arguments it
/// guessed, it proposes a form, the user corrects it, and the corrected values are what execute.
/// </remarks>
public sealed record AiFormDefinition
{
    /// <summary>The fields, in display order.</summary>
    public IReadOnlyList<AiFormField> Fields { get; init; } = [];

    /// <summary>A heading above the form.</summary>
    public string? Title { get; init; }

    /// <summary>A line of explanation under the heading.</summary>
    public string? Description { get; init; }

    /// <summary>The label on the submit button. Defaults to <see cref="AiStrings.Submit"/>.</summary>
    public string? SubmitLabel { get; init; }

    /// <summary>
    /// The label on the cancel button. Defaults to <see cref="AiStrings.Cancel"/>; set it to an empty
    /// string to hide the button.
    /// </summary>
    /// <remarks>
    /// The defaults are left unset here rather than written in English, so that a form a model
    /// produces is labelled in whatever language the application's string table speaks.
    /// </remarks>
    public string? CancelLabel { get; init; }

    /// <summary>
    /// Builds a form from a JSON Schema object, so a model can describe one with the same vocabulary
    /// it already uses for tool arguments.
    /// </summary>
    /// <param name="schema">A JSON Schema object with a <c>properties</c> member.</param>
    /// <param name="values">Optional prefilled values, keyed by property name.</param>
    /// <returns>The form definition.</returns>
    public static AiFormDefinition FromJsonSchema(JsonElement schema, JsonElement? values = null)
    {
        var fields = new List<AiFormField>();

        if (schema.ValueKind is not JsonValueKind.Object || !schema.TryGetProperty("properties", out var properties))
        {
            return new AiFormDefinition { Fields = fields };
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var requiredList) && requiredList.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in requiredList.EnumerateArray())
            {
                if (item.GetString() is { } name) required.Add(name);
            }
        }

        foreach (var property in properties.EnumerateObject())
        {
            fields.Add(FromSchemaProperty(property.Name, property.Value, required.Contains(property.Name), values));
        }

        return new AiFormDefinition
        {
            Fields = fields,
            Title = schema.TryGetProperty("title", out var title) ? title.GetString() : null,
            Description = schema.TryGetProperty("description", out var description) ? description.GetString() : null,
        };
    }

    private static AiFormField FromSchemaProperty(string name, JsonElement node, bool required, JsonElement? values)
    {
        var type = node.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : "string";
        var format = node.TryGetProperty("format", out var formatNode) ? formatNode.GetString() : null;

        string[]? options = null;
        if (node.TryGetProperty("enum", out var enumNode) && enumNode.ValueKind is JsonValueKind.Array)
        {
            options = enumNode.EnumerateArray().Select(e => e.ToString()).ToArray();
        }

        var fieldType = (type, format, options) switch
        {
            (_, _, not null) => AiFieldType.Select,
            ("boolean", _, _) => AiFieldType.Boolean,
            ("integer" or "number", _, _) => AiFieldType.Number,
            (_, "date", _) => AiFieldType.Date,
            (_, "date-time", _) => AiFieldType.DateTime,
            (_, "email", _) => AiFieldType.Email,
            (_, "uri" or "url", _) => AiFieldType.Url,
            (_, "password", _) => AiFieldType.Password,
            (_, "textarea", _) => AiFieldType.MultilineText,
            _ => AiFieldType.Text,
        };

        string? value = null;
        if (values is { ValueKind: JsonValueKind.Object } payload && payload.TryGetProperty(name, out var prefill))
        {
            value = prefill.ValueKind is JsonValueKind.String ? prefill.GetString() : prefill.ToString();
        }

        return new AiFormField(name, node.TryGetProperty("title", out var label) ? label.GetString() : null)
        {
            Type = fieldType,
            Description = node.TryGetProperty("description", out var help) ? help.GetString() : null,
            Options = options,
            Required = required,
            Value = value,
            Minimum = node.TryGetProperty("minimum", out var min) && min.TryGetDouble(out var minValue) ? minValue : null,
            Maximum = node.TryGetProperty("maximum", out var max) && max.TryGetDouble(out var maxValue) ? maxValue : null,
            Pattern = node.TryGetProperty("pattern", out var pattern) ? pattern.GetString() : null,
        };
    }
}
