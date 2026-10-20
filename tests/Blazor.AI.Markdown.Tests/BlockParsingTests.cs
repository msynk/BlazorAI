namespace Blazor.AI.Markdown.Tests;

public sealed class BlockParsingTests
{
    private static MarkdownDocument Parse(string source, bool complete = true) =>
        MarkdownParser.Parse(source, isComplete: complete);

    [Fact]
    public void ParagraphsAreSeparatedByBlankLines()
    {
        var document = Parse("first line\nstill first\n\nsecond");

        Assert.Equal(2, document.Blocks.Count);
        Assert.All(document.Blocks, b => Assert.IsType<ParagraphBlock>(b));
    }

    [Theory]
    [InlineData("# One", 1)]
    [InlineData("### Three", 3)]
    [InlineData("###### Six", 6)]
    public void AtxHeadingsCarryTheirLevel(string source, int level)
    {
        var heading = Assert.IsType<HeadingBlock>(Parse(source).Blocks[0]);
        Assert.Equal(level, heading.Level);
    }

    [Fact]
    public void SevenHashesIsAParagraphNotAHeading()
    {
        Assert.IsType<ParagraphBlock>(Parse("####### too many").Blocks[0]);
    }

    [Fact]
    public void HeadingsGetAnchorIdentifiers()
    {
        var heading = Assert.IsType<HeadingBlock>(Parse("## Getting Started Quickly").Blocks[0]);
        Assert.Equal("bai-h-getting-started-quickly", heading.Id);
    }

    [Fact]
    public void SetextHeadingsAreRecognised()
    {
        var heading = Assert.IsType<HeadingBlock>(Parse("Title\n=====").Blocks[0]);
        Assert.Equal(1, heading.Level);
    }

    [Fact]
    public void FencedCodeKeepsItsLanguageAndBody()
    {
        var code = Assert.IsType<CodeBlock>(Parse("```csharp\nvar x = 1;\nvar y = 2;\n```").Blocks[0]);

        Assert.Equal("csharp", code.Language);
        Assert.Equal("var x = 1;\nvar y = 2;", code.Code);
        Assert.False(code.IsOpen);
    }

    [Fact]
    public void AnUnclosedFenceIsOpenWhileStreamingAndClosedWhenDone()
    {
        Assert.True(Assert.IsType<CodeBlock>(Parse("```js\nlet a =", complete: false).Blocks[0]).IsOpen);
        Assert.False(Assert.IsType<CodeBlock>(Parse("```js\nlet a =").Blocks[0]).IsOpen);
    }

    [Fact]
    public void BlankLinesInsideAFenceDoNotEndIt()
    {
        var code = Assert.IsType<CodeBlock>(Parse("```\nline one\n\nline two\n```").Blocks[0]);
        Assert.Equal("line one\n\nline two", code.Code);
    }

    [Fact]
    public void MarkdownInsideAFenceIsNotParsed()
    {
        var document = Parse("```\n# not a heading\n- not a list\n```");
        Assert.Single(document.Blocks);
        Assert.IsType<CodeBlock>(document.Blocks[0]);
    }

    [Fact]
    public void BulletListsCollectTheirItems()
    {
        var list = Assert.IsType<ListBlock>(Parse("- one\n- two\n- three").Blocks[0]);

        Assert.False(list.IsOrdered);
        Assert.Equal(3, list.Items.Count);
    }

    [Fact]
    public void OrderedListsKeepTheirStartingNumber()
    {
        var list = Assert.IsType<ListBlock>(Parse("3. three\n4. four").Blocks[0]);

        Assert.True(list.IsOrdered);
        Assert.Equal(3, list.Start);
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void TaskListItemsCarryTheirCheckedState()
    {
        var list = Assert.IsType<ListBlock>(Parse("- [x] done\n- [ ] todo").Blocks[0]);

        Assert.True(list.Items[0].IsChecked);
        Assert.False(list.Items[1].IsChecked);
    }

    [Fact]
    public void NestedListsBecomeNestedBlocks()
    {
        var list = Assert.IsType<ListBlock>(Parse("- outer\n  - inner\n- outer two").Blocks[0]);

        Assert.Equal(2, list.Items.Count);
        Assert.Contains(list.Items[0].Blocks, b => b is ListBlock);
    }

    [Fact]
    public void BlockQuotesNestTheirContent()
    {
        var quote = Assert.IsType<QuoteBlock>(Parse("> quoted\n> more").Blocks[0]);
        Assert.Single(quote.Blocks);
        Assert.IsType<ParagraphBlock>(quote.Blocks[0]);
    }

    [Fact]
    public void TablesParseHeadersRowsAndAlignment()
    {
        var table = Assert.IsType<TableBlock>(
            Parse("| Name | Qty |\n| :--- | ---: |\n| Apple | 3 |\n| Pear | 12 |").Blocks[0]);

        Assert.Equal(2, table.Header.Count);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(ColumnAlignment.Left, table.Alignments[0]);
        Assert.Equal(ColumnAlignment.Right, table.Alignments[1]);
    }

    [Fact]
    public void ATableWithoutADelimiterRowStaysAParagraph()
    {
        Assert.IsType<ParagraphBlock>(Parse("| a | b |\n| c | d |").Blocks[0]);
    }

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("- - -")]
    public void ThematicBreaksAreRecognised(string source) =>
        Assert.IsType<ThematicBreakBlock>(Parse(source).Blocks[0]);

    [Fact]
    public void DisplayMathIsItsOwnBlock()
    {
        var math = Assert.IsType<MathBlock>(Parse("$$\nE = mc^2\n$$").Blocks[0]);
        Assert.Equal("E = mc^2", math.Expression);
    }

    [Fact]
    public void RawHtmlIsCapturedAsHtmlOnlyWhenAllowed()
    {
        var allowed = MarkdownParser.Parse("<div>hi</div>", new MarkdownOptions { AllowRawHtml = true });
        Assert.IsType<HtmlBlock>(allowed.Blocks[0]);

        var escaped = MarkdownParser.Parse("<div>hi</div>");
        Assert.IsType<ParagraphBlock>(escaped.Blocks[0]);
    }

    [Fact]
    public void TheLastParagraphIsOpenWhileStreaming()
    {
        var streaming = Parse("done\n\nstill writ", complete: false);
        Assert.False(streaming.Blocks[0].IsOpen);
        Assert.True(streaming.Blocks[1].IsOpen);
    }

    [Fact]
    public void AMixedDocumentKeepsEveryBlockInOrder()
    {
        var document = Parse("# Title\n\nText.\n\n```js\ncode\n```\n\n- a\n- b\n\n> quote");

        Assert.Collection(
            document.Blocks,
            b => Assert.IsType<HeadingBlock>(b),
            b => Assert.IsType<ParagraphBlock>(b),
            b => Assert.IsType<CodeBlock>(b),
            b => Assert.IsType<ListBlock>(b),
            b => Assert.IsType<QuoteBlock>(b));
    }
}
