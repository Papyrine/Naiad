namespace Naiad.Diagrams.EntityRelationship;

public class Entity
{
    public required string Name { get; init; }

    /// <summary>
    /// The display text of <c>Name["alias"]</c>, shown in place of <see cref="Name"/>. Relationships
    /// still refer to the entity by name.
    /// </summary>
    public string? Alias { get; set; }
    public List<EntityAttribute> Attributes { get; } = [];

    // Layout properties
    public Position Position { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}