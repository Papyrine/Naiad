public class FlowchartParserTests
{
    [Test]
    public async Task Simple_ReturnsNodes()
    {
        const string input =
            """
            flowchart LR
                A[Start] --> B[End]
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Nodes.Count).IsEqualTo(2);
        await Assert.That(result.Value.Edges.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Direction_ParsesDirection()
    {
        const string input =
            """
            flowchart TD
                A --> B
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Direction).IsEqualTo(Direction.TopToBottom);
    }

    [Test]
    public async Task RoundedNodes_ParsesShape()
    {
        const string input =
            """
            flowchart LR
                A(Rounded)
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Nodes[0].Shape).IsEqualTo(NodeShape.RoundedRectangle);
        await Assert.That(result.Value.Nodes[0].Label).IsEqualTo("Rounded");
    }

    [Test]
    public async Task Diamond_ParsesShape()
    {
        const string input =
            """
            flowchart LR
                A{Decision}
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Nodes[0].Shape).IsEqualTo(NodeShape.Diamond);
    }

    [Test]
    public async Task Circle_ParsesShape()
    {
        const string input =
            """
            flowchart LR
                A((Circle))
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Nodes[0].Shape).IsEqualTo(NodeShape.Circle);
    }

    [Test]
    public async Task ChainedNodes_CreatesMultipleEdges()
    {
        const string input =
            """
            flowchart LR
                A --> B --> C --> D
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Nodes.Count).IsEqualTo(4);
        await Assert.That(result.Value.Edges.Count).IsEqualTo(3);
    }

    [Test]
    public async Task DottedArrow_ParsesEdgeStyle()
    {
        const string input =
            """
            flowchart LR
                A -.-> B
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Edges[0].LineStyle).IsEqualTo(EdgeStyle.Dotted);
    }

    [Test]
    public async Task ThickArrow_ParsesEdgeStyle()
    {
        const string input =
            """
            flowchart LR
                A ==> B
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Edges[0].LineStyle).IsEqualTo(EdgeStyle.Thick);
    }

    [Test]
    public async Task InlineEdgeLabel_ParsesLabelAndType()
    {
        const string input =
            """
            flowchart LR
                A -- yes --> B
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Edges.Count).IsEqualTo(1);
        await Assert.That(result.Value.Edges[0].Type).IsEqualTo(EdgeType.Arrow);
        await Assert.That(result.Value.Edges[0].Label).IsEqualTo("yes");
    }

    [Test]
    public async Task InlineDottedLabel_ParsesLabelAndType()
    {
        const string input =
            """
            flowchart LR
                A -. cache .-> B
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Edges[0].Type).IsEqualTo(EdgeType.DottedArrow);
        await Assert.That(result.Value.Edges[0].LineStyle).IsEqualTo(EdgeStyle.Dotted);
        await Assert.That(result.Value.Edges[0].Label).IsEqualTo("cache");
    }

    [Test]
    public async Task InlineThickLabel_ParsesLabelAndType()
    {
        const string input =
            """
            flowchart LR
                A == call ==> B
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Edges[0].Type).IsEqualTo(EdgeType.ThickArrow);
        await Assert.That(result.Value.Edges[0].LineStyle).IsEqualTo(EdgeStyle.Thick);
        await Assert.That(result.Value.Edges[0].Label).IsEqualTo("call");
    }

    [Test]
    public async Task Parallelogram_ParsesShape()
    {
        const string input =
            """
            flowchart LR
                A[/Validate/]
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Nodes[0].Shape).IsEqualTo(NodeShape.Parallelogram);
        await Assert.That(result.Value.Nodes[0].Label).IsEqualTo("Validate");
    }

    [Test]
    public async Task ClassShorthand_IsIgnoredButNodeKept()
    {
        const string input =
            """
            flowchart LR
                A[Start]:::highlight --> B
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Nodes.Count).IsEqualTo(2);
        await Assert.That(result.Value.Nodes[0].Label).IsEqualTo("Start");
        await Assert.That(result.Value.Edges.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DirectionInsideSubgraph_SetsDirectionWithoutTruncating()
    {
        const string input =
            """
            flowchart TB
                subgraph s [Sub]
                    direction LR
                    A --> B
                end
                B --> C
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Subgraphs[0].Direction).IsEqualTo(Direction.LeftToRight);
        // The statements after `direction` (and after the subgraph) must still be parsed - no truncation.
        await Assert.That(result.Value.Nodes.Count).IsEqualTo(3);
        await Assert.That(result.Value.Edges.Count).IsEqualTo(2);
    }

    [Test]
    public async Task IgnoredDirectives_AreSkippedNotTruncating()
    {
        const string input =
            """
            flowchart LR
                accTitle: A title
                accDescr {
                    A description
                    over two lines
                }
                A --> B
                linkStyle default stroke:#999
                click A "https://example.com"
                B --> C
            """;

        var parser = new FlowchartParser();
        var result = parser.Parse(input);

        // Directives with no effect on the drawing are skipped; the statements around them still parse.
        await Assert.That(result.Success).IsTrue();
        await Assert.That(string.Join(",", result.Value.Nodes.Select(_ => _.Id))).IsEqualTo("A,B,C");
        await Assert.That(result.Value.Edges.Count).IsEqualTo(2);
    }

    // A line no rule matches used to be dropped without a word, taking its nodes and edges with it.
    [Test]
    [Arguments("    this is not a statement")]
    [Arguments("this is not a statement")]
    [Arguments("    B -->")]
    [Arguments("    B ~~ C")]
    public async Task UnknownLine_FailsTheParse(string line)
    {
        var input = $"flowchart LR\n    A --> B\n{line}\n    B --> C";

        var result = new FlowchartParser().Parse(input);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(() => Mermaid.Render(input)).Throws<MermaidParseException>();
    }

    [Test]
    public async Task ArrowsWithoutSpaces_SplitIntoNodesAndLinks()
    {
        const string input =
            """
            flowchart LR
                A-->B
                B-.->C
                C==>D
                D---E
                my-node-->other-node
            """;

        var result = new FlowchartParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(string.Join(",", result.Value.Nodes.Select(_ => _.Id)))
            .IsEqualTo("A,B,C,D,E,my-node,other-node");
        await Assert.That(string.Join(",", result.Value.Edges.Select(_ => _.Type)))
            .IsEqualTo("Arrow,DottedArrow,ThickArrow,Open,Arrow");
    }

    [Test]
    public async Task TrailingWhitespace_DoesNotDropTheLine()
    {
        const string input = "flowchart LR  \n    A[Alpha] --> B[Beta]   \n    subgraph one \t\n        B --> C \n    end  \n";

        var result = new FlowchartParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Nodes.Count).IsEqualTo(3);
        await Assert.That(result.Value.Nodes[0].Label).IsEqualTo("Alpha");
        await Assert.That(result.Value.Edges.Count).IsEqualTo(2);
        await Assert.That(result.Value.Subgraphs[0].Id).IsEqualTo("one");
    }

    [Test]
    public async Task Ampersand_LinksEveryNodeOfBothGroups()
    {
        const string input =
            """
            flowchart TD
                A & B --> C & D
            """;

        var result = new FlowchartParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(string.Join(",", result.Value.Nodes.Select(_ => _.Id))).IsEqualTo("A,B,C,D");
        await Assert.That(string.Join(",", result.Value.Edges.Select(_ => $"{_.SourceId}>{_.TargetId}")))
            .IsEqualTo("A>C,A>D,B>C,B>D");
    }

    [Test]
    public async Task Semicolons_TerminateAndSeparateStatements()
    {
        const string input =
            """
            graph TD;
                A-->B;
                B-->C; C-->D;
            """;

        var result = new FlowchartParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(string.Join(",", result.Value.Nodes.Select(_ => _.Id))).IsEqualTo("A,B,C,D");
        await Assert.That(result.Value.Edges.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments("-->", EdgeType.Arrow, EdgeStyle.Solid, 1)]
    [Arguments("--->", EdgeType.Arrow, EdgeStyle.Solid, 2)]
    [Arguments("---->", EdgeType.Arrow, EdgeStyle.Solid, 3)]
    [Arguments("---", EdgeType.Open, EdgeStyle.Solid, 1)]
    [Arguments("-----", EdgeType.Open, EdgeStyle.Solid, 3)]
    [Arguments("---o", EdgeType.CircleEnd, EdgeStyle.Solid, 2)]
    [Arguments("--x", EdgeType.CrossEnd, EdgeStyle.Solid, 1)]
    [Arguments("-.->", EdgeType.DottedArrow, EdgeStyle.Dotted, 1)]
    [Arguments("-..->", EdgeType.DottedArrow, EdgeStyle.Dotted, 2)]
    [Arguments("-.-", EdgeType.Dotted, EdgeStyle.Dotted, 1)]
    [Arguments("==>", EdgeType.ThickArrow, EdgeStyle.Thick, 1)]
    [Arguments("===>", EdgeType.ThickArrow, EdgeStyle.Thick, 2)]
    [Arguments("====", EdgeType.Thick, EdgeStyle.Thick, 2)]
    [Arguments("<-->", EdgeType.BiDirectional, EdgeStyle.Solid, 1)]
    [Arguments("<-.->", EdgeType.BiDirectional, EdgeStyle.Dotted, 1)]
    [Arguments("<==>", EdgeType.BiDirectional, EdgeStyle.Thick, 1)]
    [Arguments("o--o", EdgeType.BiDirectionalCircle, EdgeStyle.Solid, 1)]
    [Arguments("x--x", EdgeType.BiDirectionalCross, EdgeStyle.Solid, 1)]
    [Arguments("~~~", EdgeType.Invisible, EdgeStyle.Solid, 1)]
    [Arguments("~~~~", EdgeType.Invisible, EdgeStyle.Solid, 2)]
    public async Task Link_ParsesTypeStyleAndLength(string link, EdgeType type, EdgeStyle style, int length)
    {
        var result = new FlowchartParser().Parse($"flowchart LR\n    A {link} B");

        await Assert.That(result.Success).IsTrue();
        var edge = result.Value.Edges.Single();
        await Assert.That(edge.Type).IsEqualTo(type);
        await Assert.That(edge.LineStyle).IsEqualTo(style);
        await Assert.That(edge.MinLength).IsEqualTo(length);
    }

    [Test]
    public async Task InlineLabelOnLongLink_KeepsLabelAndLength()
    {
        const string input =
            """
            flowchart LR
                A -- two ranks ---> B
                B -. dotted ..-> C
            """;

        var result = new FlowchartParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Edges[0].Label).IsEqualTo("two ranks");
        await Assert.That(result.Value.Edges[0].MinLength).IsEqualTo(2);
        await Assert.That(result.Value.Edges[1].Label).IsEqualTo("dotted");
        await Assert.That(result.Value.Edges[1].MinLength).IsEqualTo(2);
    }

    [Test]
    public async Task LongLink_SpansMoreRanks()
    {
        static double Gap(string link)
        {
            var model = new FlowchartParser().Parse($"flowchart TD\n    A {link} B").Value;
            new Naiad.Diagrams.Flowchart.FlowchartRenderer().Render(model, RenderOptions.Default);
            return model.Nodes[1].Position.Y - model.Nodes[0].Position.Y;
        }

        await Assert.That(Gap("---->")).IsGreaterThan(Gap("-->") * 2);
    }

    [Test]
    public async Task InvisibleLink_IsLaidOutButNotDrawn()
    {
        const string input =
            """
            flowchart LR
                A ~~~ B
            """;

        var svg = Mermaid.Render(input);

        await Assert.That(svg).Contains(">A<");
        await Assert.That(svg).Contains(">B<");
        await Assert.That(svg).DoesNotContain("class='flowchart-link'");
    }

    [Test]
    [Arguments("subgraph one", "one", "one")]
    [Arguments("subgraph one [Label]", "one", "Label")]
    [Arguments("subgraph one[\"Quoted Label\"]", "one", "Quoted Label")]
    [Arguments("subgraph \"Quoted Title\"", "subGraph0", "Quoted Title")]
    [Arguments("subgraph Two Words", "subGraph0", "Two Words")]
    public async Task SubgraphHeader_ParsesIdAndTitle(string header, string id, string title)
    {
        var input = $"flowchart TB\n    {header}\n        A --> B\n    end\n    B --> C";

        var result = new FlowchartParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        var subgraph = result.Value.Subgraphs.Single();
        await Assert.That(subgraph.Id).IsEqualTo(id);
        await Assert.That(subgraph.Title).IsEqualTo(title);
        await Assert.That(string.Join(",", subgraph.NodeIds)).IsEqualTo("A,B");
        await Assert.That(result.Value.Nodes.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments("A[\"Text (with) [brackets]\"]", "Text (with) [brackets]", NodeShape.Rectangle)]
    [Arguments("A(\"Round (x)\")", "Round (x)", NodeShape.RoundedRectangle)]
    [Arguments("A([\"Stadium ]\"])", "Stadium ]", NodeShape.Stadium)]
    [Arguments("A[[\"Sub ]]\"]]", "Sub ]]", NodeShape.Subroutine)]
    [Arguments("A[(\"Data )\")]", "Data )", NodeShape.Cylinder)]
    [Arguments("A((\"Circle )\"))", "Circle )", NodeShape.Circle)]
    [Arguments("A(((\"Double )\")))", "Double )", NodeShape.DoubleCircle)]
    [Arguments("A{\"Is it }?\"}", "Is it }?", NodeShape.Diamond)]
    [Arguments("A{{\"Hex }\"}}", "Hex }", NodeShape.Hexagon)]
    [Arguments("A>\"Flag ]\"]", "Flag ]", NodeShape.Asymmetric)]
    [Arguments("A[/\"Lean / in\"/]", "Lean / in", NodeShape.Parallelogram)]
    [Arguments("A[ \"Padded\" ]", "Padded", NodeShape.Rectangle)]
    // Quotes that do not wrap the whole label are part of it.
    [Arguments("A[say \"hi\" twice]", "say \"hi\" twice", NodeShape.Rectangle)]
    public async Task QuotedLabel_DropsQuotesAndKeepsClosingCharacters(string node, string label, NodeShape shape)
    {
        var result = new FlowchartParser().Parse($"flowchart LR\n    {node} --> B");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Nodes[0].Label).IsEqualTo(label);
        await Assert.That(result.Value.Nodes[0].Shape).IsEqualTo(shape);
        await Assert.That(result.Value.Nodes.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments("A -->|\"quoted\"| B")]
    [Arguments("A --> |\"quoted\"| B")]
    [Arguments("A -- \"quoted\" --> B")]
    public async Task QuotedEdgeLabel_DropsQuotes(string statement)
    {
        var result = new FlowchartParser().Parse($"flowchart LR\n    {statement}");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Edges.Single().Label).IsEqualTo("quoted");
    }

    // `o--o` and `x--x` used to be drawn as `<-->`, with an arrowhead at each end.
    [Test]
    [Arguments("<-->", "pointStart", "pointEnd")]
    [Arguments("o--o", "circleStart", "circleEnd")]
    [Arguments("x--x", "crossStart", "crossEnd")]
    public async Task TwoHeadedLink_DrawsItsOwnHeadAtBothEnds(string link, string start, string end)
    {
        var svg = Mermaid.Render($"flowchart LR\n    A {link} B");

        await Assert.That(svg).Contains($"marker-start='url(#naiad_flowchart-{start})'");
        await Assert.That(svg).Contains($"marker-end='url(#naiad_flowchart-{end})'");
    }

    [Test]
    [Arguments("--o", "circleEnd")]
    [Arguments("--x", "crossEnd")]
    [Arguments("-->", "pointEnd")]
    public async Task OneHeadedLink_HasNoStartMarker(string link, string end)
    {
        var svg = Mermaid.Render($"flowchart LR\n    A {link} B");

        await Assert.That(svg).DoesNotContain("marker-start=");
        await Assert.That(svg).Contains($"marker-end='url(#naiad_flowchart-{end})'");
    }

    [Test]
    public async Task LineBreak_GrowsTheNodeAndIsNotShownAsText()
    {
        static Node First(string label)
        {
            var model = new FlowchartParser().Parse($"flowchart LR\n    A[{label}] --> B").Value;
            new Naiad.Diagrams.Flowchart.FlowchartRenderer().Render(model, RenderOptions.Default);
            return model.Nodes[0];
        }

        var oneLine = First("Line one");
        var threeLines = First("Line one<br/>Line two<br>x");

        await Assert.That(threeLines.Width).IsEqualTo(oneLine.Width);
        await Assert.That(threeLines.Height).IsGreaterThan(oneLine.Height);

        var svg = Mermaid.Render("flowchart LR\n    A[Line one<br/>Line two] -->|first<br>second| B");
        await Assert.That(svg).Contains("<p>Line one<br/>Line two</p>");
        await Assert.That(svg).Contains("<p>first<br/>second</p>");
        await Assert.That(svg).DoesNotContain("&lt;br");

        var plain = Mermaid.Render(
            "flowchart LR\n    A[Line one<br/>Line two] --> B",
            new() { AllowHtmlElements = false });
        await Assert.That(plain).Contains(">Line one</text>");
        await Assert.That(plain).Contains(">Line two</text>");
        await Assert.That(plain).DoesNotContain("&lt;br");
    }

    [Test]
    public async Task ShapeAttributes_SetShapeAndLabel()
    {
        const string input =
            """
            flowchart TD
                A@{ shape: cyl, label: "Orders, live }" } --> B@{ shape: diam, label: Check }
                B --> C@{ shape: no-such-shape, label: "Still drawn" }
            """;

        var result = new FlowchartParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        var nodes = result.Value.Nodes;
        await Assert.That(nodes[0].Shape).IsEqualTo(NodeShape.Cylinder);
        await Assert.That(nodes[0].Label).IsEqualTo("Orders, live }");
        await Assert.That(nodes[1].Shape).IsEqualTo(NodeShape.Diamond);
        await Assert.That(nodes[1].Label).IsEqualTo("Check");
        await Assert.That(nodes[2].Shape).IsEqualTo(NodeShape.Rectangle);
        await Assert.That(nodes[2].Label).IsEqualTo("Still drawn");
    }
}
