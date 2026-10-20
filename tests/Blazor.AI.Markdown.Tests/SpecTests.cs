using System.Diagnostics;
using System.Text;

namespace Blazor.AI.Markdown.Tests;

/// <summary>Shared helpers for the specification tests below.</summary>
internal static class Spec
{
    public static MarkdownDocument Parse(string source, MarkdownOptions? options = null, bool complete = true) =>
        MarkdownParser.Parse(source, options, complete);

    public static string Dump(string source, MarkdownOptions? options = null, bool complete = true) =>
        Dump(Parse(source, options, complete));

    /// <summary>The tree and the footnotes, so two parses can be compared exactly.</summary>
    public static string Dump(MarkdownDocument document)
    {
        var builder = new StringBuilder(TreeDump.Of(document));
        foreach (var footnote in document.Footnotes)
        {
            var body = new MarkdownDocument();
            body.Blocks.AddRange(footnote.Blocks);
            builder.Append("|fn ").Append(footnote.Label).Append(':').Append(footnote.Number).Append('x').Append(footnote.ReferenceCount).Append(TreeDump.Of(body));
        }

        return builder.ToString();
    }

    public static string InlineDump(string source, MarkdownOptions? options = null, bool complete = true)
    {
        var document = new MarkdownDocument();
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.AddRange(InlineParser.Parse(source, options, complete));
        document.Blocks.Add(paragraph);
        return TreeDump.Of(document);
    }

    public static string Text(IEnumerable<MarkdownInline> inlines) =>
        string.Concat(inlines.Select(i => i switch
        {
            TextInline t => t.Text,
            EmphasisInline e => Text(e.Inlines),
            StrikethroughInline s => Text(s.Inlines),
            LinkInline l => Text(l.Inlines),
            CodeInline c => c.Code,
            LineBreakInline => "\n",
            FootnoteReferenceInline f => $"[^{f.Label}]",
            CitationMarkerInline c => $"[{c.Number}]",
            _ => string.Empty,
        }));

    public static string Text(MarkdownBlock block) => block switch
    {
        ParagraphBlock p => Text(p.Inlines),
        HeadingBlock h => Text(h.Inlines),
        _ => string.Empty,
    };

    public static int BlockDepth(IEnumerable<MarkdownBlock> blocks) =>
        blocks.Select(b => b switch
        {
            QuoteBlock q => 1 + BlockDepth(q.Blocks),
            ListBlock l => 1 + l.Items.Select(i => BlockDepth(i.Blocks)).DefaultIfEmpty(0).Max(),
            _ => 0,
        }).DefaultIfEmpty(0).Max();

    public static int InlineDepth(IEnumerable<MarkdownInline> inlines) =>
        inlines.Select(i => i switch
        {
            EmphasisInline e => 1 + InlineDepth(e.Inlines),
            StrikethroughInline s => 1 + InlineDepth(s.Inlines),
            LinkInline l => 1 + InlineDepth(l.Inlines),
            _ => 0,
        }).DefaultIfEmpty(0).Max();

    public static string Repeat(string pattern, int count) => string.Concat(Enumerable.Repeat(pattern, count));

    public static TimeSpan Time(Action action, int runs = 3)
    {
        var best = TimeSpan.MaxValue;
        for (var run = 0; run < runs; run++)
        {
            GC.Collect();
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            if (stopwatch.Elapsed < best) best = stopwatch.Elapsed;
        }

        return best;
    }

    /// <summary>Asserts the incremental parser agrees with a one-shot parse at every prefix and at the end.</summary>
    public static void AssertIncrementalMatches(string source, MarkdownOptions? options = null)
    {
        var parser = new IncrementalMarkdownParser(options);

        for (var length = 1; length <= source.Length; length++)
        {
            var prefix = source[..length];
            var expected = Dump(MarkdownParser.Parse(prefix, options, isComplete: false));
            var actual = Dump(parser.Parse(prefix, isComplete: false));
            Assert.True(expected == actual, $"Mismatch at prefix {length}: {prefix}\nexpected: {expected}\nactual:   {actual}");
        }

        Assert.Equal(Dump(MarkdownParser.Parse(source, options)), Dump(parser.Parse(source, isComplete: true)));
    }
}

/// <summary>Finding 1: nesting is bounded by an option, and deep input is fast rather than fatal.</summary>
[Collection(nameof(TimingCollection))]
public sealed class NestingDepthTests
{
    [Fact]
    public void FiveThousandNestedQuotesParseQuicklyAndStayWithinTheDefaultDepth()
    {
        var source = Spec.Repeat("> ", 5000) + "x";
        MarkdownDocument document = null!;

        var elapsed = Spec.Time(() => document = Spec.Parse(source));

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Took {elapsed.TotalMilliseconds:F0}ms.");
        Assert.InRange(Spec.BlockDepth(document.Blocks), 1, 32);
    }

    [Fact]
    public void FiveThousandNestedBracketsParseQuickly()
    {
        var source = Spec.Repeat("[", 5000) + "a" + Spec.Repeat("](u)", 5000);
        var elapsed = Spec.Time(() => Spec.Parse(source));
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Took {elapsed.TotalMilliseconds:F0}ms.");
    }

    [Fact]
    public void FiveThousandNestedEmphasisSpansParseQuicklyAndStayWithinTheDefaultDepth()
    {
        var source = Spec.Repeat("*a ", 5000) + "b" + Spec.Repeat(" c*", 5000);
        List<MarkdownInline> inlines = null!;

        var elapsed = Spec.Time(() => inlines = InlineParser.Parse(source));

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Took {elapsed.TotalMilliseconds:F0}ms.");
        Assert.InRange(Spec.InlineDepth(inlines), 1, 32);
    }

