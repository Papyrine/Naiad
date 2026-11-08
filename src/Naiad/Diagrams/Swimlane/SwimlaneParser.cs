class SwimlaneParser : IDiagramParser<SwimlaneModel>
{
    // Mermaid's detector accepts only `swimlane-beta`, matched case-sensitively, so there is no plain
    // `swimlane` spelling here either.
    static readonly Parser<SwimlaneModel> parser =
        FlowchartParser.CreateGrammar<SwimlaneModel>(Literals.Text("swimlane-beta"));

    public DiagramParseResult<SwimlaneModel> Parse(string input)
    {
        var result = parser.ParseDiagram(input);
        if (result.Success)
        {
            RemoveEmptiedLanes(result.Value);
        }

        return result;
    }

    /// <summary>
    /// A node listed in two lanes stays in the first. As in Mermaid, the second lane is then dropped when
    /// that left it with no nodes at all and no edge names it, since it would only be an empty band. A lane
    /// left empty on purpose is kept.
    /// </summary>
    static void RemoveEmptiedLanes(SwimlaneModel model) =>
        model.Subgraphs.RemoveAll(_ =>
            _.LostMember &&
            !HasNodes(_) &&
            !model.Edges.Exists(edge => edge.SourceId == _.Id || edge.TargetId == _.Id));

    static bool HasNodes(Subgraph subgraph) =>
        subgraph.NodeIds.Count > 0 ||
        subgraph.NestedSubgraphs.Exists(HasNodes);
}
