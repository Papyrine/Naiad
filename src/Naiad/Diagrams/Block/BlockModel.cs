namespace Naiad.Diagrams.Block;

public class BlockModel : DiagramBase
{
    /// <summary>
    /// The number of columns set by <c>columns N</c>. Zero means none was set, and the blocks then share
    /// a single row, as they do in Mermaid.
    /// </summary>
    public int Columns { get; set; }
    public List<BlockElement> Elements { get; } = [];
}