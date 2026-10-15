using System.Text;

namespace Blazor.AI.Markdown;

/// <summary>
/// Parses Markdown into a document tree.
/// </summary>
/// <remarks>
/// <para>
/// A CommonMark subset with the GitHub-flavoured extensions that models actually emit: tables, task
/// lists, strikethrough, autolinks, and mathematics. It is not a conformance-complete CommonMark
/// implementation, and it does not try to be: link reference definitions and HTML blocks matter far
/// less here than being able to parse a half-written response forty times a second without
/// allocating a new tree each time.
/// </para>
/// <para>
/// Every block records whether the source ended inside it, which is what makes streaming look
/// settled rather than jittery. See <see cref="IncrementalMarkdownParser"/> for the streaming entry
/// point.
/// </para>
/// </remarks>
public static class MarkdownParser
{
    /// <summary>
    /// Parses Markdown into a document.
    /// </summary>
    /// <param name="markdown">The source.</param>
    /// <param name="options">Which extensions to enable. Defaults to <see cref="MarkdownOptions.Default"/>.</param>
    /// <param name="isComplete">
    /// <see langword="false"/> when more source is expected, which marks the trailing block open.
    /// </param>
    /// <returns>The document.</returns>
    public static MarkdownDocument Parse(string? markdown, MarkdownOptions? options = null, bool isComplete = true)
    {
        var document = new MarkdownDocument();
        if (string.IsNullOrEmpty(markdown)) return document;

        var settings = options ?? MarkdownOptions.Default;
        var lines = SplitLines(markdown);
        ParseBlocks(lines, 0, lines.Count, document.Blocks, settings, isComplete, 0);
        return document;
    }

    /// <summary>
    /// Parses Markdown and reports where each top-level block starts, which is how
    /// <see cref="IncrementalMarkdownParser"/> learns where it can safely cut.
    /// </summary>
    /// <param name="markdown">The source.</param>
    /// <param name="options">Which extensions to enable.</param>
    /// <param name="isComplete"><see langword="false"/> when more source is expected.</param>
    /// <param name="blockStarts">
    /// Receives, for each top-level block in order, the offset in <paramref name="markdown"/> of the
    /// line it starts on.
    /// </param>
    /// <returns>The document.</returns>
    internal static MarkdownDocument Parse(string markdown, MarkdownOptions options, bool isComplete, List<int> blockStarts)
    {
        var document = new MarkdownDocument();
        if (markdown.Length == 0) return document;

        var lines = SplitLines(markdown);
        ParseBlocks(lines, 0, lines.Count, document.Blocks, options, isComplete, 0, blockStarts);

        // Convert line indices to offsets. Both lists ascend, so one pass over the text does it.
        var line = 0;
        var offset = 0;
        for (var i = 0; i < blockStarts.Count; i++)
        {
            for (; line < blockStarts[i]; line++) offset = markdown.IndexOf('\n', offset) + 1;
            blockStarts[i] = offset;
        }

        return document;
    }

    // Quotes and lists recurse. A line of ten thousand '>' characters must not overflow the stack,
    // which on Blazor Server takes the whole process down with it.
    private const int MaxNesting = 32;

    internal static List<string> SplitLines(string text)
    {
        var lines = new List<string>(Math.Max(4, text.Length / 40));
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            var end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add(text[start..end]);
            start = i + 1;
        }

