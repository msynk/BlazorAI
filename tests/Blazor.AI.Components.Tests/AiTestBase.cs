using Blazor.AI.GenerativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.AI.Components.Tests;

/// <summary>
/// A rendering context with the framework services registered, so tests read as usage rather than
/// as setup.
/// </summary>
public abstract class AiTestBase : BunitContext
{
    protected AiTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddBlazorAIComponents();
    }

    /// <summary>The renderer registry, for tests that register a custom renderer.</summary>
    protected AiRendererRegistry Renderers => Services.GetRequiredService<AiRendererRegistry>();

    /// <summary>The generative UI registry, for tests that register a component.</summary>
    protected GenerativeUiRegistry Generative => Services.GetRequiredService<GenerativeUiRegistry>();

    /// <summary>Builds an assistant message from a sequence of parts.</summary>
    /// <param name="parts">The parts to add.</param>
    /// <returns>The message.</returns>
    protected static AiMessage Assistant(params MessagePart[] parts)
    {
        var message = new AiMessage(AiRole.Assistant);
        foreach (var part in parts) message.AddPart(part);
        return message;
    }
}
