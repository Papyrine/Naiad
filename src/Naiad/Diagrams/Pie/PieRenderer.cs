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
                return $"{_.Label} [{(int) _.Value}]";
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
        builder.AddStyles(MermaidStyles.PieStyles);

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
            var color = section.Color ?? defaultColors[i % defaultColors.Length];

            var path = CreateMermaidArcPath(startAngle, sweepAngle);
            builder.AddPath(path, fill: color, cssClass: "pieCircle");

            startAngle += sweepAngle;
        }

        // Add percentage labels with Mermaid exact positions
        startAngle = 0;
        foreach (var section in model.Sections)
        {
            var sweepAngle = section.Value / total * 360;
            var percentage = (int)Math.Round(section.Value / total * 100);

            // Mermaid uses a label radius factor of 0.75 (138.75 / 185)
            var midAngle = startAngle + sweepAngle / 2;
            const double labelDist = radius * 0.75; // Exact mermaid factor
            var labelX = labelDist * Math.Sin(ToRadians(midAngle));
            var labelY = -labelDist * Math.Cos(ToRadians(midAngle));

            builder.AddText(
                0,
                0,
                $"{percentage}%",
                cssClass: "slice",
                style: "text-anchor: middle;",
                transform: $"translate({FmtMermaid(labelX)},{FmtMermaid(labelY)})",
                omitXY: true);

            startAngle += sweepAngle;
        }

        // Title (empty element if no title - self-closing)
        builder.AddText(0, -200, model.Title ?? "", cssClass: "pieTitleText");

        // Legend items
        var legendStartY = -(model.Sections.Count * 22) / 2;
        for (var i = 0; i < model.Sections.Count; i++)
        {
            var section = model.Sections[i];
            var colorRgb = GetRgbColor(section.Color, i);
            var itemY = legendStartY + i * 22;

            builder.BeginGroup(cssClass: "legend", transform: $"translate(216,{itemY})");
            builder.AddRectNoXY(18, 18, style: $"fill: {colorRgb}; stroke: {colorRgb};");
            builder.AddText(22, 14, legendLabels[i]);
            builder.EndGroup();
        }

        builder.EndGroup();

        return builder.Build();
    }

    static string CreateMermaidArcPath(double startAngle, double sweepAngle)
    {
        // Mermaid uses: M startX,startY A r,r 0 largeArc,1 endX,endY L 0,0 Z
        // Angles start from top (12 o'clock) and go clockwise
        var startRad = ToRadians(startAngle);
        var endRad = ToRadians(startAngle + sweepAngle);
        var largeArc = sweepAngle > 180 ? 1 : 0;

        var x1 = radius * Math.Sin(startRad);
        var y1 = -radius * Math.Cos(startRad);
        var x2 = radius * Math.Sin(endRad);
        var y2 = -radius * Math.Cos(endRad);

        // Fix -0 to 0 (happens at 360 degree angles due to floating point)
        if (Math.Abs(x2) < 1e-10) x2 = 0;
        if (Math.Abs(y2) < 1e-10) y2 = 0;

        // Match mermaid's precision (2-3 decimal places)
        return string.Create(
            CultureInfo.InvariantCulture,
            $"M{Math.Round(x1, 3):0.###},{Math.Round(y1, 3):0.###}A{radius},{radius},0,{largeArc},1,{Math.Round(x2, 3):0.###},{Math.Round(y2, 3):0.###}L0,0Z");
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
