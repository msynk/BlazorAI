using System.Text.Json;
using Blazor.AI.GenerativeUI;
using Microsoft.AspNetCore.Components;

namespace Blazor.AI.Tests;

public sealed class ToolTests
{
    private sealed record WeatherArgs(string City, int Days);

    [Fact]
    public void SchemaIsDerivedFromTheArgumentType()
    {
        var schema = AiTool.CreateSchema<WeatherArgs>();

        Assert.Equal("object", schema.GetProperty("type").GetString());
        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("City", out _) || properties.TryGetProperty("city", out _));
    }

    [Fact]
    public async Task ATypedToolDeserializesItsArgumentsAndSerializesItsResult()
    {
        var tool = AiTool.Create<WeatherArgs, object>(
            "get_weather",
            "Look up the weather.",
            (args, _) => ValueTask.FromResult<object>(new { summary = $"{args.City} for {args.Days} days" }));

        var arguments = JsonDocument.Parse("""{"city":"Oslo","days":2}""").RootElement;
        var outcome = await tool.InvokeAsync(new ToolInvocation("c1", "get_weather", arguments));

        Assert.True(outcome.IsSuccess);
        Assert.Equal("Oslo for 2 days", outcome.Value!.Value.GetProperty("summary").GetString());
    }

    [Fact]
    public void ToolNamesResolveCaseInsensitively()
    {
        var registry = new ToolRegistry();
        registry.Register(AiTool.Create(new AiToolDefinition("Search"),
            (_, _) => ValueTask.FromResult(ToolOutcome.FromText("ok"))));

        Assert.NotNull(registry.Find("search"));
        Assert.NotNull(registry.Find("SEARCH"));
        Assert.Null(registry.Find("other"));
    }

    [Fact]
    public async Task TheDefaultPolicyApprovesDestructiveToolsAndHonoursAnExplicitChoice()
    {
        var policy = new DefaultToolApprovalPolicy();
        var invocation = new ToolInvocation("c1", "t", default);

        Assert.False(await policy.RequiresApprovalAsync(new AiToolDefinition("read"), invocation));
        Assert.True(await policy.RequiresApprovalAsync(
            new AiToolDefinition("wipe") { Risk = ToolRisk.Destructive }, invocation));
        Assert.True(await policy.RequiresApprovalAsync(
            new AiToolDefinition("send") { RequiresApproval = true }, invocation));
        Assert.False(await policy.RequiresApprovalAsync(
            new AiToolDefinition("wipe") { Risk = ToolRisk.Destructive, RequiresApproval = false }, invocation));
    }

    [Fact]
    public async Task AlwaysApprovingStopsTheQuestionBeingAskedAgain()
    {
        var policy = new DefaultToolApprovalPolicy();
        var definition = new AiToolDefinition("wipe") { Risk = ToolRisk.Destructive };
        var invocation = new ToolInvocation("c1", "wipe", default);

        Assert.True(await policy.RequiresApprovalAsync(definition, invocation));
        policy.RememberAlwaysApproved("wipe");
        Assert.False(await policy.RequiresApprovalAsync(definition, invocation));
    }

    [Fact]
    public void OutcomeDisplayTextFallsBackThroughTextThenJson()
    {
        Assert.Equal("shown", ToolOutcome.FromText("shown").GetDisplayText());
        Assert.Equal("boom", ToolOutcome.Failure("boom").GetDisplayText());
        Assert.Equal("""{"a":1}""", ToolOutcome.Success(new { a = 1 }).GetDisplayText());
    }
}

public sealed class GenerativeUiTests
{
    private sealed class WeatherCard : ComponentBase
    {
        [Parameter] public string City { get; set; } = string.Empty;
        [Parameter] public int Days { get; set; }
        [Parameter] public EventCallback OnClick { get; set; }
        [Parameter] public RenderFragment? ChildContent { get; set; }
        [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? Extra { get; set; }
    }

    [Fact]
    public void RegistrationExposesOnlyBindableParameters()
    {
        var registry = new GenerativeUiRegistry();
        var component = registry.Register<WeatherCard>("weather_card", "Shows a forecast.", "City");

        var names = component.Properties.Select(p => p.Name).ToArray();
        Assert.Contains("City", names);
        Assert.Contains("Days", names);
        Assert.DoesNotContain("OnClick", names);
        Assert.DoesNotContain("ChildContent", names);
        Assert.DoesNotContain("Extra", names);
    }

