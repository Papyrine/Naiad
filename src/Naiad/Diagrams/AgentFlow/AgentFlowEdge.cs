namespace Naiad.Diagrams.AgentFlow;

public class AgentFlowEdge : Edge
{
    /// <summary>The id written before the operator (<c>a e1@--&gt; b</c>), which metadata can be attached to.</summary>
    public string? Id { get; init; }

    public AgentFlowEdgeSemantic Semantic { get; init; }

    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);
}
