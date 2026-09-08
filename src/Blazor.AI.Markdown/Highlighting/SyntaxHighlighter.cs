namespace Blazor.AI.Markdown;

/// <summary>
/// Turns source code into coloured spans.
/// </summary>
/// <remarks>
/// <para>
/// Lexical rather than syntactic, and therefore total: it produces sensible output for a fragment
/// that is still being written, which a grammar-based highlighter cannot. That matters more here
/// than perfect fidelity, because most code this framework renders is half-finished most of the time.
/// </para>
/// <para>
/// Only non-plain spans are returned, in ascending order and never overlapping. A renderer walks
/// the tokens and emits the gaps between them as unstyled text, so whitespace and ordinary
/// identifiers cost nothing.
/// </para>
/// </remarks>
public static class SyntaxHighlighter
{
    /// <summary>
    /// Highlights source code.
    /// </summary>
    /// <param name="code">The source.</param>
    /// <param name="language">The language name from the fence info string.</param>
    /// <returns>
    /// The non-plain spans, in order. Empty when the language is unknown, which renders as plain text.
    /// </returns>
    public static IReadOnlyList<CodeToken> Highlight(string? code, string? language)
    {
        if (string.IsNullOrEmpty(code)) return [];

        var definition = LanguageRegistry.Find(language);
        if (definition is null) return [];

        var tokens = new List<CodeToken>(Math.Max(8, code.Length / 8));

        if (definition.Id == "diff") ScanDiff(code, tokens);
        else if (definition.IsMarkup) ScanMarkup(code, definition, tokens);
        else ScanGeneral(code, definition, tokens);

        return tokens;
    }

    private static void ScanGeneral(string code, LanguageDefinition language, List<CodeToken> tokens)
    {
        var comparison = language.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var i = 0;

        while (i < code.Length)
        {
            var c = code[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (TryLineComment(code, i, language, out var commentEnd))
            {
                tokens.Add(new CodeToken(TokenKind.Comment, i, commentEnd - i));
                i = commentEnd;
                continue;
            }

            if (TryBlockComment(code, i, language, out var blockEnd))
            {
                tokens.Add(new CodeToken(TokenKind.Comment, i, blockEnd - i));
                i = blockEnd;
                continue;
            }

            if (language.StringDelimiters.Contains(c))
            {
                var end = ScanString(code, i, language);
                tokens.Add(new CodeToken(TokenKind.String, i, end - i));
                i = end;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < code.Length && char.IsAsciiDigit(code[i + 1])))
            {
                var end = ScanNumber(code, i);
                tokens.Add(new CodeToken(TokenKind.Number, i, end - i));
                i = end;
                continue;
            }

            if (language.AnnotationPrefix == c && i + 1 < code.Length && IsWordStart(code[i + 1]))
            {
                var end = i + 1;
                while (end < code.Length && IsWordChar(code[end])) end++;
                tokens.Add(new CodeToken(TokenKind.Attribute, i, end - i));
                i = end;
                continue;
            }

            if (IsWordStart(c))
            {
                var end = i;
                while (end < code.Length && IsWordChar(code[end])) end++;
                var word = code[i..end];
                tokens.Add(new CodeToken(Classify(word, code, end, language, comparison), i, end - i));
                i = end;
                continue;
            }

            if (IsPunctuation(c))
            {
                tokens.Add(new CodeToken(TokenKind.Punctuation, i, 1));
                i++;
                continue;
            }

            if (IsOperator(c))
            {
                var end = i;
                while (end < code.Length && IsOperator(code[end])) end++;
                tokens.Add(new CodeToken(TokenKind.Operator, i, end - i));
                i = end;
                continue;
            }

            i++;
        }
    }

    private static TokenKind Classify(
        string word,
        string code,
        int end,
        LanguageDefinition language,
        StringComparison comparison)
    {
        if (Contains(language.Constants, word, comparison)) return TokenKind.Constant;
        if (Contains(language.Keywords, word, comparison)) return TokenKind.Keyword;
        if (Contains(language.Types, word, comparison)) return TokenKind.Type;

        var next = NextNonSpace(code, end);
        if (next == '(') return TokenKind.Function;
        if (language.ColonProperties && next == ':') return TokenKind.Property;
        if (char.IsUpper(word[0]) && word.Length > 1) return TokenKind.Type;

        return TokenKind.Plain;
    }

    private static bool Contains(IReadOnlySet<string> set, string word, StringComparison comparison)
    {
        if (comparison == StringComparison.Ordinal) return set.Contains(word);
        foreach (var candidate in set)
        {
            if (string.Equals(candidate, word, comparison)) return true;
        }

        return false;
    }

    private static char NextNonSpace(string code, int index)
    {
        for (var i = index; i < code.Length; i++)
        {
            if (code[i] is ' ' or '\t') continue;
            return code[i];
        }

        return '\0';
    }

    private static bool TryLineComment(string code, int index, LanguageDefinition language, out int end)
    {
        end = index;
        foreach (var marker in language.LineComments)
        {
            if (!MatchesAt(code, index, marker)) continue;
            var newline = code.IndexOf('\n', index);
            end = newline < 0 ? code.Length : newline;
            return true;
        }

        return false;
    }

