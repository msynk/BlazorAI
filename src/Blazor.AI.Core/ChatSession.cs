using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazor.AI;

/// <summary>
/// Drives one conversation: submission, streaming, tool execution, approval, cancellation, retry,
/// and regeneration.
/// </summary>
/// <remarks>
/// <para>
/// This is the type most applications hold. Components bind to its <see cref="Conversation"/> and
/// call <see cref="SubmitAsync"/>; everything between a user pressing enter and the answer
/// appearing is handled here.
/// </para>
/// <para>
/// The session owns the tool loop. When the model asks for a tool the session resolves it from the
/// registry, checks the approval policy, waits for a human decision when one is required, executes
/// it, appends the result, and asks the model again, up to
/// <see cref="ChatSessionOptions.MaxToolRoundtrips"/>. A transport therefore never has to implement
/// agentic behaviour to get it.
/// </para>
/// <para>
/// One session drives one conversation at a time. Submitting while a turn is in flight is a no-op
/// rather than an error, because double-submit is a UI race, not a programming mistake.
/// </para>
/// </remarks>
public sealed class ChatSession : IAsyncDisposable
{
    private readonly IAiTransport _transport;
    private readonly IToolRegistry? _tools;
    private readonly IToolApprovalPolicy _approvalPolicy;
    private readonly IServiceProvider? _services;
    private readonly ILogger _logger;
    private readonly Dictionary<string, TaskCompletionSource<ApprovalDecision>> _approvals = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;

    /// <summary>Creates a session.</summary>
    /// <param name="transport">Produces responses.</param>
    /// <param name="conversation">The conversation to drive, or <see langword="null"/> for a new one.</param>
    /// <param name="options">Session settings.</param>
    /// <param name="tools">The tools the model may call.</param>
    /// <param name="approvalPolicy">Decides which calls need a human decision.</param>
    /// <param name="services">Scoped services made available to tools.</param>
    /// <param name="logger">Where to log transport and tool failures.</param>
    public ChatSession(
        IAiTransport transport,
        Conversation? conversation = null,
        ChatSessionOptions? options = null,
        IToolRegistry? tools = null,
        IToolApprovalPolicy? approvalPolicy = null,
        IServiceProvider? services = null,
        ILogger<ChatSession>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _tools = tools;
        _approvalPolicy = approvalPolicy ?? new DefaultToolApprovalPolicy();
        _services = services;
        _logger = logger ?? NullLogger<ChatSession>.Instance;

        Conversation = conversation ?? new Conversation();
        Options = options ?? new ChatSessionOptions();
        Processor = new ConversationStreamProcessor(Conversation);
    }

    /// <summary>The conversation this session drives.</summary>
    public Conversation Conversation { get; }

    /// <summary>The settings in force.</summary>
    public ChatSessionOptions Options { get; set; }

    /// <summary>The reducer applying events to the conversation.</summary>
    public ConversationStreamProcessor Processor { get; }

    /// <summary><see langword="true"/> while a turn is in flight.</summary>
    public bool IsGenerating { get; private set; }

    /// <summary>The failure from the last turn, cleared when a new turn starts.</summary>
    public AiError? LastError { get; private set; }

    /// <summary>Raised whenever anything the UI cares about changes.</summary>
    /// <remarks>
    /// Coarse by design. Components should route it through a <see cref="RenderScheduler"/> so a
    /// fast stream cannot render more often than the frame rate.
    /// </remarks>
    public event Action? StateChanged;

    /// <summary>Raised when a turn ends, successfully or not.</summary>
    public event Action<AiMessage>? TurnCompleted;

    /// <summary>Raised when a tool call needs a human decision.</summary>
    public event Action<ToolCallPart>? ApprovalRequested;

