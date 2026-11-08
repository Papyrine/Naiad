namespace Naiad.Diagrams.UseCase;

/// <summary>
/// A <c>json Id@{ ... }</c> declaration, drawn as a table titled with its identifier. The object is
/// flattened to one row per leaf, keyed by its path (<c>address.city</c>, <c>items[0].name</c>).
/// </summary>
public class JsonTable
{
    public required string Id { get; init; }

    public List<JsonRow> Rows { get; } = [];

    public List<string> Classes { get; } = [];

    /// <summary>Resolved visual overrides (from <c>classDef</c>/<c>class</c>/<c>style</c>), or null for theme defaults.</summary>
    public NodeStyle? Style { get; set; }
}
