namespace Naiad.Diagrams.Wardley;

/// <summary>
/// A Wardley map. Every position is held as a fraction from 0 to 1: visibility runs up the map, evolution
/// across it. Mermaid also accepts percentages (above 1, up to 100), which the parser scales down.
/// </summary>
public class WardleyModel : DiagramBase
{
    /// <summary>The canvas size from <c>size [width, height]</c>, when given.</summary>
    public double? Width { get; set; }

    public double? Height { get; set; }

    /// <summary>The evolution stages named by <c>evolution</c>; empty for Mermaid's four default stages.</summary>
    public List<string> Stages { get; } = [];

    /// <summary>
    /// Where each stage ends, from <c>Stage@0.25</c>. Only used when every stage has one; otherwise the
    /// stages share the axis equally.
    /// </summary>
    public List<double> StageBoundaries { get; } = [];

    /// <summary>The anchors, components and pipeline components, in the order Mermaid draws them.</summary>
    public List<WardleyNode> Nodes { get; } = [];

    public List<WardleyLink> Links { get; } = [];

    public List<WardleyTrend> Trends { get; } = [];

    public List<WardleyPipeline> Pipelines { get; } = [];

    public List<WardleyNote> Notes { get; } = [];

    public List<WardleyAnnotation> Annotations { get; } = [];

    /// <summary>Where the box listing the annotations' texts goes, from <c>annotations [v, e]</c>.</summary>
    public (double Visibility, double Evolution)? AnnotationsBox { get; set; }

    public List<WardleyAccelerator> Accelerators { get; } = [];

    public List<WardleyAccelerator> Deaccelerators { get; } = [];
}