    [Fact]
    public void DeeplyNestedListsParseQuicklyAndStayWithinTheDefaultDepth()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 400; i++) builder.Append(' ', i * 2).Append("- a\n");
        var source = builder.ToString();
        MarkdownDocument document = null!;

        var elapsed = Spec.Time(() => document = Spec.Parse(source));

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Took {elapsed.TotalMilliseconds:F0}ms.");
        Assert.InRange(Spec.BlockDepth(document.Blocks), 1, 32);
    }

    [Fact]
    public void MaxNestingDepthBoundsBlocksAndTurnsDeeperMarkersIntoText()
    {
        var options = new MarkdownOptions { MaxNestingDepth = 3 };
        var document = Spec.Parse("> > > > > deep", options);

        Assert.Equal(3, Spec.BlockDepth(document.Blocks));

        var innermost = document.Blocks[0];
        for (var i = 0; i < 3; i++) innermost = Assert.IsType<QuoteBlock>(innermost).Blocks[0];
        Assert.Equal("> > deep", Spec.Text(innermost));
    }

    [Fact]
    public void MaxNestingDepthBoundsInlineSpansAndKeepsTheInnerOnes()
    {
        var options = new MarkdownOptions { MaxNestingDepth = 4 };
        var inlines = InlineParser.Parse(Spec.Repeat("*a ", 50) + "b" + Spec.Repeat(" c*", 50), options);

        Assert.Equal(4, Spec.InlineDepth(inlines));
        Assert.StartsWith("*a *a ", Spec.Text(inlines), StringComparison.Ordinal);
    }

    [Fact]
    public void BeyondTheLimitTheMarkersBecomeTextAndTheInnerSpansSurvive()
    {
        var options = new MarkdownOptions { MaxNestingDepth = 1 };
        Assert.Equal("{ParagraphBlock(\"*a \"strong(\"b\")\" c*\")}", Spec.InlineDump("*a **b** c*", options));
    }
}

/// <summary>Finding 2: unmatched emphasis openers cost linear time.</summary>
[Collection(nameof(TimingCollection))]
public sealed class UnmatchedEmphasisPerformanceTests
{
    [Theory]
    [InlineData("*a ")]
    [InlineData("_a ")]
    [InlineData("**a ")]
    public void OneHundredEightyKilobytesOfUnmatchedOpenersParseInWellUnderASecond(string pattern)
    {
        var source = Spec.Repeat(pattern, 180_000 / pattern.Length);
        var elapsed = Spec.Time(() => MarkdownParser.Parse(source));

        // Measured at about 50ms in Release; the limit leaves room for a busy CI machine while
        // staying an order of magnitude under the four seconds the quadratic scan took.
        Assert.True(elapsed < TimeSpan.FromMilliseconds(400), $"'{pattern}' took {elapsed.TotalMilliseconds:F0}ms for {source.Length} characters.");
    }
}

/// <summary>Finding 3: the stable prefix grows while text arrives a few characters at a time.</summary>
public sealed class TokenStreamingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void TheStablePrefixGrowsAsParagraphsStreamInSmallChunks(int chunk)
    {
        var source = string.Concat(Enumerable.Range(0, 50).Select(i => $"Paragraph {i} has some **bold** words and a [link](https://x.dev/{i}).\n\n"));
        var parser = new IncrementalMarkdownParser();
        var previous = 0;
        var growths = 0;

        for (var length = chunk; length < source.Length; length += chunk)
        {
            parser.Parse(source[..length], isComplete: false);
            Assert.True(parser.StableLength >= previous, "The stable prefix must never shrink while the text only grows.");
            if (parser.StableLength > previous) growths++;
            previous = parser.StableLength;
        }

        var final = parser.Parse(source, isComplete: true);

        Assert.True(growths >= 40, $"The stable prefix grew only {growths} times over 50 paragraphs.");
        Assert.True(parser.StableBlockCount >= 49, $"Only {parser.StableBlockCount} blocks became stable.");
        Assert.Equal(Spec.Dump(MarkdownParser.Parse(source)), Spec.Dump(final));
    }
}

/// <summary>Finding 4: dollar signs follow pandoc's rules, so prices are not mathematics.</summary>
public sealed class InlineMathTests
{
    [Theory]
    [InlineData("costs $5 and $10 total")]
    [InlineData("It is $5.")]
    [InlineData("a $10,000 budget and $20,000 more")]
    [InlineData("$ a$")]
    [InlineData("$a $")]
    [InlineData("$5 or $6")]
    public void PricesAreNotMath(string source) =>
        Assert.Empty(InlineParser.Parse(source).OfType<MathInline>());

    [Theory]
    [InlineData("$x^2$", "x^2")]
    [InlineData("between $5 and $x$ here", "x")]
    [InlineData("$x^2$ costs $5", "x^2")]
    [InlineData("$2x + 1 = 5$", "2x + 1 = 5")]
    [InlineData("$\\alpha$", "\\alpha")]
    public void ExpressionsHuggedByTheirDelimitersAreMath(string source, string expression) =>
        Assert.Equal(expression, InlineParser.Parse(source).OfType<MathInline>().Single().Expression);
}

/// <summary>Finding 5: emphasis follows the CommonMark delimiter algorithm, with one leniency.</summary>
public sealed class EmphasisSpecTests
{
    [Theory]
    [InlineData("*a **b** c*", "{ParagraphBlock(em(\"a \"strong(\"b\")\" c\"))}")]
    [InlineData("***bold italic*** after", "{ParagraphBlock(em(strong(\"bold italic\"))\" after\")}")]
    [InlineData("**a *b* c**", "{ParagraphBlock(strong(\"a \"em(\"b\")\" c\"))}")]
    [InlineData("*foo**bar**baz*", "{ParagraphBlock(em(\"foo\"strong(\"bar\")\"baz\"))}")]
    [InlineData("foo***bar***baz", "{ParagraphBlock(\"foo\"em(strong(\"bar\"))\"baz\")}")]
    [InlineData("**foo*bar*baz**", "{ParagraphBlock(strong(\"foo\"em(\"bar\")\"baz\"))}")]
    public void NestedEmphasisFollowsCommonMark(string source, string expected) =>
        Assert.Equal(expected, Spec.InlineDump(source));

