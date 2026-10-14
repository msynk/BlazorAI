using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Blazor.AI;

/// <summary>
/// Registers the framework with the dependency injection container.
/// </summary>
public static class BlazorAiServiceCollectionExtensions
{
    /// <summary>
    /// Adds the framework services and returns a builder for configuring them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration, equivalent to using the returned builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddBlazorAI()
    ///     .UseTransport&lt;MyTransport&gt;()
    ///     .AddTool(AiTool.Create("get_weather", "Look up the weather.", GetWeatherAsync))
    ///     .AddModels(models, defaultModelId: "my-model")
    ///     .ConfigureSessions(o =&gt; o with { MaxToolRoundtrips = 5 });
    /// </code>
    /// </example>
    public static BlazorAiBuilder AddBlazorAI(this IServiceCollection services, Action<BlazorAiBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Scoped, not singleton: the default policy remembers "allow always" decisions, and on Blazor
        // Server a singleton would carry one user's decision over to every other user.
        services.TryAddScoped<IToolApprovalPolicy, DefaultToolApprovalPolicy>();
        services.TryAddSingleton<IToolRegistry>(sp => new ToolRegistry(sp.GetServices<IAiTool>()));
        services.TryAddSingleton(new ChatSessionOptions());
        services.TryAddScoped<ChatSessionFactory>();

        var builder = new BlazorAiBuilder(services);
        configure?.Invoke(builder);
        return builder;
    }

    /// <summary>
    /// Adds a tool without going through the builder.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tool">The tool.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddAiTool(this IServiceCollection services, IAiTool tool)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tool);
        services.AddSingleton(tool);
        return services;
    }
}

/// <summary>
/// Creates <see cref="ChatSession"/> instances with the registered transport, tools, and policies.
/// </summary>
/// <remarks>
/// Inject this into a component rather than constructing a session by hand, so a session picks up
/// everything the host configured without the component having to know what that was.
/// </remarks>
public sealed class ChatSessionFactory
{
    private readonly IServiceProvider _services;
    private readonly IAiTransport _transport;
    private readonly IToolRegistry _tools;
    private readonly IToolApprovalPolicy _approvalPolicy;
    private readonly ChatSessionOptions _options;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates a factory.</summary>
    /// <param name="services">The scoped service provider handed to tools.</param>
    /// <param name="transport">The registered transport.</param>
    /// <param name="tools">The registered tools.</param>
    /// <param name="approvalPolicy">The registered approval policy.</param>
    /// <param name="options">The configured session defaults.</param>
    /// <param name="loggerFactory">Creates loggers for the sessions.</param>
    public ChatSessionFactory(
        IServiceProvider services,
        IAiTransport transport,
        IToolRegistry tools,
        IToolApprovalPolicy approvalPolicy,
        ChatSessionOptions options,
        ILoggerFactory loggerFactory)
    {
        _services = services;
        _transport = transport;
        _tools = tools;
        _approvalPolicy = approvalPolicy;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates a session for a conversation.
    /// </summary>
    /// <param name="conversation">The conversation to drive, or <see langword="null"/> for a new one.</param>
    /// <param name="options">Overrides for this session only.</param>
    /// <returns>The session.</returns>
    public ChatSession Create(Conversation? conversation = null, ChatSessionOptions? options = null) => new(
        _transport,
        conversation,
        options ?? _options,
        _tools,
        _approvalPolicy,
        _services,
        _loggerFactory.CreateLogger<ChatSession>());
}
