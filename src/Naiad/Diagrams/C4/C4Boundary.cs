namespace Naiad.Diagrams.C4;

public class C4Boundary
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public C4BoundaryType Type { get; set; } = C4BoundaryType.System;

    /// <summary>
    /// The type a <c>Boundary</c>, <c>Deployment_Node</c> or <c>Node</c> was given after its label, shown
    /// in the caption in place of the default for its <see cref="Type"/>.
    /// </summary>
    public string? TypeLabel { get; set; }
    public string? ParentBoundaryId { get; set; }
    public List<string> ElementIds { get; } = [];
    public List<string> ChildBoundaryIds { get; } = [];
}