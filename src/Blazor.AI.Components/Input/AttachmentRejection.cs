namespace Blazor.AI.Components;

/// <summary>
/// Why the composer turned a file away.
/// </summary>
public enum AttachmentRejectionReason
{
    /// <summary>The file is larger than <see cref="PromptInput.MaxAttachmentSize"/>.</summary>
    TooLarge = 0,

    /// <summary>The file does not match <see cref="PromptInput.Accept"/>.</summary>
    TypeNotAllowed = 1,

    /// <summary>The browser could not deliver the content, or the transfer was interrupted.</summary>
    ReadFailed = 2,

    /// <summary>Staging the file would exceed <see cref="PromptInput.MaxAttachments"/>.</summary>
    TooMany = 3,
}

/// <summary>
/// A file the composer did not stage, and why.
/// </summary>
/// <param name="FileName">The file name.</param>
/// <param name="ContentType">The MIME type the browser reported, which may be empty.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Reason">Why the file was turned away.</param>
/// <remarks>
/// The composer always tells the user when it drops a file. This is the same information for the
/// application, which may want to log it or offer an alternative such as uploading to its own storage.
/// </remarks>
public sealed record AttachmentRejection(string FileName, string ContentType, long Size, AttachmentRejectionReason Reason);
