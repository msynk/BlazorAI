using Blazor.AI.Markdown;

namespace Blazor.AI.Components.Tests;

public sealed class MarkdownRenderingTests : AiTestBase
{
    private IRenderedComponent<MarkdownView> RenderMarkdown(string source, bool complete = true) =>
        Render<MarkdownView>(parameters => parameters
            .Add(p => p.Document, MarkdownParser.Parse(source, isComplete: complete)));

    [Fact]
    public void HeadingsRenderOneLevelDownSoTheyDoNotCompeteWithThePageTitle()
    {
        var component = RenderMarkdown("# Top level");

        Assert.Contains("<h2", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Top level", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EmphasisBecomesSemanticElements()
    {
        var markup = RenderMarkdown("**bold** and *italic* and ~~gone~~").Markup;

        Assert.Contains("<strong>bold</strong>", markup, StringComparison.Ordinal);
        Assert.Contains("<em>italic</em>", markup, StringComparison.Ordinal);
        Assert.Contains("<del>gone</del>", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ListsRenderAsListsAndTaskItemsGetCheckboxes()
    {
        Assert.Contains("<ul", RenderMarkdown("- a\n- b").Markup, StringComparison.Ordinal);
        Assert.Contains("<ol", RenderMarkdown("1. a\n2. b").Markup, StringComparison.Ordinal);

        var tasks = RenderMarkdown("- [x] done\n- [ ] todo").Markup;
        Assert.Contains("type=\"checkbox\"", tasks, StringComparison.Ordinal);
        Assert.Contains("disabled", tasks, StringComparison.Ordinal);
    }

    [Fact]
    public void TablesScrollInsideTheirOwnContainer()
    {
        var markup = RenderMarkdown("| a | b |\n| --- | ---: |\n| 1 | 2 |").Markup;

        Assert.Contains("bai-table-wrap", markup, StringComparison.Ordinal);
        Assert.Contains("bai-align-right", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LinksOpenSafelyInANewTab()
    {
        var markup = RenderMarkdown("[docs](https://example.com)").Markup;

        Assert.Contains("""rel="noopener noreferrer nofollow" """.Trim(), markup, StringComparison.Ordinal);
        Assert.Contains("""target="_blank" """.Trim(), markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptUrlsInLinksAreNeutralised()
    {
        var markup = RenderMarkdown("[click](javascript:alert(1))").Markup;

        Assert.DoesNotContain("javascript:", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("""href="#" """.Trim(), markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptUrlsInImagesAreNeutralisedButDataImagesSurvive()
    {
        Assert.DoesNotContain(
            "javascript:",
            RenderMarkdown("![x](javascript:alert(1))").Markup,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "data:image/png",
            RenderMarkdown("![x](data:image/png;base64,AAA)").Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RawHtmlIsEscapedByDefault()
    {
        var markup = RenderMarkdown("<img src=x onerror=alert(1)>").Markup;

        Assert.DoesNotContain("onerror=alert(1)>", markup, StringComparison.Ordinal);
        Assert.Contains("&lt;img", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeBlocksShowTheirLanguageAndAreHighlightedOnceClosed()
    {
        var markup = RenderMarkdown("```csharp\npublic int Count = 1;\n```").Markup;

        Assert.Contains("bai-code", markup, StringComparison.Ordinal);
        Assert.Contains("csharp", markup, StringComparison.Ordinal);
        Assert.Contains("bai-tok-keyword", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenCodeBlockIsNotHighlightedWhileItStreams()
    {
        var markup = RenderMarkdown("```csharp\npublic int Count = 1;", complete: false).Markup;

        Assert.Contains("bai-code", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("bai-tok-keyword", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCaretAppearsOnlyOnTheOpenTrailingParagraph()
    {
        var streaming = Render<MarkdownView>(parameters => parameters
            .Add(p => p.Document, MarkdownParser.Parse("writing", isComplete: false))
            .Add(p => p.ShowCaret, true));

        Assert.Contains("bai-caret", streaming.Markup, StringComparison.Ordinal);

        var finished = Render<MarkdownView>(parameters => parameters
            .Add(p => p.Document, MarkdownParser.Parse("written"))
            .Add(p => p.ShowCaret, true));

        Assert.DoesNotContain("bai-caret", finished.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CitationMarkersRenderAsChipsWhenTheSourceIsKnown()
    {
        var citation = new Citation("c1", new CitationSource("s1", "The Docs")) { Number = 1 };

        var component = Render<MarkdownView>(parameters => parameters
            .Add(p => p.Document, MarkdownParser.Parse("as stated [1] here"))
            .Add(p => p.Citations, new[] { citation }));

        Assert.Contains("bai-cite", component.Markup, StringComparison.Ordinal);
        Assert.Contains("The Docs", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCitationNumberStaysLiteralText()
    {
        var component = Render<MarkdownView>(parameters => parameters
            .Add(p => p.Document, MarkdownParser.Parse("as stated [7] here")));

        Assert.DoesNotContain("bai-cite", component.Markup, StringComparison.Ordinal);
        Assert.Contains("[7]", component.Markup, StringComparison.Ordinal);
    }
}
