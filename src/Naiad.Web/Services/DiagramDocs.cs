namespace Naiad.Web.Services;

/// <summary>A friendly diagram-type name paired with its page on the Mermaid syntax reference.</summary>
public record DiagramDocsLink(string Name, string Url);

/// <summary>
/// Resolves the diagram type of the editor's current Mermaid source to a display name and the matching
/// page on mermaid.js.org, so the editor can deep-link to the syntax reference for whatever is being typed.
/// URLs mirror the diagrams Naiad detects in <see cref="Mermaid.TryDetectType"/>.
/// </summary>
public static class DiagramDocs
{
    const string baseUrl = "https://mermaid.js.org/syntax/";

    /// <summary>The docs link for <paramref name="source"/>, or null when no diagram type is recognised.</summary>
    public static DiagramDocsLink? For(string? source)
    {
        if (Mermaid.TryDetectType(source, out var type))
        {
            return For(type);
        }

        return null;
    }

    public static DiagramDocsLink For(DiagramType type) =>
        type switch
        {
            DiagramType.Flowchart => new("Flowchart", $"{baseUrl}flowchart.html"),
            DiagramType.Sequence => new("Sequence", $"{baseUrl}sequenceDiagram.html"),
            DiagramType.Class => new("Class", $"{baseUrl}classDiagram.html"),
            DiagramType.State => new("State", $"{baseUrl}stateDiagram.html"),
            DiagramType.EntityRelationship => new("Entity Relationship", $"{baseUrl}entityRelationshipDiagram.html"),
            DiagramType.Gantt => new("Gantt", $"{baseUrl}gantt.html"),
            DiagramType.Pie => new("Pie", $"{baseUrl}pie.html"),
            DiagramType.GitGraph => new("Git Graph", $"{baseUrl}gitgraph.html"),
            DiagramType.Mindmap => new("Mindmap", $"{baseUrl}mindmap.html"),
            DiagramType.Timeline => new("Timeline", $"{baseUrl}timeline.html"),
            DiagramType.C4Context => new("C4 Context", $"{baseUrl}c4.html"),
            DiagramType.C4Container => new("C4 Container", $"{baseUrl}c4.html"),
            DiagramType.C4Component => new("C4 Component", $"{baseUrl}c4.html"),
            DiagramType.C4Deployment => new("C4 Deployment", $"{baseUrl}c4.html"),
            DiagramType.C4Dynamic => new("C4 Dynamic", $"{baseUrl}c4.html"),
            DiagramType.Block => new("Block", $"{baseUrl}block.html"),
            DiagramType.Kanban => new("Kanban", $"{baseUrl}kanban.html"),
            DiagramType.Quadrant => new("Quadrant", $"{baseUrl}quadrantChart.html"),
            DiagramType.Requirement => new("Requirement", $"{baseUrl}requirementDiagram.html"),
            DiagramType.Sankey => new("Sankey", $"{baseUrl}sankey.html"),
            DiagramType.UserJourney => new("User Journey", $"{baseUrl}userJourney.html"),
            DiagramType.XYChart => new("XY Chart", $"{baseUrl}xyChart.html"),
            DiagramType.Architecture => new("Architecture", $"{baseUrl}architecture.html"),
            DiagramType.Packet => new("Packet", $"{baseUrl}packet.html"),
            DiagramType.Radar => new("Radar", $"{baseUrl}radar.html"),
            DiagramType.Treemap => new("Treemap", $"{baseUrl}treemap.html"),
            DiagramType.ZenUML => new("ZenUML", $"{baseUrl}zenuml.html"),
            _ => new(type.ToString(), baseUrl)
        };
}
