using System.Text.Json;

namespace Blazor.AI;

/// <summary>
/// The result of one tool call.
/// </summary>
/// <remarks>
/// Carries both the structured <see cref="Value"/> that goes back to the model and the optional
/// <see cref="Text"/> that the UI shows, because the best rendering for a human is rarely the raw
/// JSON the model asked for.
/// </remarks>
public sealed record ToolOutcome
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private ToolOutcome() { }

    /// <summary>Whether the call succeeded.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>The structured result, sent back to the model.</summary>
    public JsonElement? Value { get; private init; }

    /// <summary>
    /// A human-readable rendering of the result, shown when no custom renderer is registered for the
    /// tool. Falls back to the JSON of <see cref="Value"/> when not set.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>The failure, when <see cref="IsSuccess"/> is <see langword="false"/>.</summary>
    public AiError? Error { get; private init; }

    /// <summary>Sources the tool consulted, merged into the message citation list.</summary>
    public IReadOnlyList<Citation>? Citations { get; init; }

    /// <summary>How long the call took.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Application-defined values carried through to custom tool-result renderers.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    /// <summary>Creates a successful outcome carrying a JSON value.</summary>
    /// <param name="value">The structured result.</param>
    /// <param name="text">An optional human-readable rendering.</param>
    /// <returns>The outcome.</returns>
    public static ToolOutcome Success(JsonElement value, string? text = null) =>
        new() { IsSuccess = true, Value = value, Text = text };

    /// <summary>Creates a successful outcome by serializing a typed value.</summary>
    /// <typeparam name="T">The result model.</typeparam>
    /// <param name="value">The result.</param>
    /// <param name="options">Serializer options. Defaults to web defaults.</param>
    /// <returns>The outcome.</returns>
    public static ToolOutcome Success<T>(T value, JsonSerializerOptions? options = null) =>
        new() { IsSuccess = true, Value = JsonSerializer.SerializeToElement(value, options ?? WebOptions) };

    /// <summary>Creates a successful outcome carrying only text.</summary>
    /// <param name="text">The result text.</param>
    /// <returns>The outcome.</returns>
    public static ToolOutcome FromText(string text) =>
        new() { IsSuccess = true, Text = text, Value = JsonSerializer.SerializeToElement(text) };

    /// <summary>Creates a failed outcome.</summary>
    /// <param name="error">The failure.</param>
    /// <returns>The outcome.</returns>
    public static ToolOutcome Failure(AiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ToolOutcome { IsSuccess = false, Error = error };
    }

    /// <summary>Creates a failed outcome from a message.</summary>
    /// <param name="message">The failure message.</param>
    /// <returns>The outcome.</returns>
    public static ToolOutcome Failure(string message) =>
        Failure(new AiError(message) { Kind = AiErrorKind.Tool });

    /// <summary>
    /// The text to display for this outcome, falling back to the JSON of <see cref="Value"/>.
    /// </summary>
    /// <returns>Display text, or <see langword="null"/> when there is nothing to show.</returns>
    public string? GetDisplayText()
    {
        if (Text is not null) return Text;
        if (Error is not null) return Error.Message;
        return Value?.ValueKind switch
        {
            null or JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => Value.Value.GetString(),
            _ => Value.Value.ToString(),
        };
    }
}
