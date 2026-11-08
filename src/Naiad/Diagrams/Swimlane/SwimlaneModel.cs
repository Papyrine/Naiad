namespace Naiad.Diagrams.Swimlane;

/// <summary>
/// A swimlane diagram: a flowchart whose top-level subgraphs are drawn as lanes. Mermaid parses it with the
/// flowchart grammar and only lays it out differently, so the model is the flowchart's.
/// </summary>
public class SwimlaneModel : FlowchartModel;
