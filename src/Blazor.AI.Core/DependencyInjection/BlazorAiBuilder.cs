using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Blazor.AI;

/// <summary>
/// Configures the framework during startup.
/// </summary>
/// <remarks>
/// Returned by <c>AddBlazorAI</c> so registration reads as one fluent statement instead of a dozen
/// unrelated service registrations.
/// </remarks>
public sealed class BlazorAiBuilder
{
    internal BlazorAiBuilder(IServiceCollection services) => Services = services;

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers the transport that produces responses.
    /// </summary>
    /// <typeparam name="TTransport">The transport implementation.</typeparam>
    /// <returns>The builder, for chaining.</returns>
    public BlazorAiBuilder UseTransport<TTransport>() where TTransport : class, IAiTransport
    {
        Services.AddScoped<IAiTransport, TTransport>();
        return this;
    }

    /// <summary>
    /// Registers a transport instance.
    /// </summary>
    /// <param name="transport">The transport.</param>
    /// <returns>The builder, for chaining.</returns>
    public BlazorAiBuilder UseTransport(IAiTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        Services.AddSingleton(transport);
        return this;
    }

    /// <summary>
    /// Registers a transport built from services.
    /// </summary>
    /// <param name="factory">Creates the transport.</param>
    /// <returns>The builder, for chaining.</returns>
    public BlazorAiBuilder UseTransport(Func<IServiceProvider, IAiTransport> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.AddScoped(factory);
        return this;
    }

    /// <summary>
    /// Adds a tool the model may call.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <returns>The builder, for chaining.</returns>
    public BlazorAiBuilder AddTool(IAiTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        Services.AddSingleton(tool);
        return this;
    }

    /// <summary>
    /// Adds a tool resolved from services, for tools that need dependencies.
    /// </summary>
    /// <typeparam name="TTool">The tool implementation.</typeparam>
    /// <returns>The builder, for chaining.</returns>
    public BlazorAiBuilder AddTool<TTool>() where TTool : class, IAiTool
    {
        Services.AddSingleton<IAiTool, TTool>();
        return this;
    }

    /// <summary>
    /// Replaces the policy that decides which tool calls need a human decision.
    /// </summary>
    /// <typeparam name="TPolicy">The policy implementation.</typeparam>
    /// <returns>The builder, for chaining.</returns>
    public BlazorAiBuilder UseApprovalPolicy<TPolicy>() where TPolicy : class, IToolApprovalPolicy
    {
        // Scoped so a policy can depend on per-user services such as the current user.
        Services.RemoveAll<IToolApprovalPolicy>();
        Services.AddScoped<IToolApprovalPolicy, TPolicy>();
        return this;
    }

    /// <summary>
    /// Registers the models the user can choose between.
    /// </summary>
    /// <param name="models">The models.</param>
    /// <param name="providers">The providers serving them.</param>
    /// <param name="defaultModelId">The model selected by default.</param>
    /// <returns>The builder, for chaining.</returns>
    public BlazorAiBuilder AddModels(
        IEnumerable<AiModel> models,
        IEnumerable<AiProvider>? providers = null,
        string? defaultModelId = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        Services.RemoveAll<IModelCatalog>();
        Services.AddSingleton<IModelCatalog>(new ModelCatalog(models, providers, defaultModelId));
        return this;
    }

    /// <summary>
    /// Registers persistence for conversations.
    /// </summary>
    /// <typeparam name="TStore">The store implementation.</typeparam>
    /// <returns>The builder, for chaining.</returns>
    public BlazorAiBuilder UseConversationStore<TStore>() where TStore : class, IConversationStore
    {
        Services.RemoveAll<IConversationStore>();
        Services.AddScoped<IConversationStore, TStore>();
        return this;
    }

    /// <summary>
    /// Sets the default options every session starts with.
    /// </summary>
    /// <param name="configure">
    /// Receives the current defaults and returns the ones to use. A transform rather than a
    /// mutation, because the options are an immutable record, which is what makes one instance safe
    /// to share across every session.
    /// </param>
    /// <returns>The builder, for chaining.</returns>
    /// <example>
    /// <code>
    /// ai.ConfigureSessions(options =&gt; options with { MaxToolRoundtrips = 5 });
    /// </code>
    /// </example>
    public BlazorAiBuilder ConfigureSessions(Func<ChatSessionOptions, ChatSessionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var current = (ChatSessionOptions?)Services
            .LastOrDefault(d => d.ServiceType == typeof(ChatSessionOptions))?.ImplementationInstance;

        Services.RemoveAll<ChatSessionOptions>();
        Services.AddSingleton(configure(current ?? new ChatSessionOptions()));
        return this;
    }
}
