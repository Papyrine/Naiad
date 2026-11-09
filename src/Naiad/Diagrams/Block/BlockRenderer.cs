namespace Naiad.Diagrams.Block;

/// <summary>
/// Lays blocks out the way Mermaid does. Every container (the diagram, or a composite) is a grid whose
/// columns share one width, wide enough for the widest block in it, and whose blocks share one height. A
/// block spanning columns takes their width and the gaps between them. A composite is as big as its own grid
/// needs, and when its cell is wider it widens its columns to fill it. Shapes that are boxes fill their cell;
/// circles, diamonds and block arrows keep the size their label gives them, centred in it.
/// </summary>
public class BlockRenderer : IDiagramRenderer<BlockModel>
{
    // Mermaid's block padding: the gap between cells, and around what a composite holds.
    const double gap = 8;
    const double titleHeight = 40;
    const double doubleCircleGap = 5;

    const string blockStroke = "#333";
    const string compositeFill = "rgba(255,255,222,0.5)";
    const string compositeStroke = "#aaaa33";

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

        // The diagram is a container like a composite, without the frame or the margin inside it.
        var root = new Box(null);
        MeasureContainer(root, model.Elements, model.Columns, null, 0, options);

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;
        Arrange(root, 0, titleOffset, root.NaturalWidth, 0);

        var width = root.NaturalWidth;
        var height = root.NaturalHeight + titleOffset;

        var builder = new SvgBuilder();
        builder.Options(options);
        builder.Size(width, height);
        builder.Padding(options.Padding);
        builder.AddStyles(MermaidStyles.FlowchartStyles);

        var coloredMarkers = FlowchartRenderer.AddEdgeMarkers(builder, model.Edges);

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                width / 2,
                titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize + 4,
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        var colorIndex = 0;
        DrawChildren(builder, root, ref colorIndex);

        // Links go on top, as in Mermaid: a link from a block inside a composite crosses the composite.
        var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
        Index(root, boxes);
        foreach (var edge in model.Edges)
        {
            if (Route(edge, boxes) is { } routed)
            {
                FlowchartRenderer.RenderEdge(builder, routed, options.FontSize, coloredMarkers);
            }
        }

