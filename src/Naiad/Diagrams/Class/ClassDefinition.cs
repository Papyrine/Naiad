namespace Naiad.Diagrams.Class;

public class ClassDefinition
{
    public required string Id { get; init; }
    public string? DisplayName { get; set; }
    public List<ClassMember> Members { get; } = [];
    public List<ClassMethod> Methods { get; } = [];
    public ClassAnnotation? Annotation { get; set; }

    // An annotation outside the built-in set, such as <<Entity>>, as written.
    public string? AnnotationText { get; set; }

    // The `namespace` the class was declared in, if any.
    public string? Namespace { get; set; }

    public string Name => DisplayName ?? Id;
}