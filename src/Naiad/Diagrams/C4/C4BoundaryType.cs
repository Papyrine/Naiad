namespace Naiad.Diagrams.C4;

public enum C4BoundaryType
{
    System,
    Container,
    Enterprise,
    Deployment,
    Node,

    /// <summary>A plain <c>Boundary(...)</c>, whose kind is whatever its type argument says.</summary>
    Generic
}