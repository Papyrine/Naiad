public class QuadrantTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            quadrantChart
                title Campaign Analysis
                x-axis Low Reach --> High Reach
                y-axis Low Engagement --> High Engagement
                Campaign A: [0.3, 0.6]
                Campaign B: [0.7, 0.8]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Labels()
    {
        const string input =
            """
            quadrantChart
                title Priority Matrix
                x-axis Low Urgency --> High Urgency
                y-axis Low Impact --> High Impact
                quadrant-1 Do First
                quadrant-2 Schedule
                quadrant-3 Delegate
                quadrant-4 Eliminate
                Task A: [0.8, 0.9]
                Task B: [0.2, 0.8]
                Task C: [0.2, 0.2]
                Task D: [0.9, 0.3]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ManyPoints()
    {
        const string input =
            """
            quadrantChart
                title Product Portfolio
                x-axis Low Growth --> High Growth
                y-axis Low Share --> High Share
                Product A: [0.1, 0.9]
                Product B: [0.9, 0.9]
                Product C: [0.1, 0.1]
                Product D: [0.9, 0.1]
                Product E: [0.5, 0.5]
                Product F: [0.3, 0.7]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task TitleOnly()
    {
        const string input =
            """
            quadrantChart
                title Skills Assessment
                x-axis Beginner --> Expert
                y-axis Low Priority --> High Priority
                Python: [0.8, 0.9]
                JavaScript: [0.7, 0.7]
                Rust: [0.3, 0.5]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task AxisLabelsWithSpaces()
    {
        const string input =
            """
            quadrantChart
                title Skills Matrix
                x-axis Very Low Skill --> Very High Skill
                y-axis Very Low Value --> Very High Value
                Item A: [0.3, 0.6]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task EdgePositions()
    {
        const string input =
            """
            quadrantChart
                title Edge Cases
                x-axis Left --> Right
                y-axis Bottom --> Top
                Top Right: [1, 1]
                Top Left: [0, 1]
                Bottom Left: [0, 0]
                Bottom Right: [1, 0]
                Center: [0.5, 0.5]
            """;

        return VerifySvg(input);
    }

    // The first label stopped at its first hyphen, so `Semi-low --> Very-high` failed, and an axis with only
    // its first label, which Mermaid allows, was rejected.
    [Test]
    public async Task AxisLabels_HoldHyphensAndMayStandAlone()
    {
        var result = new QuadrantParser().Parse(
            """
            quadrantChart
                x-axis Semi-low --> Very-high
                y-axis Low
                Point: [0.5, 0.5]
            """);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Value.XAxisLeft).IsEqualTo("Semi-low");
        await Assert.That(result.Value.XAxisRight).IsEqualTo("Very-high");
        await Assert.That(result.Value.YAxisBottom).IsEqualTo("Low");
        await Assert.That(result.Value.YAxisTop).IsEqualTo("");
    }

    [Test]
    public async Task RejectsOutOfGrammarCoordinates()
    {
        // Mermaid's lexer accepts only `1` or `0[.digits]` for a coordinate, so `1.0` — and anything
        // outside [0, 1] — is a lexical error. Naiad matches that: parsing fails rather than clamping.
        await Assert.That(() => RenderPoint("1.0, 1.0")).Throws<MermaidParseException>();
        await Assert.That(() => RenderPoint("2, 0.5")).Throws<MermaidParseException>();
        await Assert.That(() => RenderPoint("-0.5, 0.5")).Throws<MermaidParseException>();

        static string RenderPoint(string coordinates) =>
            Mermaid.Render(
                $"""
                quadrantChart
                    x-axis Left --> Right
                    y-axis Bottom --> Top
                    Point: [{coordinates}]
                """);
    }
}
