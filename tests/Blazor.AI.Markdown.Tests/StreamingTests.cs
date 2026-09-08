using System.Diagnostics;

namespace Blazor.AI.Markdown.Tests;

public sealed class IncrementalParserTests
{
    [Fact]
    public void TheDocumentMatchesAOneShotParseAtEveryPrefix()
    {
        const string source =
            "# Title\n\nSome **bold** text.\n\n```csharp\nvar x = 1;\n```\n\n- one\n- two\n\nFinal words.";

        var parser = new IncrementalMarkdownParser();

        for (var length = 1; length <= source.Length; length++)
        {
            var prefix = source[..length];
            var incremental = parser.Parse(prefix, isComplete: false);
            var oneShot = MarkdownParser.Parse(prefix, isComplete: false);

            Assert.Equal(oneShot.Blocks.Count, incremental.Blocks.Count);
            for (var i = 0; i < oneShot.Blocks.Count; i++)
            {
                Assert.Equal(oneShot.Blocks[i].GetType(), incremental.Blocks[i].GetType());
            }
        }
    }

    [Fact]
    public void CompletedBlocksAreReusedRatherThanReparsed()
    {
        var parser = new IncrementalMarkdownParser();
        parser.Parse("First paragraph.\n\nSecond paragraph.\n\nthird\n", isComplete: false);

        Assert.True(parser.StableLength > 0, "Text before the last confirmed boundary should become stable.");
        Assert.Equal(2, parser.StableBlockCount);
    }

    [Fact]
    public void NothingBecomesStableInsideAnOpenFence()
    {
        var parser = new IncrementalMarkdownParser();
        parser.Parse("```js\nline one\n\nline two\n", isComplete: false);

        Assert.Equal(0, parser.StableLength);
    }

    [Fact]
    public void ABlankLineInsideALooseListIsNotAStableBoundary()
    {
        var parser = new IncrementalMarkdownParser();
        var document = parser.Parse("- one\n\n- two\n\n- three\n", isComplete: false);

        Assert.Single(document.Blocks);
        Assert.Equal(3, Assert.IsType<ListBlock>(document.Blocks[0]).Items.Count);
    }

    [Fact]
    public void RewritingTheSourceStartsOver()
    {
        var parser = new IncrementalMarkdownParser();
        parser.Parse("First.\n\nSecond.\n\n", isComplete: false);
        Assert.True(parser.StableLength > 0);

        var document = parser.Parse("Completely different.", isComplete: false);

        Assert.Equal(0, parser.StableLength);
        Assert.Single(document.Blocks);
    }

    [Fact]
    public void IncrementalParsingStaysCheapOnALongDocument()
    {
        var source = string.Concat(Enumerable.Range(0, 400)
            .Select(i => $"Paragraph number {i} with a reasonable amount of text in it.\n\n"));

        var incremental = new IncrementalMarkdownParser();
        var stopwatch = Stopwatch.StartNew();
        for (var length = 200; length <= source.Length; length += 200) incremental.Parse(source[..length], false);
        var incrementalTime = stopwatch.Elapsed;

        stopwatch.Restart();
        for (var length = 200; length <= source.Length; length += 200) MarkdownParser.Parse(source[..length], null, false);
        var oneShotTime = stopwatch.Elapsed;

        Assert.True(
            incrementalTime < oneShotTime,
            $"Incremental parsing took {incrementalTime.TotalMilliseconds:F1}ms " +
            $"against {oneShotTime.TotalMilliseconds:F1}ms for repeated full parses.");
    }
}

public sealed class SyntaxHighlighterTests
{
    [Fact]
    public void AnUnknownLanguageProducesNoTokens() =>
        Assert.Empty(SyntaxHighlighter.Highlight("some text", "brainfuck"));

    [Fact]
    public void KeywordsTypesStringsAndCommentsAreClassified()
    {
        const string code = "// note\npublic string Name = \"Ada\";";
        var tokens = SyntaxHighlighter.Highlight(code, "csharp");

        Assert.Contains(tokens, t => t.Kind is TokenKind.Comment && t.Slice(code).ToString() == "// note");
        Assert.Contains(tokens, t => t.Kind is TokenKind.Keyword && t.Slice(code).ToString() == "public");
        Assert.Contains(tokens, t => t.Kind is TokenKind.Type && t.Slice(code).ToString() == "string");
        Assert.Contains(tokens, t => t.Kind is TokenKind.String && t.Slice(code).ToString() == "\"Ada\"");
    }

    [Fact]
    public void TokensNeverOverlapAndStayInsideTheSource()
    {
        const string code = """
            using System;
            /* block */
            var items = new List<int> { 1, 2, 3 };
            Console.WriteLine($"{items.Count} items");
            """;

        var tokens = SyntaxHighlighter.Highlight(code, "csharp");
        var position = 0;

        foreach (var token in tokens)
        {
            Assert.True(token.Start >= position, "Tokens must be ordered and must not overlap.");
            Assert.True(token.Start + token.Length <= code.Length, "Tokens must stay inside the source.");
            position = token.Start + token.Length;
        }
    }

    [Fact]
    public void JsonKeysAreHighlightedAsProperties()
    {
        const string code = """{"name":"Ada","age":36}""";
        var tokens = SyntaxHighlighter.Highlight(code, "json");

        Assert.Contains(tokens, t => t.Kind is TokenKind.Number);
        Assert.Contains(tokens, t => t.Kind is TokenKind.String);
    }

    [Fact]
    public void MarkupTagsAndAttributesAreClassified()
    {
        const string code = """<a href="https://x.dev" class="link">go</a>""";
        var tokens = SyntaxHighlighter.Highlight(code, "html");

        Assert.Contains(tokens, t => t.Kind is TokenKind.Tag && t.Slice(code).ToString() == "a");
        Assert.Contains(tokens, t => t.Kind is TokenKind.Attribute && t.Slice(code).ToString() == "href");
        Assert.Contains(tokens, t => t.Kind is TokenKind.String);
    }

    [Fact]
    public void DiffLinesAreClassifiedByTheirMarker()
    {
        const string code = "@@ -1,3 +1,3 @@\n-old line\n+new line\n unchanged";
        var tokens = SyntaxHighlighter.Highlight(code, "diff");

        Assert.Contains(tokens, t => t.Kind is TokenKind.Meta);
        Assert.Contains(tokens, t => t.Kind is TokenKind.Deleted);
        Assert.Contains(tokens, t => t.Kind is TokenKind.Inserted);
    }

    [Fact]
    public void AnUnterminatedStringDoesNotThrow()
    {
        var tokens = SyntaxHighlighter.Highlight("var s = \"still typing", "csharp");
        Assert.Contains(tokens, t => t.Kind is TokenKind.String);
    }

    [Fact]
    public void LanguagesResolveThroughTheirAliases()
    {
        Assert.True(LanguageRegistry.IsSupported("cs"));
        Assert.True(LanguageRegistry.IsSupported("TS"));
        Assert.True(LanguageRegistry.IsSupported("yml"));
        Assert.False(LanguageRegistry.IsSupported("nonexistent"));
    }

    [Fact]
    public void ACustomLanguageCanBeRegistered()
    {
        LanguageRegistry.Register(new LanguageDefinition("toylang")
        {
            Keywords = new HashSet<string>(["begin", "end"], StringComparer.Ordinal),
        });

        const string code = "begin work end";
        var tokens = SyntaxHighlighter.Highlight(code, "toylang");

        Assert.Equal(2, tokens.Count(t => t.Kind is TokenKind.Keyword));
    }
}
