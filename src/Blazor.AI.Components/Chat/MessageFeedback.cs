namespace Blazor.AI.Components;

/// <summary>
/// A feedback decision the user made on a message, raised so the host can persist it.
/// </summary>
/// <param name="Message">The message the feedback is about. Its <see cref="AiMessage.Feedback"/> is already updated.</param>
/// <param name="Feedback">The new sentiment, <see cref="FeedbackKind.None"/> when the user withdrew it.</param>
public sealed record MessageFeedback(AiMessage Message, FeedbackKind Feedback);
