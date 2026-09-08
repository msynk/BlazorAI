namespace Blazor.AI;

/// <summary>
/// Applies a stream of <see cref="AiEvent"/> to a <see cref="Conversation"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the reducer at the centre of the framework. Transports produce events, this class turns
/// them into message parts, tool calls, artifacts, and agent steps, and components observe the
/// resulting state. Nothing else in the framework needs to know the event vocabulary.
/// </para>
/// <para>
/// It is deliberately usable on its own. Feeding recorded events through a processor is the
/// simplest way to test rendering, and the demo replays fixtures this way.
/// </para>
/// </remarks>
public sealed class ConversationStreamProcessor
{
    private readonly Conversation _conversation;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _firstTokenAt;

    /// <summary>Creates a processor targeting a conversation.</summary>
    /// <param name="conversation">The conversation to mutate.</param>
    public ConversationStreamProcessor(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        _conversation = conversation;
    }

    /// <summary>The message events are currently being applied to.</summary>
    public AiMessage? Current { get; private set; }

    /// <summary>The agent run events are currently being applied to.</summary>
    public AgentRun? CurrentRun { get; private set; }

    /// <summary>
    /// Raised when a tool call has complete arguments and is ready to execute or request approval.
    /// </summary>
    /// <remarks>
    /// The processor never executes tools itself: it only records what the model asked for. The
    /// chat session subscribes to this to run them, which keeps the reducer free of side effects.
    /// </remarks>
    public event Action<ToolCallPart>? ToolCallReady;

    /// <summary>Raised for every event, after it has been applied. Useful for logging and tracing.</summary>
    public event Action<AiEvent>? EventApplied;

    /// <summary>
    /// Begins a generation, creating the assistant message the stream will fill in.
    /// </summary>
    /// <param name="message">
    /// An existing message to stream into, for regeneration. When <see langword="null"/> a new
    /// assistant message is appended.
    /// </param>
    /// <returns>The message being generated.</returns>
    public AiMessage BeginGeneration(AiMessage? message = null)
    {
        _startedAt = DateTimeOffset.UtcNow;
        _firstTokenAt = null;

        Current = message ?? _conversation.AddMessage(new AiMessage(AiRole.Assistant));
        Current.SetStatus(MessageStatus.Pending);
        Current.MergeMetadata(new GenerationMetadata { StartedAt = _startedAt });
        return Current;
    }

    /// <summary>
    /// Applies one event.
    /// </summary>
    /// <param name="evt">The event to apply. Unknown event types are ignored.</param>
    public void Apply(AiEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        switch (evt)
        {
            case MessageStartedEvent started: OnMessageStarted(started); break;
            case TextDeltaEvent text: OnTextDelta(text); break;
            case ReasoningDeltaEvent reasoning: OnReasoningDelta(reasoning); break;
            case PartStartedEvent part: OnPartStarted(part); break;
            case PartCompletedEvent part: OnPartCompleted(part); break;
            case StatusEvent status: Target(evt)?.SetStatusText(status.Status); break;
            case ImageEvent image: OnImage(image); break;
            case FileEvent file: Target(evt)?.AddPart(new FilePart(file.Attachment, file.PartId)); break;
            case CitationEvent citation: Target(evt)?.AddCitation(citation.Citation); break;
            case ToolCallStartedEvent tool: OnToolCallStarted(tool); break;
            case ToolCallDeltaEvent tool: FindToolCall(tool.ToolCallId)?.AppendArguments(tool.ArgumentsDelta); break;
            case ToolCallCompletedEvent tool: OnToolCallCompleted(tool); break;
            case ToolProgressEvent tool: FindToolCall(tool.ToolCallId)?.SetProgress(tool.Progress); break;
            case ToolResultEvent tool: OnToolResult(tool); break;
            case ToolApprovalRequestedEvent tool: OnApprovalRequested(tool); break;
            case ArtifactCreatedEvent artifact: OnArtifactCreated(artifact); break;
            case ArtifactDeltaEvent artifact: _conversation.FindArtifact(artifact.ArtifactId)?.Append(artifact.Delta); break;
            case ArtifactCompletedEvent artifact: _conversation.FindArtifact(artifact.ArtifactId)?.Complete(artifact.Summary); break;
            case StructuredDataDeltaEvent structured: OnStructuredDelta(structured); break;
            case StructuredDataEvent structured: OnStructured(structured); break;
            case GenerativeUiEvent generative: OnGenerativeUi(generative); break;
            case GenerativeUiDeltaEvent generative: OnGenerativeUiDelta(generative); break;
            case UsageUpdatedEvent usage: OnUsage(usage); break;
            case MetadataEvent metadata: Target(evt)?.MergeMetadata(metadata.Metadata); break;
            case AgentRunStartedEvent run: OnRunStarted(run); break;
            case AgentPlanEvent plan: _conversation.GetOrAddRun(plan.RunId).SetPlan(plan.Steps); break;
            case AgentStepStartedEvent step: OnStepStarted(step); break;
            case AgentStepCompletedEvent step: OnStepCompleted(step); break;
            case AgentRunCompletedEvent run: _conversation.GetOrAddRun(run.RunId).SetStatus(run.Status); break;
            case AgentInputRequestedEvent input: OnInputRequested(input); break;
            case ErrorEvent error: OnError(error); break;
            case MessageCompletedEvent completed: OnMessageCompleted(completed); break;
        }

        EventApplied?.Invoke(evt);
    }

