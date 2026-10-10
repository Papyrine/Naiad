namespace Naiad.Diagrams.Sankey;

public class SankeyRenderer : IDiagramRenderer<SankeyModel>
{
    const double nodeWidth = 20;
    const double nodePadding = 10;
    const double columnSpacing = 200;
    const double minNodeHeight = 20;
    const double titleHeight = 40;

    // The plot is a fixed size that values are scaled into, the way Mermaid does it. Deriving the height
    // from the data's magnitude instead would make the same shape of diagram tall or short purely
    // according to the units its numbers happen to be in.
    const double defaultChartHeight = 400;
    const double labelGap = 6;
    const double linkOpacity = 0.45;

    static string[] nodeColors =
    [
        "#4CAF50",
        "#2196F3",
        "#FF9800",
        "#E91E63",
        "#9C27B0",
        "#00BCD4",
        "#FF5722",
        "#607D8B"
    ];

    public SvgDocument Render(SankeyModel model, RenderOptions options)
    {
        if (model.Links.Count == 0)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100, 50,
                "Empty diagram",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        // Build node structure
        var nodes = BuildNodes(model);
        AssignColumns(nodes, model);

        // Calculate scale
        var maxColumn = nodes.Values.Max(_ => _.Column);

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;

        // Only the busiest column's node count can force the plot taller, so that stacked bars never
        // collapse below their minimum height.
        var mostNodesInAColumn = nodes.Values
            .GroupBy(_ => _.Column)
            .Max(_ => _.Count());
        var chartHeight = Math.Max(
            defaultChartHeight,
            mostNodesInAColumn * (minNodeHeight + nodePadding));

        // Spans the first bar's left edge to the last bar's right edge; the labels sit in the gaps
        // between columns rather than outside them, so no extra margin is reserved.
        var chartWidth = maxColumn * columnSpacing + nodeWidth;

        var width = chartWidth + options.Padding * 2;
        var height = chartHeight + options.Padding * 2 + titleOffset;

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

        // Position nodes
        PositionNodes(nodes, chartHeight, titleOffset + options.Padding);

        // Draw links first (behind nodes)
        var sourceOffsets = new Dictionary<string, double>();
        var targetOffsets = new Dictionary<string, double>();
        // Index each node's column position once for link colouring (was an O(N) ToArray()+IndexOf per link).
        var nodeColorIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var colorIdx = 0;
        foreach (var key in nodes.Keys)
        {
            nodeColorIndices[key] = colorIdx++;
        }

        foreach (var link in model.Links)
        {
            var sourceNode = nodes[link.Source];
            var targetNode = nodes[link.Target];

            var sourceBand = link.Value / Math.Max(1, sourceNode.OutputValue) * sourceNode.Height;
            var targetBand = link.Value / Math.Max(1, targetNode.InputValue) * targetNode.Height;

            sourceOffsets.TryGetValue(link.Source, out var sOff);
            targetOffsets.TryGetValue(link.Target, out var tOff);

            var sourceY = sourceNode.Y + sOff + sourceBand / 2;
            var targetY = targetNode.Y + tOff + targetBand / 2;

            sourceOffsets[link.Source] = sOff + sourceBand;
            targetOffsets[link.Target] = tOff + targetBand;

            var sourceX = options.Padding + sourceNode.Column * columnSpacing + nodeWidth;
            var targetX = options.Padding + targetNode.Column * columnSpacing;

            // Draw bezier curve for link (tapers from source band to target band so each end meets its node edge exactly)
            var pathData = CreateLinkPath(sourceX, sourceY, targetX, targetY, sourceBand, targetBand);
            var colorIndex = nodeColorIndices[link.Source] % nodeColors.Length;

            // Translucent, as Mermaid draws them: node labels lie over the ribbons, and overlapping
            // ribbons stay distinguishable where they cross.
            builder.AddPath(
                pathData,
                fill: nodeColors[colorIndex],
                stroke: "none",
                opacity: linkOpacity);
        }

        // Draw nodes
        var nodeIndex = 0;
        foreach (var (name, node) in nodes)
        {
            var x = options.Padding + node.Column * columnSpacing;
            var color = nodeColors[nodeIndex % nodeColors.Length];

            builder.AddRect(
                x,
                node.Y,
                nodeWidth,
                node.Height,
                fill: color,
                stroke: "#333",
                strokeWidth: 1);

            // Nodes in the left half of the plot are labelled to the right of the bar and vice versa, so a
            // label always runs into the diagram rather than off the edge of the canvas.
            var labelOnRight = x + nodeWidth / 2 < options.Padding + chartWidth / 2;
            var labelX = labelOnRight ? x + nodeWidth + labelGap : x - labelGap;
            var anchor = labelOnRight ? "start" : "end";
            var centerY = node.Y + node.Height / 2;

            builder.AddText(
                labelX,
                centerY - 6,
                name,
                anchor: anchor,
                baseline: "middle",
                fontSize: options.FontSize - 1,
                fontFamily: options.FontFamily,
                fill: "#333");

            // Mermaid's sankey shows node values by default.
            builder.AddText(
                labelX,
                centerY + 8,
                FormatValue(Math.Max(node.InputValue, node.OutputValue)),
                anchor: anchor,
                baseline: "middle",
                fontSize: options.FontSize - 3,
                fontFamily: options.FontFamily,
                fill: "#666");

            nodeIndex++;
        }

        return builder.Build();
    }

