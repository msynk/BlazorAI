using System.Text;

namespace Blazor.AI.Markdown.Tests;

/// <summary>Renders a parsed tree to a canonical string so two parses can be compared exactly.</summary>
internal static class TreeDump
{
    public static string Of(MarkdownDocument document)
    {
        var builder = new StringBuilder();
        foreach (var block in document.Blocks) Block(block, builder);
        return builder.ToString();
    }

    private static void Block(MarkdownBlock block, StringBuilder b)
    {
        b.Append('{').Append(block.GetType().Name).Append(block.IsOpen ? " open" : string.Empty);
        switch (block)
        {
            case ParagraphBlock p: Inlines(p.Inlines, b); break;
            case HeadingBlock h: b.Append(' ').Append(h.Level).Append(' ').Append(h.Id); Inlines(h.Inlines, b); break;
            case CodeBlock c: b.Append(' ').Append(c.Language).Append(" [").Append(c.Code).Append(']'); break;
            case MathBlock m: b.Append(" [").Append(m.Expression).Append(']'); break;
            case HtmlBlock html: b.Append(" [").Append(html.Html).Append(']'); break;
            case QuoteBlock q: foreach (var child in q.Blocks) Block(child, b); break;
            case ListBlock l:
                b.Append(l.IsOrdered ? " ol" : " ul").Append(l.IsLoose ? " loose" : string.Empty);
                foreach (var item in l.Items) Block(item, b);
                break;
            case ListItemBlock item:
                b.Append(' ').Append(item.IsChecked);
                foreach (var child in item.Blocks) Block(child, b);
                break;
            case TableBlock t:
                foreach (var cell in t.Header) Inlines(cell, b);
                foreach (var row in t.Rows) foreach (var cell in row) Inlines(cell, b);
                break;
        }

        b.Append('}');
    }

    private static void Inlines(IEnumerable<MarkdownInline> inlines, StringBuilder b)
    {
        b.Append('(');
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextInline t: b.Append('"').Append(t.Text).Append('"'); break;
                case EmphasisInline e: b.Append(e.IsStrong ? "strong" : "em"); Inlines(e.Inlines, b); break;
                case StrikethroughInline s: b.Append("del"); Inlines(s.Inlines, b); break;
                case LinkInline l: b.Append("a[").Append(l.Url).Append(']'); Inlines(l.Inlines, b); break;
                case CodeInline c: b.Append("code[").Append(c.Code).Append(']'); break;
                case MathInline m: b.Append("math[").Append(m.Expression).Append(']'); break;
                default: b.Append(inline.GetType().Name); break;
            }
        }

        b.Append(')');
    }
}

public sealed class BlockRegressionTests
{
    private static MarkdownDocument Parse(string source, bool complete = true) =>
        MarkdownParser.Parse(source, isComplete: complete);

    private static string Text(IEnumerable<MarkdownInline> inlines) =>
        string.Concat(inlines.Select(i => i switch
        {
            TextInline t => t.Text,
            EmphasisInline e => Text(e.Inlines),
            LinkInline l => Text(l.Inlines),
            CodeInline c => c.Code,
            LineBreakInline => "\n",
            _ => string.Empty,
        }));

    [Theory]
    [InlineData("$$$")]
    [InlineData("$$$\n")]
    [InlineData("> $$$")]
    public void ThreeDollarSignsDoNotThrow(string source) => Parse(source);

    [Fact]
    public void ContentOnTheOpeningMathLineIsKept()
    {
        var math = Assert.IsType<MathBlock>(Parse("$$ a + b\n= c\n$$").Blocks[0]);
        Assert.Equal("a + b\n= c", math.Expression);
    }

    [Fact]
    public void DisplayMathInterruptsAParagraph()
    {
        var document = Parse("The formula:\n$$\nE = mc^2\n$$\nafter");

        Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        Assert.Equal("E = mc^2", Assert.IsType<MathBlock>(document.Blocks[1]).Expression);
    }

