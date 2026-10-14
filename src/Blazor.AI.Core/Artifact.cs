using System.Text;

namespace Blazor.AI;

/// <summary>
/// A substantial piece of content produced alongside a conversation, revised across turns and shown
/// in its own panel.
/// </summary>
/// <remarks>
/// <para>
/// Artifacts belong to the conversation rather than to a message, so an assistant that rewrites a
/// document three times updates one object and the UI keeps one panel with a version history,
/// instead of scattering three near-identical code blocks through the transcript.
/// </para>
/// <para>
/// Content is accumulated in a builder for the same reason text parts are: a long artifact streamed
/// character by character must not be quadratic.
/// </para>
/// <para>
/// Safe for one writer and concurrent readers: the panel renders while content streams in on the
/// transport's thread, so the builder is only touched under a lock, readers get a cached snapshot,
/// and the version history is replaced rather than mutated.
/// </para>
/// </remarks>
public sealed class Artifact : IArtifact
{
    private readonly StringBuilder _content = new(1024);
    private readonly object _gate = new();
    private volatile ArtifactVersion[] _versions = [];
    private volatile string? _cachedContent;
    private volatile int _length;
    private int _version;

    /// <summary>Creates an artifact.</summary>
    /// <param name="artifactId">The identifier, or <see langword="null"/> to generate one.</param>
    /// <param name="kind">The artifact type, which selects a renderer.</param>
    /// <param name="title">The display title.</param>
    public Artifact(string? artifactId, ArtifactKind kind, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArtifactId = artifactId ?? AiId.NewArtifactId();
        Kind = kind;
        Title = title;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public string ArtifactId { get; }

    /// <inheritdoc />
    public ArtifactKind Kind { get; }

    /// <inheritdoc />
    public string Title { get; private set; }

    /// <inheritdoc />
    public string? Content
    {
        get
        {
            if (_cachedContent is { } cached) return cached;
            lock (_gate) return _cachedContent ??= _content.ToString();
        }
    }

    /// <inheritdoc />
    public string? Language { get; set; }

    /// <inheritdoc />
    public string? Url { get; set; }

    /// <inheritdoc />
    public int Version { get; private set; } = 1;

    /// <inheritdoc />
    public bool IsStreaming { get; private set; } = true;

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?>? Metadata { get; set; }

    /// <summary>Every committed version, oldest first. The current draft is not included.</summary>
    public IReadOnlyList<ArtifactVersion> Versions => _versions;

    /// <summary>How many characters the current content holds, without materialising it.</summary>
    public int Length => _length;

    /// <summary>Increments on every change, for cheap render checks.</summary>
    public int ChangeVersion => _version;

    /// <summary>Raised after the artifact changes.</summary>
    public event Action<Artifact>? Changed;

    /// <summary>Appends content.</summary>
    /// <param name="delta">The text to append.</param>
    public void Append(string? delta)
    {
        if (string.IsNullOrEmpty(delta)) return;
        lock (_gate)
        {
            _content.Append(delta);
            _length = _content.Length;
            _cachedContent = null;
        }

        Touch();
    }

    /// <summary>Replaces the content wholesale.</summary>
    /// <param name="content">The new content.</param>
    public void SetContent(string? content)
    {
        lock (_gate)
        {
            _content.Clear();
            if (content is not null) _content.Append(content);
            _length = _content.Length;
            _cachedContent = content;
        }

        Touch();
    }

    /// <summary>Renames the artifact.</summary>
    /// <param name="title">The new title.</param>
    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
        Touch();
    }

    /// <summary>
    /// Commits the current content as a version and stops streaming.
    /// </summary>
    /// <param name="summary">A short description of what changed.</param>
    public void Complete(string? summary = null)
    {
        // Completing a finished revision again, for example a created event that carried the content
        // followed by a completed event, must not record the same version twice.
        if (!IsStreaming) return;
        IsStreaming = false;
        _versions = [.. _versions, new ArtifactVersion(Version, Content, DateTimeOffset.UtcNow, summary)];
        Touch();
    }

    /// <summary>
    /// Starts a new revision, leaving the previous version in the history.
    /// </summary>
    /// <remarks>
    /// Call this before streaming a rewrite so that the version selector can still show what the
    /// artifact looked like before.
    /// </remarks>
    public void BeginRevision()
    {
        if (IsStreaming) Complete();
        Version++;
        IsStreaming = true;
        lock (_gate)
        {
            _content.Clear();
            _length = 0;
            _cachedContent = null;
        }

        Touch();
    }

    /// <summary>Restores the content of an earlier version as a new revision.</summary>
    /// <param name="version">The version to restore.</param>
    /// <returns><see langword="true"/> when the version exists and was restored.</returns>
    public bool Restore(int version)
    {
        var snapshot = _versions.FirstOrDefault(v => v.Version == version);
        if (snapshot is null) return false;
        BeginRevision();
        SetContent(snapshot.Content);
        Complete($"Restored version {version}");
        return true;
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _version);
        Changed?.Invoke(this);
    }
}
