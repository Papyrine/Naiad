namespace Naiad.Diagrams.Block;

public class BlockElement
{
    public required string Id { get; init; }
    public string? Label { get; set; }

    /// <summary>How many columns the block takes up, from <c>id:N</c>.</summary>
    public int Span { get; set; } = 1;

    public BlockShape Shape { get; set; }

    /// <summary>Set for <c>space</c>: the element takes up room in the grid and draws nothing.</summary>
    public bool IsSpace { get; init; }

    /// <summary>Where a <see cref="BlockShape.BlockArrow"/> points.</summary>
    public BlockArrowDirection ArrowDirections { get; set; }

    /// <summary>
    /// Set for a <c>block … end</c>: a block drawn around the blocks it holds, which are laid out in a grid
    /// of their own.
    /// </summary>
    public bool IsComposite { get; init; }

    /// <summary>
    /// For a composite, the number of columns set by a <c>columns N</c> inside it. Zero means none was set,
    /// and what it holds then shares a single row.
    /// </summary>
    public int Columns { get; set; }

    /// <summary>For a composite, the blocks it holds.</summary>
    public List<BlockElement> Children { get; } = [];

    /// <summary>Overrides from <c>style</c>, <c>classDef</c> and <c>class</c> lines, or null for none.</summary>
    public NodeStyle? Style { get; set; }
}
