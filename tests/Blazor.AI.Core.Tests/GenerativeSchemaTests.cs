using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blazor.AI.GenerativeUI;
using Microsoft.AspNetCore.Components;

namespace Blazor.AI.Tests;

public sealed class GenerativeSchemaTests
{
    public enum Tone { Calm, Urgent }

    public sealed record Line(string Text, double Amount)
    {
        [Description("Whether the line is taxed.")]
        public bool Taxed { get; init; }
    }

    [Description("A folder that can hold folders.")]
    public sealed class Folder
    {
        [Required] public string Name { get; set; } = string.Empty;
        public List<Folder> Children { get; set; } = [];
        public Folder? Parent { get; set; }
    }

    public sealed class Trap
    {
        public string Label { get; set; } = string.Empty;
        public Action? OnFire { get; set; }
    }

    public sealed class Reflective
    {
        public Type? Kind { get; set; }
    }

    [Description("Shows an invoice.")]
    private sealed class InvoiceCard : ComponentBase
    {
        [Parameter, Required] public string Customer { get; set; } = string.Empty;
        [Parameter] public Line[] Lines { get; set; } = [];
        [Parameter] public Folder? Attachments { get; set; }
        [Parameter] public Dictionary<string, decimal>? Totals { get; set; }
        [Parameter] public DateOnly Issued { get; set; }
        [Parameter] public DateTimeOffset? Paid { get; set; }
        [Parameter] public Guid Id { get; set; }
        [Parameter] public Uri? Link { get; set; }
        [Parameter] public TimeSpan Terms { get; set; }
        [Parameter] public Tone? Tone { get; set; }
        [Parameter, GenerativeIgnore] public string TenantKey { get; set; } = string.Empty;
        [Parameter] public Trap? Trap { get; set; }
        [Parameter] public Reflective? Reflective { get; set; }
        [Parameter] public IReadOnlyList<Tone>? Tones { get; set; }
    }

    private static JsonElement Props(GenerativeComponent component) => component.Schema.GetProperty("properties");

