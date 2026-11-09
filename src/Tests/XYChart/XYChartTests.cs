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

    // An axis titled without a range was pinned to 0 to 100, so data past 100 was drawn above the plot.
    // Mermaid spans the data instead.
    [Test]
    public async Task TitledAxisWithoutRange_SpansTheData()
    {
        var result = new XYChartParser().Parse(
            """
            xychart-beta
                y-axis "Revenue"
                bar [500, 1000]
            """);

        await Assert.That(result.Value.YAxisMin).IsNull();
        await Assert.That(result.Value.YAxisMax).IsNull();
    }

    // Mermaid lays a chart on its side with `horizontal` after the keyword: categories run down the left
    // and the bars grow to the right.
    [Test]
    public Task Horizontal()
    {
        const string input =
            """
            xychart-beta horizontal
                title "Regional Sales"
                x-axis "Region" ["North America", "Europe", "Asia Pacific"]
                y-axis "Revenue" 0 --> 100
                bar [85, 72, 90]
                line [80, 70, 88]
            """;

        return VerifySvg(input);
    }

    // A series may be named, and the x-axis may be a numeric range rather than categories, which the data
    // points are spread across.
    [Test]
    public Task NamedSeriesAndNumericXAxis()
    {
        const string input =
            """
            xychart-beta vertical
                x-axis "Month" 1 --> 5
                y-axis "Units" 0 --> 50
                bar "Sold" [10, 20, 30, 40, 50]
                line "Target" [15, 25, 30, 35, 45]
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task NamedSeriesAndNumericXAxis_AreRead()
    {
        var result = new XYChartParser().Parse(
            """
            xychart-beta horizontal
                x-axis "Month" 1 --> 12
                bar "Sold" [10, 20]
            """);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Value.Horizontal).IsTrue();
        await Assert.That(result.Value.XAxisLabel).IsEqualTo("Month");
        await Assert.That(result.Value.XAxisMin).IsEqualTo(1);
        await Assert.That(result.Value.XAxisMax).IsEqualTo(12);
        await Assert.That(result.Value.Series.Single().Name).IsEqualTo("Sold");
    }

    [Test]
    public Task TitledAxisWithoutRange()
    {
        const string input =
            """
            xychart-beta
                x-axis [Q1, Q2, Q3]
                y-axis "Revenue"
                bar [500, 750, 1000]
            """;

        return VerifySvg(input);
    }

    // Mermaid's docs example: the chart sized to 900 by 600, with each bar's value written on it.
    [Test]
    public Task SizeAndDataLabels()
    {
        const string input =
            """
            ---
            config:
              xyChart:
                width: 900
                height: 600
                showDataLabel: true
            ---
            xychart
                title "Sales Revenue"
                x-axis [jan, feb, mar, apr, may, jun]
                y-axis "Revenue (in $)" 4000 --> 11000
                bar [5000, 6000, 7500, 8200, 9500, 10500]
                line [5000, 6000, 7500, 8200, 9500, 10500]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DataLabelsOutsideHorizontalBars()
    {
        const string input =
            """
            %%{init: {"xyChart": {"showDataLabel": true, "showDataLabelOutsideBar": true}}}%%
            xychart horizontal
                x-axis [Fantasy, Romance, Thriller]
                bar [32, 25, 17]
            """;

        return VerifySvg(input);
    }

    // Mermaid's docs example: the palette colours the plots in the order they are declared.
    [Test]
    public Task PlotColorPalette()
    {
        const string input =
            """
            ---
            config:
              themeVariables:
                xyChart:
                  plotColorPalette: '#000000, #0000FF, #00FF00, #FF0000'
            ---
            xychart
            title "Different Colors in xyChart"
            x-axis "categoriesX" ["Category 1", "Category 2", "Category 3", "Category 4"]
            y-axis "valuesY" 0 --> 50
            line [10, 20, 30, 40]
            line [15, 25, 35, 45]
            line [5, 15, 25, 35]
            line [20, 30, 40, 50]
            """;

        return VerifySvg(input);
    }
}
