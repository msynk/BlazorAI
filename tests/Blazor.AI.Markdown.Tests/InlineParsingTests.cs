namespace Blazor.AI.Markdown.Tests;

public sealed class InlineParsingTests
{
    private static List<MarkdownInline> Parse(string source, bool complete = true) =>
        InlineParser.Parse(source, isComplete: complete);

    private static string Flatten(IEnumerable<MarkdownInline> inlines) =>
        string.Concat(inlines.Select(i => i switch
        {
            TextInline t => t.Text,
            EmphasisInline e => Flatten(e.Inlines),
            StrikethroughInline s => Flatten(s.Inlines),
            LinkInline l => Flatten(l.Inlines),
            CodeInline c => c.Code,
            LineBreakInline => " ",
            _ => string.Empty,
        }));

    [Fact]
    public void PlainTextIsOneRun()
    {
        var inlines = Parse("just text");
        Assert.Equal("just text", Assert.IsType<TextInline>(Assert.Single(inlines)).Text);
    }

    [Fact]
    public void StrongAndOrdinaryEmphasisAreDistinguished()
    {
        var strong = Parse("**bold**").OfType<EmphasisInline>().Single();
        Assert.True(strong.IsStrong);

        var italic = Parse("*italic*").OfType<EmphasisInline>().Single();
        Assert.False(italic.IsStrong);
    }

    [Fact]
    public void EmphasisNestsAndKeepsSurroundingText()
    {
        var inlines = Parse("a **bold *and italic* end** z");

        Assert.Equal("a bold and italic end z", Flatten(inlines));
        var outer = inlines.OfType<EmphasisInline>().Single();
        Assert.True(outer.IsStrong);
        Assert.Contains(outer.Inlines, i => i is EmphasisInline { IsStrong: false });
    }

    [Fact]
    public void UnderscoresInsideAWordAreNotEmphasis()
    {
        var inlines = Parse("snake_case_name");
        Assert.Empty(inlines.OfType<EmphasisInline>());
    }

    [Fact]
    public void CodeSpansAreLiteralAndStripOneSurroundingSpace()
    {
        Assert.Equal("a * b", Parse("`a * b`").OfType<CodeInline>().Single().Code);
        Assert.Equal("`", Parse("`` ` ``").OfType<CodeInline>().Single().Code);
    }

    [Fact]
    public void EmphasisInsideACodeSpanIsNotParsed()
    {
        var inlines = Parse("`**not bold**`");
        Assert.Empty(inlines.OfType<EmphasisInline>());
    }

    [Fact]
    public void StrikethroughUsesDoubleTildes()
    {
        var strike = Parse("~~gone~~").OfType<StrikethroughInline>().Single();
        Assert.Equal("gone", Flatten(strike.Inlines));
    }

    [Fact]
    public void LinksCaptureTextDestinationAndTitle()
    {
        var link = Parse("""[Docs](https://example.com "The docs")""").OfType<LinkInline>().Single();

        Assert.Equal("https://example.com", link.Url);
        Assert.Equal("The docs", link.Title);
        Assert.Equal("Docs", Flatten(link.Inlines));
    }

    [Fact]
    public void ImagesCaptureAltAndSource()
    {
        var image = Parse("![a cat](cat.png)").OfType<ImageInline>().Single();

        Assert.Equal("cat.png", image.Url);
        Assert.Equal("a cat", image.Alt);
    }

    [Fact]
    public void BareUrlsBecomeLinksWhenAutoLinkingIsOn()
    {
        var link = Parse("see https://example.com/page for more").OfType<LinkInline>().Single();

        Assert.Equal("https://example.com/page", link.Url);
        Assert.True(link.IsAutoLink);
    }

    [Fact]
    public void TrailingPunctuationIsNotPartOfABareUrl()
    {
        var link = Parse("go to https://example.com.").OfType<LinkInline>().Single();
        Assert.Equal("https://example.com", link.Url);
    }

    [Fact]
    public void AngleAutoLinksIncludeMailTo()
    {
        Assert.Equal("mailto:a@b.com", Parse("<a@b.com>").OfType<LinkInline>().Single().Url);
        Assert.Equal("https://x.dev", Parse("<https://x.dev>").OfType<LinkInline>().Single().Url);
    }

    [Fact]
    public void BracketedNumbersBecomeCitationMarkers()
    {
        var marker = Parse("as reported [3] recently").OfType<CitationMarkerInline>().Single();
        Assert.Equal(3, marker.Number);
    }

    [Fact]
    public void ALinkIsNotMistakenForACitationMarker()
    {
        var inlines = Parse("[42](https://example.com)");
        Assert.Empty(inlines.OfType<CitationMarkerInline>());
        Assert.Single(inlines.OfType<LinkInline>());
    }

    [Fact]
    public void InlineMathIsCaptured()
    {
        Assert.Equal("x^2", Parse("the value $x^2$ here").OfType<MathInline>().Single().Expression);
    }

    [Fact]
    public void BackslashEscapesProduceLiteralCharacters()
    {
        Assert.Equal("*not emphasis*", Flatten(Parse("""\*not emphasis\*""")));
    }

    [Fact]
    public void TwoTrailingSpacesMakeAHardBreak()
    {
        Assert.True(Parse("one  \ntwo").OfType<LineBreakInline>().Single().IsHard);
        Assert.False(Parse("one\ntwo").OfType<LineBreakInline>().Single().IsHard);
    }

    [Fact]
    public void ADanglingDelimiterIsHiddenWhileStreamingAndLiteralWhenDone()
    {
        Assert.Equal("bold so far", Flatten(Parse("**bold so far", complete: false)));
        Assert.Equal("**bold so far", Flatten(Parse("**bold so far")));
    }

    [Fact]
    public void AnUnclosedCodeSpanIsHiddenWhileStreamingAndLiteralWhenDone()
    {
        Assert.Equal("half", Flatten(Parse("`half", complete: false)));
        Assert.Equal("`half", Flatten(Parse("`half")));
    }

    [Fact]
    public void EmphasisResolvesOnceTheClosingDelimiterArrives()
    {
        Assert.Empty(Parse("**partial", complete: false).OfType<EmphasisInline>());
        Assert.Single(Parse("**partial**", complete: false).OfType<EmphasisInline>());
    }
}
