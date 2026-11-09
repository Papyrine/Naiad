namespace Naiad.Diagrams.XYChart;

public class XYChartRenderer : IDiagramRenderer<XYChartModel>
{
    const double chartWidth = 500;
    const double chartHeight = 300;
    const double leftMargin = 60;
    const double rightMargin = 20;
    const double topMargin = 60;
    const double bottomMargin = 60;
    const double titleHeight = 30;

    static string[] barColors = ["#4CAF50", "#2196F3", "#FF9800", "#E91E63"];
    static string[] lineColors = ["#9C27B0", "#00BCD4", "#FF5722", "#607D8B"];

    public SvgDocument Render(XYChartModel model, RenderOptions options)
    {
        if (model.Series.Count == 0)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "Empty chart",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        var horizontal = model.Horizontal;
        var tickFontSize = options.FontSize - 2;

        // Calculate category count
        var categoryCount = model.XAxisCategories.Count > 0
            ? model.XAxisCategories.Count
            : model.Series.Max(_ => _.Data.Count);
        categoryCount = Math.Max(1, categoryCount);
        var categories = Enumerable.Range(0, categoryCount)
            .Select(_ => CategoryLabel(model, _, categoryCount))
            .ToList();

        // Lying down, the categories are labelled up the left side, so the margin there is made wide
        // enough for the longest of them.
        var left = leftMargin;
        if (horizontal)
        {
            left = Math.Max(leftMargin, categories.Max(_ => MeasureText(_, tickFontSize)) + 40);
        }

        // Named series get a legend at the right, as Mermaid draws one, and the canvas widens for it.
        var legend = LegendEntries(model);
        var legendWidth = 0d;
        if (legend.Count > 0)
        {
            legendWidth = legendGap + legendMarkerSize + legendLabelGap + legend.Max(_ => MeasureText(_.Name, legendFontSize));
        }

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;
        var width = chartWidth + left + rightMargin + legendWidth + options.Padding * 2;
        var height = chartHeight + topMargin + bottomMargin + titleOffset + options.Padding * 2;

        var builder = new SvgBuilder();
        builder.Size(width, height);

        var chartLeft = options.Padding + left;
        var chartTop = options.Padding + titleOffset + topMargin;
        var chartRight = chartLeft + chartWidth;
        var chartBottom = chartTop + chartHeight;

        // Positions are worked out along the category axis and the value axis, then placed: standing up,
        // categories run left to right and values bottom to top; lying down, categories run top to bottom
        // and values left to right.
        var categoryLength = horizontal ? chartHeight : chartWidth;
        var valueLength = horizontal ? chartWidth : chartHeight;

        (double x, double y) Place(double along, double value)
        {
            if (horizontal)
            {
                return (chartLeft + value, chartTop + along);
            }

            return (chartLeft + along, chartBottom - value);
        }

        // Draw title
        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                width / 2,
                options.Padding + titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize + 4,
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        // Calculate data range
        var allData = model.Series.SelectMany(_ => _.Data).ToList();
        var dataMin = model.YAxisMin ?? (allData.Count > 0 ? allData.Min() : 0);
        var dataMax = model.YAxisMax ?? (allData.Count > 0 ? allData.Max() : 100);
        var dataRange = dataMax - dataMin;
        if (dataRange == 0) dataRange = 1;

        double ValuePosition(double value) => (value - dataMin) / dataRange * valueLength;

        var categoryWidth = categoryLength / categoryCount;

        // Draw grid lines
        const int gridLines = 5;
        for (var i = 0; i <= gridLines; i++)
        {
            var position = valueLength * i / gridLines;
            var value = dataMin + dataRange * i / gridLines;
            var (gridStartX, gridStartY) = Place(0, position);
            var (gridEndX, gridEndY) = Place(categoryLength, position);

            // Grid line
            builder.AddLine(
                gridStartX,
                gridStartY,
                gridEndX,
                gridEndY,
                stroke: "#e0e0e0",
                strokeWidth: 1);

            // Value label
            var valueText = value.ToString("0.##", CultureInfo.InvariantCulture);
            if (horizontal)
            {
                builder.AddText(
                    gridStartX,
                    chartBottom + 20,
                    valueText,
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: tickFontSize,
                    fontFamily: options.FontFamily,
                    fill: "#666");
            }
            else
            {
                builder.AddText(
                    chartLeft - 10,
                    gridStartY,
                    valueText,
                    anchor: "end",
                    baseline: "middle",
                    fontSize: tickFontSize,
                    fontFamily: options.FontFamily,
                    fill: "#666");
            }
        }

        // Draw category labels
        for (var i = 0; i < categoryCount; i++)
        {
            var (x, y) = Place((i + 0.5) * categoryWidth, 0);
            if (horizontal)
            {
                builder.AddText(
                    chartLeft - 10,
                    y,
                    categories[i],
                    anchor: "end",
                    baseline: "middle",
                    fontSize: tickFontSize,
                    fontFamily: options.FontFamily,
                    fill: "#333");
            }
            else
            {
                builder.AddText(
                    x,
                    chartBottom + 20,
                    categories[i],
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: tickFontSize,
                    fontFamily: options.FontFamily,
                    fill: "#333");
            }
        }

        // Axis titles: the axis up the left is titled sideways, the one along the bottom beneath it.
        var leftTitle = horizontal ? model.XAxisLabel : model.YAxisLabel;
        var bottomTitle = horizontal ? model.YAxisLabel : model.XAxisLabel;
        if (!string.IsNullOrEmpty(leftTitle))
        {
            var labelX = options.Padding + 15;
            var labelY = chartTop + chartHeight / 2;
            builder.BeginGroup(transform: string.Create(CultureInfo.InvariantCulture, $"rotate(-90, {labelX:0.##}, {labelY:0.##})"));
            builder.AddText(
                labelX,
                labelY,
                leftTitle,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily,
                fill: "#333");
            builder.EndGroup();
        }

        if (!string.IsNullOrEmpty(bottomTitle))
        {
            builder.AddText(
                chartLeft + chartWidth / 2,
                chartBottom + 45,
                bottomTitle,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily,
                fill: "#333");
        }

        // Draw series
        var barSeriesIndex = 0;
        var lineSeriesIndex = 0;
        var barSeries = model.Series.Where(_ => _.Type == ChartSeriesType.Bar).ToList();
        var barThickness = categoryWidth * 0.8 / Math.Max(1, barSeries.Count);

        foreach (var series in model.Series)
        {
            if (series.Type == ChartSeriesType.Bar)
            {
                var color = barColors[barSeriesIndex % barColors.Length];
                var barOffset = (barSeriesIndex - barSeries.Count / 2.0 + 0.5) * barThickness;

                for (var i = 0; i < series.Data.Count && i < categoryCount; i++)
                {
                    var barLength = ValuePosition(series.Data[i]);
                    var along = (i + 0.5) * categoryWidth + barOffset - barThickness / 2;

                    if (horizontal)
                    {
                        builder.AddRect(
                            chartLeft,
                            chartTop + along,
                            barLength,
                            barThickness - 2,
                            fill: color,
                            stroke: "none",
                            rx: 2);
                    }
                    else
                    {
                        builder.AddRect(
                            chartLeft + along,
                            chartBottom - barLength,
                            barThickness - 2,
                            barLength,
                            fill: color,
                            stroke: "none",
                            rx: 2);
                    }
                }

                barSeriesIndex++;
            }
            else if (series.Type == ChartSeriesType.Line)
            {
                var color = lineColors[lineSeriesIndex % lineColors.Length];
                var points = new List<(double x, double y)>();

                for (var i = 0; i < series.Data.Count && i < categoryCount; i++)
                {
                    points.Add(Place((i + 0.5) * categoryWidth, ValuePosition(series.Data[i])));
                }

                // Draw line
                if (points.Count >= 2)
                {
                    var pathBuilder = new StringBuilder();
                    pathBuilder.Append(CultureInfo.InvariantCulture, $"M {points[0].x:0.##} {points[0].y:0.##}");
                    for (var i = 1; i < points.Count; i++)
                    {
                        pathBuilder.Append(CultureInfo.InvariantCulture, $" L {points[i].x:0.##} {points[i].y:0.##}");
                    }
                    builder.AddPath(pathBuilder.ToString(), stroke: color, strokeWidth: 2, fill: "none");
                }

                // Draw points
                foreach (var (x, y) in points)
                {
                    builder.AddCircle(x, y, 4, fill: color, stroke: "#fff", strokeWidth: 2);
                }

                lineSeriesIndex++;
            }
        }

        DrawLegend(builder, legend, chartRight + legendGap, chartTop + chartHeight / 2, options);

        // Draw axes last so the baseline sits on top of the series
        builder.AddLine(
            chartLeft,
            chartTop,
            chartLeft,
            chartBottom,
            stroke: "#333",
            strokeWidth: 2);
        builder.AddLine(
            chartLeft,
            chartBottom,
            chartRight,
            chartBottom,
            stroke: "#333",
            strokeWidth: 2);

        return builder.Build();
    }

