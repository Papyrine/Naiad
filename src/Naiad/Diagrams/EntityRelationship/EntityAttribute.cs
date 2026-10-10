namespace Naiad.Diagrams.EntityRelationship;

public class EntityAttribute
{
    public required string Name { get; init; }
    public string? Type { get; set; }

    /// <summary>The attribute's key, or the first of them when it has several.</summary>
    public AttributeKeyType KeyType { get; set; } = AttributeKeyType.None;

    /// <summary>Every key the attribute carries, in the order written: <c>PK, FK</c>.</summary>
    public List<AttributeKeyType> KeyTypes { get; } = [];
    public string? Comment { get; set; }
}