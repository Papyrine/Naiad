using Naiad.Diagrams.Architecture;
using Naiad.Diagrams.Block;
using Naiad.Diagrams.GitGraph;
using Naiad.Diagrams.Kanban;
using Naiad.Diagrams.Radar;
using Naiad.Diagrams.Requirement;

// The chart-style diagrams' own syntax, checked against the model each parser builds.
public class ChartSyntaxTests
{
    [Test]
    public async Task Requirement_IndentedClosingBrace()
    {
        var model = new RequirementParser().Parse(
            "requirementDiagram\n\n    requirement test_req {\n    id: 1\n    text: the test text.\n    risk: high\n    verifymethod: test\n    }\n\n    element test_entity {\n    type: simulation\n    }\n\n    test_entity - satisfies -> test_req\n").Value;

        await Assert.That(model.Requirements.Single().Text).IsEqualTo("the test text.");
        await Assert.That(model.Requirements.Single().Risk).IsEqualTo(RiskLevel.High);
        await Assert.That(model.Elements.Single().Type).IsEqualTo("simulation");
        await Assert.That(model.Relations.Single().Type).IsEqualTo(RelationType.Satisfies);
    }

    [Test]
    public async Task Requirement_QuotedNamesValuesAndReverseRelation()
    {
        var model = new RequirementParser().Parse(
            """
            requirementDiagram
                direction LR
                requirement "My requirement":::important {
                    id: "1.1"
                    text: "quoted text"
                }
                element doc {
                    type: "word doc"
                }
                classDef important fill:#f96
                class doc important
                "My requirement" <- copies - doc
            """).Value;

        var requirement = model.Requirements.Single();
        await Assert.That(requirement.Name).IsEqualTo("My requirement");
        await Assert.That(requirement.Id).IsEqualTo("1.1");
        await Assert.That(requirement.Text).IsEqualTo("quoted text");
        await Assert.That(model.Elements.Single().Type).IsEqualTo("word doc");

        var relation = model.Relations.Single();
        await Assert.That(relation.Source).IsEqualTo("doc");
        await Assert.That(relation.Target).IsEqualTo("My requirement");
        await Assert.That(relation.Type).IsEqualTo(RelationType.Copies);
    }

    [Test]
    public async Task Requirement_UnclosedBlock_FailsTheParse() =>
        await Assert.That(new RequirementParser().Parse("requirementDiagram\n    requirement a {\n    id: 1\n").Success).IsFalse();

    [Test]
    public async Task Radar_AxisLabelsAndOptions()
    {
        var model = new RadarParser().Parse(
            """
            radar-beta
              axis m["Math"], s["Science"], plain
              axis h[History]
              curve a["Alice"]{85, 90, 80, 70}
              curve b{
                70, 75,
                85, 80
              }
              max 100
              min 10
              ticks 4
              graticule polygon
              showLegend false
            """).Value;

        await Assert.That(string.Join("|", model.Axes.Select(_ => $"{_.Id}:{_.Label}")))
            .IsEqualTo("m:Math|s:Science|plain:plain|h:History");
        await Assert.That(model.Curves[0].Label).IsEqualTo("Alice");
        await Assert.That(string.Join(",", model.Curves[1].Values)).IsEqualTo("70,75,85,80");
        await Assert.That(model.Max).IsEqualTo(100);
        await Assert.That(model.Min).IsEqualTo(10);
        await Assert.That(model.Ticks).IsEqualTo(4);
        await Assert.That(model.Graticule).IsEqualTo(GraticuleType.Polygon);
        await Assert.That(model.ShowLegend).IsFalse();
    }

    // Values named for their axis are put in axis order, even when the axes are declared afterwards.
    [Test]
    public async Task Radar_NamedValues_FollowTheAxes()
    {
        var model = new RadarParser().Parse(
            """
            radar-beta
              curve a{ c: 3, a: 1 }
              axis a, b, c
            """).Value;

        await Assert.That(string.Join(",", model.Curves.Single().Values)).IsEqualTo("1,0,3");
    }

