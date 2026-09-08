namespace Blazor.AI;

/// <summary>
/// An in-memory <see cref="IModelCatalog"/> populated at startup.
/// </summary>
/// <remarks>
/// Deliberately dumb. Hosts that fetch their model list from a gateway implement
/// <see cref="IModelCatalog"/> over that instead; this exists so that the common case of a fixed
/// list is one call rather than a class.
/// </remarks>
public sealed class ModelCatalog : IModelCatalog
{
    private readonly Dictionary<string, AiModel> _models;
    private readonly Dictionary<string, AiProvider> _providers;

    /// <summary>Creates a catalog.</summary>
    /// <param name="models">The models on offer, in the order they should appear in a picker.</param>
    /// <param name="providers">The providers serving them.</param>
    /// <param name="defaultModelId">
    /// The model to select when the user has not chosen. Defaults to the first non-deprecated model.
    /// </param>
    public ModelCatalog(
        IEnumerable<AiModel> models,
        IEnumerable<AiProvider>? providers = null,
        string? defaultModelId = null)
    {
        ArgumentNullException.ThrowIfNull(models);

        Models = models.ToArray();
        Providers = providers?.ToArray() ?? [];
        _models = Models.ToDictionary(m => m.ModelId, StringComparer.OrdinalIgnoreCase);
        _providers = Providers.ToDictionary(p => p.ProviderId, StringComparer.OrdinalIgnoreCase);

        DefaultModel = defaultModelId is not null && _models.TryGetValue(defaultModelId, out var model)
            ? model
            : Models.FirstOrDefault(m => !m.IsDeprecated);
    }

    /// <inheritdoc />
    public IReadOnlyList<AiModel> Models { get; }

    /// <inheritdoc />
    public IReadOnlyList<AiProvider> Providers { get; }

    /// <inheritdoc />
    public AiModel? DefaultModel { get; }

    /// <inheritdoc />
    public AiModel? FindModel(string modelId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        return _models.TryGetValue(modelId, out var model) ? model : null;
    }

    /// <inheritdoc />
    public AiProvider? FindProvider(string providerId)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        return _providers.TryGetValue(providerId, out var provider) ? provider : null;
    }

    /// <summary>Models grouped by provider, in catalog order, for a grouped picker.</summary>
    /// <returns>The groups.</returns>
    public IReadOnlyList<(AiProvider? Provider, IReadOnlyList<AiModel> Models)> GroupByProvider() => Models
        .GroupBy(m => m.ProviderId)
        .Select(g => (
            Provider: g.Key is null ? null : FindProvider(g.Key),
            Models: (IReadOnlyList<AiModel>)g.ToArray()))
        .ToArray();
}
