namespace Naiad.Diagrams.UseCase;

public class UseCaseModel : DiagramBase
{
    // Mermaid lays a use case diagram out left to right unless it says otherwise.
    public UseCaseModel() =>
        Direction = Direction.LeftToRight;

    public string? AccessibilityTitle { get; set; }
    public string? AccessibilityDescription { get; set; }

    public List<UseCaseActor> Actors { get; } = [];
    public List<UseCase> UseCases { get; } = [];
    public List<SystemBoundary> Boundaries { get; } = [];
    public List<UseCaseNote> Notes { get; } = [];
    public List<JsonTable> JsonTables { get; } = [];
    public List<UseCaseRelationship> Relationships { get; } = [];
}
