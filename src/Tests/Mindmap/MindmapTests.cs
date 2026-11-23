using Naiad.Diagrams.Mindmap;

public class MindmapTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            mindmap
              Root
                Branch A
                Branch B
                Branch C
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Icons()
    {
        const string input =
            """
            mindmap
              Project
                Storage ::icon(sample:box)
                Transport ::icon(fa fa-car)
            """;

        // sample:box comes from a registered iconify pack; fa fa-car is FontAwesome.
        const string pack =
            """
            {
              "prefix": "sample",
              "width": 24,
              "height": 24,
              "icons": {
                "box": {"body": "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"3\" fill=\"currentColor\"/>"}
              }
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(pack));
        IconPack.Load(stream);

        return VerifySvg(input);
    }

    [Test]
    public Task NodeIds()
    {
        const string input =
            """
            mindmap
              root((Project))
                api[API]
                db(Database)
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Nested()
    {
        const string input =
            """
            mindmap
              Root
                Branch 1
                  Sub 1.1
                  Sub 1.2
                Branch 2
                  Sub 2.1
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task CircleShape()
    {
        const string input =
            """
            mindmap
              ((Central))
                Child 1
                Child 2
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SquareShape()
    {
        const string input =
            """
            mindmap
              [Square Root]
                [Square Child]
                Normal Child
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task RoundedShape()
    {
        const string input =
            """
            mindmap
              (Rounded Root)
                (Rounded Child)
                Normal Child
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task HexagonShape()
    {
        const string input =
            """
            mindmap
              {{Hexagon}}
                Child A
                Child B
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MixedShapes()
    {
        const string input =
            """
            mindmap
              ((Center))
                [Square]
                  Normal
                (Rounded)
                  {{Hex}}
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DeepHierarchy()
    {
        const string input =
            """
            mindmap
              Root
                Level 1
                  Level 2
                    Level 3
                      Level 4
                        Level 5
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task WideTree()
    {
        const string input =
            """
            mindmap
              Center
                A
                B
                C
                D
                E
                F
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        const string input =
            """
            mindmap
              ((Project))
                [Planning]
                  Requirements
                  Design
                [Development]
                  Frontend
                  Backend
                  Database
                [Testing]
                  Unit Tests
                  Integration
                [Deployment]
                  Staging
                  Production
            """;

        return VerifySvg(input);
    }

    // An icon or class on a line of its own, as Mermaid's documentation writes it, became an extra node with
    // no text. A class list took only its first class, and a colon in a node's text failed the parse.
    [Test]
    public async Task IconAndClassLines_DecorateTheNodeAbove()
    {
        var result = new MindmapParser().Parse(
            """
            mindmap
              root((History))
                Long history
                ::icon(fa fa-book)
                Ratio 1:2
                :::urgent large
            """);

        await Assert.That(result.Error).IsNull();
        var children = result.Value.Root!.Children;
        await Assert.That(children.Select(_ => _.Text)).IsEquivalentTo(["Long history", "Ratio 1:2"], CollectionOrdering.Matching);
        await Assert.That(children[0].Icon).IsEqualTo("fa fa-book");
        await Assert.That(children[1].CssClass).IsEqualTo("urgent large");
    }

    // Markdown strings, as Mermaid's documentation writes them: bold and italic, a line break taken from the
    // source, and a plain label beside them. A quoted label is drawn without its quotes.
    [Test]
    public Task MarkdownStrings()
    {
        const string input =
            """
            mindmap
                id1["`**Root** with
            a second line`"]
                  id2["`The dog in **the** hog... a *very long text*`"]
                  id3[Regular labels still work]
                  id4("`_Rounded_ and **bold**`")
                  id5["Quoted label"]
            """;

        return VerifySvg(input);
    }

    // Without HTML a markdown string falls back to its plain text, without the markers.
    [Test]
    public Task MarkdownStrings_NoHtml()
    {
        const string input =
            """
            mindmap
                root["`**Root** node`"]
                  child["`An _italic_ child`"]
            """;

        return VerifySvg(input, new RenderOptions { AllowHtmlElements = false });
    }

    // A markdown string keeps its backticks in the model, which is how the renderer knows it for one; the
    // quotes around a label are not part of it.
    [Test]
    public async Task QuotedLabels_LoseTheirQuotes()
    {
        var result = new MindmapParser().Parse(
            """
            mindmap
              root["`**Bold** root`"]
                plain["Just text"]
                bare[No quotes]
            """);

        await Assert.That(result.Error).IsNull();
        var root = result.Value.Root!;
        await Assert.That(root.Text).IsEqualTo("`**Bold** root`");
        await Assert.That(root.Children.Select(_ => _.Text)).IsEquivalentTo(["Just text", "No quotes"], CollectionOrdering.Matching);
    }

    // A markdown string is drawn with its bold and italics, not its markers, and a node is a line taller
    // for each line break in it.
    [Test]
    public async Task MarkdownString_IsBoldAndItalicMarkup()
    {
        var svg = Mermaid.Render("mindmap\n  root[\"`**Bold** and _italic_`\"]");

        await Assert.That(svg).Contains("<strong>Bold</strong> and <em>italic</em>");
        await Assert.That(svg).DoesNotContain("`");
        await Assert.That(svg).DoesNotContain("**");

        var model = new MindmapParser().Parse("mindmap\n  root[\"`one\ntwo`\"]\n    single[\"`one`\"]").Value;
        new MindmapRenderer().Render(model, RenderOptions.Default);
        await Assert.That(model.Root!.Height).IsGreaterThan(model.Root.Children[0].Height);
    }
}
