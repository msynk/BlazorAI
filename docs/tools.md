# Tools

## Defining a tool

```csharp
AiTool.Create<WeatherArgs, WeatherResult>(
    "get_weather",
    "Look up the current weather for a city.",
    (args, token) => weather.GetAsync(args.City, token));

public sealed record WeatherArgs(
    [property: Description("The city to look up, for example Oslo.")] string City,
    [property: Description("How many days ahead to forecast.")] int Days = 1);
```

The JSON Schema is generated from `WeatherArgs`, including the descriptions, and travels on the
request so your transport can hand it to a provider unchanged.

For a tool that needs to attach citations, custom display text, or a structured failure, return a
full outcome:

```csharp
AiTool.Create<SearchArgs>(
    "search_docs",
    "Search the documentation and return matching passages.",
    async (args, invocation, token) =>
    {
        invocation.Progress.Report(new ToolProgress(0.3, "Querying the index"));
        var hits = await index.SearchAsync(args.Query, token);

        return ToolOutcome.Success(hits) with
        {
            Text = $"{hits.Count} passages matched.",
            Citations = hits.Select(h => new Citation(AiId.New("cit"), h.Source)).ToArray(),
        };
    });
```

Citations returned by a tool are merged into the message, so a retrieval tool contributes to the
source list without the transport doing anything.

Tools that need dependencies implement `IAiTool` and resolve them from
`ToolInvocation.Services` rather than capturing them, because one instance is shared across
conversations.

## The execution loop

The transport asks; the session runs. Per round it collects the calls whose arguments completed,
consults the approval policy, waits where a human is required, executes the rest **concurrently**,
records each result on its `ToolCallPart`, and asks the model again — up to `MaxToolRoundtrips`.

Results are recorded on the originating call rather than appended as synthetic messages, so there is
one representation of a call and the UI has nothing to deduplicate. On the next request your
transport reads them off the message it is handed:

```csharp
var results = request.Messages
    .SelectMany(m => m.Parts)
    .OfType<ToolCallPart>()
    .Where(c => c.Result is not null);
```

Turn the loop off entirely when the server runs tools and only reports them:

```csharp
new ChatSessionOptions { ExecuteTools = false }
```

## Approval

The default policy asks for anything declared `ToolRisk.Destructive`, and honours an explicit
`RequiresApproval` either way.

```csharp
AiTool.Create<DeleteArgs, DeleteResult>(
    "delete_records", "Permanently delete records matching a filter.", DeleteAsync,
    definition => definition with { Risk = ToolRisk.Destructive });
```

Replace it when the decision depends on the arguments or the current user rather than only on the
tool:

```csharp
public sealed class BudgetPolicy(ICurrentUser user) : IToolApprovalPolicy
{
    public async ValueTask<bool> RequiresApprovalAsync(
        AiToolDefinition definition, ToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (definition.Name is not "issue_refund") return definition.Risk is ToolRisk.Destructive;

        var amount = invocation.GetArguments<RefundArgs>()?.Amount ?? 0m;
        return amount > await user.GetApprovalLimitAsync(cancellationToken);
    }
}

services.AddBlazorAI(ai => ai.UseApprovalPolicy<BudgetPolicy>());
```

While a call waits, the turn is genuinely paused: the tool has not run, the model has not been asked
again, and cancelling the turn cancels the wait. A policy that throws is treated as requiring
approval, because failing closed is the only safe direction.

The UI offers three answers rather than two. Allowing once and allowing always are different
decisions, and collapsing them forces users either to re-approve constantly or to grant more than
they meant to.

```csharp
session.Approve(toolCallId, ApprovalDecision.ApprovedAlways);
```

## Rendering

Tool calls render as a collapsed card that expands on failure or when approval is needed, because
neither belongs behind a chevron. Arguments are parsed leniently on every delta, so the card shows
`city: "Osl"` while the model is still writing.

Replace the card for one tool without touching the message list:

```csharp
services.ConfigureAiRenderers(renderers =>
    renderers.RegisterTool<WeatherCard>("get_weather"));
```

The component receives the call as a `Call` parameter. Renderers run inside an error boundary, so a
payload shaped differently from what the renderer expected degrades to a small notice rather than
taking the page down — worth remembering, because the data came from a model.

## Failures

Prefer returning a failed outcome to throwing:

```csharp
return ToolOutcome.Failure(new AiError("The index is rebuilding.")
{
    Code = "index_unavailable",
    Kind = AiErrorKind.Tool,
    IsRetryable = true,
    RetryAfter = TimeSpan.FromSeconds(30),
});
```

An exception that escapes is caught, logged, and converted into the same shape. Either way the
failure is sent back to the model with the other results, so it can react rather than stall. One
failed call does not stop the others in its round.
