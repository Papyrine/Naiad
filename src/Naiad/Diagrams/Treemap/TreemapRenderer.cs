namespace Naiad.Diagrams.Treemap;

public class TreemapRenderer : IDiagramRenderer<TreemapModel>
{
    const double titleHeight = 30;
    const double defaultWidth = 600;
    const double defaultHeight = 400;

    static string[] colors =
    [
        "#8dd3c7", "#ffffb3", "#bebada", "#fb8072",
        "#80b1d3", "#fdb462", "#b3de69", "#fccde5",
        "#d9d9d9", "#bc80bd", "#ccebc5", "#ffed6f"
    ];

    public SvgDocument Render(TreemapModel model, RenderOptions options)
    {
        if (model.RootNodes.Count == 0)
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
        var chartWidth = defaultWidth - options.Padding * 2;
        var chartHeight = defaultHeight - options.Padding * 2 - titleOffset;

        var builder = new SvgBuilder();
        builder.Size(defaultWidth, defaultHeight);

        // Draw title
        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                defaultWidth / 2,
                options.Padding + titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize + 4,
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        var x = options.Padding;
        var y = options.Padding + titleOffset;
        var colorIndex = 0;

        DrawNodes(builder, model.RootNodes, x, y, chartWidth, chartHeight, 0, ref colorIndex, options);

        return builder.Build();
    }

