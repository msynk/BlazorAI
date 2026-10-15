using Microsoft.JSInterop;

namespace Blazor.AI.Components;

/// <summary>
/// A command offered when the user types a forward slash in the composer.
/// </summary>
/// <param name="Name">The command word, without the slash.</param>
/// <param name="Description">What it does, shown beside the name.</param>
/// <param name="Icon">An icon name from the built-in set.</param>
/// <remarks>
/// Selecting a command either inserts its <see cref="Template"/> into the composer or invokes
/// <see cref="Handler"/>. The first suits a prompt shortcut, the second an application action such
/// as clearing the conversation.
/// </remarks>
public sealed record SlashCommand(string Name, string? Description = null, string? Icon = null)
{
    /// <summary>Text inserted into the composer when the command is chosen.</summary>
    public string? Template { get; init; }

    /// <summary>Invoked when the command is chosen, instead of inserting text.</summary>
    public Func<Task>? Handler { get; init; }
}

/// <summary>
/// Something the user can reference by typing an at sign in the composer: a file, a person, a data
/// source.
/// </summary>
/// <param name="Id">The stable identifier the application resolves later.</param>
/// <param name="Label">What the user sees and what is inserted into the text.</param>
/// <param name="Description">Extra detail shown beside the label.</param>
/// <param name="Icon">An icon name from the built-in set.</param>
public sealed record MentionItem(string Id, string Label, string? Description = null, string? Icon = null);

/// <summary>
/// A file handed to the composer by the browser, from a paste or a drop.
/// </summary>
/// <param name="Name">The file name.</param>
/// <param name="ContentType">The MIME type.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Data">
/// The content, base64 encoded, when it was sent inline. The browser helpers send
/// <see cref="Content"/> instead.
/// </param>
/// <remarks>
/// The browser describes the file first and streams the bytes only when they are read. A single
/// interop message carrying the whole file would exceed the SignalR message limit on Blazor Server
/// (32 KB by default) for anything but a small screenshot, and the file would be dropped.
/// </remarks>
public sealed record ComposerFile(string Name, string ContentType, long Size, string? Data = null)
{
    /// <summary>
    /// The content as a browser stream, or <see langword="null"/> when it was sent inline in
    /// <see cref="Data"/> or withheld because the file was over the size limit.
    /// </summary>
    /// <remarks>Read it with <see cref="ToAttachmentAsync"/>, which also releases it.</remarks>
    public IJSStreamReference? Content { get; init; }

    /// <summary>
    /// Converts a file sent inline into an attachment with the content as a data URL.
    /// </summary>
    /// <returns>The attachment.</returns>
    /// <exception cref="InvalidOperationException">
    /// The content was not sent inline; use <see cref="ToAttachmentAsync"/>.
    /// </exception>
    /// <remarks>
    /// Inline data suits screenshots and small files, which is what pasting and dropping produce.
    /// Applications that accept large uploads should intercept the composer event and upload to
    /// their own storage instead, then attach a URL.
    /// </remarks>
    public Attachment ToAttachment()
    {
        if (Data is null)
        {
            throw new InvalidOperationException(
                $"'{Name}' was not sent inline. Read it with {nameof(ToAttachmentAsync)} instead.");
        }

        return Create(Convert.FromBase64String(Data));
    }

    /// <summary>
    /// Reads the content, from <see cref="Content"/> or <see cref="Data"/>, into an attachment with
    /// the content as a data URL, then releases the browser stream.
    /// </summary>
    /// <param name="maxAllowedSize">The most bytes to accept; a longer stream fails the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The attachment.</returns>
    /// <exception cref="InvalidOperationException">The file carries no content at all.</exception>
    /// <remarks>
    /// On Blazor Server the stream arrives in chunks sized to fit the circuit, so the file size is
    /// bounded by <paramref name="maxAllowedSize"/> rather than by the SignalR message limit.
    /// </remarks>
    public async Task<Attachment> ToAttachmentAsync(long maxAllowedSize, CancellationToken cancellationToken = default)
    {
        if (Content is not { } content) return ToAttachment();

        await using (content.ConfigureAwait(false))
        {
            await using var stream = await content.OpenReadStreamAsync(maxAllowedSize, cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream((int)Math.Clamp(Size, 0, Math.Min(maxAllowedSize, int.MaxValue)));
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return Create(buffer.ToArray());
        }
    }

    private Attachment Create(byte[] bytes) => CreateAttachment(Name, ContentType, bytes);

    internal static Attachment CreateAttachment(string name, string contentType, byte[] bytes) => new(AiId.New("att"), name)
    {
        ContentType = contentType,
        Size = bytes.LongLength,
        Kind = Attachment.InferKind(contentType, name),
        Data = bytes,
        Url = $"data:{contentType};base64,{Convert.ToBase64String(bytes)}",
        Status = AttachmentStatus.Ready,
    };
}