    [Theory]
    [InlineData("## Using C#", "Using C#")]
    [InlineData("# F# and C#", "F# and C#")]
    [InlineData("# Title ##", "Title")]
    [InlineData("# Title #####   ", "Title")]
    [InlineData("# #", "")]
    public void OnlyASpaceSeparatedClosingSequenceIsStrippedFromAHeading(string source, string expected)
    {
        var heading = Assert.IsType<HeadingBlock>(Parse(source).Blocks[0]);
        Assert.Equal(expected, Text(heading.Inlines));
    }

    [Fact]
    public void AClosingFenceMayHaveTrailingWhitespace()
    {
        var document = Parse("```\ncode\n```   \n\nafter");

        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal("code", Assert.IsType<CodeBlock>(document.Blocks[0]).Code);
        Assert.IsType<ParagraphBlock>(document.Blocks[1]);
    }

    [Fact]
    public void ATabAfterAListMarkerKeepsTheItemText()
    {
        var list = Assert.IsType<ListBlock>(Parse("-\tfirst\n-\tsecond").Blocks[0]);
        Assert.Equal("first", Text(Assert.IsType<ParagraphBlock>(list.Items[0].Blocks[0]).Inlines));
    }

    [Fact]
    public void TabIndentedContinuationLinesAreNotTruncated()
    {
        var list = Assert.IsType<ListBlock>(Parse("- Run:\n\t```go\n\tfunc main() {\n\t\tfmt.Println()\n\t}\n\t```").Blocks[0]);
        var code = Assert.IsType<CodeBlock>(list.Items[0].Blocks[1]);

        Assert.Equal("go", code.Language);
        Assert.Contains("func main() {", code.Code);
        Assert.Contains("fmt.Println()", code.Code);
    }

    [Theory]
    [InlineData("text\n***\nmore")]
    [InlineData("text\n___\nmore")]
    public void AThematicBreakInterruptsAParagraph(string source)
    {
        var document = Parse(source);
        Assert.Collection(
            document.Blocks,
            b => Assert.IsType<ParagraphBlock>(b),
            b => Assert.IsType<ThematicBreakBlock>(b),
            b => Assert.IsType<ParagraphBlock>(b));
    }

    [Fact]
    public void TwoMarkersAreNotAThematicBreak()
    {
        Assert.IsNotType<ThematicBreakBlock>(Parse("- -").Blocks[0]);
    }

    [Theory]
    [InlineData("- a\n---")]
    [InlineData("- a\n***")]
    [InlineData("- a\n* * *")]
    public void AThematicBreakEndsAList(string source)
    {
        var document = Parse(source);
        Assert.Equal(2, document.Blocks.Count);
        Assert.Single(Assert.IsType<ListBlock>(document.Blocks[0]).Items);
        Assert.IsType<ThematicBreakBlock>(document.Blocks[1]);
    }

    [Fact]
    public void ABlankLineBeforeADifferentKindOfListDoesNotMakeTheFirstLoose()
    {
        var document = Parse("- a\n- b\n\n1. c");
        Assert.False(Assert.IsType<ListBlock>(document.Blocks[0]).IsLoose);
    }

    [Theory]
    [InlineData("> note\n# Heading")]
    [InlineData("> note\n```\ncode\n```")]
    [InlineData("> note\n- item")]
    public void ABlockStartIsNotALazyQuoteContinuation(string source)
    {
        var document = Parse(source);
        Assert.Equal(2, document.Blocks.Count);
        Assert.Single(Assert.IsType<QuoteBlock>(document.Blocks[0]).Blocks);
    }

    [Fact]
    public void AFinishedListIsNotOpenInsideWhileStreaming()
    {
        var document = Parse("- a *b\n\nnext", complete: false);
        var list = Assert.IsType<ListBlock>(document.Blocks[0]);
        var paragraph = Assert.IsType<ParagraphBlock>(list.Items[0].Blocks[0]);

        Assert.False(paragraph.IsOpen);
        Assert.Equal("a *b", Text(paragraph.Inlines));
    }

