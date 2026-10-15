using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Blazor.AI.Components.Tests;

public sealed class TranscriptAndInteropTests : AiTestBase
{
    /// <summary>Renders which component instance is showing which message.</summary>
    private sealed class InstanceProbe : ComponentBase
    {
        private static int s_next;
        private readonly int _instance = Interlocked.Increment(ref s_next);

        [Parameter] public AiMessage Message { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "probe");
            builder.AddAttribute(2, "data-message", Message.MessageId);
            builder.AddAttribute(3, "data-instance", _instance);
            builder.CloseElement();
        }
    }

    /// <summary>A runtime whose module import completes only when the test says so.</summary>
    private sealed class PendingImportRuntime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            new((Task<TValue>)(object)Import.Task);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class FakeModule : IJSObjectReference
    {
        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void RemovingAMessageDoesNotRecreateTemplatedMessagesAfterIt()
    {
        var conversation = new Conversation();
        var first = conversation.AddMessage(AiRole.User, "one");
        var second = conversation.AddMessage(AiRole.Assistant, "two");
        var third = conversation.AddMessage(AiRole.User, "three");

        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var list = Render<MessageList>(p => p
            .Add(c => c.Conversation, conversation)
            .Add(c => c.MessageTemplate, message => builder =>
            {
                builder.OpenComponent<InstanceProbe>(0);
                builder.AddComponentParameter(1, nameof(InstanceProbe.Message), message);
                builder.CloseComponent();
            }));

        string InstanceOf(AiMessage message) =>
            list.Find($".probe[data-message='{message.MessageId}']").GetAttribute("data-instance")!;

        var secondInstance = InstanceOf(second);
        var thirdInstance = InstanceOf(third);

        conversation.RemoveMessage(first.MessageId);

        list.WaitForAssertion(() => Assert.Equal(2, list.FindAll(".probe").Count));
        Assert.Equal(secondInstance, InstanceOf(second));
        Assert.Equal(thirdInstance, InstanceOf(third));
    }

    [Fact]
    public void ATemplatedMessageAddsNoWrapperElement()
    {
        var conversation = new Conversation();
        conversation.AddMessage(AiRole.User, "one");

        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var list = Render<MessageList>(p => p
            .Add(c => c.Conversation, conversation)
            .Add(c => c.MessageTemplate, message => builder =>
            {
                builder.OpenElement(0, "article");
                builder.AddContent(1, message.GetText());
                builder.CloseElement();
            }));

        Assert.Equal("ARTICLE", list.Find(".bai-chat__content").Children.Single().TagName);
    }

    [Fact]
    public async Task DisposingWhileTheModuleIsLoadingDoesNotThrow()
    {
        var runtime = new PendingImportRuntime();
        var interop = new AiInterop(runtime);

        var loading = interop.GetModuleAsync().AsTask();
        var calling = interop.InvokeVoidAsync("focusElement").AsTask();
        interop.Dispose();
        runtime.Import.SetResult(new FakeModule());

        Assert.Null(await loading);
        await calling;
        Assert.Null(await interop.GetModuleAsync());
    }

    [Fact]
    public async Task DisposingAsynchronouslyReleasesAModuleThatArrivesLate()
    {
        var runtime = new PendingImportRuntime();
        var interop = new AiInterop(runtime);
        var module = new FakeModule();

        var loading = interop.GetModuleAsync().AsTask();
        var disposing = interop.DisposeAsync().AsTask();
        runtime.Import.SetResult(module);

        await disposing;
        Assert.True(module.Disposed);
        Assert.Null(await loading);
    }

    [Fact]
    public void AnAgentRunShowsItsUsageWhenUsageDisplayIsOn()
    {
        Services.AddSingleton(AiUiOptions.Default with { ShowUsage = true });
        var run = new AgentRun(agentName: "Researcher");

        var view = Render<AgentRunView>(p => p.Add(c => c.Run, run));
        Assert.Empty(view.FindAll(".bai-panel__header .bai-usage"));

        run.AddUsage(new TokenUsage { InputTokens = 1200, OutputTokens = 300 });

        view.WaitForAssertion(() =>
            Assert.Contains("1.5k", view.Find(".bai-panel__header .bai-usage").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void AnAgentRunHidesItsUsageWhenUsageDisplayIsOff()
    {
        var run = new AgentRun(agentName: "Researcher");
        run.AddUsage(new TokenUsage { InputTokens = 1200, OutputTokens = 300 });

        var view = Render<AgentRunView>(p => p.Add(c => c.Run, run));

        Assert.Empty(view.FindAll(".bai-usage"));
    }
}
