using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Blazor.AI;

/// <summary>
/// Settings for evening out bursty text output.
/// </summary>
public sealed record TextPacingOptions
{
    /// <summary>The default settings: a 16 millisecond tick draining the buffer over 150 milliseconds.</summary>
    public static readonly TextPacingOptions Default = new();

    /// <summary>How often to release a chunk. One animation frame by default.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// How long the pacer aims to take to drain whatever is buffered. Larger values look smoother
    /// and add latency; smaller values track the model more closely.
    /// </summary>
    public TimeSpan TargetDrain { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>The fewest characters to release per tick.</summary>
    public int MinCharsPerTick { get; init; } = 1;

    /// <summary>The most characters to release per tick, which caps the smoothing latency.</summary>
    public int MaxCharsPerTick { get; init; } = 48;
}

/// <summary>
/// Transformations over an event stream.
/// </summary>
public static class AiEventStreamExtensions
{
    /// <summary>
    /// Evens out text deltas so output appears at a steady rate instead of in bursts.
    /// </summary>
    /// <param name="source">The stream to pace.</param>
    /// <param name="options">Pacing settings. Defaults to <see cref="TextPacingOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels pacing and the underlying stream.</param>
    /// <returns>The paced stream.</returns>
    /// <remarks>
    /// <para>
    /// Providers deliver text in uneven bursts: nothing for two hundred milliseconds, then a
    /// paragraph. Rendering that verbatim reads as stuttering, which users perceive as slowness even
    /// when the total time is identical. This operator buffers text and releases it at a steady rate
    /// while passing every other event through immediately and in order.
    /// </para>
    /// <para>
    /// It never delays the end of a stream by more than one drain window, and it adds no latency at
    /// all when the model is slower than the pacer, which is the common case.
    /// </para>
    /// </remarks>
    public static async IAsyncEnumerable<AiEvent> Paced(
        this IAsyncEnumerable<AiEvent> source,
        TextPacingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var settings = options ?? TextPacingOptions.Default;

        var channel = Channel.CreateUnbounded<AiEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        _ = PumpAsync(source, channel.Writer, cancellationToken);

        var pending = new StringBuilder();
        string? pendingPartId = null;
        string? pendingMessageId = null;
        var completed = false;
        var ticksToDrain = Math.Max(1, (int)(settings.TargetDrain.Ticks / Math.Max(1, settings.Interval.Ticks)));

        while (true)
        {
            while (channel.Reader.TryRead(out var evt))
            {
                if (evt is TextDeltaEvent text
                    && (pending.Length == 0 || (text.PartId == pendingPartId && text.MessageId == pendingMessageId)))
                {
                    pendingPartId = text.PartId;
                    pendingMessageId = text.MessageId;
                    pending.Append(text.Delta);
                    continue;
                }

                while (pending.Length > 0)
                {
                    yield return Take(pending, pending.Length, pendingPartId, pendingMessageId);
                }

                yield return evt;
            }

            if (pending.Length > 0)
            {
                var size = Math.Clamp(
                    (pending.Length + ticksToDrain - 1) / ticksToDrain,
                    settings.MinCharsPerTick,
                    settings.MaxCharsPerTick);

                yield return Take(pending, size, pendingPartId, pendingMessageId);

                if (pending.Length > 0 || !completed)
                {
                    await Task.Delay(settings.Interval, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (completed) yield break;
            if (!await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) completed = true;
        }
    }

    private static TextDeltaEvent Take(StringBuilder pending, int count, string? partId, string? messageId)
    {
        count = Math.Min(count, pending.Length);
        var chunk = pending.ToString(0, count);
        pending.Remove(0, count);
        return new TextDeltaEvent(chunk) { PartId = partId, MessageId = messageId };
    }

    private static async Task PumpAsync(
        IAsyncEnumerable<AiEvent> source,
        ChannelWriter<AiEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteAsync(evt, cancellationToken).ConfigureAwait(false);
            }

            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }
}
