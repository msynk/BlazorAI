using System.Text.Json;

namespace Blazor.AI.Components.Tests;

public sealed class UsageAndPrimitiveTests : AiTestBase
{
    private const string ModulePath = "./_content/Blazor.AI.Components/blazor-ai.js";

    private static Conversation ConversationWithTurns(int turns, long window = 10_000)
    {
        var conversation = new Conversation();
        for (var i = 0; i < turns; i++)
        {
            conversation.AddMessage(AiRole.User, $"question {i}");
            var reply = conversation.AddMessage(new AiMessage(AiRole.Assistant));
            reply.MergeMetadata(new GenerationMetadata
            {
                Usage = new TokenUsage { InputTokens = 1_000 * (i + 1), OutputTokens = 100 },
                ContextWindow = window,
            });
        }

        return conversation;
    }

    [Fact]
    public void TheContextMeterReadsTheLatestTurnRatherThanSummingEveryTurn()
    {
        var conversation = ConversationWithTurns(3);

        var (used, window) = ContextMeter.Measure(conversation);

        Assert.Equal(3_100, used);
        Assert.Equal(10_000, window);
        Assert.Equal(6_300, conversation.TotalUsage.TotalTokens);
    }

    [Fact]
    public void TheContextMeterCountsPendingTokensOnTopOfTheConversation()
    {
        var view = Render<ContextMeter>(p => p
            .Add(c => c.Conversation, ConversationWithTurns(1))
            .Add(c => c.PendingTokens, 400));

        Assert.Equal("15", view.Find("[role=progressbar]").GetAttribute("aria-valuenow"));
    }

    [Theory]
    [InlineData(79, null)]
    [InlineData(80, "bai-meter--warning")]
    [InlineData(95, "bai-meter--danger")]
    public void TheContextMeterWarnsAtEightyAndNinetyFivePercent(long used, string? severity)
    {
        var view = Render<ContextMeter>(p => p.Add(c => c.Used, used).Add(c => c.Window, 100));

        var meter = view.Find(".bai-meter");
        if (severity is null)
        {
            Assert.DoesNotContain("bai-meter--", meter.ClassName, StringComparison.Ordinal);
            Assert.Empty(view.FindAll("[role=status]"));
        }
        else
        {
            Assert.Contains(severity, meter.ClassName, StringComparison.Ordinal);
            Assert.NotEmpty(view.Find("[role=status]").TextContent);
        }
    }

    [Fact]
    public void TheContextMeterThresholdsAreConfigurable()
    {
        var view = Render<ContextMeter>(p => p
            .Add(c => c.Used, 60).Add(c => c.Window, 100)
            .Add(c => c.Warn, 0.5).Add(c => c.Critical, 0.7));

        Assert.Contains("bai-meter--warning", view.Find(".bai-meter").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCopyButtonAnnouncesTheCopy()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("copyText", _ => true).SetResult(true);

        var button = Render<CopyButton>(p => p
            .Add(c => c.Content, "hello")
            .Add(c => c.ConfirmationDuration, TimeSpan.FromMilliseconds(50)));

        Assert.Equal("Copy", button.Find("button").GetAttribute("aria-label"));
        Assert.Equal(string.Empty, button.Find("[role=status]").TextContent);

        button.Find("button").Click();

        button.WaitForAssertion(() => Assert.Equal("Copied", button.Find("[role=status]").TextContent));
        Assert.Equal("Copied", button.Find("button").GetAttribute("aria-label"));
        button.WaitForAssertion(() => Assert.Equal("Copy", button.Find("button").GetAttribute("aria-label")), TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=", false)]
    [InlineData("data:image/png;base64,iVBORw0KGgo=", true)]
    [InlineData("https://example.com/favicon.ico", true)]
    public void ASourceIconLoadsOnlyFromTheWebOrAnInlineImage(string icon, bool rendered)
    {
        var source = new CitationSource("s1", "Docs") { Url = "https://example.com", IconUrl = icon };

        var card = Render<SourceCard>(p => p.Add(c => c.Source, source));

        Assert.Equal(rendered ? 1 : 0, card.FindAll("img").Count);
        if (rendered) Assert.Equal(icon, card.Find("img").GetAttribute("src"));
    }

    [Fact]
    public void TogglingOneNodeInAJsonTreeLeavesASameNamedNodeInAnotherBranchAlone()
    {
        using var document = JsonDocument.Parse("""{"a":{"x":{"y":1}},"b":{"x":{"y":2}}}""");
        var view = Render<JsonView>(p => p
            .Add(c => c.Value, document.RootElement.Clone())
            .Add(c => c.DefaultExpandDepth, 2));

        string[] States() => view.FindAll("button.bai-json__toggle").Select(b => b.GetAttribute("aria-expanded")!).ToArray();
        Assert.Equal(["true", "true", "false", "true", "false"], States());

        view.FindAll("button.bai-json__toggle")[2].Click();
        Assert.Equal(["true", "true", "true", "true", "false"], States());

        view.FindAll("button.bai-json__toggle")[1].Click();
        Assert.Equal(["true", "false", "true", "false"], States());
    }
}
