namespace Naiad.Diagrams.AgentFlow;

public enum AgentFlowEdgeSemantic
{
    /// <summary><c>--&gt;</c>: control or data flows from source to target.</summary>
    Sequence,

    /// <summary><c>-.-</c>: the source consults the target; no control passes.</summary>
    Reference,

    /// <summary><c>--x</c>: the failure path out of the source.</summary>
    Failure
}
