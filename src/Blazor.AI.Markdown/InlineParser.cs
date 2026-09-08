using System.Text;

namespace Blazor.AI.Markdown;

/// <summary>
/// Parses the inline content of a block: emphasis, code, links, images, mathematics, and citation
/// markers.
/// </summary>
/// <remarks>
/// <para>
/// Emphasis matching is a pragmatic simplification of the CommonMark delimiter algorithm: a run
/// looks forward for the nearest valid closer and recurses into the span between them. It handles
/// everything a language model actually produces, and it is linear rather than quadratic on the
/// pathological inputs the full algorithm exists to cope with.
/// </para>
/// <para>
/// When the source is still streaming, an unmatched trailing delimiter is dropped rather than shown
/// literally. Without that, every bold phrase in a response flashes a stray asterisk on its way in.
/// </para>
/// </remarks>
public static class InlineParser
{
    /// <summary>
    /// Parses inline content.
    /// </summary>
    /// <param name="text">The source.</param>
    /// <param name="options">Which extensions to enable.</param>
    /// <param name="isComplete">
    /// <see langword="false"/> while more text is expected, which hides unmatched trailing delimiters.
    /// </param>
    /// <returns>The parsed inlines.</returns>
    public static List<MarkdownInline> Parse(string? text, MarkdownOptions? options = null, bool isComplete = true)
    {
        var result = new List<MarkdownInline>();
        if (string.IsNullOrEmpty(text)) return result;
        ParseInto(text.AsSpan(), result, options ?? MarkdownOptions.Default, isComplete);
        return result;
    }

    private static void ParseInto(ReadOnlySpan<char> text, List<MarkdownInline> output, MarkdownOptions options, bool isComplete)
    {
        var pending = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            switch (c)
            {
                case '\\' when i + 1 < text.Length && IsEscapable(text[i + 1]):
                    pending.Append(text[i + 1]);
                    i += 2;
                    continue;

                case '\\' when i + 1 < text.Length && text[i + 1] == '\n':
                    Flush(pending, output);
                    output.Add(new LineBreakInline(true));
                    i += 2;
                    continue;

                case '`' when TryCodeSpan(text, i, out var codeEnd, out var code):
                    Flush(pending, output);
                    output.Add(new CodeInline(code));
                    i = codeEnd;
                    continue;

                case '$' when options.Math && TryMath(text, i, out var mathEnd, out var expression):
                    Flush(pending, output);
                    output.Add(new MathInline(expression));
                    i = mathEnd;
                    continue;

                case '!' when i + 1 < text.Length && text[i + 1] == '[' && TryLink(text, i + 1, out var imgEnd, out var imgLabel, out var imgUrl, out var imgTitle):
                    Flush(pending, output);
                    output.Add(new ImageInline(imgUrl, imgLabel, imgTitle));
                    i = imgEnd;
                    continue;

                case '[' when options.CitationMarkers && TryCitationMarker(text, i, out var citeEnd, out var number):
                    Flush(pending, output);
                    output.Add(new CitationMarkerInline(number));
                    i = citeEnd;
                    continue;

                case '[' when TryLink(text, i, out var linkEnd, out var label, out var url, out var title):
                    Flush(pending, output);
                    var link = new LinkInline(url, title);
                    ParseInto(label.AsSpan(), link.Inlines, options, isComplete);
                    output.Add(link);
                    i = linkEnd;
                    continue;

                case '<' when TryAutoLink(text, i, out var autoEnd, out var autoUrl):
                    Flush(pending, output);
                    var auto = new LinkInline(autoUrl) { IsAutoLink = true };
                    auto.Inlines.Add(new TextInline(autoUrl));
                    output.Add(auto);
                    i = autoEnd;
                    continue;

                case '<' when options.AllowRawHtml && TryRawHtml(text, i, out var htmlEnd, out var html):
                    Flush(pending, output);
                    output.Add(new RawHtmlInline(html));
                    i = htmlEnd;
                    continue;

                case '~' when options.Strikethrough && i + 1 < text.Length && text[i + 1] == '~':
                    if (TryDelimited(text, i, "~~", out var strikeEnd, out var strikeInner))
                    {
                        Flush(pending, output);
                        var strike = new StrikethroughInline();
                        ParseInto(strikeInner.AsSpan(), strike.Inlines, options, isComplete);
                        output.Add(strike);
                        i = strikeEnd;
                        continue;
                    }

                    break;

                case '*':
                case '_':
                    if (TryEmphasis(text, i, options, isComplete, out var emphasisEnd, out var emphasis))
                    {
                        Flush(pending, output);
                        if (emphasis is not null) output.Add(emphasis);
                        i = emphasisEnd;
                        continue;
                    }

                    break;

                case '\n':
                    Flush(pending, output);
                    output.Add(new LineBreakInline(EndsWithHardBreak(text, i)));
                    i++;
                    while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
                    continue;

                case 'h':
                case 'w':
                    if (options.AutoLinks && TryBareUrl(text, i, pending, out var bareEnd, out var bareUrl))
                    {
                        Flush(pending, output);
                        var bare = new LinkInline(bareUrl) { IsAutoLink = true };
                        bare.Inlines.Add(new TextInline(bareUrl));
                        output.Add(bare);
                        i = bareEnd;
                        continue;
                    }

                    break;
            }

            pending.Append(c);
            i++;
        }

