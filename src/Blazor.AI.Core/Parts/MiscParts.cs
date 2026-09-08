using System.Text.Json;

namespace Blazor.AI;

/// <summary>
/// A reference to an artifact held by the conversation.
/// </summary>
/// <remarks>
/// The artifact itself lives on the conversation so that revisions across several turns update one
/// object. The part only says where it was introduced, which is what lets the message list show an
/// inline card that opens the shared artifact panel.
/// </remarks>
public sealed class ArtifactPart : MessagePart
{
    /// <summary>Creates an artifact reference.</summary>
    /// <param name="artifactId">The artifact identifier.</param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public ArtifactPart(string artifactId, string? partId = null) : base(partId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArtifactId = artifactId;
    }

    /// <inheritdoc />
    public override string Kind => MessagePartKinds.Artifact;

    /// <summary>The artifact identifier.</summary>
    public string ArtifactId { get; }

    /// <summary>
    /// The artifact version introduced here, so an older turn keeps showing what it produced.
    /// </summary>
    /// <remarks>
    /// Named distinctly from <see cref="MessagePart.Version"/>, which counts changes to the part
    /// itself and means something entirely different.
    /// </remarks>
    public int? ArtifactVersion { get; set; }
}

/// <summary>
/// A tool result carried on its own, for providers that emit results as separate tool messages.
/// </summary>
/// <remarks>
/// The framework reconciles these onto the originating <see cref="ToolCallPart"/> when it can find
/// one, and renders them standalone when it cannot.
/// </remarks>
public sealed class ToolResultPart : MessagePart
{
    /// <summary>Creates a tool result part.</summary>
    /// <param name="toolCallId">The call being answered.</param>
    /// <param name="outcome">The result.</param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public ToolResultPart(string toolCallId, ToolOutcome outcome, string? partId = null) : base(partId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        ArgumentNullException.ThrowIfNull(outcome);
        ToolCallId = toolCallId;
        Outcome = outcome;
    }

    /// <inheritdoc />
    public override string Kind => MessagePartKinds.ToolResult;

    /// <summary>The call being answered.</summary>
    public string ToolCallId { get; }

    /// <summary>The tool that produced the result, when known.</summary>
    public string? ToolName { get; set; }

    /// <summary>The result.</summary>
    public ToolOutcome Outcome { get; }
}

/// <summary>
/// An error surfaced inside the message body rather than replacing the whole message.
/// </summary>
/// <remarks>
/// Used when part of a response failed but the rest is worth keeping, for example when one of
/// several parallel tool calls threw.
/// </remarks>
public sealed class ErrorPart : MessagePart
{
    /// <summary>Creates an error part.</summary>
    /// <param name="error">The failure.</param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public ErrorPart(AiError error, string? partId = null) : base(partId)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    /// <inheritdoc />
    public override string Kind => MessagePartKinds.Error;

    /// <summary>The failure.</summary>
    public AiError Error { get; }
}

/// <summary>
/// An application-defined part carrying a typed payload.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
/// <remarks>
/// The escape hatch for content the framework has no concept for. Register a renderer against the
/// same <see cref="MessagePart.Kind"/> string and it participates in message rendering, copying,
/// and virtualization like any built-in part.
/// </remarks>
/// <example>
/// <code>
/// var part = new CustomPart&lt;Invoice&gt;("acme:invoice", invoice);
/// message.AddPart(part);
/// </code>
/// </example>
public sealed class CustomPart<T> : MessagePart, ITextualPart
{
    private T _data;

    /// <summary>Creates a custom part.</summary>
    /// <param name="kind">The discriminator a renderer is registered against.</param>
    /// <param name="data">The payload.</param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public CustomPart(string kind, T data, string? partId = null) : base(partId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
        _data = data;
    }

    /// <inheritdoc />
    public override string Kind { get; }

    /// <summary>The payload. Setting it notifies observers.</summary>
    public T Data
    {
        get => _data;
        set
        {
            _data = value;
            NotifyChanged();
        }
    }

    /// <summary>
    /// The plain-text projection, used for copy and export. Defaults to nothing.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>Notifies observers after mutating <see cref="Data"/> in place.</summary>
    public void Refresh() => NotifyChanged();
}

/// <summary>
/// A loosely typed application-defined part, for payloads that arrive as JSON.
/// </summary>
public sealed class CustomJsonPart : MessagePart, ITextualPart
{
    /// <summary>Creates a custom JSON part.</summary>
    /// <param name="kind">The discriminator a renderer is registered against.</param>
    /// <param name="data">The payload.</param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public CustomJsonPart(string kind, JsonElement data, string? partId = null) : base(partId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
        Data = data;
    }

    /// <inheritdoc />
    public override string Kind { get; }

    /// <summary>The payload.</summary>
    public JsonElement Data { get; }

    /// <inheritdoc />
    public string? Text { get; set; }
}
