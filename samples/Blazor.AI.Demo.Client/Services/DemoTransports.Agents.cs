namespace Blazor.AI.Demo.Client.Services;

public static class DemoAgentTransports
{
    /// <summary>
    /// A multi-step agent: it plans, works through the plan, and reports as it goes.
    /// </summary>
    /// <returns>The transport.</returns>
    public static IAiTransport Research() =>
        new ScriptedTransport((request, token) => DemoTransports.Stream(token, async yielder =>
        {
            var runId = AiId.NewRunId();

            await yielder(new MessageStartedEvent());
            await yielder(new AgentRunStartedEvent(runId)
            {
                AgentName = "Research agent",
                Goal = Script.LastUserText(request),
            });

            await Script.Pause(400, token);
            await yielder(new AgentPlanEvent(runId,
            [
                new AgentPlanStep("s1", "Search for primary sources"),
                new AgentPlanStep("s2", "Read the three most relevant"),
                new AgentPlanStep("s3", "Reconcile the disagreements"),
                new AgentPlanStep("s4", "Write the summary"),
            ]));

            var summaries = new[]
            {
                "11 results, 3 look primary",
                "Two agree on the mechanism, one does not",
                "The disagreement is about scope, not direction",
                "Summary written",
            };

            for (var i = 0; i < summaries.Length; i++)
            {
                var stepId = $"s{i + 1}";
                await Script.Pause(500, token);
                await yielder(new AgentStepStartedEvent(runId, stepId));
                await Script.Pause(700 + i * 200, token);
                await yielder(new AgentStepCompletedEvent(runId, stepId) { Summary = summaries[i] });
            }

            await yielder(new AgentRunCompletedEvent(runId));

            const string answer = """
                The three primary sources agree on direction and disagree on scope.

                Two of them measure the effect inside a single population and find it clearly. The
                third measures across populations and finds it weakly, which is what you would expect
                if the effect is real but the between-group variance is larger than the within-group
                variance.

                That is not a contradiction. It is a reason to be careful about which claim you make.
                """;

            await foreach (var evt in Script.Write(answer, 4, 11, token)) await yielder(evt);
            await yielder(Script.Usage(request, answer));
            await yielder(new MessageCompletedEvent());
        }));

    /// <summary>
    /// An agent that stops mid-run to ask the user something.
    /// </summary>
    /// <returns>The transport.</returns>
    public static IAiTransport AsksForInput() =>
        new ScriptedTransport((request, token) => DemoTransports.Stream(token, async yielder =>
        {
            const string runId = "run-migration";
            var answered = request.Messages.Count(m => m.Role.IsUser) > 1;

            await yielder(new MessageStartedEvent());

            if (!answered)
            {
                await yielder(new AgentRunStartedEvent(runId)
                {
                    AgentName = "Migration agent",
                    Goal = "Move the database to the new schema",
                });

                await yielder(new AgentPlanEvent(runId,
                [
                    new AgentPlanStep("s1", "Inspect the current schema"),
                    new AgentPlanStep("s2", "Choose a downtime window"),
                    new AgentPlanStep("s3", "Run the migration"),
                ]));

                await Script.Pause(400, token);
                await yielder(new AgentStepStartedEvent(runId, "s1"));
                await Script.Pause(900, token);
                await yielder(new AgentStepCompletedEvent(runId, "s1") { Summary = "14 tables, 2 need rewriting" });

                await Script.Pause(300, token);
                await yielder(new AgentStepStartedEvent(runId, "s2"));
                await yielder(new AgentInputRequestedEvent(runId,
                    "Two tables need a rewrite, which needs downtime. When should I take it?")
                {
                    Suggestions = ["Tonight after 22:00", "Saturday morning", "Ask me again later"],
                });

                await yielder(new MessageCompletedEvent(FinishReason.Stop));
                return;
            }

            await yielder(new AgentStepCompletedEvent(runId, "s2") { Summary = "Window agreed" });
            await Script.Pause(400, token);
            await yielder(new AgentStepStartedEvent(runId, "s3"));
            await Script.Pause(900, token);
            await yielder(new AgentStepCompletedEvent(runId, "s3") { Summary = "14 tables migrated" });
            await yielder(new AgentRunCompletedEvent(runId));

            const string answer = "Booked. I will run the migration in that window and report back.";
            await foreach (var evt in Script.Write(answer, 4, 12, token)) await yielder(evt);
            await yielder(new MessageCompletedEvent());
        }));

    /// <summary>
    /// An agent whose second step fails, so the failure has to be legible.
    /// </summary>
    /// <returns>The transport.</returns>
    public static IAiTransport Fails() =>
        new ScriptedTransport((request, token) => DemoTransports.Stream(token, async yielder =>
        {
            const string runId = "run-deploy";

            await yielder(new MessageStartedEvent());
            await yielder(new AgentRunStartedEvent(runId) { AgentName = "Deploy agent", Goal = "Ship the release" });
            await yielder(new AgentPlanEvent(runId,
            [
                new AgentPlanStep("s1", "Build"),
                new AgentPlanStep("s2", "Run the test suite"),
                new AgentPlanStep("s3", "Deploy"),
            ]));

            await Script.Pause(400, token);
            await yielder(new AgentStepStartedEvent(runId, "s1"));
            await Script.Pause(800, token);
            await yielder(new AgentStepCompletedEvent(runId, "s1") { Summary = "Build succeeded in 41s" });

            await Script.Pause(300, token);
            await yielder(new AgentStepStartedEvent(runId, "s2"));
            await Script.Pause(1100, token);
            await yielder(new AgentStepCompletedEvent(runId, "s2", AgentStepStatus.Failed)
            {
                Error = new AiError("3 of 812 tests failed.")
                {
                    Detail = "Checkout.AppliesDiscount\nCheckout.RoundsTax\nCheckout.HandlesRefund",
                    IsRetryable = true,
                },
            });

            await yielder(new AgentRunCompletedEvent(runId, AgentRunStatus.Failed));

            const string answer = """
                I stopped before deploying. Three checkout tests fail, and all three touch money.

                The failures look related: they share `Checkout.PriceCalculator`, which changed in the
                last commit on this branch.
                """;

            await foreach (var evt in Script.Write(answer, 4, 12, token)) await yielder(evt);
            await yielder(new MessageCompletedEvent());
        }));
}
