namespace Naiad.Diagrams.Cynefin;

public class CynefinModel : DiagramBase
{
    /// <summary>
    /// The domains the diagram declares, by name. All five are drawn whether declared or not; a declared one
    /// only adds its items. A domain declared twice keeps the items of its last block, as Mermaid does.
    /// </summary>
    public Dictionary<CynefinDomainName, CynefinDomain> Domains { get; } = [];

    /// <summary>The movements between domains, in declaration order. Self-loops are left out.</summary>
    public List<CynefinTransition> Transitions { get; } = [];
}
