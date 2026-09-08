using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Blazor.AI;

/// <summary>
/// Creates tools from delegates, with argument schemas derived from the argument type.
/// </summary>
/// <remarks>
/// Writing an <see cref="IAiTool"/> class for a one-line capability is friction that discourages
/// good tool design. These factories let a tool be declared where it is registered, while still
/// producing a real JSON Schema for the model.
/// </remarks>
/// <example>
/// <code>
/// services.AddAiTool(AiTool.Create(
///     "get_weather",
///     "Look up the current weather for a city.",
///     async (WeatherArgs args, CancellationToken ct) =&gt; await weather.GetAsync(args.City, ct)));
/// </code>
/// </example>
public static class AiTool
{
    /// <remarks>
    /// The schema exporter refuses to run without a type-info resolver, and the implicit reflection
    /// default is not applied to an options instance built this way, so it is set explicitly here.
    /// </remarks>
    private static readonly JsonSerializerOptions SchemaOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>
    /// Creates a tool from a typed delegate, deriving the argument schema from <typeparamref name="TArgs"/>.
    /// </summary>
    /// <typeparam name="TArgs">The argument model. Its JSON Schema is sent to the model.</typeparam>
    /// <typeparam name="TResult">The result model, serialized back to the model as JSON.</typeparam>
    /// <param name="name">The identifier the model uses to call the tool.</param>
    /// <param name="description">What the tool does. The model reads this when deciding to call it.</param>
    /// <param name="handler">The implementation.</param>
    /// <param name="configure">Adjusts the generated definition, for example to require approval.</param>
    /// <returns>The tool.</returns>
    public static IAiTool Create<TArgs, TResult>(
        string name,
        string description,
        Func<TArgs, CancellationToken, ValueTask<TResult>> handler,
        Func<AiToolDefinition, AiToolDefinition>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var definition = new AiToolDefinition(name)
        {
            Description = description,
            ParametersSchema = CreateSchema<TArgs>(),
        };

        if (configure is not null) definition = configure(definition);

        return new DelegateAiTool(definition, async (invocation, cancellationToken) =>
        {
            var args = invocation.GetArguments<TArgs>()
                ?? throw new InvalidOperationException($"Tool '{name}' received arguments that do not match {typeof(TArgs).Name}.");
            var result = await handler(args, cancellationToken).ConfigureAwait(false);
            return ToolOutcome.Success(result);
        });
    }

    /// <summary>
    /// Creates a tool from a delegate that returns a full <see cref="ToolOutcome"/>, for tools that
    /// need to attach citations, custom display text, or a structured failure.
    /// </summary>
    /// <typeparam name="TArgs">The argument model.</typeparam>
    /// <param name="name">The identifier the model uses to call the tool.</param>
    /// <param name="description">What the tool does.</param>
    /// <param name="handler">The implementation.</param>
    /// <param name="configure">Adjusts the generated definition.</param>
    /// <returns>The tool.</returns>
    public static IAiTool Create<TArgs>(
        string name,
        string description,
        Func<TArgs, ToolInvocation, CancellationToken, ValueTask<ToolOutcome>> handler,
        Func<AiToolDefinition, AiToolDefinition>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var definition = new AiToolDefinition(name)
        {
            Description = description,
            ParametersSchema = CreateSchema<TArgs>(),
        };

        if (configure is not null) definition = configure(definition);

        return new DelegateAiTool(definition, async (invocation, cancellationToken) =>
        {
            var args = invocation.GetArguments<TArgs>()
                ?? throw new InvalidOperationException($"Tool '{name}' received arguments that do not match {typeof(TArgs).Name}.");
            return await handler(args, invocation, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Creates a tool from a raw handler, for tools whose arguments are dynamic.
    /// </summary>
    /// <param name="definition">The tool definition, including its schema.</param>
    /// <param name="handler">The implementation.</param>
    /// <returns>The tool.</returns>
    public static IAiTool Create(
        AiToolDefinition definition,
        Func<ToolInvocation, CancellationToken, ValueTask<ToolOutcome>> handler) =>
        new DelegateAiTool(definition, handler);

    /// <summary>
    /// Produces the JSON Schema for a type, which is what a provider needs to constrain arguments.
    /// </summary>
    /// <typeparam name="T">The type to describe.</typeparam>
    /// <returns>The schema.</returns>
    public static JsonElement CreateSchema<T>() => CreateSchema(typeof(T));

    /// <summary>
    /// Produces the JSON Schema for a type.
    /// </summary>
    /// <param name="type">The type to describe.</param>
    /// <returns>The schema.</returns>
    public static JsonElement CreateSchema(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var node = SchemaOptions.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
        });
        return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
    }

    private sealed class DelegateAiTool(
        AiToolDefinition definition,
        Func<ToolInvocation, CancellationToken, ValueTask<ToolOutcome>> handler) : IAiTool
    {
        public AiToolDefinition Definition { get; } = definition;

        public ValueTask<ToolOutcome> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken = default) =>
            handler(invocation, cancellationToken);
    }
}
