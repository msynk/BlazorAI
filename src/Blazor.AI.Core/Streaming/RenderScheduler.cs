namespace Blazor.AI;

/// <summary>
/// Coalesces high-frequency change notifications into a bounded number of renders.
/// </summary>
/// <remarks>
/// <para>
/// A fast model emits hundreds of deltas a second. Rendering each one is wasted work in WebAssembly
/// and, on Blazor Server, a flood of diffs down the circuit that makes the UI feel worse rather than
/// faster. This scheduler collapses any number of requests inside an interval into one render, so
/// throughput stops mattering and only the frame rate does.
/// </para>
/// <para>
/// It also applies backpressure: while a flush is in flight, further requests set a dirty flag
/// rather than queueing, and one more flush runs when the first completes. A slow renderer
/// therefore falls behind in render count, never in memory.
/// </para>
/// </remarks>
public sealed class RenderScheduler : IAsyncDisposable
{
    private readonly Func<Task> _flush;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private bool _scheduled;
    private bool _flushing;
    private bool _dirty;
    private bool _disposed;

    /// <summary>Creates a scheduler.</summary>
    /// <param name="flush">
    /// Invoked when a render is due. For Blazor components this should call
    /// <c>InvokeAsync(StateHasChanged)</c>.
    /// </param>
    /// <param name="interval">
    /// The minimum time between renders. Defaults to <see cref="DefaultInterval"/>.
    /// </param>
    public RenderScheduler(Func<Task> flush, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(flush);
        _flush = flush;
        _interval = interval ?? DefaultInterval;
    }

    /// <summary>
    /// One render every 50 milliseconds, or twenty a second.
    /// </summary>
    /// <remarks>
    /// Fast enough to read as continuous and slow enough to stay comfortable over a Blazor Server
    /// circuit on a poor connection.
    /// </remarks>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>How many renders the scheduler has performed. Useful in tests and diagnostics.</summary>
    public int RenderCount { get; private set; }

    /// <summary>How many requests have been coalesced away.</summary>
    public int CoalescedCount { get; private set; }

    /// <summary>
    /// Asks for a render. Cheap and safe to call from any thread, on every token.
    /// </summary>
    public void Request()
    {
        lock (_gate)
        {
            if (_disposed) return;

            if (_scheduled || _flushing)
            {
                _dirty = true;
                CoalescedCount++;
                return;
            }

            _scheduled = true;
            _cts = new CancellationTokenSource();
        }

        _ = RunAsync(_cts.Token);
    }

    /// <summary>
    /// Renders immediately, bypassing the interval. Use at the end of a stream so the final state is
    /// never left waiting behind a timer.
    /// </summary>
    /// <returns>A task that completes when the render has been requested.</returns>
    public async Task FlushAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _cts?.Cancel();
            _scheduled = false;
            _dirty = false;
        }

        await InvokeFlushAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            _scheduled = false;
            if (_disposed) return;
        }

        await InvokeFlushAsync().ConfigureAwait(false);

        bool again;
        lock (_gate)
        {
            again = _dirty && !_disposed;
            _dirty = false;
            if (again)
            {
                _scheduled = true;
                _cts = new CancellationTokenSource();
            }
        }

        if (again) _ = RunAsync(_cts!.Token);
    }

    private async Task InvokeFlushAsync()
    {
        lock (_gate)
        {
            if (_flushing || _disposed) return;
            _flushing = true;
        }

        try
        {
            RenderCount++;
            await _flush().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) _flushing = false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
