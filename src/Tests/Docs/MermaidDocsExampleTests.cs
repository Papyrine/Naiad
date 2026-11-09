using Naiad.Diagrams.Sankey;

/// <summary>
/// Examples from Mermaid 12.3.0's own syntax pages (mermaid.js.org/syntax) that Naiad rendered
/// differently from Mermaid, or failed to read.
/// </summary>
public class MermaidDocsExampleTests : TestBase
{
    // The front matter's title was copied into the model, and drawn by some renderers but not these.
    [Test]
    [Arguments("flowchart LR\n    id")]
    [Arguments("classDiagram\n    class Duck")]
    [Arguments("stateDiagram-v2\n    [*] --> Still")]
    [Arguments("erDiagram\n    CUSTOMER ||--o{ ORDER : places")]
    [Arguments("gitGraph\n    commit")]
    public async Task FrontMatterTitle_IsDrawn(string diagram)
    {
        var svg = Mermaid.Render($"---\ntitle: Example title\n---\n{diagram}");

        await Assert.That(svg).Contains(">Example title<");
    }

    [Test]
    public Task FrontMatterTitle_Flowchart() =>
        VerifySvg(
            """
            ---
            title: Node with text
            ---
            flowchart LR
                id1[This is the text in the box]
            """);

    // `#quot;` and `#9829;` were shown as written.
    [Test]
    public async Task EntityCodes_AreDecoded()
    {
        var flowchart = Mermaid.Render(
            """
            flowchart LR
                A["A double quote:#quot;"] --> B["A dec char:#9829;"]
            """);
        var sequence = Mermaid.Render(
            """
            sequenceDiagram
                Alice->>John: I #9829; you!
                John->>Alice: I #9829; you #infin; times more!
            """);

        await Assert.That(flowchart).Contains("A dec char:♥");
        await Assert.That(flowchart).DoesNotContain("#quot;");
        await Assert.That(sequence).Contains("I ♥ you ∞ times more!");
    }

    // showData cut the values down to whole numbers.
    [Test]
    public async Task PieShowData_KeepsDecimals()
    {
        var svg = Mermaid.Render(
            """
            pie showData
                title Key elements in Product X
                "Calcium" : 42.96
                "Potassium" : 50.05
            """);

        await Assert.That(svg).Contains("Calcium [42.96]");
        await Assert.That(svg).Contains("Potassium [50.05]");
    }

    // A quoted axis label kept its quotes.
    [Test]
    public async Task QuadrantQuotedAxisLabel_LosesItsQuotes()
    {
        var svg = Mermaid.Render(
            """
            quadrantChart
                x-axis "Urgent" --> "Not Urgent"
                y-axis "Not Important" --> "Important ❤"
                quadrant-1 Plan
                Point A: [0.3, 0.6]
            """);

        await Assert.That(svg).Contains(">Important ❤<");
        await Assert.That(svg).DoesNotContain("\"Important");
    }

    // Tildes were paired off in turn, so a nested generic came out as `List<List>int<>`.
    [Test]
    public async Task ClassNestedGenerics_KeepTheirNesting()
    {
        var svg = Mermaid.Render(
            """
            classDiagram
                class Square~Shape~{
                    int id
                    List~int~ position
                    +getDistanceMatrix() List~List~int~~
                }
            """);

        await Assert.That(svg).Contains("List&lt;List&lt;int&gt;&gt;");
    }

    // An annotation on the declaration line failed to parse.
    [Test]
    public async Task ClassAnnotationOnDeclarationLine_IsRead()
    {
        var svg = Mermaid.Render(
            """
            classDiagram
                class Shape <<interface>>
            """);

        await Assert.That(svg).Contains("«interface»");
    }

    // A state named on a line of its own, and a comment after a transition, failed to parse.
    [Test]
    public async Task StateBareIdAndTrailingComment_AreRead()
    {
        var bare = Mermaid.Render(
            """
            stateDiagram-v2
                stateId
            """);
        var commented = Mermaid.Render(
            """
            stateDiagram-v2
                [*] --> Still
                Still --> [*]
            %% this is a comment
                Still --> Moving
                Moving --> Still %% another comment
                Moving --> Crash
                Crash --> [*]
            """);

        await Assert.That(bare).Contains(">stateId<");
        await Assert.That(commented).Contains(">Crash<");
    }

    // A quote inside a quoted CSV field is written twice.
    [Test]
    public async Task SankeyEscapedQuotes_AreRead()
    {
        var result = new SankeyParser().Parse(
            """"
            sankey

            Pumped heat,"Heating and cooling, ""homes""",193.026
            Pumped heat,"Heating and cooling, ""commercial""",70.672
            """");

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Value.Links[0].Target).IsEqualTo("Heating and cooling, \"homes\"");
    }

    // Small nodes were raised to the minimum height after the scale was chosen, so a busy column ran off
    // the bottom of the canvas.
    [Test]
    public async Task SankeyBusyColumn_StaysOnTheCanvas()
    {
        var input = new StringBuilder("sankey\n\nSource,Big,1000\n");
        for (var i = 0; i < 15; i++)
        {
            input.Append(CultureInfo.InvariantCulture, $"Source,Small{i},1\n");
        }

        var svg = Mermaid.Render(input.ToString());

        var height = double.Parse(Regex.Match(svg, @"viewBox='0 0 [\d.]+ ([\d.]+)'").Groups[1].Value, CultureInfo.InvariantCulture);
        var bottoms = Regex.Matches(svg, @"<rect x='[\d.]+' y='([\d.]+)' width='20' height='([\d.]+)'")
            .Select(_ => double.Parse(_.Groups[1].Value, CultureInfo.InvariantCulture) + double.Parse(_.Groups[2].Value, CultureInfo.InvariantCulture))
            .ToList();

        await Assert.That(bottoms.Count).IsEqualTo(17);
        await Assert.That(bottoms.Max()).IsLessThanOrEqualTo(height);
    }

    // A circle node was given the height of a band in the layout, so its top and bottom were cut off.
    [Test]
    public async Task MindmapCircle_FitsTheCanvas()
    {
        var svg = Mermaid.Render(
            """
            mindmap
                id1((I am a circle))
            """);

        var height = double.Parse(Regex.Match(svg, @"viewBox='[-\d.]+ [-\d.]+ [\d.]+ ([\d.]+)'").Groups[1].Value, CultureInfo.InvariantCulture);
        var circle = Regex.Match(svg, @"<circle cx='[\d.]+' cy='([\d.]+)' r='([\d.]+)'");
        var cy = double.Parse(circle.Groups[1].Value, CultureInfo.InvariantCulture);
        var radius = double.Parse(circle.Groups[2].Value, CultureInfo.InvariantCulture);

        await Assert.That(cy - radius).IsGreaterThanOrEqualTo(0);
        await Assert.That(cy + radius).IsLessThanOrEqualTo(height);
    }

    // Mermaid 11.17+ draws a legend for named series.
    [Test]
    public Task XYChartLegend() =>
        VerifySvg(
            """
            xychart-beta
                title "An Example Chart"
                x-axis ["90d", "60d", "30d", "7d", "1d", "Current"]
                y-axis "Seconds" 0 --> 198.2
                line "avg" [48.1, 41.5, 45.7, 72.8, 67.7, 59.9]
                bar "p50" [38.2, 36.8, 39.7, 54.5, 49.0, 38.4]
                line "p95" [112.2, 75.3, 103.0, 177.0, 180.2, 109.4]
            """);
}
