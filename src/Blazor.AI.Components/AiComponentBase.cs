using Microsoft.AspNetCore.Components;

namespace Blazor.AI.Components;

/// <summary>
/// The base class for the library components: cascaded options, the string table, and a coalesced
/// re-render helper.
/// </summary>
/// <remarks>
/// The re-render helper is the reason this class exists. Streaming raises change events far faster
/// than a browser can paint, and calling <c>StateHasChanged</c> on each one is the single easiest
/// way to make an AI chat UI feel slow. Derived components call <see cref="RequestRender"/> instead,
/// which collapses any number of requests into one render per frame interval.
/// </remarks>
public abstract class AiComponentBase : ComponentBase, IAsyncDisposable
{
    private RenderScheduler? _scheduler;

    /// <summary>The presentation settings in force, cascaded from the nearest provider.</summary>
    [CascadingParameter]
    public AiUiOptions? CascadedOptions { get; set; }

    /// <summary>The settings this component should use.</summary>
    protected AiUiOptions Options => CascadedOptions ?? AiUiOptions.Default;

    /// <summary>The user-facing text.</summary>
    protected AiStrings Strings => Options.Strings;

    /// <summary>
    /// Asks for a re-render, at most once per <see cref="AiUiOptions.RenderInterval"/>.
    /// </summary>
    /// <remarks>Safe to call from any thread and on every streamed token.</remarks>
    protected void RequestRender()
    {
        _scheduler ??= new RenderScheduler(() => InvokeAsync(StateHasChanged), Options.RenderInterval);
        _scheduler.Request();
    }

    /// <summary>
    /// Renders immediately, bypassing the interval. Call this when a stream ends so the final state
    /// is not left waiting on a timer.
    /// </summary>
    /// <returns>A task that completes when the render has been requested.</returns>
    protected Task RenderNowAsync() => _scheduler?.FlushAsync() ?? InvokeAsync(StateHasChanged);

    /// <summary>Releases the render scheduler.</summary>
    /// <returns>A task that completes when disposal is finished.</returns>
    protected virtual ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync().ConfigureAwait(false);
        if (_scheduler is not null) await _scheduler.DisposeAsync().ConfigureAwait(false);
        _scheduler = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Joins CSS class names, skipping the empty ones.
    /// </summary>
    /// <param name="values">The candidate class names.</param>
    /// <returns>The joined class attribute value.</returns>
    protected static string Classes(params string?[] values) =>
        string.Join(' ', values.Where(v => !string.IsNullOrWhiteSpace(v)));
}
