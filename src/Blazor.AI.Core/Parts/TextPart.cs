using System.Text;

namespace Blazor.AI;

/// <summary>
/// A run of text that grows as the model writes it.
/// </summary>
/// <remarks>
/// <para>
/// Text is accumulated in a <see cref="StringBuilder"/> and materialised lazily, so appending a
/// token is amortised constant time rather than the quadratic string concatenation that naive
/// streaming implementations fall into on long responses.
/// </para>
/// <para>
/// The assistant body is a text part by default and is rendered as Markdown unless the host turns
/// that off. Use <see cref="MarkdownPart"/> only when the distinction has to survive serialization.
/// </para>
/// </remarks>
public class TextPart : MessagePart, ITextualPart, IStreamablePart
{
    private readonly StringBuilder _builder;
    private string? _cachedText;
    private bool _isStreaming;

    /// <summary>Creates an empty part that expects more text.</summary>
    public TextPart() : this(null, null) { }

    /// <summary>Creates a finished part with known content.</summary>
    /// <param name="text">The text.</param>
    public TextPart(string text) : this(text ?? throw new ArgumentNullException(nameof(text)), null) { }

    /// <summary>
    /// Creates a part with an explicit identifier, for transports that address parts directly.
    /// </summary>
    /// <param name="text">
    /// The text, or <see langword="null"/> for an empty part that expects more.
    /// </param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    /// <remarks>
    /// Both parameters are required rather than optional. An optional overload would make
    /// <c>new TextPart("hello")</c> ambiguous between text and identifier, and the compiler resolves
    /// that ambiguity the wrong way round.
    /// </remarks>
    public TextPart(string? text, string? partId) : base(partId)
    {
        _builder = new StringBuilder(text ?? string.Empty, 256);
        _cachedText = text;
        _isStreaming = text is null;
    }

    /// <inheritdoc />
    public override string Kind => MessagePartKinds.Text;

    /// <summary>The text accumulated so far.</summary>
    public string Text => _cachedText ??= _builder.ToString();

    /// <summary>How many characters have arrived, without materialising the string.</summary>
    /// <remarks>
    /// Prefer this over <c>Text.Length</c> in hot paths such as auto-scroll checks: it avoids
    /// building the string.
    /// </remarks>
    public int Length => _builder.Length;

    /// <inheritdoc />
    public bool IsStreaming => _isStreaming;

    /// <summary><see langword="true"/> when no text has arrived yet.</summary>
    public bool IsEmpty => _builder.Length == 0;

    /// <summary>
    /// Appends text and notifies observers.
    /// </summary>
    /// <param name="delta">The text to append. An empty delta is ignored.</param>
    public void Append(string? delta)
    {
        if (string.IsNullOrEmpty(delta)) return;
        _builder.Append(delta);
        _cachedText = null;
        NotifyChanged();
    }

    /// <summary>
    /// Replaces the whole content, for edits and regenerations.
    /// </summary>
    /// <param name="text">The new text.</param>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _builder.Clear();
        _builder.Append(text);
        _cachedText = text;
        NotifyChanged();
    }

    /// <summary>
    /// Marks the part finished, which lets renderers do work they deferred while it was growing.
    /// </summary>
    public void Complete()
    {
        if (!_isStreaming) return;
        _isStreaming = false;
        OnCompleted();
        NotifyChanged();
    }

    /// <summary>Called once when the part finishes, before observers are notified.</summary>
    protected virtual void OnCompleted() { }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// Text that is explicitly Markdown, for hosts that render plain text parts verbatim.
/// </summary>
public sealed class MarkdownPart : TextPart
{
    /// <summary>Creates an empty part that expects more text.</summary>
    public MarkdownPart() : base(null, null) { }

    /// <summary>Creates a finished part with known content.</summary>
    /// <param name="text">The Markdown source.</param>
    public MarkdownPart(string text) : base(text ?? throw new ArgumentNullException(nameof(text)), null) { }

    /// <summary>Creates a part with an explicit identifier.</summary>
    /// <param name="text">The Markdown source, or <see langword="null"/> for an empty part.</param>
    /// <param name="partId">The identifier, or <see langword="null"/> to generate one.</param>
    public MarkdownPart(string? text, string? partId) : base(text, partId) { }

    /// <inheritdoc />
    public override string Kind => MessagePartKinds.Markdown;
}
