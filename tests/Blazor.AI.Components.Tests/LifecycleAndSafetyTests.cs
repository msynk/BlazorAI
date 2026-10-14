using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.AI.Components.Tests;

public sealed class LifecycleAndSafetyTests : AiTestBase
{
    private static Delegate? Subscribers(MessagePart part) =>
        (Delegate?)typeof(MessagePart)
            .GetField(nameof(MessagePart.Changed), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(part);

    [Fact]
    public async Task ADerivedComponentThatImplementsIDisposableIsDisposed()
    {
        var component = Render<DisposableProbe>();
        var probe = component.Instance;

        await DisposeComponentsAsync();

        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task AToolCardStopsListeningToItsCallWhenRemoved()
    {
        var call = new ToolCallPart("call_1", "get_weather");
        Render<ToolCallView>(p => p.Add(c => c.Call, call));
        Assert.NotNull(Subscribers(call));

        await DisposeComponentsAsync();

        Assert.Null(Subscribers(call));
    }

    [Fact]
    public void RegisteredOptionsApplyWithoutAnAiTheme()
    {
        Services.AddSingleton(AiUiOptions.Default with { ShowCodeCopy = false });

        var component = Render<CodeBlockView>(p => p.Add(c => c.Code, "x"));

        Assert.Empty(component.FindAll("button"));
    }

    [Fact]
    public void ConfiguredOptionsSurviveARendererRegistrationMadeFirst()
    {
        var services = new ServiceCollection();
        services.ConfigureAiRenderers(_ => { });
        services.AddBlazorAIComponents(options => options with { ShowUsage = true });
        services.AddBlazorAIComponents(options => options with { ShowModelName = true });

        var options = services.BuildServiceProvider().GetRequiredService<AiUiOptions>();

        Assert.True(options.ShowUsage);
        Assert.True(options.ShowModelName);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("  JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    public void ASourceWithAScriptUrlIsNotRenderedAsALink(string url)
    {
        var source = new CitationSource("s1", "Evil") { Url = url };

        var component = Render<SourceCard>(p => p.Add(c => c.Source, source));

        Assert.Empty(component.FindAll("a"));
        Assert.NotNull(component.Find("button.bai-source"));
    }

    [Fact]
    public void ASourceWithAWebUrlIsRenderedAsALink()
    {
        var source = new CitationSource("s1", "Docs") { Url = "https://learn.microsoft.com/aspnet" };

        var component = Render<SourceCard>(p => p.Add(c => c.Source, source));

        Assert.Equal("https://learn.microsoft.com/aspnet", component.Find("a.bai-source").GetAttribute("href"));
    }

    [Fact]
    public void TogglingOneArrayItemInAJsonTreeLeavesItsSiblingsAlone()
    {
        using var document = JsonDocument.Parse("""[{"a":1},{"a":2},{"a":3}]""");
        var component = Render<JsonView>(p => p
            .Add(c => c.Value, document.RootElement.Clone())
            .Add(c => c.DefaultExpandDepth, 1));

        // The root is open; each item starts collapsed.
        Assert.Equal(["true", "false", "false", "false"], component.FindAll("button.bai-json__toggle").Select(b => b.GetAttribute("aria-expanded")));

        component.FindAll("button.bai-json__toggle")[1].Click();

        Assert.Equal(["true", "true", "false", "false"], component.FindAll("button.bai-json__toggle").Select(b => b.GetAttribute("aria-expanded")));
    }

    [Fact]
    public void TheContextMeterFollowsAChangeOfModel()
    {
        var small = new AiModel("small", "Small") { ContextWindow = 1_000 };
        var large = new AiModel("large", "Large") { ContextWindow = 4_000 };

        var component = Render<ContextMeter>(p => p.Add(c => c.Used, 500).Add(c => c.Model, small));
        Assert.Equal("50", component.Find("[role=progressbar]").GetAttribute("aria-valuenow"));

        component.Render(p => p.Add(c => c.Model, large));

        Assert.Equal("12", component.Find("[role=progressbar]").GetAttribute("aria-valuenow"));
    }

    [Fact]
    public void NumberFieldsValidateIndependentlyOfTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            IReadOnlyDictionary<string, string?>? submitted = null;
            var definition = new AiFormDefinition
            {
                Fields = [new AiFormField("ratio") { Type = AiFieldType.Number, Maximum = 2 }],
            };
            var component = Render<AiForm>(p => p
                .Add(c => c.Definition, definition)
                .Add(c => c.OnSubmit, values => submitted = values));

            component.Find("input").Input("1.5");
            component.Find("button.bai-btn--primary").Click();

            Assert.Empty(component.FindAll(".bai-field__error"));
            Assert.Equal("1.5", submitted!["ratio"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task MessagesAddedFromABackgroundThreadAreRendered()
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var conversation = new Conversation();
        conversation.AddMessage(AiRole.User, "first");
        var component = Render<MessageList>(p => p.Add(c => c.Conversation, conversation));

        await Task.Run(() =>
        {
            for (var i = 0; i < 20; i++) conversation.AddMessage(AiRole.User, $"message {i}");
        });

        component.WaitForAssertion(() => Assert.Equal(21, component.FindAll("article.bai-message").Count));
    }

    [Fact]
    public void RemovingAMessageKeepsTheComponentsOfTheOthers()
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var conversation = new Conversation();
        var first = conversation.AddMessage(AiRole.User, "one");
        var second = conversation.AddMessage(AiRole.User, "two");
        var third = conversation.AddMessage(AiRole.User, "three");
        var component = Render<MessageList>(p => p.Add(c => c.Conversation, conversation));
        var before = component.FindComponents<ChatMessage>().Single(c => c.Instance.Message == third).Instance;

        conversation.RemoveMessage(second.MessageId);

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("article.bai-message").Count));
        var after = component.FindComponents<ChatMessage>().Single(c => c.Instance.Message == third).Instance;
        Assert.Same(before, after);
        Assert.Contains(component.FindComponents<ChatMessage>(), c => c.Instance.Message == first);
    }

    [Fact]
    public void TheSlashMenuIsExposedToAssistiveTechnologyAndToTheKeyGuard()
    {
        var component = Render<PromptInput>(p => p
            .Add(c => c.Commands, [new SlashCommand("summarize"), new SlashCommand("translate")]));

        component.Find("textarea").Input("/");

        var textarea = component.Find("textarea");
        var listbox = component.Find("[role=listbox]");
        Assert.Equal(listbox.Id, textarea.GetAttribute("aria-controls"));
        Assert.Equal(component.FindAll("[role=option]")[0].Id, textarea.GetAttribute("aria-activedescendant"));
        Assert.True(textarea.HasAttribute("data-bai-menu-open"));

        textarea.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal(component.FindAll("[role=option]")[1].Id, component.Find("textarea").GetAttribute("aria-activedescendant"));

        component.Find("textarea").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.False(component.Find("textarea").HasAttribute("data-bai-menu-open"));
    }

    [Fact]
    public void TheAttachButtonIsReachableFromTheKeyboard()
    {
        var component = Render<PromptInput>();

        var input = component.Find("input[type=file]");

        Assert.DoesNotContain("display:none", input.GetAttribute("style") ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("bai-sr-only", input.ClassName ?? string.Empty, StringComparison.Ordinal);
    }

    private sealed class DisposableProbe : AiComponentBase, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
