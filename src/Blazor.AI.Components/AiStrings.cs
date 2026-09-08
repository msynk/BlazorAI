namespace Blazor.AI.Components;

/// <summary>
/// Every piece of user-facing text the components render.
/// </summary>
/// <remarks>
/// Kept in one replaceable object rather than scattered through markup, so that localising the
/// library, or simply changing its tone, does not mean forking components. Register a subclass and
/// every component picks it up.
/// </remarks>
public class AiStrings
{
    /// <summary>The default English strings.</summary>
    public static AiStrings Default { get; } = new();

    /// <summary>Placeholder in the prompt composer.</summary>
    public virtual string PromptPlaceholder => "Send a message...";

    /// <summary>Accessible label for the send button.</summary>
    public virtual string Send => "Send";

    /// <summary>Label for the button that stops generation.</summary>
    public virtual string Stop => "Stop";

    /// <summary>Label for regenerating a response.</summary>
    public virtual string Regenerate => "Regenerate";

    /// <summary>Label for retrying after a failure.</summary>
    public virtual string Retry => "Try again";

    /// <summary>Label for copying content.</summary>
    public virtual string Copy => "Copy";

    /// <summary>Confirmation shown after copying.</summary>
    public virtual string Copied => "Copied";

    /// <summary>Label for editing a message.</summary>
    public virtual string Edit => "Edit";

    /// <summary>Label for saving an edit.</summary>
    public virtual string Save => "Save";

    /// <summary>Label for cancelling an action.</summary>
    public virtual string Cancel => "Cancel";

    /// <summary>Label for positive feedback.</summary>
    public virtual string GoodResponse => "Good response";

    /// <summary>Label for negative feedback.</summary>
    public virtual string BadResponse => "Bad response";

    /// <summary>Label on the button that scrolls to the newest message.</summary>
    public virtual string ScrollToLatest => "Scroll to latest";

    /// <summary>Shown while the model is working but has produced nothing yet.</summary>
    public virtual string Thinking => "Thinking";

    /// <summary>Title of the collapsed reasoning panel.</summary>
    public virtual string Reasoning => "Reasoning";

    /// <summary>Title of the reasoning panel once finished.</summary>
    /// <param name="duration">How long the model reasoned for.</param>
    /// <returns>The title.</returns>
    public virtual string ReasonedFor(TimeSpan duration) => $"Thought for {FormatDuration(duration)}";

    /// <summary>Heading above the list of sources.</summary>
    public virtual string Sources => "Sources";

    /// <summary>Heading above an attachment list.</summary>
    public virtual string Attachments => "Attachments";

    /// <summary>Label for the attach-file button.</summary>
    public virtual string AttachFile => "Attach files";

    /// <summary>Label for removing an attachment.</summary>
    public virtual string Remove => "Remove";

    /// <summary>Prompt shown over the composer while a file is dragged over it.</summary>
    public virtual string DropFiles => "Drop files to attach";

    /// <summary>Label for the voice input button.</summary>
    public virtual string StartDictation => "Dictate";

    /// <summary>Label shown while dictation is active.</summary>
    public virtual string StopDictation => "Stop dictation";

    /// <summary>Label for the model picker.</summary>
    public virtual string Model => "Model";

    /// <summary>Heading of a tool call card before the tool has run.</summary>
    /// <param name="tool">The tool name.</param>
    /// <returns>The heading.</returns>
    public virtual string CallingTool(string tool) => $"Calling {tool}";

    /// <summary>Heading of a tool call card once the tool has run.</summary>
    /// <param name="tool">The tool name.</param>
    /// <returns>The heading.</returns>
    public virtual string CalledTool(string tool) => $"Used {tool}";

    /// <summary>Heading of a tool call card that failed.</summary>
    /// <param name="tool">The tool name.</param>
    /// <returns>The heading.</returns>
    public virtual string ToolFailed(string tool) => $"{tool} failed";

    /// <summary>Question asked when a tool call needs approval.</summary>
    /// <param name="tool">The tool name.</param>
    /// <returns>The question.</returns>
    public virtual string ApproveTool(string tool) => $"Allow {tool} to run?";

    /// <summary>Label for approving a tool call once.</summary>
    public virtual string Approve => "Allow";

    /// <summary>Label for approving a tool and not being asked again.</summary>
    public virtual string ApproveAlways => "Always allow";

    /// <summary>Label for rejecting a tool call.</summary>
    public virtual string Reject => "Decline";

    /// <summary>Label for the arguments section of a tool call.</summary>
    public virtual string Arguments => "Arguments";

    /// <summary>Label for the result section of a tool call.</summary>
    public virtual string Result => "Result";

    /// <summary>Heading of an agent run panel.</summary>
    public virtual string Plan => "Plan";

    /// <summary>Shown when an agent is waiting for the user.</summary>
    public virtual string WaitingForYou => "Waiting for your answer";

    /// <summary>Heading of the artifact panel.</summary>
    public virtual string Artifact => "Artifact";

    /// <summary>Label for downloading an artifact.</summary>
    public virtual string Download => "Download";

    /// <summary>Label for opening an artifact full screen.</summary>
    public virtual string Expand => "Expand";

    /// <summary>Label for leaving full screen.</summary>
    public virtual string Collapse => "Collapse";

    /// <summary>Label for the artifact version selector.</summary>
    public virtual string Version => "Version";

    /// <summary>Heading shown above an empty conversation.</summary>
    public virtual string EmptyTitle => "How can I help?";

    /// <summary>Body shown above an empty conversation.</summary>
    public virtual string EmptyBody => "Ask a question to get started.";

    /// <summary>Label for starting a new conversation.</summary>
    public virtual string NewConversation => "New conversation";

    /// <summary>Label for deleting a conversation.</summary>
    public virtual string Delete => "Delete";

    /// <summary>Fallback title for a conversation with no title yet.</summary>
    public virtual string Untitled => "Untitled";

    /// <summary>Heading of an error panel.</summary>
    public virtual string SomethingWentWrong => "Something went wrong";

    /// <summary>Label for showing error detail.</summary>
    public virtual string ShowDetails => "Show details";

    /// <summary>Label for the token usage readout.</summary>
    public virtual string Tokens => "tokens";

    /// <summary>Label for the context usage meter.</summary>
    public virtual string Context => "Context";

    /// <summary>Formats a duration for display.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>A short human-readable string.</returns>
    public virtual string FormatDuration(TimeSpan duration) => duration.TotalSeconds switch
    {
        < 1 => $"{duration.TotalMilliseconds:F0}ms",
        < 60 => $"{duration.TotalSeconds:F1}s",
        _ => $"{(int)duration.TotalMinutes}m {duration.Seconds}s",
    };

    /// <summary>Formats a byte count for display.</summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>A short human-readable string.</returns>
    public virtual string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
    };

    /// <summary>Formats a token count for display.</summary>
    /// <param name="tokens">The token count.</param>
    /// <returns>A short human-readable string.</returns>
    public virtual string FormatTokens(long tokens) => tokens switch
    {
        < 1000 => tokens.ToString("N0"),
        < 1_000_000 => $"{tokens / 1000.0:F1}k",
        _ => $"{tokens / 1_000_000.0:F1}M",
    };
}
