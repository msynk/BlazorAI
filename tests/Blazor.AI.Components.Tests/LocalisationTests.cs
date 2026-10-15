using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.AI.Components.Tests;

public sealed class LocalisationTests : AiTestBase
{
    private sealed class GermanStrings : AiStrings
    {
        public override string AgentCompleted => "Fertig";

        public override string Submitted => "Gesendet";

        public override string Submit => "Absenden";

        public override string Today => "Heute";

        public override string Transcript => "Unterhaltung";

        public override string FieldRequired(string label) => $"{label} fehlt.";

        public override string CitationLabel(int? number, string title) => $"Quelle {number}: {title}";
    }

    public LocalisationTests() =>
        Services.AddSingleton(AiUiOptions.Default with { Strings = new GermanStrings() });

    [Fact]
    public void AgentStatusComesFromTheStringTable()
    {
        var badge = Render<AgentStatusBadge>(p => p.Add(c => c.Status, AgentRunStatus.Completed));

        Assert.Equal("Fertig", badge.Find(".bai-chip").TextContent.Trim());
    }

    [Fact]
    public void FormLabelsAndValidationComeFromTheStringTable()
    {
        var definition = new AiFormDefinition { Fields = [new AiFormField("city") { Required = true }] };
        var form = Render<AiForm>(p => p.Add(c => c.Definition, definition));

        var submit = form.Find(".bai-btn--primary");
        Assert.Equal("Absenden", submit.TextContent.Trim());

        submit.Click();
        Assert.Equal("City fehlt.", form.Find(".bai-field__error").TextContent);

        form.Render(p => p.Add(c => c.Submitted, true));
        Assert.Contains("Gesendet", form.Find(".bai-chip").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversationGroupsComeFromTheStringTable()
    {
        var list = Render<ConversationList>(p => p.Add(
            c => c.Conversations, [new ConversationSummary("c1", "Trip", DateTimeOffset.Now)]));

        Assert.Equal("Heute", list.Find(".bai-menu__group").TextContent);
    }

    [Fact]
    public void AccessibleNamesComeFromTheStringTable()
    {
        var citation = new Citation("c1", new CitationSource("s1", "Handbook")) { Number = 2 };
        var marker = Render<CitationMarker>(p => p.Add(c => c.Citation, citation));
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var transcript = Render<MessageList>(p => p.Add(c => c.Conversation, new Conversation()));

        Assert.Equal("Quelle 2: Handbook", marker.Find("button").GetAttribute("aria-label"));
        Assert.Equal("Unterhaltung", transcript.Find("[role=log]").GetAttribute("aria-label"));
    }
}

public sealed class DefaultStringTests : AiTestBase
{
    [Fact]
    public void AFormWithoutLabelsUsesTheDefaultEnglishOnes()
    {
        var form = Render<AiForm>(p => p.Add(c => c.Definition, new AiFormDefinition()));

        Assert.Equal("Submit", form.Find(".bai-btn--primary").TextContent.Trim());
        Assert.Equal("Cancel", form.Find(".bai-btn--outline").TextContent.Trim());
    }

    [Fact]
    public void AnEmptyCancelLabelHidesTheCancelButton()
    {
        var form = Render<AiForm>(p => p.Add(c => c.Definition, new AiFormDefinition { CancelLabel = "" }));

        Assert.Empty(form.FindAll(".bai-btn--outline"));
    }
}
