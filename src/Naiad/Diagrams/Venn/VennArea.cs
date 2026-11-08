namespace Naiad.Diagrams.Venn;

/// <summary>A set (one identifier) or a union, the overlap of two or more sets.</summary>
public class VennArea
{
    /// <summary>The identifiers of the sets this area belongs to, in ordinal order.</summary>
    public required IReadOnlyList<string> Sets { get; init; }

    /// <summary>The display label; a set without one shows its identifier, a union without one shows nothing.</summary>
    public string? Label { get; init; }

    /// <summary>The relative size the area is drawn at. Mermaid defaults it to 10 / n², n being the number of sets.</summary>
    public double Size { get; init; }

    public bool IsSet => Sets.Count == 1;

    /// <summary>The key styles and text nodes are matched on: the identifiers joined with <c>|</c>.</summary>
    public string Key => string.Join('|', Sets);
}
