namespace Naiad.Diagrams.Block;

public class BlockRenderer : IDiagramRenderer<BlockModel>
{
    const double cellWidth = 120;
    const double cellHeight = 60;
    const double cellPadding = 10;
    const double titleHeight = 40;

    static readonly string[] blockColors =
    [
        "#E3F2FD",
        "#E8F5E9",
        "#FFF3E0",
        "#F3E5F5",
        "#FCE4EC",
        "#E0F7FA",
        "#FFF8E1",
        "#F1F8E9"
    ];

    public SvgDocument Render(BlockModel model, RenderOptions options)
    {
        if (model.Elements.Count == 0)
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

        var columns = Math.Max(1, model.Columns);
        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;

        // Calculate rows needed
        var rows = CalculateRows(model.Elements, columns);

        // A circle is inscribed in its cell, so the grid has to be at least as big as the largest circle
        // label needs; at the default cell size the text spills straight out of the shape.
        var cellW = cellWidth;
        var cellH = cellHeight;
        foreach (var element in model.Elements)
        {
            if (element.Shape != BlockShape.Circle)
            {
                continue;
            }

            var diameter = CircleDiameter(element.Label ?? element.Id, options);
            cellW = Math.Max(cellW, diameter + cellPadding * 2);
            cellH = Math.Max(cellH, diameter + cellPadding * 2);
        }

        var width = columns * cellW + options.Padding * 2;
        var height = rows * cellH + options.Padding * 2 + titleOffset;

        var builder = new SvgBuilder();
        builder.Size(width, height);

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

        // Position and draw elements
        var currentColumn = 0;
        var currentRow = 0;
        var colorIndex = 0;

        foreach (var element in model.Elements)
        {
            var span = Math.Min(element.Span, columns - currentColumn);
            if (span <= 0 ||
                currentColumn + span > columns)
            {
                currentRow++;
                currentColumn = 0;
                span = Math.Min(element.Span, columns);
            }

            var x = options.Padding + currentColumn * cellW + cellPadding;
            var y = titleOffset + options.Padding + currentRow * cellH + cellPadding;
            var blockWidth = span * cellW - cellPadding * 2;
            var blockHeight = cellH - cellPadding * 2;

            var color = blockColors[colorIndex % blockColors.Length];
            var label = element.Label ?? element.Id;

            DrawBlock(builder, x, y, blockWidth, blockHeight, label, element.Shape, color, options);

            currentColumn += span;
            if (currentColumn >= columns)
            {
                currentColumn = 0;
                currentRow++;
            }

            colorIndex++;
        }

        return builder.Build();
    }

    static int CalculateRows(List<BlockElement> elements, int columns)
    {
        var currentColumn = 0;
        var rows = 1;

        foreach (var element in elements)
        {
            var span = Math.Min(element.Span, columns);
            if (currentColumn + span > columns)
            {
                rows++;
                currentColumn = span;
            }
            else
            {
                currentColumn += span;
            }

            if (currentColumn >= columns)
            {
                rows++;
                currentColumn = 0;
            }
        }

        if (rows == 1 || currentColumn > 0)
        {
            return rows;
        }

        return rows - 1;
    }

    /// <summary>
    /// Diameter of the smallest circle that contains the label's text box, plus a little breathing room.
    /// </summary>
    static double CircleDiameter(string label, RenderOptions options)
    {
        var fontSize = options.FontSize - 1;
        var textWidth = label.Length * fontSize * 0.55;
        return Math.Sqrt(textWidth * textWidth + fontSize * fontSize) + 12;
    }

    static void DrawBlock(
        SvgBuilder builder,
        double x,
        double y,
        double width,
        double height,
        string label,
        BlockShape shape,
        string color,
        RenderOptions options)
    {
        var centerX = x + width / 2;
        var centerY = y + height / 2;

        switch (shape)
        {
            case BlockShape.Rectangle:
                builder.AddRect(
                    x,
                    y,
                    width,
                    height,
                    rx: 4,
                    fill: color,
                    stroke: "#333",
                    strokeWidth: 1);
                break;

            case BlockShape.Rounded:
                // A small corner radius. Half the height would make this a stadium, which is the
                // separate ([...]) shape.
                builder.AddRect(
                    x,
                    y,
                    width,
                    height,
                    rx: 8,
                    fill: color,
                    stroke: "#333",
                    strokeWidth: 1);
                break;

            case BlockShape.Stadium:
                builder.AddRect(
                    x,
                    y,
                    width,
                    height,
                    rx: height / 2,
                    fill: color,
                    stroke: "#333",
                    strokeWidth: 1);
                break;

            case BlockShape.Circle:
                var radius = Math.Min(width, height) / 2;
                builder.AddCircle(
                    centerX,
                    centerY,
                    radius,
                    fill: color,
                    stroke: "#333",
                    strokeWidth: 1);
                break;

            case BlockShape.Diamond:
                var diamondPath = string.Create(
                    CultureInfo.InvariantCulture,
                    $"M {centerX:0.##} {y:0.##} L {x + width:0.##} {centerY:0.##} L {centerX:0.##} {y + height:0.##} L {x:0.##} {centerY:0.##} Z");
                builder.AddPath(
                    diamondPath,
                    fill: color,
                    stroke: "#333",
                    strokeWidth: 1);
                break;

            case BlockShape.Hexagon:
                var hOffset = width * 0.15;
                var hexPath = string.Create(
                    CultureInfo.InvariantCulture,
                    $"M {x + hOffset:0.##} {y:0.##} L {x + width - hOffset:0.##} {y:0.##} L {x + width:0.##} {centerY:0.##} L {x + width - hOffset:0.##} {y + height:0.##} L {x + hOffset:0.##} {y + height:0.##} L {x:0.##} {centerY:0.##} Z");
                builder.AddPath(
                    hexPath,
                    fill: color,
                    stroke: "#333",
                    strokeWidth: 1);
                break;
        }

        // Label
        builder.AddText(
            centerX,
            centerY,
            label,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize - 1,
            fontFamily: options.FontFamily,
            fill: "#333");
    }
}