namespace Naiad.Diagrams.AgentFlow;

public class AgentFlowContainer : Subgraph
{
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);

    /// <summary><c>@{ view: collapsed }</c> folds the container down to a single summary node.</summary>
    public bool Collapsed =>
        Metadata.TryGetValue("view", out var view) &&
        view == "collapsed";
}