    private static bool TryBlockComment(string code, int index, LanguageDefinition language, out int end)
    {
        end = index;
        foreach (var (open, close) in language.BlockComments)
        {
            if (!MatchesAt(code, index, open)) continue;
            var closeIndex = code.IndexOf(close, index + open.Length, StringComparison.Ordinal);
            end = closeIndex < 0 ? code.Length : closeIndex + close.Length;
            return true;
        }

        return false;
    }

    private static bool MatchesAt(string code, int index, string value) =>
        value.Length > 0 && index + value.Length <= code.Length
        && code.AsSpan(index, value.Length).SequenceEqual(value);

    private static int ScanString(string code, int start, LanguageDefinition language)
    {
        var quote = code[start];
        var i = start + 1;

        while (i < code.Length)
        {
            var c = code[i];

            if (language.StringEscapes && c == '\\' && i + 1 < code.Length)
            {
                i += 2;
                continue;
            }

            if (c == quote) return i + 1;
            if (c == '\n' && !language.MultiLineStrings && quote != '`') return i;
            i++;
        }

        return code.Length;
    }

    private static int ScanNumber(string code, int start)
    {
        var i = start;
        if (i + 1 < code.Length && code[i] == '0' && (code[i + 1] is 'x' or 'X' or 'b' or 'B' or 'o' or 'O'))
        {
            i += 2;
            while (i < code.Length && (char.IsAsciiLetterOrDigit(code[i]) || code[i] == '_')) i++;
            return i;
        }

        var seenDot = false;
        var seenExponent = false;

        while (i < code.Length)
        {
            var c = code[i];
            if (char.IsAsciiDigit(c) || c == '_') { i++; continue; }
            if (c == '.' && !seenDot && !seenExponent) { seenDot = true; i++; continue; }
            if ((c is 'e' or 'E') && !seenExponent && i + 1 < code.Length
                && (char.IsAsciiDigit(code[i + 1]) || code[i + 1] is '+' or '-'))
            {
                seenExponent = true;
                i += 2;
                continue;
            }

            if (c is 'f' or 'F' or 'd' or 'D' or 'm' or 'M' or 'L' or 'l' or 'u' or 'U') { i++; continue; }
            break;
        }

        return i;
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_' || c == '$';

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static bool IsPunctuation(char c) => c is '(' or ')' or '[' or ']' or '{' or '}' or ',' or ';';

    private static bool IsOperator(char c) =>
        c is '+' or '-' or '*' or '/' or '%' or '=' or '<' or '>' or '!' or '&' or '|' or '^' or '~'
            or '?' or ':' or '.' or '@' or '#';

    private static void ScanMarkup(string code, LanguageDefinition language, List<CodeToken> tokens)
    {
        var i = 0;

        while (i < code.Length)
        {
            var open = code.IndexOf('<', i);
            if (open < 0) break;

            if (MatchesAt(code, open, "<!--"))
            {
                var close = code.IndexOf("-->", open, StringComparison.Ordinal);
                var end = close < 0 ? code.Length : close + 3;
                tokens.Add(new CodeToken(TokenKind.Comment, open, end - open));
                i = end;
                continue;
            }

            var tagEnd = code.IndexOf('>', open);
            if (tagEnd < 0) tagEnd = code.Length - 1;

            ScanTag(code, open, tagEnd, tokens);
            i = tagEnd + 1;
        }
    }

    private static void ScanTag(string code, int open, int close, List<CodeToken> tokens)
    {
        var i = open + 1;
        if (i < code.Length && code[i] == '/') i++;

        var nameStart = i;
        while (i <= close && i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] is '-' or '_' or ':')) i++;
        if (i > nameStart) tokens.Add(new CodeToken(TokenKind.Tag, nameStart, i - nameStart));

        while (i < close && i < code.Length)
        {
            var c = code[i];

            if (char.IsWhiteSpace(c) || c is '/' or '=') { i++; continue; }

            if (c is '"' or '\'')
            {
                var end = ScanString(code, i, BuiltInLanguages.Html with { StringDelimiters = ['"', '\''] });
                end = Math.Min(end, close + 1);
                tokens.Add(new CodeToken(TokenKind.String, i, end - i));
                i = end;
                continue;
            }

            var attributeStart = i;
            while (i < close && i < code.Length && !char.IsWhiteSpace(code[i]) && code[i] is not ('=' or '/' or '>')) i++;
            if (i > attributeStart) tokens.Add(new CodeToken(TokenKind.Attribute, attributeStart, i - attributeStart));
            else i++;
        }
    }

    private static void ScanDiff(string code, List<CodeToken> tokens)
    {
        var i = 0;

        while (i < code.Length)
        {
            var newline = code.IndexOf('\n', i);
            var end = newline < 0 ? code.Length : newline;
            var length = end - i;

            if (length > 0)
            {
                var kind = code[i] switch
                {
                    '+' when !MatchesAt(code, i, "+++") => TokenKind.Inserted,
                    '-' when !MatchesAt(code, i, "---") => TokenKind.Deleted,
                    '@' => TokenKind.Meta,
                    '+' or '-' => TokenKind.Meta,
                    'd' when MatchesAt(code, i, "diff ") => TokenKind.Meta,
                    'i' when MatchesAt(code, i, "index ") => TokenKind.Meta,
                    _ => TokenKind.Plain,
                };

                if (kind != TokenKind.Plain) tokens.Add(new CodeToken(kind, i, length));
            }

            i = end + 1;
        }
    }
}
