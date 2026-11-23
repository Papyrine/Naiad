namespace Naiad;

/// <summary>
/// Caps on what one render may take on, so that a host rendering diagrams it does not control cannot be made
/// to spend minutes, or all its memory, on one. They are on by default, with room well beyond any diagram
/// written to be read; a diagram over one is turned down with a <see cref="MermaidLimitException"/> before
/// the work it would cost is done. Use <see cref="Unlimited"/> for input that is trusted.
/// </summary>
/// <remarks>
/// The graph caps bound the layout, which is what grows fastest: laying out 1,000 nodes joined by 2,000 edges
/// takes seconds, and twice that many takes most of a minute. Mermaid itself turns a flowchart down past 500
/// edges and 50,000 characters.
/// </remarks>
public sealed class RenderLimits
{
    /// <summary>The caps a render has unless told otherwise.</summary>
    public static RenderLimits Default => new();

    /// <summary>No caps at all, for input that is trusted.</summary>
    public static RenderLimits Unlimited => new()
    {
        MaxInputLength = int.MaxValue,
        MaxNodes = int.MaxValue,
        MaxEdges = int.MaxValue,
        MaxSubgraphDepth = int.MaxValue
    };

    /// <summary>The most characters the diagram text may run to, front matter and directives included.</summary>
    public int MaxInputLength { get; set; } = 1_000_000;

    /// <summary>The most nodes a graph laid out by layers (flowchart, class, ER, state and the like) may hold.</summary>
    public int MaxNodes { get; set; } = 5_000;

    /// <summary>The most edges a graph laid out by layers may hold.</summary>
    public int MaxEdges { get; set; } = 2_000;

    /// <summary>
    /// How deep subgraphs, and the composite states, namespaces and boundaries laid out as subgraphs, may nest
    /// in a graph laid out by layers. The layout's cost grows with the cube of the depth.
    /// </summary>
    public int MaxSubgraphDepth { get; set; } = 100;
}
