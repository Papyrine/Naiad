namespace Naiad.Diagrams.Swimlane;

/// <summary>
/// Draws a swimlane diagram: each top-level subgraph is a lane, a band running the length of the flow with
/// its title at the head, and every node sits inside its lane. With <c>TB</c>/<c>BT</c> the lanes are
/// columns titled across the top; with <c>LR</c>/<c>RL</c> they are rows titled up the left side.
/// </summary>
/// <remarks>
/// Mermaid lays swimlanes out with an engine of its own. Here dagre still decides the ranks (how far along
/// the flow each node goes) and the order of the nodes within a rank, which keeps related nodes together;
/// the position across the flow is then taken from the lane each node belongs to, and the edges are routed
/// orthogonally between the boxes, as Mermaid's lane router does.
/// <para>
/// The layout works in one orientation throughout, with the flow running down the Y axis and the lanes
/// side by side along X, and turns the result for <c>LR</c>/<c>RL</c> at the end.
/// </para>
/// </remarks>
public class SwimlaneRenderer : IDiagramRenderer<SwimlaneModel>
{
    // Mermaid's default theme: the cluster background and border, and the text colour.
    const string laneFill = "#ffffde";
    const string laneStroke = "#aaaa33";
    const string textColor = "#333";

    // Room between a lane's border and the nodes in it, across the flow.
    const double lanePadding = 30;

    // How far beside a node an edge runs along the flow: halfway to the lane border.
    const double channelOffset = lanePadding / 2;

    // Space between two nodes that share a rank in one lane.
    const double nodeGap = 30;

    // Room between the title band and the first rank, and after the last.
    const double contentPadding = 30;

    const double minLaneSize = 140;
    const double titlePadding = 8;
    const double nestedPadding = 12;
    const double nestedTitleRoom = 22;

    // Edges keep this far clear of nodes they pass, and their corners are rounded to this radius.
    const double clearance = 8;
    const double cornerRadius = 5;

    // Distance between edges that meet one side of a node.
    const double portSpacing = 14;

    // How far the loop of an edge from a node to itself stands off the node.
    const double selfLoopReach = 22;

    public SvgDocument Render(SwimlaneModel model, RenderOptions options)
    {
        if (model.Nodes.Count == 0 &&
            model.Subgraphs.Count == 0)
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

        FlowchartRenderer.MeasureNodes(model.Nodes, options);
        FlowchartRenderer.MeasureEdgeLabels(model.Edges, options);

        var horizontal = model.Direction is Direction.LeftToRight or Direction.RightToLeft;
        var reversed = model.Direction is Direction.BottomToTop or Direction.RightToLeft;

        var lanes = BuildLanes(model, options);
        var nested = lanes
            .Where(_ => _.Subgraph is not null)
            .SelectMany(_ => _.Subgraph!.NestedSubgraphs)
            .Where(HasNodes)
            .ToList();

        RankNodes(model, nested, horizontal);

        var layout = new LaneLayout(model, lanes, horizontal, reversed, options);
        layout.Place(nested);
        layout.RouteEdges();

        return Draw(model, layout, nested, options);
    }

    // The lanes in the order they are written, each with every node inside it, its nested subgraphs'
    // included. Nodes outside every lane share a band of their own at the end, drawn without a lane.
    static List<Lane> BuildLanes(SwimlaneModel model, RenderOptions options)
    {
        var lanes = new List<Lane>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subgraph in model.Subgraphs)
        {
            var title = LabelLines.Flatten(FlowchartRenderer.DisplayText(subgraph.Title ?? subgraph.Id));
            var lane = new Lane(subgraph, title, FlowchartRenderer.MeasureText(title, options.FontSize).Width);
            foreach (var id in MemberIds(subgraph))
            {
                if (model.GetNode(id) is { } node &&
                    placed.Add(id))
                {
                    lane.Nodes.Add(node);
                }
            }

            lanes.Add(lane);
        }

        var outside = model.Nodes.Where(_ => !placed.Contains(_.Id)).ToList();
        if (outside.Count > 0)
        {
            var lane = new Lane(null, "", 0);
            lane.Nodes.AddRange(outside);
            lanes.Add(lane);
        }

