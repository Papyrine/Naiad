namespace Naiad.Diagrams.Quadrant;

// Renders a quadrant chart matching Mermaid's default theme: a fixed 500x500 grid of four lavender
// quadrants (lightened diagonally so the top reads more saturated than the bottom), a thin border
// with an internal cross, dark points labelled just below, axis labels centred under and beside
// each half (y-axis rotated), and a centred title.
public class QuadrantRenderer : IDiagramRenderer<QuadrantModel>
{
    // Mermaid's quadrantChart config. The layout is a 500x500 canvas unless `quadrantChart.chartWidth` or
    // `chartHeight` says otherwise, so these sizes are absolute rather than scaled by RenderOptions.
    const double defaultChartSize = 500;
    const double quadrantPadding = 5;
    const double titleFontSize = 20;
    const double titlePadding = 10;
    const double axisLabelFontSize = 16;
    const double axisLabelPadding = 5;
    const double quadrantLabelFontSize = 16;
    const double quadrantTextTopPadding = 5;
    const double pointRadius = 5;
    const double pointTextPadding = 5;
    const double pointLabelFontSize = 12;
    const double externalBorderWidth = 2;

    // Default-theme colours derived from Mermaid's primaryColor (#ECECFF).
    const string quadrant1Fill = "#ECECFF"; // top-right (most saturated)
    const string quadrant2Fill = "#F1F1FF"; // top-left
    const string quadrant3Fill = "#F6F6FF"; // bottom-left
    const string quadrant4Fill = "#FBFBFF"; // bottom-right (almost white)
    const string borderColor = "#C7C7F1";   // primaryBorderColor
    const string textColor = "#131300";     // primaryTextColor (invert of primaryColor)
    const string pointFill = "#333";        // Mermaid points inherit the root text fill

