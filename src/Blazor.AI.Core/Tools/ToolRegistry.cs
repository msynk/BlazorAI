using System.Collections.Concurrent;

namespace Blazor.AI;

/// <summary>
/// The default <see cref="IToolRegistry"/>: a thread-safe, case-insensitive name-to-tool map.
/// </summary>
/// <remarks>
/// Registered as a singleton by <c>AddBlazorAI</c>. Tools can be added at runtime, which is what
/// lets an application enable a capability only for certain users or conversations.
/// </remarks>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, IAiTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates an empty registry.</summary>
    public ToolRegistry() { }

    /// <summary>Creates a registry populated from a sequence.</summary>
    /// <param name="tools">The tools to register.</param>
    public ToolRegistry(IEnumerable<IAiTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var tool in tools) Register(tool);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IAiTool> Tools => _tools.Values.ToArray();

    /// <inheritdoc />
    public IAiTool? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    /// <inheritdoc />
    public void Register(IAiTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Definition.Name] = tool;
    }

    /// <inheritdoc />
    public bool Unregister(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _tools.TryRemove(name, out _);
    }

    /// <summary>The definitions of every registered tool, which is what a request carries.</summary>
    /// <returns>The definitions.</returns>
    public IReadOnlyList<AiToolDefinition> GetDefinitions() =>
        _tools.Values.Select(t => t.Definition).ToArray();
}

/// <summary>
/// The default approval policy: approve destructive tools, and honour an explicit
/// <see cref="AiToolDefinition.RequiresApproval"/> either way.
/// </summary>
/// <remarks>
/// Replace it by registering your own <see cref="IToolApprovalPolicy"/> when the decision depends on
/// the arguments or the current user rather than only on the tool.
/// </remarks>
public sealed class DefaultToolApprovalPolicy : IToolApprovalPolicy
{
    private readonly HashSet<string> _alwaysApproved = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask<bool> RequiresApprovalAsync(
        AiToolDefinition definition,
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Tools in one round are checked concurrently while the UI may be recording an "always".
        bool alwaysApproved;
        lock (_alwaysApproved) alwaysApproved = _alwaysApproved.Contains(definition.Name);

        if (alwaysApproved) return ValueTask.FromResult(false);
        if (definition.RequiresApproval is { } explicitChoice) return ValueTask.FromResult(explicitChoice);
        return ValueTask.FromResult(definition.Risk is ToolRisk.Destructive);
    }

    /// <summary>
    /// Stops asking for a tool the user approved with "always".
    /// </summary>
    /// <param name="toolName">The tool to stop asking about.</param>
    public void RememberAlwaysApproved(string toolName)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        lock (_alwaysApproved) _alwaysApproved.Add(toolName);
    }
}
