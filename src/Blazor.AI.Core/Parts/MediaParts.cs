namespace Blazor.AI;

/// <summary>
/// An image inside a message.
/// </summary>
/// <remarks>
/// Supply <see cref="Width"/> and <see cref="Height"/> whenever they are known. The renderer uses
/// them to reserve layout space, which stops the message list from jumping as images decode during
/// streaming.
/// </remarks>
public sealed class ImagePart : MessagePart, ITextualPart
{
    /// <summary>Creates an image part.</summary>
    /// <param name="url">An absolute or data URL.</param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public ImagePart(string url, string? partId = null) : base(partId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Url = url;
    }

    /// <inheritdoc />
    public override string Kind => MessagePartKinds.Image;

    /// <summary>An absolute or data URL.</summary>
    public string Url { get; }

    /// <summary>Alternative text, which becomes the accessible name of the image.</summary>
    public string? Alt { get; set; }

    /// <summary>A caption rendered under the image.</summary>
    public string? Caption { get; set; }

    /// <summary>Intrinsic width in pixels.</summary>
    public int? Width { get; set; }

    /// <summary>Intrinsic height in pixels.</summary>
    public int? Height { get; set; }

    /// <inheritdoc />
    string? ITextualPart.Text => Alt is null ? null : $"[image: {Alt}]";
}

/// <summary>
/// A file inside a message, rendered as a downloadable card.
/// </summary>
public sealed class FilePart : MessagePart, ITextualPart
{
    /// <summary>Creates a file part.</summary>
    /// <param name="attachment">The file.</param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public FilePart(Attachment attachment, string? partId = null) : base(partId)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        Attachment = attachment;
    }

    /// <inheritdoc />
    public override string Kind => MessagePartKinds.File;

    /// <summary>The file.</summary>
    public Attachment Attachment { get; }

    /// <inheritdoc />
    string? ITextualPart.Text => $"[file: {Attachment.Name}]";
}

/// <summary>
/// A single citation rendered where it sits, for responses that place attributions inline rather
/// than in a source list.
/// </summary>
public sealed class CitationPart : MessagePart
{
    /// <summary>Creates a citation part.</summary>
    /// <param name="citation">The citation.</param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public CitationPart(Citation citation, string? partId = null) : base(partId)
    {
        ArgumentNullException.ThrowIfNull(citation);
        Citation = citation;
    }

    /// <inheritdoc />
    public override string Kind => MessagePartKinds.Citation;

    /// <summary>The citation.</summary>
    public Citation Citation { get; }
}

/// <summary>
/// A placeholder that renders the source list of the containing message at this position.
/// </summary>
/// <remarks>
/// Without this part the source list appears under the message. Insert it to place sources between
/// sections instead, for example after each retrieved section of a research answer.
/// </remarks>
public sealed class SourcesPart : MessagePart
{
    /// <summary>Creates a sources part.</summary>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public SourcesPart(string? partId = null) : base(partId) { }

    /// <inheritdoc />
    public override string Kind => MessagePartKinds.Sources;

    /// <summary>
    /// Restricts the list to these citation identifiers. When empty, every citation on the message
    /// is shown.
    /// </summary>
    public IReadOnlyList<string>? CitationIds { get; set; }
}