    [Test]
    public async Task Kanban_ColumnsAndTasksByIndentation()
    {
        var model = new KanbanParser().Parse(
            """
            kanban
              Todo
                [Create Documentation]
                docs[Create Blog]
              [In progress]
                  deeper[Still a task]
              id9[Ready for deploy]
            """).Value;

        await Assert.That(string.Join("|", model.Columns.Select(_ => $"{_.Id}:{_.Name}:{_.Tasks.Count}")))
            .IsEqualTo("Todo:Todo:2|In progress:In progress:1|id9:Ready for deploy:0");
        await Assert.That(model.Columns[0].Tasks[0].Name).IsEqualTo("Create Documentation");
        await Assert.That(model.Columns[0].Tasks[1].Id).IsEqualTo("docs");
    }

    // Columns need not be indented at all, and tasks need not be indented by four.
    [Test]
    [Arguments("kanban\nTodo\n  [A task]\nDone\n  [Another]")]
    [Arguments("kanban\n\tTodo\n\t\t[A task]\n\tDone\n\t\t[Another]")]
    [Arguments("kanban\n      Todo\n        [A task]\n      Done\n        [Another]")]
    public async Task Kanban_AnyIndentation(string input)
    {
        var model = new KanbanParser().Parse(input).Value;

        await Assert.That(string.Join("|", model.Columns.Select(_ => $"{_.Name}:{_.Tasks.Count}"))).IsEqualTo("Todo:1|Done:1");
    }

    [Test]
    public async Task Kanban_TaskMetadata()
    {
        const string input =
            """
            kanban
              Todo
                id4[Create parsing tests]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }
                id3[Update DB function]@{ assigned: knsv, priority: "Very Low" }
                id2[No metadata]
            """;

        var tasks = new KanbanParser().Parse(input).Value.Columns.Single().Tasks;

        await Assert.That(tasks[0].Ticket).IsEqualTo("MC-2038");
        await Assert.That(tasks[0].Assigned).IsEqualTo("K.Sveidqvist");
        await Assert.That(tasks[0].Priority).IsEqualTo("High");
        await Assert.That(tasks[1].Assigned).IsEqualTo("knsv");
        await Assert.That(tasks[1].Priority).IsEqualTo("Very Low");
        await Assert.That(tasks[1].Ticket).IsNull();
        await Assert.That(tasks[2].Assigned).IsNull();

        var svg = Mermaid.Render(input);
        await Assert.That(svg).Contains("MC-2038");
        await Assert.That(svg).Contains("K.Sveidqvist");
    }

    [Test]
    public async Task Kanban_FrontMatterConfig_IsSkipped() =>
        await Assert.That(Mermaid.Render("---\nconfig:\n  kanban:\n    ticketBaseUrl: 'https://example.com/#TICKET#'\n---\nkanban\n  Todo\n    [A task]"))
            .Contains(">A task<");

