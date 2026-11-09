public class XYChartTests: TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            xychart-beta
                title "Monthly Sales"
                x-axis [Jan, Feb, Mar, Apr, May]
                y-axis "Revenue" 0 --> 100
                bar [50, 60, 75, 80, 90]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task PlusAndBareDotNumbers()
    {
        // Exercises the widened number grammar end to end: a leading + and bare-dot decimals like .5.
        const string input =
            """
            xychart-beta
                title "Fractional Values"
                x-axis [A, B, C, D]
                y-axis "Value" 0 --> 3
                bar [.5, 1.5, +2, 2.5]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task BarAndLine()
    {
        const string input =
            """
            xychart-beta
                title "Sales vs Target"
                x-axis [Q1, Q2, Q3, Q4]
                y-axis "Amount" 0 --> 200
                bar [120, 150, 180, 160]
                line [100, 140, 170, 190]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MultipleBarSeries()
    {
        const string input =
            """
            xychart-beta
                title "Product Comparison"
                x-axis [2020, 2021, 2022, 2023]
                y-axis "Units" 0 --> 500
                bar [100, 150, 200, 250]
                bar [80, 120, 180, 220]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task WithoutTitle()
    {
        const string input =
            """
            xychart-beta
                x-axis [A, B, C, D]
                bar [10, 20, 30, 40]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task QuotedCategories()
    {
        const string input =
            """
            xychart-beta
                title "Regional Sales"
                x-axis ["North America", "Europe", "Asia Pacific"]
                y-axis "Revenue (M$)" 0 --> 100
                bar [85, 72, 90]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        const string input =
            """
            xychart-beta
                title "Annual Data"
                x-axis [Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec]
                y-axis "Value" 0 --> 100
                bar [45, 52, 61, 58, 72, 85, 91, 88, 76, 65, 55, 48]
                line [40, 48, 58, 55, 70, 82, 88, 85, 73, 62, 52, 45]
            """;

        return VerifySvg(input);
    }

    // A blank before a list's closing bracket was rejected, and an axis title with nothing after it took
    // the line end with it, so the next line failed to parse.
    [Test]
    public async Task BlanksBeforeBracketsAndATitleOnItsOwn_AreRead()
    {
        var result = new XYChartParser().Parse(
            """
            xychart-beta
                x-axis ["Jan", "Feb" ]
                y-axis "Revenue"
                bar [1, 2 ]
                line [ 3, 4 ]
            """);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Value.XAxisCategories).IsEquivalentTo(["Jan", "Feb"], CollectionOrdering.Matching);
        await Assert.That(result.Value.YAxisLabel).IsEqualTo("Revenue");
        await Assert.That(result.Value.Series.Select(_ => string.Join(",", _.Data))).IsEquivalentTo(["1,2", "3,4"], CollectionOrdering.Matching);
    }
}
