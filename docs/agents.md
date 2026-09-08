# Agents

A spinner is not an acceptable answer to "what is it doing". An `AgentRun` makes multi-step activity
legible: a plan, the steps as they run, their timings, what each produced, and what the agent needs
from the user.

## Emitting a run

```csharp
var runId = AiId.NewRunId();

yield return new AgentRunStartedEvent(runId)
{
    AgentName = "Research agent",
    Goal = "Find whether the sources agree",
};

yield return new AgentPlanEvent(runId,
[
    new AgentPlanStep("search", "Search for primary sources"),
    new AgentPlanStep("read", "Read the three most relevant"),
    new AgentPlanStep("write", "Write the summary"),
]);

yield return new AgentStepStartedEvent(runId, "search");
yield return new AgentStepCompletedEvent(runId, "search") { Summary = "11 results, 3 primary" };
```

The run is attached to the message that produced it, so scrolling back to an old answer still shows
what the agent did to produce it.

## Plans are replaced, not patched

Agents routinely rewrite the tail of a plan as they learn. `AgentPlanEvent` replaces the whole plan;
steps that keep their identifier keep their state, summary, and timings:

```csharp
yield return new AgentPlanEvent(runId,
[
    new AgentPlanStep("search", "Search the web"),    // already completed; stays completed
    new AgentPlanStep("verify", "Check the outlier"), // new; starts pending
    new AgentPlanStep("write", "Write the summary"),
]);
```

## Asking the user

```csharp
yield return new AgentInputRequestedEvent(runId, "When should I take the downtime?")
{
    Suggestions = ["Tonight after 22:00", "Saturday morning"],
};
yield return new MessageCompletedEvent();
```

The run moves to `WaitingForInput` and the UI offers the suggestions as one-tap answers. Choosing one
submits it as a user message and the agent continues from the step it stopped at:

```csharp
await session.RespondToAgentAsync(runId, answer);
```

This is the difference between an agent that asks and one that guesses. The state is explicit, so
waiting can be made to look deliberate rather than stuck.

## Failure

```csharp
yield return new AgentStepCompletedEvent(runId, "test", AgentStepStatus.Failed)
{
    Error = new AiError("3 of 812 tests failed.") { IsRetryable = true },
};
yield return new AgentRunCompletedEvent(runId, AgentRunStatus.Failed);
```

The failing step keeps its own error and the completed steps keep their summaries. Nothing that
already succeeded is lost, which is what lets the answer explain what happened rather than merely
reporting that something did.

## Rendering

```razor
<AgentRunView Run="run" Session="_session" />
```

`AiChat` renders any run attached to a message automatically. The component draws a timeline: a
marker per step that pulses while running, fills green on success and red on failure, plus a
progress meter, elapsed time, and the pending question when there is one.

Each step subscribes to itself, so a step completing re-renders one row rather than the run.

## Statuses

```text
Idle -> Planning -> Running -> Completed
                            -> WaitingForInput -> Running
                            -> Failed
                            -> Cancelled
```

`AgentStatusBadge` renders these with a polite live region, so a screen reader user learns of a
transition without being interrupted.

## Usage across steps

`AgentRun.Usage` accumulates the usage of its steps, because `TokenUsage` is additive. A run that
called a model six times reports what the whole run cost, not what the last call did.