    /// <summary>
    /// Ends the generation, closing any open parts and stamping the final timings.
    /// </summary>
    /// <param name="status">The terminal status. Defaults to complete.</param>
    public void EndGeneration(MessageStatus status = MessageStatus.Complete)
    {
        if (Current is not { } message) return;

        message.CompleteOpenParts();
        if (message.Status is MessageStatus.Pending or MessageStatus.Streaming) message.SetStatus(status);
        message.MergeMetadata(new GenerationMetadata
        {
            CompletedAt = DateTimeOffset.UtcNow,
            TimeToFirstToken = _firstTokenAt is { } first && _startedAt is { } start ? first - start : null,
        });

        Current = null;
        CurrentRun = null;
    }

    private AiMessage? Target(AiEvent evt)
    {
        if (evt.MessageId is { } id)
        {
            var found = _conversation.FindMessage(id);
            if (found is not null) return found;
        }

        return Current ??= _conversation.AddMessage(new AiMessage(AiRole.Assistant, evt.MessageId));
    }

    private AiMessage? EnsureStreaming(AiEvent evt)
    {
        var message = Target(evt);
        if (message is null) return null;
        if (message.Status is MessageStatus.Pending or MessageStatus.Draft) message.SetStatus(MessageStatus.Streaming);
        _firstTokenAt ??= DateTimeOffset.UtcNow;
        return message;
    }

    private ToolCallPart? FindToolCall(string toolCallId)
    {
        if (Current?.FindToolCall(toolCallId) is { } fromCurrent) return fromCurrent;
        for (var i = _conversation.Messages.Count - 1; i >= 0; i--)
        {
            if (_conversation.Messages[i].FindToolCall(toolCallId) is { } found) return found;
        }

        return null;
    }

    private void OnMessageStarted(MessageStartedEvent evt)
    {
        if (Current is null || (evt.MessageId is not null && Current.MessageId != evt.MessageId))
        {
            Current = evt.MessageId is not null
                ? _conversation.FindMessage(evt.MessageId) ?? _conversation.AddMessage(new AiMessage(evt.Role, evt.MessageId))
                : _conversation.AddMessage(new AiMessage(evt.Role));
        }

        Current.SetStatus(MessageStatus.Streaming);
    }

    private void OnTextDelta(TextDeltaEvent evt)
    {
        if (EnsureStreaming(evt) is not { } message) return;
        var part = evt.PartId is { } id
            ? message.FindPart<TextPart>(id) ?? message.AddPart(new TextPart(null, id))
            : message.GetOrCreateOpenPart(MessagePartKinds.Text, () => new TextPart());
        part.Append(evt.Delta);
    }

    private void OnReasoningDelta(ReasoningDeltaEvent evt)
    {
        if (EnsureStreaming(evt) is not { } message) return;
        var part = evt.PartId is { } id
            ? message.FindPart<ReasoningPart>(id) ?? message.AddPart(new ReasoningPart(null, id))
            : message.GetOrCreateOpenPart(MessagePartKinds.Reasoning, () => new ReasoningPart());
        if (evt.Signature is not null) part.Signature = evt.Signature;
        part.Append(evt.Delta);
    }

