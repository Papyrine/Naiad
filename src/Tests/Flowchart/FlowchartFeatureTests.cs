using Naiad.Diagrams.Flowchart;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// Flowchart features beyond nodes and plain links: shape-aware edge ends, subgraph directions, link
// styles and markdown strings.
public class FlowchartFeatureTests
{
    static FlowchartModel Render(string input)
    {
        var model = new FlowchartParser().Parse(input).Value;
        new FlowchartRenderer().Render(model, RenderOptions.Default);
        return model;
    }

    // The end of an edge lies on the diamond's outline: |x|/halfWidth + |y|/halfHeight is 1 there, and
    // less than 1 anywhere inside the box's corners the edge used to stop at.
    [Test]
    public async Task EdgeEnds_LieOnTheDiamond()
    {
        var model = Render("flowchart TD\n    A[One] --> D{Decision}\n    B[Two] --> D\n    C[Three] --> D\n    D --> E[Four]\n    D --> F[Five]");
        var diamond = model.GetNode("D")!;

        foreach (var edge in model.Edges)
        {
            var end = edge.TargetId == "D" ? edge.Points[^1] : edge.Points[0];
            var onOutline =
                Math.Abs(end.X - diamond.Position.X) / (diamond.Width / 2) +
                Math.Abs(end.Y - diamond.Position.Y) / (diamond.Height / 2);

            await Assert.That(onOutline).IsBetween(0.99, 1.01).Because($"{edge.SourceId} -> {edge.TargetId}");
        }
    }

    [Test]
    public async Task EdgeEnds_LieOnTheCircle()
    {
        var model = Render("flowchart TD\n    A[One] --> C((Round))\n    B[Two] --> C\n    D[Three] --> C");
        var circle = model.GetNode("C")!;

        foreach (var edge in model.Edges)
        {
            var end = edge.Points[^1];
            var distance = Math.Sqrt(
                Math.Pow(end.X - circle.Position.X, 2) +
                Math.Pow(end.Y - circle.Position.Y, 2));

            await Assert.That(distance).IsBetween(circle.Width / 2 - 0.5, circle.Width / 2 + 0.5);
        }
    }

    [Test]
    public async Task SubgraphDirection_LaysItsMembersOutItsOwnWay()
    {
        var model = Render(
            """
            flowchart TB
                start --> pipeline
                subgraph pipeline [Pipeline]
                    direction LR
                    fetch --> parse --> store
                end
                pipeline --> done
            """);

        var fetch = model.GetNode("fetch")!;
        var parse = model.GetNode("parse")!;
        var store = model.GetNode("store")!;

        // Left to right inside, on one line.
        await Assert.That(parse.Position.X).IsGreaterThan(fetch.Position.X);
        await Assert.That(store.Position.X).IsGreaterThan(parse.Position.X);
        await Assert.That(Math.Abs(fetch.Position.Y - store.Position.Y)).IsLessThan(1);

        // Top to bottom around it, and the members sit inside the box.
        var box = model.Subgraphs.Single().Bounds;
        await Assert.That(model.GetNode("start")!.Position.Y).IsLessThan(box.Top);
        await Assert.That(model.GetNode("done")!.Position.Y).IsGreaterThan(box.Bottom);
        foreach (var node in new[] { fetch, parse, store })
        {
            await Assert.That(node.Position.X - node.Width / 2).IsGreaterThan(box.Left);
            await Assert.That(node.Position.X + node.Width / 2).IsLessThan(box.Right);
            await Assert.That(node.Position.Y - node.Height / 2).IsGreaterThan(box.Top);
            await Assert.That(node.Position.Y + node.Height / 2).IsLessThan(box.Bottom);
        }

        // The links inside moved with their nodes.
        var inner = model.Edges.Single(_ => _.SourceId == "fetch");
        await Assert.That(inner.Points[0].X).IsBetween(fetch.Position.X, parse.Position.X);
        await Assert.That(Math.Abs(inner.Points[0].Y - fetch.Position.Y)).IsLessThan(1);
    }