    [Test]
    public async Task Block_NoColumns_IsOneRow()
    {
        var model = new BlockParser().Parse("block-beta\n  a b\n  c:2 d").Value;
        await Assert.That(model.Columns).IsEqualTo(0);

        // Five columns' worth of blocks side by side: every block has the same y.
        var svg = Mermaid.Render("block-beta\n  a b\n  c:2 d");
        var tops = Regex.Matches(svg, @"<rect[^>]* y=.([\d.]+).")
            .Select(_ => _.Groups[1].Value)
            .Distinct()
            .ToList();
        await Assert.That(tops.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Block_ColumnsAutoAndSpace()
    {
        var model = new BlockParser().Parse("block-beta\n  columns auto\n  a space b space:3 c").Value;

        await Assert.That(model.Columns).IsEqualTo(0);
        await Assert.That(string.Join(",", model.Elements.Select(_ => $"{_.Id}:{_.Span}:{_.IsSpace}")))
            .IsEqualTo("a:1:False,space:1:True,b:1:False,space:3:True,c:1:False");

        // A space is not drawn, and a block that only happens to be called space still is.
        await Assert.That(Mermaid.Render("block-beta\n  a space b")).DoesNotContain(">space<");
        await Assert.That(Mermaid.Render("block-beta\n  a space[\"space\"] b")).Contains(">space<");
    }

    [Test]
    [Arguments("db:L -- R:server", false, false, null)]
    [Arguments("db:L --> R:server", false, true, null)]
    [Arguments("db:L <-- R:server", true, false, null)]
    [Arguments("db:L <--> R:server", true, true, null)]
    [Arguments("db:L -[reads]- R:server", false, false, "reads")]
    [Arguments("db:L <-[reads and writes]-> R:server", true, true, "reads and writes")]
    [Arguments("<db:L -- R>:server", true, true, null)]
    public async Task Architecture_ArrowsOnTheLine(string edgeLine, bool sourceArrow, bool targetArrow, string? label)
    {
        var edge = new ArchitectureParser().Parse(
            $"architecture-beta\n    service db(database)[Database]\n    service server(server)[Server]\n    {edgeLine}").Value.Edges.Single();

        await Assert.That(edge.SourceId).IsEqualTo("db");
        await Assert.That(edge.TargetId).IsEqualTo("server");
        await Assert.That(edge.SourceSide).IsEqualTo(EdgeDirection.Left);
        await Assert.That(edge.TargetSide).IsEqualTo(EdgeDirection.Right);
        await Assert.That(edge.SourceArrow).IsEqualTo(sourceArrow);
        await Assert.That(edge.TargetArrow).IsEqualTo(targetArrow);
        await Assert.That(edge.Label).IsEqualTo(label);
    }

    // Commits step along the page for LR and down or up it for TB and BT.
    [Test]
    [Arguments("LR", true, false)]
    [Arguments("TB", false, false)]
    [Arguments("BT", false, true)]
    public async Task GitGraph_DirectionSetsTheAxisCommitsRunAlong(string direction, bool horizontal, bool upwards)
    {
        var svg = Mermaid.Render($"gitGraph {direction}:\n    commit id: \"first\"\n    commit id: \"second\"\n    commit id: \"third\"");

        var circles = Regex.Matches(svg, @"<circle cx=.([\d.]+). cy=.([\d.]+).")
            .Select(_ => (
                X: double.Parse(_.Groups[1].Value, CultureInfo.InvariantCulture),
                Y: double.Parse(_.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();

        await Assert.That(circles.Count).IsEqualTo(3);
        if (horizontal)
        {
            await Assert.That(circles[2].X).IsGreaterThan(circles[0].X);
            await Assert.That(circles[2].Y).IsEqualTo(circles[0].Y);
            return;
        }

        await Assert.That(circles[2].X).IsEqualTo(circles[0].X);
        if (upwards)
        {
            await Assert.That(circles[2].Y).IsLessThan(circles[0].Y);
            return;
        }

        await Assert.That(circles[2].Y).IsGreaterThan(circles[0].Y);
    }

    [Test]
    public async Task Pie_WholeSlice_IsAClosedDisc()
    {
        var svg = Mermaid.Render("pie\n    \"All\" : 100");

        // Two half circles, not an arc from a point back to itself.
        await Assert.That(svg).Contains("M0,-185A185,185,0,1,1,0,185A185,185,0,1,1,0,-185Z");
        await Assert.That(svg).Contains(">100%<");
    }

    [Test]
    public async Task Pie_EmptySection_HasNoSliceOrLabel()
    {
        var svg = Mermaid.Render("pie\n    \"All\" : 100\n    \"None\" : 0");

        await Assert.That(Regex.Matches(svg, "pieCircle").Count - Regex.Matches(svg, @"\.pieCircle").Count).IsEqualTo(1);
        await Assert.That(svg).DoesNotContain(">0%<");
        await Assert.That(svg).Contains("None");
    }
}
