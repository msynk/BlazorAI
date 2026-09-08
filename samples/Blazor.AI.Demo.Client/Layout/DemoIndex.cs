namespace Blazor.AI.Demo.Client.Layout;

/// <summary>One page of the demo.</summary>
/// <param name="Number">Its position in the tour.</param>
/// <param name="Title">The navigation label.</param>
/// <param name="Route">The route it lives at.</param>
/// <param name="Group">The section it belongs to.</param>
/// <param name="Summary">What it demonstrates.</param>
public sealed record DemoPage(int Number, string Title, string Route, string Group, string Summary);

/// <summary>
/// The tour, in the order it is meant to be read.
/// </summary>
public static class DemoIndex
{
    /// <summary>Every demo page.</summary>
    public static IReadOnlyList<DemoPage> Pages { get; } =
    [
        new(1, "Basic chat", "/basic", "Conversation", "The smallest working chat."),
        new(2, "Streaming", "/streaming", "Conversation", "Progressive Markdown, code, and tables."),
        new(3, "Reasoning", "/reasoning", "Conversation", "Thinking shown separately from the answer."),
        new(4, "Multiple conversations", "/conversations", "Conversation", "A sidebar over a store."),
        new(5, "Attachments", "/attachments", "Conversation", "Drag, paste, and pick files."),
        new(6, "Citations", "/citations", "Content", "Inline markers anchored to claims."),
        new(7, "Artifacts", "/artifacts", "Content", "A document written and then revised."),
        new(8, "Structured output", "/structured", "Content", "A table that fills in row by row."),
        new(9, "Tool calling", "/tools", "Tools", "A tool proposed, run, and answered."),
        new(10, "Tool approval", "/approval", "Tools", "A destructive tool that has to be allowed."),
        new(11, "Custom tool UI", "/custom-tool", "Tools", "One tool rendered by your component."),
        new(12, "Parallel tools", "/parallel-tools", "Tools", "Two calls in flight at once."),
        new(13, "Multi-step agent", "/agent", "Agents", "A plan, executed and reported."),
        new(14, "Agent asks a question", "/agent-input", "Agents", "A run that blocks on the user."),
        new(15, "Agent failure", "/agent-failure", "Agents", "A step that fails, legibly."),
        new(16, "Generative UI", "/generative", "Generative", "The model renders your components."),
        new(17, "AI forms", "/forms", "Generative", "A form proposed, checked, submitted."),
        new(18, "Models and usage", "/usage", "Operations", "Picker, tokens, cost, context."),
        new(19, "Errors and retry", "/errors", "Operations", "Failure that recovers."),
        new(20, "Long conversations", "/performance", "Operations", "Five hundred messages, virtualized."),
    ];

    /// <summary>The pages grouped by section, in tour order.</summary>
    public static IEnumerable<IGrouping<string, DemoPage>> Groups => Pages.GroupBy(p => p.Group);

    /// <summary>Finds the page for a route.</summary>
    /// <param name="route">The route to look up.</param>
    /// <returns>The page, or <see langword="null"/> when the route is not part of the tour.</returns>
    public static DemoPage? Find(string route) =>
        Pages.FirstOrDefault(p => string.Equals(p.Route, route, StringComparison.OrdinalIgnoreCase));
}
