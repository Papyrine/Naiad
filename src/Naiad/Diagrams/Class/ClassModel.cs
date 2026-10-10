namespace Naiad.Diagrams.Class;

public class ClassModel : DiagramBase
{
    public List<ClassDefinition> Classes { get; } = [];
    public List<ClassRelationship> Relationships { get; } = [];
    public List<ClassNote> Notes { get; } = [];
}