namespace Blazor.AI.Components.Tests;

public sealed class ArtifactRenderingTests : AiTestBase
{
    private static (Conversation Conversation, AiMessage Message, ArtifactPart Part) WithArtifact(
        ArtifactKind kind, string title, string content)
    {
        var conversation = new Conversation();
        var artifact = conversation.GetOrAddArtifact("a1", kind, title);
        artifact.SetContent(content);
        artifact.Complete();

        var part = new ArtifactPart("a1") { ArtifactVersion = artifact.Version };
        var message = conversation.AddMessage(Assistant(part));
        return (conversation, message, part);
    }

    private IRenderedComponent<MessagePartView> RenderPart((Conversation Conversation, AiMessage Message, ArtifactPart Part) setup) =>
        Render<MessagePartView>(p => p
            .Add(c => c.Part, setup.Part)
            .Add(c => c.Message, setup.Message)
            .Add(c => c.Conversation, setup.Conversation));

    [Fact]
    public void AnArtifactPartRendersTheArtifactPanelRatherThanAnUnknownElement()
    {
        var component = RenderPart(WithArtifact(ArtifactKind.Document, "Project README", "# Hello\n\nWorld"));

        Assert.DoesNotContain("artifactpartview", component.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Project README", component.Find(".bai-artifact__title").TextContent);
        Assert.NotNull(component.Find(".bai-artifact .bai-prose"));
        Assert.NotNull(component.Find("button[aria-label='Download']"));
        Assert.NotNull(component.Find("button[aria-label='Expand']"));
    }

    [Fact]
    public void AnArtifactThatCannotBeResolvedRendersAPlaceholder()
    {
        var setup = WithArtifact(ArtifactKind.Code, "x", "y");
        var component = Render<MessagePartView>(p => p
            .Add(c => c.Part, new ArtifactPart("missing"))
            .Add(c => c.Message, setup.Message)
            .Add(c => c.Conversation, setup.Conversation));

        Assert.Empty(component.FindAll(".bai-artifact"));
        Assert.Contains("Artifact", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlArtifactsRenderInAFrameWithoutSameOriginAccess()
    {
        var component = RenderPart(WithArtifact(ArtifactKind.Html, "Page", "<p>hi</p><script>parent.x=1</script>"));

        var frame = component.Find("iframe.bai-artifact__frame");
        Assert.Equal("allow-scripts allow-forms allow-popups", frame.GetAttribute("sandbox"));
        Assert.DoesNotContain("allow-same-origin", frame.GetAttribute("sandbox"), StringComparison.Ordinal);
        Assert.Empty(component.FindAll(".bai-artifact__body script"));
    }

    [Fact]
    public void SvgArtifactsRenderAsAnImageSoEmbeddedScriptNeverRuns()
    {
        var component = RenderPart(WithArtifact(ArtifactKind.Svg, "Logo", "<svg onload=\"alert(1)\"><script>alert(2)</script></svg>"));

        var image = component.Find(".bai-artifact__body img");
        Assert.StartsWith("data:image/svg+xml;base64,", image.GetAttribute("src"), StringComparison.Ordinal);
        Assert.Empty(component.FindAll(".bai-artifact__body svg"));
    }

    [Fact]
    public void CsvTableArtifactsRenderAsATable()
    {
        var component = RenderPart(WithArtifact(ArtifactKind.Table, "Prices", "name,price\n\"Widget, large\",3\nGadget,4\n"));

        Assert.Equal(["name", "price"], component.FindAll("th").Select(th => th.TextContent));
        Assert.Equal("Widget, large", component.FindAll("td")[0].TextContent);
        Assert.Equal(2, component.FindAll("tbody tr").Count);
    }

    [Fact]
    public void ARegisteredArtifactRendererReplacesTheBuiltInBody()
    {
        Renderers.RegisterArtifact(ArtifactKind.Chart, context => builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "my-chart");
            builder.AddContent(2, context.Artifact.Content);
            builder.CloseElement();
        });

        var component = RenderPart(WithArtifact(ArtifactKind.Chart, "Sales", "{\"type\":\"bar\"}"));

        Assert.Equal("{\"type\":\"bar\"}", component.Find(".my-chart").TextContent);
    }

    [Fact]
    public void StreamedContentAppearsAsItArrives()
    {
        var conversation = new Conversation();
        var artifact = conversation.GetOrAddArtifact("a1", ArtifactKind.Code, "Program");
        artifact.Language = "csharp";
        var component = Render<ArtifactView>(p => p.Add(c => c.Artifact, artifact));

        artifact.Append("var answer = 42;");

        component.WaitForAssertion(() =>
            Assert.Contains("answer", component.Find(".bai-artifact__body code").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void AnEarlierTurnKeepsShowingTheVersionItProducedAndOffersAVersionSelector()
    {
        var conversation = new Conversation();
        var artifact = conversation.GetOrAddArtifact("a1", ArtifactKind.Code, "Program");
        artifact.SetContent("first draft");
        artifact.Complete();
        var firstVersion = artifact.Version;

        artifact.BeginRevision();
        artifact.SetContent("second draft");
        artifact.Complete();

        var older = Render<ArtifactView>(p => p
            .Add(c => c.Artifact, artifact)
            .Add(c => c.InitialVersion, firstVersion));
        var latest = Render<ArtifactView>(p => p.Add(c => c.Artifact, artifact));

        Assert.Contains("first draft", older.Find(".bai-artifact__body").TextContent, StringComparison.Ordinal);
        Assert.Contains("second draft", latest.Find(".bai-artifact__body").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, older.FindAll("select[aria-label='Version'] option").Count);

        older.Find("select[aria-label='Version']").Change(artifact.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Contains("second draft", older.Find(".bai-artifact__body").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFullScreenToggleExpandsAndEscapeCollapses()
    {
        var setup = WithArtifact(ArtifactKind.Code, "Program", "x");
        var component = Render<ArtifactView>(p => p.Add(c => c.Artifact, setup.Conversation.FindArtifact("a1")!));

        component.Find("button[aria-label='Expand']").Click();
        Assert.NotNull(component.Find(".bai-artifact--expanded"));
        Assert.NotNull(component.Find(".bai-backdrop"));

        component.Find(".bai-artifact").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(component.FindAll(".bai-artifact--expanded"));
    }

    [Theory]
    [InlineData("document", null, "Project README", "project-readme.md", "text/markdown")]
    [InlineData("code", "csharp", "Program", "program.cs", "text/plain")]
    [InlineData("html", null, "Landing page!", "landing-page.html", "text/html")]
    [InlineData("table", null, "Prices", "prices.csv", "text/csv")]
    [InlineData("svg", null, "Logo", "logo.svg", "image/svg+xml")]
    public void DownloadsGetASensibleFileNameAndMimeType(string kind, string? language, string title, string fileName, string mimeType)
    {
        var artifact = new Artifact("a1", new ArtifactKind(kind), title) { Language = language };

        var (actualName, actualMime) = ArtifactView.DownloadInfo(artifact, "a,b");

        Assert.Equal(fileName, actualName);
        Assert.Equal(mimeType, actualMime);
    }

    [Fact]
    public void DelimitedParsingHonoursQuotedFieldsAndEscapedQuotes()
    {
        var rows = ArtifactView.ParseDelimited("a,b\r\n\"x, \"\"y\"\"\",2");

        Assert.Equal(["a", "b"], rows[0]);
        Assert.Equal(["x, \"y\"", "2"], rows[1]);
    }
}
