using System.Runtime.CompilerServices;
using Bunit;
using Xunit;

namespace Blazor.AI.Components.Tests;

/// <summary>
/// Behaviours added after the full review: regeneration only on the newest reply, feedback that
/// reaches the host, citation markers that stay out of code, and reasoning that opens while it
/// streams and collapses when the answer arrives.
/// </summary>
public class ChatBehaviourTests : AiTestBase
{
    [Fact]
    public async Task OnlyTheNewestReplyOffersRegeneration()
    {
        var session = new ChatSession(new SilentTransport(), options: new ChatSessionOptions { Pacing = null });
        session.Conversation.AddMessage(AiRole.User, "one");
        session.Conversation.AddMessage(AiRole.Assistant, "first reply");
        session.Conversation.AddMessage(AiRole.User, "two");
        session.Conversation.AddMessage(AiRole.Assistant, "second reply");

        var cut = Render<MessageList>(p => p
            .Add(x => x.Conversation, session.Conversation)
            .Add(x => x.Session, session));

        var regenerate = cut.FindAll("button[title='Regenerate']");
        Assert.Single(regenerate);
        Assert.Contains("second reply", regenerate[0].Closest("article")!.TextContent);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task FeedbackReachesTheHostWithTheMessage()
    {
        var session = new ChatSession(new SilentTransport(), options: new ChatSessionOptions { Pacing = null });
        session.Conversation.AddMessage(AiRole.User, "q");
        var reply = session.Conversation.AddMessage(AiRole.Assistant, "a");

        MessageFeedback? received = null;
        var cut = Render<AiChat>(p => p
            .Add(x => x.Session, session)
            .Add(x => x.OnFeedback, feedback => received = feedback));

        cut.Find("button[title='Good response']").Click();

        Assert.NotNull(received);
        Assert.Same(reply, received!.Message);
        Assert.Equal(FeedbackKind.Positive, received.Feedback);
        Assert.Equal(FeedbackKind.Positive, reply.Feedback);

        await session.DisposeAsync();
    }

    [Theory]
    [InlineData("See `foo` here", 6, true)]
    [InlineData("See `foo` here", 11, false)]
    [InlineData("```\ncode\n```\nprose", 6, true)]
    [InlineData("```\ncode\n```\nprose", 16, false)]
    [InlineData("plain text", 5, false)]
    public void CitationMarkersAreNotInsertedInsideCode(string text, int index, bool insideCode) =>
        Assert.Equal(insideCode, TextPartView.IsInsideCode(text, index));

    [Fact]
    public void ACitationEndingInsideACodeSpanIsLeftToTheSourceList()
    {
        var message = new AiMessage(AiRole.Assistant);
        var part = message.AddPart(new TextPart("Run `dotnet build` first."));
        message.AddCitation(new Citation("c1", new CitationSource("s1", "Docs")) { PartId = part.PartId, StartIndex = 5, EndIndex = 11 });
        message.AddCitation(new Citation("c2", new CitationSource("s2", "Guide")) { PartId = part.PartId, StartIndex = 19, EndIndex = 25 });

        var cut = Render<TextPartView>(p => p.Add(x => x.Part, part).Add(x => x.Message, message));

        Assert.Contains("dotnet build", cut.Find("code").TextContent);
        Assert.DoesNotContain("[1]", cut.Find("code").TextContent);
        Assert.Single(cut.FindAll(".bai-citation"));
    }

    [Fact]
    public void ReasoningIsOpenWhileStreamingAndCollapsesWhenDone()
    {
        var part = new ReasoningPart();
        part.Append("thinking");

        var cut = Render<ReasoningPartView>(p => p.Add(x => x.Part, part));
        Assert.Equal("true", cut.Find("[aria-expanded]").GetAttribute("aria-expanded"));

        part.Complete();
        cut.Render();

        Assert.Equal("false", cut.Find("[aria-expanded]").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void AUserWhoOpenedTheReasoningKeepsItOpen()
    {
        var part = new ReasoningPart();
        part.Append("thinking");

        var cut = Render<ReasoningPartView>(p => p.Add(x => x.Part, part));
        cut.Find("[aria-expanded]").Click(); // close it by hand while streaming
        cut.Find("[aria-expanded]").Click(); // and open it again: an explicit choice

        part.Complete();
        cut.Render();

        Assert.Equal("true", cut.Find("[aria-expanded]").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void ReasoningRendersAsMarkdown()
    {
        var part = new ReasoningPart("Step **one** then two");
        var cut = Render<ReasoningPartView>(p => p.Add(x => x.Part, part));
        cut.Find("[aria-expanded]").Click();

        Assert.Contains("<strong>one</strong>", cut.Markup);
    }

    private sealed class SilentTransport : IAiTransport
    {
        public async IAsyncEnumerable<AiEvent> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new MessageStartedEvent();
            yield return new MessageCompletedEvent();
            await Task.CompletedTask;
        }
    }
}
