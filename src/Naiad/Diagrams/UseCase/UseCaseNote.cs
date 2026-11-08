namespace Naiad.Diagrams.UseCase;

public class UseCaseNote
{
    public required string Id { get; init; }

    /// <summary>The actor or use case the note is attached to.</summary>
    public required string TargetId { get; init; }

    /// <summary>The note text: plain text, or a Mermaid markdown string still wrapped in its backticks.</summary>
    public required string Text { get; init; }
}
