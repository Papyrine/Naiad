namespace Naiad.Diagrams.Class;

public class MethodParameter
{
    public required string Name { get; init; }
    public string? Type { get; set; }

    // Whether the type was written before the name (`int age`) rather than after it (`age: int`).
    internal bool TypeFirst { get; set; }
}