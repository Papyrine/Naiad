namespace Naiad.Diagrams.Mindmap;

public class MindmapRenderer : IDiagramRenderer<MindmapModel>
{
    const double nodePadding = 15;
    const double nodeMinWidth = 60;
    const double nodeHeight = 35;
    const double horizontalSpacing = 40;
    const double verticalSpacing = 15;
    const double iconSize = 18;
    const double iconGap = 6;

    static string[] levelColors =
    [
        "#FFB6C1", // pink - root
        "#87CEEB", // sky blue - level 1
        "#98FB98", // pale green - level 2
        "#DDA0DD", // plum - level 3
        "#F0E68C", // khaki - level 4
        "#E0FFFF", // light cyan - level 5
        "#FFDAB9"  // peach - level 6+
    ];

    public SvgDocument Render(MindmapModel model, RenderOptions options)
    {
        if (model.Root == null)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "Empty mindmap",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        // Every walk goes over this list rather than recursing into the children: nesting is as deep as the
        // input's indentation, and a recursion that deep overflows the stack, which cannot be caught and
        // takes the process down.
        var nodes = PreOrder(model.Root);

        // Calculate node sizes
        foreach (var (node, _) in nodes)
        {
            CalculateNodeSize(node, options);
        }

        // Calculate subtree heights, children before their parents
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            CalculateSubtreeHeight(nodes[i].Node);
        }

        // Layout the tree
        LayoutTree(model.Root, options.Padding, options.Padding);

        // Calculate total dimensions
        var (width, height) = CalculateBounds(nodes);
        width += options.Padding * 2;
        height += options.Padding * 2;

        var builder = new SvgBuilder();
        builder.Options(options);
        builder.Size(width, height);

        // Draw connections first (behind nodes)
        foreach (var (node, parent) in nodes)
        {
            if (parent is not null)
            {
                DrawConnection(builder, parent, node);
            }
        }

        // Draw nodes
        foreach (var (node, _) in nodes)
        {
            DrawNode(builder, node, options);
        }

