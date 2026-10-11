namespace Naiad.Diagrams.Radar;

public class RadarRenderer : IDiagramRenderer<RadarModel>
{
    const double chartRadius = 120;
    const double titleHeight = 30;
    const double legendHeight = 25;
    const double labelOffsetX = 60;  // Space for horizontal labels
    const double labelOffsetY = 30;  // Space for vertical labels (including text height)
    const double labelGap = 20;      // Between the end of an axis and its label

    static string[] curveColors =
    [
        "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728",
        "#9467bd", "#8c564b", "#e377c2", "#7f7f7f",
        "#bcbd22", "#17becf", "#aec7e8", "#ffbb78"
    ];

    public SvgDocument Render(RadarModel model, RenderOptions options)
    {
        if (model.Axes.Count == 0)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "Empty diagram",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;
        var legendOffset = model is {ShowLegend: true, Curves.Count: > 0} ? legendHeight * model.Curves.Count : 0;

        // Axis labels hang off the ends of the axes, so the room beside the chart has to take the widest
        // of them; a fixed margin clipped anything longer than a short word.
        var widestLabel = model.Axes.Max(_ => (_.Label ?? _.Id).Length) * (options.FontSize - 2) * 0.6;
        var labelRoomX = Math.Max(labelOffsetX, labelGap + widestLabel + 5);

        var contentWidth = (chartRadius + labelRoomX) * 2;
        var contentHeight = (chartRadius + labelOffsetY) * 2 + titleOffset + legendOffset;

        var centerX = chartRadius + labelRoomX;
        var centerY = chartRadius + labelOffsetY + titleOffset;

        var builder = new SvgBuilder();
        builder.Size(contentWidth, contentHeight);
        builder.Padding(options.Padding);

        // Draw title
        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                contentWidth / 2,
                titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize + 4,
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        // Calculate max value
        var maxValue = model.Max ?? model.Curves.SelectMany(_ => _.Values).DefaultIfEmpty(100).Max();
        var minValue = model.Min ?? 0;

        // Draw graticule
        DrawGraticule(builder, centerX, centerY, model.Axes.Count, model.Ticks, model.Graticule);

        // Draw axis lines and labels
        DrawAxes(builder, centerX, centerY, model.Axes, options);

        // Draw curves
        for (var i = 0; i < model.Curves.Count; i++)
        {
            DrawCurve(
                builder,
                centerX,
                centerY,
                model.Curves[i],
                model.Axes.Count,
                minValue,
                maxValue,
                curveColors[i % curveColors.Length]);
        }

        // Draw legend
        if (model is {ShowLegend: true, Curves.Count: > 0})
        {
            DrawLegend(builder, model.Curves, 0, contentHeight - legendOffset + 10, options);
        }

        return builder.Build();
    }

    static void DrawGraticule(
        SvgBuilder builder,
        double cx,
        double cy,
        int axisCount,
        int ticks,
        GraticuleType graticule)
    {
        for (var i = 1; i <= ticks; i++)
        {
            var radius = chartRadius * i / ticks;

            if (graticule == GraticuleType.Circle)
            {
                builder.AddCircle(cx, cy, radius, fill: "none", stroke: "#E0E0E0", strokeWidth: 1);
            }
            else
            {
                // Polygon using path
                var pathBuilder = new StringBuilder();
                for (var j = 0; j < axisCount; j++)
                {
                    var angle = 2 * Math.PI * j / axisCount - Math.PI / 2;
                    var x = cx + radius * Math.Cos(angle);
                    var y = cy + radius * Math.Sin(angle);
                    if (j == 0)
                    {
                        pathBuilder.Append(CultureInfo.InvariantCulture, $"M {x:0.##} {y:0.##}");
                    }
                    else
                    {
                        pathBuilder.Append(CultureInfo.InvariantCulture, $" L {x:0.##} {y:0.##}");
                    }
                }
                pathBuilder.Append(" Z");
                builder.AddPath(pathBuilder.ToString(), fill: "none", stroke: "#E0E0E0", strokeWidth: 1);
            }
        }
    }

