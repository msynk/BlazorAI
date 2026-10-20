namespace Blazor.AI.Markdown;

/// <summary>The base of every node in a parsed Markdown document.</summary>
public abstract class MarkdownNode;

/// <summary>The base of every block-level node.</summary>
public abstract class MarkdownBlock : MarkdownNode
{
    /// <summary>
    /// <see langword="true"/> when the block may still grow because the source ended inside it.
    /// </summary>
    /// <remarks>
    /// Renderers use this to defer work that would flicker: an open fenced block is not highlighted,
    /// an open table is not measured. It is the single most important flag for streaming quality.
    /// </remarks>
    public bool IsOpen { get; internal set; }
}

/// <summary>The base of every inline node.</summary>
public abstract class MarkdownInline : MarkdownNode;

/// <summary>A parsed document: an ordered list of blocks.</summary>
public sealed class MarkdownDocument : MarkdownBlock
{
    /// <summary>The blocks, in order.</summary>
    public List<MarkdownBlock> Blocks { get; } = [];

    /// <summary>
    /// The footnotes the text cites, in the order they are first cited and numbered from one.
    /// </summary>
    /// <remarks>
    /// A definition nobody cites is dropped, as GitHub does. Renderers list these after the blocks,
    /// each with a link back to the reference that cited it.
    /// </remarks>
    public List<FootnoteBlock> Footnotes { get; } = [];

    /// <summary>An empty document.</summary>
    public static MarkdownDocument Empty { get; } = new();
}

/// <summary>A run of text terminated by a blank line or another block.</summary>
public sealed class ParagraphBlock : MarkdownBlock
{
    /// <summary>The inline content.</summary>
    public List<MarkdownInline> Inlines { get; } = [];
}

/// <summary>A heading.</summary>
/// <param name="level">The heading level, 1 through 6.</param>
public sealed class HeadingBlock(int level) : MarkdownBlock
{
    /// <summary>The heading level, 1 through 6.</summary>
    public int Level { get; } = Math.Clamp(level, 1, 6);

    /// <summary>The inline content.</summary>
    public List<MarkdownInline> Inlines { get; } = [];

    /// <summary>A slug derived from the text, used as an anchor id.</summary>
    public string? Id { get; internal set; }
}

/// <summary>A code block, fenced or indented.</summary>
public sealed class CodeBlock : MarkdownBlock
{
    /// <summary>The language identifier from the info string, when there was one.</summary>
    public string? Language { get; internal set; }

    /// <summary>The rest of the info string after the language.</summary>
    public string? Meta { get; internal set; }

    /// <summary>The source, without the fences.</summary>
    public string Code { get; internal set; } = string.Empty;

    /// <summary><see langword="true"/> for a fenced block, <see langword="false"/> for an indented one.</summary>
    public bool IsFenced { get; internal set; }
}

/// <summary>A bullet or numbered list.</summary>
public sealed class ListBlock : MarkdownBlock
{
    /// <summary><see langword="true"/> for a numbered list.</summary>
    public bool IsOrdered { get; internal set; }

    /// <summary>The first number of an ordered list.</summary>
    public int Start { get; internal set; } = 1;

    /// <summary>
    /// <see langword="true"/> when items are separated by blank lines and should be spaced apart.
    /// </summary>
    public bool IsLoose { get; internal set; }

    /// <summary>The items, in order.</summary>
    public List<ListItemBlock> Items { get; } = [];
}

/// <summary>One item of a list.</summary>
public sealed class ListItemBlock : MarkdownBlock
{
    /// <summary>
    /// The checkbox state of a task-list item, or <see langword="null"/> when the item has no checkbox.
    /// </summary>
    public bool? IsChecked { get; internal set; }

    /// <summary>The item content, which may contain nested blocks.</summary>
    public List<MarkdownBlock> Blocks { get; } = [];
}

/// <summary>A block quote.</summary>
public sealed class QuoteBlock : MarkdownBlock
{
    /// <summary>The quoted content.</summary>
    public List<MarkdownBlock> Blocks { get; } = [];
}

/// <summary>How a table column is aligned.</summary>
public enum ColumnAlignment
{
    /// <summary>No alignment specified.</summary>
    None = 0,

    /// <summary>Left aligned.</summary>
    Left = 1,

    /// <summary>Centre aligned.</summary>
    Center = 2,

    /// <summary>Right aligned.</summary>
    Right = 3,
}

/// <summary>A pipe table.</summary>
public sealed class TableBlock : MarkdownBlock
{
    /// <summary>The header cells.</summary>
    public List<List<MarkdownInline>> Header { get; } = [];

    /// <summary>The body rows.</summary>
    public List<List<List<MarkdownInline>>> Rows { get; } = [];

    /// <summary>The alignment of each column.</summary>
    public List<ColumnAlignment> Alignments { get; } = [];
}

/// <summary>A horizontal rule.</summary>
public sealed class ThematicBreakBlock : MarkdownBlock;

/// <summary>A footnote the text cites, listed on <see cref="MarkdownDocument.Footnotes"/>.</summary>
/// <param name="label">The label the source used, without the caret.</param>
/// <param name="number">The footnote's number, which is its position in the list.</param>
public sealed class FootnoteBlock(string label, int number) : MarkdownBlock
{
    /// <summary>The label the source used, without the caret.</summary>
    public string Label { get; } = label;

    /// <summary>The footnote's number, which is its position in the list.</summary>
    public int Number { get; } = number;

    /// <summary>How many references cite this footnote, so each can get its own back-link.</summary>
    public int ReferenceCount { get; internal set; }

    /// <summary>The footnote content, which may contain several blocks.</summary>
    public List<MarkdownBlock> Blocks { get; } = [];
}

/// <summary>A displayed mathematical expression.</summary>
/// <param name="expression">The expression source, without delimiters.</param>
public sealed class MathBlock(string expression) : MarkdownBlock
{
    /// <summary>The expression source, without delimiters.</summary>
    public string Expression { get; internal set; } = expression;
}

/// <summary>Raw HTML found in the source.</summary>
/// <param name="html">The raw markup.</param>
/// <remarks>
/// Never rendered as markup unless the host opts in. Model output is untrusted input, and treating
/// it as HTML by default is how a chat UI grows a cross-site scripting hole.
/// </remarks>
public sealed class HtmlBlock(string html) : MarkdownBlock
{
    /// <summary>The raw markup.</summary>
    public string Html { get; internal set; } = html;
}
