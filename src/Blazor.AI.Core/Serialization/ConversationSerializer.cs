using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazor.AI.Serialization;

/// <summary>
/// Hooks for persisting application-defined parts and for customising the JSON produced.
/// </summary>
public sealed class ConversationSerializerOptions
{
    /// <summary>The default options.</summary>
    public static ConversationSerializerOptions Default { get; } = new();

    /// <summary>
    /// Converts an application-defined part to a snapshot. Return <see langword="null"/> to fall
    /// back to the built-in handling, which keeps any <see cref="ITextualPart"/> text and otherwise
    /// drops the part.
    /// </summary>
    public Func<MessagePart, PartSnapshot?>? SnapshotPart { get; init; }

    /// <summary>
    /// Rebuilds an application-defined part from its snapshot. Return <see langword="null"/> to fall
    /// back to the built-in handling, which produces a <see cref="CustomJsonPart"/>.
    /// </summary>
    public Func<PartSnapshot, MessagePart?>? RestorePart { get; init; }

    /// <summary>JSON settings used by <see cref="ConversationSerializer.ToJson(Conversation, ConversationSerializerOptions?)"/> and <see cref="ConversationSerializer.FromJson"/>.</summary>
    public JsonSerializerOptions Json { get; init; } = CreateJsonOptions();

    /// <summary>Creates the JSON settings the serializer uses by default: web naming, enums as strings, nulls omitted.</summary>
    /// <returns>The settings.</returns>
    public static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false,
    };
}

/// <summary>
/// Converts between live <see cref="Conversation"/> objects and <see cref="ConversationSnapshot"/>
/// data, and between snapshots and JSON.
/// </summary>
/// <remarks>
/// <para>
/// This is the missing piece between <see cref="IConversationStore"/> and the UI: a store persists
/// snapshots (or their JSON), and <see cref="ToConversation(ConversationSnapshot, ConversationSerializerOptions?)"/>
/// turns a loaded one back into something a <see cref="ChatSession"/> can drive.
/// </para>
/// <para>
/// Attachments are persisted by reference (<see cref="Attachment.Url"/>) plus any inline
/// <see cref="Attachment.Data"/>; a <see cref="Attachment.ContentFactory"/> cannot be serialized
/// and is dropped. <see cref="AiError.Exception"/> is likewise dropped.
/// </para>
/// </remarks>
public static class ConversationSerializer
{
    /// <summary>Takes a snapshot of a conversation.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="options">Serializer hooks.</param>
    /// <returns>The snapshot.</returns>
    public static ConversationSnapshot ToSnapshot(Conversation conversation, ConversationSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        options ??= ConversationSerializerOptions.Default;

        return new ConversationSnapshot
        {
            ConversationId = conversation.ConversationId,
            Title = conversation.Title,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            Messages = conversation.Messages.Select(m => ToSnapshot(m, options)).ToArray(),
            Artifacts = conversation.Artifacts.Values.Select(ToSnapshot).ToArray(),
            Runs = conversation.Runs.Values.Select(ToSnapshot).ToArray(),
            Extensions = ToJsonDictionary(conversation.Extensions, options.Json),
        };
    }

    /// <summary>
    /// Takes a snapshot of any <see cref="IConversation"/>, including implementations from other
    /// packages. Parts that are not <see cref="MessagePart"/> instances are kept as text when they
    /// implement <see cref="ITextualPart"/>.
    /// </summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="options">Serializer hooks.</param>
    /// <returns>The snapshot.</returns>
    public static ConversationSnapshot ToSnapshot(IConversation conversation, ConversationSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation is Conversation typed) return ToSnapshot(typed, options);
        options ??= ConversationSerializerOptions.Default;

