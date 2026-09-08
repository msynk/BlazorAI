using System.Text.Json;

namespace Blazor.AI.Components.Tests;

public sealed class ToolCallRenderingTests : AiTestBase
{
    [Fact]
    public void ARunningCallReadsAsRunningAndAFinishedOneAsUsed()
    {
        var call = new ToolCallPart("c1", "get_weather");
        var component = Render<ToolCallView>(p => p.Add(c => c.Call, call));
        Assert.Contains("Calling get_weather", component.Markup, StringComparison.Ordinal);

        call.CompleteArguments(JsonDocument.Parse("""{"city":"Oslo"}""").RootElement);
        call.SetResult(ToolOutcome.FromText("12 degrees"));

        component.WaitForAssertion(
            () => Assert.Contains("Used get_weather", component.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AFailedCallIsExpandedRatherThanHiddenBehindAChevron()
    {
        var call = new ToolCallPart("c1", "deploy");
        call.CompleteArguments(JsonDocument.Parse("{}").RootElement);

        var component = Render<ToolCallView>(p => p.Add(c => c.Call, call));
        Assert.DoesNotContain("bai-panel__body", component.Markup, StringComparison.Ordinal);

        call.SetResult(ToolOutcome.Failure("permission denied"));

        component.WaitForAssertion(
            () => Assert.Contains("permission denied", component.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void PartialArgumentsAreShownWhileTheyStream()
    {
        var call = new ToolCallPart("c1", "search");
        call.AppendArguments("""{"query":"blazor str""");

        var component = Render<ToolCallView>(p => p
            .Add(c => c.Call, call)
            .Add(c => c.CascadedOptions, AiUiOptions.Default with { CollapseToolCalls = false }));

        Assert.Contains("blazor str", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnApprovalRequestOffersThreeDistinctChoices()
    {
        var call = new ToolCallPart("c1", "delete_everything");
        call.CompleteArguments(JsonDocument.Parse("{}").RootElement);
        call.SetStatus(ToolCallStatus.AwaitingApproval);

        var markup = Render<ToolCallView>(p => p.Add(c => c.Call, call)).Markup;

        Assert.Contains("Always allow", markup, StringComparison.Ordinal);
        Assert.Contains("Decline", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovingRecordsTheDecisionOnTheCall()
    {
        var call = new ToolCallPart("c1", "delete_everything");
        call.CompleteArguments(JsonDocument.Parse("{}").RootElement);
        call.SetStatus(ToolCallStatus.AwaitingApproval);

        var component = Render<ToolApproval>(p => p.Add(c => c.Call, call));
        component.FindAll("button").Last().Click();

        Assert.Equal(ApprovalDecision.Approved, call.Approval);
    }

    [Fact]
    public void DecliningMarksTheCallRejected()
    {
        var call = new ToolCallPart("c1", "delete_everything");
        call.CompleteArguments(JsonDocument.Parse("{}").RootElement);
        call.SetStatus(ToolCallStatus.AwaitingApproval);

        var component = Render<ToolApproval>(p => p.Add(c => c.Call, call));
        component.FindAll("button").First().Click();

        Assert.Equal(ToolCallStatus.Rejected, call.Status);
    }

    [Fact]
    public void ARegisteredToolRendererReplacesTheDefaultCard()
    {
        Renderers.RegisterTool("get_weather", context => builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "weather-card");
            builder.AddContent(2, context.Call.ToolName);
            builder.CloseElement();
        });

        var markup = Render<ToolCallView>(p => p.Add(c => c.Call, new ToolCallPart("c1", "get_weather"))).Markup;

        Assert.Contains("weather-card", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("bai-panel__header", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressIsExposedToAssistiveTechnology()
    {
        var call = new ToolCallPart("c1", "index");
        call.CompleteArguments(JsonDocument.Parse("{}").RootElement);
        call.SetStatus(ToolCallStatus.Running);
        call.SetProgress(new ToolProgress(0.4, "indexing files"));

        var markup = Render<ToolCallView>(p => p.Add(c => c.Call, call)).Markup;

        Assert.Contains("role=\"progressbar\"", markup, StringComparison.Ordinal);
        Assert.Contains("aria-valuenow=\"40\"", markup, StringComparison.Ordinal);
    }
}
