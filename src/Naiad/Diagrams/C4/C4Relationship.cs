namespace Naiad.Diagrams.C4;

public class C4Relationship
{
    public required string From { get; init; }
    public required string To { get; init; }
    public string? Label { get; set; }
    public string? Technology { get; set; }
    public C4RelationshipDirection Direction { get; set; } = C4RelationshipDirection.Default;

    /// <summary>Set by <c>BiRel</c>: the relationship runs both ways and has an arrowhead at each end.</summary>
    public bool IsBidirectional { get; set; }
}