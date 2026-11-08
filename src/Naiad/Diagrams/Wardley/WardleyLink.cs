namespace Naiad.Diagrams.Wardley;

public class WardleyLink
{
    /// <summary>The <see cref="WardleyNode.Id"/> of the node the link starts at.</summary>
    public required string Source { get; init; }

    public required string Target { get; init; }

    public bool Dashed { get; init; }

    public string? Label { get; init; }

    public WardleyFlow Flow { get; init; }
}
