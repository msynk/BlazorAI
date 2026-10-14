using System.Collections.Concurrent;

namespace Blazor.AI;

/// <summary>
/// An ordered set of messages plus the artifacts and state that belong to the whole thread.
/// </summary>
/// <remarks>
/// <para>
/// The conversation is the unit of state a chat UI binds to. It is observable at three grains:
/// <see cref="Changed"/> for thread-level edits, <see cref="MessageAdded"/> and
/// <see cref="MessageRemoved"/> for list structure, and per-message events for content. A message
/// list can therefore keep its virtualized rows stable while one message streams.
/// </para>
/// <para>
/// Artifacts are held here rather than on messages so revisions across turns update one object.
/// </para>
/// <para>
/// Safe for one writer and concurrent readers. The message list is copy-on-write and the artifact
/// and run maps are concurrent, because the stream mutates them off the renderer's thread while the
/// list is being enumerated.
/// </para>
/// </remarks>
public sealed class Conversation : IConversation
{
    private readonly object _gate = new();
    private volatile AiMessage[] _messages = [];
    private readonly ConcurrentDictionary<string, Artifact> _artifacts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AgentRun> _runs = new(StringComparer.Ordinal);
    private int _version;

    /// <summary>Creates a conversation.</summary>
    /// <param name="conversationId">The identifier, or <see langword="null"/> to generate one.</param>
    public Conversation(string? conversationId = null)
    {
        ConversationId = conversationId ?? AiId.NewConversationId();
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public string ConversationId { get; }

    /// <inheritdoc />
    public string? Title { get; private set; }

    /// <summary>The messages, oldest first.</summary>
    public IReadOnlyList<AiMessage> Messages => _messages;

    /// <inheritdoc />
    IReadOnlyList<IAiMessage> IConversation.Messages => _messages;

    /// <summary>The artifacts produced in this conversation, keyed by identifier.</summary>
    public IReadOnlyDictionary<string, Artifact> Artifacts => _artifacts;

    /// <summary>The agent runs in this conversation, keyed by identifier.</summary>
    public IReadOnlyDictionary<string, AgentRun> Runs => _runs;

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; init; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Application-defined values attached to the conversation.</summary>
    public Dictionary<string, object?> Extensions { get; } = [];

    /// <summary>Increments on every change, for cheap render checks.</summary>
    public int Version => _version;

    /// <summary>The last message, or <see langword="null"/> when the conversation is empty.</summary>
    public AiMessage? LastMessage => _messages is [.., var last] ? last : null;

    /// <summary><see langword="true"/> when a message is pending or streaming.</summary>
    public bool IsGenerating => LastMessage is { IsBusy: true };

    /// <summary>Token usage summed across every assistant message.</summary>
    public TokenUsage TotalUsage
    {
        get
        {
            var total = default(TokenUsage);
            foreach (var message in _messages)
            {
                if (message.Metadata is { } metadata) total += metadata.Usage;
            }

            return total;
        }
    }

    /// <summary>Raised after a thread-level change, such as the title or an artifact.</summary>
    public event Action<Conversation>? Changed;

    /// <summary>Raised after a message is appended or inserted.</summary>
    public event Action<Conversation, AiMessage>? MessageAdded;

    /// <summary>Raised after a message is removed.</summary>
    public event Action<Conversation, AiMessage>? MessageRemoved;

    /// <summary>Raised when any message reports a change, structural or in one of its parts.</summary>
    /// <remarks>
    /// Convenient for coarse observers such as auto-save. Components should subscribe to the message
    /// itself instead, so one token does not invalidate the whole list.
    /// </remarks>
    public event Action<Conversation, AiMessage>? MessageUpdated;

    /// <summary>Appends a message.</summary>
    /// <param name="message">The message to append.</param>
    /// <returns>The appended message.</returns>
    public AiMessage AddMessage(AiMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Attach(message);
        lock (_gate) _messages = [.. _messages, message];
        Touch();
        MessageAdded?.Invoke(this, message);
        return message;
    }

    /// <summary>Appends a message built from text.</summary>
    /// <param name="role">Who authored it.</param>
    /// <param name="text">The message text.</param>
    /// <returns>The appended message.</returns>
    public AiMessage AddMessage(AiRole role, string text)
    {
        var message = new AiMessage(role);
        message.AddPart(new TextPart(text));
        return AddMessage(message);
    }

    /// <summary>Removes a message.</summary>
    /// <param name="messageId">The message to remove.</param>
    /// <returns><see langword="true"/> when a message was removed.</returns>
    public bool RemoveMessage(string messageId)
    {
        AiMessage message;
        lock (_gate)
        {
            var messages = _messages;
            var index = Array.FindIndex(messages, m => m.MessageId == messageId);
            if (index < 0) return false;
            message = messages[index];
            _messages = [.. messages.AsSpan(0, index), .. messages.AsSpan(index + 1)];
        }

        Detach(message);
        Touch();
        MessageRemoved?.Invoke(this, message);
        return true;
    }

    /// <summary>Finds a message by identifier.</summary>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>The message, or <see langword="null"/> when it is not in this conversation.</returns>
    public AiMessage? FindMessage(string messageId)
    {
        var messages = _messages;
        for (var i = messages.Length - 1; i >= 0; i--)
        {
            if (messages[i].MessageId == messageId) return messages[i];
        }

        return null;
    }

    /// <summary>
    /// Removes every message after the given one, which is how retry and edit rewind the thread.
    /// </summary>
    /// <param name="messageId">The message to keep as the new last message.</param>
    /// <param name="inclusive">When <see langword="true"/>, removes the named message as well.</param>
    /// <returns>The removed messages, newest first.</returns>
    public IReadOnlyList<AiMessage> TruncateAfter(string messageId, bool inclusive = false)
    {
        List<AiMessage> removed;
        lock (_gate)
        {
            var messages = _messages;
            var index = Array.FindIndex(messages, m => m.MessageId == messageId);
            if (index < 0) return [];

            var from = inclusive ? index : index + 1;
            if (from >= messages.Length) return [];

            removed = [.. messages.AsSpan(from)];
            _messages = messages[..from];
        }

        foreach (var message in removed)
        {
            Detach(message);
            MessageRemoved?.Invoke(this, message);
        }

        Touch();
        removed.Reverse();
        return removed;
    }

    /// <summary>Removes every message.</summary>
    public void Clear()
    {
        AiMessage[] removed;
        lock (_gate)
        {
            removed = _messages;
            _messages = [];
        }

        foreach (var message in removed) Detach(message);
        foreach (var message in removed) MessageRemoved?.Invoke(this, message);
        Touch();
    }

    /// <summary>Sets the conversation title.</summary>
    /// <param name="title">The new title.</param>
    public void SetTitle(string? title)
    {
        if (Title == title) return;
        Title = title;
        Touch();
    }

    /// <summary>Finds an artifact, creating it when it does not exist yet.</summary>
    /// <param name="artifactId">The artifact identifier.</param>
    /// <param name="kind">The kind to use when creating.</param>
    /// <param name="title">The title to use when creating.</param>
    /// <returns>The artifact.</returns>
    public Artifact GetOrAddArtifact(string artifactId, ArtifactKind kind, string title)
    {
        if (_artifacts.TryGetValue(artifactId, out var existing)) return existing;
        var artifact = new Artifact(artifactId, kind, title);
        if (!_artifacts.TryAdd(artifactId, artifact)) return _artifacts[artifactId];
        artifact.Changed += OnArtifactChanged;
        Touch();
        return artifact;
    }

    /// <summary>Finds an artifact.</summary>
    /// <param name="artifactId">The artifact identifier.</param>
    /// <returns>The artifact, or <see langword="null"/> when it does not exist.</returns>
    public Artifact? FindArtifact(string artifactId) =>
        _artifacts.TryGetValue(artifactId, out var artifact) ? artifact : null;

    /// <summary>Finds an agent run, creating it when it does not exist yet.</summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The run.</returns>
    public AgentRun GetOrAddRun(string runId)
    {
        if (_runs.TryGetValue(runId, out var existing)) return existing;
        var run = new AgentRun(runId);
        if (!_runs.TryAdd(runId, run)) return _runs[runId];
        run.Changed += OnRunChanged;
        Touch();
        return run;
    }

    /// <summary>Finds an agent run.</summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The run, or <see langword="null"/> when it does not exist.</returns>
    public AgentRun? FindRun(string runId) => _runs.TryGetValue(runId, out var run) ? run : null;

    /// <summary>Projects the conversation into the shape a conversation list renders.</summary>
    /// <returns>The summary.</returns>
    public ConversationSummary ToSummary()
    {
        var preview = LastMessage?.GetText();
        if (preview is { Length: > 140 }) preview = string.Concat(preview.AsSpan(0, 140), "...");
        return new ConversationSummary(ConversationId, Title, UpdatedAt, preview, _messages.Length);
    }

    private void Attach(AiMessage message)
    {
        message.Changed += OnMessageChanged;
        message.PartChanged += OnMessagePartChanged;
    }

    private void Detach(AiMessage message)
    {
        message.Changed -= OnMessageChanged;
        message.PartChanged -= OnMessagePartChanged;
    }

    private void OnMessageChanged(AiMessage message)
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        MessageUpdated?.Invoke(this, message);
    }

    private void OnMessagePartChanged(AiMessage message, MessagePart part)
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        MessageUpdated?.Invoke(this, message);
    }

    private void OnArtifactChanged(Artifact artifact) => Touch();

    private void OnRunChanged(AgentRun run) => Touch();

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _version);
        Changed?.Invoke(this);
    }
}