        return new ConversationSnapshot
        {
            ConversationId = conversation.ConversationId,
            Title = conversation.Title,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            Messages = conversation.Messages.Select(m => m is AiMessage typedMessage
                ? ToSnapshot(typedMessage, options)
                : new MessageSnapshot
                {
                    MessageId = m.MessageId,
                    Role = m.Role,
                    CreatedAt = m.CreatedAt,
                    Status = m.Status,
                    Metadata = m.Metadata,
                    Parts = m.Parts.Select(p => p is MessagePart part
                        ? ToSnapshot(part, options)
                        : new PartSnapshot { PartId = p.PartId, Kind = p.Kind, Text = (p as ITextualPart)?.Text }).ToArray(),
                    Attachments = m.Attachments,
                }).ToArray(),
        };
    }

    /// <summary>Rebuilds a live conversation from a snapshot.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="options">Serializer hooks.</param>
    /// <returns>The conversation.</returns>
    public static Conversation ToConversation(ConversationSnapshot snapshot, ConversationSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= ConversationSerializerOptions.Default;

        var conversation = new Conversation(snapshot.ConversationId);

        foreach (var artifactSnapshot in snapshot.Artifacts)
        {
            var artifact = new Artifact(artifactSnapshot.ArtifactId, artifactSnapshot.Kind, artifactSnapshot.Title)
            {
                Language = artifactSnapshot.Language,
                Url = artifactSnapshot.Url,
                Metadata = FromJsonDictionary(artifactSnapshot.Metadata),
            };
            artifact.RestoreState(
                artifactSnapshot.Content,
                artifactSnapshot.Version,
                artifactSnapshot.IsStreaming,
                artifactSnapshot.CreatedAt,
                artifactSnapshot.UpdatedAt,
                artifactSnapshot.Versions);
            conversation.AttachArtifact(artifact);
        }

        foreach (var runSnapshot in snapshot.Runs)
        {
            var run = new AgentRun(runSnapshot.RunId, runSnapshot.AgentName);
            run.RestoreState(
                runSnapshot.Status,
                runSnapshot.Goal,
                runSnapshot.PendingPrompt,
                runSnapshot.PendingSuggestions,
                runSnapshot.StartedAt,
                runSnapshot.CompletedAt,
                runSnapshot.Usage,
                runSnapshot.Error,
                runSnapshot.Steps.Select(s =>
                {
                    var step = new AgentStep(s.StepId, s.Title) { Detail = s.Detail };
                    step.RestoreState(s.Status, s.Summary, s.Error, s.StartedAt, s.CompletedAt, s.ToolCallIds);
                    return step;
                }));
            conversation.AttachRun(run);
        }

        foreach (var messageSnapshot in snapshot.Messages)
        {
            var message = new AiMessage(messageSnapshot.Role, messageSnapshot.MessageId)
            {
                CreatedAt = messageSnapshot.CreatedAt,
            };

            foreach (var partSnapshot in messageSnapshot.Parts)
            {
                var part = options.RestorePart?.Invoke(partSnapshot) ?? RestoreBuiltIn(partSnapshot, options);
                if (part is not null) message.AddPart(part);
            }

            foreach (var attachment in messageSnapshot.Attachments) message.AddAttachment(attachment);
            foreach (var citation in messageSnapshot.Citations) message.AddCitation(citation);
            if (messageSnapshot.Extensions is { } extensions)
            {
                foreach (var (key, value) in extensions) message.Extensions[key] = value;
            }

            // A message interrupted mid-stream is restored as cancelled: nothing will ever finish it.
            var status = messageSnapshot.Status is MessageStatus.Pending or MessageStatus.Streaming
                ? MessageStatus.Cancelled
                : messageSnapshot.Status;

            message.Restore(
                status,
                messageSnapshot.Metadata,
                messageSnapshot.Error,
                messageSnapshot.Feedback,
                messageSnapshot.FeedbackComment,
                messageSnapshot.RunId is { } runId ? conversation.FindRun(runId) : null);

            conversation.AddMessage(message);
        }

        if (snapshot.Extensions is { } conversationExtensions)
        {
            foreach (var (key, value) in conversationExtensions) conversation.Extensions[key] = value;
        }

        conversation.SetTitle(snapshot.Title);
        conversation.RestoreTimestamps(snapshot.CreatedAt, snapshot.UpdatedAt);
        return conversation;
    }

    /// <summary>
    /// Returns the conversation itself when it already is a <see cref="Conversation"/>, and otherwise
    /// rebuilds one from it. This is what to call on the result of <see cref="IConversationStore.LoadAsync"/>.
    /// </summary>
    /// <param name="conversation">The loaded conversation.</param>
    /// <param name="options">Serializer hooks.</param>
    /// <returns>A live conversation.</returns>
    public static Conversation ToConversation(IConversation conversation, ConversationSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return conversation as Conversation ?? ToConversation(ToSnapshot(conversation, options), options);
    }

    /// <summary>Serializes a conversation to JSON.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="options">Serializer hooks and JSON settings.</param>
    /// <returns>The JSON.</returns>
    public static string ToJson(Conversation conversation, ConversationSerializerOptions? options = null)
    {
        options ??= ConversationSerializerOptions.Default;
        return JsonSerializer.Serialize(ToSnapshot(conversation, options), options.Json);
    }

    /// <summary>Serializes a snapshot to JSON.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="options">JSON settings.</param>
    /// <returns>The JSON.</returns>
    public static string ToJson(ConversationSnapshot snapshot, ConversationSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, (options ?? ConversationSerializerOptions.Default).Json);
    }

    /// <summary>Deserializes a snapshot from JSON.</summary>
    /// <param name="json">The JSON produced by <see cref="ToJson(ConversationSnapshot, ConversationSerializerOptions?)"/>.</param>
    /// <param name="options">JSON settings.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="JsonException">The JSON is not a conversation snapshot.</exception>
    public static ConversationSnapshot SnapshotFromJson(string json, ConversationSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ConversationSnapshot>(json, (options ?? ConversationSerializerOptions.Default).Json)
            ?? throw new JsonException("The JSON did not contain a conversation snapshot.");
    }

    /// <summary>Deserializes JSON straight into a live conversation.</summary>
    /// <param name="json">The JSON.</param>
    /// <param name="options">Serializer hooks and JSON settings.</param>
    /// <returns>The conversation.</returns>
    public static Conversation FromJson(string json, ConversationSerializerOptions? options = null) =>
        ToConversation(SnapshotFromJson(json, options), options);

    private static MessageSnapshot ToSnapshot(AiMessage message, ConversationSerializerOptions options) => new()
    {
        MessageId = message.MessageId,
        Role = message.Role,
        CreatedAt = message.CreatedAt,
        Status = message.Status,
        Metadata = message.Metadata,
        Error = message.Error,
        Feedback = message.Feedback,
        FeedbackComment = message.FeedbackComment,
        RunId = message.Run?.RunId,
        Parts = message.Parts.Select(p => ToSnapshot(p, options)).ToArray(),
        Attachments = message.Attachments,
        Citations = message.Citations,
        Extensions = ToJsonDictionary(message.Extensions, options.Json),
    };

    private static PartSnapshot ToSnapshot(MessagePart part, ConversationSerializerOptions options)
    {
        if (options.SnapshotPart?.Invoke(part) is { } custom) return custom;

        var snapshot = new PartSnapshot { PartId = part.PartId, Kind = part.Kind };
        return part switch
        {
            ReasoningPart reasoning => snapshot with
            {
                Text = reasoning.Text,
                Signature = reasoning.Signature,
                StartedAt = reasoning.StartedAt,
                CompletedAt = reasoning.CompletedAt,
            },
            CodePart code => snapshot with { Text = code.Text, Language = code.Language, FileName = code.FileName },
            TextPart text => snapshot with { Text = text.Text },
            ImagePart image => snapshot with
            {
                Url = image.Url,
                Alt = image.Alt,
                Caption = image.Caption,
                Width = image.Width,
                Height = image.Height,
            },
            FilePart file => snapshot with { Attachment = file.Attachment },
            CitationPart citation => snapshot with { Citation = citation.Citation },
            SourcesPart sources => snapshot with { CitationIds = sources.CitationIds },
            ToolCallPart call => snapshot with
            {
                ToolCallId = call.ToolCallId,
                ToolName = call.ToolName,
                Definition = call.Definition,
                Arguments = call.Arguments,
                RawArguments = call.Arguments is null ? call.RawArguments : null,
                ToolStatus = call.Status,
                Approval = call.Approval,
                ApprovalReason = call.ApprovalReason,
                ToolStartedAt = call.StartedAt,
                CompletedAt = call.CompletedAt,
                Outcome = call.Result is { } result ? ToolOutcomeSnapshot.From(result) : null,
            },
            ToolResultPart toolResult => snapshot with
            {
                ToolCallId = toolResult.ToolCallId,
                ToolName = toolResult.ToolName,
                Outcome = ToolOutcomeSnapshot.From(toolResult.Outcome),
            },
            ArtifactPart artifact => snapshot with { ArtifactId = artifact.ArtifactId, ArtifactVersion = artifact.ArtifactVersion },
            StructuredDataPart structured => snapshot with { SchemaName = structured.SchemaName, Data = structured.Value, Text = structured.Value is null ? structured.RawJson : null },
            GenerativeUiPart generative => snapshot with { ComponentName = generative.ComponentName, Data = generative.Props },
            ErrorPart error => snapshot with { Error = error.Error },
            CustomJsonPart customJson => snapshot with { Data = customJson.Data, Text = customJson.Text },
            ITextualPart textual => snapshot with { Text = textual.Text },
            _ => snapshot,
        };
    }

    private static MessagePart? RestoreBuiltIn(PartSnapshot snapshot, ConversationSerializerOptions options)
    {
        switch (snapshot.Kind)
        {
            case MessagePartKinds.Text:
                return new TextPart(snapshot.Text ?? string.Empty, snapshot.PartId);

            case MessagePartKinds.Markdown:
                return new MarkdownPart(snapshot.Text ?? string.Empty, snapshot.PartId);

            case MessagePartKinds.Reasoning:
            {
                var part = new ReasoningPart(snapshot.Text ?? string.Empty, snapshot.PartId) { Signature = snapshot.Signature };
                part.RestoreTimings(snapshot.StartedAt ?? part.StartedAt, snapshot.CompletedAt);
                return part;
            }

            case MessagePartKinds.Code:
                return new CodePart(snapshot.Text ?? string.Empty, snapshot.Language, snapshot.PartId) { FileName = snapshot.FileName };

            case MessagePartKinds.Image when snapshot.Url is { Length: > 0 } url:
                return new ImagePart(url, snapshot.PartId)
                {
                    Alt = snapshot.Alt,
                    Caption = snapshot.Caption,
                    Width = snapshot.Width,
                    Height = snapshot.Height,
                };

            case MessagePartKinds.File when snapshot.Attachment is { } attachment:
                return new FilePart(attachment, snapshot.PartId);

            case MessagePartKinds.Citation when snapshot.Citation is { } citation:
                return new CitationPart(citation, snapshot.PartId);

            case MessagePartKinds.Sources:
                return new SourcesPart(snapshot.PartId) { CitationIds = snapshot.CitationIds };

            case MessagePartKinds.ToolCall when snapshot.ToolCallId is { } callId && snapshot.ToolName is { } toolName:
            {
                var part = new ToolCallPart(callId, toolName, snapshot.PartId)
                {
                    Definition = snapshot.Definition,
                    ApprovalReason = snapshot.ApprovalReason,
                };

                // A call that never finished cannot finish now: it is restored as cancelled.
                var status = snapshot.ToolStatus ?? ToolCallStatus.Pending;
                if (status is ToolCallStatus.Streaming or ToolCallStatus.Pending or ToolCallStatus.AwaitingApproval or ToolCallStatus.Running)
                {
                    status = ToolCallStatus.Cancelled;
                }

                part.RestoreState(
                    snapshot.Arguments,
                    snapshot.RawArguments,
                    status,
                    snapshot.Approval ?? ApprovalDecision.Undecided,
                    snapshot.Outcome?.ToOutcome(),
                    snapshot.ToolStartedAt,
                    snapshot.CompletedAt);
                return part;
            }

            case MessagePartKinds.ToolResult when snapshot.ToolCallId is { } callId && snapshot.Outcome is { } outcome:
                return new ToolResultPart(callId, outcome.ToOutcome(), snapshot.PartId) { ToolName = snapshot.ToolName };

            case MessagePartKinds.Artifact when snapshot.ArtifactId is { } artifactId:
                return new ArtifactPart(artifactId, snapshot.PartId) { ArtifactVersion = snapshot.ArtifactVersion };

            case MessagePartKinds.StructuredData:
            {
                if (snapshot.Data is { } value) return new StructuredDataPart(value, snapshot.SchemaName, snapshot.PartId);
                var part = new StructuredDataPart(snapshot.SchemaName, snapshot.PartId);
                part.Append(snapshot.Text);
                part.Complete();
                return part;
            }

            case MessagePartKinds.GenerativeUi when snapshot.ComponentName is { } component:
            {
                var part = new GenerativeUiPart(component, snapshot.PartId);
                if (snapshot.Data is { } props) part.SetProps(props);
                part.Complete();
                return part;
            }

            case MessagePartKinds.Error when snapshot.Error is { } error:
                return new ErrorPart(error, snapshot.PartId);

            default:
            {
                // Unknown or application-defined kind: keep the payload so nothing is lost on load.
                var data = snapshot.Data ?? JsonSerializer.SerializeToElement(snapshot, options.Json);
                return new CustomJsonPart(snapshot.Kind, data, snapshot.PartId) { Text = snapshot.Text };
            }
        }
    }

    private static ArtifactSnapshot ToSnapshot(Artifact artifact) => new()
    {
        ArtifactId = artifact.ArtifactId,
        Kind = artifact.Kind,
        Title = artifact.Title,
        Content = artifact.Content,
        Language = artifact.Language,
        Url = artifact.Url,
        Version = artifact.Version,
        IsStreaming = artifact.IsStreaming,
        CreatedAt = artifact.CreatedAt,
        UpdatedAt = artifact.UpdatedAt,
        Versions = artifact.Versions.ToArray(),
        Metadata = ToJsonDictionary(artifact.Metadata, ConversationSerializerOptions.Default.Json),
    };

    private static AgentRunSnapshot ToSnapshot(AgentRun run) => new()
    {
        RunId = run.RunId,
        AgentName = run.AgentName,
        Goal = run.Goal,
        Status = run.Status,
        PendingPrompt = run.PendingPrompt,
        PendingSuggestions = run.PendingSuggestions,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        Usage = run.Usage,
        Error = run.Error,
        Steps = run.Steps.Select(s => new AgentStepSnapshot
        {
            StepId = s.StepId,
            Title = s.Title,
            Detail = s.Detail,
            Status = s.Status,
            Summary = s.Summary,
            Error = s.Error,
            StartedAt = s.StartedAt,
            CompletedAt = s.CompletedAt,
            ToolCallIds = s.ToolCallIds.ToArray(),
        }).ToArray(),
    };

    private static IReadOnlyDictionary<string, JsonElement>? ToJsonDictionary(
        IReadOnlyDictionary<string, object?>? values,
        JsonSerializerOptions json)
    {
        if (values is null || values.Count == 0) return null;
        var result = new Dictionary<string, JsonElement>(values.Count, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            try
            {
                result[key] = value is JsonElement element ? element : JsonSerializer.SerializeToElement(value, json);
            }
            catch (NotSupportedException)
            {
                // A value that cannot be serialized (a delegate, a live object) is skipped rather
                // than failing the whole save.
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, object?>? FromJsonDictionary(IReadOnlyDictionary<string, JsonElement>? values)
    {
        if (values is null) return null;
        var result = new Dictionary<string, object?>(values.Count, StringComparer.Ordinal);
        foreach (var (key, value) in values) result[key] = value;
        return result;
    }
}
