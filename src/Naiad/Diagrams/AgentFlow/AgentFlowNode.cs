namespace Naiad.Diagrams.AgentFlow;

public class AgentFlowNode : Node
{
    public AgentFlowNodeKind Kind { get; set; } = AgentFlowNodeKind.Node;

    /// <summary>The <c>@{ … }</c> keys, as written. Mermaid keeps unknown keys, so this does too.</summary>
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);
}
