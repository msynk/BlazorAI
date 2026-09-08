using System.Collections.Concurrent;

namespace Blazor.AI.Markdown;

/// <summary>
/// The languages the highlighter knows, keyed by identifier and alias.
/// </summary>
/// <remarks>
/// Register a <see cref="LanguageDefinition"/> to add a language, or replace a built-in one to
/// change how it is coloured, without touching the highlighter itself.
/// </remarks>
public static class LanguageRegistry
{
    private static readonly ConcurrentDictionary<string, LanguageDefinition> Languages =
        new(StringComparer.OrdinalIgnoreCase);

    static LanguageRegistry()
    {
        foreach (var language in BuiltInLanguages.All) Register(language);
    }

    /// <summary>Adds or replaces a language, indexing it by identifier and every alias.</summary>
    /// <param name="language">The language definition.</param>
    public static void Register(LanguageDefinition language)
    {
        ArgumentNullException.ThrowIfNull(language);
        Languages[language.Id] = language;
        foreach (var alias in language.Aliases) Languages[alias] = language;
    }

    /// <summary>Finds a language by identifier or alias.</summary>
    /// <param name="name">The name from the fence info string.</param>
    /// <returns>The language, or <see langword="null"/> when it is not registered.</returns>
    public static LanguageDefinition? Find(string? name) =>
        name is not null && Languages.TryGetValue(name, out var language) ? language : null;

    /// <summary>Whether a language is registered under this name.</summary>
    /// <param name="name">The name from the fence info string.</param>
    /// <returns><see langword="true"/> when highlighting is available.</returns>
    public static bool IsSupported(string? name) => Find(name) is not null;

    /// <summary>The distinct registered languages.</summary>
    public static IReadOnlyCollection<LanguageDefinition> All =>
        Languages.Values.DistinctBy(l => l.Id).OrderBy(l => l.Id, StringComparer.Ordinal).ToArray();
}
