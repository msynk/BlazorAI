using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.AI.Components.Tests;

public sealed class ComposerKeyboardTests : AiTestBase
{
    private const string ModulePath = "./_content/Blazor.AI.Components/blazor-ai.js";

    private static KeyboardEventArgs Key(string key, bool shift = false, bool ctrl = false, bool meta = false, bool composing = false) =>
        new() { Key = key, ShiftKey = shift, CtrlKey = ctrl, MetaKey = meta, IsComposing = composing };

    [Fact]
    public void EnterDuringImeCompositionDoesNotSubmit()
    {
        var submissions = new List<PromptInput.PromptSubmission>();
        var composer = Render<PromptInput>(p => p
            .Add(c => c.Value, "こんにちは")
            .Add(c => c.OnSubmit, s => submissions.Add(s)));

        composer.Find("textarea").KeyDown(Key("Enter", composing: true));
        composer.Find("textarea").KeyDown(Key("Process"));
        Assert.Empty(submissions);

        composer.Find("textarea").KeyDown(Key("Enter"));
        Assert.Equal("こんにちは", Assert.Single(submissions).Text);
    }

    [Fact]
    public void ShiftEnterInsertsANewlineRatherThanSubmitting()
    {
        var submissions = new List<PromptInput.PromptSubmission>();
        var composer = Render<PromptInput>(p => p
            .Add(c => c.Value, "hello")
            .Add(c => c.OnSubmit, s => submissions.Add(s)));

        composer.Find("textarea").KeyDown(Key("Enter", shift: true));

        Assert.Empty(submissions);
    }

    [Fact]
    public void EnterDuringCompositionDoesNotChooseAMenuItem()
    {
        var value = string.Empty;
        var composer = Render<PromptInput>(p => p
            .Add(c => c.Commands, [new SlashCommand("summarize") { Template = "Summarize: " }])
            .Add(c => c.ValueChanged, v => value = v));

        composer.Find("textarea").Input("/");
        composer.Find("textarea").KeyDown(Key("Enter", composing: true));

        Assert.Equal("/", value);
        Assert.NotEmpty(composer.FindAll("[role=option]"));
    }

    [Fact]
    public void TabAcceptsTheActiveMenuItem()
    {
        var value = string.Empty;
        var composer = Render<PromptInput>(p => p
            .Add(c => c.Commands, [new SlashCommand("summarize") { Template = "Summarize: " }, new SlashCommand("translate")])
            .Add(c => c.ValueChanged, v => value = v));

        composer.Find("textarea").Input("/");
        composer.Find("textarea").KeyDown(Key("ArrowDown"));
        composer.Find("textarea").KeyDown(Key("Tab"));

        Assert.Equal("/translate ", value);
        Assert.Empty(composer.FindAll("[role=option]"));
    }

    [Fact]
    public void EscapeClosesTheMenuFirstAndStopsGenerationSecond()
    {
        var stops = 0;
        var composer = Render<PromptInput>(p => p
            .Add(c => c.Generating, true)
            .Add(c => c.Commands, [new SlashCommand("summarize")])
            .Add(c => c.OnStop, () => stops++));

        composer.Find("textarea").Input("/");
        Assert.NotEmpty(composer.FindAll("[role=option]"));

        composer.Find("textarea").KeyDown(Key("Escape"));
        Assert.Empty(composer.FindAll("[role=option]"));
        Assert.Equal(0, stops);

        composer.Find("textarea").KeyDown(Key("Escape"));
        Assert.Equal(1, stops);
    }

    [Fact]
    public void TheTextareaIsAComboboxWhoseStateFollowsTheMenu()
    {
        var composer = Render<PromptInput>(p => p
            .Add(c => c.Commands, [new SlashCommand("summarize")])
            .Add(c => c.Mentions, [new MentionItem("u1", "John")]));

        var textarea = composer.Find("textarea");
        Assert.Equal("combobox", textarea.GetAttribute("role"));
        Assert.Equal("listbox", textarea.GetAttribute("aria-haspopup"));
        Assert.Equal("list", textarea.GetAttribute("aria-autocomplete"));
        Assert.Equal("false", textarea.GetAttribute("aria-expanded"));
        Assert.False(textarea.HasAttribute("aria-controls"));

        composer.Find("textarea").Input("/");
        textarea = composer.Find("textarea");
        var listbox = composer.Find("[role=listbox]");
        Assert.Equal("true", textarea.GetAttribute("aria-expanded"));
        Assert.Equal(listbox.Id, textarea.GetAttribute("aria-controls"));
        Assert.Equal("Commands", listbox.GetAttribute("aria-label"));
        Assert.Equal("true", composer.Find("[role=option]").GetAttribute("aria-selected"));

        composer.Find("textarea").Input("@Jo");
        Assert.Equal("Mentions", composer.Find("[role=listbox]").GetAttribute("aria-label"));
    }