    // Mermaid ignores a subgraph's direction once one of its members is linked to something outside it.
    [Test]
    public async Task SubgraphDirection_IsIgnoredWhenAMemberIsLinkedOutside()
    {
        var model = Render(
            """
            flowchart TB
                subgraph group
                    direction LR
                    x --> y
                end
                y --> outside
            """);

        var x = model.GetNode("x")!;
        var y = model.GetNode("y")!;
        await Assert.That(y.Position.Y).IsGreaterThan(x.Position.Y);
        await Assert.That(Math.Abs(x.Position.X - y.Position.X)).IsLessThan(1);
    }

    [Test]
    public async Task SubgraphDirection_IsInheritedUnlessDeclared()
    {
        var model = new FlowchartParser().Parse(
            """
            flowchart LR
                subgraph outer
                    direction BT
                    subgraph inherits
                        a --> b
                    end
                    subgraph declares
                        direction RL
                        c --> d
                    end
                end
                subgraph plain
                    e --> f
                end
            """).Value;

        var outer = model.Subgraphs[0];
        await Assert.That(outer.Direction).IsEqualTo(Direction.BottomToTop);
        await Assert.That(outer.NestedSubgraphs[0].Direction).IsEqualTo(Direction.BottomToTop);
        await Assert.That(outer.NestedSubgraphs[1].Direction).IsEqualTo(Direction.RightToLeft);
        await Assert.That(model.Subgraphs[1].Direction).IsEqualTo(Direction.LeftToRight);
    }

    [Test]
    public async Task LinkStyle_ByNumberListAndDefault()
    {
        var model = new FlowchartParser().Parse(
            """
            flowchart LR
                A --> B
                B --> C & D
                linkStyle 1, 2 stroke:#00f,stroke-dasharray:5 5;
                linkStyle default stroke:#888,stroke-width:1px
                linkStyle 0 interpolate basis stroke:#f00,stroke-width:4px,color:red
                linkStyle 9 stroke:#0f0
            """).Value;

        // The default is laid down first whatever order the lines come in, and a number styles on top of it.
        await Assert.That(model.Edges[0].Style!.Stroke).IsEqualTo("#f00");
        await Assert.That(model.Edges[0].Style!.StrokeWidth).IsEqualTo(4);
        await Assert.That(model.Edges[0].Style!.Color).IsEqualTo("red");
        await Assert.That(model.Edges[1].Style!.Stroke).IsEqualTo("#00f");
        await Assert.That(model.Edges[1].Style!.StrokeWidth).IsEqualTo(1);
        await Assert.That(model.Edges[2].Style!.StrokeDasharray).IsEqualTo("5 5");
    }

    [Test]
    public async Task LinkStyle_IsDrawn_AndTheHeadTakesTheLinksColour()
    {
        var svg = Mermaid.Render("flowchart LR\n    A -- go --> B\n    B --> C\n    linkStyle 0 stroke:#ff0000,stroke-width:4px,color:blue");

        await Assert.That(svg).Contains("stroke='#ff0000'");
        await Assert.That(svg).Contains("stroke-width='4'");
        await Assert.That(svg).Contains("<marker id='naiad_flowchart-pointEnd-c0'");
        await Assert.That(svg).Contains("marker-end='url(#naiad_flowchart-pointEnd-c0)'");
        await Assert.That(svg).Contains("color:blue");

        // The other link keeps the shared marker and the class that colours it.
        await Assert.That(svg).Contains("marker-end='url(#naiad_flowchart-pointEnd)'");
        await Assert.That(Regex.Matches(svg, "class='flowchart-link'").Count).IsEqualTo(1);
    }

    [Test]
    public async Task UnstyledChart_HasNoColouredMarkers() =>
        await Assert.That(Mermaid.Render("flowchart LR\n    A --> B")).DoesNotContain("pointEnd-c");

    [Test]
    public async Task MarkdownString_BecomesBoldAndItalicMarkup()
    {
        var svg = Mermaid.Render("flowchart LR\n    a[\"`The **cat**\n    in the _hat_ & *more*`\"] -- \"`**Bold** edge`\" --> b[\"plain **stars**\"]");

        await Assert.That(svg).Contains("<p>The <strong>cat</strong><br/>in the <em>hat</em> &amp; <em>more</em></p>");
        await Assert.That(svg).Contains("<p><strong>Bold</strong> edge</p>");

        // Without the backticks the same characters are just text.
        await Assert.That(svg).Contains("<p>plain **stars**</p>");
        await Assert.That(svg).DoesNotContain("`");
    }

