using Blazor.AI.Markdown;
using Microsoft.AspNetCore.Components;

namespace Blazor.AI.Components.Tests;

/// <summary>
/// The Markdown renderer's extension points and policies: custom code languages, mathematics,
/// footnotes, image handling, and the options the streaming component parses with.
/// </summary>
public sealed class MarkdownHooksTests : AiTestBase
{
    private IRenderedComponent<MarkdownView> RenderMarkdown(string source, MarkdownOptions? options = null, AiUiOptions? ui = null) =>
        Render<MarkdownView>(parameters =>
        {
            parameters.Add(p => p.Document, MarkdownParser.Parse(source, options));
            if (ui is not null) parameters.Add(p => p.CascadedOptions, ui);
        });

    [Fact]
    public void ARegisteredCodeLanguageRendersThroughItsOwnRenderer()
    {
        Renderers.RegisterCodeLanguage("mermaid", context => builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "diagram");
            builder.AddAttribute(2, "data-streaming", context.IsStreaming);
            builder.AddContent(3, context.Code);
            builder.CloseElement();
        });

        var markup = RenderMarkdown("```mermaid\ngraph TD; A-->B\n```").Markup;

        Assert.Contains("class=\"diagram\"", markup, StringComparison.Ordinal);
        Assert.Contains("graph TD; A--&gt;B", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("bai-code", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeLanguagesAreMatchedWithoutRegardToCaseAndOtherLanguagesStillUseTheCodeView()
    {
        Renderers.RegisterCodeLanguage("Vega", _ => builder => builder.AddMarkupContent(0, "<div class=\"chart\"></div>"));

        Assert.Contains("class=\"chart\"", RenderMarkdown("```vega\n{}\n```").Markup, StringComparison.Ordinal);
        Assert.Contains("bai-code", RenderMarkdown("```json\n{}\n```").Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MathFallsBackToMonospaceWithTheSourceInADataAttribute()
    {
        var markup = RenderMarkdown("Inline $x^2$ and\n\n$$\nE = mc^2\n$$").Markup;

        Assert.Contains("class=\"bai-math\" data-math=\"x^2\"", markup, StringComparison.Ordinal);
        Assert.Contains("bai-math--block", markup, StringComparison.Ordinal);
        Assert.Contains("data-math=\"E = mc^2\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegisteredMathRendererReplacesTheFallback()
    {
        Renderers.MathRenderer = context => builder =>
        {
            builder.OpenElement(0, context.IsBlock ? "div" : "span");
            builder.AddAttribute(1, "class", "katex");
            builder.AddContent(2, context.Expression);
            builder.CloseElement();
        };

        var markup = RenderMarkdown("Inline $x^2$ and\n\n$$\nE\n$$").Markup;

        Assert.Contains("<span class=\"katex\">x^2</span>", markup, StringComparison.Ordinal);
        Assert.Contains("<div class=\"katex\">E</div>", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("bai-math", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void FootnotesAreListedAfterTheTextWithBackLinks()
    {
        var component = RenderMarkdown("Claim[^1] and again[^1].\n\n[^1]: The note.");

        var references = component.FindAll("sup.bai-footnote-ref");
        Assert.Equal(2, references.Count);
        Assert.Equal("1", references[0].TextContent);

        var section = component.Find("section.bai-footnotes");
        Assert.Contains("The note.", section.TextContent, StringComparison.Ordinal);
        Assert.Equal(2, section.QuerySelectorAll("a.bai-footnote-backref").Length);

        // Each reference links to the note, and each back-link to its reference.
        var noteId = section.QuerySelector("li")!.Id;
        Assert.Equal("#" + noteId, references[0].QuerySelector("a")!.GetAttribute("href"));
        var backHref = section.QuerySelector("a.bai-footnote-backref")!.GetAttribute("href");
        Assert.Equal(backHref![1..], references[0].Id);
    }

    [Fact]
    public void ADocumentWithoutFootnotesHasNoFootnoteSection() =>
        Assert.DoesNotContain("bai-footnotes", RenderMarkdown("plain").Markup, StringComparison.Ordinal);

    [Fact]
    public void AnHtmlBlockIsShownAsTextWhenTheRendererDoesNotAllowRawHtml()
    {
        var markup = RenderMarkdown("<div onclick=\"x()\">hi</div>", new MarkdownOptions { AllowRawHtml = true }).Markup;

        Assert.DoesNotContain("<div onclick", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<pre", markup, StringComparison.Ordinal);
        Assert.Contains("<p>&lt;div onclick=", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadingIdentifiersArePrefixedInTheMarkup() =>
        Assert.Contains("id=\"bai-h-setup\"", RenderMarkdown("## Setup").Markup, StringComparison.Ordinal);

    [Fact]
    public void LinkTitlesAreRendered() =>
        Assert.Contains("title=\"The docs\"", RenderMarkdown("[docs](https://x.dev \"The docs\")").Markup, StringComparison.Ordinal);

    [Fact]
    public void EntitiesRenderAsTheirCharacters() =>
        Assert.Contains("AT&amp;T ©", RenderMarkdown("AT&amp;T &copy;").Markup, StringComparison.Ordinal);

    [Fact]
    public void ImagesAreShownByDefault()
    {
        var markup = RenderMarkdown("![cat](https://img.x.dev/cat.png)").Markup;
        Assert.Contains("<img src=\"https://img.x.dev/cat.png\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlockPolicyReplacesImagesWithTheirAlternativeText()
    {
        var ui = AiUiOptions.Default with { Images = AiImagePolicy.Block };
        var markup = RenderMarkdown("![a cat](https://img.x.dev/cat.png) ![](data:image/png;base64,AAA)", ui: ui).Markup;

        Assert.DoesNotContain("<img", markup, StringComparison.Ordinal);
        Assert.Contains("<span class=\"bai-image-blocked\">a cat</span>", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClickToLoadPolicyShowsAPlaceholderUntilActivated()
    {
        var ui = AiUiOptions.Default with { Images = AiImagePolicy.ClickToLoad };
        var component = RenderMarkdown("![a cat](https://img.x.dev/cat.png)", ui: ui);

        Assert.DoesNotContain("<img", component.Markup, StringComparison.Ordinal);
        var placeholder = component.Find("button.bai-image-placeholder");
        Assert.Equal("Load image: a cat", placeholder.TextContent.Trim());

        placeholder.Click();

        Assert.Contains("<img src=\"https://img.x.dev/cat.png\"", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("bai-image-placeholder", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ClickToLoadStillShowsInlineDataImages()
    {
        var ui = AiUiOptions.Default with { Images = AiImagePolicy.ClickToLoad };
        var markup = RenderMarkdown("![x](data:image/png;base64,AAA)", ui: ui).Markup;

        Assert.Contains("<img src=\"data:image/png;base64,AAA\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHostListPolicyShowsListedHostsAndAsksForTheRest()
    {
        var ui = AiUiOptions.Default with { Images = AiImagePolicy.AllowListedHosts("cdn.x.dev", "*.trusted.org") };
        var markup = RenderMarkdown(
            "![a](https://cdn.x.dev/a.png) ![b](https://img.trusted.org/b.png) ![c](https://evil.example/c.png)",
            ui: ui).Markup;

        Assert.Contains("<img src=\"https://cdn.x.dev/a.png\"", markup, StringComparison.Ordinal);
        Assert.Contains("<img src=\"https://img.trusted.org/b.png\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=\"https://evil.example/c.png\"", markup, StringComparison.Ordinal);
        Assert.Contains("bai-image-placeholder", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptImageUrlsAreNeutralisedUnderEveryPolicy()
    {
        foreach (var policy in new[] { AiImagePolicy.Allow, AiImagePolicy.ClickToLoad, AiImagePolicy.Block, AiImagePolicy.AllowListedHosts("x.dev") })
        {
            var markup = RenderMarkdown("![x](javascript:alert(1))", ui: AiUiOptions.Default with { Images = policy }).Markup;
            Assert.DoesNotContain("javascript:", markup, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ThePolicyDecidesPerUrl()
    {
        var listed = AiImagePolicy.AllowListedHosts("cdn.x.dev");

        Assert.Equal(AiImageDecision.Show, listed.Decide("https://cdn.x.dev/a.png"));
        Assert.Equal(AiImageDecision.Show, listed.Decide("https://CDN.X.DEV/a.png"));
        Assert.Equal(AiImageDecision.AskFirst, listed.Decide("https://other.dev/a.png"));
        Assert.Equal(AiImageDecision.AskFirst, listed.Decide("not a url"));
        Assert.Equal(AiImageDecision.Show, listed.Decide("data:image/png;base64,AAA"));
        Assert.Equal(AiImageDecision.Hide, AiImagePolicy.Block.Decide("data:image/png;base64,AAA"));
        Assert.Equal(AiImageDecision.Hide, (AiImagePolicy.Allow with { AllowDataImages = false }).Decide("data:image/png;base64,AAA"));
    }

    [Fact]
    public void StreamingMarkdownParsesWithTheCascadedMarkdownOptions()
    {
        var withMath = Render<StreamingMarkdown>(p => p.Add(c => c.Text, "value $x^2$ here"));
        Assert.Contains("bai-math", withMath.Markup, StringComparison.Ordinal);

        var ui = AiUiOptions.Default with { Markdown = new MarkdownOptions { Math = false } };
        var withoutMath = Render<StreamingMarkdown>(p => p
            .Add(c => c.Text, "value $x^2$ here")
            .Add(c => c.CascadedOptions, ui));

        Assert.DoesNotContain("bai-math", withoutMath.Markup, StringComparison.Ordinal);
        Assert.Contains("$x^2$", withoutMath.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamingMarkdownReparsesWhenTheOptionsInstanceChanges()
    {
        var component = Render<OptionsHost>(p => p.Add(c => c.Options, AiUiOptions.Default));
        Assert.DoesNotContain("<br", component.Markup, StringComparison.Ordinal);

        var hardBreaks = AiUiOptions.Default with { Markdown = new MarkdownOptions { SoftBreaksAsHardBreaks = true } };
        component.Render(p => p.Add(c => c.Options, hardBreaks));

        Assert.Contains("<br", component.Markup, StringComparison.Ordinal);
    }

    /// <summary>Cascades a changeable options instance to one streaming component, as AiUiOptionsProvider would.</summary>
    private sealed class OptionsHost : ComponentBase
    {
        [Parameter]
        public AiUiOptions Options { get; set; } = AiUiOptions.Default;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<AiUiOptions>>(0);
            builder.AddComponentParameter(1, "Value", Options);
            builder.AddComponentParameter(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<StreamingMarkdown>(0);
                child.AddComponentParameter(1, "Text", "a\nb");
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    [Fact]
    public void StreamingMarkdownKeepsEarlierBlocksAsTextArrivesAndSettlesWhenItEnds()
    {
        var component = Render<StreamingMarkdown>(p => p
            .Add(c => c.Text, "First.\n\nSecond **bo")
            .Add(c => c.IsStreaming, true));

        Assert.Contains("bai-caret", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("**", component.Markup, StringComparison.Ordinal);

        component.Render(p => p
            .Add(c => c.Text, "First.\n\nSecond **bold**.")
            .Add(c => c.IsStreaming, false));

        Assert.Contains("<strong>bold</strong>", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("bai-caret", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamingMarkdownDisposesThroughTheAsyncPath()
    {
        var component = Render<StreamingMarkdown>(p => p.Add(c => c.Text, "text"));

        // The base class is IAsyncDisposable; the component must not rely on IDisposable, which
        // Blazor never calls on a component that implements both.
        Assert.False(component.Instance is IDisposable);
        await ((IAsyncDisposable)component.Instance).DisposeAsync();
    }

    [Fact]
    public void IndentedCodeRendersAsACodeBlock()
    {
        var markup = RenderMarkdown("para\n\n    var x = 1;").Markup;
        Assert.Contains("bai-code", markup, StringComparison.Ordinal);
        Assert.Contains("var x = 1;", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceLinksRenderAsLinks()
    {
        var markup = RenderMarkdown("See [the docs][d].\n\n[d]: https://x.dev/docs").Markup;
        Assert.Contains("href=\"https://x.dev/docs\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("[d]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShellCodeIsHighlightedWithItsOwnTokens()
    {
        var markup = RenderMarkdown("```powershell\nGet-ChildItem -Path $env:HOME\n```").Markup;

        Assert.Contains("bai-tok-function", markup, StringComparison.Ordinal);
        Assert.Contains("bai-tok-variable", markup, StringComparison.Ordinal);
    }
}
