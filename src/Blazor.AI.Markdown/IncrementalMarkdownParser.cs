namespace Blazor.AI.Markdown;

/// <summary>
/// Parses Markdown that arrives a token at a time, reusing the work done for the text already seen.
/// </summary>
/// <remarks>
/// <para>
/// Re-parsing the whole response on every delta is quadratic, and a long answer streamed at forty
/// updates a second makes that obvious. This parser tracks the point beyond which no future text can
/// change the result, keeps the blocks before it, and re-parses only the tail.
/// </para>
/// <para>
/// The stable point is a blank line at fence depth zero, followed by a line at column zero that
/// cannot continue the preceding block. That last condition matters: a blank line inside a loose
/// list looks like a block boundary but is not one, and cutting there would split one list into two.
/// </para>
/// <para>
/// An instance is not thread-safe and belongs to one streaming text part.
/// </para>
/// </remarks>
public sealed class IncrementalMarkdownParser
{
    private readonly MarkdownOptions _options;
    private readonly List<MarkdownBlock> _stable = [];
    private string _source = string.Empty;
    private int _stableLength;
    private int _scanned;
    private int _boundary;
    private bool _fenceOpen;
    private char _fenceChar;
    private int _fenceLength;

    /// <summary>Creates a parser.</summary>
    /// <param name="options">Which extensions to enable.</param>
    public IncrementalMarkdownParser(MarkdownOptions? options = null) => _options = options ?? MarkdownOptions.Default;

    /// <summary>How many characters have been promoted to the stable prefix.</summary>
    /// <remarks>Exposed for diagnostics and tests: it is the measure of how much work is being reused.</remarks>
    public int StableLength => _stableLength;

    /// <summary>How many blocks are in the stable prefix.</summary>
    public int StableBlockCount => _stable.Count;

    /// <summary>
    /// Parses the source as it currently stands.
    /// </summary>
    /// <param name="source">The whole text received so far, not just the new part.</param>
    /// <param name="isComplete"><see langword="true"/> once no more text will arrive.</param>
    /// <returns>The document. A new instance is returned each call; the blocks may be shared.</returns>
    public MarkdownDocument Parse(string? source, bool isComplete = false)
    {
        source ??= string.Empty;

        if (!source.StartsWith(_source, StringComparison.Ordinal)) Reset();
        _source = source;

        ScanForBoundary();
        PromoteStableBlocks();

        var document = new MarkdownDocument();
        document.Blocks.AddRange(_stable);

        if (_stableLength < source.Length)
        {
            var tail = MarkdownParser.Parse(source[_stableLength..], _options, isComplete);
            document.Blocks.AddRange(tail.Blocks);
        }

        return document;
    }

    /// <summary>Discards all cached state, forcing the next parse to start from scratch.</summary>
    public void Reset()
    {
        _stable.Clear();
        _source = string.Empty;
        _stableLength = 0;
        _scanned = 0;
        _boundary = 0;
        _fenceOpen = false;
        _fenceChar = '\0';
        _fenceLength = 0;
    }

    private void ScanForBoundary()
    {
        var source = _source;
        var i = _scanned;

        while (i < source.Length)
        {
            var lineEnd = source.IndexOf('\n', i);
            if (lineEnd < 0) break;

            var line = source.AsSpan(i, lineEnd - i).TrimEnd('\r');
            var next = lineEnd + 1;

            if (_fenceOpen)
            {
                if (ClosesFence(line)) _fenceOpen = false;
            }
            else if (TryOpenFence(line))
            {
                _fenceOpen = true;
            }
            else if (line.IsWhiteSpace() && IsSafeAfterBlank(source, next))
            {
                _boundary = next;
            }

            i = next;
        }

        _scanned = i;
    }

    private bool TryOpenFence(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length < 3) return false;

        var marker = trimmed[0];
        if (marker is not ('`' or '~')) return false;

        var run = 0;
        while (run < trimmed.Length && trimmed[run] == marker) run++;
        if (run < 3) return false;

        _fenceChar = marker;
        _fenceLength = run;
        return true;
    }

    private bool ClosesFence(ReadOnlySpan<char> line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < _fenceLength) return false;

        foreach (var c in trimmed)
        {
            if (c != _fenceChar) return false;
        }

        return true;
    }

    private static bool IsSafeAfterBlank(string source, int position)
    {
        for (var i = position; i < source.Length;)
        {
            var lineEnd = source.IndexOf('\n', i);
            if (lineEnd < 0) return false;

            var line = source.AsSpan(i, lineEnd - i).TrimEnd('\r');
            if (line.IsWhiteSpace()) { i = lineEnd + 1; continue; }

            if (line[0] is ' ' or '\t') return false;
            return !StartsListItem(line) && line[0] is not ('>' or '|');
        }

        return false;
    }

    private static bool StartsListItem(ReadOnlySpan<char> line)
    {
        if (line.Length >= 2 && line[0] is '-' or '*' or '+' && line[1] is ' ' or '\t') return true;

        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
        return digits > 0
            && digits + 1 < line.Length
            && line[digits] is '.' or ')'
            && line[digits + 1] is ' ' or '\t';
    }

    private void PromoteStableBlocks()
    {
        if (_boundary <= _stableLength) return;

        var segment = _source[_stableLength.._boundary];
        var parsed = MarkdownParser.Parse(segment, _options, isComplete: true);
        _stable.AddRange(parsed.Blocks);
        _stableLength = _boundary;
    }
}
