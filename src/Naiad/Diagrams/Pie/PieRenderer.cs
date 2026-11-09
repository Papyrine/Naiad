namespace Naiad.Diagrams.Pie;

public class PieRenderer : IDiagramRenderer<PieModel>
{
    // Mermaid default pie colors
    static readonly string[] defaultColors =
    [
        "#ECECFF",                          // lavender
        "#ffffde",                          // light yellow
        "hsl(80, 100%, 56.2745098039%)",    // lime green
        "hsl(240, 100%, 86.2745098039%)",   // light blue
        "hsl(0, 100%, 86.2745098039%)",     // light red
        "hsl(160, 100%, 56.2745098039%)",   // teal
        "hsl(40, 100%, 56.2745098039%)",    // orange
        "hsl(280, 100%, 76.2745098039%)"    // purple
    ];

    // RGB equivalents for legend (mermaid converts HSL to RGB for style attribute)
    static readonly string[] defaultColorRgb =
    [
        "rgb(236, 236, 255)",               // #ECECFF
        "rgb(255, 255, 222)",               // #ffffde
        "rgb(181, 255, 32)",                // hsl(80, 100%, 56.27%)
        "rgb(185, 185, 255)",               // hsl(240, 100%, 86.27%)
        "rgb(255, 185, 185)",               // hsl(0, 100%, 86.27%)
        "rgb(32, 255, 181)",                // hsl(160, 100%, 56.27%)
        "rgb(255, 181, 32)",                // hsl(40, 100%, 56.27%)
        "rgb(215, 134, 255)"                // hsl(280, 100%, 76.27%)
    ];

    const double radius = 185.0;
    const double outerRadius = 186.0;

    public SvgDocument Render(PieModel model, RenderOptions options)
    {
        // `pie.donutHole` cuts a hole of that share of the radius out of the middle; Mermaid takes it only
        // between 0 and 0.9. `pie.textPosition` places the percentages from the centre (0) to the rim (1).
        var donutHole = model.Config.GetNumber("pie", "donutHole") ?? 0;
        var innerRadius = 0.0;
        if (donutHole is > 0 and <= 0.9)
        {
            innerRadius = donutHole * radius;
        }

        var textPosition = model.Config.GetNumber("pie", "textPosition") ?? 0.75;

        // A configured theme (or theme variables) gives the slices `pie1` to `pie12` and the text and
        // strokes their pie variables; without one the default theme's colours below are used as they are.
        var theme = MermaidTheme.From(model.Config, "pie");

        var total = model.Sections.Sum(_ => _.Value);
        if (total == 0)
        {
            total = 1;
        }

        // Calculate legend labels (with values if showData is enabled)
        var legendLabels = model.Sections.Select(_ =>
        {
            if (model.ShowData)
            {
                return $"{_.Label} [{_.Value.ToString(CultureInfo.InvariantCulture)}]";
            }

            return _.Label;
        }).ToList();

        // Match Mermaid exact dimensions - width varies based on legend text
        var width = model.ShowData ? 613.140625 : 551.6875;
        const double height = 450.0;
        const double cx = 225.0;
        const double cy = 225.0;

        var builder = new SvgBuilder();
        builder.Size(width, height);
        builder.DiagramType(null!, "pie");
        builder.AddStyles(Styles(theme));

        // Empty group (Mermaid artifact)
        builder.BeginGroup();
        builder.EndGroup();

        // Main pie group with transform to center
        builder.BeginGroup(transform: string.Create(CultureInfo.InvariantCulture, $"translate({cx:0.##},{cy:0.##})"));

        // Outer circle
        builder.AddCircle(0, 0, outerRadius, cssClass: "pieOuterCircle");

        // Draw pie slices
        double startAngle = 0;
        for (var i = 0; i < model.Sections.Count; i++)
        {
            var section = model.Sections[i];
            var sweepAngle = section.Value / total * 360;
            var color = section.Color ?? SliceColor(theme, i);

            // A section with nothing in it has no slice to draw.
            if (sweepAngle > 0)
            {
                var path = CreateMermaidArcPath(startAngle, sweepAngle, innerRadius);
                builder.AddPath(path, fill: color, cssClass: "pieCircle");
            }

            startAngle += sweepAngle;
        }

        // Add percentage labels with Mermaid exact positions
        startAngle = 0;
        foreach (var section in model.Sections)
        {
            var sweepAngle = section.Value / total * 360;
            var percentage = (int)Math.Round(section.Value / total * 100);

            var midAngle = startAngle + sweepAngle / 2;
            var labelDist = radius * textPosition;
            var labelX = labelDist * Math.Sin(ToRadians(midAngle));
            var labelY = -labelDist * Math.Cos(ToRadians(midAngle));

            // As in Mermaid, a slice too thin to round up to 1% carries no label; several of them would
            // only pile "0%" on top of each other.
            if (percentage > 0)
            {
                builder.AddText(
                    0,
                    0,
                    $"{percentage}%",
                    cssClass: "slice",
                    style: "text-anchor: middle;",
                    transform: $"translate({FmtMermaid(labelX)},{FmtMermaid(labelY)})",
                    omitXY: true);
            }

            startAngle += sweepAngle;
        }

        // Title (empty element if no title - self-closing)
        builder.AddText(0, -200, model.Title ?? "", cssClass: "pieTitleText");

        // Legend items
        var legendStartY = -(model.Sections.Count * 22) / 2;
        for (var i = 0; i < model.Sections.Count; i++)
        {
            var section = model.Sections[i];
            var colorRgb = GetRgbColor(section.Color ?? ThemedColor(theme, i), i);
            var itemY = legendStartY + i * 22;

            builder.BeginGroup(cssClass: "legend", transform: $"translate(216,{itemY})");
            builder.AddRectNoXY(18, 18, style: $"fill: {colorRgb}; stroke: {colorRgb};");
            builder.AddText(22, 14, legendLabels[i]);
            builder.EndGroup();
        }

        builder.EndGroup();

        return builder.Build();
    }

