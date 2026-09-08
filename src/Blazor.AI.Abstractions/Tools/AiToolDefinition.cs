using System.Text.Json;

namespace Blazor.AI;

/// <summary>
/// How much damage a tool can do, used to pick a default approval policy and to style the
/// approval prompt.
/// </summary>
public enum ToolRisk
{
    /// <summary>Reads data and has no side effects.</summary>
    ReadOnly = 0,

    /// <summary>Changes state in a way that can be undone.</summary>
    Write = 1,

    /// <summary>Destructive, irreversible, or externally visible. Approve by default.</summary>
    Destructive = 2,
}

/// <summary>
/// The provider-independent description of a tool: what it is called, what it does, and what
/// arguments it accepts.
/// </summary>
/// <remarks>
/// This is the shape the UI renders and the shape a transport adapts into whatever its provider
/// expects. It deliberately carries presentation hints alongside the schema, because a tool call is
/// something the user sees, not only something the model calls.
/// </remarks>
public sealed record AiToolDefinition
{
    /// <summary>Creates a tool definition.</summary>
    /// <param name="name">The identifier the model uses to call the tool.</param>
    public AiToolDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The identifier the model uses to call the tool.</summary>
    public string Name { get; }

    /// <summary>A human-friendly label for the UI. Falls back to <see cref="Name"/>.</summary>
    public string? Title { get; init; }

    /// <summary>What the tool does. This is the text the model reads when deciding to call it.</summary>
    public string? Description { get; init; }

    /// <summary>A JSON Schema object describing the arguments.</summary>
    public JsonElement? ParametersSchema { get; init; }

    /// <summary>How much damage the tool can do.</summary>
    public ToolRisk Risk { get; init; } = ToolRisk.ReadOnly;

    /// <summary>
    /// Whether a human must approve each call before it executes. When <see langword="null"/> the
    /// registered approval policy decides, which by default means approving destructive tools only.
    /// </summary>
    public bool? RequiresApproval { get; init; }

    /// <summary>An icon name resolved by the host icon set, shown on the tool-call card.</summary>
    public string? Icon { get; init; }

    /// <summary>Application-defined values carried through to custom tool renderers.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    /// <summary>The label to show for this tool.</summary>
    public string DisplayName => Title ?? Name;
}
