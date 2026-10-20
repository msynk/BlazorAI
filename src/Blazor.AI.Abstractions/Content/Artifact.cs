namespace Blazor.AI;

/// <summary>
/// The type of an artifact, which selects a renderer.
/// </summary>
/// <remarks>
/// A string-backed value type rather than an <see langword="enum"/> so applications can introduce
/// artifact types the framework does not know about and register renderers for them. Comparison is
/// ordinal and case-insensitive.
/// </remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(Json.ArtifactKindJsonConverter))]
public readonly record struct ArtifactKind
{
    /// <summary>Prose, rendered as Markdown.</summary>
    public static readonly ArtifactKind Document = new("document");

    /// <summary>Source code, rendered with syntax highlighting.</summary>
    public static readonly ArtifactKind Code = new("code");

    /// <summary>A self-contained HTML page, rendered in a sandboxed frame.</summary>
    public static readonly ArtifactKind Html = new("html");

    /// <summary>A JSON value, rendered as a collapsible tree.</summary>
    public static readonly ArtifactKind Json = new("json");

    /// <summary>Tabular data, rendered as a sortable table.</summary>
    public static readonly ArtifactKind Table = new("table");

    /// <summary>A chart specification, rendered by the registered chart renderer.</summary>
    public static readonly ArtifactKind Chart = new("chart");

    /// <summary>A raster or vector image.</summary>
    public static readonly ArtifactKind Image = new("image");

    /// <summary>Inline SVG markup, sanitised before rendering.</summary>
    public static readonly ArtifactKind Svg = new("svg");

    /// <summary>A diagram description, rendered by the registered diagram renderer.</summary>
    public static readonly ArtifactKind Diagram = new("diagram");

    /// <summary>Creates an artifact kind from its wire value.</summary>
    /// <param name="value">The kind identifier. Must not be <see langword="null"/> or whitespace.</param>
    public ArtifactKind(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The underlying identifier, as it appears on the wire.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(ArtifactKind other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() => Value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;

    /// <summary>Converts a string to an <see cref="ArtifactKind"/>.</summary>
    /// <param name="value">The kind identifier.</param>
    public static implicit operator ArtifactKind(string value) => new(value);
}

/// <summary>
/// A substantial, self-contained piece of content produced alongside a conversation: a document, a
/// program, a table, a chart.
/// </summary>
/// <remarks>
/// Artifacts are addressed by id and versioned, so an assistant can revise one across several turns
/// while the UI keeps showing a single stable panel rather than a growing pile of code blocks.
/// </remarks>
public interface IArtifact
{
    /// <summary>Stable identifier, unique within a conversation.</summary>
    string ArtifactId { get; }

    /// <summary>The type of the artifact, which selects a renderer.</summary>
    ArtifactKind Kind { get; }

    /// <summary>Display title.</summary>
    string Title { get; }

    /// <summary>The current content, as text. Binary artifacts use <see cref="Url"/> instead.</summary>
    string? Content { get; }

    /// <summary>Programming language or format hint, for example <c>csharp</c> or <c>csv</c>.</summary>
    string? Language { get; }

    /// <summary>An addressable URL, for artifacts whose content is not inline text.</summary>
    string? Url { get; }

    /// <summary>One-based version number, incremented on every revision.</summary>
    int Version { get; }

    /// <summary><see langword="true"/> while content is still streaming in.</summary>
    bool IsStreaming { get; }

    /// <summary>When the artifact was first created.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>When the current version was produced.</summary>
    DateTimeOffset UpdatedAt { get; }

    /// <summary>Application-defined values carried through to custom artifact renderers.</summary>
    IReadOnlyDictionary<string, object?>? Metadata { get; }
}

/// <summary>
/// An immutable snapshot of an artifact at one version.
/// </summary>
/// <param name="Version">The one-based version number.</param>
/// <param name="Content">The content at this version.</param>
/// <param name="CreatedAt">When this version was produced.</param>
/// <param name="Summary">An optional description of what changed.</param>
public sealed record ArtifactVersion(int Version, string? Content, DateTimeOffset CreatedAt, string? Summary = null);