    static void DrawAxes(SvgBuilder builder, double cx, double cy, List<RadarAxis> axes, RenderOptions options)
    {
        for (var i = 0; i < axes.Count; i++)
        {
            var angle = 2 * Math.PI * i / axes.Count - Math.PI / 2;
            var x = cx + chartRadius * Math.Cos(angle);
            var y = cy + chartRadius * Math.Sin(angle);

            // Draw axis line
            builder.AddLine(cx, cy, x, y, stroke: "#BDBDBD", strokeWidth: 1);

            // Draw label
            var labelX = cx + (chartRadius + labelGap) * Math.Cos(angle);
            var labelY = cy + (chartRadius + labelGap) * Math.Sin(angle);
            var anchor = Math.Abs(Math.Cos(angle)) < 0.1 ? "middle" :
                         Math.Cos(angle) > 0 ? "start" : "end";

            builder.AddText(
                labelX,
                labelY,
                axes[i].Label ?? axes[i].Id,
                anchor: anchor,
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily);
        }
    }

    static void DrawCurve(
        SvgBuilder builder,
        double cx,
        double cy,
        RadarCurve curve,
        int axisCount,
        double minValue,
        double maxValue,
        string color)
    {
        if (curve.Values.Count == 0)
        {
            return;
        }

        var pathBuilder = new StringBuilder();
        for (var i = 0; i < Math.Min(curve.Values.Count, axisCount); i++)
        {
            var value = curve.Values[i];
            var normalizedValue = (value - minValue) / (maxValue - minValue);
            var radius = chartRadius * normalizedValue;

            var angle = 2 * Math.PI * i / axisCount - Math.PI / 2;
            var x = cx + radius * Math.Cos(angle);
            var y = cy + radius * Math.Sin(angle);
            if (i == 0)
            {
                pathBuilder.Append(CultureInfo.InvariantCulture, $"M {x:0.##} {y:0.##}");
            }
            else
            {
                pathBuilder.Append(CultureInfo.InvariantCulture, $" L {x:0.##} {y:0.##}");
            }
        }
        pathBuilder.Append(" Z");

        // Draw filled polygon using path
        // Convert color to semi-transparent by using rgba
        var fillColor = ColorToRgba(color, 0.3);
        builder.AddPath(
            pathBuilder.ToString(),
            fill: fillColor,
            stroke: color,
            strokeWidth: 2);

        // Draw points
        for (var i = 0; i < Math.Min(curve.Values.Count, axisCount); i++)
        {
            var value = curve.Values[i];
            var normalizedValue = (value - minValue) / (maxValue - minValue);
            var radius = chartRadius * normalizedValue;

            var angle = 2 * Math.PI * i / axisCount - Math.PI / 2;
            var x = cx + radius * Math.Cos(angle);
            var y = cy + radius * Math.Sin(angle);

            builder.AddCircle(x, y, 4, fill: color, stroke: "white", strokeWidth: 1);
        }
    }

    static void DrawLegend(SvgBuilder builder, List<RadarCurve> curves, double x, double y, RenderOptions options)
    {
        for (var i = 0; i < curves.Count; i++)
        {
            var legendY = y + i * legendHeight;
            var color = curveColors[i % curveColors.Length];

            builder.AddRect(x, legendY, 16, 12, fill: color);
            builder.AddText(
                x + 24,
                legendY + 6,
                curves[i].Label ?? curves[i].Id,
                anchor: "start",
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily);
        }
    }

    static string ColorToRgba(string hexColor, double alpha)
    {
        if (!hexColor.StartsWith('#') || hexColor.Length != 7)
            return hexColor;

        var r = Convert.ToInt32(hexColor.Substring(1, 2), 16);
        var g = Convert.ToInt32(hexColor.Substring(3, 2), 16);
        var b = Convert.ToInt32(hexColor.Substring(5, 2), 16);

        return $"rgba({r},{g},{b},{alpha.ToString("0.##", CultureInfo.InvariantCulture)})";
    }
}
