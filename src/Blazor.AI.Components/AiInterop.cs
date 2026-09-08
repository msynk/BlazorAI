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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IJSObjectReference? _module;
    private bool _unavailable;

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
    public async ValueTask<IJSObjectReference?> GetModuleAsync()
    {
        if (_module is not null) return _module;
        if (_unavailable) return null;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_module is not null) return _module;
            _module = await _js.InvokeAsync<IJSObjectReference>("import", ModulePath).ConfigureAwait(false);
            return _module;
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
        catch (TaskCanceledException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
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
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }

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
        catch (JSDisconnectedException) { return default; }
        catch (JSException) { return default; }
        catch (ObjectDisposedException) { return default; }
        catch (TaskCanceledException) { return default; }
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
        _module = null;
        _gate.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            _gate.Dispose();
            return;
        }

        try
        {
            await _module.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            _module = null;
            _gate.Dispose();
        }
    }
}
