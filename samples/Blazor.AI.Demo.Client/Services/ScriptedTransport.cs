using System.Runtime.CompilerServices;

namespace Blazor.AI.Demo.Client.Services;

/// <summary>
/// A transport built from a delegate, used throughout the demo in place of a provider.
/// </summary>
/// <remarks>
/// The demo runs entirely in the browser with no API key, which is only possible because the
/// framework never talks to a provider itself. Every page below is wired to one of these, and the
/// components cannot tell the difference between this and a real model.
/// </remarks>
public sealed class ScriptedTransport(Func<AiRequest, CancellationToken, IAsyncEnumerable<AiEvent>> script)
    : IAiTransport
{
    /// <inheritdoc />
    public IAsyncEnumerable<AiEvent> StreamAsync(AiRequest request, CancellationToken cancellationToken = default) =>
        script(request, cancellationToken);
}

/// <summary>
/// Helpers for writing readable scripts.
/// </summary>
public static class Script
{
    /// <summary>Streams text a few characters at a time, the way a model does.</summary>
    /// <param name="text">The text to stream.</param>
    /// <param name="chunk">How many characters to send at once.</param>
    /// <param name="delayMs">How long to wait between chunks.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>The text delta events.</returns>
    public static async IAsyncEnumerable<AiEvent> Write(
        string text,
        int chunk = 4,
        int delayMs = 14,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < text.Length; i += chunk)
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            yield return new TextDeltaEvent(text.Substring(i, Math.Min(chunk, text.Length - i)));
        }
    }

    /// <summary>Streams reasoning text.</summary>
    /// <param name="text">The reasoning to stream.</param>
    /// <param name="delayMs">How long to wait between chunks.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>The reasoning delta events.</returns>
    public static async IAsyncEnumerable<AiEvent> Think(
        string text,
        int delayMs = 10,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var word in text.Split(' '))
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            yield return new ReasoningDeltaEvent(word + " ");
        }
    }

    /// <summary>Waits, so a demo reads at human speed.</summary>
    /// <param name="milliseconds">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes after the delay.</returns>
    public static Task Pause(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(milliseconds, cancellationToken);

    /// <summary>The text of the most recent user message.</summary>
    /// <param name="request">The request to read.</param>
    /// <returns>The user text, or an empty string.</returns>
    public static string LastUserText(AiRequest request)
    {
        for (var i = request.Messages.Count - 1; i >= 0; i--)
        {
            if (request.Messages[i].Role.IsUser) return request.Messages[i].GetText();
        }

        return string.Empty;
    }

    /// <summary>Whether a tool has already returned a result since the most recent user message.</summary>
    /// <param name="request">The request to read.</param>
    /// <returns><see langword="true"/> when this turn already ran a tool and the model should answer.</returns>
    /// <remarks>
    /// Scoped to the current turn, so a follow-up question calls the tool again instead of answering
    /// from a result an earlier turn produced.
    /// </remarks>
    public static bool HasToolResultThisTurn(AiRequest request)
    {
        for (var i = request.Messages.Count - 1; i >= 0; i--)
        {
            var message = request.Messages[i];
            if (message.Role.IsUser) return false;
            if (message.Parts.OfType<ToolCallPart>().Any(c => c.Result is not null)) return true;
        }

        return false;
    }

    /// <summary>Reports plausible token usage for a generated response.</summary>
    /// <param name="request">The request, used to size the prompt.</param>
    /// <param name="output">The generated text.</param>
    /// <returns>A usage event.</returns>
    public static UsageUpdatedEvent Usage(AiRequest request, string output)
    {
        var input = request.Messages.Sum(m => m.GetText().Length) / 4;
        var generated = output.Length / 4;

        return new UsageUpdatedEvent(new TokenUsage
        {
            InputTokens = input,
            OutputTokens = generated,
            Cost = (input * 3m + generated * 15m) / 1_000_000m,
            Currency = "USD",
        });
    }
}
