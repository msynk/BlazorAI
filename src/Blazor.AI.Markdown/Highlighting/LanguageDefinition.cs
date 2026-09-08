namespace Blazor.AI.Markdown;

/// <summary>
/// Describes a language well enough to highlight it.
/// </summary>
/// <remarks>
/// Deliberately a data description rather than a grammar. A real parser per language would be more
/// accurate and far more code; for the fragments that appear in chat responses, a lexical
/// description gets the colours right and never fails on incomplete input.
/// </remarks>
public sealed record LanguageDefinition
{
    /// <summary>Creates a language definition.</summary>
    /// <param name="id">The canonical identifier, matching the fence info string.</param>
    public LanguageDefinition(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    /// <summary>The canonical identifier, matching the fence info string.</summary>
    public string Id { get; }

    /// <summary>Other names that select this language.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Words highlighted as keywords.</summary>
    public IReadOnlySet<string> Keywords { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Words highlighted as type names.</summary>
    public IReadOnlySet<string> Types { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Words highlighted as constants, such as boolean and null literals.</summary>
    public IReadOnlySet<string> Constants { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Sequences that start a comment running to the end of the line.</summary>
    public IReadOnlyList<string> LineComments { get; init; } = ["//"];

    /// <summary>Paired sequences that delimit a block comment.</summary>
    public IReadOnlyList<(string Open, string Close)> BlockComments { get; init; } = [("/*", "*/")];

    /// <summary>Characters that delimit a string literal.</summary>
    public IReadOnlyList<char> StringDelimiters { get; init; } = ['"', '\''];

    /// <summary>Whether a backslash escapes the next character inside a string.</summary>
    public bool StringEscapes { get; init; } = true;

    /// <summary>Whether a string literal may span lines.</summary>
    public bool MultiLineStrings { get; init; }

    /// <summary>Whether the language is markup, which switches to the tag-aware scanner.</summary>
    public bool IsMarkup { get; init; }

    /// <summary>Whether identifiers before a colon are highlighted as properties, as in JSON.</summary>
    public bool ColonProperties { get; init; }

    /// <summary>Whether words are matched case-insensitively, as in SQL.</summary>
    public bool IgnoreCase { get; init; }

    /// <summary>The character that introduces an annotation or attribute, such as an at sign.</summary>
    public char? AnnotationPrefix { get; init; }
}
