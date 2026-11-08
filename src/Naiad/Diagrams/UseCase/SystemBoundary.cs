namespace Naiad.Diagrams.UseCase;

public class SystemBoundary
{
    public required string Id { get; init; }

    /// <summary>The display title: plain text, or a Mermaid markdown string still wrapped in its backticks.</summary>
    public required string Title { get; set; }

    public BoundaryType Type { get; set; } = BoundaryType.Rect;

    /// <summary>The actors and use cases declared inside the boundary, in declaration order.</summary>
    public List<string> MemberIds { get; } = [];

    public List<string> Classes { get; } = [];

    /// <summary>Resolved visual overrides (from <c>classDef</c>/<c>class</c>/<c>style</c>), or null for theme defaults.</summary>
    public NodeStyle? Style { get; set; }
}