        lines.Add(text[start..]);
        return lines;
    }

    private static void ParseBlocks(
        List<string> lines,
        int start,
        int end,
        List<MarkdownBlock> output,
        MarkdownOptions options,
        bool isComplete,
        int depth,
        List<int>? blockStarts = null)
    {
        var index = start;

        while (index < end)
        {
            var line = lines[index];

            if (IsBlank(line))
            {
                index++;
                continue;
            }

            // Every pass from here adds exactly one block, starting on this line.
            blockStarts?.Add(index);

            if (TryFencedCode(lines, ref index, end, output, options, isComplete)) continue;
            if (TryThematicBreak(line, ref index, output)) continue;
            if (TryHeading(line, ref index, output, options)) continue;
            if (options.Math && TryMathBlock(lines, ref index, end, output, isComplete)) continue;
            if (depth < MaxNesting && TryQuote(lines, ref index, end, output, options, isComplete, depth)) continue;
            if (depth < MaxNesting && TryList(lines, ref index, end, output, options, isComplete, depth)) continue;
            if (options.Tables && TryTable(lines, ref index, end, output, options, isComplete)) continue;
            if (!options.AllowRawHtml || !TryHtmlBlock(lines, ref index, end, output)) ParseParagraph(lines, ref index, end, output, options, isComplete);
        }
    }

    private static bool IsBlank(string line)
    {
        foreach (var c in line)
        {
            if (!char.IsWhiteSpace(c)) return false;
        }

        return true;
    }

    private static int IndentOf(string line)
    {
        var indent = 0;
        foreach (var c in line)
        {
            if (c == ' ') indent++;
            else if (c == '\t') indent += 4;
            else break;
        }

        return indent;
    }

    private static bool TryThematicBreak(string line, ref int index, List<MarkdownBlock> output)
    {
        if (!IsThematicBreak(line)) return false;

        output.Add(new ThematicBreakBlock());
        index++;
        return true;
    }

    private static bool IsThematicBreak(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3) return false;

        var marker = trimmed[0];
        if (marker is not ('-' or '*' or '_')) return false;

        var count = 0;
        foreach (var c in trimmed)
        {
            if (c == marker) count++;
            else if (c is not (' ' or '\t')) return false;
        }

        return count >= 3;
    }

    private static bool TryHeading(string line, ref int index, List<MarkdownBlock> output, MarkdownOptions options)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '#') return false;

        var level = 0;
        while (level < trimmed.Length && trimmed[level] == '#') level++;
        if (level > 6) return false;
        if (level < trimmed.Length && trimmed[level] is not (' ' or '\t')) return false;

        var text = StripClosingSequence(trimmed[level..].Trim());
        var heading = new HeadingBlock(level);
        heading.Inlines.AddRange(InlineParser.Parse(text, options, isComplete: true));
        if (options.HeadingIds) heading.Id = Slug(text);
        output.Add(heading);
        index++;
        return true;
    }

    // The closing run of '#' only counts when a space separates it from the text, or "## Using C#"
    // would lose its last character.
    private static string StripClosingSequence(string text)
    {
        var closing = text.Length;
        while (closing > 0 && text[closing - 1] == '#') closing--;

        if (closing == 0) return string.Empty;
        if (closing == text.Length || text[closing - 1] is not (' ' or '\t')) return text;
        return text[..closing].TrimEnd();
    }

    internal static string Slug(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or '_' && builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }

    private static bool TryFencedCode(
        List<string> lines,
        ref int index,
        int end,
        List<MarkdownBlock> output,
        MarkdownOptions options,
        bool isComplete)
    {
        var line = lines[index];
        var trimmed = line.TrimStart();
        if (trimmed.Length < 3) return false;

        var fence = trimmed[0];
        if (fence is not ('`' or '~')) return false;

        var fenceLength = 0;
        while (fenceLength < trimmed.Length && trimmed[fenceLength] == fence) fenceLength++;
        if (fenceLength < 3) return false;

        var info = trimmed[fenceLength..].Trim();
        if (fence == '`' && info.Contains('`')) return false;

        var space = info.IndexOf(' ');
        var block = new CodeBlock
        {
            IsFenced = true,
            Language = info.Length == 0 ? null : (space < 0 ? info : info[..space]),
            Meta = space < 0 ? null : info[(space + 1)..].Trim(),
        };

        var body = new StringBuilder();
        var cursor = index + 1;
        var closed = false;

        while (cursor < end)
        {
            var candidate = lines[cursor].Trim();
            if (candidate.Length >= fenceLength && candidate.All(c => c == fence))
            {
                closed = true;
                cursor++;
                break;
            }

            if (body.Length > 0) body.Append('\n');
            body.Append(lines[cursor]);
            cursor++;
        }

        block.Code = body.ToString();
        block.IsOpen = !closed && !isComplete;
        output.Add(block);
        index = cursor;
        return true;
    }

    private static bool TryMathBlock(
        List<string> lines,
        ref int index,
        int end,
        List<MarkdownBlock> output,
        bool isComplete)
    {
        var trimmed = lines[index].Trim();
        if (!trimmed.StartsWith("$$", StringComparison.Ordinal)) return false;

        var body = new StringBuilder();
        var closed = false;

        if (trimmed.Length >= 4 && trimmed.EndsWith("$$", StringComparison.Ordinal))
        {
            body.Append(trimmed[2..^2]);
            closed = true;
            index++;
        }
        else
        {
            body.Append(trimmed[2..]);
            var cursor = index + 1;
            while (cursor < end)
            {
                var candidate = lines[cursor].Trim();
                if (candidate.EndsWith("$$", StringComparison.Ordinal))
                {
                    if (candidate.Length > 2)
                    {
                        if (body.Length > 0) body.Append('\n');
                        body.Append(candidate[..^2]);
                    }

                    closed = true;
                    cursor++;
                    break;
                }

                if (body.Length > 0) body.Append('\n');
                body.Append(lines[cursor]);
                cursor++;
            }

            index = cursor;
        }

        output.Add(new MathBlock(body.ToString().Trim()) { IsOpen = !closed && !isComplete });
        return true;
    }

    private static bool TryQuote(
        List<string> lines,
        ref int index,
        int end,
        List<MarkdownBlock> output,
        MarkdownOptions options,
        bool isComplete,
        int depth)
    {
        if (!IsQuoteLine(lines[index])) return false;

        var inner = new List<string>();
        var cursor = index;

        while (cursor < end && (IsQuoteLine(lines[cursor]) || (inner.Count > 0 && IsLazyQuoteLine(lines, cursor, end, options))))
        {
            inner.Add(StripQuoteMarker(lines[cursor]));
            cursor++;
        }

        var quote = new QuoteBlock();
        ParseBlocks(inner, 0, inner.Count, quote.Blocks, options, isComplete || cursor < end, depth + 1);
        quote.IsOpen = !isComplete && cursor >= end;
        output.Add(quote);
        index = cursor;
        return true;
    }

    // Only paragraph text continues a quote lazily; a heading, fence, or list after it starts anew.
    private static bool IsLazyQuoteLine(List<string> lines, int index, int end, MarkdownOptions options) =>
        !IsBlank(lines[index]) && !StartsNewBlock(lines, index, end, options);

    private static bool IsQuoteLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith('>');
    }

    private static string StripQuoteMarker(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('>')) return line;
        trimmed = trimmed[1..];
        return trimmed.StartsWith(' ') ? trimmed[1..] : trimmed;
    }

    private readonly record struct ListMarker(bool IsOrdered, int Start, int ContentIndent, char Bullet)
    {
        // Changing the bullet character, or an ordered list's "." for ")", starts a new list.
        public bool Continues(ListMarker first) => IsOrdered == first.IsOrdered && Bullet == first.Bullet;
    }

    private static ListMarker? TryReadListMarker(string line)
    {
        var indent = IndentOf(line);
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0) return null;

        if (trimmed[0] is '-' or '*' or '+')
        {
            if (trimmed.Length < 2 || trimmed[1] is not (' ' or '\t')) return null;
            return new ListMarker(false, 1, indent + 2, trimmed[0]);
        }

        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits])) digits++;
        if (digits == 0 || digits > 9 || digits >= trimmed.Length) return null;
        if (trimmed[digits] is not ('.' or ')')) return null;
        if (digits + 1 >= trimmed.Length || trimmed[digits + 1] is not (' ' or '\t')) return null;

        return new ListMarker(true, int.Parse(trimmed[..digits]), indent + digits + 2, trimmed[digits]);
    }

    private static bool TryList(
        List<string> lines,
        ref int index,
        int end,
        List<MarkdownBlock> output,
        MarkdownOptions options,
        bool isComplete,
        int depth)
    {
        if (TryReadListMarker(lines[index]) is not { } first) return false;

        var list = new ListBlock { IsOrdered = first.IsOrdered, Start = first.Start };
        var cursor = index;
        var sawBlank = false;

        while (cursor < end)
        {
            if (IsBlank(lines[cursor]))
            {
                if (cursor + 1 < end && TryReadListMarker(lines[cursor + 1]) is { } next && next.Continues(first)) sawBlank = true;
                else if (cursor + 1 < end && IndentOf(lines[cursor + 1]) >= first.ContentIndent) sawBlank = true;
                else break;

                cursor++;
                continue;
            }

            if (IsThematicBreak(lines[cursor])) break;

            if (TryReadListMarker(lines[cursor]) is not { } marker)
            {
                if (list.Items.Count == 0 || IndentOf(lines[cursor]) < first.ContentIndent) break;
                cursor++;
                continue;
            }

            if (!marker.Continues(first)) break;

            var itemStart = cursor;
            cursor++;
            while (cursor < end)
            {
                if (IsBlank(lines[cursor]))
                {
                    if (cursor + 1 < end && IndentOf(lines[cursor + 1]) >= marker.ContentIndent) { cursor++; continue; }
                    break;
                }

                if (TryReadListMarker(lines[cursor]) is not null && IndentOf(lines[cursor]) < marker.ContentIndent) break;
                if (IndentOf(lines[cursor]) < marker.ContentIndent && !IsLazyContinuation(lines[cursor])) break;
                cursor++;
            }

            // Only the item the source ends inside can still grow.
            list.Items.Add(BuildItem(lines, itemStart, cursor, marker, options, isComplete || cursor < end, depth));
        }

        list.IsLoose = sawBlank;
        list.IsOpen = !isComplete && cursor >= end;
        output.Add(list);
        index = Math.Max(cursor, index + 1);
        return true;
    }

    private static bool IsLazyContinuation(string line) =>
        !IsBlank(line) && TryReadListMarker(line) is null && !IsThematicBreak(line)
        && line.TrimStart()[0] is not ('#' or '>' or '`' or '~');

    private static ListItemBlock BuildItem(
        List<string> lines,
        int start,
        int end,
        ListMarker marker,
        MarkdownOptions options,
        bool isComplete,
        int depth)
    {
        var item = new ListItemBlock();
        var body = new List<string>(end - start);

        for (var i = start; i < end; i++)
        {
            var line = lines[i];
            if (i == start)
            {
                var trimmed = line.TrimStart();
                var offset = trimmed.IndexOfAny([' ', '\t']);
                line = offset < 0 ? string.Empty : trimmed[(offset + 1)..];

                if (options.TaskLists && line.Length >= 3 && line[0] == '[' && line[2] == ']')
                {
                    var state = char.ToLowerInvariant(line[1]);
                    if (state is ' ' or 'x')
                    {
                        item.IsChecked = state == 'x';
                        line = line[3..].TrimStart();
                    }
                }
            }
            else
            {
                line = StripIndent(line, marker.ContentIndent);
            }

            body.Add(line);
        }

        ParseBlocks(body, 0, body.Count, item.Blocks, options, isComplete, depth + 1);
        return item;
    }

    // Removes up to the given number of columns of indentation, counting a tab as IndentOf does, so
    // a tab is never cut in half and the characters after it are never eaten.
    private static string StripIndent(string line, int columns)
    {
        var column = 0;
        var i = 0;

        while (i < line.Length && column < columns)
        {
            if (line[i] == ' ') column++;
            else if (line[i] == '\t')
            {
                if (column + 4 > columns) return new string(' ', column + 4 - columns) + line[(i + 1)..];
                column += 4;
            }
            else break;

            i++;
        }

        return line[i..];
    }

    private static bool TryTable(
        List<string> lines,
        ref int index,
        int end,
        List<MarkdownBlock> output,
        MarkdownOptions options,
        bool isComplete)
    {
        if (index + 1 >= end) return false;
        if (!lines[index].Contains('|')) return false;
        if (ParseAlignments(lines[index + 1]) is not { } alignments) return false;

        var header = SplitRow(lines[index]);
        if (header.Count == 0) return false;

        var table = new TableBlock();
        table.Alignments.AddRange(alignments);
        foreach (var cell in header) table.Header.Add(InlineParser.Parse(cell, options, isComplete: true));

        while (table.Alignments.Count < table.Header.Count) table.Alignments.Add(ColumnAlignment.None);

        var cursor = index + 2;
        while (cursor < end && !IsBlank(lines[cursor]) && lines[cursor].Contains('|'))
        {
            var cells = SplitRow(lines[cursor]);
            var row = new List<List<MarkdownInline>>(cells.Count);
            var lastLine = cursor == end - 1;
            foreach (var cell in cells) row.Add(InlineParser.Parse(cell, options, isComplete || !lastLine));
            table.Rows.Add(row);
            cursor++;
        }

        table.IsOpen = !isComplete && cursor >= end;
        output.Add(table);
        index = cursor;
        return true;
    }

    private static List<ColumnAlignment>? ParseAlignments(string line)
    {
        if (!line.Contains('-') || !line.Contains('|')) return null;

        var alignments = new List<ColumnAlignment>();
        foreach (var raw in SplitRow(line))
        {
            var cell = raw.Trim();
            if (cell.Length == 0) return null;

            var left = cell[0] == ':';
            var right = cell[^1] == ':';
            var body = cell.Trim(':');
            if (body.Length == 0 || body.Any(c => c != '-')) return null;

            alignments.Add((left, right) switch
            {
                (true, true) => ColumnAlignment.Center,
                (true, false) => ColumnAlignment.Left,
                (false, true) => ColumnAlignment.Right,
                _ => ColumnAlignment.None,
            });
        }

        return alignments.Count > 0 ? alignments : null;
    }

    private static List<string> SplitRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal)) trimmed = trimmed[..^1];

        var cells = new List<string>();
        var builder = new StringBuilder();

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                builder.Append('|');
                i++;
                continue;
            }

            if (c == '|')
            {
                cells.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(c);
        }

        cells.Add(builder.ToString().Trim());
        return cells;
    }

    private static bool TryHtmlBlock(List<string> lines, ref int index, int end, List<MarkdownBlock> output)
    {
        var trimmed = lines[index].TrimStart();
        if (!trimmed.StartsWith('<') || trimmed.Length < 2) return false;
        if (!char.IsLetter(trimmed[1]) && trimmed[1] is not ('/' or '!')) return false;

        var body = new StringBuilder();
        var cursor = index;
        while (cursor < end && !IsBlank(lines[cursor]))
        {
            if (body.Length > 0) body.Append('\n');
            body.Append(lines[cursor]);
            cursor++;
        }

        output.Add(new HtmlBlock(body.ToString()));
        index = cursor;
        return true;
    }

    private static void ParseParagraph(
        List<string> lines,
        ref int index,
        int end,
        List<MarkdownBlock> output,
        MarkdownOptions options,
        bool isComplete)
    {
        var builder = new StringBuilder();
        var cursor = index;
        var level = 0;

        while (cursor < end)
        {
            var line = lines[cursor];
            if (IsBlank(line)) break;

            if (cursor > index)
            {
                var setext = line.Trim();
                if (setext.Length > 0 && setext.All(c => c == '=')) { level = 1; cursor++; break; }
                if (setext.Length > 0 && setext.All(c => c == '-')) { level = 2; cursor++; break; }
                if (StartsNewBlock(lines, cursor, end, options)) break;
            }

            if (builder.Length > 0) builder.Append('\n');
            builder.Append(line);
            cursor++;
        }

        var text = builder.ToString();
        index = Math.Max(cursor, index + 1);
        if (text.Trim().Length == 0) return;

        var open = !isComplete && cursor >= end;

        if (level > 0)
        {
            var heading = new HeadingBlock(level);
            heading.Inlines.AddRange(InlineParser.Parse(text, options, isComplete: true));
            if (options.HeadingIds) heading.Id = Slug(text);
            output.Add(heading);
            return;
        }

        var paragraph = new ParagraphBlock { IsOpen = open };
        paragraph.Inlines.AddRange(InlineParser.Parse(text, options, !open || !options.HideDanglingDelimiters));
        output.Add(paragraph);
    }

    private static bool StartsNewBlock(List<string> lines, int index, int end, MarkdownOptions options)
    {
        var line = lines[index];
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0) return true;
        if (trimmed[0] == '#' && TryHeadingLevel(trimmed) > 0) return true;
        if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) return true;
        if (trimmed.StartsWith('>')) return true;
        if (TryReadListMarker(line) is not null) return true;
        if (IsThematicBreak(line)) return true;
        if (options.Math && IsDisplayMathLine(trimmed)) return true;
        if (options.Tables && index + 1 < end && line.Contains('|') && ParseAlignments(lines[index + 1]) is not null) return true;
        return false;
    }

    // A line that is only "$$", or a whole "$$...$$" expression, is display math even straight after
    // text. Anything looser would turn a paragraph line that merely starts with "$$" into a math block.
    private static bool IsDisplayMathLine(string trimmed)
    {
        var line = trimmed.TrimEnd();
        return line == "$$"
            || (line.Length >= 4 && line.StartsWith("$$", StringComparison.Ordinal) && line.EndsWith("$$", StringComparison.Ordinal));
    }

    private static int TryHeadingLevel(string trimmed)
    {
        var level = 0;
        while (level < trimmed.Length && trimmed[level] == '#') level++;
        if (level is 0 or > 6) return 0;
        return level == trimmed.Length || trimmed[level] is ' ' or '\t' ? level : 0;
    }
}
