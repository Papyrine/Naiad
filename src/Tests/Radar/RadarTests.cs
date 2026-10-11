public class RadarTests : TestBase
{
    [Test]
    public Task Simple()
    {
        var input =
            """
            radar-beta
            axis A, B, C, D, E
            curve data1["Series1"]{20, 40, 60, 80, 50}
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Title()
    {
        var input =
            """
            radar-beta
            title Performance Metrics
            axis Speed, Quality, Cost, Time, Scope
            curve a["Project A"]{80, 60, 40, 70, 90}
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MultipleCurves()
    {
        var input =
            """
            radar-beta
            title Comparison
            axis Strength, Speed, Agility, Stamina, Intelligence
            curve hero["Hero"]{90, 70, 85, 60, 75}
            curve villain["Villain"]{75, 80, 65, 85, 90}
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ThreeCurves()
    {
        var input =
            """
            radar-beta
            title Language Skills
            axis English, French, German, Spanish
            curve a["User1"]{80, 30, 40, 50}
            curve b["User2"]{50, 80, 30, 40}
            curve c["User3"]{60, 60, 80, 70}
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Triangle()
    {
        var input =
            """
            radar-beta
            axis A, B, C
            curve data["Values"]{100, 80, 60}
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Hexagon()
    {
        var input =
            """
            radar-beta
            title Six Dimensions
            axis Dim1, Dim2, Dim3, Dim4, Dim5, Dim6
            curve data["Metrics"]{50, 80, 60, 90, 40, 70}
            """;

        return VerifySvg(input);
    }

    // Axes with their own labels over two lines, a curve whose values are named for their axes, and
    // every option: a polygon graticule, a fixed range, a tick count and no legend.
    [Test]
    public Task LabelledAxesAndOptions()
    {
        const string input =
            """
            radar-beta
                title Restaurant Comparison
                axis food["Food Quality"], service["Service"], price["Price"]
                axis ambiance["Ambiance"]
                curve a["Restaurant A"]{4, 3, 2, 4}
                curve b["Restaurant B"]{ service: 4, food: 3, ambiance: 3, price: 3 }

                graticule polygon
                max 5
                min 0
                ticks 4
                showLegend false
            """;

        return VerifySvg(input);
    }
}