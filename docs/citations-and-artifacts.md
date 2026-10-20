# Citations and artifacts

## Citations

A `Citation` attributes a claim to a `CitationSource`. Several citations can share one source; the
source list is deduplicated and numbered in the order first seen, so markers read `[1]`, `[2]` in
document order regardless of the order events arrived.

```csharp
var source = new CitationSource("s1", "Render modes")
{
    Kind = SourceKind.Web,
    Url = "https://learn.microsoft.com/aspnet/core/blazor/components/render-modes",
    SiteName = "Microsoft Learn",
};

yield return new CitationEvent(new Citation("c1", source)
{
    StartIndex = 0,
    EndIndex = sentence.Length,
    Quote = "Interactive Server renders on the server and sends updates over a circuit.",
});
```

### Anchored, not appended

Offsets are the difference between a footnote and an attribution. A citation carrying `StartIndex`
and `EndIndex` addresses a span of the text part, and the renderer inserts a marker at that offset
before parsing, so the superscript lands mid-sentence where the claim actually sits.

Offsets are measured in UTF-16 code units against the final text of the part. A span reaching past
the text received so far is held back until the text catches up, so a marker never appears in the
wrong sentence while streaming.

A citation with no offsets is still added to the source list. That is the right shape when a provider
reports which documents were consulted but not which sentence used which.

### Rendering

The source list appears under any message that cites something. Place it yourself with a
`SourcesPart`, optionally restricted to particular citations:

```csharp
message.AddPart(new SourcesPart { CitationIds = ["c1", "c2"] });
```

Handle activation to show a source in a side panel rather than navigating away:

```razor
<AiChat OnCitationClick="ShowSource" />
```

Replace the chip entirely through `AiRendererRegistry.CitationRenderer`, or restyle it with the
accent tokens.

### URL safety

Source URLs come from the model or from retrieved content, and Blazor writes an `href` or `src` as
given, so they are never trusted as addresses. `SourceCard` renders a source as a link only when
its `Url` parses as an absolute `http` or `https` address; anything else, such as a `javascript:`
or `data:` URL, renders the same card as a button that raises `OnActivate` and navigates nowhere.
The site name falls back to the host only for a safe URL. `IconUrl` is loaded only when it is a web
address or a `data:image/...` payload that is not SVG, since an image can carry script where a
bitmap cannot. `SourceList` and the citation marker go through the same card, so there is no second
path for a URL to reach the page.

## Artifacts

An artifact is substantial content produced alongside the conversation: a document, a program, a
table, a chart. It belongs to the **conversation**, not to a message, so an assistant that rewrites a
document three times updates one object and the UI keeps one panel with a version history, instead of
scattering near-identical code blocks through the transcript.

```csharp
yield return new ArtifactCreatedEvent("readme", ArtifactKind.Document, "Project README")
{
    Language = "markdown",
};

foreach (var chunk in chunks) yield return new ArtifactDeltaEvent("readme", chunk);

yield return new ArtifactCompletedEvent("readme") { Summary = "First draft" };
```

Emitting `ArtifactCreatedEvent` again for the same identifier starts a revision: the previous version
is committed to history and the content is replaced.

```csharp
artifact.Versions;        // every committed version, oldest first
artifact.Restore(1);      // restores an earlier version as a new revision
```

### Kinds

`ArtifactKind` is a string-backed value type rather than an enumeration, so applications can add
their own:

| Kind | Rendered as |
| --- | --- |
| `Document` | Streaming Markdown |
| `Code` | Highlighted code |
| `Html` | A sandboxed frame with scripts allowed and same-origin denied |
| `Json` | A collapsible tree |
| `Table` | A table when the content is JSON, otherwise CSV |
| `Svg`, `Image` | Markup or an image |
| `Chart`, `Diagram` | Whatever renderer you register |

```csharp
services.ConfigureAiRenderers(renderers =>
    renderers.RegisterArtifact(new ArtifactKind("chart"), context => builder =>
    {
        builder.OpenComponent<MyChart>(0);
        builder.AddComponentParameter(1, "Spec", context.Artifact.Content);
        builder.CloseComponent();
    }));
```

### The panel

`ArtifactView` gives every artifact a title, a version selector once there is more than one, copy,
download with a sensible file name and MIME type, and a full-screen toggle. Copy and download act on
the version being viewed, not always the latest.

HTML artifacts render in a frame sandboxed to `allow-scripts allow-forms allow-popups`. Same-origin
access is deliberately withheld: that is the only way to preview generated markup without handing it
the host page.
