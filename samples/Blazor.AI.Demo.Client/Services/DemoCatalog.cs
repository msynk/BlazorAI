using System.ComponentModel;

namespace Blazor.AI.Demo.Client.Services;

/// <summary>
/// The models, providers, and tools the demo offers.
/// </summary>
public static class DemoCatalog
{
    /// <summary>The providers shown in the model picker.</summary>
    public static IReadOnlyList<AiProvider> Providers { get; } =
    [
        new AiProvider("demo", "Demo Provider") { Website = "https://example.com" },
        new AiProvider("local", "Local Runtime"),
    ];

    /// <summary>The models shown in the model picker.</summary>
    public static IReadOnlyList<AiModel> Models { get; } =
    [
        new AiModel("demo-model-1", "Demo Large")
        {
            ProviderId = "demo",
            Family = "Demo Provider",
            Description = "The general model. Slower, better at long reasoning.",
            Capabilities = ModelCapabilities.Streaming | ModelCapabilities.Tools | ModelCapabilities.Vision
                | ModelCapabilities.Reasoning | ModelCapabilities.StructuredOutput | ModelCapabilities.Citations,
            ContextWindow = 200_000,
            MaxOutputTokens = 16_000,
            InputPricePerMillion = 3m,
            OutputPricePerMillion = 15m,
        },
        new AiModel("demo-model-1-fast", "Demo Fast")
        {
            ProviderId = "demo",
            Family = "Demo Provider",
            Description = "Cheaper and quicker. No reasoning output.",
            Capabilities = ModelCapabilities.Streaming | ModelCapabilities.Tools
                | ModelCapabilities.StructuredOutput,
            ContextWindow = 128_000,
            MaxOutputTokens = 8_000,
            InputPricePerMillion = 0.5m,
            OutputPricePerMillion = 1.5m,
        },
        new AiModel("local-small", "Local Small")
        {
            ProviderId = "local",
            Family = "Local Runtime",
            Description = "Runs on the machine. No network, no cost.",
            Capabilities = ModelCapabilities.Streaming,
            ContextWindow = 32_000,
            InputPricePerMillion = 0m,
            OutputPricePerMillion = 0m,
        },
    ];

    /// <summary>The tools the demo registers.</summary>
    /// <returns>The tools.</returns>
    public static IReadOnlyList<IAiTool> CreateTools() =>
    [
        AiTool.Create<WeatherArgs, WeatherResult>(
            "get_weather",
            "Look up the current weather for a city.",
            async (args, token) =>
            {
                await Task.Delay(700, token);
                return Weather(args.City);
            },
            definition => definition with { Title = "Weather", Icon = "globe", Risk = ToolRisk.ReadOnly }),

        AiTool.Create<SearchArgs>(
            "search_docs",
            "Search the product documentation and return matching passages.",
            async (args, invocation, token) =>
            {
                invocation.Progress.Report(new ToolProgress(0.2, "Querying the index"));
                await Task.Delay(500, token);
                invocation.Progress.Report(new ToolProgress(0.7, "Ranking results"));
                await Task.Delay(500, token);

                var source = new CitationSource("docs-render-modes", "Render modes")
                {
                    Kind = SourceKind.Document,
                    SiteName = "Product documentation",
                    Url = "https://example.com/docs/render-modes",
                };

                return ToolOutcome.Success(new
                {
                    query = args.Query,
                    hits = new[]
                    {
                        new { title = "Render modes", score = 0.94 },
                        new { title = "Circuit lifetime", score = 0.81 },
                    },
                }) with
                {
                    Text = $"2 passages matched “{args.Query}”.",
                    Citations = [new Citation(AiId.New("cit"), source)],
                };
            },
            definition => definition with { Title = "Search docs", Icon = "search" }),

        AiTool.Create<DeleteArgs, DeleteResult>(
            "delete_records",
            "Permanently delete records matching a filter.",
            async (args, token) =>
            {
                await Task.Delay(600, token);
                return new DeleteResult(args.Filter, 128);
            },
            definition => definition with
            {
                Title = "Delete records",
                Icon = "trash",
                Risk = ToolRisk.Destructive,
            }),
    ];

    private static WeatherResult Weather(string city) => city.ToLowerInvariant() switch
    {
        "oslo" => new WeatherResult("Oslo", -3, "Snow", 81),
        "lisbon" => new WeatherResult("Lisbon", 17, "Clear", 54),
        "auckland" => new WeatherResult("Auckland", 22, "Showers", 72),
        _ => new WeatherResult(city, 14, "Cloudy", 66),
    };

    /// <summary>Arguments for the weather tool.</summary>
    /// <param name="City">The city to look up.</param>
    /// <param name="Days">How many days ahead to forecast.</param>
    public sealed record WeatherArgs(
        [property: Description("The city to look up, for example Oslo.")] string City,
        [property: Description("How many days ahead to forecast.")] int Days = 1);

    /// <summary>The weather for one city.</summary>
    /// <param name="City">The city.</param>
    /// <param name="Degrees">The temperature in Celsius.</param>
    /// <param name="Condition">A one-word summary.</param>
    /// <param name="Humidity">Relative humidity as a percentage.</param>
    public sealed record WeatherResult(string City, int Degrees, string Condition, int Humidity);

    /// <summary>Arguments for the documentation search tool.</summary>
    /// <param name="Query">What to search for.</param>
    public sealed record SearchArgs(string Query);

    /// <summary>Arguments for the destructive delete tool.</summary>
    /// <param name="Filter">Which records to delete.</param>
    public sealed record DeleteArgs(string Filter);

    /// <summary>The outcome of a delete.</summary>
    /// <param name="Filter">The filter that was applied.</param>
    /// <param name="Deleted">How many records were removed.</param>
    public sealed record DeleteResult(string Filter, int Deleted);
}
