using Microsoft.AspNetCore.Components;

namespace Blazor.AI.Components;

/// <summary>
/// What a custom part renderer is given.
/// </summary>
/// <param name="Part">The part to render.</param>
/// <param name="Message">The message it belongs to.</param>
/// <param name="Conversation">The conversation, when the part is being rendered inside one.</param>
/// <param name="Session">The session driving the conversation, when there is one.</param>
/// <remarks>
/// The session is included so a renderer can act as well as display: a custom approval card can
/// call back into the session without the application having to thread state through cascading
/// values.
/// </remarks>
public sealed record MessagePartContext(
    MessagePart Part,
    AiMessage Message,
    Conversation? Conversation = null,
    ChatSession? Session = null);

/// <summary>
/// What a custom tool renderer is given.
/// </summary>
/// <param name="Call">The tool call, including its arguments, status, and result.</param>
/// <param name="Message">The message it belongs to.</param>
/// <param name="Session">The session driving the conversation, when there is one.</param>
public sealed record ToolCallContext(ToolCallPart Call, AiMessage Message, ChatSession? Session = null);

/// <summary>
/// What a custom artifact renderer is given.
/// </summary>
/// <param name="Artifact">The artifact to render.</param>
/// <param name="IsExpanded">Whether the artifact is being shown full size rather than as a card.</param>
public sealed record ArtifactContext(Artifact Artifact, bool IsExpanded = false);

/// <summary>
/// What a custom structured-data renderer is given.
/// </summary>
/// <param name="Part">The structured part, which may still be streaming.</param>
/// <param name="Message">The message it belongs to.</param>
public sealed record StructuredDataContext(StructuredDataPart Part, AiMessage Message);

/// <summary>
/// What a custom citation renderer is given.
/// </summary>
/// <param name="Citation">The citation.</param>
/// <param name="Message">The message it belongs to.</param>
public sealed record CitationContext(Citation Citation, AiMessage? Message = null);

/// <summary>Renders a message part.</summary>
/// <param name="context">The part and its surroundings.</param>
/// <returns>The markup.</returns>
public delegate RenderFragment MessagePartRenderer(MessagePartContext context);

/// <summary>Renders a tool call.</summary>
/// <param name="context">The call and its surroundings.</param>
/// <returns>The markup.</returns>
public delegate RenderFragment ToolCallRenderer(ToolCallContext context);

/// <summary>Renders an artifact.</summary>
/// <param name="context">The artifact and how it is being shown.</param>
/// <returns>The markup.</returns>
public delegate RenderFragment ArtifactRenderer(ArtifactContext context);

/// <summary>Renders a structured value.</summary>
/// <param name="context">The part and its surroundings.</param>
/// <returns>The markup.</returns>
public delegate RenderFragment StructuredDataRenderer(StructuredDataContext context);

/// <summary>Renders a citation.</summary>
/// <param name="context">The citation and its surroundings.</param>
/// <returns>The markup.</returns>
public delegate RenderFragment CitationRenderer(CitationContext context);
