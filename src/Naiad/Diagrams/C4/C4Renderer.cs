namespace Naiad.Diagrams.C4;

public class C4Renderer(ILayoutEngine? layoutEngine = null) :
    IDiagramRenderer<C4Model>
{
    ILayoutEngine layoutEngine = layoutEngine ?? new DagreEngine();

    const double elementWidth = 160;
    const double elementHeight = 100;
    const double lineHeight = 18;

    // Characters per description line at the fixed element width.
    const int descriptionWrapChars = 22;
    const double titleHeight = 50;
    const double boundaryPadding = 15;
    const double boundaryTitleHeight = 40;

    const string personColor = "#08427B";
    const string personExtColor = "#999999";
    const string systemColor = "#1168BD";
    const string systemDbColor = "#1168BD";
    const string systemExtColor = "#999999";
    const string containerColor = "#438DD5";
    const string containerDbColor = "#438DD5";
    const string componentColor = "#85BBF0";
    const string boundaryStroke = "#444444";
    const string boundaryFill = "#FFFFFF";

    // Boundary layout state (recursive composite layout). Keyed by boundary id;
    // the top-level container uses the empty-string key.
    readonly Dictionary<string, ContainerLayout> containerLayouts = new();
    readonly Dictionary<string, (double w, double h)> boundarySizes = new();
    readonly Dictionary<string, (double x, double y, double w, double h)> elementAbs = new();
    readonly Dictionary<string, (double x, double y, double w, double h)> boundaryAbs = new();
    readonly Dictionary<string, (double x, double y)> containerOriginAbs = new();
    Dictionary<string, C4Element> elementsById = new();
    Dictionary<string, C4Boundary> boundariesById = new();
    double nodeSeparation = defaultNodeSeparation;

    const double defaultNodeSeparation = 60;

    public SvgDocument Render(C4Model model, RenderOptions options)
    {
        if (model.Elements.Count == 0 && model.Boundaries.Count == 0)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "Empty C4 diagram",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        // Both paths place related elements adjacently with the shared
        // Sugiyama/Dagre engine. Boundary diagrams additionally lay out each
        // boundary's contents in isolation and treat the boundary as a single
        // composite node in its parent container.
        if (model.Boundaries.Count == 0)
        {
            return RenderWithLayoutEngine(model, options);
        }

        return RenderWithBoundaries(model, options);
    }

    /// <summary>
    /// Layout-engine path for boundary-free diagrams: builds a graph from the
    /// elements and relationships, runs the shared Sugiyama/Dagre engine for
    /// placement and edge routing, then draws C4 shapes at the computed
    /// positions with edges following the routed polylines.
    /// </summary>
    SvgDocument RenderWithLayoutEngine(C4Model model, RenderOptions options)
    {
        var graph = new C4LayoutGraph();

        foreach (var element in model.Elements)
        {
            graph.AddNode(
                new()
                {
                    Id = element.Id,
                    Label = element.Label,
                    Width = elementWidth,
                    Height = NodeHeight(element)
                });
        }

        var edgePairs = new List<(Edge edge, C4Relationship rel)>();
        foreach (var rel in model.Relationships)
        {
            // Skip relationships that reference unknown elements.
            if (graph.GetNode(rel.From) is null ||
                graph.GetNode(rel.To) is null)
            {
                continue;
            }

            // "Up" is honored by orienting the layout edge so the target ranks above the source.
            // The other placement keywords are applied after layout, by ApplyPositionalDirections.
            var up = rel.Direction == C4RelationshipDirection.Up;

            // Reserve space for the label of a routed (non-positional) edge so a
            // long edge's chip doesn't land on a node it passes alongside.
            var reservesLabel = !IsPositional(rel.Direction) && !string.IsNullOrEmpty(rel.Label);
            var edge = new Edge
            {
                SourceId = up ? rel.To : rel.From,
                TargetId = up ? rel.From : rel.To,
                Label = rel.Label,
                LineStyle = EdgeStyle.Dotted,
                RankConstraint = ToRankConstraint(rel.Direction),
                LabelWidth = reservesLabel ? LabelChipWidth(rel.Label!, rel.Technology, options) : 0,
                LabelHeight = reservesLabel ? LabelChipHeight(rel.Technology, options) : 0
            };
            graph.AddEdge(edge);
            edgePairs.Add((edge, rel));
        }

        var layoutOptions = new LayoutOptions
        {
            Direction = Direction.TopToBottom,
            NodeSeparation = ComputeNodeSeparation(model, options),
            RankSeparation = 90
        };
        var layoutResult = layoutEngine.BuildLayout(graph, layoutOptions);

        // Left/Right/Neighbor are same-rank hints, but the layout engine has no notion of rank
        // constraints - Edge.RankConstraint is written and never read - so their targets are placed
        // against the source here instead.
        ApplyPositionalDirections(model, graph, layoutOptions.NodeSeparation);

        // Resolve each edge's polyline and label point. Positional relationships
        // (Up/Left/Right/Neighbor) are drawn as straight border-to-border lines
        // between the placed nodes; other edges follow the engine-routed polyline.
        var drawn = new List<(IReadOnlyList<Position> route, ArrowEnds arrows, double labelX, double labelY, string? label, string? technology)>();
        foreach (var (edge, rel) in edgePairs)
        {
            IReadOnlyList<Position> route;
            double labelX;
            double labelY;

            if (IsPositional(rel.Direction) &&
                graph.GetNode(rel.From) is { } fromNode &&
                graph.GetNode(rel.To) is { } toNode)
            {
                route = StraightRoute(
                    (fromNode.Position.X, fromNode.Position.Y, fromNode.Width, fromNode.Height),
                    (toNode.Position.X, toNode.Position.Y, toNode.Width, toNode.Height));
                (labelX, labelY) = PolylineLabelPoint(route);
            }
            else
            {
                route = edge.Points;
                labelX = edge.LabelPosition.X;
                labelY = edge.LabelPosition.Y;
            }

            drawn.Add((route, Arrows(rel), labelX, labelY, rel.Label, rel.Technology));
        }

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;

        // Body bounding box: node bounds plus the label chips, which can extend
        // past the nodes (e.g. a side label on a back edge).
        double minX = 0;
        double minY = 0;
        var maxX = layoutResult.Width;
        var maxY = layoutResult.Height;

        // A node placed by a positional direction can sit outside the layout's reported extent.
        foreach (var node in graph.Nodes)
        {
            minX = Math.Min(minX, node.Position.X - node.Width / 2);
            maxX = Math.Max(maxX, node.Position.X + node.Width / 2);
            minY = Math.Min(minY, node.Position.Y - node.Height / 2);
            maxY = Math.Max(maxY, node.Position.Y + node.Height / 2);
        }

        foreach (var (_, _, labelX, labelY, label, technology) in drawn)
        {
            if (string.IsNullOrEmpty(label))
            {
                continue;
            }

            var chipWidth = LabelChipWidth(label, technology, options);
            var chipHeight = LabelChipHeight(technology, options);
            minX = Math.Min(minX, labelX - chipWidth / 2);
            maxX = Math.Max(maxX, labelX + chipWidth / 2);
            minY = Math.Min(minY, labelY - chipHeight / 2);
            maxY = Math.Max(maxY, labelY + chipHeight / 2);
        }

        var bodyWidth = maxX - minX;
        var bodyHeight = maxY - minY;

        // Ensure the canvas is wide enough for the title too.
        var titleWidth = string.IsNullOrEmpty(model.Title)
            ? 0
            : model.Title.Length * (options.FontSize + 6) * 0.6 + 20;
        var contentWidth = Math.Max(bodyWidth, titleWidth);
        var contentHeight = bodyHeight + titleOffset;

        var builder = new SvgBuilder();
        builder.Size(contentWidth, contentHeight);
        builder.Padding(options.Padding);

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                contentWidth / 2,
                titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize + 6,
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        // Center the body horizontally, offset it below the title, and shift so
        // the leftmost/topmost chip sits inside the canvas.
        var bodyOffsetX = (contentWidth - bodyWidth) / 2 - minX;
        var bodyOffsetY = titleOffset - minY;
        builder.BeginGroup(transform: string.Create(
            CultureInfo.InvariantCulture, $"translate({bodyOffsetX:0.##},{bodyOffsetY:0.##})"));

        // Edge lines first so element boxes sit on top of them.
        foreach (var (route, arrows, _, _, _, _) in drawn)
        {
            DrawRoutedPolyline(builder, route, arrows);
        }

        // Element boxes.
        foreach (var element in model.Elements)
        {
            var node = graph.GetNode(element.Id);
            if (node is null)
            {
                continue;
            }

            var h = NodeHeight(element);
            DrawElement(builder, element, node.Position.X - elementWidth / 2, node.Position.Y - h / 2, options);
        }

        // Edge labels last so their chips stay legible on top of everything.
        foreach (var (_, _, labelX, labelY, label, technology) in drawn)
        {
            if (!string.IsNullOrEmpty(label))
            {
                DrawLabelChip(builder, labelX, labelY, label, technology, options);
            }
        }

        builder.EndGroup();

        return builder.Build();
    }

    /// <summary>
    /// Layout path for diagrams with boundaries. Each boundary's contents are
    /// laid out in isolation (recursively), the boundary is treated as a single
    /// composite node in its parent, and edges are drawn leaf-to-leaf once every
    /// element has an absolute position.
    /// </summary>
    SvgDocument RenderWithBoundaries(C4Model model, RenderOptions options)
    {
        containerLayouts.Clear();
        boundarySizes.Clear();
        elementAbs.Clear();
        boundaryAbs.Clear();
        containerOriginAbs.Clear();
        // Ids are user-supplied and not guaranteed unique; group so a reused id
        // resolves to the first element/boundary declared with it instead of throwing.
        elementsById = model.Elements.DistinctBy(_ => _.Id).ToDictionary(_ => _.Id);
        boundariesById = model.Boundaries.DistinctBy(_ => _.Id).ToDictionary(_ => _.Id);
        nodeSeparation = ComputeNodeSeparation(model, options);

        // Pass 1: lay out each container, children before parents.
        var topLayout = LayoutContainer(model, null);
        containerLayouts[""] = topLayout;

        // Pass 2: assign absolute positions (origin-based; the body group below
        // applies the title offset and centering).
        PlaceContainer(null, 0, 0);

        // Resolve each relationship's polyline up front so the canvas can account
        // for the label chips (a wide side label can extend past the boxes). Use
        // the engine-routed polyline when both ends sit in the same container (so
        // a skipping edge routes around its siblings), otherwise a straight line.
        var edges = new List<(List<Position> route, ArrowEnds arrows, string? label, string? technology)>();
        foreach (var rel in model.Relationships)
        {
            if (!elementAbs.TryGetValue(rel.From, out var from) ||
                !elementAbs.TryGetValue(rel.To, out var to))
            {
                continue;
            }

            // Positional relationships are drawn straight between the placed
            // boxes; other same-container edges follow the engine-routed polyline.
            var route = !IsPositional(rel.Direction) && TryGetRoutedPolyline(rel, out var routed)
                ? routed
                : StraightRoute(from, to);
            edges.Add((route, Arrows(rel), rel.Label, rel.Technology));
        }

        // Body bounding box over boundaries, elements and label chips.
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        void Expand(double x0, double y0, double x1, double y1)
        {
            minX = Math.Min(minX, x0);
            minY = Math.Min(minY, y0);
            maxX = Math.Max(maxX, x1);
            maxY = Math.Max(maxY, y1);
        }

        foreach (var (x, y, w, h) in boundaryAbs.Values)
        {
            Expand(x, y, x + w, y + h);
        }

        foreach (var (x, y, w, h) in elementAbs.Values)
        {
            Expand(x - w / 2, y - h / 2, x + w / 2, y + h / 2);
        }

        foreach (var (route, _, label, technology) in edges)
        {
            if (string.IsNullOrEmpty(label))
            {
                continue;
            }

            var (lx, ly) = PolylineLabelPoint(route);
            var chipWidth = LabelChipWidth(label, technology, options);
            var chipHeight = LabelChipHeight(technology, options);
            Expand(lx - chipWidth / 2, ly - chipHeight / 2, lx + chipWidth / 2, ly + chipHeight / 2);
        }

        if (minX > maxX)
        {
            (minX, minY, maxX, maxY) = (0, 0, topLayout.ContentWidth, topLayout.ContentHeight);
        }

        var bodyWidth = maxX - minX;
        var bodyHeight = maxY - minY;

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;
        var titleWidth = string.IsNullOrEmpty(model.Title)
            ? 0
            : model.Title.Length * (options.FontSize + 6) * 0.6 + 20;
        var contentWidth = Math.Max(bodyWidth, titleWidth);
        var contentHeight = bodyHeight + titleOffset;

        var builder = new SvgBuilder();
        builder.Size(contentWidth, contentHeight);
        builder.Padding(options.Padding);

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                contentWidth / 2,
                titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize + 6,
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        // Center the body, offset it below the title, and shift so nothing clips.
        builder.BeginGroup(transform: string.Create(
            CultureInfo.InvariantCulture,
            $"translate({(contentWidth - bodyWidth) / 2 - minX:0.##},{titleOffset - minY:0.##})"));

        // Draw order: boundary boxes (outermost first) so their fills don't cover
        // nested content, then edge lines, then element boxes, then label chips.
        foreach (var boundary in model.Boundaries.OrderBy(BoundaryDepth))
        {
            if (boundaryAbs.TryGetValue(boundary.Id, out var b))
            {
                DrawBoundaryBox(builder, b.x, b.y, b.w, b.h);
            }
        }

        var labels = new List<(double x, double y, string label, string? technology)>();
        foreach (var (route, arrows, label, technology) in edges)
        {
            var (mx, my) = DrawRoutedPolyline(builder, route, arrows);
            if (!string.IsNullOrEmpty(label))
            {
                labels.Add((mx, my, label, technology));
            }
        }

        // Captions after the edges: an edge entering a boundary crosses its top edge, and would
        // otherwise be painted straight through the caption sitting there.
        foreach (var boundary in model.Boundaries.OrderBy(BoundaryDepth))
        {
            if (boundaryAbs.TryGetValue(boundary.Id, out var b))
            {
                DrawBoundaryCaption(builder, boundary, b.x, b.y, options);
            }
        }

        foreach (var element in model.Elements)
        {
            if (elementAbs.TryGetValue(element.Id, out var e))
            {
                var h = NodeHeight(element);
                DrawElement(builder, element, e.x - elementWidth / 2, e.y - h / 2, options);
            }
        }

        foreach (var (x, y, label, technology) in labels)
        {
            DrawLabelChip(builder, x, y, label, technology, options);
        }

        builder.EndGroup();

        return builder.Build();
    }

    /// <summary>
    /// Tries to build the absolute engine-routed polyline for a relationship
    /// whose endpoints share a container (so it can route around siblings).
    /// </summary>
    bool TryGetRoutedPolyline(C4Relationship rel, out List<Position> absolute)
    {
        absolute = [];
        if (!elementsById.TryGetValue(rel.From, out var from) ||
            !elementsById.TryGetValue(rel.To, out var to) ||
            from.BoundaryId != to.BoundaryId)
        {
            return false;
        }

        var key = from.BoundaryId ?? "";
        if (!containerLayouts.TryGetValue(key, out var layout) ||
            !containerOriginAbs.TryGetValue(key, out var origin) ||
            !layout.EdgeRoutes.TryGetValue((rel.From, rel.To), out var points) ||
            points.Count < 2)
        {
            return false;
        }

        absolute = points.Select(_ => new Position(_.X + origin.x, _.Y + origin.y)).ToList();
        return true;
    }

    /// <summary>
    /// Lays out a single container (the top level when <paramref name="boundaryId"/>
    /// is null, otherwise one boundary) using the shared engine, treating child
    /// boundaries as composite nodes. Returns the content size and each direct
    /// member's center relative to the content's top-left.
    /// </summary>
    ContainerLayout LayoutContainer(C4Model model, string? boundaryId)
    {
        var directElements = model.Elements.Where(_ => _.BoundaryId == boundaryId).ToList();
        var childBoundaries = model.Boundaries.Where(_ => _.ParentBoundaryId == boundaryId).ToList();

        // Lay out child boundaries first so their composite sizes are known.
        foreach (var child in childBoundaries)
        {
            var childLayout = LayoutContainer(model, child.Id);
            containerLayouts[child.Id] = childLayout;
            boundarySizes[child.Id] = (
                childLayout.ContentWidth + boundaryPadding * 2,
                childLayout.ContentHeight + boundaryPadding * 2 + boundaryTitleHeight);
        }

        var graph = new C4LayoutGraph();
        foreach (var element in directElements)
        {
            graph.AddNode(
                new()
                {
                    Id = element.Id,
                    Width = elementWidth,
                    Height = NodeHeight(element)
                });
        }

        foreach (var child in childBoundaries)
        {
            var (w, h) = boundarySizes[child.Id];
            graph.AddNode(new() { Id = child.Id, Width = w, Height = h });
        }

        // Add an edge between two members only at the level where they first
        // become distinct direct members (their lowest common container).
        foreach (var rel in model.Relationships)
        {
            var from = DirectRepresentative(rel.From, boundaryId);
            var to = DirectRepresentative(rel.To, boundaryId);
            if (from is not null &&
                to is not null &&
                from != to &&
                graph.GetNode(from) is not null &&
                graph.GetNode(to) is not null)
            {
                var up = rel.Direction == C4RelationshipDirection.Up;
                graph.AddEdge(
                    new()
                    {
                        SourceId = up ? to : from,
                        TargetId = up ? from : to,
                        RankConstraint = ToRankConstraint(rel.Direction)
                    });
            }
        }

        var layout = new ContainerLayout();
        if (graph.Nodes.Count == 0)
        {
            layout.ContentWidth = elementWidth;
            layout.ContentHeight = elementHeight;
            return layout;
        }

        // Actors flow top-to-bottom at the top level; a boundary's contents are
        // laid out left-to-right so an edge leaving the boundary downward does
        // not pass through a sibling box stacked below it.
        var layoutOptions = new LayoutOptions
        {
            Direction = boundaryId is null ? Direction.TopToBottom : Direction.LeftToRight,
            NodeSeparation = nodeSeparation,
            RankSeparation = 90
        };
        layoutEngine.BuildLayout(graph, layoutOptions);

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var node in graph.Nodes)
        {
            minX = Math.Min(minX, node.Position.X - node.Width / 2);
            minY = Math.Min(minY, node.Position.Y - node.Height / 2);
            maxX = Math.Max(maxX, node.Position.X + node.Width / 2);
            maxY = Math.Max(maxY, node.Position.Y + node.Height / 2);
        }

        foreach (var node in graph.Nodes)
        {
            layout.MemberCenters[node.Id] = (node.Position.X - minX, node.Position.Y - minY);
        }

        // Keep the engine-routed polyline for each edge (content-relative) so a
        // relationship that skips a sibling routes around it instead of crossing.
        foreach (var edge in graph.Edges)
        {
            if (edge.Points.Count < 2)
            {
                continue;
            }

            layout.EdgeRoutes[(edge.SourceId, edge.TargetId)] =
                edge.Points.Select(_ => new Position(_.X - minX, _.Y - minY)).ToList();
        }

        layout.ContentWidth = maxX - minX;
        layout.ContentHeight = maxY - minY;
        return layout;
    }

    /// <summary>
    /// Returns the id of the direct child of <paramref name="containerId"/> (an
    /// element or a child boundary) that contains <paramref name="elementId"/>,
    /// or null if the element is not within that container.
    /// </summary>
    string? DirectRepresentative(string elementId, string? containerId)
    {
        if (!elementsById.TryGetValue(elementId, out var element))
        {
            return null;
        }

        // The element sits directly inside this container.
        if (element.BoundaryId == containerId)
        {
            return elementId;
        }

        // Otherwise find the boundary ancestor that is a direct child of the
        // container; that boundary is the element's representative here.
        var current = element.BoundaryId;
        while (current is not null)
        {
            if (!boundariesById.TryGetValue(current, out var boundary))
            {
                return null;
            }

            if (boundary.ParentBoundaryId == containerId)
            {
                return current;
            }

            current = boundary.ParentBoundaryId;
        }

        return null;
    }

    /// <summary>
    /// Recursively assigns absolute positions. <paramref name="contentOriginX"/>
    /// / <paramref name="contentOriginY"/> are the absolute top-left of the
    /// container's content area; each member's center is offset from there.
    /// </summary>
    void PlaceContainer(string? boundaryId, double contentOriginX, double contentOriginY)
    {
        if (!containerLayouts.TryGetValue(boundaryId ?? "", out var layout))
        {
            return;
        }

        containerOriginAbs[boundaryId ?? ""] = (contentOriginX, contentOriginY);

        foreach (var (memberId, center) in layout.MemberCenters)
        {
            var centerX = contentOriginX + center.X;
            var centerY = contentOriginY + center.Y;

            if (boundariesById.ContainsKey(memberId))
            {
                var (w, h) = boundarySizes[memberId];
                var topLeftX = centerX - w / 2;
                var topLeftY = centerY - h / 2;
                boundaryAbs[memberId] = (topLeftX, topLeftY, w, h);
                PlaceContainer(
                    memberId,
                    topLeftX + boundaryPadding,
                    topLeftY + boundaryTitleHeight + boundaryPadding);
            }
            else if (elementsById.TryGetValue(memberId, out var element))
            {
                var h = NodeHeight(element);
                elementAbs[memberId] = (centerX, centerY, elementWidth, h);
            }
        }
    }

    /// <summary>Nesting depth of a boundary (0 for a top-level boundary).</summary>
    int BoundaryDepth(C4Boundary boundary)
    {
        var depth = 0;
        var current = boundary.ParentBoundaryId;
        while (current is not null && boundariesById.TryGetValue(current, out var parent))
        {
            depth++;
            current = parent.ParentBoundaryId;
        }

        return depth;
    }

    /// <summary>
    /// Draws a dashed polyline through the routed layout points with a manual
    /// arrowhead, and returns the point where its label chip should sit. The
    /// arrowhead is placed at the target end, at the source end for a "back"
    /// relationship, or at both for a two-way one.
    /// </summary>
    static (double x, double y) DrawRoutedPolyline(
        SvgBuilder builder,
        IReadOnlyList<Position> points,
        ArrowEnds arrows)
    {
        if (points.Count < 2)
        {
            return PolylineLabelPoint(points);
        }

        var path = new StringBuilder();
        path.Append(CultureInfo.InvariantCulture, $"M {points[0].X:0.##} {points[0].Y:0.##}");
        for (var i = 1; i < points.Count; i++)
        {
            path.Append(CultureInfo.InvariantCulture, $" L {points[i].X:0.##} {points[i].Y:0.##}");
        }

        builder.AddPath(
            path.ToString(),
            fill: "none",
            stroke: "#666",
            strokeWidth: 1.5,
            strokeDasharray: "5,5");

        if (arrows != ArrowEnds.Source)
        {
            DrawArrowhead(builder, points[^1], points[^2]);
        }

        if (arrows != ArrowEnds.Target)
        {
            DrawArrowhead(builder, points[0], points[1]);
        }

        return PolylineLabelPoint(points);
    }

    enum ArrowEnds
    {
        Target,
        Source,
        Both
    }

    static ArrowEnds Arrows(C4Relationship rel)
    {
        if (rel.IsBidirectional)
        {
            return ArrowEnds.Both;
        }

        if (rel.Direction == C4RelationshipDirection.Back)
        {
            return ArrowEnds.Source;
        }

        return ArrowEnds.Target;
    }

    // An arrowhead with its point at tip, aimed along the line arriving from prev.
    static void DrawArrowhead(SvgBuilder builder, Position tip, Position prev)
    {
        var angle = Math.Atan2(tip.Y - prev.Y, tip.X - prev.X);
        const int arrowSize = 8;
        const double arrowAngle = Math.PI / 6;
        var ax1 = tip.X - arrowSize * Math.Cos(angle - arrowAngle);
        var ay1 = tip.Y - arrowSize * Math.Sin(angle - arrowAngle);
        var ax2 = tip.X - arrowSize * Math.Cos(angle + arrowAngle);
        var ay2 = tip.Y - arrowSize * Math.Sin(angle + arrowAngle);

        builder.AddPath(
            string.Create(CultureInfo.InvariantCulture, $"M {tip.X:0.##} {tip.Y:0.##} L {ax1:0.##} {ay1:0.##} L {ax2:0.##} {ay2:0.##} Z"),
            fill: "#666",
            stroke: "none");
    }

    /// <summary>Point on the polyline where its label chip should sit.</summary>
    static (double x, double y) PolylineLabelPoint(IReadOnlyList<Position> points)
    {
        if (points.Count == 0)
        {
            return (0, 0);
        }

        if (points.Count == 1)
        {
            return (points[0].X, points[0].Y - 8);
        }

        var mid = points.Count / 2;
        if (points.Count % 2 == 0)
        {
            return ((points[mid - 1].X + points[mid].X) / 2, (points[mid - 1].Y + points[mid].Y) / 2 - 8);
        }

        return (points[mid].X, points[mid].Y - 8);
    }

    /// <summary>
    /// A straight relationship as a two-point polyline trimmed to the source and
    /// target box borders.
    /// </summary>
    static List<Position> StraightRoute(
        (double x, double y, double w, double h) from,
        (double x, double y, double w, double h) to)
    {
        var angle = Math.Atan2(to.y - from.y, to.x - from.x);
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var fromReach = BorderDistance(from.w, from.h, cos, sin);
        var toReach = BorderDistance(to.w, to.h, cos, sin);
        return
        [
            new(from.x + cos * fromReach, from.y + sin * fromReach),
            new(to.x - cos * toReach, to.y - sin * toReach)
        ];
    }

    // How far a box's border is from its centre along a direction. Scaling the half-width and
    // half-height by the direction instead lands on the ellipse inside the box, which left a diagonal
    // edge's arrowhead hidden under the box it points at.
    static double BorderDistance(double width, double height, double cos, double sin)
    {
        var horizontal = Math.Abs(cos) < 1e-9 ? double.MaxValue : width / 2 / Math.Abs(cos);
        var vertical = Math.Abs(sin) < 1e-9 ? double.MaxValue : height / 2 / Math.Abs(sin);
        return Math.Min(horizontal, vertical);
    }

    /// <summary>
    /// Whether a direction pins the target relative to the source (so the edge is
    /// drawn straight between the placed boxes rather than engine-routed).
    /// </summary>
    static bool IsPositional(C4RelationshipDirection direction) =>
        direction is C4RelationshipDirection.Up
            or C4RelationshipDirection.Down
            or C4RelationshipDirection.Left
            or C4RelationshipDirection.Right
            or C4RelationshipDirection.Neighbor;

    static RankConstraint ToRankConstraint(C4RelationshipDirection direction) =>
        direction switch
        {
            C4RelationshipDirection.Left => RankConstraint.SameBefore,
            C4RelationshipDirection.Right => RankConstraint.SameAfter,
            C4RelationshipDirection.Neighbor => RankConstraint.Same,
            _ => RankConstraint.None
        };

    /// <summary>
    /// Node separation widened so that a same-rank relationship's label chip fits
    /// in the gap between the two boxes it connects, instead of overlapping them.
    /// </summary>
    static double ComputeNodeSeparation(C4Model model, RenderOptions options)
    {
        var widest = 0.0;
        foreach (var rel in model.Relationships)
        {
            if (!string.IsNullOrEmpty(rel.Label) &&
                rel.Direction is C4RelationshipDirection.Left
                    or C4RelationshipDirection.Right
                    or C4RelationshipDirection.Neighbor)
            {
                widest = Math.Max(widest, LabelChipWidth(rel.Label, rel.Technology, options));
            }
        }

        // 8px of breathing room on each side of the chip.
        return Math.Max(defaultNodeSeparation, widest + 16);
    }

    /// <summary>
    /// Draws a relationship label centered at the given point on a white chip so
    /// it stays legible where it crosses lines or boxes.
    /// </summary>
    static double LabelChipWidth(string label, string? technology, RenderOptions options)
    {
        var fontSize = options.FontSize - 3;
        var techFontSize = options.FontSize - 4;
        var techWidth = string.IsNullOrEmpty(technology)
            ? 0
            : $"[{technology}]".Length * (techFontSize * 0.6);
        return Math.Max(label.Length * (fontSize * 0.6), techWidth) + 8;
    }

    static double LabelChipHeight(string? technology, RenderOptions options)
    {
        var fontSize = options.FontSize - 3;
        if (string.IsNullOrEmpty(technology))
        {
            return fontSize + 6;
        }

        return (fontSize + 4) * 2 + 2;
    }

    static void DrawLabelChip(
        SvgBuilder builder,
        double x,
        double y,
        string label,
        string? technology,
        RenderOptions options)
    {
        var fontSize = options.FontSize - 3;
        var techFontSize = options.FontSize - 4;
        var hasTech = !string.IsNullOrEmpty(technology);
        var techText = hasTech ? $"[{technology}]" : null;

        var width = LabelChipWidth(label, technology, options);
        var lineHeight = fontSize + 4;
        var height = LabelChipHeight(technology, options);

        builder.AddRect(
            x - width / 2,
            y - height / 2.0,
            width,
            height,
            rx: 3,
            fill: "#FFFFFF",
            stroke: "none");

        builder.AddText(
            x,
            hasTech ? y - lineHeight / 2.0 + 1 : y,
            label,
            anchor: "middle",
            baseline: "middle",
            fontSize: fontSize,
            fontFamily: options.FontFamily,
            fill: "#666");

        if (hasTech)
        {
            builder.AddText(
                x,
                y + lineHeight / 2.0,
                techText!,
                anchor: "middle",
                baseline: "middle",
                fontSize: techFontSize,
                fontFamily: options.FontFamily,
                fill: "#888");
        }
    }

    static void DrawBoundaryBox(
        SvgBuilder builder,
        double x,
        double y,
        double width,
        double height) =>
        builder.AddRect(
            x,
            y,
            width,
            height,
            rx: 5,
            fill: boundaryFill,
            stroke: boundaryStroke,
            strokeWidth: 2,
            style: "stroke-dasharray: 8 4");

    static void DrawBoundaryCaption(
        SvgBuilder builder,
        C4Boundary boundary,
        double x,
        double y,
        RenderOptions options)
    {
        // Top-left, as Mermaid places it. Centred on the top edge is exactly where an edge entering the
        // boundary from above crosses it.
        var captionX = x + boundaryPadding;

        builder.AddText(
            captionX,
            y + boundaryTitleHeight / 2 - 5,
            boundary.Label,
            anchor: "start",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily,
            fontWeight: "bold",
            fill: "#333333");

        var typeLabel = boundary.Type switch
        {
            C4BoundaryType.Container => "[Container]",
            C4BoundaryType.System => "[System]",
            C4BoundaryType.Enterprise => "[Enterprise]",
            C4BoundaryType.Deployment => "[Deployment]",
            C4BoundaryType.Node => "[Node]",
            _ => ""
        };
        if (!string.IsNullOrEmpty(boundary.TypeLabel))
        {
            typeLabel = $"[{boundary.TypeLabel}]";
        }
        if (!string.IsNullOrEmpty(typeLabel))
        {
            builder.AddText(
                captionX,
                y + boundaryTitleHeight / 2 + 10,
                typeLabel,
                anchor: "start",
                baseline: "middle",
                fontSize: options.FontSize - 3,
                fontFamily: options.FontFamily,
                fill: "#666666");
        }
    }

    /// <summary>Number of text lines an element shows (label, optional technology, optional description).</summary>
    static int ContentLineCount(C4Element element) =>
        1
        + (string.IsNullOrEmpty(element.Technology) ? 0 : 1)
        + DescriptionLines(element).Count;

    /// <summary>
    /// The description, wrapped to the box rather than cut off at an ellipsis. Elements are a fixed
    /// width, so the limit is a character count; the box height follows from how many lines come back.
    /// </summary>
    static List<string> DescriptionLines(C4Element element)
    {
        if (string.IsNullOrEmpty(element.Description))
        {
            return [];
        }

        return WrapText(element.Description, descriptionWrapChars);
    }

    static List<string> WrapText(string text, int maxChars)
    {
        var lines = new List<string>();
        var current = "";

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var piece = word;

            // A single word wider than the box is broken rather than allowed to overflow it.
            while (piece.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = "";
                }

                lines.Add(piece[..maxChars]);
                piece = piece[maxChars..];
            }

            if (current.Length == 0)
            {
                current = piece;
            }
            else if (current.Length + 1 + piece.Length <= maxChars)
            {
                current += " " + piece;
            }
            else
            {
                lines.Add(current);
                current = piece;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    /// <summary>
    /// Places the targets of Rel_L/Rel_R/Rel_Neighbor beside their source on the same rank. Rel_U is
    /// already honoured by orienting its layout edge upwards, so it is left where the engine put it.
    /// </summary>
    static void ApplyPositionalDirections(C4Model model, GraphDiagramBase graph, double separation)
    {
        // Several relationships can share a source and a keyword, so each successive target steps further out.
        var placed = new Dictionary<(string, C4RelationshipDirection), int>();

        foreach (var rel in model.Relationships)
        {
            if (graph.GetNode(rel.From) is not {} from ||
                graph.GetNode(rel.To) is not {} to)
            {
                continue;
            }

            var key = (rel.From, rel.Direction);
            placed.TryGetValue(key, out var index);

            switch (rel.Direction)
            {
                case C4RelationshipDirection.Left:
                case C4RelationshipDirection.Right:
                case C4RelationshipDirection.Neighbor:
                    var side = rel.Direction == C4RelationshipDirection.Left ? -1 : 1;
                    var offset = (from.Width + to.Width) / 2 + separation + index * (to.Width + separation);
                    to.Position = new(from.Position.X + side * offset, from.Position.Y);
                    break;

                // Down already ranks below the source; it only needs lining up under it, since the rank
                // ordering can otherwise leave it off to one side.
                case C4RelationshipDirection.Down:
                    to.Position = new(from.Position.X + index * (to.Width + separation), to.Position.Y);
                    break;

                default:
                    continue;
            }

            placed[key] = index + 1;
        }
    }

    /// <summary>Box height sized to the element's text rather than a fixed value.</summary>
    static double NodeHeight(C4Element element)
    {
        var lines = ContentLineCount(element);
        if (element.Type == C4ElementType.Person)
        {
            // Head clearance plus the centered text block.
            return 40 + lines * lineHeight + 18;
        }

        // ~22px of padding above and below the text block.
        return lines * lineHeight + 44;
    }

    static void DrawElement(SvgBuilder builder, C4Element element, double x, double y, RenderOptions options)
    {
        var color = GetElementColor(element);
        const string textColor = "#FFFFFF";
        var height = NodeHeight(element);

        if (element.Type == C4ElementType.Person)
        {
            // Draw person shape: a circular head sitting on top of a full-width
            // rounded body. The body spans the full element width so labels and
            // descriptions stay inside the shape.
            const int headRadius = 20;
            var centerX = x + elementWidth / 2;
            var bodyTop = y + headRadius + 8;
            var bodyHeight = height - (headRadius + 8);

            // Body first so the head circle overlaps its top edge (shoulders).
            builder.AddRect(
                x,
                bodyTop,
                elementWidth,
                bodyHeight,
                rx: 8,
                fill: color,
                stroke: "none");

            // Head
            builder.AddCircle(
                centerX,
                y + headRadius,
                headRadius,
                fill: color,
                stroke: "none");

            // Center the text in the body region below the head.
            var textCenterY = (y + headRadius * 2 + (y + height)) / 2;
            var descriptionLines = DescriptionLines(element);
            var textY = textCenterY - descriptionLines.Count * (lineHeight / 2);

            // Label
            builder.AddText(
                centerX,
                textY,
                element.Label,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 1,
                fontFamily: options.FontFamily,
                fill: textColor,
                fontWeight: "bold");

            // Description
            foreach (var line in descriptionLines)
            {
                textY += lineHeight;
                builder.AddText(
                    centerX,
                    textY,
                    line,
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: options.FontSize - 3,
                    fontFamily: options.FontFamily,
                    fill: textColor);
            }
        }
        else if (element.Type is
                 C4ElementType.ContainerDb or
                 C4ElementType.ComponentDb or
                 C4ElementType.SystemDb)
        {
            // Draw database shape (cylinder)
            const int ellipseHeight = 15;

            // Top ellipse
            builder.AddEllipse(
                x + elementWidth / 2,
                y + ellipseHeight,
                elementWidth / 2 - 5,
                ellipseHeight,
                fill: color, stroke: "none");

            // Body
            builder.AddRect(
                x + 5,
                y + ellipseHeight,
                elementWidth - 10,
                height - ellipseHeight * 2,
                fill: color,
                stroke: "none");

            // Bottom ellipse
            builder.AddEllipse(
                x + elementWidth / 2,
                y + height - ellipseHeight,
                elementWidth / 2 - 5,
                ellipseHeight,
                fill: color,
                stroke: "none");

            DrawElementText(builder, element, x, y, height, options, textColor);
        }
        else
        {
            // Standard box
            builder.AddRect(
                x,
                y,
                elementWidth,
                height,
                rx: 5,
                fill: color,
                stroke: "none");

            DrawElementText(builder, element, x, y, height, options, textColor);
        }
    }

    static void DrawElementText(
        SvgBuilder builder,
        C4Element element,
        double x,
        double y,
        double height,
        RenderOptions options,
        string textColor)
    {
        var centerX = x + elementWidth / 2;

        // Center the block of lines vertically within the box.
        var textY = y + height / 2 - (ContentLineCount(element) - 1) * (lineHeight / 2);

        // Label
        builder.AddText(
            centerX,
            textY,
            element.Label,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize - 1,
            fontFamily: options.FontFamily,
            fill: textColor,
            fontWeight: "bold");

        // Technology
        if (!string.IsNullOrEmpty(element.Technology))
        {
            textY += lineHeight;
            builder.AddText(
                centerX,
                textY,
                $"[{element.Technology}]",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 3,
                fontFamily: options.FontFamily,
                fill: textColor);
        }

        // Description
        foreach (var line in DescriptionLines(element))
        {
            textY += lineHeight;
            builder.AddText(
                centerX,
                textY,
                line,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 3,
                fontFamily: options.FontFamily,
                fill: textColor);
        }
    }

    static string GetElementColor(C4Element element)
    {
        if (element.IsExternal)
        {
            if (element.Type == C4ElementType.Person)
            {
                return personExtColor;
            }

            return systemExtColor;
        }

        return element.Type switch
        {
            C4ElementType.Person => personColor,
            C4ElementType.System => systemColor,
            C4ElementType.SystemDb => systemDbColor,
            C4ElementType.SystemQueue => systemColor,
            C4ElementType.Container => containerColor,
            C4ElementType.ContainerDb => containerDbColor,
            C4ElementType.ContainerQueue => containerColor,
            C4ElementType.Component => componentColor,
            C4ElementType.ComponentDb => componentColor,
            C4ElementType.ComponentQueue => componentColor,
            _ => systemColor
        };
    }


    /// <summary>
    /// Concrete graph model used to feed C4 elements and relationships to the
    /// shared layout engine.
    /// </summary>
    sealed class C4LayoutGraph : GraphDiagramBase;

    /// <summary>
    /// Result of laying out one container: its content size and each direct
    /// member's center relative to the content's top-left corner.
    /// </summary>
    sealed class ContainerLayout
    {
        public double ContentWidth { get; set; }
        public double ContentHeight { get; set; }
        public Dictionary<string, (double X, double Y)> MemberCenters { get; } = new();

        // Engine-routed polylines for edges laid out within this container,
        // keyed by (source, target), in content-relative coordinates.
        public Dictionary<(string From, string To), List<Position>> EdgeRoutes { get; } = new();
    }
}
