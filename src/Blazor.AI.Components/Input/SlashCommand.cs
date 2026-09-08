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
/// <param name="Data">The content, base64 encoded.</param>
public sealed record ComposerFile(string Name, string ContentType, long Size, string Data)
{
    /// <summary>
    /// Converts the file into an attachment with the content inline as a data URL.
    /// </summary>
    /// <returns>The attachment.</returns>
    /// <remarks>
    /// Inline data suits screenshots and small files, which is what pasting and dropping produce.
    /// Applications that accept large uploads should intercept the composer event and upload to
    /// their own storage instead, then attach a URL.
    /// </remarks>
    public Attachment ToAttachment() => new(AiId.New("att"), Name)
    {
        ContentType = ContentType,
        Size = Size,
        Kind = Attachment.InferKind(ContentType, Name),
        Data = Convert.FromBase64String(Data),
        Url = $"data:{ContentType};base64,{Data}",
        Status = AttachmentStatus.Ready,
    };
}
