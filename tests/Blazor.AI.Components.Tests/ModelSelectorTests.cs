namespace Blazor.AI.Components.Tests;

public sealed class ModelSelectorTests : AiTestBase
{
    private static readonly AiModel Alpha = new("alpha", "Alpha") { Family = "A series", Capabilities = ModelCapabilities.Streaming | ModelCapabilities.Vision };
    private static readonly AiModel Beta = new("beta", "Beta") { Family = "B series" };
    private static readonly AiModel Old = new("old", "Old") { Family = "A series", IsDeprecated = true };

    [Fact]
    public void TheEffectiveSelectionIsRaisedWhenNothingIsBound()
    {
        string? bound = null;
        AiModel? selected = null;
        var picker = Render<ModelSelector>(p => p
            .Add(c => c.Models, [Alpha, Beta])
            .Add(c => c.ModelIdChanged, id => bound = id)
            .Add(c => c.OnModelSelected, m => selected = m));

        picker.WaitForAssertion(() => Assert.Equal("alpha", bound));
        Assert.Same(Alpha, selected);
        Assert.Equal("alpha", picker.Find("select").GetAttribute("value"));
    }

    [Fact]
    public void ABoundModelThatIsInTheListIsNotReannounced()
    {
        var changes = 0;
        var picker = Render<ModelSelector>(p => p
            .Add(c => c.Models, [Alpha, Beta])
            .Add(c => c.ModelId, "beta")
            .Add(c => c.ModelIdChanged, _ => changes++));

        picker.Render(p => p.Add(c => c.ModelId, "beta"));

        Assert.Equal(0, changes);
        Assert.Equal("beta", picker.Find("select").GetAttribute("value"));
    }

    [Fact]
    public void ADeprecatedBoundModelFallsBackAndTheChangeIsRaised()
    {
        string? bound = null;
        var picker = Render<ModelSelector>(p => p
            .Add(c => c.Models, [Old, Alpha])
            .Add(c => c.ModelId, "old")
            .Add(c => c.ModelIdChanged, id => bound = id));

        picker.WaitForAssertion(() => Assert.Equal("alpha", bound));
        Assert.DoesNotContain(picker.FindAll("option"), o => o.GetAttribute("value") == "old");
    }

    [Fact]
    public void DeprecatedModelsAreShownAndLabelledOnlyOnRequest()
    {
        string? bound = null;
        var picker = Render<ModelSelector>(p => p
            .Add(c => c.Models, [Old, Alpha])
            .Add(c => c.ModelId, "old")
            .Add(c => c.ShowDeprecated, true)
            .Add(c => c.ModelIdChanged, id => bound = id));

        var option = picker.FindAll("option").Single(o => o.GetAttribute("value") == "old");
        Assert.Equal("Old (deprecated)", option.TextContent);
        Assert.Null(bound);
    }

    [Fact]
    public void RequiredCapabilitiesFilterTheListAndMoveTheSelection()
    {
        string? bound = null;
        var picker = Render<ModelSelector>(p => p
            .Add(c => c.Models, [Beta, Alpha])
            .Add(c => c.ModelId, "beta")
            .Add(c => c.RequiredCapabilities, ModelCapabilities.Vision)
            .Add(c => c.ModelIdChanged, id => bound = id));

        Assert.Equal("alpha", Assert.Single(picker.FindAll("option")).GetAttribute("value"));
        picker.WaitForAssertion(() => Assert.Equal("alpha", bound));
    }

    [Fact]
    public void ModelsAreGroupedByFamilyThenProvider()
    {
        var gamma = new AiModel("gamma", "Gamma") { ProviderId = "acme" };
        var loose = new AiModel("delta", "Delta");
        var picker = Render<ModelSelector>(p => p.Add(c => c.Models, [Alpha, Beta, gamma, loose]));

        var groups = picker.FindAll("optgroup").Select(g => g.GetAttribute("label")).ToList();

        Assert.Equal(["A series", "B series", "acme", "Other"], groups);
    }

    [Fact]
    public void TheSelectionIsAnnouncedOnceNotOnEveryRender()
    {
        var selections = 0;
        var picker = Render<ModelSelector>(p => p
            .Add(c => c.Models, [Alpha, Beta])
            .Add(c => c.OnModelSelected, _ => selections++));

        picker.WaitForAssertion(() => Assert.Equal(1, selections));
        picker.Render(p => p.Add(c => c.Models, [Alpha, Beta]));
        picker.Render(p => p.Add(c => c.Models, [Alpha, Beta]));

        Assert.Equal(1, selections);
    }

    [Fact]
    public void ChoosingAnOptionRaisesBothEvents()
    {
        string? bound = null;
        AiModel? selected = null;
        var picker = Render<ModelSelector>(p => p
            .Add(c => c.Models, [Alpha, Beta])
            .Add(c => c.ModelId, "alpha")
            .Add(c => c.ModelIdChanged, id => bound = id)
            .Add(c => c.OnModelSelected, m => selected = m));

        picker.Find("select").Change("beta");

        Assert.Equal("beta", bound);
        Assert.Same(Beta, selected);
    }

    [Fact]
    public void NothingRendersWhenEveryModelIsFilteredOut()
    {
        var picker = Render<ModelSelector>(p => p
            .Add(c => c.Models, [Beta])
            .Add(c => c.RequiredCapabilities, ModelCapabilities.Vision));

        Assert.Empty(picker.FindAll("select"));
    }
}