    /// <summary>
    /// Appends a user message and generates a reply.
    /// </summary>
    /// <param name="text">The user text. May be empty when attachments carry the request.</param>
    /// <param name="attachments">Files to attach.</param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <returns>The assistant message, or <see langword="null"/> when a turn was already running.</returns>
    public async Task<AiMessage?> SubmitAsync(
        string text,
        IReadOnlyList<Attachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        if (IsGenerating) return null;

        var message = new AiMessage(AiRole.User);
        if (!string.IsNullOrEmpty(text)) message.AddPart(new TextPart(text));
        if (attachments is not null)
        {
            foreach (var attachment in attachments) message.AddAttachment(attachment);
        }

        Conversation.AddMessage(message);

        if (Options.AutoTitle && Conversation.Title is null && !string.IsNullOrWhiteSpace(text))
        {
            Conversation.SetTitle(Summarize(text));
        }

        return await RunTurnAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Regenerates an assistant message, discarding it and everything after it.
    /// </summary>
    /// <param name="messageId">
    /// The assistant message to regenerate, or <see langword="null"/> for the most recent one.
    /// </param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <returns>The new assistant message, or <see langword="null"/> when there was nothing to regenerate.</returns>
    public async Task<AiMessage?> RegenerateAsync(string? messageId = null, CancellationToken cancellationToken = default)
    {
        if (IsGenerating) return null;

        var target = messageId is not null
            ? Conversation.FindMessage(messageId)
            : Conversation.Messages.LastOrDefault(m => m.Role.IsAssistant);

        if (target is null) return null;

        Conversation.TruncateAfter(target.MessageId, inclusive: true);
        return await RunTurnAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retries the last turn after a failure, keeping the user message in place.
    /// </summary>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <returns>The new assistant message, or <see langword="null"/> when there is nothing to retry.</returns>
    public async Task<AiMessage?> RetryAsync(CancellationToken cancellationToken = default)
    {
        if (IsGenerating) return null;

        if (Conversation.LastMessage is { } last && last.Role.IsAssistant)
        {
            Conversation.RemoveMessage(last.MessageId);
        }

        if (Conversation.LastMessage is null) return null;
        return await RunTurnAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the text of a user message and regenerates everything after it.
    /// </summary>
    /// <param name="messageId">The user message to edit.</param>
    /// <param name="text">The replacement text.</param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <returns>The new assistant message, or <see langword="null"/> when the message was not found.</returns>
    public async Task<AiMessage?> EditAsync(string messageId, string text, CancellationToken cancellationToken = default)
    {
        if (IsGenerating) return null;

        var message = Conversation.FindMessage(messageId);
        if (message is null || !message.Role.IsUser) return null;

        Conversation.TruncateAfter(messageId);
        if (message.PartsOf<TextPart>().FirstOrDefault() is { } part)
        {
            part.SetText(text);
        }
        else
        {
            message.AddPart(new TextPart(text));
        }

        return await RunTurnAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the current turn. The partial answer is kept and marked cancelled.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Records a human decision on a tool call that was waiting for one.
    /// </summary>
    /// <param name="toolCallId">The call being decided.</param>
    /// <param name="decision">The decision.</param>
    public void Approve(string toolCallId, ApprovalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(toolCallId);

        TaskCompletionSource<ApprovalDecision>? waiter;
        lock (_approvals)
        {
            _approvals.Remove(toolCallId, out waiter);
        }

        if (decision is ApprovalDecision.ApprovedAlways
            && _approvalPolicy is DefaultToolApprovalPolicy policy
            && FindToolCall(toolCallId) is { } call)
        {
            policy.RememberAlwaysApproved(call.ToolName);
        }

        FindToolCall(toolCallId)?.SetApproval(decision);
        waiter?.TrySetResult(decision);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Answers a question an agent run is blocked on, then continues the turn.
    /// </summary>
    /// <param name="runId">The blocked run.</param>
    /// <param name="answer">The user answer, appended as a user message.</param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <returns>The assistant message, or <see langword="null"/> when the run was not waiting.</returns>
    public async Task<AiMessage?> RespondToAgentAsync(
        string runId,
        string answer,
        CancellationToken cancellationToken = default)
    {
        var run = Conversation.FindRun(runId);
        if (run is null || run.Status is not AgentRunStatus.WaitingForInput) return null;

        run.ResolveInput();
        return await SubmitAsync(answer, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AiMessage?> RunTurnAsync(AiMessage? into, CancellationToken cancellationToken)
    {
        IsGenerating = true;
        LastError = null;

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (Options.Timeout is { } timeout) _cts.CancelAfter(timeout);
        var token = _cts.Token;

        var assistant = Processor.BeginGeneration(into);
        StateChanged?.Invoke();

        try
        {
            for (var round = 0; round <= Options.MaxToolRoundtrips; round++)
            {
                if (round > 0) assistant.SetStatus(MessageStatus.Streaming);

                var ready = new List<ToolCallPart>();
                void OnReady(ToolCallPart part) => ready.Add(part);

                Processor.ToolCallReady += OnReady;
                try
                {
                    await ConsumeAsync(BuildRequest(assistant), token).ConfigureAwait(false);
                }
                finally
                {
                    Processor.ToolCallReady -= OnReady;
                }

                if (!Options.ExecuteTools || ready.Count == 0) break;
                if (round == Options.MaxToolRoundtrips)
                {
                    assistant.AddPart(new ErrorPart(new AiError(
                        $"Stopped after {Options.MaxToolRoundtrips} tool round trips.")
                    {
                        Code = "tool_loop_limit",
                        Kind = AiErrorKind.Tool,
                    }));
                    break;
                }

                await ExecuteToolsAsync(ready, token).ConfigureAwait(false);
            }

            Processor.EndGeneration();
        }
        catch (OperationCanceledException)
        {
            LastError = new AiError("Generation stopped.") { Kind = AiErrorKind.Cancelled, Code = "cancelled" };
            assistant.CompleteOpenParts();
            assistant.SetStatus(MessageStatus.Cancelled);
            Processor.EndGeneration(MessageStatus.Cancelled);
        }
        catch (Exception exception) when (Options.CatchTransportExceptions)
        {
            _logger.LogError(exception, "The transport failed while generating a reply.");
            LastError = AiError.FromException(exception);
            assistant.CompleteOpenParts();
            assistant.SetError(LastError);
            Processor.EndGeneration(MessageStatus.Failed);
        }
        finally
        {
            IsGenerating = false;
            StateChanged?.Invoke();
            TurnCompleted?.Invoke(assistant);
        }

        return assistant;
    }

    private async Task ConsumeAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var stream = _transport.StreamAsync(request, cancellationToken);
        if (Options.Pacing is { } pacing) stream = stream.Paced(pacing, cancellationToken);

        await foreach (var evt in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            Processor.Apply(evt);
            if (evt is ErrorEvent { IsTerminal: true } error) LastError = error.Error;
            StateChanged?.Invoke();
        }
    }

    private AiRequest BuildRequest(AiMessage assistant)
    {
        var messages = new List<IAiMessage>(Conversation.Messages.Count);
        foreach (var message in Conversation.Messages)
        {
            if (ReferenceEquals(message, assistant) && message.Parts.Count == 0) continue;
            messages.Add(message);
        }

        return new AiRequest(messages)
        {
            ConversationId = Conversation.ConversationId,
            Options = Options.RequestOptions,
            Tools = _tools is null ? [] : _tools.Tools.Select(t => t.Definition).ToArray(),
            Services = _services,
        };
    }

    private async Task ExecuteToolsAsync(IReadOnlyList<ToolCallPart> calls, CancellationToken cancellationToken)
    {
        var tasks = new List<Task>(calls.Count);
        foreach (var call in calls) tasks.Add(ExecuteToolAsync(call, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ExecuteToolAsync(ToolCallPart call, CancellationToken cancellationToken)
    {
        var tool = _tools?.Find(call.ToolName);
        if (tool is null)
        {
            call.SetResult(ToolOutcome.Failure(new AiError($"No tool named '{call.ToolName}' is registered.")
            {
                Code = "unknown_tool",
                Kind = AiErrorKind.Tool,
            }));
            return;
        }

        call.Definition ??= tool.Definition;

        var invocation = new ToolInvocation(call.ToolCallId, call.ToolName, call.Arguments ?? default)
        {
            ConversationId = Conversation.ConversationId,
            MessageId = call.Message?.MessageId,
            Services = _services,
            Progress = new ToolProgressReporter(call, RaiseStateChanged),
        };

        try
        {
            if (await NeedsApprovalAsync(tool.Definition, invocation, cancellationToken).ConfigureAwait(false))
            {
                var decision = await WaitForApprovalAsync(call, cancellationToken).ConfigureAwait(false);
                if (decision is ApprovalDecision.Rejected or ApprovalDecision.Undecided)
                {
                    call.SetResult(ToolOutcome.Failure(new AiError("The user declined this tool call.")
                    {
                        Code = "rejected",
                        Kind = AiErrorKind.Tool,
                    }));
                    call.SetStatus(ToolCallStatus.Rejected);
                    return;
                }
            }

            call.SetStatus(ToolCallStatus.Running);
            StateChanged?.Invoke();

            var started = DateTimeOffset.UtcNow;
            var outcome = await tool.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
            call.SetResult(outcome with { Duration = outcome.Duration ?? DateTimeOffset.UtcNow - started });
        }
        catch (OperationCanceledException)
        {
            call.SetStatus(ToolCallStatus.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Tool {ToolName} threw while executing.", call.ToolName);
            call.SetResult(ToolOutcome.Failure(new AiError(exception.Message)
            {
                Kind = AiErrorKind.Tool,
                Detail = exception.ToString(),
                Exception = exception,
                IsRetryable = true,
            }));
        }
        finally
        {
            StateChanged?.Invoke();
        }
    }

    private async ValueTask<bool> NeedsApprovalAsync(
        AiToolDefinition definition,
        ToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _approvalPolicy.RequiresApprovalAsync(definition, invocation, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The approval policy threw; requiring approval to stay safe.");
            return true;
        }
    }

    private async Task<ApprovalDecision> WaitForApprovalAsync(ToolCallPart call, CancellationToken cancellationToken)
    {
        var waiter = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_approvals) _approvals[call.ToolCallId] = waiter;

        call.SetStatus(ToolCallStatus.AwaitingApproval);
        ApprovalRequested?.Invoke(call);
        StateChanged?.Invoke();

        await using var registration = cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken))
            .ConfigureAwait(false);

        return await waiter.Task.ConfigureAwait(false);
    }

    private ToolCallPart? FindToolCall(string toolCallId)
    {
        for (var i = Conversation.Messages.Count - 1; i >= 0; i--)
        {
            if (Conversation.Messages[i].FindToolCall(toolCallId) is { } call) return call;
        }

        return null;
    }

    private static string Summarize(string text)
    {
        var trimmed = text.Trim().ReplaceLineEndings(" ");
        return trimmed.Length <= 48 ? trimmed : string.Concat(trimmed.AsSpan(0, 48).TrimEnd(), "...");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        lock (_approvals)
        {
            foreach (var waiter in _approvals.Values) waiter.TrySetCanceled();
            _approvals.Clear();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();

    private sealed class ToolProgressReporter(ToolCallPart call, Action notify) : IProgress<ToolProgress>
    {
        public void Report(ToolProgress value)
        {
            call.SetProgress(value);
            notify();
        }
    }
}
