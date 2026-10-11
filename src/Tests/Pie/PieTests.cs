public class PieTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            pie
                "Dogs" : 40
                "Cats" : 30
                "Birds" : 20
                "Fish" : 10
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Title()
    {
        const string input =
            """
            pie
                title Pet Distribution
                "Dogs" : 40
                "Cats" : 30
                "Birds" : 30
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ShowData()
    {
        const string input =
            """
            pie showData
                "Revenue" : 65
                "Costs" : 35
            """;

        return VerifySvg(input);
    }

    // One section holding everything is a full disc, and the empty section beside it has no slice and
    // no "0%" label, though it keeps its place in the legend.
    [Test]
    public Task WholePie()
    {
        const string input =
            """
            pie title Coverage
                "Covered" : 100
                "Missed" : 0
            """;

        return VerifySvg(input);
    }
}
