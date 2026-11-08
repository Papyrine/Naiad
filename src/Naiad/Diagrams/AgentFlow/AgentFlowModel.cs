namespace Naiad.Diagrams.AgentFlow;

/// <summary>
/// An agent flow: agents, tasks and tools as nodes, the control and data moving between them as edges, and
/// <c>flow … end</c> containers grouping them. <see cref="GraphDiagramBase.Nodes"/> holds
/// <see cref="AgentFlowNode"/>s, <see cref="GraphDiagramBase.Edges"/> holds <see cref="AgentFlowEdge"/>s
/// and <see cref="GraphDiagramBase.Subgraphs"/> holds the top-level <see cref="AgentFlowContainer"/>s.
/// </summary>
public class AgentFlowModel : GraphDiagramBase
{
    public string? AccessibilityTitle { get; set; }
    public string? AccessibilityDescription { get; set; }

    public IEnumerable<AgentFlowNode> AgentNodes => Nodes.OfType<AgentFlowNode>();
    public IEnumerable<AgentFlowEdge> AgentEdges => Edges.OfType<AgentFlowEdge>();

    /// <summary>Every container, nested ones included, each before the ones inside it.</summary>
    public IEnumerable<AgentFlowContainer> AllContainers =>
        Flatten(Subgraphs.OfType<AgentFlowContainer>());

    public AgentFlowContainer? GetContainer(string id) =>
        AllContainers.FirstOrDefault(_ => _.Id == id);

    static IEnumerable<AgentFlowContainer> Flatten(IEnumerable<AgentFlowContainer> containers)
    {
        foreach (var container in containers)
        {
            yield return container;
            foreach (var nested in Flatten(container.NestedSubgraphs.OfType<AgentFlowContainer>()))
            {
                yield return nested;
            }
        }
    }
}
