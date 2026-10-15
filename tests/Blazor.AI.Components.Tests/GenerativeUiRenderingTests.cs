using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazor.AI.Components.Tests;

public sealed class GenerativeUiRenderingTests : AiTestBase
{
    private sealed class ForecastCard : ComponentBase
    {
        [Parameter] public string City { get; set; } = string.Empty;

        [Parameter] public int Degrees { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "forecast");
            builder.AddContent(2, $"{City}: {Degrees}");
            builder.CloseElement();
        }
    }

    [Fact]
    public void ARegisteredComponentIsInstantiatedWithModelSuppliedProperties()
    {
        Generative.Register<ForecastCard>("forecast_card");

        var part = new GenerativeUiPart("forecast_card");
        part.SetProps(JsonDocument.Parse("""{"city":"Oslo","degrees":12}""").RootElement);

        var markup = Render<GenerativeUiView>(p => p.Add(c => c.Part, part)).Markup;

        Assert.Contains("forecast", markup, StringComparison.Ordinal);
        Assert.Contains("Oslo: 12", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoredPropertiesAreListedApartFromErrors()
    {
        Generative.Register<ForecastCard>("forecast_card");

        var part = new GenerativeUiPart("forecast_card");
        part.SetProps(JsonDocument.Parse("""{"city":"Oslo","degrees":"warm","mood":"sunny"}""").RootElement);
        part.Complete();

        var view = Render<GenerativeUiView>(p => p
            .Add(c => c.Part, part)
            .Add(c => c.ShowBindingErrors, true));

        var ignored = Assert.Single(view.FindAll(".bai-chip--muted"));
        Assert.Equal("Ignored: mood", ignored.TextContent);
        var error = Assert.Single(view.FindAll(".bai-chip--danger"));
        Assert.DoesNotContain("mood", error.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoredPropertiesAloneAreListedWhenBindingOtherwiseSucceeded()
    {
        Generative.Register<ForecastCard>("forecast_card");

        var part = new GenerativeUiPart("forecast_card");
        part.SetProps(JsonDocument.Parse("""{"city":"Oslo","degrees":12,"mood":"sunny"}""").RootElement);
        part.Complete();

        var view = Render<GenerativeUiView>(p => p
            .Add(c => c.Part, part)
            .Add(c => c.ShowBindingErrors, true));

        Assert.Equal("Ignored: mood", Assert.Single(view.FindAll(".bai-chip--muted")).TextContent);
        Assert.Empty(view.FindAll(".bai-chip--danger"));
    }

    [Fact]
    public void AnUnregisteredComponentNameRendersAPlaceholderRatherThanThrowing()
    {
        var part = new GenerativeUiPart("definitely_not_registered");
        part.SetProps(JsonDocument.Parse("{}").RootElement);
        part.Complete();

        var markup = Render<GenerativeUiView>(p => p.Add(c => c.Part, part)).Markup;

        Assert.Contains("definitely_not_registered", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("forecast", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PartialPropertiesStillRender()
    {
        Generative.Register<ForecastCard>("forecast_card");

        var part = new GenerativeUiPart("forecast_card");
        part.AppendProps("""{"city":"Osl""");

        var markup = Render<GenerativeUiView>(p => p.Add(c => c.Part, part)).Markup;

        Assert.Contains("Osl: 0", markup, StringComparison.Ordinal);
    }
}

public sealed class StructuredDataRenderingTests : AiTestBase
{
    [Fact]
    public void AnArrayOfObjectsBecomesATableWithTheUnionOfItsKeys()
    {
        var part = new StructuredDataPart(
            JsonDocument.Parse("""[{"name":"Ada"},{"name":"Alan","field":"logic"}]""").RootElement);

        var markup = Render<StructuredDataView>(p => p.Add(c => c.Part, part)).Markup;

        Assert.Contains("<table", markup, StringComparison.Ordinal);
        Assert.Contains("Name", markup, StringComparison.Ordinal);
        Assert.Contains("Field", markup, StringComparison.Ordinal);
        Assert.Contains("logic", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectBecomesALabelledKeyValueList()
    {
        var part = new StructuredDataPart(
            JsonDocument.Parse("""{"firstName":"Ada","born":1815}""").RootElement);

        var markup = Render<StructuredDataView>(p => p.Add(c => c.Part, part)).Markup;

        Assert.Contains("<dt", markup, StringComparison.Ordinal);
        Assert.Contains("First Name", markup, StringComparison.Ordinal);
        Assert.Contains("1815", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegisteredSchemaRendererTakesOver()
    {
        Renderers.RegisterStructured("invoice", _ => builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "invoice-card");
            builder.CloseElement();
        });

        var part = new StructuredDataPart(JsonDocument.Parse("""{"total":10}""").RootElement, "invoice");
        var markup = Render<StructuredDataView>(p => p.Add(c => c.Part, part)).Markup;

        Assert.Contains("invoice-card", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<dt", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PartialStructuredDataFillsInAsItArrives()
    {
        var part = new StructuredDataPart();
        part.Append("""[{"name":"Ada"},{"name":"Al""");

        var markup = Render<StructuredDataView>(p => p.Add(c => c.Part, part)).Markup;

        Assert.Contains("Ada", markup, StringComparison.Ordinal);
        Assert.Contains("Al", markup, StringComparison.Ordinal);
    }
}
