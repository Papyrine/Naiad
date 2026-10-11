namespace Naiad;

public class LayoutOptions
{
    public Direction Direction { get; set; } = Direction.TopToBottom;
    public double NodeSeparation { get; set; } = 50;
    public double RankSeparation { get; set; } = 50;

    /// <summary>
    /// Lay a subgraph out in its own <see cref="Subgraph.Direction"/> when that differs from the direction
    /// around it. As in Mermaid, this only applies to a subgraph none of whose members is linked to
    /// anything outside it.
    /// </summary>
    public bool SubgraphDirections { get; set; }
}
