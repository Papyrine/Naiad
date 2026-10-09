using Naiad.Diagrams.Flowchart;

public class FlowchartLayoutTests
{
    static FlowchartModel Layout(string input)
    {
        var model = new FlowchartParser().Parse(input).Value;
        new FlowchartRenderer().Render(model, RenderOptions.Default);
        return model;
    }

    // Siblings used to come out mirrored: the last of the tied ordering sweeps won, and it is a
    // right-biased one.
    [Test]
    public async Task Siblings_AreLaidOutInDeclarationOrder()
    {
        var model = Layout(
            """
            flowchart TD
                A --> B
                A --> C
                A --> D
            """);

        var xs = new[] { "B", "C", "D" }.Select(_ => model.GetNode(_)!.Position.X).ToList();

        await Assert.That(xs[0]).IsLessThan(xs[1]);
        await Assert.That(xs[1]).IsLessThan(xs[2]);
    }

    [Test]
    public async Task Siblings_AreLaidOutInDeclarationOrder_LeftToRight()
    {
        var model = Layout(
            """
            flowchart LR
                A -->|Yes| B
                A -->|No| C
            """);

        await Assert.That(model.GetNode("B")!.Position.Y).IsLessThan(model.GetNode("C")!.Position.Y);
    }

    [Test]
    public async Task DiamondBranches_KeepDeclarationOrder()
    {
        var model = Layout(
            """
            flowchart TD
                A --> B
                A --> C
                B --> D
                C --> D
            """);

        await Assert.That(model.GetNode("B")!.Position.X).IsLessThan(model.GetNode("C")!.Position.X);
    }

    // Each of these used to throw KeyNotFoundException out of the layout: dagre cannot rank an edge that
    // ends on a cluster.
    [Test]
    [Arguments(
        "node to subgraph",
        """
        flowchart TB
            C[Gamma] --> two
            subgraph two
                B[Beta]
            end
        """)]
    [Arguments(
        "subgraph to node",
        """
        flowchart TB
            subgraph one
                A[Alpha]
            end
            one --> C[Gamma]
        """)]
    [Arguments(
        "subgraph to subgraph",
        """
        flowchart LR
            subgraph one
                A[Alpha]
            end
            subgraph two
                B[Beta]
            end
            one --> two
        """)]
    [Arguments(
        "nested subgraph",
        """
        flowchart TB
            subgraph outer
                subgraph inner
                    A[Alpha]
                end
            end
            C[Gamma] --> inner
            outer --> D[Delta]
        """)]
    [Arguments(
        "subgraph and its own member",
        """
        flowchart TB
            subgraph one
                A[Alpha] --> B[Beta]
            end
            A --> one
        """)]
    [Arguments(
        "empty subgraph",
        """
        flowchart TB
            subgraph one
            end
            A[Alpha] --> one
        """)]
    public async Task LinkToSubgraph_Renders(string name, string input)
    {
        var svg = Mermaid.Render(input);

        await Assert.That(svg).Contains("flowchart-link").Because(name);
        await Assert.That(svg).DoesNotContain("NaN").Because(name);
    }

    [Test]
    public async Task LinkToSubgraph_DoesNotAddANodeForTheSubgraph()
    {
        var model = new FlowchartParser().Parse(
            """
            flowchart TB
                C[Gamma] --> two
                subgraph two [Second]
                    B[Beta]
                end
                two --> D[Delta]
            """).Value;

        await Assert.That(string.Join(",", model.Nodes.Select(_ => _.Id))).IsEqualTo("C,B,D");
        await Assert.That(string.Join(",", model.Subgraphs.Single().NodeIds)).IsEqualTo("B");
        await Assert.That(string.Join(",", model.Edges.Select(_ => $"{_.SourceId}>{_.TargetId}")))
            .IsEqualTo("C>two,two>D");
    }

    // The edge is laid out against a node inside the subgraph, then cut back so it stops at the box.
    [Test]
    public async Task LinkToSubgraph_EndsOnTheSubgraphBorder()
    {
        var model = Layout(
            """
            flowchart TB
                C[Gamma] --> two
                subgraph two
                    B[Beta]
                end
                two --> D[Delta]
            """);

        var box = model.Subgraphs.Single().Bounds;
        var into = model.Edges[0].Points[^1];
        var outOf = model.Edges[1].Points[0];

        await Assert.That(Math.Abs(into.Y - box.Top)).IsLessThan(0.01);
        await Assert.That(into.X).IsBetween(box.Left, box.Right);
        await Assert.That(Math.Abs(outOf.Y - box.Bottom)).IsLessThan(0.01);
        await Assert.That(outOf.X).IsBetween(box.Left, box.Right);
        await Assert.That(model.Edges[0].Points.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(model.Edges[1].Points.Count).IsGreaterThanOrEqualTo(2);
    }
}