    [Theory]
    // The second run cannot close by the letter of the specification, since a space precedes
    // it, but nothing else claims it and a reader sees bold text.
    [InlineData("**bold **text", "{ParagraphBlock(strong(\"bold\")\" text\")}")]
    [InlineData("*it *text", "{ParagraphBlock(em(\"it\")\" text\")}")]
    [InlineData("say **bold **text and **more**", "{ParagraphBlock(\"say \"strong(\"bold\")\" text and \"strong(\"more\"))}")]
    // A run with whitespace on both sides is neither an opener nor a closer, and stays text.
    [InlineData("say **bold ** and more", "{ParagraphBlock(\"say **bold ** and more\")}")]
    // Where a run can close properly, the specification wins.
    [InlineData("**a **b**", "{ParagraphBlock(\"**a \"strong(\"b\"))}")]
    // Runs of different sizes are not paired leniently.
    [InlineData("**bold *text", "{ParagraphBlock(\"**bold *text\")}")]
    public void AWhitespacePrecededRunClosesWhenNothingElseClaimsIt(string source, string expected) =>
        Assert.Equal(expected, Spec.InlineDump(source));

    [Fact]
    public void TheLenientCloserAlsoAppliesWhileStreaming() =>
        Assert.Equal("{ParagraphBlock(strong(\"bold\")\" text\")}", Spec.InlineDump("**bold **text", complete: false));
}

/// <summary>Findings 6, 7, 8: streaming marks only the last item open and agrees with a full parse.</summary>
public sealed class StreamingBlockTests
{
    [Theory]
    [InlineData("- a **b\n- c **d\n- e")]
    [InlineData("1. a **b\n2. c **d\n3. e")]
    public void OnlyTheLastListItemIsOpenWhileStreaming(string source)
    {
        var list = Assert.IsType<ListBlock>(Spec.Parse(source, complete: false).Blocks[0]);

        Assert.Equal(3, list.Items.Count);
        Assert.False(list.Items[0].Blocks[0].IsOpen);
        Assert.False(list.Items[1].Blocks[0].IsOpen);
        Assert.True(list.Items[2].Blocks[0].IsOpen);
        Assert.Equal("a **b", Spec.Text(list.Items[0].Blocks[0]));
        Assert.Equal("c **d", Spec.Text(list.Items[1].Blocks[0]));
    }

    [Fact]
    public void AClosingFenceWithTrailingWhitespaceClosesInBothParsers()
    {
        const string source = "```js\ncode\n```   \n\nparagraph\n\nmore";
        Spec.AssertIncrementalMatches(source);

        var document = Spec.Parse(source);
        Assert.Equal(3, document.Blocks.Count);
        Assert.False(document.Blocks[0].IsOpen);
    }

    [Fact]
    public void DisplayMathSpanningBlankLinesIsOneBlockInBothParsers()
    {
        const string source = "Intro.\n\n$$\na\n\nb\n$$\n\nafter";
        Spec.AssertIncrementalMatches(source);

        var document = Spec.Parse(source);
        Assert.Equal("a\n\nb", Assert.IsType<MathBlock>(document.Blocks[1]).Expression);
    }
}

/// <summary>Finding 10: a link's text never contains another link.</summary>
public sealed class LinkLabelTests
{
    [Theory]
    [InlineData("[https://x.dev](https://x.dev)")]
    [InlineData("[<https://x.dev>](https://x.dev)")]
    [InlineData("[www.x.dev](https://x.dev)")]
    [InlineData("[see https://x.dev now](https://x.dev)")]
    public void AutoLinksInsideALinkLabelAreText(string source)
    {
        var link = Assert.IsType<LinkInline>(Assert.Single(InlineParser.Parse(source)));
        Assert.Empty(link.Inlines.OfType<LinkInline>());
    }
}

/// <summary>Finding 11: entity and numeric character references are decoded in text.</summary>
public sealed class EntityTests
{
    [Theory]
    [InlineData("AT&amp;T", "AT&T")]
    [InlineData("&copy; 2024", "© 2024")]
    [InlineData("&#39;x&#39;", "'x'")]
    [InlineData("&#x27;&#X27;", "''")]
    [InlineData("&lt;b&gt;", "<b>")]
    [InlineData("&#0;", "�")]
    [InlineData("&#1114112;", "�")]
    [InlineData("&nbsp;", " ")]
    [InlineData("&hellip;", "…")]
    public void ValidReferencesAreDecoded(string source, string expected) =>
        Assert.Equal(expected, Assert.IsType<TextInline>(Assert.Single(InlineParser.Parse(source))).Text);

    [Theory]
    [InlineData("&notanentity;")]
    [InlineData("&amp")]
    [InlineData("a & b")]
    [InlineData("&#;")]
    [InlineData("&#xZZ;")]
    [InlineData("&;")]
    [InlineData("& amp;")]
    public void AnythingElseStaysAsWritten(string source) =>
        Assert.Equal(source, Spec.Text(InlineParser.Parse(source)));

    [Fact]
    public void ReferencesInsideCodeAreNotDecoded()
    {
        Assert.Equal("&amp;", InlineParser.Parse("`&amp;`").OfType<CodeInline>().Single().Code);
        Assert.Equal("&amp;", Assert.IsType<CodeBlock>(Spec.Parse("```\n&amp;\n```").Blocks[0]).Code);
    }

    [Fact]
    public void ADecodedAngleBracketIsTextNotMarkup()
    {
        var inlines = InlineParser.Parse("&lt;script&gt;", new MarkdownOptions { AllowRawHtml = true });
        Assert.Equal("<script>", Assert.IsType<TextInline>(Assert.Single(inlines)).Text);
    }
}