    private void OnPartStarted(PartStartedEvent evt)
    {
        if (EnsureStreaming(evt) is not { } message) return;
        if (evt.PartId is { } existing && message.FindPart(existing) is not null) return;

        MessagePart part = evt.Kind switch
        {
            MessagePartKinds.Reasoning => new ReasoningPart(null, evt.PartId),
            MessagePartKinds.Markdown => new MarkdownPart(null, evt.PartId),
            MessagePartKinds.Code => new CodePart(null, Attribute(evt, "language"), evt.PartId),
            MessagePartKinds.StructuredData => new StructuredDataPart(Attribute(evt, "schema"), evt.PartId),
            MessagePartKinds.Sources => new SourcesPart(evt.PartId),
            _ => new TextPart(null, evt.PartId),
        };

        if (part is CodePart code) code.FileName = Attribute(evt, "fileName");
        message.AddPart(part);
    }

    private static string? Attribute(PartStartedEvent evt, string name) =>
        evt.Attributes is not null && evt.Attributes.TryGetValue(name, out var value) ? value?.ToString() : null;

    private void OnPartCompleted(PartCompletedEvent evt)
    {
        if (Target(evt) is not { } message) return;
        var part = evt.PartId is { } id ? message.FindPart(id) : message.Parts.LastOrDefault();
        switch (part)
        {
            case ReasoningPart reasoning: reasoning.Complete(); break;
            case TextPart text: text.Complete(); break;
            case StructuredDataPart structured: structured.Complete(); break;
            case GenerativeUiPart generative: generative.Complete(); break;
        }
    }

    private void OnImage(ImageEvent evt)
    {
        if (EnsureStreaming(evt) is not { } message) return;
        message.AddPart(new ImagePart(evt.Url, evt.PartId)
        {
            Alt = evt.Alt,
            Width = evt.Width,
            Height = evt.Height,
        });
    }

    private void OnToolCallStarted(ToolCallStartedEvent evt)
    {
        if (EnsureStreaming(evt) is not { } message) return;
        if (FindToolCall(evt.ToolCallId) is not null) return;

        var part = message.AddPart(new ToolCallPart(evt.ToolCallId, evt.ToolName));
        if (evt.Arguments is { } arguments)
        {
            part.CompleteArguments(arguments);
            ToolCallReady?.Invoke(part);
        }
    }

    private void OnToolCallCompleted(ToolCallCompletedEvent evt)
    {
        if (FindToolCall(evt.ToolCallId) is not { } part) return;
        part.CompleteArguments(evt.Arguments);
        ToolCallReady?.Invoke(part);
    }

    private void OnToolResult(ToolResultEvent evt)
    {
        if (FindToolCall(evt.ToolCallId) is { } part)
        {
            part.SetResult(evt.Outcome);
        }
        else if (Target(evt) is { } message)
        {
            message.AddPart(new ToolResultPart(evt.ToolCallId, evt.Outcome));
        }

        if (evt.Outcome.Citations is { Count: > 0 } citations && Target(evt) is { } target)
        {
            foreach (var citation in citations) target.AddCitation(citation);
        }
    }

    private void OnApprovalRequested(ToolApprovalRequestedEvent evt)
    {
        if (FindToolCall(evt.ToolCallId) is not { } part) return;
        part.ApprovalReason = evt.Reason;
        part.SetStatus(ToolCallStatus.AwaitingApproval);
    }

    private void OnArtifactCreated(ArtifactCreatedEvent evt)
    {
        var artifact = _conversation.GetOrAddArtifact(evt.ArtifactId, evt.Kind, evt.Title);
        artifact.Language = evt.Language ?? artifact.Language;
        artifact.Url = evt.Url ?? artifact.Url;
        artifact.Metadata = evt.Metadata ?? artifact.Metadata;

        if (!artifact.IsStreaming) artifact.BeginRevision();
        if (evt.Content is not null)
        {
            artifact.SetContent(evt.Content);
            artifact.Complete();
        }

        if (Target(evt) is { } message && !message.PartsOf<ArtifactPart>().Any(p => p.ArtifactId == evt.ArtifactId))
        {
            message.AddPart(new ArtifactPart(evt.ArtifactId) { ArtifactVersion = artifact.Version });
        }
    }