    [Theory]
    [InlineData('>', 1)]
    [InlineData('>', 2)]
    public void DeeplyNestedBlocksDoNotOverflowTheStack(char marker, int width)
    {
        var line = string.Concat(Enumerable.Repeat(marker + new string(' ', width - 1), 50_000)) + "x";
        Assert.NotEmpty(Parse(line).Blocks);
    }

    [Fact]
    public void DeeplyNestedListMarkersDoNotOverflowTheStack()
    {
        var line = string.Concat(Enumerable.Repeat("1. ", 50_000)) + "x";
        Assert.NotEmpty(Parse(line).Blocks);
    }

    [Theory]
    [InlineData("- a\n* b\n+ c", 3)]
    [InlineData("- a\n- b\n* c", 2)]
    [InlineData("- a\n\n* b", 2)]
    [InlineData("1. a\n2) b", 2)]
    [InlineData("1) a\n\n2. b", 2)]
    [InlineData("- a\n1. b", 2)]
    public void ChangingTheBulletOrDelimiterStartsANewList(string source, int lists)
    {
        var document = Parse(source);

        Assert.Equal(lists, document.Blocks.Count);
        Assert.All(document.Blocks, b => Assert.False(Assert.IsType<ListBlock>(b).IsLoose));
    }

    [Fact]
    public void AListKeepsItsItemsWhenTheBulletStaysTheSame()
    {
        Assert.Equal(3, Assert.IsType<ListBlock>(Assert.Single(Parse("* a\n* b\n\n* c").Blocks)).Items.Count);
        Assert.Equal(2, Assert.IsType<ListBlock>(Assert.Single(Parse("1) a\n2) b").Blocks)).Items.Count);
    }
}

public sealed class InlineRegressionTests
{
    private static List<MarkdownInline> Parse(string source, bool complete = true) =>
        InlineParser.Parse(source, isComplete: complete);

    private static string Dump(string source, bool complete = true) =>
        TreeDump.Of(Doc(Parse(source, complete)));

    private static MarkdownDocument Doc(List<MarkdownInline> inlines)
    {
        var document = new MarkdownDocument();
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.AddRange(inlines);
        document.Blocks.Add(paragraph);
        return document;
    }

    [Theory]
    [InlineData("***both***", "{ParagraphBlock(em(strong(\"both\")))}")]
    [InlineData("a ***b*** c", "{ParagraphBlock(\"a \"em(strong(\"b\"))\" c\")}")]
    [InlineData("*a **b** c*", "{ParagraphBlock(em(\"a \"strong(\"b\")\" c\"))}")]
    [InlineData("**a *b* c**", "{ParagraphBlock(strong(\"a \"em(\"b\")\" c\"))}")]
    [InlineData("*foo**bar**baz*", "{ParagraphBlock(em(\"foo\"strong(\"bar\")\"baz\"))}")]
    [InlineData("**2*3 is six**", "{ParagraphBlock(strong(\"2*3 is six\"))}")]
    [InlineData("2*3*4", "{ParagraphBlock(\"2\"em(\"3\")\"4\")}")]
    [InlineData("**bold*", "{ParagraphBlock(\"*\"em(\"bold\"))}")]
    [InlineData("*it**", "{ParagraphBlock(em(\"it\")\"*\")}")]
    [InlineData("****", "{ParagraphBlock(\"****\")}")]
    [InlineData("**a **b**", "{ParagraphBlock(\"**a \"strong(\"b\"))}")]
    [InlineData("**bold** and *it*", "{ParagraphBlock(strong(\"bold\")\" and \"em(\"it\"))}")]
    [InlineData("snake_case_name", "{ParagraphBlock(\"snake_case_name\")}")]
    [InlineData("_under_ score", "{ParagraphBlock(em(\"under\")\" score\")}")]
    public void EmphasisRunsAreMatchedSensibly(string source, string expected) =>
        Assert.Equal(expected, Dump(source));

    [Theory]
    [InlineData("**")]
    [InlineData("**bold*")]
    [InlineData("***bold**")]
    [InlineData("text **")]
    public void APartialCloserIsHiddenWhileStreaming(string source) =>
        Assert.DoesNotContain("*", Dump(source, complete: false));