/// <summary>Findings 12, 13, 16: lazy continuation lines never start a block.</summary>
public sealed class LazyContinuationTests
{
    [Fact]
    public void AThematicBreakAfterAListItemEndsTheList()
    {
        var document = Spec.Parse("- last\n---\nNext");

        Assert.Collection(
            document.Blocks,
            b => Assert.Single(Assert.IsType<ListBlock>(b).Items),
            b => Assert.IsType<ThematicBreakBlock>(b),
            b => Assert.Equal("Next", Spec.Text(b)));
    }

    [Fact]
    public void DisplayMathAfterAListItemEndsTheList()
    {
        var document = Spec.Parse("- a\n$$\nx\n$$");

        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal("x", Assert.IsType<MathBlock>(document.Blocks[1]).Expression);
    }

    [Fact]
    public void ATableAfterAListItemEndsTheList()
    {
        var document = Spec.Parse("- a\n| x | y |\n|---|---|\n| 1 | 2 |");

        Assert.Equal(2, document.Blocks.Count);
        Assert.IsType<TableBlock>(document.Blocks[1]);
    }

    [Fact]
    public void AnHtmlBlockAfterAListItemEndsTheList()
    {
        var document = Spec.Parse("- a\n<div>\nx\n</div>", new MarkdownOptions { AllowRawHtml = true });

        Assert.Equal(2, document.Blocks.Count);
        Assert.IsType<HtmlBlock>(document.Blocks[1]);
    }

    [Fact]
    public void PlainTextStillContinuesAListItemLazily()
    {
        var list = Assert.IsType<ListBlock>(Assert.Single(Spec.Parse("- a\nb").Blocks));
        Assert.Equal("a\nb", Spec.Text(list.Items[0].Blocks[0]));
    }

    [Fact]
    public void TextAfterABlankLineThatNoItemCanClaimIsKeptAsAParagraph()
    {
        var document = Spec.Parse("- a\n - b\n\n  text");

        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal(2, Assert.IsType<ListBlock>(document.Blocks[0]).Items.Count);
        Assert.Equal("text", Spec.Text(document.Blocks[1]));
    }

    [Fact]
    public void IndentedTextAfterABlankLineContinuesTheItem()
    {
        var list = Assert.IsType<ListBlock>(Assert.Single(Spec.Parse("- a\n\n  continued\n\n  more").Blocks));
        var item = Assert.Single(list.Items);

        Assert.Equal(["a", "continued", "more"], item.Blocks.Select(Spec.Text));
    }

    [Fact]
    public void SeveralBlankLinesBetweenItemsKeepOneList()
    {
        var list = Assert.IsType<ListBlock>(Assert.Single(Spec.Parse("- a\n\n\n- b").Blocks));
        Assert.Equal(2, list.Items.Count);
        Assert.True(list.IsLoose);
    }

    [Theory]
    [InlineData("> quote\n# Heading", typeof(HeadingBlock))]
    [InlineData("> quote\n```\ncode\n```", typeof(CodeBlock))]
    [InlineData("> quote\n---", typeof(ThematicBreakBlock))]
    [InlineData("> quote\n$$\nx\n$$", typeof(MathBlock))]
    public void ABlockStartIsNotALazyQuoteContinuation(string source, Type following)
    {
        var document = Spec.Parse(source);

        Assert.Equal(2, document.Blocks.Count);
        Assert.IsType<QuoteBlock>(document.Blocks[0]);
        Assert.IsType(following, document.Blocks[1]);
    }
}

/// <summary>Findings 14, 15, 17, 18, 19: smaller parsing corrections.</summary>
public sealed class SmallCorrectionTests
{
    [Theory]
    [InlineData("## Using C#", "Using C#")]
    [InlineData("## C# and F# ##", "C# and F#")]
    [InlineData("# Title #", "Title")]
    public void OnlyASpaceSeparatedClosingSequenceIsStripped(string source, string expected) =>
        Assert.Equal(expected, Spec.Text(Spec.Parse(source).Blocks[0]));

    [Theory]
    [InlineData("-\titem")]
    [InlineData("1.\titem")]
    [InlineData("-   item")]
    public void TheMarkerContentStartsAfterTheFirstSpaceOrTab(string source)
    {
        var list = Assert.IsType<ListBlock>(Spec.Parse(source).Blocks[0]);
        Assert.Equal("item", Spec.Text(list.Items[0].Blocks[0]));
    }

    [Theory]
    [InlineData("arr[0] and x[12]")]
    [InlineData("a[1]")]
    [InlineData("[text][1]")]
    public void ABracketAfterAWordOrLabelIsNotACitation(string source) =>
        Assert.Empty(InlineParser.Parse(source).OfType<CitationMarkerInline>());

    [Theory]
    [InlineData("see [1] and [2]", 2)]
    [InlineData("see [1][2] here", 2)]
    [InlineData("see [1][2][3].", 3)]
    [InlineData("(see [4])", 1)]
    public void CitationMarkersAreRecognisedAfterSpacesPunctuationAndOtherMarkers(string source, int count) =>
        Assert.Equal(count, InlineParser.Parse(source).OfType<CitationMarkerInline>().Count());

