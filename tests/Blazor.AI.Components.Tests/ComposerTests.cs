using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Blazor.AI.Components.Tests;

public sealed class ComposerTests : AiTestBase
{
    private const string ModulePath = "./_content/Blazor.AI.Components/blazor-ai.js";

    /// <summary>A browser stream over fixed bytes, or one whose read fails.</summary>
    private sealed class FakeStreamReference(byte[] bytes, bool fail = false) : IJSStreamReference
    {
        public bool Disposed { get; private set; }

        public long Length => bytes.Length;

        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            fail
                ? ValueTask.FromException<Stream>(new IOException("The transfer was interrupted."))
                : ValueTask.FromResult<Stream>(new MemoryStream(bytes));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new NotSupportedException();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AFileOverTheLimitIsRejectedAndTheUserIsTold()
    {
        var rejections = new List<AttachmentRejection>();
        var composer = Render<PromptInput>(p => p
            .Add(c => c.MaxAttachmentSize, 1024)
            .Add(c => c.OnAttachmentRejected, r => rejections.Add(r)));

        // The browser withholds the content of a file it has already found too large.
        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(new ComposerFile("video.mp4", "video/mp4", 5_000_000)));

        var rejection = Assert.Single(rejections);
        Assert.Equal(AttachmentRejectionReason.TooLarge, rejection.Reason);
        Assert.Equal("video.mp4", rejection.FileName);
        Assert.Empty(composer.Instance.Attachments);

        var notice = composer.Find(".bai-composer__notice");
        Assert.Equal("alert", notice.GetAttribute("role"));
        Assert.Contains("video.mp4 is too large. Files can be up to 1 KB.", notice.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRejectionNoticeCanBeDismissed()
    {
        var composer = Render<PromptInput>(p => p.Add(c => c.MaxAttachmentSize, 10));
        await composer.InvokeAsync(() => composer.Instance.OnPastedFile(new ComposerFile("big.png", "image/png", 11)));

        composer.Find(".bai-composer__notice button").Click();

        Assert.Empty(composer.FindAll(".bai-composer__notice"));
    }

    [Fact]
    public async Task APastedFileOfATypeNotAcceptedIsRejectedWithoutBeingRead()
    {
        var rejections = new List<AttachmentRejection>();
        var content = new FakeStreamReference([1, 2, 3]);
        var composer = Render<PromptInput>(p => p
            .Add(c => c.Accept, "image/*, .pdf")
            .Add(c => c.OnAttachmentRejected, r => rejections.Add(r)));

        await composer.InvokeAsync(() => composer.Instance.OnPastedFile(
            new ComposerFile("notes.txt", "text/plain", 3) { Content = content }));

        Assert.Equal(AttachmentRejectionReason.TypeNotAllowed, Assert.Single(rejections).Reason);
        Assert.True(content.Disposed);
        Assert.Contains("notes.txt is not a supported file type.", composer.Find(".bai-composer__notice").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("report.PDF", "application/octet-stream")]
    [InlineData("shot", "image/png")]
    public async Task AcceptMatchesExtensionsAndWildcardTypes(string name, string contentType)
    {
        var composer = Render<PromptInput>(p => p.Add(c => c.Accept, "image/*, .pdf"));

        await composer.InvokeAsync(() => composer.Instance.OnPastedFile(
            new ComposerFile(name, contentType, 3) { Content = new FakeStreamReference([1, 2, 3]) }));

        Assert.Single(composer.Instance.Attachments);
    }

    [Fact]
    public async Task AFileWithinTheLimitIsStreamedIntoAnAttachment()
    {
        var content = new FakeStreamReference([1, 2, 3, 4]);
        var composer = Render<PromptInput>();

        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(
            new ComposerFile("data.bin", "application/octet-stream", 4) { Content = content }));

        var attachment = Assert.Single(composer.Instance.Attachments);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, attachment.Data!.Value.ToArray());
        Assert.Equal(4, attachment.Size);
        Assert.Equal("data:application/octet-stream;base64,AQIDBA==", attachment.Url);
        Assert.True(content.Disposed);
        Assert.Empty(composer.FindAll(".bai-composer__notice"));
    }

    [Fact]
    public async Task AFileThatCannotBeReadIsReportedRatherThanLost()
    {
        var rejections = new List<AttachmentRejection>();
        var composer = Render<PromptInput>(p => p.Add(c => c.OnAttachmentRejected, r => rejections.Add(r)));

        await composer.InvokeAsync(() => composer.Instance.OnPastedFile(
            new ComposerFile("photo.jpg", "image/jpeg", 4) { Content = new FakeStreamReference([1, 2, 3, 4], fail: true) }));

        Assert.Equal(AttachmentRejectionReason.ReadFailed, Assert.Single(rejections).Reason);
        Assert.Empty(composer.Instance.Attachments);
        Assert.Contains("photo.jpg could not be read.", composer.Find(".bai-composer__notice").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInlineFileStillConvertsSynchronously()
    {
        var attachment = new ComposerFile("a.txt", "text/plain", 2, "aGk=").ToAttachment();

        Assert.Equal("hi"u8.ToArray(), attachment.Data!.Value.ToArray());
    }

    [Fact]
    public void APickedFileOverTheLimitIsRejectedToo()
    {
        var rejections = new List<AttachmentRejection>();
        var composer = Render<PromptInput>(p => p
            .Add(c => c.MaxAttachmentSize, 4)
            .Add(c => c.OnAttachmentRejected, r => rejections.Add(r)));

        composer.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2], "small.bin"),
            InputFileContent.CreateFromBinary([1, 2, 3, 4, 5, 6], "large.bin"));

        composer.WaitForAssertion(() => Assert.Single(composer.Instance.Attachments));
        Assert.Equal("small.bin", composer.Instance.Attachments[0].Name);
        Assert.Equal("large.bin", Assert.Single(rejections).FileName);
        Assert.Contains("large.bin is too large.", composer.Find(".bai-composer__notice").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AMentionInsertedMidSentenceKeepsTheTextAfterTheCaret()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<int?>("getCaret", _ => true).SetResult("hi @Jo".Length);
        var value = string.Empty;

        var composer = Render<PromptInput>(p => p
            .Add(c => c.Mentions, [new MentionItem("u1", "John")])
            .Add(c => c.ValueChanged, v => value = v));

        composer.Find("textarea").Input("hi @Jo there");
        composer.Find("[role=option]").Click();

        Assert.Equal("hi @John there", value);
        composer.WaitForAssertion(() =>
            Assert.Equal("hi @John ".Length, module.VerifyInvoke("setCaret").Arguments[1]));
    }

    [Fact]
    public void ACommandTemplateKeepsTheTextAfterTheCaret()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<int?>("getCaret", _ => true).SetResult("/sum".Length);
        var value = string.Empty;

        var composer = Render<PromptInput>(p => p
            .Add(c => c.Commands, [new SlashCommand("summarize") { Template = "Summarize: " }])
            .Add(c => c.ValueChanged, v => value = v));

        composer.Find("textarea").Input("/sum the report");
        composer.Find("[role=option]").Click();

        Assert.Equal("Summarize: the report", value);
        composer.WaitForAssertion(() =>
            Assert.Equal("Summarize: ".Length, module.VerifyInvoke("setCaret").Arguments[1]));
    }
}