    public SvgDocument Render(QuadrantModel model, RenderOptions options)
    {
        var chartWidth = model.Config.GetNumber("quadrantChart", "chartWidth") ?? defaultChartSize;
        var chartHeight = model.Config.GetNumber("quadrantChart", "chartHeight") ?? defaultChartSize;

        var hasTitle = !string.IsNullOrEmpty(model.Title);
        var hasXAxis = !string.IsNullOrEmpty(model.XAxisLeft) || !string.IsNullOrEmpty(model.XAxisRight);
        var hasYAxis = !string.IsNullOrEmpty(model.YAxisBottom) || !string.IsNullOrEmpty(model.YAxisTop);

        var titleSpace = hasTitle ? titleFontSize + titlePadding * 2 : 0;
        var xAxisSpace = hasXAxis ? axisLabelFontSize + axisLabelPadding * 2 : 0;
        var yAxisSpace = hasYAxis ? axisLabelFontSize + axisLabelPadding * 2 : 0;

        // The grid fills the canvas minus the title gutter (top), the x-axis labels (below) and the
        // y-axis labels (left) — matching Mermaid's default top-title / bottom-x / left-y placement.
        var plotLeft = quadrantPadding + yAxisSpace;
        var plotTop = quadrantPadding + titleSpace;
        var plotRight = chartWidth - quadrantPadding;
        var plotBottom = chartHeight - quadrantPadding - xAxisSpace;
        var plotWidth = plotRight - plotLeft;
        var plotHeight = plotBottom - plotTop;
        var centerX = plotLeft + plotWidth / 2;
        var centerY = plotTop + plotHeight / 2;
        var halfWidth = plotWidth / 2;
        var halfHeight = plotHeight / 2;

        var builder = new SvgBuilder();
        builder.Size(chartWidth, chartHeight);

        // Quadrant fills (no stroke; the grid lines come from the border pass below).
        builder.AddRect(plotLeft, plotTop, halfWidth, halfHeight, fill: quadrant2Fill); // top-left
        builder.AddRect(centerX, plotTop, halfWidth, halfHeight, fill: quadrant1Fill);  // top-right
        builder.AddRect(plotLeft, centerY, halfWidth, halfHeight, fill: quadrant3Fill); // bottom-left
        builder.AddRect(centerX, centerY, halfWidth, halfHeight, fill: quadrant4Fill);  // bottom-right

        // Quadrant labels sit at the top of each quadrant.
        var topLabelY = plotTop + quadrantTextTopPadding + quadrantLabelFontSize / 2;
        var bottomLabelY = centerY + quadrantTextTopPadding + quadrantLabelFontSize / 2;
        AddCenteredText(centerX + halfWidth / 2, topLabelY, model.Quadrant1Label, quadrantLabelFontSize);
        AddCenteredText(plotLeft + halfWidth / 2, topLabelY, model.Quadrant2Label, quadrantLabelFontSize);
        AddCenteredText(plotLeft + halfWidth / 2, bottomLabelY, model.Quadrant3Label, quadrantLabelFontSize);
        AddCenteredText(centerX + halfWidth / 2, bottomLabelY, model.Quadrant4Label, quadrantLabelFontSize);

        // Border: external rectangle plus the internal cross (drawn at the default 1px width).
        builder.AddRect(plotLeft, plotTop, plotWidth, plotHeight, fill: "none", stroke: borderColor, strokeWidth: externalBorderWidth);
        builder.AddLine(centerX, plotTop, centerX, plotBottom, stroke: borderColor);
        builder.AddLine(plotLeft, centerY, plotRight, centerY, stroke: borderColor);

        // Points, each labelled centred just below the dot.
        foreach (var point in model.Points)
        {
            var pointX = plotLeft + point.X * plotWidth;
            var pointY = plotBottom - point.Y * plotHeight; // y grows upward
            builder.AddCircle(pointX, pointY, pointRadius, fill: pointFill);
            AddPointLabel(pointX, pointY + pointTextPadding + pointLabelFontSize / 2, point.Name);
        }

        // X-axis labels centred under each half; y-axis labels rotated beside each half.
        var xLabelY = plotBottom + xAxisSpace / 2;
        AddCenteredText(plotLeft + halfWidth / 2, xLabelY, model.XAxisLeft, axisLabelFontSize);
        AddCenteredText(centerX + halfWidth / 2, xLabelY, model.XAxisRight, axisLabelFontSize);

        var yLabelX = quadrantPadding + axisLabelFontSize / 2;
        AddRotatedText(yLabelX, plotTop + halfHeight / 2, model.YAxisTop, axisLabelFontSize);
        AddRotatedText(yLabelX, centerY + halfHeight / 2, model.YAxisBottom, axisLabelFontSize);

        // Title centred in the top gutter.
        AddCenteredText(chartWidth / 2, titlePadding + titleFontSize / 2, model.Title, titleFontSize);

        return builder.Build();

        // A point at x=0 or x=1 sits on the plot border, so its centred label would run off the canvas and
        // be clipped ("Top Right" came out as "Top R"). Nudge the label back inside instead.
        void AddPointLabel(double textX, double textY, string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var half = MeasureText(text, pointLabelFontSize) / 2;
            var minX = quadrantPadding + half;
            var maxX = chartWidth - quadrantPadding - half;
            var clampedX = maxX < minX ? chartWidth / 2 : Math.Clamp(textX, minX, maxX);

            AddCenteredText(clampedX, textY, text, pointLabelFontSize);
        }

        void AddCenteredText(double textX, double textY, string? text, double fontSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            builder.AddText(
                textX,
                textY,
                text,
                anchor: "middle",
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fill: textColor);
        }

        void AddRotatedText(double textX, double textY, string? text, double fontSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            builder.BeginGroup(transform: string.Create(CultureInfo.InvariantCulture, $"rotate(-90, {textX:0.##}, {textY:0.##})"));
            builder.AddText(
                textX,
                textY,
                text,
                anchor: "middle",
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fill: textColor);
            builder.EndGroup();
        }
    }

    static double MeasureText(string text, double fontSize) =>
        text.Length * fontSize * 0.55;
}
