namespace Naiad.Diagrams.Class;

/// <summary>
/// The glyph drawn at one end of a class relationship. Mermaid writes a marker on each side of the
/// line independently (<c>&lt;|--</c>, <c>--|&gt;</c>, <c>*--</c>, <c>--o</c>, <c>&lt;|--|&gt;</c>), so each
/// end carries its own marker rather than the pair being implied by a single relationship kind.
/// </summary>
public enum RelationshipMarker
{
    None,
    /// <summary>Hollow triangle: inheritance (solid line) or realization (dashed line).</summary>
    Triangle,
    /// <summary>Filled diamond, on the "whole" end of a composition.</summary>
    FilledDiamond,
    /// <summary>Hollow diamond, on the "whole" end of an aggregation.</summary>
    HollowDiamond,
    /// <summary>Open arrowhead: association (solid line) or dependency (dashed line).</summary>
    Arrow,
    /// <summary>Circle on a stalk: the interface a class provides, written <c>()--</c> or <c>--()</c>.</summary>
    Lollipop
}
