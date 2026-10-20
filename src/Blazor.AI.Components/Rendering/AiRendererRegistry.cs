using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;

namespace Blazor.AI.Components;

/// <summary>
/// Where applications register their own renderers for parts, tools, artifacts, structured data,
/// and citations.
/// </summary>
/// <remarks>
/// <para>
/// The framework resolves a renderer here before falling back to its own. That is the single
/// extension point behind most customisation: a tool that deserves a map instead of a JSON dump, an
/// artifact type the framework has never heard of, a bespoke citation chip.
/// </para>
/// <para>
/// Registered as a singleton, so register at startup and treat entries as immutable afterwards.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// registry.RegisterTool("get_weather", ctx =&gt; builder =&gt;
/// {
///     builder.OpenComponent&lt;WeatherCard&gt;(0);
///     builder.AddComponentParameter(1, "Call", ctx.Call);
///     builder.CloseComponent();
/// });
/// </code>
/// </example>
public sealed class AiRendererRegistry
{
    private readonly ConcurrentDictionary<string, MessagePartRenderer> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ToolCallRenderer> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ArtifactRenderer> _artifacts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StructuredDataRenderer> _structured = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Renders every citation, when set. Overrides the built-in chip.</summary>
    public CitationRenderer? CitationRenderer { get; set; }

    /// <summary>
    /// Renders mathematics in Markdown, when set. Overrides the built-in monospace fallback.
    /// </summary>
    /// <remarks>
    /// The fallback renders the expression source with the class <c>bai-math</c> and a
    /// <c>data-math</c> attribute, so a host that prefers to typeset with KaTeX or MathJax in the
    /// browser can do that from JavaScript instead of setting this.
    /// </remarks>
    public MathRenderer? MathRenderer { get; set; }

    private readonly ConcurrentDictionary<string, CodeBlockRenderer> _codeLanguages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a renderer for fenced code blocks of one language, such as <c>mermaid</c> or
    /// <c>vega</c>, in place of the highlighted code view.
    /// </summary>
    /// <param name="language">The language as written in the fence info string. Case does not matter.</param>
    /// <param name="renderer">Produces the markup.</param>
    public void RegisterCodeLanguage(string language, CodeBlockRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(renderer);
        _codeLanguages[language.Trim()] = renderer;
    }

    /// <summary>Finds a code block renderer.</summary>
    /// <param name="language">The language from the fence info string.</param>
    /// <returns>The renderer, or <see langword="null"/> to use the built-in code view.</returns>
    public CodeBlockRenderer? FindCodeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? null : _codeLanguages.GetValueOrDefault(language.Trim());

    /// <summary>
    /// Registers a renderer for a message part kind.
    /// </summary>
    /// <param name="kind">The <see cref="IMessagePart.Kind"/> to handle.</param>
    /// <param name="renderer">Produces the markup.</param>
    public void RegisterPart(string kind, MessagePartRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(renderer);
        _parts[kind] = renderer;
    }

    /// <summary>
    /// Registers a component as the renderer for a part kind. The component receives the part as a
    /// <c>Part</c> parameter.
    /// </summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="kind">The <see cref="IMessagePart.Kind"/> to handle.</param>
    public void RegisterPart<TComponent>(string kind) where TComponent : IComponent =>
        RegisterPart(kind, context => builder =>
        {
            builder.OpenComponent<TComponent>(0);
            builder.AddComponentParameter(1, "Part", context.Part);
            builder.CloseComponent();
        });

    /// <summary>
    /// Registers a renderer for one tool, matched by the name the model calls.
    /// </summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="renderer">Produces the markup.</param>
    public void RegisterTool(string toolName, ToolCallRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(renderer);
        _tools[toolName] = renderer;
    }

    /// <summary>
    /// Registers a component as the renderer for one tool. The component receives the call as a
    /// <c>Call</c> parameter.
    /// </summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="toolName">The tool name.</param>
    public void RegisterTool<TComponent>(string toolName) where TComponent : IComponent =>
        RegisterTool(toolName, context => builder =>
        {
            builder.OpenComponent<TComponent>(0);
            builder.AddComponentParameter(1, "Call", context.Call);
            builder.CloseComponent();
        });

    /// <summary>
    /// Registers a renderer for an artifact kind.
    /// </summary>
    /// <param name="kind">The artifact kind.</param>
    /// <param name="renderer">Produces the markup.</param>
    public void RegisterArtifact(ArtifactKind kind, ArtifactRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _artifacts[kind.Value] = renderer;
    }

    /// <summary>
    /// Registers a renderer for a structured-data schema name.
    /// </summary>
    /// <param name="schemaName">The schema name carried on the part.</param>
    /// <param name="renderer">Produces the markup.</param>
    public void RegisterStructured(string schemaName, StructuredDataRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentNullException.ThrowIfNull(renderer);
        _structured[schemaName] = renderer;
    }

    /// <summary>Finds a part renderer.</summary>
    /// <param name="kind">The part kind.</param>
    /// <returns>The renderer, or <see langword="null"/> to use the built-in one.</returns>
    public MessagePartRenderer? FindPart(string kind) => _parts.GetValueOrDefault(kind);

    /// <summary>Finds a tool renderer.</summary>
    /// <param name="toolName">The tool name.</param>
    /// <returns>The renderer, or <see langword="null"/> to use the built-in one.</returns>
    public ToolCallRenderer? FindTool(string toolName) => _tools.GetValueOrDefault(toolName);

    /// <summary>Finds an artifact renderer.</summary>
    /// <param name="kind">The artifact kind.</param>
    /// <returns>The renderer, or <see langword="null"/> to use the built-in one.</returns>
    public ArtifactRenderer? FindArtifact(ArtifactKind kind) => _artifacts.GetValueOrDefault(kind.Value);

    /// <summary>Finds a structured-data renderer.</summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The renderer, or <see langword="null"/> to use the built-in one.</returns>
    public StructuredDataRenderer? FindStructured(string? schemaName) =>
        schemaName is null ? null : _structured.GetValueOrDefault(schemaName);
}
