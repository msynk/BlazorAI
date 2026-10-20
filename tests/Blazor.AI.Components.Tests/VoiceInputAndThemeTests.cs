using System.Text.RegularExpressions;

namespace Blazor.AI.Components.Tests;

public sealed class VoiceInputTests : AiTestBase
{
    private const string ModulePath = "./_content/Blazor.AI.Components/blazor-ai.js";

    [Fact]
    public void DictationStateIsAnnouncedThroughALiveRegion()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDictationSupported", _ => true).SetResult(true);
        module.Setup<int>("startDictation", _ => true).SetResult(7);
        var listening = new List<bool>();

        var voice = Render<VoiceInput>(p => p.Add(c => c.OnListeningChanged, l => listening.Add(l)));
        voice.WaitForElement("button");

        var status = voice.Find("[role=status]");
        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Equal(string.Empty, status.TextContent.Trim());

        voice.Find("button").Click();
        Assert.Equal("Listening", voice.Find("[role=status]").TextContent.Trim());
        Assert.Equal("true", voice.Find("button").GetAttribute("aria-pressed"));
        Assert.Equal("Stop dictation", voice.Find("button").GetAttribute("aria-label"));

        voice.Find("button").Click();
        Assert.Equal(7, module.VerifyInvoke("stopDictation").Arguments[0]);
        Assert.Equal("Dictation stopped", voice.Find("[role=status]").TextContent.Trim());
        Assert.Equal([true, false], listening);
    }

    [Fact]
    public async Task AFailureIsAnnouncedAndEndsListening()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDictationSupported", _ => true).SetResult(true);
        module.Setup<int>("startDictation", _ => true).SetResult(3);
        string? error = null;

        var voice = Render<VoiceInput>(p => p.Add(c => c.OnError, e => error = e));
        voice.WaitForElement("button").Click();

        await voice.InvokeAsync(() => voice.Instance.OnDictationError("not-allowed"));

        Assert.Equal("not-allowed", error);
        Assert.False(voice.Instance.IsListening);
        Assert.Equal("Dictation failed: not-allowed", voice.Find("[role=status]").TextContent.Trim());
        Assert.Equal("false", voice.Find("button").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task InterimTextIsForwardedAndClearedWhenAPhraseIsFinal()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDictationSupported", _ => true).SetResult(true);
        module.Setup<int>("startDictation", _ => true).SetResult(3);
        var composer = Render<PromptInput>(p => p.Add(c => c.AllowDictation, true));
        var voice = composer.FindComponent<VoiceInput>();
        composer.WaitForElement("[aria-label='Dictate']").Click();

        await composer.InvokeAsync(() => voice.Instance.OnTranscriptReceived(string.Empty, "hel"));
        Assert.Equal("hel", composer.Find(".bai-composer__interim").TextContent.Trim());

        await composer.InvokeAsync(() => voice.Instance.OnTranscriptReceived("hello", string.Empty));
        Assert.Empty(composer.FindAll(".bai-composer__interim"));
        Assert.Equal("hello", composer.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public async Task DisposingWhileListeningStopsTheMicrophone()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDictationSupported", _ => true).SetResult(true);
        module.Setup<int>("startDictation", _ => true).SetResult(5);
        var voice = Render<VoiceInput>();
        voice.WaitForElement("button").Click();

        await DisposeComponentsAsync();

        Assert.Equal(5, module.VerifyInvoke("stopDictation").Arguments[0]);
    }

    [Fact]
    public void TheButtonIsAbsentWhenTheBrowserCannotTranscribe()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("isDictationSupported", _ => true).SetResult(false);

        var voice = Render<VoiceInput>();

        Assert.Empty(voice.FindAll("button"));
        Assert.Empty(voice.FindAll("[role=status]"));
    }
}

