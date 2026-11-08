namespace Naiad.Diagrams.AgentFlow;

/// <summary>
/// Draws an agent flow the way Mermaid's unified renderer draws it in the default theme and classic look:
/// nodes in their domain shapes, flow containers as unfilled rounded frames, and the three edge operators
/// as an arrow, a dotted line and a crossed-out line. Layout is Dagre's, as in a flowchart. Mermaid
/// measures its labels in the DOM; here they are estimated from their character counts.
/// </summary>
public class AgentFlowRenderer(ILayoutEngine? layoutEngine = null) :
    IDiagramRenderer<AgentFlowModel>
{
    ILayoutEngine layoutEngine = layoutEngine ?? new DagreEngine();

    // Mermaid's default theme.
    const string nodeFill = "#ECECFF";
    const string nodeStroke = "#9370DB";
    const string lineColor = "#333333";
    const string textColor = "#333";
    const string clusterStroke = "#aaaa33";

    // The collapse indicator's dots are the node border at half opacity.
    const string indicatorFill = "rgba(147,112,219,0.5)";

    const double charWidthFactor = 0.55;
    const double titleFontSize = 18;
    const double titleBand = 48;
    const double arrowLength = 8;
    const double arrowHalfWidth = 4;

    // The collapsed container: its gap under the title, the row the dots sit in, and its narrowest width.
    const double collapsedSeparatorGap = 8;
    const double collapsedIndicatorRow = 20;
    const double collapsedMinWidth = 80;
    const double collapsedPadding = 8;

    static readonly CultureInfo culture = CultureInfo.InvariantCulture;

    const string styles =
        MermaidStyles.BaseStyles +
        """
        #naiad p{margin:0;}
        #naiad span{fill:#333;color:#333;}
        #naiad .edgeLabel{background-color:rgba(232,232,232, 0.8);text-align:center;}
        #naiad .edgeLabel p{background-color:rgba(232,232,232, 0.8);}
        #naiad .edgeLabel rect{opacity:0.5;background-color:rgba(232,232,232, 0.8);fill:rgba(232,232,232, 0.8);}
        #naiad .agentflowTitleText{text-anchor:middle;font-size:18px;fill:#333;}
        """;

    public SvgDocument Render(AgentFlowModel model, RenderOptions options)
    {
        var graph = LayoutGraph.From(model);

        foreach (var node in graph.Nodes)
        {
            SizeNode(node, graph.IsCollapsed(node.Id), options.FontSize);
        }

        SizeEmptyContainers(graph.Subgraphs, options.FontSize);

        // Reserve room for edge labels so the layout keeps a gap clear for them.
        foreach (var edge in graph.Edges)
        {
            if (string.IsNullOrEmpty(edge.Label))
            {
                continue;
            }

            var labelSize = MeasureText(DisplayText(edge.Label), options.FontSize);
            edge.LabelWidth = labelSize.Width + 16;
            edge.LabelHeight = labelSize.Height + 8;
        }

        // Agent flow's own defaults are 50 for both spacings; the extra rank spacing gives edge labels
        // and the frames of nested flows the room Mermaid's measured layout finds for them.
        var layout = layoutEngine.BuildLayout(
            graph,
            new()
            {
                Direction = model.Direction,
                NodeSeparation = 50,
                RankSeparation = 60,
                SubgraphDirections = true
            });

        ClipEdgesToShapes(graph);

        var title = string.IsNullOrWhiteSpace(model.Title) ? null : model.Title.Trim();
        var titleWidth = title is null ? 0 : title.Length * titleFontSize * charWidthFactor;
        var offsetY = title is null ? 0 : titleBand;
        var width = Math.Max(layout.Width, titleWidth);
        var offsetX = (width - layout.Width) / 2;

        var builder = new SvgBuilder();
        builder.Options(options);
        builder.Size(width, layout.Height + offsetY);
        builder.Padding(options.Padding);
        builder.AddStyles(styles);

        if (title is not null)
        {
            builder.AddText(
                width / 2,
                titleBand / 2 - 4,
                title,
                anchor: "middle",
                baseline: "middle",
                fontSize: titleFontSize,
                fontFamily: options.FontFamily,
                fill: textColor,
                cssClass: "agentflowTitleText");
        }

        var shifted = offsetX > 0 || offsetY > 0;
        if (shifted)
        {
            builder.BeginGroup(transform: string.Create(culture, $"translate({offsetX:0.##},{offsetY:0.##})"));
        }

        // Frames first, outermost first, so everything else sits on top of them.
        RenderContainerFrames(builder, graph.Subgraphs, graph);

        foreach (var edge in graph.Edges.OfType<AgentFlowEdge>())
        {
            RenderEdge(builder, edge, options.FontSize);
        }

        foreach (var node in graph.Nodes)
        {
            if (graph.IsCollapsed(node.Id))
            {
                RenderCollapsed(builder, node, graph.Container(node.Id), options.FontSize);
            }
            else
            {
                RenderNode(builder, node);
            }
        }

        // Titles go last, so an edge running into a flow does not cross out its name.
        RenderContainerTitles(builder, graph.Subgraphs, graph, options);

        if (shifted)
        {
            builder.EndGroup();
        }

        return builder.Build();
    }

    static void SizeNode(Node node, bool collapsed, double fontSize)
    {
        var text = MeasureText(DisplayText(node.Label ?? node.Id), fontSize);
        if (collapsed)
        {
            node.Width = Math.Max(text.Width + collapsedPadding * 4, collapsedMinWidth);
            node.Height = text.Height + collapsedSeparatorGap + collapsedIndicatorRow + collapsedPadding * 2;
            return;
        }

        var width = text.Width + 30;
        var height = text.Height + 27;
        switch (node.Shape)
        {
            case NodeShape.Diamond:
                width *= 1.4;
                height *= 1.4;
                break;

            case NodeShape.Hexagon:
                width += HexagonSlant(height) * 2;
                break;

            case NodeShape.Parallelogram:
                width += ParallelogramSlant(height);
                break;

            case NodeShape.Subroutine:
                width += ShapePathGenerator.SubroutineBarInset * 2;
                break;

            case NodeShape.LinedDocument:
                // Mermaid widens the box by a tenth for the rule, and its wave rises and falls by an
                // eighth of the body's height below it.
                width *= 1.1;
                height += height / 8;
                break;
        }

        node.Width = width;
        node.Height = height;
    }

    // Mermaid's hexagon cuts its corners a quarter of its height in.
    static double HexagonSlant(double height) =>
        height / 4;

    // Mermaid's lean-right shape shifts its top half its height to the right of its bottom.
    static double ParallelogramSlant(double height) =>
        height / 2;

    // The lined document's wave, from its whole height: an eighth of the body, which is 8/9 of the whole.
    static double DocumentWave(double height) =>
        height / 9;

    // A flow that holds nothing is drawn as a frame around its title, sized as a node is around its label.
    static void SizeEmptyContainers(IEnumerable<Subgraph> subgraphs, double fontSize)
    {
        foreach (var subgraph in subgraphs)
        {
            if (subgraph.NodeIds.Count == 0 &&
                subgraph.NestedSubgraphs.Count == 0)
            {
                var size = MeasureText(LabelLines.Flatten(DisplayText(subgraph.Title ?? "")), fontSize);
                subgraph.Width = size.Width + 30;
                subgraph.Height = size.Height + 27;
                continue;
            }

            SizeEmptyContainers(subgraph.NestedSubgraphs, fontSize);
        }
    }

    /// <summary>
    /// The layout routes every edge to the box around a node. For a shape that does not fill its box that
    /// leaves the edge stopping short of it, so each end is pulled in along its own line to the shape.
    /// </summary>
    static void ClipEdgesToShapes(LayoutGraph graph)
    {
        foreach (var edge in graph.Edges)
        {
            var points = edge.Points;
            if (points.Count < 2)
            {
                continue;
            }

            if (graph.GetNode(edge.SourceId) is { } source &&
                !graph.IsCollapsed(source.Id))
            {
                points[0] = ClipToShape(source, points[1], points[0]);
            }

            if (graph.GetNode(edge.TargetId) is { } target &&
                !graph.IsCollapsed(target.Id))
            {
                points[^1] = ClipToShape(target, points[^2], points[^1]);
            }
        }
    }

    // Where the line from the node's centre towards a point leaves the node's shape, or the end the layout
    // gave when the shape fills its box.
    static Position ClipToShape(Node node, Position toward, Position current)
    {
        var dx = toward.X - node.Position.X;
        var dy = toward.Y - node.Position.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 1e-6)
        {
            return current;
        }

        var halfWidth = node.Width / 2;
        var halfHeight = node.Height / 2;
        var absoluteX = Math.Abs(dx);
        var absoluteY = Math.Abs(dy);
        var flat = absoluteY < 1e-9 ? double.MaxValue : halfHeight / absoluteY;

        double reach;
        switch (node.Shape)
        {
            case NodeShape.Diamond:
                reach = 1 / (absoluteX / halfWidth + absoluteY / halfHeight);
                break;

            case NodeShape.Hexagon:
                // A slanted side runs from each end of the flat top and bottom out to the point at
                // mid-height: |x| = halfWidth - slant * |y| / halfHeight.
                var slant = HexagonSlant(node.Height);
                reach = Math.Min(flat, halfWidth / (absoluteX + slant * absoluteY / halfHeight));
                break;

            case NodeShape.Parallelogram:
                // The right side runs from the top right corner to `slant` short of the bottom right one,
                // so x = (halfWidth - slant / 2) - y * slant / (2 * halfHeight); the left side mirrors it.
                var lean = ParallelogramSlant(node.Height);
                var across = Math.Abs(dx + dy * lean / (2 * halfHeight));
                var side = across < 1e-9 ? double.MaxValue : (halfWidth - lean / 2) / across;
                reach = Math.Min(flat, side);
                break;

            default:
                return current;
        }

        if (reach >= 1)
        {
            return current;
        }

        return new(node.Position.X + dx * reach, node.Position.Y + dy * reach);
    }

    static void RenderContainerFrames(SvgBuilder builder, IEnumerable<Subgraph> subgraphs, LayoutGraph graph)
    {
        foreach (var subgraph in subgraphs)
        {
            var style = graph.Container(subgraph.Id)?.Style;
            var bounds = subgraph.Bounds;
            builder.AddRect(
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                rx: 10,
                fill: style?.Fill ?? "none",
                stroke: style?.Stroke ?? clusterStroke,
                strokeWidth: style?.StrokeWidth ?? 0.75,
                cssClass: "cluster flow-cluster");

            RenderContainerFrames(builder, subgraph.NestedSubgraphs, graph);
        }
    }

    static void RenderContainerTitles(SvgBuilder builder, IEnumerable<Subgraph> subgraphs, LayoutGraph graph, RenderOptions options)
    {
        foreach (var subgraph in subgraphs)
        {
            // The title band has room for one line, so a `<br/>` in it becomes a space.
            var title = LabelLines.Flatten(DisplayText(subgraph.Title ?? ""));
            if (!string.IsNullOrWhiteSpace(title))
            {
                builder.AddText(
                    TitleCentre(subgraph, MeasureText(title, options.FontSize).Width, graph),
                    subgraph.Bounds.Y + 14,
                    title,
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: options.FontSize,
                    fontFamily: options.FontFamily,
                    fill: graph.Container(subgraph.Id)?.Style?.Color ?? textColor,
                    cssClass: "cluster-label");
            }

            RenderContainerTitles(builder, subgraph.NestedSubgraphs, graph, options);
        }
    }

    /// <summary>
    /// A flow's title sits centred in its top band, as Mermaid puts it. A flow is unfilled, so an edge
    /// running into it through the middle of that band would cross the title out; the title then moves to
    /// the left or right of the band, whichever no edge crosses.
    /// </summary>
    static double TitleCentre(Subgraph subgraph, double titleWidth, LayoutGraph graph)
    {
        var bounds = subgraph.Bounds;
        var centre = subgraph.Position.X;
        var halfWidth = titleWidth / 2 + 4;
        var top = bounds.Y + 2;
        var bottom = bounds.Y + 26;
        var left = bounds.X + 10 + halfWidth;
        var right = bounds.X + bounds.Width - 10 - halfWidth;
        foreach (var candidate in new[] { centre, left, right })
        {
            if (!graph.Edges.Any(_ => Crosses(_.Points, candidate - halfWidth, top, candidate + halfWidth, bottom)))
            {
                return candidate;
            }
        }

        return centre;
    }

    // Whether a route passes through a rectangle, judged from points a pixel or two apart along it.
    static bool Crosses(List<Position> points, double left, double top, double right, double bottom)
    {
        for (var i = 1; i < points.Count; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            var steps = Math.Max(1, (int) (Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y)) / 2);
            for (var step = 0; step <= steps; step++)
            {
                var fraction = (double) step / steps;
                var x = start.X + (end.X - start.X) * fraction;
                var y = start.Y + (end.Y - start.Y) * fraction;
                if (x >= left && x <= right && y >= top && y <= bottom)
                {
                    return true;
                }
            }
        }

        return false;
    }

    static void RenderNode(SvgBuilder builder, Node node)
    {
        var x = node.Position.X - node.Width / 2;
        var y = node.Position.Y - node.Height / 2;
        var style = node.Style;
        var fill = style?.Fill ?? nodeFill;
        var stroke = style?.Stroke ?? nodeStroke;
        var strokeWidth = style?.StrokeWidth ?? 1;

        // The label's box, which the lined document narrows to the part right of its rule and above its wave.
        var labelX = x;
        var labelWidth = node.Width;
        var labelHeight = node.Height;

        string path;
        switch (node.Shape)
        {
            case NodeShape.Hexagon:
                path = Hexagon(x, y, node.Width, node.Height);
                break;

            case NodeShape.Parallelogram:
                path = Parallelogram(x, y, node.Width, node.Height);
                break;

            case NodeShape.LinedDocument:
                var wave = DocumentWave(node.Height);
                var rule = node.Width * 0.05 / 1.1;
                path = LinedDocument(x, y, node.Width, node.Height, wave, rule);
                labelX = x + rule;
                labelWidth = node.Width - rule;
                labelHeight = node.Height - wave;
                break;

            default:
                path = ShapePathGenerator.GetPath(node.Shape, x, y, node.Width, node.Height);
                break;
        }

        builder.AddPath(
            path,
            fill: fill,
            stroke: stroke,
            strokeWidth: strokeWidth,
            strokeDasharray: style?.StrokeDasharray);

        AddNodeLabel(builder, node.Label ?? node.Id, labelX, y, labelWidth, labelHeight, style?.Color);
    }

    static void AddNodeLabel(SvgBuilder builder, string label, double x, double y, double width, double height, string? color)
    {
        var html = MarkdownLabel.IsMarkdown(label)
            ? MarkdownLabel.Html(label)
            : EncodeLines(label);

        builder.AddLabel(
            x,
            y,
            width,
            height,
            Paragraph(html, color),
            DisplayText(label),
            className: "nodeLabel");
    }

    // A collapsed flow: a rounded box with the flow's name, a dashed rule under it and three dots for what
    // it hides.
    static void RenderCollapsed(SvgBuilder builder, Node node, AgentFlowContainer? container, double fontSize)
    {
        var x = node.Position.X - node.Width / 2;
        var y = node.Position.Y - node.Height / 2;
        var style = container?.Style;
        var stroke = style?.Stroke ?? nodeStroke;

        builder.AddRect(
            x,
            y,
            node.Width,
            node.Height,
            rx: 10,
            fill: style?.Fill ?? nodeFill,
            stroke: stroke,
            strokeWidth: style?.StrokeWidth ?? 1,
            cssClass: "flow-collapsed");

        var label = node.Label ?? "";
        var textHeight = MeasureText(DisplayText(label), fontSize).Height;
        var separatorY = y + collapsedPadding + textHeight + collapsedSeparatorGap;
        if (!string.IsNullOrWhiteSpace(label))
        {
            AddNodeLabel(builder, label, x, y + collapsedPadding, node.Width, textHeight, style?.Color);
        }

        builder.AddLine(
            x + 8,
            separatorY,
            x + node.Width - 8,
            separatorY,
            stroke: stroke,
            strokeWidth: 0.75,
            strokeDasharray: "3, 3");

        var dotY = separatorY + collapsedIndicatorRow / 2;
        for (var i = -1; i <= 1; i++)
        {
            builder.AddCircle(node.Position.X + i * 10, dotY, 2.5, fill: indicatorFill, stroke: "none", cssClass: "collapsed-indicator");
        }
    }

    static void RenderEdge(SvgBuilder builder, AgentFlowEdge edge, double fontSize)
    {
        var points = edge.Points;
        if (points.Count < 2)
        {
            return;
        }

        // The head points along the last stretch of the route that has any length: an edge to a flow can end
        // with a repeated point, which gives no direction of its own.
        var tip = points[^1];
        var previous = points[^2];
        for (var i = points.Count - 2; i >= 0; i--)
        {
            previous = points[i];
            if (Math.Abs(previous.X - tip.X) + Math.Abs(previous.Y - tip.Y) > 0.5)
            {
                break;
            }
        }

        var dx = tip.X - previous.X;
        var dy = tip.Y - previous.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var unitX = length < 1e-9 ? 0 : dx / length;
        var unitY = length < 1e-9 ? 0 : dy / length;

        // The line stops inside the arrowhead, so its square end does not show past the narrow tip.
        var route = new List<Position> { points[0] };
        foreach (var point in points.Skip(1))
        {
            if (Math.Abs(point.X - route[^1].X) + Math.Abs(point.Y - route[^1].Y) > 0.5)
            {
                route.Add(point);
            }
        }

        if (route.Count < 2)
        {
            return;
        }

        route[^1] = tip;
        if (edge.Semantic == AgentFlowEdgeSemantic.Sequence &&
            length > arrowLength)
        {
            route[^1] = new(tip.X - unitX * arrowLength / 2, tip.Y - unitY * arrowLength / 2);
        }

        builder.AddPath(
            EdgePath.Build(route),
            fill: "none",
            stroke: lineColor,
            strokeWidth: 2,
            strokeDasharray: edge.Semantic == AgentFlowEdgeSemantic.Reference ? "3" : null,
            cssClass: "flowchart-link");

        switch (edge.Semantic)
        {
            case AgentFlowEdgeSemantic.Sequence:
                var baseX = tip.X - unitX * arrowLength;
                var baseY = tip.Y - unitY * arrowLength;
                builder.AddPath(
                    string.Create(
                        culture,
                        $"M{tip.X:0.##},{tip.Y:0.##} L{baseX - unitY * arrowHalfWidth:0.##},{baseY + unitX * arrowHalfWidth:0.##} L{baseX + unitY * arrowHalfWidth:0.##},{baseY - unitX * arrowHalfWidth:0.##} Z"),
                    fill: lineColor,
                    stroke: "none",
                    cssClass: "arrowheadPath");
                break;

            case AgentFlowEdgeSemantic.Failure:
                // Mermaid's cross sits just short of the end, its arms 4.5 either side of its centre.
                const double arm = 4.5;
                var centerX = tip.X - unitX * 6.5;
                var centerY = tip.Y - unitY * 6.5;
                builder.AddPath(
                    string.Create(
                        culture,
                        $"M{centerX - arm:0.##},{centerY - arm:0.##} L{centerX + arm:0.##},{centerY + arm:0.##} M{centerX + arm:0.##},{centerY - arm:0.##} L{centerX - arm:0.##},{centerY + arm:0.##}"),
                    fill: "none",
                    stroke: lineColor,
                    strokeWidth: 2,
                    cssClass: "cross");
                break;
        }

        if (string.IsNullOrEmpty(edge.Label))
        {
            return;
        }

        var labelPosition = edge.LabelPosition;
        var plain = DisplayText(edge.Label);
        var size = MeasureText(plain, fontSize);
        var labelWidth = size.Width + 16;
        var labelHeight = size.Height + 8;
        builder.AddEdgeLabelBackground(labelPosition.X, labelPosition.Y, labelWidth, labelHeight);

        var html = MarkdownLabel.IsMarkdown(edge.Label)
            ? MarkdownLabel.Html(edge.Label)
            : EncodeLines(edge.Label);
        builder.AddLabel(
            labelPosition.X - labelWidth / 2,
            labelPosition.Y - labelHeight / 2,
            labelWidth,
            labelHeight,
            Paragraph(html, null),
            plain,
            className: "edgeLabel");
    }

    static string Hexagon(double x, double y, double width, double height)
    {
        var slant = HexagonSlant(height);
        var middle = y + height / 2;
        return string.Create(
            culture,
            $"M{x + slant:0.##},{y:0.##} H{x + width - slant:0.##} L{x + width:0.##},{middle:0.##} L{x + width - slant:0.##},{y + height:0.##} H{x + slant:0.##} L{x:0.##},{middle:0.##} Z");
    }

    static string Parallelogram(double x, double y, double width, double height)
    {
        var slant = ParallelogramSlant(height);
        return string.Create(
            culture,
            $"M{x + slant:0.##},{y:0.##} H{x + width:0.##} L{x + width - slant:0.##},{y + height:0.##} H{x:0.##} Z");
    }

    // Mermaid's lined document: a box whose bottom edge is most of a sine wave, with a rule down its left
    // side. The wave runs 0.8 of a cycle, as Mermaid's does.
    static string LinedDocument(double x, double y, double width, double height, double wave, double rule)
    {
        var path = new StringBuilder();
        var baseline = y + height - wave;
        path.Append(culture, $"M{x:0.##},{y:0.##} H{x + width:0.##} V{baseline:0.##}");
        const int steps = 24;
        for (var i = steps; i >= 0; i--)
        {
            var fraction = (double) i / steps;
            var waveY = baseline + wave * Math.Sin(2 * Math.PI * 0.8 * fraction);
            path.Append(culture, $" L{x + width * fraction:0.##},{waveY:0.##}");
        }

        path.Append(" Z");
        path.Append(culture, $" M{x + rule:0.##},{y:0.##} V{baseline:0.##}");
        return path.ToString();
    }

    // A markdown string is measured, and falls back to plain text, without its markers.
    static string DisplayText(string label) =>
        MarkdownLabel.IsMarkdown(label) ? MarkdownLabel.Plain(label) : label;

    static string EncodeLines(string text) =>
        string.Join("<br/>", LabelLines.Split(text).Select(WebUtility.HtmlEncode));

    static string Paragraph(string innerHtml, string? color) =>
        color is null
            ? $"<p>{innerHtml}</p>"
            : $"<p style=\"color:{WebUtility.HtmlEncode(color)}\">{innerHtml}</p>";

    // A label is as wide as its longest line and one line pitch taller for each `<br/>` it holds.
    static Size MeasureText(string text, double fontSize)
    {
        var width = LabelLines.WidestLength(text.Trim()) * fontSize * charWidthFactor;
        var height = fontSize * (1.5 + SvgBuilder.LineHeightFactor * (LabelLines.Count(text) - 1));
        return new(width, height);
    }

    /// <summary>
    /// What the layout sees: the model with each collapsed flow folded into a single node. Edges that
    /// crossed into a collapsed flow end at it instead, and one that would now run from the collapsed node
    /// to itself is dropped, as Mermaid drops it. The model's own nodes are laid out in place; the flows
    /// and edges are copies, so folding leaves the model as it was parsed.
    /// </summary>
    sealed class LayoutGraph : GraphDiagramBase
    {
        readonly Dictionary<string, AgentFlowContainer> containers = new(StringComparer.Ordinal);
        readonly HashSet<string> collapsed = new(StringComparer.Ordinal);

        public bool IsCollapsed(string id) => collapsed.Contains(id);

        public AgentFlowContainer? Container(string id) => containers.GetValueOrDefault(id);

        public static LayoutGraph From(AgentFlowModel model)
        {
            var graph = new LayoutGraph();
            foreach (var container in model.AllContainers)
            {
                graph.containers[container.Id] = container;
            }

            // Every id inside a collapsed flow, onto the outermost collapsed flow around it.
            var hidden = new Dictionary<string, string>(StringComparer.Ordinal);
            var collapsedNodes = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (var container in model.Subgraphs.OfType<AgentFlowContainer>())
            {
                graph.Subgraphs.AddRange(graph.Fold(container, hidden, collapsedNodes));
            }

            foreach (var node in model.Nodes)
            {
                if (!hidden.ContainsKey(node.Id))
                {
                    graph.AddNode(node);
                }
                else if (collapsedNodes.TryGetValue(hidden[node.Id], out var summary))
                {
                    graph.AddNode(summary);
                }
            }

            // A collapsed flow with nothing inside still has to be placed.
            foreach (var summary in collapsedNodes.Values)
            {
                graph.AddNode(summary);
            }

            foreach (var edge in model.AgentEdges)
            {
                var source = hidden.GetValueOrDefault(edge.SourceId, edge.SourceId);
                var target = hidden.GetValueOrDefault(edge.TargetId, edge.TargetId);
                if (source == target &&
                    edge.SourceId != edge.TargetId)
                {
                    continue;
                }

                graph.AddEdge(
                    new AgentFlowEdge
                    {
                        Id = edge.Id,
                        SourceId = source,
                        TargetId = target,
                        Label = edge.Label,
                        Semantic = edge.Semantic,
                        Type = edge.Type,
                        LineStyle = edge.LineStyle,
                        MinLength = edge.MinLength
                    });
            }

            return graph;
        }

        // The layout's copy of a flow, or, for a collapsed flow, nothing: it becomes a node of the flow
        // around it, and everything inside it is hidden behind that node.
        IEnumerable<Subgraph> Fold(AgentFlowContainer container, Dictionary<string, string> hidden, Dictionary<string, Node> collapsedNodes)
        {
            if (container.Collapsed)
            {
                collapsed.Add(container.Id);
                collapsedNodes[container.Id] = new AgentFlowNode
                {
                    Id = container.Id,
                    Label = container.Title
                };
                Hide(container, container.Id, hidden);
                yield break;
            }

            var copy = new Subgraph
            {
                Id = container.Id,
                Title = container.Title,
                Direction = container.Direction
            };
            copy.NodeIds.AddRange(container.NodeIds);
            foreach (var nested in container.NestedSubgraphs.OfType<AgentFlowContainer>())
            {
                if (nested.Collapsed)
                {
                    copy.NodeIds.Add(nested.Id);
                }

                copy.NestedSubgraphs.AddRange(Fold(nested, hidden, collapsedNodes));
            }

            yield return copy;
        }

        static void Hide(Subgraph container, string collapsedId, Dictionary<string, string> hidden)
        {
            foreach (var id in container.NodeIds)
            {
                hidden[id] = collapsedId;
            }

            foreach (var nested in container.NestedSubgraphs)
            {
                hidden[nested.Id] = collapsedId;
                Hide(nested, collapsedId, hidden);
            }
        }
    }
}