    [Fact]
    public void UnregisteredNamesResolveToNothing()
    {
        var registry = new GenerativeUiRegistry();
        registry.Register<WeatherCard>("weather_card");

        Assert.NotNull(registry.Find("WEATHER_CARD"));
        Assert.Null(registry.Find("System.Diagnostics.Process"));
    }

    [Fact]
    public void BindingConvertsKnownPropertiesAndRejectsUnknownOnes()
    {
        var registry = new GenerativeUiRegistry();
        var component = registry.Register<WeatherCard>("weather_card");
        var props = JsonDocument.Parse("""{"city":"Oslo","days":3,"onClick":"evil","nope":1}""").RootElement;

        var result = GenerativePropsBinder.Bind(component, props);

        Assert.Equal("Oslo", result.Parameters["City"]);
        Assert.Equal(3, result.Parameters["Days"]);
        Assert.False(result.Parameters.ContainsKey("OnClick"));
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void BindingReportsMissingRequiredPropertiesWithoutThrowing()
    {
        var registry = new GenerativeUiRegistry();
        var component = registry.Register<WeatherCard>("weather_card", null, "City");

        var result = GenerativePropsBinder.Bind(component, JsonDocument.Parse("""{"days":1}""").RootElement);

        Assert.False(result.IsComplete);
        Assert.Contains(result.Errors, e => e.Contains("City", StringComparison.Ordinal));
    }

    [Fact]
    public void BindingToleratesAPartialPayload()
    {
        var registry = new GenerativeUiRegistry();
        var component = registry.Register<WeatherCard>("weather_card");
        var partial = PartialJson.TryParse("""{"city":"Osl""");

        var result = GenerativePropsBinder.Bind(component, partial);

        Assert.Equal("Osl", result.Parameters["City"]);
    }

    [Fact]
    public void TheCatalogueSchemaListsEveryRegisteredComponent()
    {
        var registry = new GenerativeUiRegistry();
        registry.Register<WeatherCard>("weather_card", "Shows a forecast.");

        var schema = registry.DescribeAll();
        var variants = schema.GetProperty("oneOf");

        Assert.Equal(1, variants.GetArrayLength());
        Assert.Equal("weather_card",
            variants[0].GetProperty("properties").GetProperty("component").GetProperty("const").GetString());
    }

    public enum Severity { Info, Warning }

    public sealed class Unconstructible(int code)
    {
        public int Value { get; } = code;
    }

    private sealed class AlertCard : ComponentBase
    {
        [Parameter] public Severity Level { get; set; }
        [Parameter] public DateTimeOffset When { get; set; }
        [Parameter] public Guid Id { get; set; }
        [Parameter] public byte Priority { get; set; }
        [Parameter] public Dictionary<string, string>? Labels { get; set; }
        [Parameter] public List<string>? Tags { get; set; }
        [Parameter] public Unconstructible? Broken { get; set; }
    }

    [Fact]
    public void EnumPropertiesBindFromTheNamesTheSchemaAdvertises()
    {
        var component = new GenerativeUiRegistry().Register<AlertCard>("alert");
        Assert.Equal("string", component.Schema.GetProperty("properties").GetProperty("Level").GetProperty("type").GetString());

        var result = GenerativePropsBinder.Bind(component, JsonDocument.Parse("""{"level":"Warning"}""").RootElement);

        Assert.True(result.IsComplete, string.Join("; ", result.Errors));
        Assert.Equal(Severity.Warning, result.Parameters["Level"]);
    }

    [Theory]
    [InlineData("When", "string")]
    [InlineData("Id", "string")]
    [InlineData("Priority", "integer")]
    [InlineData("Labels", "object")]
    [InlineData("Tags", "array")]
    public void TheSchemaDescribesEachTypeTheWayTheBinderReadsIt(string property, string expected)
    {
        var component = new GenerativeUiRegistry().Register<AlertCard>("alert");

        var type = component.Schema.GetProperty("properties").GetProperty(property).GetProperty("type").GetString();

        Assert.Equal(expected, type);
    }

    [Fact]
    public void AParameterTypeTheSerializerCannotBuildIsReportedNotThrown()
    {
        var component = new GenerativeUiRegistry().Register<AlertCard>("alert");

        var result = GenerativePropsBinder.Bind(component,
            JsonDocument.Parse("""{"broken":{"value":1},"level":"Info"}""").RootElement);

        Assert.Contains(result.Errors, e => e.Contains("broken", StringComparison.Ordinal));
        Assert.Equal(Severity.Info, result.Parameters["Level"]);
    }
}
