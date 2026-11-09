using Naiad.Diagrams.Treemap;

public class TreemapTests : TestBase
{
    [Test]
    public Task Simple()
    {
        var input =
            """
            treemap-beta
            "Section A"
                "Item 1": 30
                "Item 2": 20
            "Section B"
                "Item 3": 50
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SingleLevel()
    {
        var input =
            """
            treemap-beta
            "Alpha": 40
            "Beta": 30
            "Gamma": 20
            "Delta": 10
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task NestedSections()
    {
        var input =
            """
            treemap-beta
            "Root"
                "Branch 1"
                    "Leaf 1.1": 15
                    "Leaf 1.2": 25
                "Branch 2"
                    "Leaf 2.1": 30
                    "Leaf 2.2": 10
                    "Leaf 2.3": 20
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MixedHierarchy()
    {
        var input =
            """
            treemap-beta
            "Products": 100
            "Services"
                "Consulting": 50
                "Support": 30
            "Other": 20
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task LargeValues()
    {
        var input =
            """
            treemap-beta
            "Category A"
                "Sub A1": 1000
                "Sub A2": 500
            "Category B"
                "Sub B1": 750
                "Sub B2": 250
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        var input =
            """
            treemap-beta
            "Group 1"
                "A": 10
                "B": 15
                "C": 20
            "Group 2"
                "D": 25
                "E": 30
            "Group 3"
                "F": 12
                "G": 18
                "H": 22
                "I": 8
            """;

        return VerifySvg(input);
    }

    // A blank before the line end was allowed only after a class, so a trailing space after a value or a
    // section name failed the whole diagram, and a class straight after a name was not read.
    [Test]
    public async Task TrailingBlanksAndClassAfterAName_AreRead()
    {
        var result = new TreemapParser().Parse(
            "treemap-beta\n\"Root\":::big \n    \"Leaf 1.1\": 12 \n    \"Section 2\"\t\n        \"Leaf 2.1\" : 5 :::small  \n");

        await Assert.That(result.Error).IsNull();
        var root = result.Value.RootNodes.Single();
        await Assert.That(root.CssClass).IsEqualTo("big");
        await Assert.That(root.Children.Select(_ => _.Name)).IsEquivalentTo(["Leaf 1.1", "Section 2"], CollectionOrdering.Matching);
        await Assert.That(root.Children[0].Value).IsEqualTo(12);
        var leaf = root.Children[1].Children.Single();
        await Assert.That(leaf.Value).IsEqualTo(5);
        await Assert.That(leaf.CssClass).IsEqualTo("small");
    }
}