namespace Naiad.Diagrams.Block;

public class BlockModel : DiagramBase
{
    /// <summary>
    /// The number of columns set by <c>columns N</c>. Zero means none was set, and the blocks then share
    /// a single row, as they do in Mermaid.
    /// </summary>
    public int Columns { get; set; }

    /// <summary>The top-level blocks, in the order they fill the grid. Composites hold their own.</summary>
    public List<BlockElement> Elements { get; } = [];

    /// <summary>The links between blocks, from <c>A --&gt; B</c>.</summary>
    public List<Edge> Edges { get; } = [];
}