    [Fact]
    public void ContentInsideAClosedSpanIsNotTreatedAsStreaming()
    {
        Assert.Equal("{ParagraphBlock(strong(\"2*3 is six\")\" and more\")}", Dump("**2*3 is six** and more", complete: false));
        Assert.Equal("{ParagraphBlock(a[u](\"a *b\"))}", Dump("[a *b](u)", complete: false));
    }

    [Fact]
    public void AnUnmatchedBacktickRunIsLiteralAsAWhole()
    {
        Assert.Empty(Parse("``foo`").OfType<CodeInline>());
        Assert.Equal("{ParagraphBlock(\"``foo\"code[and]\"x`\")}", Dump("``foo` and `x`"));
    }

    [Theory]
    [InlineData("It costs $5 and $10 today.")]
    [InlineData("Between $5 and $10.")]
    [InlineData("$a $")]
    public void DollarAmountsAreNotMath(string source) =>
        Assert.Empty(Parse(source).OfType<MathInline>());

    [Fact]
    public void AnAngleAutoLinkWithAnAtSignAndASchemeIsNotMailTo()
    {
        Assert.Equal("https://medium.com/@user", Parse("<https://medium.com/@user>").OfType<LinkInline>().Single().Url);
        Assert.Equal("mailto:a@b.com", Parse("<a@b.com>").OfType<LinkInline>().Single().Url);
        Assert.Empty(Parse("<a\nb:c>").OfType<LinkInline>());
    }

