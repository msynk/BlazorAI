using System.Diagnostics;
using System.Runtime;

namespace Blazor.AI.Markdown.Tests;

/// <summary>Runs timing-sensitive tests on their own, so other tests do not compete for the CPU.</summary>
[CollectionDefinition(nameof(TimingCollection), DisableParallelization = true)]
public sealed class TimingCollection;

/// <summary>
/// Guards the inline parser's linear-time claim on the inputs the CommonMark delimiter algorithm
/// exists to cope with. Each of these took seconds, some of them minutes, while a scan was quadratic.
/// </summary>
/// <remarks>
/// Every timing is the best of several runs, and every limit is an order of magnitude above what a
/// linear parser needs, so a slow or busy CI machine does not make the tests flaky.
/// </remarks>
[Collection(nameof(TimingCollection))]
public sealed class InlinePerformanceTests
{
    public static TheoryData<string> PathologicalPatterns =>
    [
        "*a", "_a ", "**a", "*a **b ", "**a*", "a*", "~~a ", "a~~ ", "[a", "![a", "[a](", "[a]( (", "[a](b \"x ",
        "[a](b 'x ", "[a](<b ", "<a", "<a ", "$a ", "``a`", "*a [b ", "_a [b](c) ",
    ];

    [Theory]
    [MemberData(nameof(PathologicalPatterns))]
    public void FiftyThousandRepetitionsParseWellUnderASecond(string pattern)
    {
        var text = Repeat(pattern, 50_000);

        foreach (var complete in new[] { true, false })
        {
            var elapsed = BestOf(2, () => InlineParser.Parse(text, isComplete: complete));
            Assert.True(elapsed < TimeSpan.FromSeconds(1), $"'{pattern}' x 50,000 (complete: {complete}) took {elapsed.TotalMilliseconds:F0}ms.");
        }

        if (!pattern.Contains('<')) return;

        var options = new MarkdownOptions { AllowRawHtml = true };
        var html = BestOf(2, () => InlineParser.Parse(text, options));
        Assert.True(html < TimeSpan.FromSeconds(1), $"'{pattern}' x 50,000 with raw HTML took {html.TotalMilliseconds:F0}ms.");
    }

    [Theory]
    [InlineData("*a")]
    [InlineData("_a ")]
    [InlineData("**a")]
    [InlineData("*a **b ")]
    [InlineData("[a")]
    [InlineData("[a](")]
    [InlineData("<a")]
    public void ParsingTimeGrowsLinearlyWithTheInput(string pattern)
    {
        var small = Repeat(pattern, 6_250);
        var large = Repeat(pattern, 100_000);

        var smallTime = BestOf(3, () => InlineParser.Parse(small, isComplete: false), withoutCollections: true);
        var largeTime = BestOf(3, () => InlineParser.Parse(large, isComplete: false), withoutCollections: true);

        // Sixteen times the input costs sixteen times as much when linear, and in practice up to
        // twice that as the working set outgrows the caches; when quadratic it costs 256 times as
        // much. The limit sits between the two on a log scale, with a factor of two and a half of
        // slack on the linear side and three on the quadratic one, and the fixed allowance absorbs
        // timer noise on very short runs.
        var limit = smallTime * 80 + TimeSpan.FromMilliseconds(20);
        Assert.True(
            largeTime < limit,
            $"'{pattern}': {smallTime.TotalMilliseconds:F1}ms for 6,250 repetitions but {largeTime.TotalMilliseconds:F1}ms for 100,000.");
    }

    [Fact]
    public void DeeplyNestedEmphasisStaysShallowAndFast()
    {
        string[] sources =
        [
            new string('*', 50_000) + "a" + new string('*', 50_000),
            Repeat("*a ", 20_000) + "b" + Repeat(" c*", 20_000),
            Repeat("~~a ", 20_000) + "b" + Repeat(" c~~", 20_000),
        ];

        foreach (var source in sources)
        {
            List<MarkdownInline> inlines = [];
            var elapsed = BestOf(1, () => inlines = InlineParser.Parse(source));

            Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Took {elapsed.TotalMilliseconds:F0}ms.");
            Assert.InRange(Depth(inlines), 0, 33);
        }
    }

    private static string Repeat(string pattern, int count) => string.Concat(Enumerable.Repeat(pattern, count));

    // The first run also pays for compilation, and each run starts from a collected heap so that
    // one run's garbage is not charged to the next. Comparing growth, the runs can also be kept
    // free of collections: a parse keeps everything it allocates alive until it returns, so the
    // collector's cost depends on its own heuristics as much as on the input, and that noise can
    // swamp the difference between linear and quadratic.
    private static TimeSpan BestOf(int runs, Action action, bool withoutCollections = false)
    {
        var budget = 0L;
        if (withoutCollections)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            budget = (GC.GetAllocatedBytesForCurrentThread() - before) * 2 + (16 << 20);
        }

        var best = TimeSpan.MaxValue;
        for (var run = 0; run < runs; run++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var noCollections = budget > 0 && TryStartNoGCRegion(budget);

            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();

            if (noCollections && GCSettings.LatencyMode == GCLatencyMode.NoGCRegion) GC.EndNoGCRegion();
            if (stopwatch.Elapsed < best) best = stopwatch.Elapsed;
        }

        return best;
    }

    // Falls back to an ordinary run where the runtime cannot reserve that much.
    private static bool TryStartNoGCRegion(long budget)
    {
        try
        {
            return GC.TryStartNoGCRegion(budget);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int Depth(IEnumerable<MarkdownInline> inlines) =>
        inlines.Select(i => i switch
        {
            EmphasisInline e => 1 + Depth(e.Inlines),
            StrikethroughInline s => 1 + Depth(s.Inlines),
            LinkInline l => 1 + Depth(l.Inlines),
            _ => 0,
        }).DefaultIfEmpty(0).Max();
}