    [Theory]
    [InlineData("see https://en.wikipedia.org/wiki/Foo_(bar) now", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("(see https://x.dev/a)", "https://x.dev/a")]
    [InlineData("https://x.dev/a(b)(c))", "https://x.dev/a(b)(c)")]
    public void BareUrlsBalanceTheirParentheses(string source, string url) =>
        Assert.Equal(url, InlineParser.Parse(source).OfType<LinkInline>().Single().Url);

    [Theory]
    [InlineData("[a](<http://x y>)", "http://x y", null)]
    [InlineData("[a](http://x \"title\")", "http://x", "title")]
    [InlineData("[a](http://x 'title')", "http://x", "title")]
    [InlineData("[a](<http://x> \"t\")", "http://x", "t")]
    public void LinkDestinationsMayBeBracketedAndTitled(string source, string url, string? title)
    {
        var link = Assert.IsType<LinkInline>(Assert.Single(InlineParser.Parse(source)));
        Assert.Equal(url, link.Url);
        Assert.Equal(title, link.Title);
    }
}

/// <summary>Finding 20: heading identifiers are prefixed and unique within a document.</summary>
public sealed class HeadingIdTests
{
    private static string? Id(MarkdownBlock block) => Assert.IsType<HeadingBlock>(block).Id;

    [Fact]
    public void IdentifiersCarryAPrefixSoTheyCannotClobberThePage() =>
        Assert.Equal("bai-h-hello-world", Id(Spec.Parse("# Hello World").Blocks[0]));

    [Fact]
    public void AnEmptyHeadingStillGetsAnIdentifier() =>
        Assert.Equal("bai-h-section", Id(Spec.Parse("#").Blocks[0]));

    [Fact]
    public void RepeatedHeadingsAreNumbered()
    {
        var document = Spec.Parse("## A\n\n## A\n\n## A\n\n## A 2");
        Assert.Equal(["bai-h-a", "bai-h-a-2", "bai-h-a-3", "bai-h-a-2-2"], document.Blocks.Select(Id));
    }

    [Fact]
    public void HeadingsInsideQuotesAndListsShareTheDocumentNamespace()
    {
        var document = Spec.Parse("> ## A\n\n- ## A\n\n## A");

        Assert.Equal("bai-h-a", Id(Assert.IsType<QuoteBlock>(document.Blocks[0]).Blocks[0]));
        Assert.Equal("bai-h-a-2", Id(Assert.IsType<ListBlock>(document.Blocks[1]).Items[0].Blocks[0]));
        Assert.Equal("bai-h-a-3", Id(document.Blocks[2]));
    }

    [Fact]
    public void SetextHeadingsAreNumberedToo()
    {
        var document = Spec.Parse("A\n===\n\nA\n---");
        Assert.Equal(["bai-h-a", "bai-h-a-2"], document.Blocks.Select(Id));
    }

    [Fact]
    public void TheIncrementalParserNumbersHeadingsAcrossTheStablePrefix() =>
        Spec.AssertIncrementalMatches("## A\n\ntext\n\n## A\n\nmore\n\n## A\n\nend");

    [Fact]
    public void IdentifiersCanBeTurnedOff() =>
        Assert.Null(Id(Spec.Parse("# A", new MarkdownOptions { HeadingIds = false }).Blocks[0]));
}

/// <summary>Finding 24: indented code blocks.</summary>
public sealed class IndentedCodeTests
{
    [Fact]
    public void FourSpacesAfterABlankLineOpenACodeBlock()
    {
        var document = Spec.Parse("para\n\n    code here\n    more\n\nafter");

        Assert.Equal(3, document.Blocks.Count);
        var code = Assert.IsType<CodeBlock>(document.Blocks[1]);
        Assert.False(code.IsFenced);
        Assert.Null(code.Language);
        Assert.Equal("code here\nmore", code.Code);
        Assert.Equal("after", Spec.Text(document.Blocks[2]));
    }

    [Fact]
    public void BlankLinesInsideAreKeptAndTrailingOnesAreNot()
    {
        var code = Assert.IsType<CodeBlock>(Assert.Single(Spec.Parse("    a\n\n    b\n\n\n").Blocks));
        Assert.Equal("a\n\nb", code.Code);
    }

    [Fact]
    public void ATabCountsAsFourColumns() =>
        Assert.Equal("code", Assert.IsType<CodeBlock>(Spec.Parse("\tcode").Blocks[0]).Code);

    [Fact]
    public void IndentedCodeCannotInterruptAParagraph()
    {
        var document = Spec.Parse("para\n    not code");
        Assert.Equal("para\nnot code", Spec.Text(Assert.Single(document.Blocks)));
    }

    [Fact]
    public void IndentedCodeWorksInsideAListItem()
    {
        var list = Assert.IsType<ListBlock>(Assert.Single(Spec.Parse("- item\n\n      code").Blocks));
        var item = Assert.Single(list.Items);

        Assert.Equal(2, item.Blocks.Count);
        Assert.Equal("code", Assert.IsType<CodeBlock>(item.Blocks[1]).Code);
    }

    [Fact]
    public void MarkdownInsideIndentedCodeIsNotParsed() =>
        Assert.Equal("# not a heading", Assert.IsType<CodeBlock>(Assert.Single(Spec.Parse("    # not a heading").Blocks)).Code);

    [Fact]
    public void AnIndentedBlockIsOpenWhileStreaming()
    {
        Assert.True(Spec.Parse("    code", complete: false).Blocks[0].IsOpen);
        Assert.False(Spec.Parse("    code\n\nafter", complete: false).Blocks[0].IsOpen);
    }

    [Fact]
    public void TheIncrementalParserAgrees() =>
        Spec.AssertIncrementalMatches("para\n\n    code\n\n    more\n\nafter\n\n    again\n\nend");
}

/// <summary>Finding 24: link reference definitions and reference links.</summary>
public sealed class ReferenceLinkTests
{
    [Fact]
    public void AFullReferenceResolvesAgainstADefinitionThatFollowsIt()
    {
        var document = Spec.Parse("See [the docs][ref] now.\n\n[ref]: https://x.dev/docs \"Docs\"");

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        var link = paragraph.Inlines.OfType<LinkInline>().Single();
        Assert.Equal("https://x.dev/docs", link.Url);
        Assert.Equal("Docs", link.Title);
        Assert.Equal("the docs", Spec.Text(link.Inlines));
    }