    [Theory]
    [InlineData("see https://en.wikipedia.org/wiki/Foo_(bar) now", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("(see https://en.wikipedia.org/wiki/Foo_(bar))", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("(https://example.com)", "https://example.com")]
    [InlineData("(https://example.com).", "https://example.com")]
    public void BareUrlsKeepBalancedParentheses(string source, string expected) =>
        Assert.Equal(expected, Parse(source).OfType<LinkInline>().Single().Url);

    [Fact]
    public void ALinkWhoseTextIsAUrlDoesNotNestALink()
    {
        var link = Parse("[https://example.com](https://example.com)").OfType<LinkInline>().Single();
        Assert.Empty(link.Inlines.OfType<LinkInline>());
        Assert.Equal("https://example.com", Assert.IsType<TextInline>(Assert.Single(link.Inlines)).Text);
    }

    [Fact]
    public void DeeplyNestedInlinesDoNotOverflowTheStack()
    {
        var links = string.Concat(Enumerable.Repeat("[", 30_000)) + "x" + string.Concat(Enumerable.Repeat("](u)", 30_000));
        Assert.NotEmpty(Parse(links));
    }

    [Theory]
    // A link cannot contain a link, so the innermost one wins and the outer brackets are text.
    [InlineData("[a [b](c) d](e)", "{ParagraphBlock(\"[a \"a[c](\"b\")\" d](e)\")}")]
    // Links bind more tightly than emphasis, and code spans more tightly than either.
    [InlineData("*[a*](u)", "{ParagraphBlock(\"*\"a[u](\"a*\"))}")]
    [InlineData("[`](u)`", "{ParagraphBlock(\"[\"code[](u)])}")]
    [InlineData("**`a**`**", "{ParagraphBlock(strong(code[a**]))}")]
    // An image may sit inside a link, as badges do.
    [InlineData("[![build](b.svg)](u)", "{ParagraphBlock(a[u](ImageInline))}")]
    // Autolinks inside a link's text stay text rather than nesting an anchor in an anchor.
    [InlineData("[see <https://x.dev>](u)", "{ParagraphBlock(a[u](\"see <https://x.dev>\"))}")]
    [InlineData("[go to www.x.dev](u)", "{ParagraphBlock(a[u](\"go to www.x.dev\"))}")]
    // A bare URL does not swallow the delimiters around it.
    [InlineData("**https://x.dev**", "{ParagraphBlock(strong(a[https://x.dev](\"https://x.dev\")))}")]
    [InlineData("_see https://x.dev/a_b_", "{ParagraphBlock(em(\"see \"a[https://x.dev/a_b](\"https://x.dev/a_b\")))}")]
    // Strikethrough pairs runs of exactly two tildes, so a run of three is text.
    [InlineData("~~gone~~ and ~~~kept~~~", "{ParagraphBlock(del(\"gone\")\" and ~~~kept~~~\")}")]
    [InlineData("**a ~~b** c~~", "{ParagraphBlock(strong(\"a ~~b\")\" c~~\")}")]
    public void InlineSpansFollowCommonMarkPrecedence(string source, string expected) =>
        Assert.Equal(expected, Dump(source));

    [Theory]
    // What remains of an opener may still close, so it is hidden too.
    [InlineData("***a* b", "{ParagraphBlock(em(\"a\")\" b\")}")]
    [InlineData("a *b c", "{ParagraphBlock(\"a b c\")}")]
    [InlineData("~~gone so far", "{ParagraphBlock(\"gone so far\")}")]
    [InlineData("almost ~", "{ParagraphBlock(\"almost \")}")]
    [InlineData("_a__", "{ParagraphBlock(em(\"a\"))}")]
    // An underscore ending a word is far more likely the middle of snake_case than a delimiter.
    [InlineData("snake_", "{ParagraphBlock(\"snake_\")}")]
    // Delimiters inside a closed span are literal.
    [InlineData("~~a *b~~ c", "{ParagraphBlock(del(\"a *b\")\" c\")}")]
    [InlineData("[*a](u) *b", "{ParagraphBlock(a[u](\"*a\")\" b\")}")]
    public void UnmatchedDelimitersAreHiddenOnlyOutsideClosedSpansWhileStreaming(string source, string expected) =>
        Assert.Equal(expected, Dump(source, complete: false));

    [Fact]
    public void UnmatchedDelimitersAreLiteralOnceComplete()
    {
        Assert.Equal("{ParagraphBlock(\"**\"em(\"a\")\" b\")}", Dump("***a* b"));
        Assert.Equal("{ParagraphBlock(\"~~gone so far\")}", Dump("~~gone so far"));
        Assert.Equal("{ParagraphBlock(\"almost ~\")}", Dump("almost ~"));
    }

    [Theory]
    [InlineData("\\é", "\\é")]
    [InlineData("\\a", "\\a")]
    [InlineData("\\1", "\\1")]
    [InlineData("\\—", "\\—")]
    [InlineData("\\«quote»", "\\«quote»")]
    [InlineData("\\→", "\\→")]
    [InlineData("\\\\", "\\")]
    [InlineData("\\*not em\\*", "*not em*")]
    public void OnlyAsciiPunctuationCanBeEscaped(string source, string expected) =>
        Assert.Equal($"{{ParagraphBlock(\"{expected}\")}}", Dump(source));

    [Fact]
    public void EveryAsciiPunctuationCharacterCanBeEscaped()
    {
        foreach (var c in "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~")
        {
            Assert.Equal($"{{ParagraphBlock(\"{c}\")}}", Dump("\\" + c));
        }
    }

    [Theory]
    [InlineData("[a](u \"t\")", "u", "t")]
    [InlineData("[a](u 't')", "u", "t")]
    [InlineData("[a](u (t))", "u", "t")]
    [InlineData("[a](u\n  \"t\"  )", "u", "t")]
    [InlineData("[a](<my file.md> 't')", "my file.md", "t")]
    [InlineData("[a](u \"say \\\"hi\\\"\")", "u", "say \"hi\"")]
    [InlineData("[a](u 'it\\'s')", "u", "it's")]
    [InlineData("[a](https://x.dev/it's)", "https://x.dev/it's", null)]
    [InlineData("[a](https://en.wikipedia.org/wiki/Foo_(bar))", "https://en.wikipedia.org/wiki/Foo_(bar)", null)]
    [InlineData("[a](Q3 summary.pdf)", "Q3 summary.pdf", null)]
    [InlineData("[a](u\\)v)", "u)v", null)]
    [InlineData("[a](u\\é)", "u\\é", null)]
    [InlineData("[a]()", "", null)]
    public void LinkDestinationsAndTitlesFollowCommonMark(string source, string url, string? title)
    {
        var link = Assert.IsType<LinkInline>(Assert.Single(Parse(source)));

        Assert.Equal(url, link.Url);
        Assert.Equal(title, link.Title);
    }

    [Theory]
    [InlineData("![cat](c.png 'A cat')", "c.png", "A cat")]
    [InlineData("![cat](c.png (A cat))", "c.png", "A cat")]
    public void ImageTitlesFollowCommonMark(string source, string url, string title)
    {
        var image = Assert.IsType<ImageInline>(Assert.Single(Parse(source)));

        Assert.Equal(url, image.Url);
        Assert.Equal("cat", image.Alt);
        Assert.Equal(title, image.Title);
    }

    [Theory]
    [InlineData("[a](u \"t\" x)")]
    [InlineData("[a](foo (bar) baz)")]
    [InlineData("[a](u\n\nv)")]
    [InlineData("[a](u (t)")]
    [InlineData("[a](<u)")]
    public void AMalformedLinkTailIsText(string source) =>
        Assert.Empty(Parse(source).OfType<LinkInline>());
}

public sealed class IncrementalRegressionTests
{
    private static void AssertMatchesOneShotAtEveryPrefix(string source, MarkdownOptions? options = null)
    {
        var parser = new IncrementalMarkdownParser(options);

        for (var length = 1; length <= source.Length; length++)
        {
            var prefix = source[..length];
            Assert.True(
                TreeDump.Of(MarkdownParser.Parse(prefix, options, isComplete: false)) == TreeDump.Of(parser.Parse(prefix, isComplete: false)),
                $"Mismatch at prefix {length}: {prefix}");
        }

        Assert.Equal(TreeDump.Of(MarkdownParser.Parse(source, options)), TreeDump.Of(parser.Parse(source, isComplete: true)));
    }

    [Theory]
    [InlineData("Intro.\n\n$$\na\n\nb\n$$\n\nafter\n\nend")]
    [InlineData("```a```\n```\ncode\n\nx\n```\n\nafter\n\nend")]
    [InlineData("```\ncode\n```  \n\nmiddle\n\n```\nmore\n\nstill code\n```\n\nend")]
    [InlineData("- a *b\n\nnext *c\n\n- d\n\n  e\n\nlast")]
    [InlineData("# Title\n\nSome **bold** text.\n\n> quote\n> more\n\n1. one\n2. two\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\nFinal.")]
    // A fence left open inside a list item ends with the item, so the column-zero fence after it
    // opens a new block rather than closing the first.
    [InlineData("- item\n  ```\n  code\n\ntext\n\n```\ninside\n\nstill inside\n```\n\nafter\n\nend")]
    [InlineData("1. step\n   ```bash\n   npm i\n\nNext.\n\n```\nnot closed by the list\n\nmore\n```\n\nend")]
    // A "$$" line in the middle of a paragraph is text, so the "$$" after it opens display math.
    [InlineData("text\n$$ not math\n\npara\n\n$$\nreal\n\nmath\n$$\n\nafter\n\nend")]
    [InlineData("- a\n* b\n\n+ c\n\n1. d\n2) e\n\nend")]
    public void TheDocumentMatchesAOneShotParseExactlyAtEveryPrefix(string source) =>
        AssertMatchesOneShotAtEveryPrefix(source);

    [Fact]
    public void AFenceHiddenInsideRawHtmlDoesNotConfuseTheBoundaries() =>
        AssertMatchesOneShotAtEveryPrefix(
            "<div>\n```\n</div>\n\ntext\n\n```\ncode\n\nmore code\n```\n\nend",
            new MarkdownOptions { AllowRawHtml = true });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomDocumentsMatchAOneShotParseAtEveryPrefix(bool allowRawHtml)
    {
        string[] fragments =
        [
            "Plain *text* here.", "Two\nlines", "# Heading", "Setext\n---", "***", "- a\n- b", "- a\n\n- b",
            "1. one\n2. two", "10) ten", "- a\n\n  continued", "> quote\n> more", "> lazy\nline",
            "```js\nlet a;\n\nlet b;\n```", "~~~\ntilde\n\n~~~", "```inline``` text", "$$\nx\n\ny\n$$", "$$x$$",
            "Text:\n$$\nz\n$$", "| a | b |\n|---|---|\n| 1 | 2 |", "  indented", "-", "1.", "**bold", "- [x] done",
            // Fences left open inside list items, and column-zero fences that may or may not close them.
            "- a\n  ```\n  code", "- a\n  ```js\n  x\n\n  y", "1. step\n   ```bash\n   npm i\n\n   more", "```",
            "```\nz\n\nw\n```", "  ```\n  indented fence\n\n  body", "- a\n  ~~~\n  t", "- a\n```\nb",
            // "$$" lines inside paragraphs, at block starts, and inside list items.
            "text\n$$ not math\nmore", "para\n$$ x", "$$ x", "$$", "- item\n  $$\n  m", "end $$", "$$ a\n\nb $$",
            // Lists whose bullet or delimiter changes, and fences or math hidden inside other blocks.
            "* star\n+ plus\n- dash", "1. one\n2) two", "- a\n\n* b", "<div>\n```\n</div>", "<p>\n$$\n</p>", "> ```\n> q",
        ];

        var options = new MarkdownOptions { AllowRawHtml = allowRawHtml };
        var random = new Random(1234);
        for (var round = 0; round < 300; round++)
        {
            var parts = Enumerable.Range(0, random.Next(2, 7)).Select(_ => fragments[random.Next(fragments.Length)]);
            var source = string.Join(random.Next(3) == 0 ? "\n" : "\n\n", parts) + (random.Next(2) == 0 ? "\n" : string.Empty);
            AssertMatchesOneShotAtEveryPrefix(source, options);
        }
    }

    [Fact]
    public void BlocksAfterACodeBlockInsideAListStillBecomeStable()
    {
        const string source = "1. Install:\n   ```bash\n   npm i\n   ```\n\nFirst.\n\nSecond.\n\nThird.\n";
        var parser = new IncrementalMarkdownParser();

        for (var length = 1; length <= source.Length; length++) parser.Parse(source[..length]);

        Assert.Equal(3, parser.StableBlockCount);
    }

    [Fact]
    public void BoundariesAreFoundWhenTextArrivesACharacterAtATime()
    {
        var source = string.Concat(Enumerable.Range(0, 50).Select(i => $"Paragraph {i}.\n\n"));
        var parser = new IncrementalMarkdownParser();

        for (var length = 1; length <= source.Length; length++) parser.Parse(source[..length]);

        Assert.True(parser.StableBlockCount >= 49, $"Only {parser.StableBlockCount} blocks became stable.");
    }
}

public sealed class HighlighterRegressionTests
{
    [Fact]
    public void TripleQuotedStringsAreOneToken()
    {
        const string code = "def f():\n    \"\"\"It's done.\n    Really.\"\"\"\n    return 1";
        var tokens = SyntaxHighlighter.Highlight(code, "python");

        Assert.Contains(tokens, t => t.Kind is TokenKind.String && t.Slice(code).ToString() == "\"\"\"It's done.\n    Really.\"\"\"");
        Assert.Contains(tokens, t => t.Kind is TokenKind.Keyword && t.Slice(code).ToString() == "return");
    }

    [Fact]
    public void AnIndexerIsNotAnAttribute()
    {
        const string code = "[Fact]\nvar x = items[index];";
        var tokens = SyntaxHighlighter.Highlight(code, "csharp");

        Assert.Contains(tokens, t => t.Kind is TokenKind.Attribute && t.Slice(code).ToString() == "[Fact");
        Assert.DoesNotContain(tokens, t => t.Kind is TokenKind.Attribute && t.Slice(code).ToString() == "[index");
    }

    [Fact]
    public void RustLifetimesAndLabelsAreNotStrings()
    {
        const string code = "fn longest<'a>(x: &'a str) -> &'static str {\n    let c = 'a';\n    let n = '\\n';\n    'outer: loop { break 'outer; }\n}";
        var tokens = SyntaxHighlighter.Highlight(code, "rust");
        string[] Texts(TokenKind kind) => tokens.Where(t => t.Kind == kind).Select(t => t.Slice(code).ToString()).ToArray();

        Assert.Equal(["'a", "'a", "'static", "'outer", "'outer"], Texts(TokenKind.Label));
        Assert.DoesNotContain(Texts(TokenKind.Type), t => t.StartsWith('\''));
        Assert.Equal(["'a'", "'\\n'"], Texts(TokenKind.String));
        Assert.Contains("str", Texts(TokenKind.Type));
        Assert.Contains("loop", Texts(TokenKind.Keyword));
    }

    [Fact]
    public void RustAttributesAreAttributes()
    {
        const string code = "#![allow(dead_code)]\n#[derive(Debug, Clone)]\nstruct S;\n#[tokio::main]\nasync fn main() {}";
        var tokens = SyntaxHighlighter.Highlight(code, "rust");

        Assert.Equal(
            ["#![allow", "#[derive", "#[tokio::main"],
            tokens.Where(t => t.Kind is TokenKind.Attribute).Select(t => t.Slice(code).ToString()));
        Assert.Contains(tokens, t => t.Kind is TokenKind.Type && t.Slice(code).ToString() == "Debug");
    }

    [Theory]
    [InlineData("css", ".a { background: url(https://x.dev/a.png); color: red; }", "https://x.dev/a.png")]
    [InlineData("css", ".a { background: URL(//cdn.x.dev/a.png) }", "//cdn.x.dev/a.png")]
    [InlineData("scss", ".a { b: url(\"a).png\"); }", "\"a).png\"")]
    [InlineData("css", ".a { b: url(data:image/png;base64,iVBOR//8=) }", "data:image/png;base64,iVBOR//8=")]
    public void ACssUrlIsOneStringEvenWithSlashes(string language, string code, string argument)
    {
        var tokens = SyntaxHighlighter.Highlight(code, language);

        Assert.DoesNotContain(tokens, t => t.Kind is TokenKind.Comment);
        Assert.Contains(tokens, t => t.Kind is TokenKind.String && t.Slice(code).ToString() == argument);
        Assert.Contains(tokens, t => t.Kind is TokenKind.Function && t.Slice(code).ToString().Equals("url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScssLineCommentsStillWorkAroundUrls()
    {
        const string code = "// header\n.a { background: url(https://x.dev/a.png); } // trailing\n.b { color: red; }";
        var tokens = SyntaxHighlighter.Highlight(code, "scss");

        Assert.Equal(["// header", "// trailing"], tokens.Where(t => t.Kind is TokenKind.Comment).Select(t => t.Slice(code).ToString()));
        Assert.Contains(tokens, t => t.Kind is TokenKind.Property && t.Slice(code).ToString() == "color");
    }

    [Fact]
    public void AnUnclosedUrlStillStreamsAsAString()
    {
        const string code = ".a { background: url(https://x.dev/a";
        var tokens = SyntaxHighlighter.Highlight(code, "css");

        Assert.DoesNotContain(tokens, t => t.Kind is TokenKind.Comment);
        Assert.Equal(TokenKind.String, tokens[^1].Kind);
        Assert.Equal(code.Length, tokens[^1].Start + tokens[^1].Length);
    }

    [Fact]
    public void ReplacingALanguageAlsoReplacesItUnderItsAliases()
    {
        var original = new LanguageDefinition("regression-lang") { Aliases = ["regression-alias"] };
        var replacement = new LanguageDefinition("regression-lang");

        LanguageRegistry.Register(original);
        LanguageRegistry.Register(replacement);

        Assert.Same(replacement, LanguageRegistry.Find("regression-alias"));
        Assert.Same(replacement, Assert.Single(LanguageRegistry.All, l => l.Id == "regression-lang"));
    }
}
