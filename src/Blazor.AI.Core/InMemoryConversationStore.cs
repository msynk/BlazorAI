using System.Collections.Concurrent;

namespace Blazor.AI;

/// <summary>
/// A conversation store that keeps everything in process memory.
/// </summary>
/// <remarks>
/// Useful for demos, tests, and Blazor Server apps where a conversation only has to outlive a page
/// navigation. It is not durable: restart the process and the conversations are gone. Implement
/// <see cref="IConversationStore"/> over a database for anything else.
/// </remarks>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, IConversation> _conversations = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ConversationSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var summaries = _conversations.Values
            .OrderByDescending(c => c.UpdatedAt)
            .Select(Summarize)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<ConversationSummary>>(summaries);
    }

    /// <inheritdoc />
    public ValueTask<IConversation?> LoadAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversationId);
        _conversations.TryGetValue(conversationId, out var conversation);
        return ValueTask.FromResult(conversation);
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(IConversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        _conversations[conversation.ConversationId] = conversation;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversationId);
        _conversations.TryRemove(conversationId, out _);
        return ValueTask.CompletedTask;
    }

    private static ConversationSummary Summarize(IConversation conversation)
    {
        if (conversation is Conversation typed) return typed.ToSummary();

        var preview = conversation.Messages.Count > 0
            ? conversation.Messages[^1].GetText()
            : null;

        if (preview is { Length: > 140 }) preview = string.Concat(preview.AsSpan(0, 140), "...");

        return new ConversationSummary(
            conversation.ConversationId,
            conversation.Title,
            conversation.UpdatedAt,
            preview,
            conversation.Messages.Count);
    }
}