        Flush(pending, output);
    }

    private static void Flush(StringBuilder pending, List<MarkdownInline> output)
    {
        if (pending.Length == 0) return;
        output.Add(new TextInline(pending.ToString()));
        pending.Clear();
    }

    private static bool IsEscapable(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);

    private static bool EndsWithHardBreak(ReadOnlySpan<char> text, int newlineIndex)
    {
        var spaces = 0;
        for (var i = newlineIndex - 1; i >= 0 && text[i] == ' '; i--) spaces++;
        return spaces >= 2;
    }

    private static bool TryCodeSpan(ReadOnlySpan<char> text, int start, out int end, out string code)
    {
        end = start;
        code = string.Empty;

        var ticks = 0;
        while (start + ticks < text.Length && text[start + ticks] == '`') ticks++;

        var i = start + ticks;
        while (i < text.Length)
        {
            if (text[i] != '`') { i++; continue; }

            var run = 0;
            while (i + run < text.Length && text[i + run] == '`') run++;
            if (run == ticks)
            {
                var inner = text[(start + ticks)..i];
                if (inner.Length > 1 && inner[0] == ' ' && inner[^1] == ' ') inner = inner[1..^1];
                code = inner.ToString().Replace('\n', ' ');
                end = i + run;
                return true;
            }

            i += run;
        }

        return false;
    }

    private static bool TryMath(ReadOnlySpan<char> text, int start, out int end, out string expression)
    {
        end = start;
        expression = string.Empty;
        if (start + 1 >= text.Length || text[start + 1] is ' ' or '$') return false;

        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\n') return false;
            if (text[i] != '$' || text[i - 1] == '\\') continue;

            expression = text[(start + 1)..i].ToString();
            end = i + 1;
            return expression.Length > 0;
        }

        return false;
    }

    private static bool TryCitationMarker(ReadOnlySpan<char> text, int start, out int end, out int number)
    {
        end = start;
        number = 0;

        var i = start + 1;
        var digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
        if (digits == 0 || digits > 4 || i >= text.Length || text[i] != ']') return false;
        if (i + 1 < text.Length && text[i + 1] == '(') return false;

        number = int.Parse(text[(start + 1)..i]);
        end = i + 1;
        return true;
    }

    private static bool TryLink(ReadOnlySpan<char> text, int start, out int end, out string label, out string url, out string? title)
    {
        end = start;
        label = string.Empty;
        url = string.Empty;
        title = null;

        var depth = 0;
        var close = -1;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '[') depth++;
            else if (text[i] == ']')
            {
                depth--;
                if (depth == 0) { close = i; break; }
            }
        }

        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(') return false;

        var cursor = close + 2;
        var destination = new StringBuilder();
        var parens = 1;

        while (cursor < text.Length)
        {
            var c = text[cursor];
            if (c == '\\' && cursor + 1 < text.Length) { destination.Append(text[cursor + 1]); cursor += 2; continue; }
            if (c == '(') parens++;
            if (c == ')') { parens--; if (parens == 0) break; }
            if (c == '"' && parens == 1) break;
            if (c == '\n') return false;
            destination.Append(c);
            cursor++;
        }

        if (cursor < text.Length && text[cursor] == '"')
        {
            var titleEnd = text[(cursor + 1)..].IndexOf('"');
            if (titleEnd < 0) return false;
            title = text.Slice(cursor + 1, titleEnd).ToString();
            cursor += titleEnd + 2;
            while (cursor < text.Length && text[cursor] == ' ') cursor++;
        }

        if (cursor >= text.Length || text[cursor] != ')') return false;

        label = text[(start + 1)..close].ToString();
        url = destination.ToString().Trim();
        end = cursor + 1;
        return true;
    }

    private static bool TryAutoLink(ReadOnlySpan<char> text, int start, out int end, out string url)
    {
        end = start;
        url = string.Empty;

        var close = text[start..].IndexOf('>');
        if (close <= 1) return false;

        var inner = text.Slice(start + 1, close - 1);
        if (inner.Contains(' ') || !inner.Contains(':') && !inner.Contains('@')) return false;

        url = inner.Contains('@') && !inner.StartsWith("mailto:") ? "mailto:" + inner.ToString() : inner.ToString();
        end = start + close + 1;
        return true;
    }

    private static bool TryRawHtml(ReadOnlySpan<char> text, int start, out int end, out string html)
    {
        end = start;
        html = string.Empty;

        var close = text[start..].IndexOf('>');
        if (close < 0) return false;

        html = text.Slice(start, close + 1).ToString();
        end = start + close + 1;
        return true;
    }

    private static bool TryBareUrl(ReadOnlySpan<char> text, int start, StringBuilder pending, out int end, out string url)
    {
        end = start;
        url = string.Empty;

        if (pending.Length > 0 && (char.IsLetterOrDigit(pending[^1]) || pending[^1] is '/' or '.' or '@')) return false;

        var rest = text[start..];
        var length = 0;
        if (rest.StartsWith("https://")) length = 8;
        else if (rest.StartsWith("http://")) length = 7;
        else if (rest.StartsWith("www.")) length = 4;
        else return false;

        var i = start + length;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('<' or '>' or '"' or '`')) i++;
        while (i > start + length && text[i - 1] is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']') i--;
        if (i <= start + length) return false;

        var raw = text[start..i].ToString();
        url = raw.StartsWith("www.", StringComparison.Ordinal) ? "https://" + raw : raw;
        end = i;
        return true;
    }

    private static bool TryDelimited(ReadOnlySpan<char> text, int start, string delimiter, out int end, out string inner)
    {
        end = start;
        inner = string.Empty;

        var from = start + delimiter.Length;
        if (from >= text.Length) return false;

        var offset = text[from..].IndexOf(delimiter, StringComparison.Ordinal);
        if (offset <= 0) return false;

        inner = text.Slice(from, offset).ToString();
        end = from + offset + delimiter.Length;
        return true;
    }

    private static bool TryEmphasis(
        ReadOnlySpan<char> text,
        int start,
        MarkdownOptions options,
        bool isComplete,
        out int end,
        out MarkdownInline? emphasis)
    {
        end = start;
        emphasis = null;

        var marker = text[start];
        var run = 0;
        while (start + run < text.Length && text[start + run] == marker) run++;

        var strong = run >= 2;
        var width = strong ? 2 : 1;

        var after = start + width < text.Length ? text[start + width] : '\0';
        if (after is '\0' or ' ' or '\t' or '\n') return false;
        if (marker == '_' && start > 0 && char.IsLetterOrDigit(text[start - 1])) return false;

        var closer = FindCloser(text, start + width, marker, width);
        if (closer < 0)
        {
            if (isComplete) return false;
            end = start + width;
            return true;
        }

        var node = new EmphasisInline(strong);
        var inner = text[(start + width)..closer].ToString();
        ParseInto(inner.AsSpan(), node.Inlines, options, isComplete);
        emphasis = node;
        end = closer + width;
        return true;
    }

    private static int FindCloser(ReadOnlySpan<char> text, int from, char marker, int width)
    {
        for (var i = from; i + width <= text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '`')
            {
                if (TryCodeSpan(text, i, out var skip, out _)) { i = skip - 1; continue; }
            }

            if (text[i] != marker) continue;

            var run = 0;
            while (i + run < text.Length && text[i + run] == marker) run++;
            if (run < width) { i += run - 1; continue; }
            if (text[i - 1] is ' ' or '\t' or '\n') { i += run - 1; continue; }
            if (marker == '_' && i + run < text.Length && char.IsLetterOrDigit(text[i + run])) { i += run - 1; continue; }

            return i;
        }

        return -1;
    }
}
