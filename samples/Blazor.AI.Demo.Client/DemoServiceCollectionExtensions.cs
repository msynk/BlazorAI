using Blazor.AI.Components;
using Blazor.AI.Demo.Client.Components;
using Blazor.AI.Demo.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.AI.Demo.Client;

/// <summary>
/// The whole wiring for the demo, called identically by the server and the WebAssembly host.
/// </summary>
/// <remarks>
/// One registration method used by both hosts is the point: the framework has no server-only or
/// browser-only pieces, so a page prerendered on the server and then rehydrated in WebAssembly gets
/// the same services either way.
/// </remarks>
public static class DemoServiceCollectionExtensions
{
    /// <summary>Registers the framework and everything this demo adds to it.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddDemo(this IServiceCollection services)
    {
        services.AddBlazorAI(ai =>
        {
            ai.UseTransport(DemoTransports.Echo());
            ai.AddModels(DemoCatalog.Models, DemoCatalog.Providers, "demo-model-1");
            ai.ConfigureSessions(options => options with { MaxToolRoundtrips = 6 });
        });

        foreach (var tool in DemoCatalog.CreateTools()) services.AddAiTool(tool);

        services.AddSingleton<IConversationStore, InMemoryConversationStore>();

        services.AddBlazorAIComponents(options => options with
        {
            ShowUsage = true,
            ShowModelName = true,
            ShowTimestamps = false,
        });

        // A custom renderer for one tool, which replaces the default card wherever that tool appears.
        services.ConfigureAiRenderers(renderers => renderers.RegisterTool<SearchResultCard>("search_docs"));

        // The components model output is allowed to instantiate, and the properties it may set.
        services.ConfigureGenerativeUi(registry => registry.Register<ForecastCard>(
            "forecast_card",
            "Shows the current weather for one city.",
            "City"));

        return services;
    }
}