    [Fact]
    public void TheSendButtonBecomesTheStopButtonWithoutLeaving()
    {
        var composer = Render<PromptInput>(p => p.Add(c => c.Value, "hi"));

        Assert.Equal("Send", composer.Find(".bai-composer__send").GetAttribute("aria-label"));
        Assert.False(composer.Find(".bai-composer__send").HasAttribute("disabled"));

        composer.Render(p => p.Add(c => c.Generating, true));

        var button = Assert.Single(composer.FindAll(".bai-composer__send"));
        Assert.Equal("Stop", button.GetAttribute("aria-label"));
        Assert.False(button.HasAttribute("disabled"));
    }

    [Fact]
    public void TheCharacterCounterIsAnnouncedOnlyNearTheLimit()
    {
        var composer = Render<PromptInput>(p => p.Add(c => c.MaxLength, 20).Add(c => c.Value, "hi"));

        Assert.Equal("20", composer.Find("textarea").GetAttribute("maxlength"));
        Assert.Equal("2 / 20", composer.Find(".bai-composer__counter").TextContent.Trim());
        Assert.Equal(string.Empty, composer.Find("[role=status]").TextContent.Trim());

        composer.Find("textarea").Input(new string('x', 18));

        Assert.Equal("18 / 20", composer.Find(".bai-composer__counter").TextContent.Trim());
        Assert.Contains("bai-composer__counter--near", composer.Find(".bai-composer__counter").ClassName, StringComparison.Ordinal);
        Assert.Equal("2 characters remaining", composer.Find("[role=status]").TextContent.Trim());
    }

    [Fact]
    public void TheSubmitKeyGuardFollowsTheOption()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var theme = Render<AiTheme>(p => p
            .Add(c => c.UiOptions, AiUiOptions.Default with { SubmitOnEnter = true })
            .AddChildContent<PromptInput>());

        theme.WaitForAssertion(() => Assert.Equal(true, module.VerifyInvoke("registerSubmitKey").Arguments[1]));

        theme.Render(p => p.Add(c => c.UiOptions, AiUiOptions.Default with { SubmitOnEnter = false }));

        theme.WaitForAssertion(() =>
        {
            var calls = module.VerifyInvoke("registerSubmitKey", calledTimes: 2);
            Assert.Equal(false, calls[1].Arguments[1]);
        });
    }

    [Fact]
    public void ADraftIsRestoredSavedAndCleared()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<string?>("readDraft", _ => true).SetResult("saved text");
        var value = string.Empty;

        var composer = Render<PromptInput>(p => p
            .Add(c => c.DraftKey, "conversation-1")
            .Add(c => c.ValueChanged, v => value = v));

        composer.WaitForAssertion(() => Assert.Equal("saved text", value));
        Assert.Equal("conversation-1", module.VerifyInvoke("readDraft").Arguments[0]);

        composer.Find("textarea").Input("saved text and more");
        var saved = module.VerifyInvoke("writeDraft");
        Assert.Equal("conversation-1", saved.Arguments[0]);
        Assert.Equal("saved text and more", saved.Arguments[1]);

        composer.Find("textarea").KeyDown(Key("Enter"));
        Assert.Equal("conversation-1", module.VerifyInvoke("clearDraft").Arguments[0]);
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public async Task DisposingTheComposerReleasesItsBrowserRegistrations()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var composer = Render<PromptInput>();
        composer.WaitForAssertion(() => module.VerifyInvoke("registerDrop"));

        await DisposeComponentsAsync();

        module.VerifyInvoke("unregisterPaste");
        module.VerifyInvoke("unregisterDrop");
        module.VerifyInvoke("unregisterSubmitKey");
    }
}

public sealed class ComposerSubmitKeyOptionTests : AiTestBase
{
    public ComposerSubmitKeyOptionTests() =>
        Services.AddSingleton(AiUiOptions.Default with { SubmitOnEnter = false });

    [Fact]
    public void CtrlOrCmdEnterSubmitsWhenEnterAloneDoesNot()
    {
        var submissions = new List<PromptInput.PromptSubmission>();
        var composer = Render<PromptInput>(p => p
            .Add(c => c.Value, "hello")
            .Add(c => c.OnSubmit, s => submissions.Add(s)));

        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Empty(submissions);

        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        Assert.Single(submissions);

        composer.Render(p => p.Add(c => c.Value, "again"));
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter", MetaKey = true });
        Assert.Equal(2, submissions.Count);
    }
}
