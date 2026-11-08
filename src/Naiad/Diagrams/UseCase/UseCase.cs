namespace Naiad.Diagrams.UseCase;

public class UseCase
{
    public required string Id { get; init; }

    /// <summary>The display label: plain text, or a Mermaid markdown string still wrapped in its backticks.</summary>
    public required string Label { get; set; }

    public UseCaseShape Shape { get; set; } = UseCaseShape.Ellipse;

    /// <summary>Draws the business slash on the right of the ellipse.</summary>
    public bool Business { get; set; }

    public string? Stereotype { get; set; }

    /// <summary>The system boundary the use case is declared in, if any.</summary>
    public string? BoundaryId { get; set; }

    public List<string> Classes { get; } = [];

    /// <summary>Resolved visual overrides (from <c>classDef</c>/<c>class</c>/<c>style</c>), or null for theme defaults.</summary>
    public NodeStyle? Style { get; set; }
}
