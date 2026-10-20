using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.Web;

namespace Blazor.AI.Components.Tests;

public sealed class AgentViewTests : AiTestBase
{
    private static int Subscribers(object target) =>
        ((Delegate?)target.GetType()
            .GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target))?.GetInvocationList().Length ?? 0;

    private static AgentRun RunningRun()
    {
        var run = new AgentRun("run_1", "Researcher");
        run.SetPlan([new AgentPlanStep("search", "Search"), new AgentPlanStep("write", "Write")]);
        run.GetOrAddStep("search").Start();
        run.SetStatus(AgentRunStatus.Running);
        return run;
    }

    [Fact]
    public async Task ARunViewStopsListeningToItsRunAndStepsWhenRemoved()
    {
        var run = RunningRun();
        var step = run.Steps[0];
        var before = Subscribers(run);
        var stepBefore = Subscribers(step);

        Render<AgentRunView>(p => p.Add(c => c.Run, run));
        Assert.True(Subscribers(run) > before);
        Assert.True(Subscribers(step) > stepBefore);

        await DisposeComponentsAsync();

        Assert.Equal(before, Subscribers(run));
        Assert.Equal(stepBefore, Subscribers(step));
    }

    [Fact]
    public void AQuestionWithoutSuggestionsStillTakesAFreeTextAnswer()
    {
        var run = RunningRun();
        run.RequestInput("Which region?");
        var answers = new List<string>();

        var view = Render<AgentRunView>(p => p
            .Add(c => c.Run, run)
            .Add(c => c.OnRespond, a => answers.Add(a)));

        Assert.Empty(view.FindAll(".bai-run__prompt .bai-btn--outline"));
        var input = view.Find(".bai-run__answer input");
        Assert.Equal(view.Find(".bai-approval__text").Id, input.GetAttribute("aria-labelledby"));

        input.Input("eu-west");
        view.Find(".bai-run__answer input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(["eu-west"], answers);
    }

    [Fact]
    public async Task AnAnswerGoesToTheCallbackOrTheSessionButNeverBoth()
    {
        var session = new ChatSession(new SilentTransport());
        var run = session.Conversation.GetOrAddRun("run_1");
        run.SetPlan([new AgentPlanStep("ask", "Ask")]);
        run.RequestInput("Proceed?", ["Yes", "No"]);
        var answers = new List<string>();

        var view = Render<AgentRunView>(p => p
            .Add(c => c.Run, run)
            .Add(c => c.Session, session)
            .Add(c => c.OnRespond, a => answers.Add(a)));

        await view.Find(".bai-run__prompt .bai-btn--outline").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Yes"], answers);
        Assert.Equal(AgentRunStatus.WaitingForInput, run.Status);
        Assert.Empty(session.Conversation.Messages);
    }

    [Fact]
    public async Task WithoutACallbackTheSessionAnswersTheRun()
    {
        var session = new ChatSession(new SilentTransport());
        var run = session.Conversation.GetOrAddRun("run_1");
        run.SetPlan([new AgentPlanStep("ask", "Ask")]);
        run.RequestInput("Proceed?", ["Yes"]);

        var view = Render<AgentRunView>(p => p.Add(c => c.Run, run).Add(c => c.Session, session));
        await view.Find(".bai-run__prompt .bai-btn--outline").ClickAsync(new MouseEventArgs());

        view.WaitForAssertion(() => Assert.Contains(session.Conversation.Messages, m => m.GetText() == "Yes"));
        Assert.Equal(1, session.Conversation.Messages.Count(m => m.Role == AiRole.User));
    }

    [Fact]
    public void AStepListsTheToolCallsItMadeByName()
    {
        var conversation = new Conversation();
        var message = conversation.AddMessage(new AiMessage(AiRole.Assistant));
        var call = new ToolCallPart("call_1", "search_docs");
        message.AddPart(call);
        var step = new AgentStep("search", "Search");
        step.AddToolCall("call_1");
        step.AddToolCall("call_unknown");

        var view = Render<AgentStepView>(p => p.Add(c => c.Step, step).Add(c => c.Conversation, conversation));

        var chips = view.FindAll(".bai-step__tools .bai-chip");
        Assert.Equal(2, chips.Count);
        Assert.Contains("search_docs", chips[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("call_unknown", chips[1].TextContent, StringComparison.Ordinal);

        call.CompleteArguments(System.Text.Json.JsonDocument.Parse("{}").RootElement);
        call.SetResult(ToolOutcome.Failure("boom"));
        step.Complete(AgentStepStatus.Failed);

        view.WaitForAssertion(() => Assert.Contains("bai-chip--danger", view.FindAll(".bai-step__tools .bai-chip")[0].ClassName, StringComparison.Ordinal));
    }

    [Fact]
    public void TheElapsedTimeTicksWhileTheRunIsActiveAndStopsWhenItEnds()
    {
        var run = RunningRun();
        var view = Render<AgentRunView>(p => p
            .Add(c => c.Run, run)
            .Add(c => c.ClockInterval, TimeSpan.FromMilliseconds(20)));

        var first = view.Find(".bai-run__elapsed").TextContent;
        view.WaitForAssertion(() => Assert.NotEqual(first, view.Find(".bai-run__elapsed").TextContent), TimeSpan.FromSeconds(3));

        run.SetStatus(AgentRunStatus.Completed);
        view.WaitForAssertion(() => Assert.Contains("Done", view.Markup, StringComparison.Ordinal));
        var frozen = view.Find(".bai-run__elapsed").TextContent;
        Thread.Sleep(80);
        view.Render();
        Assert.Equal(frozen, view.Find(".bai-run__elapsed").TextContent);
    }

    private sealed class SilentTransport : IAiTransport
    {
        public async IAsyncEnumerable<AiEvent> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new MessageCompletedEvent();
        }
    }
}
