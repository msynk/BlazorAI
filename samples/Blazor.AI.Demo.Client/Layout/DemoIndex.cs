namespace Blazor.AI.Demo.Client.Layout;

/// <summary>One page of the demo.</summary>
/// <param name="Number">Its position in the tour.</param>
/// <param name="Title">The navigation label.</param>
/// <param name="Route">The route it lives at.</param>
/// <param name="Group">The section it belongs to.</param>
/// <param name="Summary">What it demonstrates.</param>
/// <param name="Source">The page's file name under <c>Pages/</c>, for the view-source link.</param>
public sealed record DemoEntry(int Number, string Title, string Route, string Group, string Summary, string Source);

/// <summary>
/// The tour, in the order it is meant to be read.
/// </summary>
public static class DemoIndex
{
    /// <summary>Where the demo source is published, for the view-source links.</summary>
    public const string SourceRoot = "https://github.com/msynk/BlazorAI/blob/main/samples/Blazor.AI.Demo.Client/Pages/";

    private static readonly (string Title, string Route, string Group, string Summary, string Source)[] Entries =
    [
        ("Basic chat", "/basic", "Conversation", "The smallest working chat.", "Basic.razor"),
        ("Streaming", "/streaming", "Conversation", "Progressive Markdown, code, and tables.", "Streaming.razor"),
        ("Reasoning", "/reasoning", "Conversation", "Thinking shown separately from the answer.", "Reasoning.razor"),
        ("Multiple conversations", "/conversations", "Conversation", "A sidebar over a store.", "Conversations.razor"),
        ("Attachments", "/attachments", "Conversation", "Drag, paste, and pick files.", "Attachments.razor"),
        ("Commands and mentions", "/commands", "Conversation", "Slash commands and @-references in the composer.", "Commands.razor"),
        ("Citations", "/citations", "Content", "Inline markers anchored to claims.", "Citations.razor"),
        ("Artifacts", "/artifacts", "Content", "A document written and then revised.", "Artifacts.razor"),
        ("Structured output", "/structured", "Content", "A table that fills in row by row.", "Structured.razor"),
        ("Tool calling", "/tools", "Tools", "A tool proposed, run, and answered.", "Tools.razor"),
        ("Tool approval", "/approval", "Tools", "A destructive tool that has to be allowed.", "Approval.razor"),
        ("Custom tool UI", "/custom-tool", "Tools", "One tool rendered by your component.", "CustomTool.razor"),
        ("Parallel tools", "/parallel-tools", "Tools", "Two calls in flight at once.", "ParallelTools.razor"),
        ("Multi-step agent", "/agent", "Agents", "A plan, executed and reported.", "Agent.razor"),
        ("Agent asks a question", "/agent-input", "Agents", "A run that blocks on the user.", "AgentInput.razor"),
        ("Agent failure", "/agent-failure", "Agents", "A step that fails, legibly.", "AgentFailure.razor"),
        ("Generative UI", "/generative", "Generative", "The model renders your components.", "Generative.razor"),
        ("AI forms", "/forms", "Generative", "A form proposed, checked, submitted.", "Forms.razor"),
        ("Models and usage", "/usage", "Operations", "Picker, tokens, cost, context.", "Usage.razor"),
        ("Errors and retry", "/errors", "Operations", "Failure that recovers.", "Errors.razor"),
        ("Long conversations", "/performance", "Operations", "Five hundred messages, virtualized.", "Performance.razor"),
        ("Theming", "/theming", "Operations", "Colour scheme, density, and tokens.", "Theming.razor"),
    ];

    /// <summary>Every demo page, numbered in tour order.</summary>
    public static IReadOnlyList<DemoEntry> Pages { get; } =
        Entries.Select((e, i) => new DemoEntry(i + 1, e.Title, e.Route, e.Group, e.Summary, e.Source)).ToArray();

    /// <summary>The pages grouped by section, in tour order.</summary>
    public static IEnumerable<IGrouping<string, DemoEntry>> Groups => Pages.GroupBy(p => p.Group);

    /// <summary>Finds the page for a route.</summary>
    /// <param name="route">The route to look up, with or without a leading slash.</param>
    /// <returns>The page, or <see langword="null"/> when the route is not part of the tour.</returns>
    public static DemoEntry? Find(string route)
    {
        var path = "/" + route.Split('?', '#')[0].Trim('/');
        return Pages.FirstOrDefault(p => string.Equals(p.Route, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The page before this one in the tour.</summary>
    /// <param name="page">The current page.</param>
    /// <returns>The previous page, or <see langword="null"/> at the start.</returns>
    public static DemoEntry? Previous(DemoEntry page) => page.Number > 1 ? Pages[page.Number - 2] : null;

    /// <summary>The page after this one in the tour.</summary>
    /// <param name="page">The current page.</param>
    /// <returns>The next page, or <see langword="null"/> at the end.</returns>
    public static DemoEntry? Next(DemoEntry page) => page.Number < Pages.Count ? Pages[page.Number] : null;
}