public sealed class ThemeTests : AiTestBase
{
    [Fact]
    public void AnExplicitSchemeAndDirectionAreWrittenOnTheRoot()
    {
        var theme = Render<AiTheme>(p => p
            .Add(c => c.Theme, AiTheme.AiThemeMode.Light)
            .Add(c => c.Direction, AiTheme.AiTextDirection.RightToLeft));

        var root = theme.Find("div");
        Assert.Equal("light", root.GetAttribute("data-bai-theme"));
        Assert.Equal("rtl", root.GetAttribute("dir"));
        Assert.Contains("bai-theme", root.ClassList);
    }

    [Fact]
    public void TheSystemSchemeLeavesTheMarkerAndDirectionOff()
    {
        var theme = Render<AiTheme>();

        var root = theme.Find("div");
        Assert.False(root.HasAttribute("data-bai-theme"));
        Assert.False(root.HasAttribute("dir"));
    }
}

/// <summary>
/// The stylesheet and the browser module are not compiled, so their structural promises are checked
/// as text: the theme scoping, the logical properties, the contrast and print rules, and the key
/// guards the components rely on.
/// </summary>
public sealed class StaticAssetTests
{
    private static string Asset(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Blazor.AI.Components", "wwwroot", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(name);
    }

    [Fact]
    public void AnExplicitLightRegionOptsOutOfTheSystemDarkScheme()
    {
        var css = Asset("blazor-ai.css");

        var light = css.IndexOf("[data-bai-theme=\"light\"] {", StringComparison.Ordinal);
        Assert.True(light >= 0);
        Assert.Contains("color-scheme: light;", css[light..(light + 200)], StringComparison.Ordinal);

        var dark = css.IndexOf("[data-bai-theme=\"dark\"] {", StringComparison.Ordinal);
        Assert.Contains("color-scheme: dark;", css[dark..(dark + 200)], StringComparison.Ordinal);

        Assert.Contains(":root:not([data-bai-theme=\"light\"]):not(.bai-theme-light)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStylesheetUsesLogicalPropertiesOnly()
    {
        var css = Asset("blazor-ai.css");

        Assert.DoesNotMatch(new Regex(@"^\s*(padding|margin|border)-(left|right)\s*:", RegexOptions.Multiline), css);
        Assert.DoesNotMatch(new Regex(@"^\s*text-align:\s*left", RegexOptions.Multiline), css);
        Assert.Contains("inset-inline-start", css, StringComparison.Ordinal);
        Assert.Contains("border-end-end-radius", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ContrastForcedColoursAndPrintAreCovered()
    {
        var css = Asset("blazor-ai.css");

        Assert.Contains("@media (prefers-contrast: more)", css, StringComparison.Ordinal);

        var forced = css[css.IndexOf("@media (forced-colors: active)", StringComparison.Ordinal)..];
        Assert.Contains(".bai-menu__item--active", forced, StringComparison.Ordinal);
        Assert.Contains(".bai-composer__drop", forced, StringComparison.Ordinal);
        Assert.Contains("outline: 2px solid Highlight", forced, StringComparison.Ordinal);

        var print = css[css.IndexOf("@media print", StringComparison.Ordinal)..];
        Assert.Contains(".bai-composer,", print, StringComparison.Ordinal);
        Assert.Contains(".bai-menu,", print, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\.bai-chat__footer,\s*\n"), print);
    }

    [Fact]
    public void TheBrowserModuleGuardsCompositionDragsAndObservers()
    {
        var js = Asset("blazor-ai.js");

        Assert.Contains("event.keyCode === 229", js, StringComparison.Ordinal);
        Assert.Contains("data-bai-menu-open", js, StringComparison.Ordinal);
        Assert.Contains("export function unobserveScroll", js, StringComparison.Ordinal);
        Assert.Contains("export function disposeHandle", js, StringComparison.Ordinal);
        Assert.Contains("export function unregisterSubmitKey", js, StringComparison.Ordinal);
        Assert.Contains("MutationObserver", js, StringComparison.Ordinal);
        Assert.Contains("URL.createObjectURL", js, StringComparison.Ordinal);
        Assert.Contains("sessionStorage", js, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"const onLeave = \(event\) => \{\s*if \(!hasFiles\(event\)"), js);
    }
}
