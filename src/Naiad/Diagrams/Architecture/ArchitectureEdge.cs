namespace Naiad.Diagrams.Architecture;

public class ArchitectureEdge
{
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public EdgeDirection SourceSide { get; set; } = EdgeDirection.Right;
    public EdgeDirection TargetSide { get; set; } = EdgeDirection.Left;
    public bool SourceArrow { get; set; }
    public bool TargetArrow { get; set; }
    public string? SourceGroup { get; set; }
    public string? TargetGroup { get; set; }

    /// <summary>The text of <c>-[Label]-</c>, drawn at the middle of the edge.</summary>
    public string? Label { get; set; }
}