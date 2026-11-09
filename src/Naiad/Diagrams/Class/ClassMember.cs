namespace Naiad.Diagrams.Class;

public class ClassMember
{
    public required string Name { get; init; }
    public string? Type { get; set; }
    public Visibility Visibility { get; set; } = Visibility.None;
    public bool IsStatic { get; set; }

    // Whether the type was written before the name (`+String name`) rather than after it (`+name : String`).
    // Mermaid shows a member as written, so the order is kept.
    internal bool TypeFirst { get; set; }
}