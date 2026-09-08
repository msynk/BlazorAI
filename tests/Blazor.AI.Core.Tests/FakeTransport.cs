using System.Runtime.CompilerServices;

namespace Blazor.AI.Tests;

/// <summary>
/// A transport that replays a scripted event list, which is all a transport has to be.
/// </summary>
/// <remarks>
/// This class is also the shortest demonstration of the framework's central claim: nothing
/// downstream of <see cref="IAiTransport"/> knows or cares what produced the events.
/// </remarks>
internal sealed class FakeTransport : IAiTransport
{
    private readonly Func<AiRequest, IEnumerable<AiEvent>> _script;

    public FakeTransport(params AiEvent[] events) => _script = _ => events;

    public FakeTransport(Func<AiRequest, IEnumerable<AiEvent>> script) => _script = script;

    /// <summary>Requests the transport has been given, in order.</summary>
    public List<AiRequest> Requests { get; } = [];

    /// <summary>A delay inserted before each event, used to test cancellation.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Thrown instead of streaming, used to test failure handling.</summary>
    public Exception? Throw { get; set; }

    public async IAsyncEnumerable<AiEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Throw is not null) throw Throw;

        foreach (var evt in _script(request))
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield return evt;
        }
    }
}

/// <summary>
/// A transport that never finishes, so a test can stop it.
/// </summary>
internal sealed class HangingTransport : IAiTransport
{
    public async IAsyncEnumerable<AiEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new MessageStartedEvent();

        while (true)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            yield return new TextDeltaEvent("token ");
        }
    }
}
