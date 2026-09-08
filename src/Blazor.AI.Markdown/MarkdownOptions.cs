namespace Blazor.AI.Markdown;

/// <summary>
/// Which Markdown extensions to enable and how to treat untrusted markup.
/// </summary>
/// <remarks>
/// The defaults are what an AI chat surface wants: GitHub-flavoured extensions on, raw HTML off.
/// Model output is untrusted, so rendering embedded markup is opt-in rather than opt-out.
/// </remarks>
public sealed record MarkdownOptions
{
    /// <summary>The default options.</summary>
    public static readonly MarkdownOptions Default = new();

    /// <summary>Parse pipe tables.</summary>
    public bool Tables { get; init; } = true;

    /// <summary>Parse strikethrough written with double tildes.</summary>
    public bool Strikethrough { get; init; } = true;

    /// <summary>Parse task-list checkboxes in list items.</summary>
    public bool TaskLists { get; init; } = true;

    /// <summary>Turn bare URLs into links.</summary>
    public bool AutoLinks { get; init; } = true;

    /// <summary>Parse dollar-delimited mathematics.</summary>
    public bool Math { get; init; } = true;

    /// <summary>
    /// Turn bracketed digit runs such as <c>[3]</c> into citation markers.
    /// </summary>
    public bool CitationMarkers { get; init; } = true;

    /// <summary>
    /// Keep raw HTML as markup instead of escaping it.
    /// </summary>
    /// <remarks>
    /// Leave this off unless the Markdown comes from a source you trust as much as your own code.
    /// A model can be talked into emitting a script tag.
    /// </remarks>
    public bool AllowRawHtml { get; init; }

    /// <summary>Give headings anchor identifiers derived from their text.</summary>
    public bool HeadingIds { get; init; } = true;

    /// <summary>
    /// Hide a trailing unmatched emphasis or code delimiter while the text is still streaming.
    /// </summary>
    /// <remarks>
    /// Without this, a response containing bold text flashes a literal asterisk before the closing
    /// one arrives. With it, the delimiter stays invisible until it either closes or the stream ends.
    /// </remarks>
    public bool HideDanglingDelimiters { get; init; } = true;
}
