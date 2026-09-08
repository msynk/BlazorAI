namespace Blazor.AI;

/// <summary>
/// An agent run has begun.
/// </summary>
public sealed record AgentRunStartedEvent : AiEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="runId">Identifier for the run.</param>
    public AgentRunStartedEvent(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        RunId = runId;
    }

    /// <summary>Identifier for the run.</summary>
    public string RunId { get; }

    /// <summary>The name of the agent, shown in the run header.</summary>
    public string? AgentName { get; init; }

    /// <summary>The goal the run is pursuing, shown under the name.</summary>
    public string? Goal { get; init; }

    /// <inheritdoc />
    public override string Type => "agent.run.started";
}

/// <summary>
/// The agent published or revised its plan.
/// </summary>
/// <remarks>
/// Replaces the whole plan rather than patching it, because agents commonly rewrite the tail of a
/// plan as they learn. Steps that keep their identifier keep their state.
/// </remarks>
public sealed record AgentPlanEvent : AiEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="runId">The run being planned.</param>
    /// <param name="steps">The planned steps, in execution order.</param>
    public AgentPlanEvent(string runId, IReadOnlyList<AgentPlanStep> steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(steps);
        RunId = runId;
        Steps = steps;
    }

    /// <summary>The run being planned.</summary>
    public string RunId { get; }

    /// <summary>The planned steps, in execution order.</summary>
    public IReadOnlyList<AgentPlanStep> Steps { get; }

    /// <inheritdoc />
    public override string Type => "agent.plan";
}

/// <summary>
/// One entry in an agent plan.
/// </summary>
/// <param name="StepId">Stable identifier, preserved across plan revisions.</param>
/// <param name="Title">What the step does.</param>
/// <param name="Detail">Optional elaboration shown when the step is expanded.</param>
public readonly record struct AgentPlanStep(string StepId, string Title, string? Detail = null);

/// <summary>
/// A step started executing.
/// </summary>
public sealed record AgentStepStartedEvent : AiEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="runId">The run the step belongs to.</param>
    /// <param name="stepId">The step being started.</param>
    /// <param name="title">What the step does. Optional when the step came from a plan.</param>
    public AgentStepStartedEvent(string runId, string stepId, string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        RunId = runId;
        StepId = stepId;
        Title = title;
    }

    /// <summary>The run the step belongs to.</summary>
    public string RunId { get; }

    /// <summary>The step being started.</summary>
    public string StepId { get; }

    /// <summary>What the step does.</summary>
    public string? Title { get; }

    /// <inheritdoc />
    public override string Type => "agent.step.started";
}

/// <summary>
/// A step finished.
/// </summary>
public sealed record AgentStepCompletedEvent : AiEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="runId">The run the step belongs to.</param>
    /// <param name="stepId">The step that finished.</param>
    /// <param name="status">How it finished. Defaults to completed.</param>
    public AgentStepCompletedEvent(string runId, string stepId, AgentStepStatus status = AgentStepStatus.Completed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        RunId = runId;
        StepId = stepId;
        Status = status;
    }

    /// <summary>The run the step belongs to.</summary>
    public string RunId { get; }

    /// <summary>The step that finished.</summary>
    public string StepId { get; }

    /// <summary>How it finished.</summary>
    public AgentStepStatus Status { get; }

    /// <summary>A short account of what the step produced.</summary>
    public string? Summary { get; init; }

    /// <summary>The failure, when the step failed.</summary>
    public AiError? Error { get; init; }

    /// <inheritdoc />
    public override string Type => "agent.step.completed";
}

/// <summary>
/// The run ended.
/// </summary>
public sealed record AgentRunCompletedEvent : AiEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="runId">The run that ended.</param>
    /// <param name="status">How it ended. Defaults to completed.</param>
    public AgentRunCompletedEvent(string runId, AgentRunStatus status = AgentRunStatus.Completed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        RunId = runId;
        Status = status;
    }

    /// <summary>The run that ended.</summary>
    public string RunId { get; }

    /// <summary>How it ended.</summary>
    public AgentRunStatus Status { get; }

    /// <inheritdoc />
    public override string Type => "agent.run.completed";
}

/// <summary>
/// The run is blocked until the user answers.
/// </summary>
public sealed record AgentInputRequestedEvent : AiEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="runId">The blocked run.</param>
    /// <param name="prompt">What the agent needs from the user.</param>
    public AgentInputRequestedEvent(string runId, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(prompt);
        RunId = runId;
        Prompt = prompt;
    }

    /// <summary>The blocked run.</summary>
    public string RunId { get; }

    /// <summary>What the agent needs from the user.</summary>
    public string Prompt { get; }

    /// <summary>Suggested answers, rendered as one-tap choices.</summary>
    public IReadOnlyList<string>? Suggestions { get; init; }

    /// <inheritdoc />
    public override string Type => "agent.input_requested";
}