        return lanes;
    }

    static IEnumerable<string> MemberIds(Subgraph subgraph) =>
        subgraph.NodeIds.Concat(subgraph.NestedSubgraphs.SelectMany(MemberIds));

    static bool HasNodes(Subgraph subgraph) =>
        subgraph.NodeIds.Count > 0 ||
        subgraph.NestedSubgraphs.Exists(HasNodes);

    /// <summary>
    /// Lets dagre rank the whole graph as one flowchart, lanes aside, so that each node's position along
    /// the flow and its order within its rank come out as they would in a flowchart. Subgraphs nested in a
    /// lane take part as clusters, which keeps room around their members for the box drawn about them.
    /// </summary>
    static void RankNodes(SwimlaneModel model, List<Subgraph> nested, bool horizontal)
    {
        var graph = new RankingGraph();
        foreach (var node in model.Nodes)
        {
            graph.AddNode(node);
        }

        graph.Subgraphs.AddRange(nested);

        // An edge to a lane has no node to rank against, so only edges between nodes and nested subgraphs
        // shape the ranking; the rest are routed once everything has a place.
        var nestedIds = new HashSet<string>(AllSubgraphs(nested).Select(_ => _.Id), StringComparer.Ordinal);
        foreach (var edge in model.Edges)
        {
            if (IsRankable(edge.SourceId) &&
                IsRankable(edge.TargetId))
            {
                graph.Edges.Add(edge);
            }
        }

        new DagreEngine().BuildLayout(
            graph,
            new()
            {
                Direction = horizontal ? Direction.LeftToRight : Direction.TopToBottom,
                NodeSeparation = 40,
                RankSeparation = 60
            });

        bool IsRankable(string id) =>
            model.GetNode(id) is not null ||
            nestedIds.Contains(id);
    }

    static IEnumerable<Subgraph> AllSubgraphs(IEnumerable<Subgraph> subgraphs) =>
        subgraphs.SelectMany(_ => AllSubgraphs(_.NestedSubgraphs).Prepend(_));

    static SvgDocument Draw(SwimlaneModel model, LaneLayout layout, List<Subgraph> nested, RenderOptions options)
    {
        var builder = new SvgBuilder();
        builder.Options(options);
        builder.Size(layout.Width, layout.Height);
        builder.Padding(options.Padding);

        var coloredMarkers = FlowchartRenderer.AddEdgeMarkers(builder, model.Edges);
        builder.AddStyles(MermaidStyles.FlowchartStyles);

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                layout.Width / 2,
                layout.TitleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize * 18 / 16,
                fontFamily: options.FontFamily,
                fill: textColor,
                cssClass: "flowchartTitleText");
        }

        foreach (var lane in layout.Lanes)
        {
            DrawLane(builder, layout, lane, options);
        }

        foreach (var subgraph in AllSubgraphs(nested))
        {
            FlowchartRenderer.RenderSubgraphBox(builder, subgraph);
        }

        foreach (var edge in model.Edges)
        {
            FlowchartRenderer.RenderEdge(builder, edge, options.FontSize, coloredMarkers, OrthogonalPath(edge.Points));
        }

        foreach (var node in model.Nodes)
        {
            FlowchartRenderer.RenderNode(builder, node);
        }

        foreach (var subgraph in AllSubgraphs(nested))
        {
            FlowchartRenderer.RenderSubgraphTitle(builder, subgraph, options);
        }

        return builder.Build();
    }

    // A lane is a title band at its head and a body below (or, across the page, beside) it, both in the
    // cluster colours, as Mermaid draws them in its default theme.
    static void DrawLane(SvgBuilder builder, LaneLayout layout, Lane lane, RenderOptions options)
    {
        if (lane.Subgraph is not { } subgraph)
        {
            return;
        }

        var style = subgraph.Style;
        var fill = style?.Fill ?? laneFill;
        var stroke = style?.Stroke ?? laneStroke;
        var strokeWidth = style?.StrokeWidth ?? 1;
        var band = layout.BandSize;
        var top = layout.TitleHeight;

        if (layout.Horizontal)
        {
            var y = top + lane.Start;
            builder.AddRect(0, y, band, lane.Size, fill: fill, stroke: stroke, strokeWidth: strokeWidth, cssClass: "swimlane-title");
            builder.AddRect(band, y, layout.Width - band, lane.Size, fill: fill, stroke: stroke, strokeWidth: strokeWidth, cssClass: "swimlane-body");

            // The title reads bottom to top, centred in the band.
            var centerX = band / 2;
            var centerY = y + lane.Size / 2;
            builder.BeginGroup(transform: string.Create(CultureInfo.InvariantCulture, $"rotate(-90, {centerX:0.##}, {centerY:0.##})"));
            AddLaneTitle(builder, centerX, centerY, lane.Title, style, options);
            builder.EndGroup();
        }
        else
        {
            var x = lane.Start;
            builder.AddRect(x, top, lane.Size, band, fill: fill, stroke: stroke, strokeWidth: strokeWidth, cssClass: "swimlane-title");
            builder.AddRect(x, top + band, lane.Size, layout.Height - top - band, fill: fill, stroke: stroke, strokeWidth: strokeWidth, cssClass: "swimlane-body");
            AddLaneTitle(builder, x + lane.Size / 2, top + band / 2, lane.Title, style, options);
        }
    }

    static void AddLaneTitle(SvgBuilder builder, double x, double y, string title, NodeStyle? style, RenderOptions options)
    {
        if (title.Length == 0)
        {
            return;
        }

        builder.AddText(
            x,
            y,
            title,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily,
            fill: style?.Color ?? textColor,
            cssClass: "swimlane-label");
    }

    /// <summary>
    /// The path through a route's corners, straight between them and rounded at each: the routes are
    /// orthogonal, and the B-spline the flowchart draws through its waypoints would cut every corner.
    /// </summary>
    static string OrthogonalPath(List<Position> points)
    {
        static string Format(Position point) =>
            string.Create(CultureInfo.InvariantCulture, $"{point.X:0.##},{point.Y:0.##}");

        if (points.Count < 2)
        {
            return "";
        }

        var path = new StringBuilder("M").Append(Format(points[0]));
        for (var i = 1; i < points.Count - 1; i++)
        {
            var previous = points[i - 1];
            var corner = points[i];
            var next = points[i + 1];
            var inLength = Distance(previous, corner);
            var outLength = Distance(corner, next);
            var radius = Math.Min(cornerRadius, Math.Min(inLength, outLength) / 2);
            if (radius < 0.5)
            {
                path.Append(" L").Append(Format(corner));
                continue;
            }

            var before = corner + (previous - corner) * (radius / inLength);
            var after = corner + (next - corner) * (radius / outLength);
            path.Append(" L").Append(Format(before))
                .Append(" Q").Append(Format(corner)).Append(' ').Append(Format(after));
        }

        path.Append(" L").Append(Format(points[^1]));
        return path.ToString();
    }

    static double Distance(Position from, Position to) =>
        Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));

    sealed class RankingGraph : GraphDiagramBase;

    sealed class Lane(Subgraph? subgraph, string title, double titleWidth)
    {
        public Subgraph? Subgraph { get; } = subgraph;
        public string Title { get; } = title;
        public double TitleWidth { get; } = titleWidth;
        public List<Node> Nodes { get; } = [];

        // Where the lane starts across the flow, and how wide it is that way.
        public double Start { get; set; }
        public double Size { get; set; }
    }

    // One side of a box, as seen with the flow running down the page.
    enum Face
    {
        Top,
        Bottom,
        Left,
        Right
    }

    /// <summary>
    /// Places the lanes and nodes and routes the edges. Everything in here is in flow space: the flow runs
    /// down Y and the lanes stand side by side along X. <see cref="ToPage"/> turns a point into the page's
    /// coordinates.
    /// </summary>
    sealed class LaneLayout(SwimlaneModel model, List<Lane> lanes, bool horizontal, bool reversed, RenderOptions options)
    {
        // Every node's box in flow space, by id, and the ranks as spans along the flow.
        Dictionary<string, Rect> boxes = new(StringComparer.Ordinal);
        Dictionary<string, Rect> subgraphBoxes = new(StringComparer.Ordinal);
        List<(double Top, double Bottom)> ranks = [];

        // How far the flow runs and how wide the lanes come to, in flow space.
        double flowLength;
        double crossLength;

        public List<Lane> Lanes { get; } = lanes;
        public bool Horizontal { get; } = horizontal;

        // The thickness of a lane's title band, and the height of the diagram title above everything.
        public double BandSize { get; } = options.FontSize * 1.5 + titlePadding * 2;
        public double TitleHeight { get; } = string.IsNullOrEmpty(model.Title) ? 0 : options.FontSize * 2.5;

        public double Width => Horizontal ? flowLength : crossLength;
        public double Height => TitleHeight + (Horizontal ? crossLength : flowLength);

        // The size of a node along the flow and across it.
        double Along(Node node) => Horizontal ? node.Width : node.Height;

        double Across(Node node) => Horizontal ? node.Height : node.Width;

        // Dagre's coordinates for a node, along the flow and across it.
        double RankOf(Position position) => Horizontal ? position.X : position.Y;

        double OrderOf(Position position) => Horizontal ? position.Y : position.X;

        // A reversed flow is laid out and routed as a forward one, then mirrored within the lanes' bodies,
        // so the first rank sits furthest from the title band.
        Position ToPage(Position point)
        {
            var along = point.Y;
            if (reversed)
            {
                along = BandSize + flowLength - along;
            }

            return Horizontal
                ? new(along, point.X + TitleHeight)
                : point with { Y = along + TitleHeight };
        }

        /// <summary>
        /// Keeps dagre's placing along the flow, and lays each lane's nodes out across it: the nodes of one
        /// rank in a lane side by side in dagre's order, centred in the lane.
        /// </summary>
        public void Place(List<Subgraph> nested)
        {
            var nodes = model.Nodes;
            var flowStart = BandSize + contentPadding;

            // Dagre's span of the flow, its nested clusters' included, so a cluster box stays clear of the band.
            var minimum = double.MaxValue;
            var maximum = double.MinValue;
            foreach (var node in nodes)
            {
                minimum = Math.Min(minimum, RankOf(node.Position) - Along(node) / 2);
                maximum = Math.Max(maximum, RankOf(node.Position) + Along(node) / 2);
            }

            foreach (var subgraph in AllSubgraphs(nested))
            {
                var along = Horizontal ? subgraph.Width : subgraph.Height;
                minimum = Math.Min(minimum, RankOf(subgraph.Position) - along / 2);
                maximum = Math.Max(maximum, RankOf(subgraph.Position) + along / 2);
            }

            if (nodes.Count == 0)
            {
                minimum = 0;
                maximum = 0;
            }

            var rankCenters = new Dictionary<Node, double>();
            foreach (var node in nodes)
            {
                rankCenters[node] = RankOf(node.Position) - minimum + flowStart;
            }

            flowLength = flowStart + (maximum - minimum) + contentPadding;

            // Nodes dagre put in one rank share its centre.
            var rankKeys = rankCenters.Values.Select(_ => Math.Round(_)).Distinct().Order().ToList();
            foreach (var key in rankKeys)
            {
                var members = nodes.Where(_ => Math.Round(rankCenters[_]) == key).ToList();
                var half = members.Max(Along) / 2;
                ranks.Add((key - half, key + half));
            }

            var start = 0.0;
            foreach (var lane in Lanes)
            {
                var byRank = lane.Nodes
                    .GroupBy(_ => Math.Round(rankCenters[_]))
                    .Select(_ => _.OrderBy(node => OrderOf(node.Position)).ToList())
                    .ToList();

                var widest = byRank.Count == 0
                    ? 0
                    : byRank.Max(_ => _.Sum(Across) + nodeGap * (_.Count - 1));
                var padding = lanePadding + (lane.Nodes.Any(_ => InNested(_, nested)) ? nestedPadding : 0);
                lane.Start = start;
                lane.Size = Math.Max(minLaneSize, Math.Max(widest + padding * 2, lane.TitleWidth + titlePadding * 4));

                foreach (var group in byRank)
                {
                    var extent = group.Sum(Across) + nodeGap * (group.Count - 1);
                    var cross = start + (lane.Size - extent) / 2;
                    foreach (var node in group)
                    {
                        var center = new Position(cross + Across(node) / 2, rankCenters[node]);
                        boxes[node.Id] = new(center.X - Across(node) / 2, center.Y - Along(node) / 2, Across(node), Along(node));
                        node.Position = ToPage(center);
                        cross += Across(node) + nodeGap;
                    }
                }

                start += lane.Size;
            }

            crossLength = start;

            foreach (var subgraph in nested)
            {
                PlaceNested(subgraph);
            }
        }

        static bool InNested(Node node, List<Subgraph> nested) =>
            nested.Exists(_ => MemberIds(_).Contains(node.Id));

        // A nested subgraph's box wraps its members with a margin, and keeps room above them for its title.
        Rect? PlaceNested(Subgraph subgraph)
        {
            var parts = subgraph.NodeIds
                .Where(boxes.ContainsKey)
                .Select(_ => boxes[_])
                .Concat(subgraph.NestedSubgraphs.Select(PlaceNested).OfType<Rect>())
                .ToList();
            if (parts.Count == 0)
            {
                return null;
            }

            var left = parts.Min(_ => _.Left) - nestedPadding;
            var right = parts.Max(_ => _.Right) + nestedPadding;
            var top = parts.Min(_ => _.Top) - nestedPadding;
            var bottom = parts.Max(_ => _.Bottom) + nestedPadding;

            // The title goes along the top of the page, which in flow space is the left when the flow runs
            // across the page, and the bottom when it runs up it.
            if (Horizontal)
            {
                left -= nestedTitleRoom;
            }
            else if (reversed)
            {
                bottom += nestedTitleRoom;
            }
            else
            {
                top -= nestedTitleRoom;
            }

            var box = new Rect(left, top, right - left, bottom - top);
            subgraphBoxes[subgraph.Id] = box;

            var topLeft = ToPage(new(box.Left, box.Top));
            var bottomRight = ToPage(new(box.Right, box.Bottom));
            subgraph.Width = Math.Abs(bottomRight.X - topLeft.X);
            subgraph.Height = Math.Abs(bottomRight.Y - topLeft.Y);
            subgraph.Position = new((topLeft.X + bottomRight.X) / 2, (topLeft.Y + bottomRight.Y) / 2);
            return box;
        }

        // The box an edge ends on: a node's, a nested subgraph's, or a lane's body.
        Rect? EndBox(string id)
        {
            if (boxes.TryGetValue(id, out var box) ||
                subgraphBoxes.TryGetValue(id, out box))
            {
                return box;
            }

            if (Lanes.Find(_ => _.Subgraph?.Id == id) is { } lane)
            {
                var top = BandSize + contentPadding / 2;
                return new(lane.Start + lanePadding / 2, top, lane.Size - lanePadding, flowLength - top - contentPadding / 2);
            }

            return null;
        }

        public void RouteEdges()
        {
            var routes = new List<(Edge Edge, List<Position> Points)>();
            foreach (var edge in model.Edges)
            {
                edge.Points.Clear();
                if (EndBox(edge.SourceId) is not { } source ||
                    EndBox(edge.TargetId) is not { } target)
                {
                    continue;
                }

                var points = edge.SourceId == edge.TargetId
                    ? SelfLoop(source)
                    : BestRoute(edge, source, target);

                // A straight route gets a corner in its middle, so either end can be slid along its side
                // when it shares that side with other edges and still meet it square.
                if (points.Count == 2)
                {
                    points = Bend(points);
                }

                routes.Add((edge, points));
            }

            SpreadPorts(routes);

            foreach (var (edge, points) in routes)
            {
                ClipToShape(edge.SourceId, points, true);
                ClipToShape(edge.TargetId, points, false);
                var simplified = Simplify(points);
                if (!string.IsNullOrEmpty(edge.Label))
                {
                    edge.LabelPosition = ToPage(LabelPlace(simplified, edge));
                }

                edge.Points.AddRange(simplified.Select(ToPage));
            }
        }

        static List<Position> Bend(List<Position> points)
        {
            var (start, end) = (points[0], points[1]);
            if (Math.Abs(start.X - end.X) < Math.Abs(start.Y - end.Y))
            {
                var middle = (start.Y + end.Y) / 2;
                return [start, start with { Y = middle }, end with { Y = middle }, end];
            }

            var center = (start.X + end.X) / 2;
            return [start, start with { X = center }, end with { X = center }, end];
        }

        static List<Position> SelfLoop(Rect box)
        {
            var centerY = box.Top + box.Height / 2;
            var offset = Math.Min(box.Height / 4, 10);
            var outside = box.Right + selfLoopReach;
            return
            [
                new(box.Right, centerY - offset),
                new(outside, centerY - offset),
                new(outside, centerY + offset),
                new(box.Right, centerY + offset)
            ];
        }

        /// <summary>
        /// Tries the routes that suit how the two ends lie along the flow and keeps the one that crosses
        /// fewest nodes, then is shortest and has fewest corners.
        /// </summary>
        List<Position> BestRoute(Edge edge, Rect source, Rect target)
        {
            var candidates = new List<List<Position>>();
            var sourceX = source.Left + source.Width / 2;
            var targetX = target.Left + target.Width / 2;
            var sourceY = source.Top + source.Height / 2;
            var targetY = target.Top + target.Height / 2;
            var channels = Channels(source, target);

            if (target.Top >= source.Bottom - 0.5)
            {
                // Forward: out of the bottom and into the top, turning in a gap between ranks.
                var start = new Position(sourceX, source.Bottom);
                var end = new Position(targetX, target.Top);
                if (Math.Abs(sourceX - targetX) < 0.5)
                {
                    candidates.Add([start, end]);
                }

                var gaps = GapsBetween(source.Bottom, target.Top);
                foreach (var gap in gaps)
                {
                    candidates.Add([start, new(sourceX, gap), new(targetX, gap), end]);
                }

                if (gaps.Count >= 2)
                {
                    foreach (var channel in channels)
                    {
                        candidates.Add(
                        [
                            start, new(sourceX, gaps[0]), new(channel, gaps[0]),
                            new(channel, gaps[^1]), new(targetX, gaps[^1]), end
                        ]);
                    }
                }
            }
            else if (target.Bottom <= source.Top + 0.5)
            {
                // Backward: out of a side, along a channel beside the nodes, and into the target's side;
                // or out of the bottom and round into the top.
                foreach (var channel in channels)
                {
                    var leaveRight = channel > source.Right;
                    var leaveLeft = channel < source.Left;
                    var enterRight = channel > target.Right;
                    var enterLeft = channel < target.Left;
                    if ((leaveRight || leaveLeft) &&
                        (enterRight || enterLeft))
                    {
                        var sourceSide = leaveRight ? source.Right : source.Left;
                        var targetSide = enterRight ? target.Right : target.Left;
                        candidates.Add(
                        [
                            new(sourceSide, sourceY), new(channel, sourceY),
                            new(channel, targetY), new(targetSide, targetY)
                        ]);
                    }

                    var below = GapAfter(source.Bottom);
                    var above = GapBefore(target.Top);
                    candidates.Add(
                    [
                        new(sourceX, source.Bottom), new(sourceX, below), new(channel, below),
                        new(channel, above), new(targetX, above), new(targetX, target.Top)
                    ]);
                }
            }
            else
            {
                // The same rank: side to side, or round through the gap below or above.
                if (target.Left >= source.Right || target.Right <= source.Left)
                {
                    var toRight = target.Left >= source.Right;
                    var sourceSide = toRight ? source.Right : source.Left;
                    var targetSide = toRight ? target.Left : target.Right;
                    if (Math.Abs(sourceY - targetY) < 0.5)
                    {
                        candidates.Add([new(sourceSide, sourceY), new(targetSide, targetY)]);
                    }
                    else
                    {
                        var middle = (sourceSide + targetSide) / 2;
                        candidates.Add([new(sourceSide, sourceY), new(middle, sourceY), new(middle, targetY), new(targetSide, targetY)]);
                    }
                }

                var below = GapAfter(Math.Max(source.Bottom, target.Bottom));
                candidates.Add([new(sourceX, source.Bottom), new(sourceX, below), new(targetX, below), new(targetX, target.Bottom)]);
                var above = GapBefore(Math.Min(source.Top, target.Top));
                candidates.Add([new(sourceX, source.Top), new(sourceX, above), new(targetX, above), new(targetX, target.Top)]);
            }

            // Only a backward edge with nowhere to run beside the nodes can be left without a route; it
            // goes straight from the source's top to the target's bottom.
            if (candidates.Count == 0)
            {
                candidates.Add([new(sourceX, source.Top), new(targetX, target.Bottom)]);
            }

            return candidates
                .OrderBy(_ => Crossings(_, edge))
                .ThenBy(_ => Length(_) + (_.Count - 2) * 20)
                .First();
        }

        // Where a route may run along the flow: beside the two ends, between them, and beside every node, as
        // long as it stays within the lanes. A route hugging a lane border would read as part of it, so no
        // channel runs close to one.
        List<double> Channels(Rect source, Rect target)
        {
            var channels = new List<double>
            {
                source.Right + channelOffset,
                source.Left - channelOffset,
                target.Right + channelOffset,
                target.Left - channelOffset
            };

            if (target.Left > source.Right)
            {
                channels.Add((source.Right + target.Left) / 2);
            }
            else if (source.Left > target.Right)
            {
                channels.Add((target.Right + source.Left) / 2);
            }

            foreach (var box in boxes.Values)
            {
                channels.Add(box.Right + channelOffset);
                channels.Add(box.Left - channelOffset);
            }

            return channels
                .Where(_ => _ > clearance && _ < crossLength - clearance)
                .Where(_ => !Lanes.Exists(lane => Math.Abs(lane.Start - _) < clearance))
                .Distinct()
                .ToList();
        }

        // The middles of the gaps between ranks that lie between two places along the flow, in order.
        List<double> GapsBetween(double from, double to)
        {
            var gaps = new List<double>();
            for (var i = 0; i < ranks.Count - 1; i++)
            {
                var middle = (ranks[i].Bottom + ranks[i + 1].Top) / 2;
                if (middle > from &&
                    middle < to)
                {
                    gaps.Add(middle);
                }
            }

            if (gaps.Count == 0)
            {
                gaps.Add((from + to) / 2);
            }

            return gaps;
        }

        // The first gap past a place along the flow, or the room after the last rank.
        double GapAfter(double from)
        {
            var gaps = GapsBetween(from, double.MaxValue);
            return gaps[0] < flowLength ? gaps[0] : from + contentPadding / 2;
        }

        // The last gap before a place along the flow, or the room before the first rank.
        double GapBefore(double to)
        {
            var gaps = GapsBetween(double.MinValue, to);
            return gaps[^1] > BandSize ? gaps[^1] : to - contentPadding / 2;
        }

        // How many node boxes a route runs through. The route's own ends count too, except where it
        // leaves or meets them.
        int Crossings(List<Position> points, Edge edge)
        {
            var count = 0;
            foreach (var (id, box) in boxes)
            {
                var isSource = id == edge.SourceId;
                var isTarget = id == edge.TargetId;
                var grown = isSource || isTarget
                    ? new(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2)
                    : new Rect(box.X - clearance, box.Y - clearance, box.Width + clearance * 2, box.Height + clearance * 2);
                for (var i = 0; i < points.Count - 1; i++)
                {
                    if ((isSource && i == 0) ||
                        (isTarget && i == points.Count - 2))
                    {
                        continue;
                    }

                    if (SegmentHits(points[i], points[i + 1], grown))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        static bool SegmentHits(Position from, Position to, Rect box) =>
            Math.Max(from.X, to.X) > box.Left &&
            Math.Min(from.X, to.X) < box.Right &&
            Math.Max(from.Y, to.Y) > box.Top &&
            Math.Min(from.Y, to.Y) < box.Bottom;

        static double Length(List<Position> points)
        {
            var length = 0.0;
            for (var i = 0; i < points.Count - 1; i++)
            {
                length += Distance(points[i], points[i + 1]);
            }

            return length;
        }

        /// <summary>
        /// Edges that only leave one side of a node share a stem and branch off it, and edges that only
        /// arrive share their last stretch and one head, as a tree reads best. Where edges both leave and
        /// arrive on one side they are spread along it instead, so a head never lands on a line going out.
        /// They are ordered by where each comes from along that side, which keeps them from crossing just
        /// short of the node.
        /// </summary>
        void SpreadPorts(List<(Edge Edge, List<Position> Points)> routes)
        {
            var ports = new Dictionary<(string Id, Face Face), List<(List<Position> Points, bool AtStart)>>();
            foreach (var (edge, points) in routes)
            {
                if (edge.SourceId == edge.TargetId)
                {
                    continue;
                }

                Add(edge.SourceId, points, true);
                Add(edge.TargetId, points, false);
            }

            foreach (var ((id, face), members) in ports)
            {
                if (members.TrueForAll(_ => _.AtStart) ||
                    members.TrueForAll(_ => !_.AtStart) ||
                    EndBox(id) is not { } box)
                {
                    continue;
                }

                var acrossFace = face is Face.Top or Face.Bottom;
                var sideLength = acrossFace ? box.Width : box.Height;
                var spacing = Math.Min(portSpacing, sideLength * 0.6 / (members.Count - 1));
                var ordered = members
                    .OrderBy(_ => Coordinate(Far(_.Points, _.AtStart), acrossFace))
                    .ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var offset = (i - (ordered.Count - 1) / 2.0) * spacing;
                    var (points, atStart) = ordered[i];
                    var end = atStart ? 0 : points.Count - 1;
                    var next = atStart ? 1 : points.Count - 2;
                    points[end] = Shift(points[end], offset, acrossFace);
                    points[next] = Shift(points[next], offset, acrossFace);
                }
            }

            void Add(string id, List<Position> points, bool atStart)
            {
                var key = (id, FaceOf(points, atStart));
                if (!ports.TryGetValue(key, out var list))
                {
                    list = [];
                    ports[key] = list;
                }

                list.Add((points, atStart));
            }
        }

        // The side of its box a route leaves or meets it by, from the direction of its end segment.
        static Face FaceOf(List<Position> points, bool atStart)
        {
            var end = atStart ? points[0] : points[^1];
            var next = atStart ? points[1] : points[^2];
            if (Math.Abs(next.X - end.X) > Math.Abs(next.Y - end.Y))
            {
                return next.X > end.X ? Face.Right : Face.Left;
            }

            return next.Y > end.Y ? Face.Bottom : Face.Top;
        }

        // The first point past the corner nearest an end, which says where along a side the route comes from.
        static Position Far(List<Position> points, bool atStart)
        {
            if (points.Count < 3)
            {
                return atStart ? points[^1] : points[0];
            }

            return atStart ? points[2] : points[^3];
        }

        static double Coordinate(Position point, bool acrossFace) => acrossFace ? point.X : point.Y;

        static Position Shift(Position point, double offset, bool acrossFace)
        {
            if (acrossFace)
            {
                return point with
                {
                    X = point.X + offset
                };
            }

            return point with
            {
                Y = point.Y + offset
            };
        }

        /// <summary>
        /// The routes end on the box around a node. A diamond or circle does not fill its box, so an end
        /// segment is carried on, along its own line, to where it meets the shape.
        /// </summary>
        void ClipToShape(string id, List<Position> points, bool atStart)
        {
            if (model.GetNode(id) is not { Shape: NodeShape.Diamond or NodeShape.Circle or NodeShape.DoubleCircle } node ||
                !boxes.TryGetValue(id, out var box))
            {
                return;
            }

            var index = atStart ? 0 : points.Count - 1;
            var end = points[index];
            var face = FaceOf(points, atStart);
            var acrossFace = face is Face.Top or Face.Bottom;
            var halfAlong = (acrossFace ? box.Width : box.Height) / 2;
            var halfDepth = (acrossFace ? box.Height : box.Width) / 2;
            var center = new Position(box.Left + box.Width / 2, box.Top + box.Height / 2);
            var offset = Math.Abs(acrossFace ? end.X - center.X : end.Y - center.Y);

            double depth;
            if (node.Shape == NodeShape.Diamond)
            {
                depth = halfDepth * Math.Max(0, 1 - offset / halfAlong);
            }
            else
            {
                var radius = Math.Min(halfAlong, halfDepth);
                depth = Math.Sqrt(Math.Max(0, radius * radius - offset * offset));
            }

            var inset = halfDepth - depth;
            points[index] = face switch
            {
                Face.Top => end with { Y = end.Y + inset },
                Face.Bottom => end with { Y = end.Y - inset },
                Face.Left => end with { X = end.X + inset },
                _ => end with { X = end.X - inset }
            };
        }

        // Drops repeated points and the middle of any three in a line, which the straight routes' extra
        // corners leave behind when nothing slid them apart.
        static List<Position> Simplify(List<Position> points)
        {
            var result = new List<Position>();
            foreach (var point in points)
            {
                if (result.Count > 0 &&
                    Distance(result[^1], point) < 0.01)
                {
                    continue;
                }

                if (result.Count >= 2 &&
                    InLine(result[^2], result[^1], point))
                {
                    result[^1] = point;
                    continue;
                }

                result.Add(point);
            }

            return result;
        }

        static bool InLine(Position first, Position middle, Position last) =>
            (Math.Abs(first.X - middle.X) < 0.01 && Math.Abs(middle.X - last.X) < 0.01) ||
            (Math.Abs(first.Y - middle.Y) < 0.01 && Math.Abs(middle.Y - last.Y) < 0.01);

        /// <summary>
        /// A label goes halfway along its route. When that leaves it across a lane border, on a stretch
        /// running across the lanes, it is slid along that stretch to one side of the border, if the
        /// stretch is long enough to hold it there.
        /// </summary>
        Position LabelPlace(List<Position> points, Edge edge)
        {
            var (middle, index) = Midpoint(points);
            if (index < 0 ||
                Math.Abs(points[index].Y - points[index + 1].Y) > 0.01)
            {
                return middle;
            }

            var half = (Horizontal ? edge.LabelHeight : edge.LabelWidth) / 2 + 4;
            var low = Math.Min(points[index].X, points[index + 1].X) + half;
            var high = Math.Max(points[index].X, points[index + 1].X) - half;
            var borders = Lanes.Skip(1).Select(_ => _.Start).ToList();
            if (!borders.Exists(_ => Math.Abs(_ - middle.X) < half))
            {
                return middle;
            }

            var places = borders
                .SelectMany(_ => new[] { _ - half, _ + half })
                .Where(_ => _ >= low && _ <= high && !borders.Exists(border => Math.Abs(border - _) < half - 0.01))
                .OrderBy(_ => Math.Abs(_ - middle.X))
                .ToList();
            if (places.Count == 0)
            {
                return middle;
            }

            return middle with
            {
                X = places[0]
            };
        }

        // The point halfway along a route, and the index of the stretch it lies on.
        static (Position Point, int Index) Midpoint(List<Position> points)
        {
            var remaining = Length(points) / 2;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var segment = Distance(points[i], points[i + 1]);
                if (segment >= remaining &&
                    segment > 0)
                {
                    return (points[i] + (points[i + 1] - points[i]) * (remaining / segment), i);
                }

                remaining -= segment;
            }

            return (points[^1], -1);
        }
    }
}
