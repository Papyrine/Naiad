namespace Naiad.Diagrams.Class;

public class ClassMethod
{
    public required string Name { get; init; }
    public string? ReturnType { get; set; }
    public List<MethodParameter> Parameters { get; } = [];
    public Visibility Visibility { get; set; } = Visibility.None;
    public bool IsStatic { get; set; }
    public bool IsAbstract { get; set; }
}
