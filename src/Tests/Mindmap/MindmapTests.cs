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
}
