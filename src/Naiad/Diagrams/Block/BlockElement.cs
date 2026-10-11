namespace Naiad.Diagrams.Block;

public class BlockElement
{
    public required string Id { get; init; }
    public required string? Label { get; init; }
    public required int Span { get; init; }
    public required BlockShape Shape { get; init; }

    /// <summary>Set for <c>space</c>: the element takes up room in the grid and draws nothing.</summary>
    public bool IsSpace { get; init; }
}