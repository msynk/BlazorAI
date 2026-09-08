namespace Blazor.AI.Components.Tests;

public sealed class MessageRenderingTests : AiTestBase
{
    [Fact]
    public void UserMessagesRenderAsABubbleAndAssistantMessagesAsProse()
    {
        var user = new AiMessage(AiRole.User);
        user.AddPart(new TextPart("hello"));

        var userMarkup = Render<ChatMessage>(p => p.Add(c => c.Message, user)).Markup;
        Assert.Contains("bai-message--user", userMarkup, StringComparison.Ordinal);
        Assert.Contains("bai-message__bubble", userMarkup, StringComparison.Ordinal);

        var assistantMarkup = Render<ChatMessage>(p => p.Add(c => c.Message, Assistant(new TextPart("hi")))).Markup;
        Assert.Contains("bai-message--assistant", assistantMarkup, StringComparison.Ordinal);
        Assert.Contains("bai-prose", assistantMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemMessagesRenderAsAPill()
    {
        var system = new AiMessage(AiRole.System);
        system.AddPart(new TextPart("You are helpful."));

        var markup = Render<ChatMessage>(p => p.Add(c => c.Message, system)).Markup;

        Assert.Contains("bai-message__system", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void APendingMessageWithNoContentShowsTheThinkingIndicator()
    {
        var message = new AiMessage(AiRole.Assistant);
        message.SetStatus(MessageStatus.Pending);

        var markup = Render<ChatMessage>(p => p.Add(c => c.Message, message)).Markup;

        Assert.Contains("bai-thinking", markup, StringComparison.Ordinal);
        Assert.Contains("aria-busy", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AStatusLineReplacesTheGenericThinkingLabel()
    {
        var message = new AiMessage(AiRole.Assistant);
        message.SetStatus(MessageStatus.Streaming);
        message.SetStatusText("Searching the web");

        var markup = Render<ChatMessage>(p => p.Add(c => c.Message, message)).Markup;

        Assert.Contains("Searching the web", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedMessageShowsAnAlertWithItsReason()
    {
        var message = new AiMessage(AiRole.Assistant);
        message.SetError(new AiError("rate limited") { IsRetryable = true });

        var markup = Render<ChatMessage>(p => p.Add(c => c.Message, message)).Markup;

        Assert.Contains("role=\"alert\"", markup, StringComparison.Ordinal);
        Assert.Contains("rate limited", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CitedSourcesAreListedUnderTheMessage()
    {
        var message = Assistant(new TextPart("answer"));
        message.AddCitation(new Citation("c1", new CitationSource("s1", "Reference Guide")
        {
            Url = "https://example.com/guide",
        }));

        var markup = Render<ChatMessage>(p => p.Add(c => c.Message, message)).Markup;

        Assert.Contains("Reference Guide", markup, StringComparison.Ordinal);
        Assert.Contains("bai-source", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MessagePartsAreDispatchedByKind()
    {
        var message = Assistant(
            new ReasoningPart("thinking"),
            new TextPart("answer"),
            new ToolCallPart("c1", "search"));

        var markup = Render<ChatMessage>(p => p.Add(c => c.Message, message)).Markup;

        Assert.Contains("bai-reasoning", markup, StringComparison.Ordinal);
        Assert.Contains("bai-tool", markup, StringComparison.Ordinal);
        Assert.Contains("answer", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegisteredPartRendererReplacesTheBuiltInOne()
    {
        Renderers.RegisterPart("acme:badge", context => builder =>
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "acme-badge");
            builder.AddContent(2, ((CustomPart<string>)context.Part).Data);
            builder.CloseElement();
        });

        var message = Assistant(new CustomPart<string>("acme:badge", "shipped"));
        var markup = Render<ChatMessage>(p => p.Add(c => c.Message, message)).Markup;

        Assert.Contains("acme-badge", markup, StringComparison.Ordinal);
        Assert.Contains("shipped", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamingATokenUpdatesTheRenderedText()
    {
        var part = new TextPart();
        var message = Assistant(part);
        var component = Render<ChatMessage>(p => p.Add(c => c.Message, message));

        part.Append("streamed in");

        component.WaitForAssertion(
            () => Assert.Contains("streamed in", component.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));
    }
}
