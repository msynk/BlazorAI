namespace Blazor.AI.Markdown;

/// <summary>Literal text.</summary>
/// <param name="text">The text.</param>
public sealed class TextInline(string text) : MarkdownInline
{
    /// <summary>The text.</summary>
    public string Text { get; internal set; } = text;

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>Emphasised text.</summary>
/// <param name="isStrong"><see langword="true"/> for strong emphasis.</param>
public sealed class EmphasisInline(bool isStrong) : MarkdownInline
{
    /// <summary><see langword="true"/> for strong emphasis, <see langword="false"/> for ordinary.</summary>
    public bool IsStrong { get; } = isStrong;

    /// <summary>The emphasised content.</summary>
    public List<MarkdownInline> Inlines { get; } = [];
}

/// <summary>Struck-through text.</summary>
public sealed class StrikethroughInline : MarkdownInline
{
    /// <summary>The struck content.</summary>
    public List<MarkdownInline> Inlines { get; } = [];
}

/// <summary>Inline code.</summary>
/// <param name="code">The code.</param>
public sealed class CodeInline(string code) : MarkdownInline
{
    /// <summary>The code.</summary>
    public string Code { get; } = code;
}

/// <summary>A hyperlink.</summary>
/// <param name="url">The destination.</param>
/// <param name="title">The title attribute, when present.</param>
public sealed class LinkInline(string url, string? title = null) : MarkdownInline
{
    /// <summary>The destination.</summary>
    public string Url { get; } = url;

    /// <summary>The title attribute, when present.</summary>
    public string? Title { get; } = title;

    /// <summary>The link text.</summary>
    public List<MarkdownInline> Inlines { get; } = [];

    /// <summary><see langword="true"/> when the source was a bare URL rather than bracketed.</summary>
    public bool IsAutoLink { get; internal set; }
}

/// <summary>An image.</summary>
/// <param name="url">The image source.</param>
/// <param name="alt">The alternative text.</param>
/// <param name="title">The title attribute, when present.</param>
public sealed class ImageInline(string url, string alt, string? title = null) : MarkdownInline
{
    /// <summary>The image source.</summary>
    public string Url { get; } = url;

    /// <summary>The alternative text.</summary>
    public string Alt { get; } = alt;

    /// <summary>The title attribute, when present.</summary>
    public string? Title { get; } = title;
}

/// <summary>A line break inside a paragraph.</summary>
/// <param name="isHard"><see langword="true"/> when the source forced the break.</param>
public sealed class LineBreakInline(bool isHard) : MarkdownInline
{
    /// <summary><see langword="true"/> when the source forced the break with a backslash or two spaces.</summary>
    public bool IsHard { get; } = isHard;
}

/// <summary>An inline mathematical expression.</summary>
/// <param name="expression">The expression source, without delimiters.</param>
public sealed class MathInline(string expression) : MarkdownInline
{
    /// <summary>The expression source, without delimiters.</summary>
    public string Expression { get; } = expression;
}

/// <summary>
/// A numbered citation marker, produced from a source reference in the text.
/// </summary>
/// <param name="number">The marker number.</param>
/// <param name="citationId">The citation this marker points at, when known.</param>
/// <remarks>
/// Emitted for bracketed digit runs such as <c>[3]</c> when citation parsing is enabled, so the
/// renderer can turn them into interactive source chips instead of literal brackets.
/// </remarks>
public sealed class CitationMarkerInline(int number, string? citationId = null) : MarkdownInline
{
    /// <summary>The marker number.</summary>
    public int Number { get; } = number;

    /// <summary>The citation this marker points at, when known.</summary>
    public string? CitationId { get; internal set; } = citationId;
}

/// <summary>Raw inline HTML found in the source.</summary>
/// <param name="html">The raw markup.</param>
public sealed class RawHtmlInline(string html) : MarkdownInline
{
    /// <summary>The raw markup.</summary>
    public string Html { get; } = html;
}