    [Theory]
    [InlineData("[ref][]\n\n[ref]: /u")]
    [InlineData("[ref]\n\n[ref]: /u")]
    [InlineData("[Ref]\n\n[ref]: /u")]
    [InlineData("[ref]: /u\n\n[ref]")]
    [InlineData("[ref]: </u>\n\n[ref]")]
    [InlineData("[ref]: /u 'Title'\n\n[ref]")]
    [InlineData("[ref]: /u (Title)\n\n[ref]")]
    [InlineData("[ref]:   /u   \n\n[ref]")]
    [InlineData("[  ref  ]\n\n[ref]: /u")]
    public void CollapsedAndShortcutReferencesResolveCaseInsensitively(string source)
    {
        var document = Spec.Parse(source);
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal("/u", paragraph.Inlines.OfType<LinkInline>().Single().Url);
    }

    [Fact]
    public void TheFirstDefinitionOfALabelWins()
    {
        var document = Spec.Parse("[ref]: /a\n[ref]: /b\n\n[ref]");
        Assert.Equal("/a", Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks)).Inlines.OfType<LinkInline>().Single().Url);
    }

    [Theory]
    [InlineData("[text][missing]")]
    [InlineData("[text][missing]\n\n[other]: /u")]
    [InlineData("[text]")]
    public void AnUndefinedReferenceIsText(string source)
    {
        var document = Spec.Parse(source);
        Assert.Empty(Assert.IsType<ParagraphBlock>(document.Blocks[0]).Inlines.OfType<LinkInline>());
        Assert.Equal(source.Split("\n\n")[0], Spec.Text(document.Blocks[0]));
    }

    [Fact]
    public void AFullReferenceWithAnUndefinedLabelDoesNotFallBackToAShortcut()
    {
        var document = Spec.Parse("[ref][missing]\n\n[ref]: /u");
        Assert.Empty(Assert.IsType<ParagraphBlock>(document.Blocks[0]).Inlines.OfType<LinkInline>());
    }

    [Fact]
    public void ADefinitionIsNotABlockOfItsOwn()
    {
        var document = Spec.Parse("[ref]: /u\n\ntext");
        Assert.Equal("text", Spec.Text(Assert.Single(document.Blocks)));
    }

    [Fact]
    public void ADefinitionCannotInterruptAParagraph()
    {
        var document = Spec.Parse("Sources:\n[1]: https://x.dev");
        Assert.Equal("Sources:\n[1]: https://x.dev", Spec.Text(Assert.Single(document.Blocks)));
    }

    [Fact]
    public void ImagesMayUseReferences()
    {
        var document = Spec.Parse("![alt][img]\n\n[img]: a.png \"A\"");
        var image = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks)).Inlines.OfType<ImageInline>().Single();

        Assert.Equal("a.png", image.Url);
        Assert.Equal("alt", image.Alt);
        Assert.Equal("A", image.Title);
    }

    [Fact]
    public void ANumericLabelWithADefinitionIsALinkNotACitation()
    {
        var document = Spec.Parse("see [1]\n\n[1]: https://x.dev");
        var inlines = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks)).Inlines;

        Assert.Empty(inlines.OfType<CitationMarkerInline>());
        Assert.Equal("https://x.dev", inlines.OfType<LinkInline>().Single().Url);
    }

    [Theory]
    [InlineData("[ref]: ")]
    [InlineData("[ref]: /u \"unterminated")]
    [InlineData("[ref]: /u extra words")]
    [InlineData("[]: /u")]
    [InlineData("[a [b]]: /u")]
    public void AMalformedDefinitionIsAParagraph(string source) =>
        Assert.IsType<ParagraphBlock>(Assert.Single(Spec.Parse(source).Blocks));

    [Fact]
    public void TheIncrementalParserAgreesEvenWhenTheDefinitionArrivesLast() =>
        Spec.AssertIncrementalMatches("Intro [a][x].\n\nMore [b][y] text.\n\n[x]: /x\n[y]: /y \"Y\"\n\nend");
}

/// <summary>Finding 24: footnotes.</summary>
public sealed class FootnoteTests
{
    [Fact]
    public void FootnotesAreNumberedInTheOrderTheyAreFirstCited()
    {
        var document = Spec.Parse("Text[^b] and more[^a].\n\n[^a]: Note A.\n[^b]: Note B.");

        Assert.Single(document.Blocks);
        Assert.Equal(2, document.Footnotes.Count);
        Assert.Equal("b", document.Footnotes[0].Label);
        Assert.Equal(1, document.Footnotes[0].Number);
        Assert.Equal("Note B.", Spec.Text(document.Footnotes[0].Blocks[0]));
        Assert.Equal("a", document.Footnotes[1].Label);
        Assert.Equal(2, document.Footnotes[1].Number);

        var references = Assert.IsType<ParagraphBlock>(document.Blocks[0]).Inlines.OfType<FootnoteReferenceInline>().ToList();
        Assert.Equal([1, 2], references.Select(r => r.Number));
    }

    [Fact]
    public void ACitedFootnoteCountsItsReferences()
    {
        var document = Spec.Parse("a[^1] b[^1]\n\n[^1]: n");
        var footnote = Assert.Single(document.Footnotes);
        var references = Assert.IsType<ParagraphBlock>(document.Blocks[0]).Inlines.OfType<FootnoteReferenceInline>().ToList();

        Assert.Equal(2, footnote.ReferenceCount);
        Assert.Equal([1, 2], references.Select(r => r.Ordinal));
    }

    [Fact]
    public void AnUncitedDefinitionIsDroppedAndAnUndefinedReferenceIsText()
    {
        var document = Spec.Parse("text[^x]\n\n[^y]: gone");

        Assert.Empty(document.Footnotes);
        Assert.Equal("text[^x]", Spec.Text(Assert.Single(document.Blocks)));
        Assert.Empty(Assert.IsType<ParagraphBlock>(document.Blocks[0]).Inlines.OfType<FootnoteReferenceInline>());
    }

