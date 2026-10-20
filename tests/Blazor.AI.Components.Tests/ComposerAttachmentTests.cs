using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Blazor.AI.Components.Tests;

public sealed class ComposerAttachmentTests : AiTestBase
{
    private const string ModulePath = "./_content/Blazor.AI.Components/blazor-ai.js";

    private sealed class FakeStreamReference(byte[] bytes) : IJSStreamReference
    {
        public bool Disposed { get; private set; }

        public int Opens { get; private set; }

        public long Length => bytes.Length;

        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            Opens++;
            return ValueTask.FromResult<Stream>(new MemoryStream(bytes));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static ComposerFile File(string name, string type, byte[] bytes, string? preview = null) =>
        new(name, type, bytes.Length) { Content = new FakeStreamReference(bytes), PreviewUrl = preview };

    [Fact]
    public async Task AFileBeyondTheCountLimitIsRejectedAsTooMany()
    {
        var rejections = new List<AttachmentRejection>();
        var composer = Render<PromptInput>(p => p
            .Add(c => c.MaxAttachments, 1)
            .Add(c => c.OnAttachmentRejected, r => rejections.Add(r)));

        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(File("one.bin", "application/octet-stream", [1])));
        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(File("two.bin", "application/octet-stream", [2])));

        Assert.Single(composer.Instance.Attachments);
        var rejection = Assert.Single(rejections);
        Assert.Equal(AttachmentRejectionReason.TooMany, rejection.Reason);
        Assert.Equal("two.bin", rejection.FileName);
        Assert.Contains("Up to 1 files can be attached", composer.Find(".bai-composer__notice").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void PickingMoreFilesThanAllowedRejectsTheExcessWithoutLosingTheRest()
    {
        var rejections = new List<AttachmentRejection>();
        var composer = Render<PromptInput>(p => p
            .Add(c => c.MaxAttachments, 2)
            .Add(c => c.OnAttachmentRejected, r => rejections.Add(r)));

        composer.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1], "a.bin"),
            InputFileContent.CreateFromBinary([2], "b.bin"),
            InputFileContent.CreateFromBinary([3], "c.bin"));

        composer.WaitForAssertion(() => Assert.Equal(2, composer.Instance.Attachments.Count));
        Assert.Equal(AttachmentRejectionReason.TooMany, Assert.Single(rejections).Reason);
        Assert.Equal("c.bin", rejections[0].FileName);
    }

    [Fact]
    public async Task ALargeFileStaysInTheBrowserUntilSomethingOpensIt()
    {
        var content = new FakeStreamReference([1, 2, 3, 4]);
        var composer = Render<PromptInput>(p => p.Add(c => c.MaxInlineSize, 2));

        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(
            new ComposerFile("big.bin", "application/octet-stream", 4) { Content = content }));

        var attachment = Assert.Single(composer.Instance.Attachments);
        Assert.False(attachment.HasInlineData);
        Assert.Null(attachment.Url);
        Assert.NotNull(attachment.ContentFactory);
        Assert.Equal(4, attachment.Size);
        Assert.Equal(0, content.Opens);
        Assert.False(content.Disposed);

        await using var stream = await attachment.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.ToArray());
        Assert.Equal(1, content.Opens);
    }

    [Fact]
    public async Task RemovingALazyAttachmentReleasesItsBrowserStreamAndPreview()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var content = new FakeStreamReference([1, 2, 3, 4]);
        var composer = Render<PromptInput>(p => p.Add(c => c.MaxInlineSize, 2));

        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(
            new ComposerFile("shot.png", "image/png", 4) { Content = content, PreviewUrl = "blob:preview" }));

        Assert.Equal("blob:preview", Assert.Single(composer.Instance.Attachments).ThumbnailUrl);
        Assert.Equal("blob:preview", composer.Find(".bai-attachment__thumb").GetAttribute("src"));

        composer.Find(".bai-attachment button").Click();

        Assert.Empty(composer.Instance.Attachments);
        Assert.True(content.Disposed);
        Assert.Equal("blob:preview", module.VerifyInvoke("revokePreview").Arguments[0]);
    }

    [Fact]
    public async Task DisposingTheComposerReleasesStagedFilesButNotSubmittedOnes()
    {
        var staged = new FakeStreamReference([1, 2, 3, 4]);
        var submitted = new FakeStreamReference([5, 6, 7, 8]);
        var submissions = new List<PromptInput.PromptSubmission>();
        var composer = Render<PromptInput>(p => p
            .Add(c => c.MaxInlineSize, 2)
            .Add(c => c.OnSubmit, s => submissions.Add(s)));

        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(
            new ComposerFile("sent.bin", "application/octet-stream", 4) { Content = submitted }));
        composer.Find(".bai-composer__send").Click();
        Assert.Single(submissions);

        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(
            new ComposerFile("left.bin", "application/octet-stream", 4) { Content = staged }));

        await DisposeComponentsAsync();

        Assert.True(staged.Disposed);
        Assert.False(submitted.Disposed);
    }

    [Fact]
    public async Task ASmallFileIsBufferedAndItsStreamReleased()
    {
        var content = new FakeStreamReference([1, 2]);
        var composer = Render<PromptInput>(p => p.Add(c => c.MaxInlineSize, 2));

        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(
            new ComposerFile("small.bin", "application/octet-stream", 2) { Content = content }));

        var attachment = Assert.Single(composer.Instance.Attachments);
        Assert.True(attachment.HasInlineData);
        Assert.Null(attachment.ContentFactory);
        Assert.True(content.Disposed);
    }

    [Fact]
    public void TheAttachControlIsARealButtonThatOpensTheHiddenInput()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var composer = Render<PromptInput>();

        var button = composer.Find("button.bai-composer__attach");
        Assert.Equal("Attach files", button.GetAttribute("aria-label"));

        var input = composer.Find("input[type=file]");
        Assert.Equal("-1", input.GetAttribute("tabindex"));
        Assert.Equal("true", input.GetAttribute("aria-hidden"));

        button.Click();

        module.VerifyInvoke("clickElement");
    }

    [Fact]
    public void AttachmentsAreHiddenForAModelThatTakesOnlyText()
    {
        var textOnly = new AiModel("t", "Text") { Capabilities = ModelCapabilities.Streaming | ModelCapabilities.Tools };
        var composer = Render<PromptInput>(p => p.Add(c => c.Model, textOnly).Add(c => c.AllowDictation, true));

        Assert.Empty(composer.FindAll("input[type=file]"));
        Assert.Empty(composer.FindAll(".bai-composer__attach"));
        Assert.Empty(composer.FindComponents<VoiceInput>());
    }

    [Fact]
    public void TheAcceptedTypesFollowTheModelCapabilities()
    {
        var composer = Render<PromptInput>(p => p.Add(c => c.Capabilities, ModelCapabilities.Vision));

        Assert.Equal("image/*", composer.Find("input[type=file]").GetAttribute("accept"));

        composer.Render(p => p.Add(c => c.Capabilities, ModelCapabilities.Vision | ModelCapabilities.Documents));
        Assert.Contains("application/pdf", composer.Find("input[type=file]").GetAttribute("accept"), StringComparison.Ordinal);

        composer.Render(p => p.Add(c => c.Accept, ".csv"));
        Assert.Equal(".csv", composer.Find("input[type=file]").GetAttribute("accept"));
    }

    [Fact]
    public async Task ADroppedFileTheModelCannotTakeIsRejected()
    {
        var rejections = new List<AttachmentRejection>();
        var composer = Render<PromptInput>(p => p
            .Add(c => c.Capabilities, ModelCapabilities.Vision)
            .Add(c => c.OnAttachmentRejected, r => rejections.Add(r)));

        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(File("notes.txt", "text/plain", [1])));
        await composer.InvokeAsync(() => composer.Instance.OnDroppedFile(File("shot.png", "image/png", [1])));

        Assert.Equal(AttachmentRejectionReason.TypeNotAllowed, Assert.Single(rejections).Reason);
        Assert.Equal("shot.png", Assert.Single(composer.Instance.Attachments).Name);
    }

    [Fact]
    public void APickedImageGetsABrowserSidePreview()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<string?>("previewFromInput", _ => true).SetResult("blob:thumb");
        var composer = Render<PromptInput>();

        composer.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "photo.png", contentType: "image/png"));

        composer.WaitForAssertion(() => Assert.Equal("blob:thumb", Assert.Single(composer.Instance.Attachments).ThumbnailUrl));
        var call = module.VerifyInvoke("previewFromInput");
        Assert.Equal(0, call.Arguments[1]);
        Assert.Equal("photo.png", call.Arguments[2]);
    }
}
