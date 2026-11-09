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

    // `donutHole` cuts the middle out, and `textPosition` moves the percentages out towards the rim.
    [Test]
    public Task DonutHoleAndTextPosition()
    {
        const string input =
            """
            ---
            config:
              pie:
                donutHole: 0.5
                textPosition: 0.85
            ---
            pie
                "Dogs" : 40
                "Cats" : 30
                "Birds" : 20
                "Fish" : 10
            """;

        return VerifySvg(input);
    }

    // Each of Mermaid's themes colours the slices from its own pie variables.
    [Test]
    public Task DarkTheme() =>
        VerifySvg(Themed("dark"));

    [Test]
    public Task ForestTheme() =>
        VerifySvg(Themed("forest"));

    [Test]
    public Task NeutralTheme() =>
        VerifySvg(Themed("neutral"));

    [Test]
    public Task BaseTheme() =>
        VerifySvg(Themed("base"));

    static string Themed(string theme) =>
        $$$"""
           %%{init: {"theme": "{{{theme}}}"}}%%
           pie title Pets
               "Dogs" : 40
               "Cats" : 30
               "Birds" : 20
               "Fish" : 10
           """;

    // The base theme is the one meant to be customised: every slice follows from primaryColor, and a
    // variable set directly wins over the derived one.
    [Test]
    public Task ThemeVariables()
    {
        const string input =
            """
            ---
            config:
              theme: base
              themeVariables:
                primaryColor: '#BB2528'
                pie3: '#00AAFF'
                pieTitleTextColor: '#BB2528'
            ---
            pie title Pets
                "Dogs" : 40
                "Cats" : 30
                "Birds" : 20
                "Fish" : 10
            """;

        return VerifySvg(input);
    }

    // As in Mermaid, a hole of more than 0.9 of the radius is ignored.
    [Test]
    public async Task DonutHole_OutOfRange_IsIgnored()
    {
        const string body =
            """
            pie
                "Dogs" : 40
                "Cats" : 60
            """;

        await Assert.That(Mermaid.Render("%%{init: {'pie': {'donutHole': 0.95}}}%%\n" + body))
            .IsEqualTo(Mermaid.Render(body));
    }
}
