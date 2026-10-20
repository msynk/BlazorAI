using Microsoft.AspNetCore.Components;

namespace Blazor.AI.Components;

/// <summary>
/// What a custom mathematics renderer is given.
/// </summary>
/// <param name="Expression">The expression source, without its dollar delimiters.</param>
/// <param name="IsBlock"><see langword="true"/> for display mathematics, <see langword="false"/> for an inline expression.</param>
public sealed record MathContext(string Expression, bool IsBlock);

/// <summary>
/// What a custom code block renderer is given.
/// </summary>
/// <param name="Code">The source inside the fence.</param>
/// <param name="Language">The language from the fence info string, as written.</param>
/// <param name="Meta">The rest of the info string after the language, when there was one.</param>
/// <param name="IsStreaming">Whether the block is still receiving content.</param>
/// <remarks>
/// A renderer for a diagram language should wait for <paramref name="IsStreaming"/> to turn false:
/// a half-written diagram is a syntax error, and a syntax error flashing on every token is worse
/// than the source text.
/// </remarks>
public sealed record CodeBlockContext(string Code, string? Language, string? Meta, bool IsStreaming);

/// <summary>Renders a mathematical expression, in place of the built-in monospace fallback.</summary>
/// <param name="context">The expression and whether it is displayed or inline.</param>
/// <returns>The markup.</returns>
public delegate RenderFragment MathRenderer(MathContext context);

/// <summary>Renders a fenced code block of a registered language, in place of the built-in code view.</summary>
/// <param name="context">The code and its info string.</param>
/// <returns>The markup.</returns>
public delegate RenderFragment CodeBlockRenderer(CodeBlockContext context);

/// <summary>
/// How images in Markdown are treated.
/// </summary>
public enum AiImageMode
{
    /// <summary>Show every image whose URL scheme is safe.</summary>
    Allow = 0,

    /// <summary>Show images only from the listed hosts; others get a placeholder.</summary>
    AllowListedHosts = 1,

    /// <summary>Show no remote images; each is replaced by its alternative text.</summary>
    Block = 2,

    /// <summary>Show a placeholder that loads the image when the user asks for it.</summary>
    ClickToLoad = 3,
}

/// <summary>
/// Which images a Markdown renderer fetches.
/// </summary>
/// <remarks>
/// <para>
/// A model can be talked into emitting an image URL, and a browser fetching that URL leaks the
/// user's address and the fact that they read the message to whoever controls the host. The
/// default allows images, as a chat UI is expected to; a host handling sensitive conversations can
/// restrict them to its own CDN, ask before loading, or block them outright.
/// </para>
/// <para>
/// Script-capable URL schemes are neutralised regardless of the policy. Inline <c>data:</c> images
/// are fetched from nowhere, so they are shown under every policy except <see cref="AiImageMode.Block"/>.
/// </para>
/// </remarks>
public sealed record AiImagePolicy
{
    private readonly HashSet<string> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Show every image whose URL scheme is safe. The default.</summary>
    public static AiImagePolicy Allow { get; } = new() { Mode = AiImageMode.Allow };

    /// <summary>Show no remote images.</summary>
    public static AiImagePolicy Block { get; } = new() { Mode = AiImageMode.Block };

    /// <summary>Load each remote image only when the user asks.</summary>
    public static AiImagePolicy ClickToLoad { get; } = new() { Mode = AiImageMode.ClickToLoad };

    /// <summary>Show remote images only from the given hosts, and a placeholder for the rest.</summary>
    /// <param name="hosts">Host names, compared without regard to case. A leading <c>*.</c> matches subdomains.</param>
    /// <returns>The policy.</returns>
    public static AiImagePolicy AllowListedHosts(params string[] hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        var policy = new AiImagePolicy { Mode = AiImageMode.AllowListedHosts };
        foreach (var host in hosts)
        {
            if (!string.IsNullOrWhiteSpace(host)) policy._hosts.Add(host.Trim());
        }

        return policy;
    }

    /// <summary>How images are treated.</summary>
    public AiImageMode Mode { get; init; } = AiImageMode.Allow;

    /// <summary>The hosts allowed under <see cref="AiImageMode.AllowListedHosts"/>.</summary>
    public IReadOnlyCollection<string> Hosts => _hosts;

    /// <summary>Whether inline <c>data:</c> images are shown. They involve no network request.</summary>
    public bool AllowDataImages { get; init; } = true;

    /// <summary>
    /// Decides what to do with one image.
    /// </summary>
    /// <param name="url">The image source, already checked for a safe scheme.</param>
    /// <returns>Whether to show it, show a placeholder that loads it on request, or hide it.</returns>
    public AiImageDecision Decide(string url)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return AllowDataImages && Mode != AiImageMode.Block ? AiImageDecision.Show : AiImageDecision.Hide;
        }

        return Mode switch
        {
            AiImageMode.Allow => AiImageDecision.Show,
            AiImageMode.Block => AiImageDecision.Hide,
            AiImageMode.ClickToLoad => AiImageDecision.AskFirst,
            _ => IsListed(url) ? AiImageDecision.Show : AiImageDecision.AskFirst,
        };
    }

    private bool IsListed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return false;

        var host = uri.Host;
        if (_hosts.Contains(host)) return true;

        foreach (var listed in _hosts)
        {
            if (listed.StartsWith("*.", StringComparison.Ordinal)
                && host.EndsWith(listed.AsSpan(1), StringComparison.OrdinalIgnoreCase)
                && host.Length > listed.Length - 1)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>What an <see cref="AiImagePolicy"/> decided about one image.</summary>
public enum AiImageDecision
{
    /// <summary>Render the image.</summary>
    Show = 0,

    /// <summary>Render a placeholder that loads the image when activated.</summary>
    AskFirst = 1,

    /// <summary>Render the alternative text in place of the image.</summary>
    Hide = 2,
}