    /// <summary>
    /// The label for a place on the x-axis: its category, or on a numeric axis (<c>x-axis 1 --&gt; 12</c>)
    /// its value, the range spread evenly over the data points as Mermaid does. With neither, points are
    /// numbered from 1.
    /// </summary>
    static string CategoryLabel(XYChartModel model, int index, int count)
    {
        if (index < model.XAxisCategories.Count)
        {
            return model.XAxisCategories[index];
        }

        if (model is { XAxisMin: { } min, XAxisMax: { } max })
        {
            if (count == 1)
            {
                return min.ToString("0.##", CultureInfo.InvariantCulture);
            }

            var value = min + (max - min) * index / (count - 1);
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    const double legendGap = 30;
    const double legendMarkerSize = 10.5;
    const double legendLabelGap = 5;
    const double legendRowHeight = 21;
    const double legendFontSize = 14;

    readonly record struct LegendEntry(string Name, ChartSeriesType Type, string Color);

    // The named series in order, each with the colour it is drawn in.
    static List<LegendEntry> LegendEntries(XYChartModel model)
    {
        var entries = new List<LegendEntry>();
        var barIndex = 0;
        var lineIndex = 0;
        foreach (var series in model.Series)
        {
            string color;
            if (series.Type == ChartSeriesType.Bar)
            {
                color = barColors[barIndex++ % barColors.Length];
            }
            else
            {
                color = lineColors[lineIndex++ % lineColors.Length];
            }

            if (!string.IsNullOrEmpty(series.Name))
            {
                entries.Add(new(series.Name, series.Type, color));
            }
        }

        return entries;
    }

    // Mermaid's legend: a block of rows centred on the plot's height, each a swatch for a bar series or a
    // short stroke for a line series, then the series' name.
    static void DrawLegend(SvgBuilder builder, List<LegendEntry> legend, double x, double centerY, RenderOptions options)
    {
        var y = centerY - (legend.Count - 1) * legendRowHeight / 2;
        foreach (var entry in legend)
        {
            if (entry.Type == ChartSeriesType.Bar)
            {
                builder.AddRect(
                    x,
                    y - legendMarkerSize / 2,
                    legendMarkerSize,
                    legendMarkerSize,
                    fill: entry.Color,
                    stroke: "none");
            }
            else
            {
                builder.AddLine(x, y, x + legendMarkerSize, y, stroke: entry.Color, strokeWidth: 2);
            }

            builder.AddText(
                x + legendMarkerSize + legendLabelGap,
                y,
                entry.Name,
                anchor: "start",
                baseline: "middle",
                fontSize: legendFontSize,
                fontFamily: options.FontFamily,
                fill: "#333");
            y += legendRowHeight;
        }
    }

    static double MeasureText(string text, double fontSize) =>
        text.Length * fontSize * 0.55;
}