        return builder.Build();
    }

    // Sizes a container's grid around what it holds. `margin` is the room left around the grid.
    static void MeasureContainer(Box box, List<BlockElement> elements, int columns, string? label, double margin, RenderOptions options)
    {
        foreach (var element in elements)
        {
            box.Children.Add(Measure(element, options));
        }

        // A column count larger than what the blocks fill would leave empty columns, so the grid is no wider
        // than its blocks need.
        var spanSum = Math.Max(1, box.Children.Sum(_ => _.Element!.Span));
        var cols = spanSum;
        if (columns > 0)
        {
            cols = Math.Min(columns, spanSum);
        }

        box.Columns = cols;

        // A block too wide for what is left of its row starts the next one.
        var row = 0;
        var column = 0;
        foreach (var child in box.Children)
        {
            var span = Math.Min(child.Element!.Span, cols);
            if (column + span > cols)
            {
                row++;
                column = 0;
            }

            child.Row = row;
            child.Column = column;
            child.Span = span;
            column += span;
            if (column >= cols)
            {
                row++;
                column = 0;
            }
        }

        var shown = box.Children.Where(_ => !_.Element!.IsSpace).ToList();
        box.ColumnWidth = shown.Count == 0 ? 0 : shown.Max(_ => _.NaturalWidth / _.Span);
        box.CellHeight = shown.Count == 0 ? 0 : shown.Max(_ => _.NaturalHeight);

        var rows = box.Children.Count == 0 ? 0 : box.Children.Max(_ => _.Row) + 1;
        box.RowHeights = new double[rows];
        foreach (var child in box.Children)
        {
            var childHeight = box.CellHeight;
            if (child.Element!.IsComposite)
            {
                childHeight = child.NaturalHeight;
            }

            box.RowHeights[child.Row] = Math.Max(box.RowHeights[child.Row], childHeight);
        }

        if (label is not null)
        {
            box.Label = MeasureLabel(label, options);
            if (box.Label.Height > 0)
            {
                box.LabelBand = box.Label.Height + gap;
            }
        }

        var gridWidth = cols * box.ColumnWidth + (cols - 1) * gap;
        var gridHeight = box.RowHeights.Sum() + Math.Max(0, rows - 1) * gap;
        box.NaturalWidth = Math.Max(gridWidth + margin * 2, box.Label.Width + gap * 2);
        box.NaturalHeight = gridHeight + margin * 2 + box.LabelBand;
    }

    static Box Measure(BlockElement element, RenderOptions options)
    {
        var box = new Box(element);
        if (element.IsSpace)
        {
            return box;
        }

        if (element.IsComposite)
        {
            MeasureContainer(box, element.Children, element.Columns, element.Label, gap, options);
            return box;
        }

        box.Label = MeasureLabel(element.Label ?? element.Id, options);
        var size = NaturalSize(element.Shape, box.Label);
        box.NaturalWidth = size.Width;
        box.NaturalHeight = size.Height;
        return box;
    }

    // The size of each shape around its label, after Mermaid's own shapes. Where Mermaid leaves only a few
    // pixels beside the label (the subroutine's bars, the cylinder's sides) there is a little more here, as
    // labels are measured from an estimate of the font rather than the font itself.
    static Size NaturalSize(BlockShape shape, Size label)
    {
        var (width, height) = label;
        switch (shape)
        {
            case BlockShape.Rounded:
                return new(width + gap * 2, height + gap * 2);
            case BlockShape.Stadium:
                return new(width + gap + (height + gap) / 2, height + gap);
            case BlockShape.Subroutine:
                return new(width + gap * 4, height + gap);
            case BlockShape.Cylinder:
                var cylinderWidth = width + gap * 2;
                return new(cylinderWidth, height + gap + CylinderRy(cylinderWidth) * 3);
            case BlockShape.Circle:
                var diameter = Math.Sqrt(width * width + height * height) + gap;
                return new(diameter, diameter);
            case BlockShape.DoubleCircle:
                var outer = Math.Sqrt(width * width + height * height) + gap + doubleCircleGap * 2;
                return new(outer, outer);
            case BlockShape.Diamond:
                var side = width + height + gap * 2;
                return new(side, side);
            case BlockShape.Hexagon:
                return new(width + gap + (height + gap) / 2, height + gap);
            case BlockShape.LeanRight:
            case BlockShape.LeanLeft:
            case BlockShape.Trapezoid:
            case BlockShape.InvertedTrapezoid:
                return new(width + gap + height + gap * 2, height + gap * 2);
            case BlockShape.Asymmetric:
                return new(width + gap * 2 + (height + gap * 2) / 4, height + gap * 2);
            case BlockShape.BlockArrow:
                return new(width + height + gap * 3, height + gap * 2);
            default:
                return new(width + gap * 4, height + gap * 2);
        }
    }

    // How flat a cylinder's ends are, from its width, as in Mermaid.
    static double CylinderRy(double width) =>
        width / 2 / (2.5 + width / 50);

    // A label of nothing but blanks takes no room, so `<[" "]>` is a bare arrow. A non-breaking space is
    // not a blank: `&nbsp;` is how a label is padded on purpose.
    static Size MeasureLabel(string label, RenderOptions options)
    {
        var text = MarkdownLabel.IsMarkdown(label) ? MarkdownLabel.Plain(label) : label;
        if (text.AsSpan().Trim(" \t\r\n").IsEmpty)
        {
            return new(0, 0);
        }

        var width = LabelLines.WidestLength(text) * options.FontSize * 0.55;
        var height = options.FontSize * (1.5 + SvgBuilder.LineHeightFactor * (LabelLines.Count(text) - 1));
        return new(width, height);
    }

    // Places a container at (x, y), `width` wide, and everything in it. A container given more width than
    // its grid needs widens its columns to fill it.
    static void Arrange(Box box, double x, double y, double width, double margin)
    {
        box.X = x;
        box.Y = y;
        box.Width = width;
        box.Height = box.NaturalHeight;
        if (box.Children.Count == 0)
        {
            return;
        }

        var cols = box.Columns;
        var columnWidth = Math.Max(box.ColumnWidth, (width - margin * 2 - (cols - 1) * gap) / cols);

        var rowTops = new double[box.RowHeights.Length];
        var top = y + margin + box.LabelBand;
        for (var row = 0; row < rowTops.Length; row++)
        {
            rowTops[row] = top;
            top += box.RowHeights[row] + gap;
        }

        foreach (var child in box.Children)
        {
            var cellX = x + margin + child.Column * (columnWidth + gap);
            var cellWidth = child.Span * columnWidth + (child.Span - 1) * gap;
            var rowTop = rowTops[child.Row];
            var rowHeight = box.RowHeights[child.Row];

            if (child.Element!.IsComposite)
            {
                Arrange(child, cellX, rowTop + (rowHeight - child.NaturalHeight) / 2, cellWidth, gap);
                continue;
            }

            child.X = cellX;
            child.Y = rowTop + (rowHeight - box.CellHeight) / 2;
            child.Width = cellWidth;
            child.Height = box.CellHeight;
        }
    }

    static void DrawChildren(SvgBuilder builder, Box container, ref int colorIndex)
    {
        foreach (var child in container.Children)
        {
            var element = child.Element!;
            if (element.IsSpace)
            {
                continue;
            }

            if (element.IsComposite)
            {
                DrawComposite(builder, child);
                DrawChildren(builder, child, ref colorIndex);
                continue;
            }

            // A space holds its place in the grid and takes no colour from the cycle.
            var color = blockColors[colorIndex % blockColors.Length];
            colorIndex++;
            DrawBlock(builder, child, color);
        }
    }

    static void DrawComposite(SvgBuilder builder, Box box)
    {
        var element = box.Element!;
        var style = element.Style;
        builder.AddPath(
            ShapePathGenerator.GetPath(NodeShape.Rectangle, box.X, box.Y, box.Width, box.Height),
            fill: style?.Fill ?? compositeFill,
            stroke: style?.Stroke ?? compositeStroke,
            strokeWidth: style?.StrokeWidth ?? 1,
            strokeDasharray: style?.StrokeDasharray,
            cssClass: "composite");

        if (box.LabelBand > 0)
        {
            AddLabel(builder, element.Label!, style, box.X, box.Y + gap, box.Width, box.Label.Height);
        }
    }

    static void DrawBlock(SvgBuilder builder, Box box, string color)
    {
        var element = box.Element!;
        var style = element.Style;
        var (x, y, width, height) = ShapeBounds(box);

        // The box the label is centred in: the part of the shape clear of a notch or a cylinder's top.
        var labelX = x;
        var labelY = y;
        var labelWidth = width;
        var labelHeight = height;

        string path;
        switch (element.Shape)
        {
            case BlockShape.Rounded:
                path = ShapePathGenerator.GetPath(NodeShape.RoundedRectangle, x, y, width, height);
                break;
            case BlockShape.Stadium:
                path = ShapePathGenerator.GetPath(NodeShape.Stadium, x, y, width, height);
                break;
            case BlockShape.Subroutine:
                path = ShapePathGenerator.GetPath(NodeShape.Subroutine, x, y, width, height);
                break;
            case BlockShape.Circle:
                path = ShapePathGenerator.GetPath(NodeShape.Circle, x, y, width, height);
                break;
            case BlockShape.DoubleCircle:
                path = ShapePathGenerator.GetPath(NodeShape.Circle, x, y, width, height) + " " +
                       ShapePathGenerator.GetPath(
                           NodeShape.Circle,
                           x + doubleCircleGap,
                           y + doubleCircleGap,
                           width - doubleCircleGap * 2,
                           height - doubleCircleGap * 2);
                break;
            case BlockShape.Diamond:
                path = ShapePathGenerator.GetPath(NodeShape.Diamond, x, y, width, height);
                break;
            case BlockShape.Cylinder:
                var ry = Math.Min(CylinderRy(width), height / 4);
                path = Cylinder(x, y, width, height, ry);
                labelY = y + ry;
                labelHeight = height - ry;
                break;
            case BlockShape.Hexagon:
                // The slant comes from the label's own height, so a block stretched to a tall row keeps it.
                var slant = Math.Min(box.NaturalHeight, height) / 4;
                path = Polygon(
                    (x + slant, y),
                    (x + width - slant, y),
                    (x + width, y + height / 2),
                    (x + width - slant, y + height),
                    (x + slant, y + height),
                    (x, y + height / 2));
                break;
            case BlockShape.LeanRight:
            case BlockShape.LeanLeft:
            case BlockShape.Trapezoid:
            case BlockShape.InvertedTrapezoid:
                path = Slanted(element.Shape, x, y, width, height, Math.Min(box.NaturalHeight, height) / 2);
                break;
            case BlockShape.Asymmetric:
                var notch = Math.Min(box.NaturalHeight, height) / 4;
                path = Polygon(
                    (x, y),
                    (x + width, y),
                    (x + width, y + height),
                    (x, y + height),
                    (x + notch, y + height / 2));
                labelX = x + notch;
                labelWidth = width - notch;
                break;
            case BlockShape.BlockArrow:
                path = Polygon(BlockArrow.Points(element.ArrowDirections, width, height, gap / 2)
                    .Select(_ => (x + _.X, y + height + _.Y))
                    .ToArray());
                break;
            default:
                path = ShapePathGenerator.GetPath(NodeShape.Rectangle, x, y, width, height);
                break;
        }

        builder.AddPath(
            path,
            fill: style?.Fill ?? color,
            stroke: style?.Stroke ?? blockStroke,
            strokeWidth: style?.StrokeWidth ?? 1,
            strokeDasharray: style?.StrokeDasharray);

        AddLabel(builder, element.Label ?? element.Id, style, labelX, labelY, labelWidth, labelHeight);
    }

    // Where a block's shape is drawn: its whole cell, or for the shapes that keep their own size, that size
    // centred in the cell.
    static (double X, double Y, double Width, double Height) ShapeBounds(Box box)
    {
        if (box.Element!.Shape is BlockShape.Circle or BlockShape.DoubleCircle or BlockShape.Diamond or BlockShape.BlockArrow &&
            !box.Element.IsComposite)
        {
            return (
                box.X + (box.Width - box.NaturalWidth) / 2,
                box.Y + (box.Height - box.NaturalHeight) / 2,
                box.NaturalWidth,
                box.NaturalHeight);
        }

        return (box.X, box.Y, box.Width, box.Height);
    }

    static void AddLabel(SvgBuilder builder, string label, NodeStyle? style, double x, double y, double width, double height)
    {
        string html;
        string plain;
        if (MarkdownLabel.IsMarkdown(label))
        {
            html = MarkdownLabel.Html(label);
            plain = MarkdownLabel.Plain(label);
        }
        else
        {
            html = string.Join("<br/>", LabelLines.Split(label).Select(WebUtility.HtmlEncode));
            plain = label;
        }

        if (style?.Color is { } color)
        {
            html = $"<p style=\"color:{color}\">{html}</p>";
        }
        else
        {
            html = $"<p>{html}</p>";
        }

        builder.AddLabel(x, y, width, height, html, plain, className: "nodeLabel");
    }

    static string Cylinder(double x, double y, double width, double height, double ry)
    {
        var rx = width / 2;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
             M{x:0.##},{y + ry:0.##}
             A{rx:0.##},{ry:0.##} 0 0 0 {x + width:0.##},{y + ry:0.##}
             A{rx:0.##},{ry:0.##} 0 0 0 {x:0.##},{y + ry:0.##}
             L{x:0.##},{y + height - ry:0.##}
             A{rx:0.##},{ry:0.##} 0 0 0 {x + width:0.##},{y + height - ry:0.##}
             L{x + width:0.##},{y + ry:0.##}
             """);
    }

    // The parallelograms lean their top edge right (`[/ /]`) or left (`[\ \]`); the trapezoids are narrow
    // at the top (`[/ \]`) or at the bottom (`[\ /]`).
    static string Slanted(BlockShape shape, double x, double y, double width, double height, double slant)
    {
        var (topLeft, topRight, bottomRight, bottomLeft) = shape switch
        {
            BlockShape.LeanRight => (slant, width, width - slant, 0d),
            BlockShape.LeanLeft => (0d, width - slant, width, slant),
            BlockShape.Trapezoid => (slant, width - slant, width, 0d),
            _ => (0d, width, width - slant, slant)
        };

        return Polygon(
            (x + topLeft, y),
            (x + topRight, y),
            (x + bottomRight, y + height),
            (x + bottomLeft, y + height));
    }

    static string Polygon(params (double X, double Y)[] points)
    {
        var path = new StringBuilder();
        foreach (var (x, y) in points)
        {
            path.Append(path.Length == 0 ? "M" : " L");
            path.Append(CultureInfo.InvariantCulture, $"{x:0.##},{y:0.##}");
        }

        path.Append(" Z");
        return path.ToString();
    }

    static void Index(Box container, Dictionary<string, Box> boxes)
    {
        foreach (var child in container.Children)
        {
            if (child.Element!.IsSpace)
            {
                continue;
            }

            boxes[child.Element.Id] = child;
            Index(child, boxes);
        }
    }

    // A link runs straight from the centre of one block towards the centre of the other, from where it
    // leaves the first block's shape to where it meets the second's. Its label sits halfway between the
    // centres, as in Mermaid.
    static Edge? Route(Edge edge, Dictionary<string, Box> boxes)
    {
        if (!boxes.TryGetValue(edge.SourceId, out var source) ||
            !boxes.TryGetValue(edge.TargetId, out var target) ||
            ReferenceEquals(source, target))
        {
            return null;
        }

        var from = Center(source);
        var to = Center(target);
        var start = Clip(source, from, to);
        var end = Clip(target, to, from);
        if (start == end)
        {
            return null;
        }

        var routed = new Edge
        {
            SourceId = edge.SourceId,
            TargetId = edge.TargetId,
            Label = edge.Label,
            Type = edge.Type,
            LineStyle = edge.LineStyle,
            Style = edge.Style
        };
        routed.Points.Add(start);
        routed.Points.Add(end);
        routed.LabelPosition = new((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        return routed;
    }

    static Position Center(Box box) =>
        new(box.X + box.Width / 2, box.Y + box.Height / 2);

    // Where the line from the block's centre towards a point leaves the block's shape. A point inside the
    // shape leaves the line at the centre.
    static Position Clip(Box box, Position center, Position toward)
    {
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            return center;
        }

        var (_, _, width, height) = ShapeBounds(box);
        var halfWidth = width / 2;
        var halfHeight = height / 2;

        double reach;
        var shape = box.Element!.IsComposite ? BlockShape.Rectangle : box.Element.Shape;
        switch (shape)
        {
            case BlockShape.Circle:
            case BlockShape.DoubleCircle:
                reach = halfWidth / length;
                break;
            case BlockShape.Diamond:
                reach = 1 / (Math.Abs(dx) / halfWidth + Math.Abs(dy) / halfHeight);
                break;
            default:
                var acrossX = Math.Abs(dx) < 1e-9 ? double.MaxValue : halfWidth / Math.Abs(dx);
                var acrossY = Math.Abs(dy) < 1e-9 ? double.MaxValue : halfHeight / Math.Abs(dy);
                reach = Math.Min(acrossX, acrossY);
                break;
        }

        if (reach >= 1)
        {
            return center;
        }

        return new(center.X + dx * reach, center.Y + dy * reach);
    }

    // A block or container as it is measured and placed. For a block, X/Y/Width/Height are its cell; for a
    // composite, its frame.
    sealed class Box(BlockElement? element)
    {
        public BlockElement? Element { get; } = element;
        public Size Label { get; set; }
        public double NaturalWidth { get; set; }
        public double NaturalHeight { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        // Where the block sits in its container's grid.
        public int Row { get; set; }
        public int Column { get; set; }
        public int Span { get; set; }

        // For a container: what it holds, and its grid.
        public List<Box> Children { get; } = [];
        public int Columns { get; set; }
        public double ColumnWidth { get; set; }
        public double CellHeight { get; set; }
        public double[] RowHeights { get; set; } = [];
        public double LabelBand { get; set; }
    }
}
