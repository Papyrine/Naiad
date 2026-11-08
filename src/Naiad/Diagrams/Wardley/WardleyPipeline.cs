namespace Naiad.Diagrams.Wardley;

/// <summary>A component evolving along a line of alternatives, all at its visibility.</summary>
public class WardleyPipeline
{
    public required string ParentId { get; init; }

    public List<string> ComponentIds { get; } = [];
}