    [Test]
    [Arguments("`plain`", "plain", "plain")]
    [Arguments("`**bold** text`", "bold text", "<strong>bold</strong> text")]
    [Arguments("`*one* and _two_`", "one and two", "<em>one</em> and <em>two</em>")]
    [Arguments("`**bold and _both_**`", "bold and both", "<strong>bold and </strong><strong><em>both</em></strong>")]
    [Arguments("`snake_case_name and 2*3*4`", "snake_case_name and 234", "snake_case_name and 2<em>3</em>4")]
    [Arguments("`a < b`", "a < b", "a &lt; b")]
    [Arguments("`first\n    second`", "first<br/>second", "first<br/>second")]
    public async Task MarkdownLabel_PlainAndHtml(string label, string plain, string html)
    {
        await Assert.That(MarkdownLabel.IsMarkdown(label)).IsTrue();
        await Assert.That(MarkdownLabel.Plain(label)).IsEqualTo(plain);
        await Assert.That(MarkdownLabel.Html(label)).IsEqualTo(html);
    }

    [Test]
    [Arguments("plain")]
    [Arguments("`unclosed")]
    [Arguments("`")]
    [Arguments("")]
    public async Task OrdinaryLabel_IsNotMarkdown(string label) =>
        await Assert.That(MarkdownLabel.IsMarkdown(label)).IsFalse();

    // A markdown node is sized from its text without the markers, so it is no wider than the same words
    // written plainly.
    [Test]
    public async Task MarkdownNode_IsSizedWithoutItsMarkers()
    {
        var markdown = Render("flowchart LR\n    a[\"`A **bold** word`\"]").GetNode("a")!;
        var plain = Render("flowchart LR\n    a[\"A bold word\"]").GetNode("a")!;

        await Assert.That(markdown.Width).IsEqualTo(plain.Width);
        await Assert.That(markdown.Height).IsEqualTo(plain.Height);
    }

    [Test]
    public async Task HtmlRuns_SplitWhereTheWeightChanges()
    {
        var lines = HtmlText.ExtractRuns("<p>The <strong>cat</strong> sat<br/><em>on</em> a &amp; <b><i>mat</i></b></p>");

        await Assert.That(string.Join("|", lines[0].Select(_ => $"{_.Text}:{_.Bold}:{_.Italic}")))
            .IsEqualTo("The :False:False|cat:True:False| sat:False:False");
        await Assert.That(string.Join("|", lines[1].Select(_ => $"{_.Text}:{_.Bold}:{_.Italic}")))
            .IsEqualTo("on:False:True| a & :False:False|mat:True:True");
        await Assert.That(HtmlText.HasEmphasis("<p>plain<br/>text</p>")).IsFalse();
    }

    // Both PNG backends draw a markdown label: the bold word puts down more ink than the same word plain.
    [Test]
    public async Task Png_DrawsBoldHeavierThanPlain()
    {
        var options = new RenderOptions { Png = new() { Scale = 2 } };
        Func<string, RenderOptions, byte[]>[] backends =
        [
            (input, renderOptions) => SkiaRenderer.RenderPng(input, renderOptions),
            (input, renderOptions) => ImageSharpRenderer.RenderPng(input, renderOptions)
        ];

        foreach (var backend in backends)
        {
            var bold = DarkPixels(backend("flowchart LR\n    a[\"`**Heavy words here**`\"]", options));
            var plain = DarkPixels(backend("flowchart LR\n    a[\"`Heavy words here`\"]", options));

            await Assert.That(plain).IsGreaterThan(0);
            await Assert.That(bold).IsGreaterThan((int) (plain * 1.1));
        }
    }

    static int DarkPixels(byte[] png)
    {
        using var image = Image.Load<Rgba32>(png);
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.R < 110 && pixel.G < 110 && pixel.B < 110)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
