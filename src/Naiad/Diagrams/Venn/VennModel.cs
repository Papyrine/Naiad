namespace Naiad.Diagrams.Venn;

public class VennModel : DiagramBase
{
    /// <summary>The sets and unions, in the order they were declared.</summary>
    public List<VennArea> Areas { get; } = [];

    /// <summary>The free-standing labels placed inside a set or union.</summary>
    public List<VennTextNode> TextNodes { get; } = [];

    /// <summary>The <c>style</c> statements, in the order they were declared; later ones win.</summary>
    public List<VennStyle> Styles { get; } = [];
}