    [Fact]
    public void ArraysDescribeTheirItems()
    {
        var component = new GenerativeUiRegistry().Register<InvoiceCard>("invoice");

        var lines = Props(component).GetProperty("Lines");
        Assert.Equal("array", lines.GetProperty("type").GetString());
        var items = lines.GetProperty("items");
        Assert.Equal("object", items.GetProperty("type").GetString());
        Assert.Equal("string", items.GetProperty("properties").GetProperty("Text").GetProperty("type").GetString());
        Assert.Equal("number", items.GetProperty("properties").GetProperty("Amount").GetProperty("type").GetString());
        Assert.Equal("Whether the line is taxed.", items.GetProperty("properties").GetProperty("Taxed").GetProperty("description").GetString());
        Assert.False(items.GetProperty("additionalProperties").GetBoolean());

        var tones = Props(component).GetProperty("Tones");
        Assert.Equal(["Calm", "Urgent"], tones.GetProperty("items").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void NestedTypesAreDescribedRecursivelyAndCyclesAreCutOff()
    {
        var component = new GenerativeUiRegistry().Register<InvoiceCard>("invoice");

        var folder = Props(component).GetProperty("Attachments");
        Assert.Equal("object", folder.GetProperty("type").GetString());
        Assert.Equal("A folder that can hold folders.", folder.GetProperty("description").GetString());
        Assert.Equal(["Name"], folder.GetProperty("required").EnumerateArray().Select(e => e.GetString()));

        var children = folder.GetProperty("properties").GetProperty("Children");
        Assert.Equal("array", children.GetProperty("type").GetString());
        Assert.Equal("object", children.GetProperty("items").GetProperty("type").GetString());
        Assert.False(children.GetProperty("items").TryGetProperty("properties", out _));
        Assert.False(folder.GetProperty("properties").GetProperty("Parent").TryGetProperty("properties", out _));
    }

    [Fact]
    public void DictionariesDescribeTheirValues()
    {
        var component = new GenerativeUiRegistry().Register<InvoiceCard>("invoice");

        var totals = Props(component).GetProperty("Totals");
        Assert.Equal("object", totals.GetProperty("type").GetString());
        Assert.Equal("number", totals.GetProperty("additionalProperties").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("Issued", "date")]
    [InlineData("Paid", "date-time")]
    [InlineData("Id", "uuid")]
    [InlineData("Link", "uri")]
    [InlineData("Terms", "duration")]
    public void WellKnownValueTypesCarryAFormat(string property, string format)
    {
        var component = new GenerativeUiRegistry().Register<InvoiceCard>("invoice");

        var node = Props(component).GetProperty(property);
        Assert.Equal("string", node.GetProperty("type").GetString());
        Assert.Equal(format, node.GetProperty("format").GetString());
    }

    [Fact]
    public void EnumsAreNamesAndNullablesAreOptional()
    {
        var component = new GenerativeUiRegistry().Register<InvoiceCard>("invoice");

        var tone = Props(component).GetProperty("Tone");
        Assert.Equal(["Calm", "Urgent"], tone.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("Tone", component.Schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void RequiredAndIgnoredAttributesAreHonouredAndTheClassDescriptionIsTheFallback()
    {
        var component = new GenerativeUiRegistry().Register<InvoiceCard>("invoice");

        Assert.Equal(["Customer"], component.Schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Shows an invoice.", component.Description);
        Assert.Null(component.FindProperty("TenantKey"));
        Assert.False(Props(component).TryGetProperty("TenantKey", out _));

        var described = new GenerativeUiRegistry().Register<InvoiceCard>("invoice", "Custom.");
        Assert.Equal("Custom.", described.Description);
    }

    [Fact]
    public void TypesWhoseGraphReachesCodeAreNotBindable()
    {
        var component = new GenerativeUiRegistry().Register<InvoiceCard>("invoice");

        Assert.Null(component.FindProperty("Trap"));
        Assert.Null(component.FindProperty("Reflective"));
        Assert.False(GenerativeUiRegistry.IsBindable(typeof(Trap)));
        Assert.False(GenerativeUiRegistry.IsBindable(typeof(List<Trap>)));
        Assert.False(GenerativeUiRegistry.IsBindable(typeof(Dictionary<string, Reflective>)));
        Assert.False(GenerativeUiRegistry.IsBindable(typeof(RenderFragment<int>)));
        Assert.True(GenerativeUiRegistry.IsBindable(typeof(Folder)));
        Assert.True(GenerativeUiRegistry.IsBindable(typeof(Line[])));

        var result = GenerativePropsBinder.Bind(component, JsonDocument.Parse("""{"customer":"Ada","trap":{"label":"x"}}""").RootElement);
        Assert.False(result.Parameters.ContainsKey("Trap"));
        Assert.Equal(["trap"], result.IgnoredProperties);
    }

    [Fact]
    public void TheRequiredCheckRunsEvenWhenNoObjectArrived()
    {
        var component = new GenerativeUiRegistry().Register<InvoiceCard>("invoice");

        var missing = GenerativePropsBinder.Bind(component, null);
        Assert.False(missing.IsComplete);
        Assert.Contains(missing.Errors, e => e.Contains("Customer", StringComparison.Ordinal));

        var wrongShape = GenerativePropsBinder.Bind(component, JsonDocument.Parse("[1]").RootElement);
        Assert.False(wrongShape.IsComplete);
        Assert.Empty(wrongShape.Parameters);
    }

    [Fact]
    public void NestedPayloadsBindThroughTheSameShapeTheSchemaAdvertises()
    {
        var component = new GenerativeUiRegistry().Register<InvoiceCard>("invoice");

        var result = GenerativePropsBinder.Bind(component, JsonDocument.Parse("""
            {
              "customer": "Ada",
              "lines": [ { "text": "Widget", "amount": 9.5, "taxed": true } ],
              "attachments": { "name": "root", "children": [ { "name": "a" } ] },
              "issued": "2026-10-20",
              "tone": "Urgent"
            }
            """).RootElement);

        Assert.True(result.IsComplete, string.Join("; ", result.Errors));
        var line = Assert.Single((Line[])result.Parameters["Lines"]!);
        Assert.Equal(("Widget", 9.5, true), (line.Text, line.Amount, line.Taxed));
        Assert.Equal("a", ((Folder)result.Parameters["Attachments"]!).Children[0].Name);
        Assert.Equal(new DateOnly(2026, 10, 20), result.Parameters["Issued"]);
        Assert.Equal(Tone.Urgent, result.Parameters["Tone"]);
    }
}
