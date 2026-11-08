using Naiad.Diagrams.Venn;

public class VennTests : TestBase
{
    [Test]
    public Task TwoSets()
    {
        const string input =
            """
            venn-beta
              title "Team overlap"
              set Frontend
              set Backend
              union Frontend,Backend["APIs"]
            """;

        return VerifySvg(input);
    }

    // The first example in Mermaid's documentation: three sets, every overlap labelled.
    [Test]
    public Task ThreeSetsWithTitle()
    {
        const string input =
            """
            venn-beta
              title What makes a good feature
              set Desirable
              set Feasible
              set Viable
              union Desirable,Feasible["Buildable"]
              union Feasible,Viable["Sustainable"]
              union Desirable,Viable["Marketable"]
              union Desirable,Feasible,Viable["Ship it"]
            """;

        return VerifySvg(input);
    }

    // Only the three-way union is declared; its pairwise overlaps are implied.
    [Test]
    public Task HigherArityUnion()
    {
        const string input =
            """
            venn-beta
              set Desirable
              set Feasible
              set Viable
              union Desirable,Feasible,Viable["Innovation"]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Sizes()
    {
        const string input =
            """
            venn-beta
              set A["Alpha"]:20
              set B["Beta"]:12
              union A,B["AB"]:3
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task TextNodes()
    {
        const string input =
            """
            venn-beta
              set A["Frontend"]
                text A1["React"]
                text A2["Design Systems"]
              set B["Backend"]
                text B1["API"]
              union A,B["Shared"]
                text AB1["OpenAPI"]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Styles()
    {
        const string input =
            """
            venn-beta
              set A["Alpha"]:20
                text A1["React"]
                text A2["Design Systems"]
              set B["Beta"]:12
              union A,B["AB"]:3
              style A fill:#ff6b6b
              style A,B color:#333
              style A1 color:red
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task StyledUnionAndStroke()
    {
        const string input =
            """
            venn-beta
              set A
              set B
              union A,B["Both"]
              style A,B fill:#ffd700, color:#000
              style B stroke:#000, stroke-width:4, fill-opacity:0.3
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SingleSet()
    {
        const string input =
            """
            venn-beta
              set Everything
            """;

        return VerifySvg(input);
    }

    // Sets with no union between them are drawn apart.
    [Test]
    public Task DisjointSets()
    {
        const string input =
            """
            venn-beta
              set Cats
              set Dogs
              set Fish:5
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ManySets()
    {
        const string input =
            """
            venn-beta
              title Programming languages
              set Python
              set JavaScript
              set Rust
              set Go
              set Haskell:6
              union Python,JavaScript["Scripting"]
              union JavaScript,Rust["WebAssembly"]
              union Rust,Go["Systems"]
              union Go,Python["Tooling"]
              union Rust,Haskell["Type nerds"]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task LongLabels()
    {
        const string input =
            """
            venn-beta
              title A rather long title for a diagram about overlapping areas of responsibility
              set Ops["Operations and infrastructure engineering"]
              set Dev["Product development and feature delivery"]
              union Ops,Dev["Continuous delivery pipelines and observability"]
                text P1["Deployment automation tooling"]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Empty()
    {
        const string input =
            """
            venn-beta
              title Nothing yet
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_SetsUnionsAndSizes()
    {
        var model = Parse(
            """
            venn-beta
              title "Team overlap"
              set B["Beta"]:12
              set A
              union B,A["AB"]:3
              union A , B
            """);

        await Assert.That(model.Title).IsEqualTo("Team overlap");
        await Assert.That(model.Areas.Count).IsEqualTo(4);
        await Assert.That(model.Areas[0].Key).IsEqualTo("B");
        await Assert.That(model.Areas[0].Label).IsEqualTo("Beta");
        await Assert.That(model.Areas[0].Size).IsEqualTo(12);
        await Assert.That(model.Areas[1].Label).IsNull();
        await Assert.That(model.Areas[1].Size).IsEqualTo(10);
        await Assert.That(model.Areas[2].Key).IsEqualTo("A|B");
        await Assert.That(model.Areas[2].Size).IsEqualTo(3);
        await Assert.That(model.Areas[3].Size).IsEqualTo(2.5);
    }

    [Test]
    public async Task Parse_QuotedIdentifiersAndBareLabels()
    {
        var model = Parse(
            """
            VENN-BETA
              set "Foo Bar"[ Foo ]
              set Baz
              union "Foo Bar",Baz
            """);

        await Assert.That(model.Areas[0].Key).IsEqualTo("Foo Bar");
        await Assert.That(model.Areas[0].Label).IsEqualTo("Foo");
        await Assert.That(model.Areas[2].Key).IsEqualTo("Baz|Foo Bar");
    }

    [Test]
    public async Task Parse_TextNodes()
    {
        var model = Parse(
            """
            venn-beta
              set A["Frontend"]
                text A1["React"]
                text "Design Systems"
              set B
              union A,B
                text AB1
              text A,B AB2["Explicit"]
            """);

        var nodes = model.TextNodes.Select(_ => $"{_.Key}:{_.Id}:{_.Label}").ToList();
        await Assert.That(string.Join(",", nodes))
            .IsEqualTo("A:A1:React,A:Design Systems:,A|B:AB1:,A|B:AB2:Explicit");
    }

    [Test]
    public async Task Parse_Styles()
    {
        var model = Parse(
            """
            venn-beta
              set A
              set B
              style B,A fill:#ff6b6b, color:rgb(1, 2, 3), stroke-width:2
              style A1 color:"dark red"
            """);

        await Assert.That(model.Styles.Count).IsEqualTo(2);
        await Assert.That(model.Styles[0].Key).IsEqualTo("A|B");
        await Assert.That(model.Styles[0].Properties["fill"]).IsEqualTo("#ff6b6b");
        await Assert.That(model.Styles[0].Properties["color"]).IsEqualTo("rgb(1, 2, 3)");
        await Assert.That(model.Styles[0].Properties["stroke-width"]).IsEqualTo("2");
        await Assert.That(model.Styles[1].Properties["color"]).IsEqualTo("dark red");
    }

    [Test]
    public async Task Parse_CommentsAndBlankLines()
    {
        var model = Parse(
            """
            venn-beta

              %% a comment between statements
              set A %% trailing comment
              set B
            """);

        await Assert.That(model.Areas.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_UnionOfUnknownSetFails() =>
        await Assert.That(
                () => new VennParser().Parse(
                    """
                    venn-beta
                      set A
                      union A,B
                    """))
            .Throws<MermaidParseException>();

    [Test]
    public async Task Parse_UnionOfOneSetFails()
    {
        var result = new VennParser().Parse(
            """
            venn-beta
              set A
              union A
            """);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("venn-beta\n  set A", out var type)).IsTrue();
        await Assert.That(type).IsEqualTo(DiagramType.Venn);
        await Assert.That(Mermaid.TryDetectType("venn\n  set A", out _)).IsFalse();
    }

    static VennModel Parse(string input)
    {
        var result = new VennParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }
}
