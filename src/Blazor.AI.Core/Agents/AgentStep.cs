namespace Blazor.AI;

/// <summary>
/// One step of an agent run.
/// </summary>
/// <remarks>
/// A step is the unit the user reasons about: "search the docs", "write the migration". It is not
/// the same as a tool call, though a step usually contains one or more.
/// </remarks>
public sealed class AgentStep
{
    private int _version;

    /// <summary>Creates a step.</summary>
    /// <param name="stepId">Stable identifier, preserved across plan revisions.</param>
    /// <param name="title">What the step does.</param>
    public AgentStep(string stepId, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        ArgumentNullException.ThrowIfNull(title);
        StepId = stepId;
        Title = title;
    }

    /// <summary>Stable identifier, preserved across plan revisions.</summary>
    public string StepId { get; }

    /// <summary>What the step does.</summary>
    public string Title { get; internal set; }

    /// <summary>Elaboration shown when the step is expanded.</summary>
    public string? Detail { get; set; }

    /// <summary>Where the step is in its lifecycle.</summary>
    public AgentStepStatus Status { get; private set; } = AgentStepStatus.Pending;

    /// <summary>A short account of what the step produced.</summary>
    public string? Summary { get; private set; }

    /// <summary>The failure, when the step failed.</summary>
    public AiError? Error { get; private set; }

    /// <summary>When the step started.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the step finished.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>How long the step took.</summary>
    public TimeSpan? Duration => StartedAt is { } s && CompletedAt is { } c ? c - s : null;

    /// <summary>Identifiers of the tool calls made during this step.</summary>
    public List<string> ToolCallIds { get; } = [];

    /// <summary>Increments on every change, for cheap render checks.</summary>
    public int Version => _version;

    /// <summary>Raised after the step changes.</summary>
    public event Action<AgentStep>? Changed;

    /// <summary>Marks the step running.</summary>
    public void Start()
    {
        Status = AgentStepStatus.Running;
        StartedAt ??= DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>Marks the step blocked on the user.</summary>
    public void Block()
    {
        Status = AgentStepStatus.Blocked;
        Touch();
    }

    /// <summary>Marks the step finished.</summary>
    /// <param name="status">How it finished.</param>
    /// <param name="summary">A short account of what it produced.</param>
    /// <param name="error">The failure, when it failed.</param>
    public void Complete(AgentStepStatus status = AgentStepStatus.Completed, string? summary = null, AiError? error = null)
    {
        Status = status;
        Summary = summary ?? Summary;
        Error = error ?? Error;
        CompletedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    private void Touch()
    {
        Interlocked.Increment(ref _version);
        Changed?.Invoke(this);
    }
}
