namespace Naiad.Diagrams.Class;

public class ClassNote
{
    public required string Text { get; init; }

    // The class a `note for X "..."` is attached to; null for a note on the diagram as a whole.
    public string? ForClassId { get; init; }
}
