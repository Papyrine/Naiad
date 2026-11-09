/// <summary>How one part of a node's shape is painted, given the node's fill and stroke.</summary>
enum ShapePaint
{
    /// <summary>Filled with the node's fill and outlined with its stroke.</summary>
    Body,

    /// <summary>Filled, with no outline: the backing behind a brace.</summary>
    Fill,

    /// <summary>Outlined, with no fill: a brace, a rim, a rule.</summary>
    Stroke,

    /// <summary>Filled and outlined in the node's stroke colour: a junction, a window's buttons.</summary>
    Solid,

    /// <summary>Neither filled nor outlined unless the node's own style asks for it: a text block.</summary>
    Hidden
}