    [Fact]
    public void AFootnoteBodyMaySpanIndentedLinesAndParagraphs()
    {
        var document = Spec.Parse("x[^1]\n\n[^1]: line one\n    line two\n\n    para two\n\nafter");

        Assert.Equal(["x[^1]", "after"], document.Blocks.Select(Spec.Text));
        var footnote = Assert.Single(document.Footnotes);
        Assert.Equal(["line one\nline two", "para two"], footnote.Blocks.Select(Spec.Text));
    }

    [Fact]
    public void AFootnoteMayCiteAnotherFootnote()
    {
        var document = Spec.Parse("x[^1]\n\n[^1]: see[^2]\n[^2]: end");

        Assert.Equal(2, document.Footnotes.Count);
        Assert.Equal("2", document.Footnotes[1].Label);
    }

    [Fact]
    public void FootnotesCanBeTurnedOff()
    {
        var document = Spec.Parse("x[^1]\n\n[^1]: n", new MarkdownOptions { Footnotes = false });

        Assert.Empty(document.Footnotes);
        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal("[^1]: n", Spec.Text(document.Blocks[1]));
    }

    [Fact]
    public void TheIncrementalParserAgreesAtEveryPrefix() =>
        Spec.AssertIncrementalMatches("Intro[^a].\n\nMore[^b] and again[^a].\n\n[^a]: Note A\n[^b]: Note B\n    continued\n\nend");
}

/// <summary>Finding 24: soft breaks and dangling backticks.</summary>
public sealed class LineBreakAndBacktickTests
{
    [Fact]
    public void SoftBreaksAreSoftByDefaultAndHardWhenAsked()
    {
        Assert.False(InlineParser.Parse("a\nb").OfType<LineBreakInline>().Single().IsHard);
        Assert.True(InlineParser.Parse("a\nb", new MarkdownOptions { SoftBreaksAsHardBreaks = true }).OfType<LineBreakInline>().Single().IsHard);
        Assert.True(InlineParser.Parse("a  \nb").OfType<LineBreakInline>().Single().IsHard);
    }

    [Fact]
    public void TheOptionReachesParagraphsThroughTheBlockParser()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Spec.Parse("a\nb", new MarkdownOptions { SoftBreaksAsHardBreaks = true }).Blocks[0]);
        Assert.True(paragraph.Inlines.OfType<LineBreakInline>().Single().IsHard);
    }

    [Theory]
    [InlineData("code `half", "code half")]
    [InlineData("`a` and `b", "a and b")]
    [InlineData("``still", "still")]
    public void AnUnmatchedBacktickRunIsHiddenWhileStreaming(string source, string expected) =>
        Assert.Equal(expected, Spec.Text(InlineParser.Parse(source, isComplete: false)));

    [Theory]
    [InlineData("code `half", "{ParagraphBlock(\"code `half\")}")]
    [InlineData("`a` and `b", "{ParagraphBlock(code[a]\" and `b\")}")]
    public void AnUnmatchedBacktickRunIsLiteralOnceComplete(string source, string expected) =>
        Assert.Equal(expected, Spec.InlineDump(source));

    [Fact]
    public void HidingCanBeTurnedOffForTheWholeDocument()
    {
        var options = new MarkdownOptions { HideDanglingDelimiters = false };
        var paragraph = Assert.IsType<ParagraphBlock>(Spec.Parse("code `half **bold", options, complete: false).Blocks[0]);
        Assert.Equal("code `half **bold", Spec.Text(paragraph.Inlines));
    }
}

/// <summary>Finding 25: raw HTML is recognised only when it is shaped like HTML.</summary>
public sealed class RawHtmlShapeTests
{
    private static readonly MarkdownOptions Html = new() { AllowRawHtml = true };

    [Theory]
    [InlineData("a < b > c")]
    [InlineData("x <3 you>")]
    [InlineData("<3>")]
    [InlineData("< div>")]
    [InlineData("<-->")]
    public void SomethingThatIsNotATagIsText(string source) =>
        Assert.Empty(InlineParser.Parse(source, Html).OfType<RawHtmlInline>());

    [Theory]
    [InlineData("<b>x</b>", 2)]
    [InlineData("<br/>", 1)]
    [InlineData("<span class=\"x\">y</span>", 2)]
    [InlineData("<!-- note -->", 1)]
    [InlineData("<?php ?>", 1)]
    [InlineData("<my-element>", 1)]
    public void TagsCommentsAndDeclarationsAreHtml(string source, int count) =>
        Assert.Equal(count, InlineParser.Parse(source, Html).OfType<RawHtmlInline>().Count());

    [Theory]
    [InlineData("<div>\nx\n</div>")]
    [InlineData("<!-- a -->")]
    [InlineData("</div>")]
    [InlineData("<!DOCTYPE html>")]
    public void ABlockStartsWithATag(string source) =>
        Assert.IsType<HtmlBlock>(Assert.Single(MarkdownParser.Parse(source, Html).Blocks));

    [Theory]
    [InlineData("< div")]
    [InlineData("<3 is less than 4")]
    [InlineData("<-- not a comment")]
    public void ABlockDoesNotStartWithSomethingThatOnlyLooksLikeOne(string source) =>
        Assert.IsType<ParagraphBlock>(Assert.Single(MarkdownParser.Parse(source, Html).Blocks));

    [Fact]
    public void ABlockLevelTagInterruptsAParagraphButAnInlineTagDoesNot()
    {
        Assert.Equal(2, MarkdownParser.Parse("text\n<div>\nx", Html).Blocks.Count);
        Assert.Single(MarkdownParser.Parse("text\n<b>x</b>", Html).Blocks);
    }

