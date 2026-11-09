public class BlockTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            block-beta
                columns 3
                a["Block A"] b["Block B"] c["Block C"]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Span()
    {
        const string input =
            """
            block-beta
                columns 3
                a["Wide Block"]:2 b["Normal"]
                c["Full Width"]:3
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DifferentShapes()
    {
        const string input =
            """
            block-beta
                columns 3
                a["Rectangle"] b("Rounded") c(["Stadium"])
                d(("Circle")) e{"Diamond"} f{{"Hexagon"}}
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Column()
    {
        const string input =
            """
            block-beta
                columns 1
                a["First"]
                b["Second"]
                c["Third"]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ManyColumns()
    {
        const string input =
            """
            block-beta
                columns 5
                a b c d e
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MixedLayout()
    {
        const string input =
            """
            block-beta
                columns 4
                header["Header"]:4
                nav["Nav"] content["Content"]:2 side["Sidebar"]
                footer["Footer"]:4
            """;

        return VerifySvg(input);
    }

    // With no `columns`, every block shares one row however the lines are broken, and `space` leaves a
    // gap of as many columns as it asks for.
    [Test]
    public Task SingleRowByDefault()
    {
        const string input =
            """
            block-beta
                a["First"] space b["Second"]
                c["Third"] space:2 d["Fourth"]
            """;

        return VerifySvg(input);
    }

    // The elements of every line were read as one list running to the end of the input, so a comment after
    // a line of elements failed the whole diagram, and a later `columns 2` was read as two blocks.
    [Test]
    public async Task StatementsBetweenLinesOfElements_AreRead()
    {
        var result = new BlockParser().Parse(
            """
            block-beta
                a b c
                %% a comment
                d
                columns 2
                e["Last"]
            """);

        await Assert.That(result.Error).IsNull();
        await Assert.That(string.Join(",", result.Value.Elements.Select(_ => _.Id))).IsEqualTo("a,b,c,d,e");
        await Assert.That(result.Value.Columns).IsEqualTo(2);
    }
}