namespace Naiad.Diagrams.Class;

public class ClassMember
{
    public required string Name { get; init; }
    public string? Type { get; set; }
    public Visibility Visibility { get; set; } = Visibility.None;
    public bool IsStatic { get; set; }
}