    [Fact]
    public void NothingIsHtmlWhenRawHtmlIsOff()
    {
        Assert.Empty(InlineParser.Parse("<b>x</b>").OfType<RawHtmlInline>());
        Assert.IsType<ParagraphBlock>(Assert.Single(MarkdownParser.Parse("<div>\nx\n</div>").Blocks));
    }
}

/// <summary>Finding 25: highlighter grammars.</summary>
public sealed class HighlighterGrammarTests
{
    private static string[] Texts(string code, string language, TokenKind kind) =>
        SyntaxHighlighter.Highlight(code, language).Where(t => t.Kind == kind).Select(t => t.Slice(code).ToString()).ToArray();

    [Fact]
    public void PowerShellHasItsOwnGrammar()
    {
        const string code = "Get-ChildItem -Path $env:HOME -Recurse | Where-Object { $_.Length -gt 100 } # list\n<# block\ncomment #>\n$Name = \"a`\"b\"";

        Assert.Equal(["Get-ChildItem", "Where-Object"], Texts(code, "powershell", TokenKind.Function));
        Assert.Equal(["-Path", "-Recurse"], Texts(code, "powershell", TokenKind.Attribute));
        Assert.Equal(["$env:HOME", "$_", "$Name"], Texts(code, "powershell", TokenKind.Variable));
        Assert.Contains("-gt", Texts(code, "powershell", TokenKind.Keyword));
        Assert.Equal(["# list", "<# block\ncomment #>"], Texts(code, "powershell", TokenKind.Comment));
        Assert.Equal(["\"a`\"b\""], Texts(code, "powershell", TokenKind.String));
    }

    [Theory]
    [InlineData("ps1")]
    [InlineData("pwsh")]
    [InlineData("PowerShell")]
    public void PowerShellAliasesResolveToPowerShellNotBash(string alias) =>
        Assert.Equal("powershell", LanguageRegistry.Find(alias)?.Id);

    [Fact]
    public void BashStillHighlightsItsOwnKeywords() =>
        Assert.Contains("echo", Texts("if true; then echo hi; fi", "bash", TokenKind.Keyword));

    [Fact]
    public void JavaScriptTemplateLiteralsSpanLines()
    {
        const string code = "const s = `line one\n${x} line two`;\nlet y = 1;";
        Assert.Equal(["`line one\n${x} line two`"], Texts(code, "typescript", TokenKind.String));
        Assert.Contains("let", Texts(code, "typescript", TokenKind.Keyword));
    }

    [Fact]
    public void PythonTripleQuotedStringsSpanLinesButSingleQuotedOnesDoNot()
    {
        const string code = "s = '''a\nb'''\nt = 'c\nd = 1";
        var strings = Texts(code, "python", TokenKind.String);

        Assert.Contains("'''a\nb'''", strings);
        Assert.Contains("'c", strings);
        Assert.Contains("1", Texts(code, "python", TokenKind.Number));
    }
}

/// <summary>Every construct, in random combination, must parse identically incrementally and in one shot.</summary>
public sealed class IncrementalPropertyTests
{
    public static readonly string[] Fragments =
    [
        "Plain *text* here.", "Two\nlines", "# Heading", "## A", "## A\n\n## A", "Setext\n---", "***", "- a\n- b", "- a\n\n- b",
        "1. one\n2. two", "10) ten", "- a\n\n  continued", "> quote\n> more", "> lazy\nline", "- a\n---", "- a\n - b\n\n  text",
        "```js\nlet a;\n\nlet b;\n```", "~~~\ntilde\n\n~~~", "```inline``` text", "$$\nx\n\ny\n$$", "$$x$$", "- a\n$$\nm\n$$",
        "Text:\n$$\nz\n$$", "| a | b |\n|---|---|\n| 1 | 2 |", "- a\n| x | y |\n|--|--|\n| 1 | 2 |", "  indented", "    code", "para\n\n    code\n\n    more",
        "-", "1.", "**bold", "- [x] done", "**bold **text", "***both*** after", "*a **b** c*", "costs $5 and $10", "AT&amp;T &copy; &#39;",
        "arr[0] [1][2] and [3]", "a\\\nb", "`half", "`a` and `b", "- a\n  ```\n  code", "```\nz\n\nw\n```", "- a\n```\nb",
        "text\n$$ not math\nmore", "$$", "end $$", "* star\n+ plus\n- dash", "1. one\n2) two", "<div>\n```\n</div>", "<b>x</b> a < b", "> ```\n> q",
        "[text][ref]", "[ref]: https://x.dev \"T\"", "[ref][]", "[ref]", "![alt][img]", "[img]: a.png", "x[^1]", "[^1]: note\n    more\n\n    para",
        "y[^a] z[^1]", "[^a]: a note", "Sources:\n[1]: https://x.dev", "see [1]", "[1]: /one",
    ];

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RandomDocumentsMatchAOneShotParseAtEveryPrefix(bool allowRawHtml, bool hardBreaks)
    {
        var options = new MarkdownOptions { AllowRawHtml = allowRawHtml, SoftBreaksAsHardBreaks = hardBreaks };
        var random = new Random(4321);

        for (var round = 0; round < 250; round++)
        {
            var parts = Enumerable.Range(0, random.Next(2, 7)).Select(_ => Fragments[random.Next(Fragments.Length)]);
            var source = string.Join(random.Next(3) == 0 ? "\n" : "\n\n", parts) + (random.Next(2) == 0 ? "\n" : string.Empty);
            Spec.AssertIncrementalMatches(source, options);
        }
    }

    [Fact]
    public void EveryFragmentOnItsOwnMatchesAOneShotParse()
    {
        foreach (var fragment in Fragments) Spec.AssertIncrementalMatches(fragment);
        foreach (var fragment in Fragments) Spec.AssertIncrementalMatches(fragment, new MarkdownOptions { AllowRawHtml = true });
    }
}
