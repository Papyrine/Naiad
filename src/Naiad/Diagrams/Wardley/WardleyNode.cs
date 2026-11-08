namespace Naiad.Diagrams.Wardley;

public class WardleyNode
{
    /// <summary>
    /// The name links refer to it by. A pipeline component's is its pipeline's name and its own joined by
    /// an underscore, as Mermaid makes it, so the same name can appear in two pipelines.
    /// </summary>
    public required string Id { get; init; }

    public required string Label { get; set; }

    public WardleyNodeKind Kind { get; set; }

    public double Visibility { get; set; }

    public double Evolution { get; set; }

    /// <summary>Where the label goes relative to the node, in pixels, from <c>label [x, y]</c>.</summary>
    public double? LabelOffsetX { get; set; }

    public double? LabelOffsetY { get; set; }

    public bool Inertia { get; set; }

    public WardleySourceStrategy? SourceStrategy { get; set; }

    /// <summary>Whether a pipeline hangs off this node, which draws it as a square on top of the pipeline.</summary>
    public bool IsPipelineParent { get; set; }
}
