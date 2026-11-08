namespace Naiad;

public class Subgraph
{
    public required string Id { get; init; }
    public string? Title { get; set; }
    public Direction Direction { get; set; } = Direction.TopToBottom;
    public List<string> NodeIds { get; } = [];
    public List<Subgraph> NestedSubgraphs { get; } = [];

    /// <summary>Style classes assigned via a <c>class</c> directive.</summary>
    public List<string> Classes { get; } = [];

    /// <summary>Resolved visual overrides (from <c>classDef</c>/<c>class</c>/<c>style</c>), or null for theme defaults.</summary>
    public NodeStyle? Style { get; set; }

    /// <summary>
    /// Whether a node listed in this subgraph stayed in an earlier one it was listed in first. A swimlane
    /// left empty that way is dropped, where one left empty on purpose is kept.
    /// </summary>
    internal bool LostMember { get; set; }

    // Layout properties
    public Position Position { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public Rect Bounds => new(
        Position.X - Width / 2,
        Position.Y - Height / 2,
        Width,
        Height);
}