    void DrawNodes(SvgBuilder builder, List<TreemapNode> nodes, double x, double y,
        double width, double height, int depth, ref int colorIndex, RenderOptions options)
    {
        if (nodes.Count == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        foreach (var (node, nodeX, nodeY, nodeWidth, nodeHeight) in Squarify(nodes, x, y, width, height))
        {
            if (nodeWidth < 2 || nodeHeight < 2)
            {
                continue;
            }

            var color = colors[colorIndex % colors.Length];
            colorIndex++;

            if (node.IsLeaf)
            {
                // Draw leaf rectangle
                DrawLeafNode(builder, node, nodeX, nodeY, nodeWidth, nodeHeight, color, options);
            }
            else
            {
                // Draw parent outline and recurse
                DrawParentNode(builder, node, nodeX, nodeY, nodeWidth, nodeHeight, color, depth, ref colorIndex, options);
            }
        }
    }

    // d3's default target aspect ratio for a squarified treemap.
    static readonly double goldenRatio = (1 + Math.Sqrt(5)) / 2;

    /// <summary>
    /// Divides a rectangle among the nodes in proportion to their values with the squarified algorithm
    /// (Bruls, Huizing and van Wijk), as d3's treemapSquarify does for Mermaid. Largest first, nodes are
    /// laid in rows along the rectangle's shorter side, and a row takes another node only while that keeps
    /// its cells closer to the golden ratio, so cells stay near square rather than becoming slivers.
    /// </summary>
    static List<(TreemapNode node, double x, double y, double width, double height)> Squarify(
        List<TreemapNode> nodes, double x, double y, double width, double height)
    {
        var sorted = nodes
            .Where(_ => _.TotalValue > 0)
            .OrderByDescending(_ => _.TotalValue)
            .ToList();
        var cells = new List<(TreemapNode, double, double, double, double)>(sorted.Count);

        var remaining = sorted.Sum(_ => _.TotalValue);
        var x1 = x + width;
        var y1 = y + height;
        var start = 0;

        while (start < sorted.Count)
        {
            var dx = x1 - x;
            var dy = y1 - y;
            var alpha = Math.Max(dy / dx, dx / dy) / (remaining * goldenRatio);

            var end = start + 1;
            var rowValue = sorted[start].TotalValue;
            var minValue = rowValue;
            var maxValue = rowValue;
            var beta = rowValue * rowValue * alpha;
            var worstRatio = Math.Max(maxValue / beta, beta / minValue);

            for (; end < sorted.Count; end++)
            {
                var value = sorted[end].TotalValue;
                var newRowValue = rowValue + value;
                beta = newRowValue * newRowValue * alpha;
                var newWorstRatio = Math.Max(Math.Max(maxValue, value) / beta, beta / Math.Min(minValue, value));
                if (newWorstRatio > worstRatio)
                {
                    break;
                }

                rowValue = newRowValue;
                minValue = Math.Min(minValue, value);
                maxValue = Math.Max(maxValue, value);
                worstRatio = newWorstRatio;
            }

            // A tall rectangle takes the row as a strip across its top, a wide one as a strip down its
            // left; the cells then divide the strip along its length.
            var lastRow = end == sorted.Count;
            if (dx < dy)
            {
                var rowBottom = lastRow ? y1 : y + dy * rowValue / remaining;
                var cellX = x;
                for (var i = start; i < end; i++)
                {
                    var cellWidth = dx * sorted[i].TotalValue / rowValue;
                    cells.Add((sorted[i], cellX, y, cellWidth, rowBottom - y));
                    cellX += cellWidth;
                }

                y = rowBottom;
            }
            else
            {
                var rowRight = lastRow ? x1 : x + dx * rowValue / remaining;
                var cellY = y;
                for (var i = start; i < end; i++)
                {
                    var cellHeight = dy * sorted[i].TotalValue / rowValue;
                    cells.Add((sorted[i], x, cellY, rowRight - x, cellHeight));
                    cellY += cellHeight;
                }

                x = rowRight;
            }

            remaining -= rowValue;
            start = end;
        }

        return cells;
    }

    static void DrawLeafNode(SvgBuilder builder, TreemapNode node, double x, double y,
        double width, double height, string color, RenderOptions options)
    {
        builder.AddRect(
            x,
            y,
            width,
            height,
            fill: color,
            stroke: "#333",
            strokeWidth: 1);

        // Draw label if there's enough space
        if (width > 40 && height > 20)
        {
            // Scale font size based on cell dimensions (similar to Mermaid's approach)
            var minDimension = Math.Min(width, height);
            var labelFontSize = Math.Clamp(minDimension * 0.15, 12, 48);
            var valueFontSize = labelFontSize * 0.6;
            var spacing = labelFontSize * 0.6;

            var label = TruncateLabel(node.Name, width - 20, labelFontSize);
            builder.AddText(
                x + width / 2,
                y + height / 2 - spacing / 2,
                label,
                anchor: "middle",
                baseline: "middle",
                fontSize: labelFontSize,
                fontFamily: options.FontFamily,
                fill: "#333");

            if (node.Value.HasValue && height > labelFontSize + valueFontSize + 10)
            {
                builder.AddText(
                    x + width / 2,
                    y + height / 2 + spacing,
                    node.Value.Value.ToString("0.#"),
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: valueFontSize,
                    fontFamily: options.FontFamily,
                    fill: "#666");
            }
        }
    }

    void DrawParentNode(SvgBuilder builder, TreemapNode node, double x, double y,
        double width, double height, string color, int depth, ref int colorIndex, RenderOptions options)
    {
        // Draw section header
        var headerHeight = Math.Min(20, height * 0.2);

        builder.AddRect(
            x,
            y,
            width,
            headerHeight,
            fill: DarkenColor(color, 0.2),
            stroke: "#333",
            strokeWidth: 1);

        if (width > 40)
        {
            // Section name (left-aligned)
            var label = TruncateLabel(node.Name, width - 50, options.FontSize - 2);
            builder.AddText(
                x + 5,
                y + headerHeight / 2,
                label,
                anchor: "start",
                baseline: "middle",
                fontSize: Math.Min(options.FontSize - 2, 10),
                fontFamily: options.FontFamily,
                fill: "#333",
                fontWeight: "bold");

            // Section total (right-aligned)
            builder.AddText(
                x + width - 5,
                y + headerHeight / 2,
                node.TotalValue.ToString("0.#"),
                anchor: "end",
                baseline: "middle",
                fontSize: Math.Min(options.FontSize - 2, 10),
                fontFamily: options.FontFamily,
                fill: "#333",
                fontWeight: "bold");
        }

        // Draw children in remaining space
        var childY = y + headerHeight;
        var childHeight = height - headerHeight;

        if (childHeight > 0)
        {
            DrawNodes(builder, node.Children, x, childY, width, childHeight, depth + 1, ref colorIndex, options);
        }
    }

    static string TruncateLabel(string text, double maxWidth, double fontSize)
    {
        var charWidth = fontSize * 0.6;
        var maxChars = (int) (maxWidth / charWidth);
        if (text.Length <= maxChars)
        {
            return text;
        }

        if (maxChars <= 3)
        {
            return "";
        }

        return text[..(maxChars - 3)] + "...";
    }

    static string DarkenColor(string hexColor, double factor)
    {
        if (!hexColor.StartsWith('#') || hexColor.Length != 7)
        {
            return hexColor;
        }

        var r = (int)(Convert.ToInt32(hexColor.Substring(1, 2), 16) * (1 - factor));
        var g = (int)(Convert.ToInt32(hexColor.Substring(3, 2), 16) * (1 - factor));
        var b = (int)(Convert.ToInt32(hexColor.Substring(5, 2), 16) * (1 - factor));

        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
