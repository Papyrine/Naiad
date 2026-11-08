namespace Naiad.Diagrams.UseCase;

public class UseCaseActor
{
    public required string Id { get; init; }

    /// <summary>The display label: plain text, or a Mermaid markdown string still wrapped in its backticks.</summary>
    public required string Label { get; set; }

    public ActorType Type { get; set; } = ActorType.Normal;

    /// <summary>The icon name of an <see cref="ActorType.Icon"/> actor, such as <c>fa:user</c>.</summary>
    public string? Icon { get; set; }

    /// <summary>Draws the business slash across the actor's head.</summary>
    public bool Business { get; set; }

    public string? Stereotype { get; set; }

    /// <summary>The system boundary the actor is declared in, if any.</summary>
    public string? BoundaryId { get; set; }

    public List<string> Classes { get; } = [];

    /// <summary>Resolved visual overrides (from <c>classDef</c>/<c>class</c>/<c>style</c>), or null for theme defaults.</summary>
    public NodeStyle? Style { get; set; }
}
