namespace Naiad.Diagrams.UseCase;

public class UseCaseRelationship
{
    /// <summary>The explicit edge ID (<c>id@--&gt;</c>), or null for an anonymous edge.</summary>
    public string? Id { get; init; }

    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public UseCaseRelationshipType Type { get; init; } = UseCaseRelationshipType.Association;

    /// <summary>The marker drawn where the edge leaves its source.</summary>
    public UseCaseMarker StartMarker { get; init; }

    /// <summary>The marker drawn where the edge reaches its target.</summary>
    public UseCaseMarker EndMarker { get; init; }

    /// <summary>An association's own label: plain text, or a Mermaid markdown string still wrapped in its backticks.</summary>
    public string? Label { get; init; }

    /// <summary>The fewest ranks the edge may span; each dash beyond the second asks for one more.</summary>
    public int MinLength { get; init; } = 1;

    public List<string> Classes { get; } = [];

    /// <summary>Resolved visual overrides (from <c>classDef</c>/<c>class</c>/<c>style</c>), or null for theme defaults.</summary>
    public NodeStyle? Style { get; set; }
}