    private void OnStructuredDelta(StructuredDataDeltaEvent evt)
    {
        if (EnsureStreaming(evt) is not { } message) return;
        var part = evt.PartId is { } id
            ? message.FindPart<StructuredDataPart>(id) ?? message.AddPart(new StructuredDataPart(evt.SchemaName, id))
            : message.GetOrCreateOpenPart(MessagePartKinds.StructuredData, () => new StructuredDataPart(evt.SchemaName));
        part.SchemaName ??= evt.SchemaName;
        part.Append(evt.JsonDelta);
    }

    private void OnStructured(StructuredDataEvent evt)
    {
        if (EnsureStreaming(evt) is not { } message) return;
        message.AddPart(new StructuredDataPart(evt.Value, evt.SchemaName, evt.PartId));
    }

    private void OnGenerativeUi(GenerativeUiEvent evt)
    {
        if (EnsureStreaming(evt) is not { } message) return;
        var part = evt.PartId is { } id
            ? message.FindPart<GenerativeUiPart>(id) ?? message.AddPart(new GenerativeUiPart(evt.ComponentName, id))
            : message.AddPart(new GenerativeUiPart(evt.ComponentName));
        part.SetProps(evt.Props);
        if (!evt.IsPartial) part.Complete();
    }

    private void OnGenerativeUiDelta(GenerativeUiDeltaEvent evt)
    {
        if (EnsureStreaming(evt) is not { } message) return;
        var part = evt.PartId is { } id
            ? message.FindPart<GenerativeUiPart>(id) ?? message.AddPart(new GenerativeUiPart(evt.ComponentName, id))
            : message.GetOrCreateOpenPart(MessagePartKinds.GenerativeUi, () => new GenerativeUiPart(evt.ComponentName));
        part.AppendProps(evt.PropsDelta);
    }

    private void OnUsage(UsageUpdatedEvent evt)
    {
        Target(evt)?.MergeMetadata(new GenerationMetadata { Usage = evt.Usage });
        CurrentRun?.AddUsage(evt.Usage);
    }

    private void OnRunStarted(AgentRunStartedEvent evt)
    {
        var run = _conversation.GetOrAddRun(evt.RunId);
        run.AgentName = evt.AgentName ?? run.AgentName;
        run.Goal = evt.Goal ?? run.Goal;
        run.SetStatus(AgentRunStatus.Running);
        CurrentRun = run;
        if (Target(evt) is { } message) message.Run = run;
    }

    private void OnStepStarted(AgentStepStartedEvent evt)
    {
        var run = _conversation.GetOrAddRun(evt.RunId);
        run.SetStatus(AgentRunStatus.Running);
        run.GetOrAddStep(evt.StepId, evt.Title).Start();
        CurrentRun = run;
    }

    private void OnStepCompleted(AgentStepCompletedEvent evt)
    {
        var run = _conversation.GetOrAddRun(evt.RunId);
        run.GetOrAddStep(evt.StepId).Complete(evt.Status, evt.Summary, evt.Error);
    }

    private void OnInputRequested(AgentInputRequestedEvent evt)
    {
        var run = _conversation.GetOrAddRun(evt.RunId);
        run.RequestInput(evt.Prompt, evt.Suggestions);
        CurrentRun = run;
    }

    private void OnError(ErrorEvent evt)
    {
        var message = Target(evt);
        if (message is null) return;

        if (evt.IsTerminal)
        {
            message.CompleteOpenParts();
            message.SetError(evt.Error);
            CurrentRun?.SetError(evt.Error);
        }
        else
        {
            message.AddPart(new ErrorPart(evt.Error));
        }
    }

    private void OnMessageCompleted(MessageCompletedEvent evt)
    {
        if (Target(evt) is not { } message) return;
        message.CompleteOpenParts();
        message.MergeMetadata(new GenerationMetadata { FinishReason = evt.FinishReason });
        if (message.Status is MessageStatus.Pending or MessageStatus.Streaming)
        {
            message.SetStatus(MessageStatus.Complete);
        }
    }
}
