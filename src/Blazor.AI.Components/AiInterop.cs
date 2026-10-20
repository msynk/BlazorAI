using Microsoft.JSInterop;

namespace Blazor.AI.Components;

/// <summary>
/// The browser-side helpers the components need: scrolling, textarea sizing, clipboard, drops, and
/// dictation.
/// </summary>
/// <remarks>
/// <para>
/// The module is imported lazily on first use, so a page that renders a transcript without a
/// composer never downloads or evaluates it. Every method fails quietly: interop that throws during
/// prerendering, teardown, or a lost circuit must not take a component down with it.
/// </para>
/// <para>
/// Registered as a scoped service. Inject it rather than importing the module yourself.
/// </para>
/// </remarks>
public sealed class AiInterop : IAsyncDisposable, IDisposable
{
    private const string ModulePath = "./_content/Blazor.AI.Components/blazor-ai.js";

    private readonly IJSRuntime _js;
    private readonly Lock _sync = new();
    private Task<IJSObjectReference?>? _loading;
    private volatile bool _unavailable;
    private volatile bool _disposed;

    /// <summary>Creates the service.</summary>
    /// <param name="js">The JavaScript runtime for the current circuit or page.</param>
    public AiInterop(IJSRuntime js)
    {
        ArgumentNullException.ThrowIfNull(js);
        _js = js;
    }

    /// <summary>
    /// Loads the module, or returns <see langword="null"/> when JavaScript is not available, which
    /// is the case during server-side prerendering.
    /// </summary>
    /// <returns>The module reference, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Concurrent callers share one in-flight import rather than queueing on a lock. That is what
    /// makes disposal safe at any moment: there is no synchronisation primitive for teardown to
    /// dispose out from under a waiter, and a load that finishes after disposal is discarded.
    /// </remarks>
    public async ValueTask<IJSObjectReference?> GetModuleAsync()
    {
        if (_disposed || _unavailable) return null;

        Task<IJSObjectReference?> loading;
        lock (_sync)
        {
            if (_disposed) return null;
            loading = _loading ??= LoadAsync();
        }

        var module = await loading.ConfigureAwait(false);

        if (module is null)
        {
            // A load that failed because the page is still prerendering, or because the call was
            // cancelled, is worth trying again on the next call. One JavaScript refused is not,
            // and _unavailable already says so.
            lock (_sync)
            {
                if (ReferenceEquals(_loading, loading)) _loading = null;
            }
        }

        return _disposed ? null : module;
    }

    private async Task<IJSObjectReference?> LoadAsync()
    {
        try
        {
            return await _js.InvokeAsync<IJSObjectReference>("import", ModulePath).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, which is not an exception but the end of the session. It is not a
            // JSException, so it needs its own clause or it escapes every caller.
            return null;
        }
        catch (JSException)
        {
            _unavailable = true;
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Invokes a module function, ignoring failures caused by teardown or prerendering.</summary>
    /// <param name="identifier">The exported function name.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>A task that completes when the call has been attempted.</returns>
    public async ValueTask InvokeVoidAsync(string identifier, params object?[] args)
    {
        var module = await GetModuleAsync().ConfigureAwait(false);
        if (module is null) return;

        try
        {
            await module.InvokeVoidAsync(identifier, args).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception)) { }
    }

    /// <summary>
    /// Whether an exception from a browser call means only that the browser is unreachable or the
    /// call was abandoned: a lost circuit, a disposed module, a cancelled call at teardown, or a
    /// JavaScript error, none of which should take a component down with it.
    /// </summary>
    /// <param name="exception">The exception a browser call threw.</param>
    /// <returns><see langword="true"/> when the exception should be swallowed.</returns>
    /// <remarks>
    /// <see cref="JSDisconnectedException"/> is not a <see cref="JSException"/>, and
    /// <see cref="OperationCanceledException"/> is what a circuit tearing down surfaces, so both need
    /// naming alongside the rest. Components awaiting interop in a render or dispose path use this
    /// filter too, so the set of expected failures lives in one place.
    /// </remarks>
    public static bool IsExpected(Exception exception) => exception switch
    {
        JSDisconnectedException or JSException or ObjectDisposedException or OperationCanceledException => true,
        // "JavaScript interop calls cannot be issued at this time": the page is still prerendering.
        InvalidOperationException invalid => invalid.Message.Contains("JavaScript interop", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>Invokes a module function that returns a value.</summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="identifier">The exported function name.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>The result, or <see langword="default"/> when the call could not be made.</returns>
    public async ValueTask<T?> InvokeAsync<T>(string identifier, params object?[] args)
    {
        var module = await GetModuleAsync().ConfigureAwait(false);
        if (module is null) return default;

        try
        {
            return await module.InvokeAsync<T>(identifier, args).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return default;
        }
    }

    /// <summary>Copies text to the clipboard.</summary>
    /// <param name="text">The text to copy.</param>
    /// <returns><see langword="true"/> when the copy succeeded.</returns>
    public ValueTask<bool> CopyAsync(string text) => InvokeAsync<bool>("copyText", text)!;

    /// <summary>Offers text to the user as a file download.</summary>
    /// <param name="fileName">The suggested file name.</param>
    /// <param name="content">The file content.</param>
    /// <param name="mimeType">The MIME type.</param>
    /// <returns>A task that completes when the download has been started.</returns>
    public ValueTask DownloadTextAsync(string fileName, string content, string mimeType = "text/plain") =>
        InvokeVoidAsync("downloadText", fileName, content, mimeType);

    /// <summary>
    /// Releases the module reference without waiting for the browser.
    /// </summary>
    /// <remarks>
    /// Present so the service survives a container that disposes scopes synchronously, which throws
    /// for a service that is only asynchronously disposable. Releasing the reference without
    /// awaiting is correct here: the module is discarded with the page or the circuit anyway.
    /// </remarks>
    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _loading = null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A load still in flight is awaited rather than abandoned, so the module it produces is released
    /// too. The load never throws, and callers waiting on it receive <see langword="null"/>.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        Task<IJSObjectReference?>? loading;
        lock (_sync)
        {
            _disposed = true;
            loading = _loading;
            _loading = null;
        }

        var module = loading is null ? null : await loading.ConfigureAwait(false);
        if (module is null) return;

        try
        {
            await module.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception)) { }
    }
}
