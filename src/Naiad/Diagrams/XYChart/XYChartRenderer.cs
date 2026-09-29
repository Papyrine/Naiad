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

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;
        var width = chartWidth + leftMargin + rightMargin + options.Padding * 2;
        var height = chartHeight + topMargin + bottomMargin + titleOffset + options.Padding * 2;

        var builder = new SvgBuilder();
        builder.Size(width, height);

        var chartLeft = options.Padding + leftMargin;
        var chartTop = options.Padding + titleOffset + topMargin;
        var chartRight = chartLeft + chartWidth;
        var chartBottom = chartTop + chartHeight;

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

        // Calculate category count
        var categoryCount = model.XAxisCategories.Count > 0
            ? model.XAxisCategories.Count
            : model.Series.Count > 0 ? model.Series.Max(_ => _.Data.Count) : 1;
        var categoryWidth = chartWidth / categoryCount;

        // Draw grid lines
        const int gridLines = 5;
        for (var i = 0; i <= gridLines; i++)
        {
            var y = chartBottom - chartHeight * i / gridLines;
            var value = dataMin + dataRange * i / gridLines;

            // Grid line
            builder.AddLine(
                chartLeft,
                y,
                chartRight,
                y,
                stroke: "#e0e0e0",
                strokeWidth: 1);

            // Y-axis label
            builder.AddText(
                chartLeft - 10,
                y,
                value.ToString("0.##", CultureInfo.InvariantCulture),
                anchor: "end",
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily,
                fill: "#666");
        }

        // Draw X-axis categories
        for (var i = 0; i < categoryCount; i++)
        {
            var x = chartLeft + (i + 0.5) * categoryWidth;
            var label = i < model.XAxisCategories.Count ? model.XAxisCategories[i] : $"{i + 1}";

            builder.AddText(
                x,
                chartBottom + 20,
                label,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily,
                fill: "#333");
        }

        // Draw axis labels
        if (!string.IsNullOrEmpty(model.YAxisLabel))
        {
            var labelX = options.Padding + 15;
            var labelY = chartTop + chartHeight / 2;
            builder.BeginGroup(transform: string.Create(CultureInfo.InvariantCulture, $"rotate(-90, {labelX:0.##}, {labelY:0.##})"));
            builder.AddText(
                labelX,
                labelY,
                model.YAxisLabel,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily,
                fill: "#333");
            builder.EndGroup();
        }

        if (!string.IsNullOrEmpty(model.XAxisLabel))
        {
            builder.AddText(
                chartLeft + chartWidth / 2,
                chartBottom + 45,
                model.XAxisLabel,
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
        var barWidth = categoryWidth * 0.8 / Math.Max(1, barSeries.Count);

        foreach (var series in model.Series)
        {
            if (series.Type == ChartSeriesType.Bar)
            {
                var color = barColors[barSeriesIndex % barColors.Length];
                var barOffset = (barSeriesIndex - barSeries.Count / 2.0 + 0.5) * barWidth;

                for (var i = 0; i < series.Data.Count && i < categoryCount; i++)
                {
                    var value = series.Data[i];
                    var barHeight = (value - dataMin) / dataRange * chartHeight;
                    var x = chartLeft + (i + 0.5) * categoryWidth + barOffset - barWidth / 2;
                    var y = chartBottom - barHeight;

                    builder.AddRect(
                        x,
                        y,
                        barWidth - 2,
                        barHeight,
                        fill: color,
                        stroke: "none",
                        rx: 2);
                }
                barSeriesIndex++;
            }
            else if (series.Type == ChartSeriesType.Line)
            {
                var color = lineColors[lineSeriesIndex % lineColors.Length];
                var points = new List<(double x, double y)>();

                for (var i = 0; i < series.Data.Count && i < categoryCount; i++)
                {
                    var value = series.Data[i];
                    var x = chartLeft + (i + 0.5) * categoryWidth;
                    var y = chartBottom - (value - dataMin) / dataRange * chartHeight;
                    points.Add((x, y));
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
}