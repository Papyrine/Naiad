namespace Naiad.Diagrams.AgentFlow;

public enum AgentFlowNodeKind
{
    /// <summary>A node declared by use: a task, tool, input, decision, reference document or action.</summary>
    Node,

    /// <summary>An external system declared with the <c>connector</c> keyword.</summary>
    Connector
}
