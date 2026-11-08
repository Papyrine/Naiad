namespace Naiad.Diagrams.Wardley;

/// <summary>An <c>evolve</c>: the node is expected to move across to a more evolved position.</summary>
public class WardleyTrend
{
    public required string NodeId { get; init; }

    public double TargetEvolution { get; init; }
}
