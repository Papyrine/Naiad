namespace Naiad.Diagrams.Venn;

/// <summary>A label placed inside the region of a set or union.</summary>
public class VennTextNode
{
    /// <summary>The identifiers of the set or union it sits in, in ordinal order.</summary>
    public required IReadOnlyList<string> Sets { get; init; }

    /// <summary>The node's own identifier, which a <c>style</c> statement can target.</summary>
    public required string Id { get; init; }

    /// <summary>The display text; the identifier is shown when there is none.</summary>
    public string? Label { get; init; }

    public string Key => string.Join('|', Sets);
}