    static string FormatValue(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    static Dictionary<string, SankeyNode> BuildNodes(SankeyModel model)
    {
        var nodes = new Dictionary<string, SankeyNode>();

        foreach (var link in model.Links)
        {
            if (!nodes.TryGetValue(link.Source, out var sourceValue))
            {
                sourceValue = new()
                {
                    Name = link.Source
                };
                nodes[link.Source] = sourceValue;
            }
            if (!nodes.TryGetValue(link.Target, out var targetValue))
            {
                targetValue = new()
                {
                    Name = link.Target
                };
                nodes[link.Target] = targetValue;
            }

            sourceValue.OutputValue += link.Value;
            targetValue.InputValue += link.Value;
        }

        return nodes;
    }

    // A Sankey diagram flows one way, so its links must not lead back to where they started. The column
    // assignment below pushes a node right of everything that feeds it, and on a cycle it would do that
    // forever. Mermaid refuses such a diagram too.
    static void RejectCircularLinks(Dictionary<string, SankeyNode> nodes, List<SankeyLink> links)
    {
        var incoming = nodes.Keys.ToDictionary(_ => _, _ => 0);
        var outgoing = nodes.Keys.ToDictionary(_ => _, _ => new List<string>());
        foreach (var link in links)
        {
            incoming[link.Target]++;
            outgoing[link.Source].Add(link.Target);
        }

        // Peel off nodes nothing flows into, and then whatever that frees up. On a cycle some remain.
        var ready = new Queue<string>(incoming.Where(_ => _.Value == 0).Select(_ => _.Key));
        var remaining = nodes.Count;
        while (ready.Count > 0)
        {
            remaining--;
            foreach (var target in outgoing[ready.Dequeue()])
            {
                incoming[target]--;
                if (incoming[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        if (remaining == 0)
        {
            return;
        }

        var stuck = string.Join(", ", incoming.Where(_ => _.Value > 0).Select(_ => _.Key));
        throw new MermaidException($"Sankey diagram has a circular link among: {stuck}");
    }

    static void AssignColumns(Dictionary<string, SankeyNode> nodes, SankeyModel model)
    {
        RejectCircularLinks(nodes, model.Links);

        // Find source nodes (no incoming links)
        var links = model.Links;
        var targets = links.Select(_ => _.Target).ToHashSet();
        var sourceOnly = links.Select(_ => _.Source).Except(targets);

        // BFS to assign columns
        var queue = new Queue<string>();
        foreach (var name in sourceOnly)
        {
            nodes[name].Column = 0;
            queue.Enqueue(name);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentColumn = nodes[current].Column;

            foreach (var link in links.Where(_ => _.Source == current))
            {
                var targetNode = nodes[link.Target];
                if (targetNode.Column <= currentColumn)
                {
                    targetNode.Column = currentColumn + 1;
                    queue.Enqueue(link.Target);
                }
            }
        }
    }

    static void PositionNodes(Dictionary<string, SankeyNode> nodes, double chartHeight, double topOffset)
    {
        var maxColumn = nodes.Values.Max(_ => _.Column);

        for (var col = 0; col <= maxColumn; col++)
        {
            var columnNodes = nodes.Values.Where(_ => _.Column == col).ToList();
            var totalValue = columnNodes.Sum(_ => Math.Max(_.InputValue, _.OutputValue));
            var scale = (chartHeight - (columnNodes.Count - 1) * nodePadding) / Math.Max(1, totalValue);

            var y = topOffset;
            foreach (var node in columnNodes)
            {
                var value = Math.Max(node.InputValue, node.OutputValue);
                node.Height = Math.Max(minNodeHeight, value * scale);
                node.Y = y;
                y += node.Height + nodePadding;
            }
        }
    }

    static string CreateLinkPath(double x1, double y1, double x2, double y2, double sourceHeight, double targetHeight)
    {
        var sourceHalf = sourceHeight / 2;
        var targetHalf = targetHeight / 2;
        var cx = (x1 + x2) / 2;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"M {x1:0.##} {y1 - sourceHalf:0.##} C {cx:0.##} {y1 - sourceHalf:0.##} {cx:0.##} {y2 - targetHalf:0.##} {x2:0.##} {y2 - targetHalf:0.##} L {x2:0.##} {y2 + targetHalf:0.##} C {cx:0.##} {y2 + targetHalf:0.##} {cx:0.##} {y1 + sourceHalf:0.##} {x1:0.##} {y1 + sourceHalf:0.##} Z");
    }
}