        return builder.Build();
    }

    // The nodes in document order, each before its children, with the node each hangs from.
    static List<(MindmapNode Node, MindmapNode? Parent)> PreOrder(MindmapNode root)
    {
        var nodes = new List<(MindmapNode Node, MindmapNode? Parent)>();
        var pending = new Stack<(MindmapNode Node, MindmapNode? Parent)>();
        pending.Push((root, null));
        while (pending.TryPop(out var entry))
        {
            nodes.Add(entry);
            var children = entry.Node.Children;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                pending.Push((children[i], entry.Node));
            }
        }

        return nodes;
    }

    static void CalculateNodeSize(MindmapNode node, RenderOptions options)
    {
        var textWidth = MeasureText(node.Text, options.FontSize);
        var iconAllowance = node.Icon is null ? 0 : iconSize + iconGap;
        node.Width = Math.Max(nodeMinWidth, textWidth + nodePadding * 2 + iconAllowance);
        node.Height = nodeHeight;

        // A circle is as tall as it is wide, and the layout has to leave it that much room or its top and
        // bottom are cut off.
        if (node.Shape == MindmapShape.Circle)
        {
            node.Height = node.Width;
        }
    }

    // Needs the subtree heights of the node's children worked out already.
    static void CalculateSubtreeHeight(MindmapNode node)
    {
        if (node.Children.Count == 0)
        {
            node.SubtreeHeight = node.Height;
            return;
        }

        double totalChildrenHeight = 0;
        foreach (var child in node.Children)
        {
            totalChildrenHeight += child.SubtreeHeight;
        }
        totalChildrenHeight += (node.Children.Count - 1) * verticalSpacing;

        node.SubtreeHeight = Math.Max(node.Height, totalChildrenHeight);
    }

    // Places each node with its subtree's top-left corner at the point its parent hands it.
    static void LayoutTree(MindmapNode root, double x, double y)
    {
        var pending = new Stack<(MindmapNode Node, double X, double Y)>();
        pending.Push((root, x, y));
        while (pending.TryPop(out var entry))
        {
            var (node, left, top) = entry;

            // Position this node
            node.Position = new(left + node.Width / 2, top + node.SubtreeHeight / 2);

            // Position children
            var childX = left + node.Width + horizontalSpacing;
            var childY = top + (node.SubtreeHeight - GetChildrenTotalHeight(node)) / 2;

            foreach (var child in node.Children)
            {
                pending.Push((child, childX, childY));
                childY += child.SubtreeHeight + verticalSpacing;
            }
        }
    }

    static double GetChildrenTotalHeight(MindmapNode node)
    {
        if (node.Children.Count == 0)
            return 0;

        return node.Children.Sum(_ => _.SubtreeHeight) + (node.Children.Count - 1) * verticalSpacing;
    }

    static (double width, double height) CalculateBounds(List<(MindmapNode Node, MindmapNode? Parent)> nodes)
    {
        var rightEdge = double.MinValue;
        var bottomEdge = double.MinValue;

        foreach (var (node, _) in nodes)
        {
            rightEdge = Math.Max(rightEdge, node.Position.X + node.Width / 2);
            bottomEdge = Math.Max(bottomEdge, node.Position.Y + node.Height / 2);
        }

        return (rightEdge, bottomEdge);
    }

    // Draws the curved connection from a parent to its child.
    static void DrawConnection(SvgBuilder builder, MindmapNode parent, MindmapNode child)
    {
        var startX = parent.Position.X + parent.Width / 2;
        var startY = parent.Position.Y;
        var endX = child.Position.X - child.Width / 2;
        var endY = child.Position.Y;

        var controlX1 = startX + horizontalSpacing / 2;
        var controlX2 = endX - horizontalSpacing / 2;

        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"M {startX:0.##} {startY:0.##} C {controlX1:0.##} {startY:0.##}, {controlX2:0.##} {endY:0.##}, {endX:0.##} {endY:0.##}");

        var color = GetLevelColor(child.Level);
        builder.AddPath(path, stroke: color, strokeWidth: 2, fill: "none");
    }

    /// <summary>
    /// Mermaid's bracket-less node: a band with rounded top corners, a square bottom and an underline along
    /// it, rather than a bordered box. Without this a `(rounded)` node and a bracket-less one rendered
    /// identically, so the syntax made no visible difference.
    /// </summary>
    static void DrawDefaultBand(
        SvgBuilder builder,
        double x,
        double y,
        double width,
        double height,
        string fill,
        string stroke)
    {
        const double corner = 5;
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"""
             M{x:0.##},{y + height:0.##}
             V{y + corner:0.##}
             Q{x:0.##},{y:0.##} {x + corner:0.##},{y:0.##}
             H{x + width - corner:0.##}
             Q{x + width:0.##},{y:0.##} {x + width:0.##},{y + corner:0.##}
             V{y + height:0.##} Z
             """);

        builder.AddPath(path, fill: fill, stroke: "none");
        builder.AddLine(x, y + height, x + width, y + height, stroke: stroke, strokeWidth: 2);
    }

    static void DrawNode(SvgBuilder builder, MindmapNode node, RenderOptions options)
    {
        var x = node.Position.X - node.Width / 2;
        var y = node.Position.Y - node.Height / 2;
        var color = GetLevelColor(node.Level);
        var strokeColor = DarkenColor(color);

        switch (node.Shape)
        {
            case MindmapShape.Circle:
                var radius = Math.Max(node.Width, node.Height) / 2;
                builder.AddCircle(
                    node.Position.X,
                    node.Position.Y,
                    radius,
                    fill: color,
                    stroke: strokeColor,
                    strokeWidth: 2);
                break;

            case MindmapShape.Square:
                builder.AddRect(
                    x,
                    y,
                    node.Width,
                    node.Height,
                    fill: color,
                    stroke: strokeColor,
                    strokeWidth: 2);
                break;

            case MindmapShape.Hexagon:
                DrawHexagon(
                    builder,
                    node.Position.X,
                    node.Position.Y,
                    node.Width,
                    node.Height,
                    color,
                    strokeColor);
                break;

            case MindmapShape.Cloud:
            case MindmapShape.Bang:
                // Simplified: draw as rounded rect with extra styling
                builder.AddRect(
                    x,
                    y,
                    node.Width,
                    node.Height,
                    rx: node.Height / 2,
                    fill: color,
                    stroke: strokeColor,
                    strokeWidth: 3);
                break;

            case MindmapShape.Rounded:
                builder.AddRect(
                    x,
                    y,
                    node.Width,
                    node.Height,
                    rx: nodePadding,
                    fill: color,
                    stroke: strokeColor,
                    strokeWidth: 2);
                break;

            case MindmapShape.Default:
            default:
                DrawDefaultBand(builder, x, y, node.Width, node.Height, color, strokeColor);
                break;
        }

        // Draw icon (left of the text) if present
        var iconAllowance = node.Icon is null ? 0 : iconSize + iconGap;
        if (node.Icon is { } icon)
        {
            DrawIcon(builder, icon, x + nodePadding, node.Position.Y - iconSize / 2, iconSize);
        }

        // Draw text, shifted right to leave room for the icon
        builder.AddText(
            node.Position.X + iconAllowance / 2,
            node.Position.Y,
            node.Text,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily,
            fontWeight: node.Level == 0 ? "bold" : "normal");
    }

    // Draws a node icon: a registered iconify pack icon (prefix:name) as inline
    // SVG, otherwise a FontAwesome class string (e.g. "fa fa-book") as an <i>.
    static void DrawIcon(SvgBuilder builder, string icon, double x, double y, double size)
    {
        if (icon.Contains(':') &&
            IconPackRegistry.Resolve(icon) is { } packIcon)
        {
            var scale = size / Math.Max(packIcon.Width, packIcon.Height);
            var drawnWidth = packIcon.Width * scale;
            var drawnHeight = packIcon.Height * scale;
            var tx = x + (size - drawnWidth) / 2;
            var ty = y + (size - drawnHeight) / 2;
            builder.AddRawSvg(string.Create(
                CultureInfo.InvariantCulture,
                $"<g transform='translate({tx:0.##},{ty:0.##}) scale({scale:0.####})' style='color:#333'>{packIcon.Body}</g>"));
            return;
        }

        // FontAwesome class string, e.g. "fa fa-book". Encode the class value (a no-op for the normal
        // dotted/spaced class names) so a crafted icon token can't break out of the attribute and inject
        // markup into the foreignObject. With HTML disabled there is no web font to draw the glyph, so the
        // icon (which carries no text) is dropped.
        builder.AddLabel(x, y, size, size, $"<i class='{WebUtility.HtmlEncode(icon)}'></i>", "");
    }

    static void DrawHexagon(SvgBuilder builder, double cx, double cy, double width, double height,
        string fill, string stroke)
    {
        var hw = width / 2;
        var hh = height / 2;
        var offset = height / 4;

        var points = new[]
        {
            (cx - hw + offset, cy - hh),
            (cx + hw - offset, cy - hh),
            (cx + hw, cy),
            (cx + hw - offset, cy + hh),
            (cx - hw + offset, cy + hh),
            (cx - hw, cy)
        };

        var pathBuilder = new StringBuilder();
        pathBuilder.Append(CultureInfo.InvariantCulture, $"M {points[0].Item1:0.##} {points[0].Item2:0.##}");
        for (var i = 1; i < points.Length; i++)
        {
            pathBuilder.Append(CultureInfo.InvariantCulture, $" L {points[i].Item1:0.##} {points[i].Item2:0.##}");
        }
        pathBuilder.Append(" Z");
        var path = pathBuilder.ToString();

        builder.AddPath(path, fill: fill, stroke: stroke, strokeWidth: 2);
    }

    static string GetLevelColor(int level) =>
        levelColors[Math.Min(level, levelColors.Length - 1)];

    static string DarkenColor(string hexColor)
    {
        // Simple darkening: reduce each component by 20%
        if (!hexColor.StartsWith('#') || hexColor.Length != 7)
            return "#333";

        var r = Convert.ToInt32(hexColor.Substring(1, 2), 16);
        var g = Convert.ToInt32(hexColor.Substring(3, 2), 16);
        var b = Convert.ToInt32(hexColor.Substring(5, 2), 16);

        r = (int)(r * 0.7);
        g = (int)(g * 0.7);
        b = (int)(b * 0.7);

        return $"#{r:X2}{g:X2}{b:X2}";
    }

    static double MeasureText(string text, double fontSize) =>
        text.Length * fontSize * 0.55;

}