    static string CreateMermaidArcPath(double startAngle, double sweepAngle, double innerRadius)
    {
        // Mermaid uses: M startX,startY A r,r 0 largeArc,1 endX,endY L 0,0 Z, or with a donut hole runs
        // back along the inner arc instead of to the centre.
        // Angles start from top (12 o'clock) and go clockwise
        // A slice that is the whole pie starts and ends at the same point, and an arc between a point
        // and itself draws nothing. Two half circles do.
        if (sweepAngle >= 360 - 1e-9)
        {
            if (innerRadius > 0)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"M0,-{radius}A{radius},{radius},0,1,1,0,{radius}A{radius},{radius},0,1,1,0,-{radius}M0,-{innerRadius:0.###}A{innerRadius:0.###},{innerRadius:0.###},0,1,0,0,{innerRadius:0.###}A{innerRadius:0.###},{innerRadius:0.###},0,1,0,0,-{innerRadius:0.###}Z");
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"M0,-{radius}A{radius},{radius},0,1,1,0,{radius}A{radius},{radius},0,1,1,0,-{radius}Z");
        }

        var startRad = ToRadians(startAngle);
        var endRad = ToRadians(startAngle + sweepAngle);
        var largeArc = sweepAngle > 180 ? 1 : 0;

        var (x1, y1) = PointAt(radius, startRad);
        var (x2, y2) = PointAt(radius, endRad);

        // Match mermaid's precision (2-3 decimal places)
        var outer = string.Create(
            CultureInfo.InvariantCulture,
            $"M{x1:0.###},{y1:0.###}A{radius},{radius},0,{largeArc},1,{x2:0.###},{y2:0.###}");

        if (innerRadius <= 0)
        {
            return outer + "L0,0Z";
        }

        var (innerX2, innerY2) = PointAt(innerRadius, endRad);
        var (innerX1, innerY1) = PointAt(innerRadius, startRad);
        return outer +
               string.Create(
                   CultureInfo.InvariantCulture,
                   $"L{innerX2:0.###},{innerY2:0.###}A{innerRadius:0.###},{innerRadius:0.###},0,{largeArc},0,{innerX1:0.###},{innerY1:0.###}Z");
    }

    // The point at a distance from the centre and an angle clockwise from 12 o'clock, rounded as Mermaid
    // rounds, without the -0 floating point leaves at whole turns.
    static (double x, double y) PointAt(double distance, double radians)
    {
        var x = Math.Round(distance * Math.Sin(radians), 3);
        var y = Math.Round(-distance * Math.Cos(radians), 3);
        if (Math.Abs(x) < 1e-10)
        {
            x = 0;
        }

        if (Math.Abs(y) < 1e-10)
        {
            y = 0;
        }

        return (x, y);
    }

    // The theme's colour for the slice at an index, cycling through pie1 to pie12 as Mermaid's ordinal
    // scale does, or null without a theme.
    static string? ThemedColor(MermaidTheme? theme, int index) =>
        theme?[$"pie{index % 12 + 1}"];

    static string SliceColor(MermaidTheme? theme, int index) =>
        ThemedColor(theme, index) ?? defaultColors[index % defaultColors.Length];

    // Mermaid's pie styles, filled in from the theme.
    static string Styles(MermaidTheme? theme)
    {
        if (theme is null)
        {
            return MermaidStyles.PieStyles;
        }

        const string font = "\"trebuchet ms\",verdana,arial,sans-serif";
        return MermaidStyles.BaseStyles +
               $$"""
                 #naiad .pieCircle{stroke:{{theme["pieStrokeColor"]}};stroke-width:{{theme["pieStrokeWidth"]}};opacity:{{theme["pieOpacity"]}};}
                 #naiad .pieOuterCircle{stroke:{{theme["pieOuterStrokeColor"]}};stroke-width:{{theme["pieOuterStrokeWidth"]}};fill:none;}
                 #naiad .pieTitleText{text-anchor:middle;font-size:{{theme["pieTitleTextSize"]}};fill:{{theme["pieTitleTextColor"]}};font-family:{{font}};}
                 #naiad .slice{font-family:{{font}};fill:{{theme["pieSectionTextColor"]}};font-size:{{theme["pieSectionTextSize"]}};}
                 #naiad .legend text{fill:{{theme["pieLegendTextColor"]}};font-family:{{font}};font-size:{{theme["pieLegendTextSize"]}};}
                 """;
    }

    static string GetRgbColor(string? color, int index)
    {
        if (color is not null)
        {
            if (color.StartsWith("rgb"))
            {
                return color;
            }

            return ConvertToRgb(color);
        }

        return defaultColorRgb[index % defaultColorRgb.Length];
    }

    static string ConvertToRgb(string color)
    {
        if (!color.StartsWith('#') || color.Length != 7)
        {
            return color;
        }

        var r = Convert.ToInt32(color.Substring(1, 2), 16);
        var g = Convert.ToInt32(color.Substring(3, 2), 16);
        var b = Convert.ToInt32(color.Substring(5, 2), 16);
        return $"rgb({r}, {g}, {b})";
    }

    static double ToRadians(double degrees) => degrees * Math.PI / 180;

    // Use R format to get full precision, then remove unnecessary trailing zeros
    static string FmtMermaid(double value)
    {
        var s = value.ToString("R", CultureInfo.InvariantCulture);
        // R format may produce exponential notation which we don't want
        if (s.Contains('E') || s.Contains('e'))
        {
            s = value.ToString("0.#################", CultureInfo.InvariantCulture);
        }

        return s;
    }
}
