namespace Naiad.Diagrams.Venn;

/// <summary>
/// A <c>style</c> statement. Its targets, sorted, name a set, a union or a text node; the properties
/// Mermaid uses are <c>fill</c>, <c>color</c>, <c>stroke</c>, <c>stroke-width</c> and <c>fill-opacity</c>.
/// </summary>
public class VennStyle
{
    public required IReadOnlyList<string> Targets { get; init; }

    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Key => string.Join('|', Targets);
}
