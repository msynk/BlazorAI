using Blazor.AI.GenerativeUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Blazor.AI.Components;

/// <summary>
/// Registers the component library with the dependency injection container.
/// </summary>
public static class AiComponentsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services the components need: browser interop, the renderer registry, the
    /// generative UI registry, and presentation defaults.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Adjusts the presentation defaults.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Safe to call alongside <c>AddBlazorAI</c>, which registers the state and transport side. The
    /// two are separate so that a project can reference the core framework without the components,
    /// for example on a server that only produces events.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddBlazorAIComponents(options =&gt; options with
    /// {
    ///     Density = AiDensity.Compact,
    ///     ShowUsage = true,
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddBlazorAIComponents(
        this IServiceCollection services,
        Func<AiUiOptions, AiUiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<AiInterop>();
        services.TryAddSingleton<AiRendererRegistry>();
        services.TryAddSingleton<GenerativeUiRegistry>();
        services.TryAddSingleton<IGenerativeUiRegistry>(sp => sp.GetRequiredService<GenerativeUiRegistry>());
        if (configure is null)
        {
            services.TryAddSingleton(AiUiOptions.Default);
        }
        else
        {
            // Build on whatever is already registered rather than giving way to it: ConfigureAiRenderers
            // and ConfigureGenerativeUi register the defaults themselves, and calling either of them
            // first must not silently discard the options passed here.
            var existing = services.LastOrDefault(d => d.ServiceType == typeof(AiUiOptions))?.ImplementationInstance as AiUiOptions;
            services.Replace(ServiceDescriptor.Singleton(configure(existing ?? AiUiOptions.Default)));
        }

        return services;
    }

    /// <summary>
    /// Registers custom renderers for parts, tools, artifacts, structured data, or citations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Receives the registry.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection ConfigureAiRenderers(
        this IServiceCollection services,
        Action<AiRendererRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddBlazorAIComponents();
        services.AddSingleton(new RendererConfiguration(configure));
        services.Replace(ServiceDescriptor.Singleton(sp =>
        {
            var registry = new AiRendererRegistry();
            foreach (var configuration in sp.GetServices<RendererConfiguration>()) configuration.Apply(registry);
            return registry;
        }));

        return services;
    }

    /// <summary>
    /// Registers components that model output may instantiate.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Receives the registry.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.ConfigureGenerativeUi(registry =&gt;
    ///     registry.Register&lt;WeatherCard&gt;("weather_card", "Shows a forecast.", "City"));
    /// </code>
    /// </example>
    public static IServiceCollection ConfigureGenerativeUi(
        this IServiceCollection services,
        Action<GenerativeUiRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddBlazorAIComponents();
        services.AddSingleton(new GenerativeConfiguration(configure));
        services.Replace(ServiceDescriptor.Singleton(sp =>
        {
            var registry = new GenerativeUiRegistry();
            foreach (var configuration in sp.GetServices<GenerativeConfiguration>()) configuration.Apply(registry);
            return registry;
        }));

        return services;
    }

    private sealed class RendererConfiguration(Action<AiRendererRegistry> configure)
    {
        public void Apply(AiRendererRegistry registry) => configure(registry);
    }

    private sealed class GenerativeConfiguration(Action<GenerativeUiRegistry> configure)
    {
        public void Apply(GenerativeUiRegistry registry) => configure(registry);
    }
}
