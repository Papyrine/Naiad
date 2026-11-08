namespace Naiad.Diagrams.TreeView;

public class TreeViewModel : DiagramBase
{
    /// <summary>
    /// The entries written at the outermost level. Mermaid hangs them all off an implicit root drawn as
    /// <c>/</c>, which the renderer adds; it is not part of the model.
    /// </summary>
    public List<TreeViewNode> Nodes { get; } = [];
}
