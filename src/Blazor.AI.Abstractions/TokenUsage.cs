namespace Blazor.AI;

/// <summary>
/// Token accounting for one or more generations.
/// </summary>
/// <remarks>
/// <para>
/// Every field is optional because providers report different subsets. Instances are additive via
/// the addition operator, which is how a message accumulates the usage of each request a tool loop
/// makes, and a run the usage of every request made within it.
/// </para>
/// <para>
/// Addition is field by field and keeps absence: a field missing on both sides stays missing, and
/// one missing on a single side takes the other side's value. A total therefore never claims a zero
/// the provider did not report.
/// </para>
/// </remarks>
public readonly record struct TokenUsage
{
    /// <summary>Tokens consumed by the prompt.</summary>
    public long? InputTokens { get; init; }

    /// <summary>Tokens produced by the model.</summary>
    public long? OutputTokens { get; init; }

    /// <summary>Input tokens served from a provider-side prompt cache.</summary>
    public long? CachedInputTokens { get; init; }

    /// <summary>Input tokens written to a provider-side prompt cache.</summary>
    public long? CacheWriteTokens { get; init; }

    /// <summary>Tokens spent on hidden reasoning, where the provider reports it separately.</summary>
    public long? ReasoningTokens { get; init; }

    /// <summary>Cost in the currency named by <see cref="Currency"/>, when the host can compute it.</summary>
    public decimal? Cost { get; init; }

    /// <summary>ISO 4217 code for <see cref="Cost"/>. Defaults to USD when a cost is present.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Total tokens, summing input and output when either is present.
    /// </summary>
    public long? TotalTokens => InputTokens is null && OutputTokens is null
        ? null
        : (InputTokens ?? 0) + (OutputTokens ?? 0);

    /// <summary><see langword="true"/> when no field carries a value.</summary>
    public bool IsEmpty => InputTokens is null && OutputTokens is null && CachedInputTokens is null
        && CacheWriteTokens is null && ReasoningTokens is null && Cost is null;

    /// <summary>
    /// Adds two usage records field by field, treating a value absent on both sides as absent.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The combined usage.</returns>
    public static TokenUsage operator +(TokenUsage left, TokenUsage right) => new()
    {
        InputTokens = Sum(left.InputTokens, right.InputTokens),
        OutputTokens = Sum(left.OutputTokens, right.OutputTokens),
        CachedInputTokens = Sum(left.CachedInputTokens, right.CachedInputTokens),
        CacheWriteTokens = Sum(left.CacheWriteTokens, right.CacheWriteTokens),
        ReasoningTokens = Sum(left.ReasoningTokens, right.ReasoningTokens),
        Cost = left.Cost is null && right.Cost is null ? null : (left.Cost ?? 0m) + (right.Cost ?? 0m),
        Currency = left.Currency ?? right.Currency,
    };

    /// <summary>Adds two usage records. Equivalent to the addition operator.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The combined usage.</returns>
    public static TokenUsage Add(TokenUsage left, TokenUsage right) => left + right;

    private static long? Sum(long? a, long? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);
}

/// <summary>
/// Everything known about how a message was produced, separate from what it says.
/// </summary>
/// <remarks>
/// Held apart from the message body so that high-frequency streaming updates never touch it.
/// </remarks>
public sealed record GenerationMetadata
{
    /// <summary>Identifier of the model that produced the message.</summary>
    public string? ModelId { get; init; }

    /// <summary>Identifier of the provider that served the request.</summary>
    public string? ProviderId { get; init; }

    /// <summary>Token accounting for the generation.</summary>
    public TokenUsage Usage { get; init; }

    /// <summary>Why the generation stopped.</summary>
    public FinishReason FinishReason { get; init; }

    /// <summary>When the request was sent.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the last event arrived.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Time from request to the first streamed token.</summary>
    public TimeSpan? TimeToFirstToken { get; init; }

    /// <summary>Total wall-clock duration of the generation.</summary>
    public TimeSpan? Duration => StartedAt is { } s && CompletedAt is { } c ? c - s : null;

    /// <summary>
    /// Output tokens per second over the streaming window, or <see langword="null"/> when it cannot
    /// be computed from the reported values.
    /// </summary>
    public double? TokensPerSecond
    {
        get
        {
            if (Usage.OutputTokens is not { } tokens || Duration is not { } duration) return null;
            var seconds = duration.TotalSeconds - (TimeToFirstToken?.TotalSeconds ?? 0);
            return seconds > 0.001 ? tokens / seconds : null;
        }
    }

    /// <summary>Sampling temperature used, when reported.</summary>
    public double? Temperature { get; init; }

    /// <summary>Size of the model context window in tokens, for context-usage UI.</summary>
    public long? ContextWindow { get; init; }

    /// <summary>Provider-specific values that do not map onto the fields above.</summary>
    public IReadOnlyDictionary<string, object?>? Extra { get; init; }
}
