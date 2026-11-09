namespace Naiad.Diagrams.UseCase;

/// <summary>
/// Draws a use case diagram the way Mermaid's classic look does: actors as figures with their name
/// beneath, use cases as ellipses (or rectangles), notes, JSON tables, and system boundaries framing what
/// is declared in them, laid out as a graph by dagre. Mermaid measures its labels in the DOM; here they are
/// estimated from their character counts.
/// </summary>
public class UseCaseRenderer(ILayoutEngine? layoutEngine = null) :
    IDiagramRenderer<UseCaseModel>
{
    ILayoutEngine layoutEngine = layoutEngine ?? new DagreEngine();

    // Mermaid's default theme.
    const string nodeFill = "#ECECFF";
    const string nodeStroke = "#9370DB";
    const string lineColor = "#333333";
    const string textColor = "#333";
    const string boundaryFill = "#ffffde";
    const string boundaryStroke = "#aaaa33";
    const string noteFill = "#fff5ad";
    const string noteStroke = "#aaaa33";

    // The actor figure's box, as Mermaid sizes it, and the gaps around its labels.
    const double figureWidth = 56;
    const double figureHeight = 72;
    const double actorLabelGap = 8;
    const double stereotypeGap = 2;
    const double actorPadding = 10;

    // Mermaid pads an ellipse by twice its half padding on every side, so the label has as much air above
    // it as beside it.
    const double ellipsePadding = 20;
    const double boxPadding = 10;

    const double jsonTitleHeight = 26;
    const double jsonRowHeight = 22;
    const double jsonCellPadding = 8;

    const double titleHeight = 40;
    const double charWidthFactor = 0.55;
    const double edgeStrokeWidth = 1.5;

    // Mermaid's stick figure, centred on the figure box.
    const string stickFigurePath =
        "M 0 -12 C 6.627 -12 12 -17.373 12 -24 C 12 -30.627 6.627 -36 0 -36 C -6.627 -36 -12 -30.627 -12 -24 " +
        "C -12 -17.373 -6.627 -12 0 -12 Z M 0 -12 V 8 M -17 -5 H 17 M 0 8 L -15 28 M 0 8 L 15 28";

    const string hollowBodyPath =
        "M -22 -10 H 22 V 0 H 6 L 22 17 L 13 28 L 0 13 L -13 28 L -22 17 L -6 0 H -22 Z";

    const string awesomePath =
        "M 0 -34 C 7.18 -34 13 -28.18 13 -21 C 13 -13.82 7.18 -8 0 -8 C -7.18 -8 -13 -13.82 -13 -21 " +
        "C -13 -28.18 -7.18 -34 0 -34 Z M -24 25 C -24 7 -14 -3 0 -3 C 14 -3 24 7 24 25 C 24 28 21 30 18 30 " +
        "H -18 C -21 30 -24 28 -24 25 Z";

    public SvgDocument Render(UseCaseModel model, RenderOptions options)
    {
        var actorFontSize = (double) options.FontSize;
        var useCaseFontSize = options.FontSize - 2.0;
        var edgeFontSize = options.FontSize - 2.0;

        var graph = new LayoutGraph();
        var shapes = new Dictionary<string, Shape>(StringComparer.Ordinal);

        foreach (var actor in model.Actors)
        {
            AddShape(graph, shapes, MeasureActor(actor, actorFontSize));
        }

        foreach (var useCase in model.UseCases)
        {
            AddShape(graph, shapes, MeasureUseCase(useCase, useCaseFontSize));
        }

        AddNotes();

        foreach (var table in model.JsonTables)
        {
            AddShape(graph, shapes, MeasureJsonTable(table, useCaseFontSize));
        }

        // Then the notes on JSON tables, which had nothing to attach to above.
        AddNotes();

        if (shapes.Count == 0)
        {
            return EmptyDiagram(options);
        }

        foreach (var boundary in model.Boundaries)
        {
            // The layout keeps nodes and subgraphs in one namespace, and an actor may share a boundary's id.
            // A space is in no element id, so this one is the boundary's alone.
            var subgraph = new Subgraph
            {
                Id = $"boundary {boundary.Id}",
                Title = boundary.Title
            };
            subgraph.NodeIds.AddRange(boundary.MemberIds.Where(shapes.ContainsKey));
            if (subgraph.NodeIds.Count == 0)
            {
                // An empty boundary is a box around its title.
                var titleSize = Measure(DisplayText(boundary.Title), actorFontSize);
                subgraph.Width = titleSize.Width + boxPadding * 4;
                subgraph.Height = titleSize.Height + boxPadding * 4;
            }

            graph.Subgraphs.Add(subgraph);
        }

        var edges = new List<(Edge edge, UseCaseRelationship? relationship)>();
        foreach (var relationship in model.Relationships)
        {
            if (!shapes.ContainsKey(relationship.SourceId) ||
                !shapes.ContainsKey(relationship.TargetId))
            {
                continue;
            }

            var label = RelationshipLabel(relationship);
            var edge = new Edge
            {
                SourceId = relationship.SourceId,
                TargetId = relationship.TargetId,
                Label = label,
                MinLength = relationship.MinLength
            };
            if (label is not null)
            {
                var size = Measure(DisplayText(label), edgeFontSize);
                edge.LabelWidth = size.Width + 8;
                edge.LabelHeight = size.Height + 4;
            }

            graph.AddEdge(edge);
            edges.Add((edge, relationship));
        }

        // A note is an ordinary node, tied to its target by a dotted line without markers.
        foreach (var note in model.Notes)
        {
            if (!shapes.ContainsKey(note.Id) ||
                !shapes.ContainsKey(note.TargetId))
            {
                continue;
            }

            var edge = new Edge
            {
                SourceId = note.Id,
                TargetId = note.TargetId
            };
            graph.AddEdge(edge);
            edges.Add((edge, null));
        }

        layoutEngine.BuildLayout(
            graph,
            new()
            {
                Direction = model.Direction,
                NodeSeparation = 50,
                RankSeparation = 50
            });

        foreach (var (edge, _) in edges)
        {
            ClipToEllipses(edge, shapes);
        }

        var boundaryFrames = model.Boundaries
            .Zip(graph.Subgraphs, (_, subgraph) => (boundary: _, frame: Frame(_, subgraph, shapes, actorFontSize)))
            .ToList();

        // Everything is drawn in layout coordinates and then moved so the drawing starts at the padding.
        var bounds = Extent(shapes.Values, boundaryFrames.Select(_ => _.frame), edges.Select(_ => _.edge));
        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;
        var titleFontSize = options.FontSize + 4.0;
        var contentWidth = bounds.Width;
        if (!string.IsNullOrEmpty(model.Title))
        {
            contentWidth = Math.Max(contentWidth, model.Title.Length * titleFontSize * 0.6);
        }

        var builder = new SvgBuilder();
        builder.Options(options);
        builder.Size(contentWidth, bounds.Height + titleOffset);
        builder.Padding(options.Padding);

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                contentWidth / 2,
                titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: titleFontSize,
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: textColor);
        }

        var offsetX = (contentWidth - bounds.Width) / 2 - bounds.X;
        var offsetY = titleOffset - bounds.Y;
        builder.BeginGroup(transform: string.Create(CultureInfo.InvariantCulture, $"translate({offsetX:0.##},{offsetY:0.##})"));

        foreach (var (boundary, frame) in boundaryFrames)
        {
            DrawBoundary(builder, boundary, frame);
        }

        foreach (var (edge, relationship) in edges)
        {
            DrawEdge(builder, edge, relationship, edgeFontSize, options);
        }

        foreach (var shape in shapes.Values)
        {
            DrawShape(builder, shape, options);
        }

        // Titles go last, over any edge that crosses into a boundary through its title band.
        foreach (var (boundary, frame) in boundaryFrames)
        {
            DrawBoundaryTitle(builder, boundary, frame, actorFontSize, options);
        }

        builder.EndGroup();
        return builder.Build();

        // Adds the notes whose target is in by now and that are not in yet.
        void AddNotes()
        {
            foreach (var note in model.Notes)
            {
                if (shapes.ContainsKey(note.Id) ||
                    !shapes.ContainsKey(note.TargetId))
                {
                    continue;
                }

                var size = Measure(DisplayText(note.Text), useCaseFontSize);
                AddShape(graph, shapes, new NoteShape(note, size.Width + boxPadding * 2, size.Height + boxPadding * 2, useCaseFontSize));
            }
        }
    }

    static SvgDocument EmptyDiagram(RenderOptions options)
    {
        var builder = new SvgBuilder();
        builder.Size(200, 100);
        builder.AddText(
            100,
            50,
            "Empty diagram",
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily);
        return builder.Build();
    }

    static void AddShape(LayoutGraph graph, Dictionary<string, Shape> shapes, Shape shape)
    {
        if (!shapes.TryAdd(shape.Id, shape))
        {
            return;
        }

        graph.AddNode(shape.Node);
    }

    static string? RelationshipLabel(UseCaseRelationship relationship) =>
        relationship.Type switch
        {
            UseCaseRelationshipType.Include => "«include»",
            UseCaseRelationshipType.Extend => "«extend»",
            _ => relationship.Label
        };

    // An actor is its figure with the name, and any stereotype, stacked beneath it.
    static ActorShape MeasureActor(UseCaseActor actor, double fontSize)
    {
        var label = Measure(DisplayText(actor.Label), fontSize);
        var stereotype = actor.Stereotype is null ? default : Measure($"«{actor.Stereotype}»", fontSize);
        var contentHeight = figureHeight + actorLabelGap + label.Height;
        if (actor.Stereotype is not null)
        {
            contentHeight += stereotype.Height + stereotypeGap;
        }

        var width = Math.Max(figureWidth, Math.Max(label.Width, stereotype.Width)) + actorPadding * 2;
        return new(actor, width, contentHeight + actorPadding * 2, contentHeight, label.Height, stereotype.Height, fontSize);
    }

    // A stereotype goes above the use case's name, inside the shape.
    static UseCaseItem MeasureUseCase(UseCase useCase, double fontSize)
    {
        var text = DisplayText(useCase.Label);
        if (useCase.Stereotype is not null)
        {
            text = $"«{useCase.Stereotype}»<br/>{text}";
        }

        var size = Measure(text, fontSize);
        if (useCase.Shape == UseCaseShape.Rectangle)
        {
            return new(useCase, text, size.Width + boxPadding * 2, size.Height + boxPadding * 2, size.Width, fontSize);
        }

        return new(useCase, text, size.Width + ellipsePadding * 2, size.Height + ellipsePadding * 2, size.Width, fontSize);
    }

    static JsonShape MeasureJsonTable(JsonTable table, double fontSize)
    {
        var keyWidth = 0.0;
        var valueWidth = 0.0;
        foreach (var row in table.Rows)
        {
            keyWidth = Math.Max(keyWidth, TextWidth(row.Key, fontSize));
            valueWidth = Math.Max(valueWidth, TextWidth(row.Value, fontSize));
        }

        var keyColumn = keyWidth + jsonCellPadding * 2;
        var valueColumn = valueWidth + jsonCellPadding * 2;
        var titleWidth = TextWidth(table.Id, fontSize) * 1.1 + jsonCellPadding * 2;
        var width = Math.Max(titleWidth, keyColumn + valueColumn);

        // A title wider than the two columns widens the value column rather than leaving a gap.
        var height = jsonTitleHeight + table.Rows.Count * jsonRowHeight;
        return new(table, width, height, keyColumn, fontSize);
    }

    // A markdown string is measured, and drawn, without its markers.
    static string DisplayText(string label) =>
        MarkdownLabel.IsMarkdown(label) ? MarkdownLabel.Plain(label) : label;

    /// <summary>
    /// Draws a label centred on (<paramref name="x"/>, <paramref name="y"/>), under an optional first line
    /// such as a stereotype. SVG text cannot change weight or slant part-way through a line, so a markdown
    /// string, whose bold and italics Mermaid shows, is HTML in a foreignObject where HTML is allowed.
    /// </summary>
    static void AddLabel(
        SvgBuilder builder,
        double x,
        double y,
        string label,
        double fontSize,
        string fill,
        string? cssClass,
        RenderOptions options,
        string? firstLine = null)
    {
        var plain = DisplayText(label);
        if (firstLine is not null)
        {
            plain = $"{firstLine}<br/>{plain}";
        }

        if (!options.AllowHtmlElements ||
            !MarkdownLabel.IsMarkdown(label))
        {
            builder.AddTextLines(
                x,
                y,
                plain,
                anchor: "middle",
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fill: fill,
                cssClass: cssClass);
            return;
        }

        var html = MarkdownLabel.Html(label);
        if (firstLine is not null)
        {
            html = $"{WebUtility.HtmlEncode(firstLine)}<br/>{html}";
        }

        // The width is an estimate, and bold runs wider, so the box is given room to spare; its text is
        // centred and does not wrap.
        var size = Measure(plain, fontSize);
        var width = size.Width * 1.2 + fontSize;
        builder.AddForeignObject(
            x - width / 2,
            y - size.Height / 2,
            width,
            size.Height,
            $"<p style=\"margin:0;color:{fill}\">{html}</p>",
            cssClass,
            string.Create(CultureInfo.InvariantCulture, $"font-size:{fontSize:0.##}px;font-family:{options.FontFamily}"));
    }

    static double TextWidth(string text, double fontSize) =>
        text.Length * fontSize * charWidthFactor;

    // A label is as wide as its longest line and one line pitch taller for each line after the first.
    static Size Measure(string text, double fontSize) =>
        new(
            LabelLines.WidestLength(text.Trim()) * fontSize * charWidthFactor,
            fontSize * (1.5 + SvgBuilder.LineHeightFactor * (LabelLines.Count(text) - 1)));

    /// <summary>
    /// The layout routes each edge to the box around a node. An ellipse does not fill its box, so an end
    /// that meets one is pulled in along its last segment to where it reaches the ellipse.
    /// </summary>
    static void ClipToEllipses(Edge edge, Dictionary<string, Shape> shapes)
    {
        var points = edge.Points;
        if (points.Count < 2)
        {
            return;
        }

        if (shapes[edge.SourceId] is UseCaseItem { IsEllipse: true } source)
        {
            points[0] = ClipToEllipse(source, points[1], points[0]);
        }

        if (shapes[edge.TargetId] is UseCaseItem { IsEllipse: true } target)
        {
            points[^1] = ClipToEllipse(target, points[^2], points[^1]);
        }
    }

    static Position ClipToEllipse(Shape shape, Position toward, Position current)
    {
        var center = shape.Node.Position;
        var deltaX = toward.X - center.X;
        var deltaY = toward.Y - center.Y;
        var halfWidth = shape.Width / 2;
        var halfHeight = shape.Height / 2;
        var scaled = Math.Sqrt(deltaX * deltaX / (halfWidth * halfWidth) + deltaY * deltaY / (halfHeight * halfHeight));
        if (scaled <= 1)
        {
            return current;
        }

        return new(center.X + deltaX / scaled, center.Y + deltaY / scaled);
    }

    /// <summary>
    /// Where a boundary is drawn. Dagre fits the box to what is inside it with a margin that is not
    /// always deep enough for the title, so the box grows upwards when the title band would reach the
    /// topmost member. A package's title sits in a tab, which is as tall as the title and takes the
    /// place of that band.
    /// </summary>
    static BoundaryFrame Frame(SystemBoundary boundary, Subgraph subgraph, Dictionary<string, Shape> shapes, double fontSize)
    {
        const double titleGap = 8;
        var title = Measure(LabelLines.Flatten(DisplayText(boundary.Title)), fontSize);
        var bounds = subgraph.Bounds;
        var band = boundary.Type == BoundaryType.Package ? title.Height + 10 : title.Height + 4;
        var minimumWidth = title.Width + (boundary.Type == BoundaryType.Package ? 20 : boxPadding * 2);
        var width = Math.Max(bounds.Width, minimumWidth);
        var left = bounds.X - (width - bounds.Width) / 2;

        var top = bounds.Y;
        foreach (var memberId in subgraph.NodeIds)
        {
            var member = shapes[memberId];
            top = Math.Min(top, member.Node.Position.Y - member.Height / 2 - band - titleGap);
        }

        if (subgraph.NodeIds.Count == 0)
        {
            top = bounds.Y - band;
        }

        var tabWidth = boundary.Type == BoundaryType.Package ? Math.Min(width, Math.Max(80, title.Width + 20)) : 0;
        return new(left, top, width, bounds.Bottom - top, band, tabWidth);
    }

    static Rect Extent(IEnumerable<Shape> shapes, IEnumerable<BoundaryFrame> frames, IEnumerable<Edge> edges)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        void Include(double left, double top, double right, double bottom)
        {
            minX = Math.Min(minX, left);
            minY = Math.Min(minY, top);
            maxX = Math.Max(maxX, right);
            maxY = Math.Max(maxY, bottom);
        }

        foreach (var shape in shapes)
        {
            var position = shape.Node.Position;
            Include(position.X - shape.Width / 2, position.Y - shape.Height / 2, position.X + shape.Width / 2, position.Y + shape.Height / 2);
        }

        foreach (var frame in frames)
        {
            Include(frame.X, frame.Y, frame.X + frame.Width, frame.Y + frame.Height);
        }

        foreach (var edge in edges)
        {
            foreach (var point in edge.Points)
            {
                Include(point.X, point.Y, point.X, point.Y);
            }

            if (edge.LabelWidth > 0)
            {
                var label = edge.LabelPosition;
                Include(label.X - edge.LabelWidth / 2, label.Y - edge.LabelHeight / 2, label.X + edge.LabelWidth / 2, label.Y + edge.LabelHeight / 2);
            }
        }

        return new(minX, minY, maxX - minX, maxY - minY);
    }

    static void DrawBoundary(SvgBuilder builder, SystemBoundary boundary, BoundaryFrame frame)
    {
        var style = boundary.Style;
        var fill = style?.Fill ?? boundaryFill;
        var stroke = style?.Stroke ?? boundaryStroke;
        var strokeWidth = style?.StrokeWidth ?? 1;

        if (boundary.Type == BoundaryType.Package)
        {
            builder.AddRect(frame.X, frame.Y, frame.TabWidth, frame.Band, fill: fill, stroke: stroke, strokeWidth: strokeWidth, cssClass: "boundary-tab");
            builder.AddRect(frame.X, frame.Y + frame.Band, frame.Width, frame.Height - frame.Band, fill: fill, stroke: stroke, strokeWidth: strokeWidth, cssClass: "boundary-body");
            return;
        }

        builder.AddRect(frame.X, frame.Y, frame.Width, frame.Height, fill: fill, stroke: stroke, strokeWidth: strokeWidth, cssClass: "boundary-body");
    }

    static void DrawBoundaryTitle(SvgBuilder builder, SystemBoundary boundary, BoundaryFrame frame, double fontSize, RenderOptions options)
    {
        var title = LabelLines.Flatten(DisplayText(boundary.Title));
        var centerX = boundary.Type == BoundaryType.Package ? frame.X + frame.TabWidth / 2 : frame.X + frame.Width / 2;
        var centerY = boundary.Type == BoundaryType.Package ? frame.Y + frame.Band / 2 : frame.Y + 2 + fontSize * 0.75;

        // An edge entering the boundary can cross its title. Backing the text with the boundary's own fill
        // hides the line behind it, which is invisible against the box anyway.
        if (boundary.Type == BoundaryType.Rect)
        {
            var width = TextWidth(title, fontSize);
            builder.AddRect(
                centerX - width / 2 - 4,
                centerY - fontSize * 0.75,
                width + 8,
                fontSize * 1.5,
                fill: boundary.Style?.Fill ?? boundaryFill,
                stroke: "none");
        }

        builder.AddText(
            centerX,
            centerY,
            title,
            anchor: "middle",
            baseline: "middle",
            fontSize: fontSize,
            fontFamily: options.FontFamily,
            fill: boundary.Style?.Color ?? textColor,
            cssClass: "system-boundary-title");
    }

    static void DrawEdge(SvgBuilder builder, Edge edge, UseCaseRelationship? relationship, double fontSize, RenderOptions options)
    {
        var points = edge.Points.ToList();
        if (points.Count < 2)
        {
            return;
        }

        var style = relationship?.Style;
        var stroke = style?.Stroke ?? lineColor;
        var strokeWidth = style?.StrokeWidth ?? edgeStrokeWidth;

        // Include, extend and a note's connector are dashed; every other relationship is solid.
        var dashed = relationship is null or { Type: UseCaseRelationshipType.Include or UseCaseRelationshipType.Extend };
        var dashArray = style?.StrokeDasharray ?? (dashed ? "3" : null);

        var startMarker = relationship?.StartMarker ?? UseCaseMarker.None;
        var endMarker = relationship?.EndMarker ?? UseCaseMarker.None;

        // The line stops where a pointed head starts, so its end does not show past the tip.
        var endTip = points[^1];
        var endDirection = UnitVector(points[^2], points[^1]);
        var startTip = points[0];
        var startDirection = UnitVector(points[1], points[0]);
        points[^1] = Shorten(points[^2], points[^1], HeadLength(endMarker));
        points[0] = Shorten(points[1], points[0], HeadLength(startMarker));

        builder.AddPath(
            EdgePath.Build(points),
            fill: "none",
            stroke: stroke,
            strokeWidth: strokeWidth,
            strokeDasharray: dashArray,
            cssClass: "relationship");

        DrawMarker(builder, endMarker, endTip, endDirection, stroke);
        DrawMarker(builder, startMarker, startTip, startDirection, stroke);

        if (string.IsNullOrEmpty(edge.Label))
        {
            return;
        }

        var position = edge.LabelPosition;
        builder.AddEdgeLabelBackground(position.X, position.Y, edge.LabelWidth, edge.LabelHeight);
        AddLabel(
            builder,
            position.X,
            position.Y,
            edge.Label,
            fontSize,
            style?.Color ?? textColor,
            "relationship-label",
            options);
    }

    static double HeadLength(UseCaseMarker marker) =>
        marker switch
        {
            UseCaseMarker.Arrow => 8,
            UseCaseMarker.Extension => 14,
            _ => 0
        };

    // The unit vector pointing from one point to the next.
    static (double x, double y) UnitVector(Position from, Position to)
    {
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        return length < 1e-9 ? (1, 0) : (deltaX / length, deltaY / length);
    }

    // The end of a segment, drawn back towards its start by the given distance, if the segment is long enough.
    static Position Shorten(Position from, Position to, double distance)
    {
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (distance <= 0 ||
            length <= distance)
        {
            return to;
        }

        return new(to.X - deltaX / length * distance, to.Y - deltaY / length * distance);
    }

    /// <summary>
    /// A marker at an end of an edge, its tip on the end point and pointing along the edge: a filled
    /// arrowhead, a filled circle, a cross, or the hollow triangle of a generalization.
    /// </summary>
    static void DrawMarker(SvgBuilder builder, UseCaseMarker marker, Position tip, (double x, double y) direction, string color)
    {
        var (unitX, unitY) = direction;
        switch (marker)
        {
            case UseCaseMarker.Arrow:
                builder.AddPath(Triangle(tip, unitX, unitY, 8, 4), fill: color, stroke: color, strokeWidth: 1);
                break;
            case UseCaseMarker.Extension:
                builder.AddPath(Triangle(tip, unitX, unitY, 14, 7), fill: nodeFill, stroke: color, strokeWidth: 1);
                break;
            case UseCaseMarker.Circle:
                builder.AddCircle(tip.X - unitX * 5, tip.Y - unitY * 5, 4.5, fill: color, stroke: color, strokeWidth: 1);
                break;
            case UseCaseMarker.Cross:
                var centerX = tip.X - unitX * 6;
                var centerY = tip.Y - unitY * 6;
                const double arm = 4.5;
                builder.AddPath(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"M {centerX - arm:0.##} {centerY - arm:0.##} L {centerX + arm:0.##} {centerY + arm:0.##} M {centerX + arm:0.##} {centerY - arm:0.##} L {centerX - arm:0.##} {centerY + arm:0.##}"),
                    fill: "none",
                    stroke: color,
                    strokeWidth: 2);
                break;
        }
    }

    static string Triangle(Position tip, double unitX, double unitY, double length, double halfWidth)
    {
        var baseX = tip.X - unitX * length;
        var baseY = tip.Y - unitY * length;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"M {tip.X:0.##} {tip.Y:0.##} L {baseX - unitY * halfWidth:0.##} {baseY + unitX * halfWidth:0.##} L {baseX + unitY * halfWidth:0.##} {baseY - unitX * halfWidth:0.##} Z");
    }

    static void DrawShape(SvgBuilder builder, Shape shape, RenderOptions options)
    {
        switch (shape)
        {
            case ActorShape actor:
                DrawActor(builder, actor, options);
                break;
            case UseCaseItem useCase:
                DrawUseCase(builder, useCase, options);
                break;
            case NoteShape note:
                DrawNote(builder, note, options);
                break;
            case JsonShape table:
                DrawJsonTable(builder, table, options);
                break;
        }
    }

    static void DrawActor(SvgBuilder builder, ActorShape shape, RenderOptions options)
    {
        var actor = shape.Actor;
        var style = actor.Style;
        var fill = style?.Fill ?? nodeFill;
        var stroke = style?.Stroke ?? nodeStroke;
        var strokeWidth = style?.StrokeWidth ?? 2;
        var center = shape.Node.Position;
        var contentTop = center.Y - shape.ContentHeight / 2;
        var figureCenterY = contentTop + figureHeight / 2;

        builder.BeginGroup(cssClass: "usecase-actor", transform: string.Create(CultureInfo.InvariantCulture, $"translate({center.X:0.##},{figureCenterY:0.##})"));
        var type = actor.Type;
        if (type == ActorType.Icon &&
            !DrawIcon(builder, actor.Icon, stroke))
        {
            // An icon that is not registered falls back to the stick figure.
            type = ActorType.Normal;
        }

        switch (type)
        {
            case ActorType.Normal:
                builder.AddPath(stickFigurePath, fill: fill, stroke: stroke, strokeWidth: strokeWidth);
                break;
            case ActorType.Hollow:
                builder.AddCircle(0, -23, 9, fill: "none", stroke: stroke, strokeWidth: strokeWidth);
                builder.AddPath(hollowBodyPath, fill: "none", stroke: stroke, strokeWidth: strokeWidth);
                break;
            case ActorType.Awesome:
                builder.AddPath(awesomePath, fill: fill, stroke: stroke, strokeWidth: strokeWidth);
                break;
        }

        if (actor.Business)
        {
            builder.AddPath(BusinessActorMarker(type), fill: "none", stroke: stroke, strokeWidth: strokeWidth);
        }

        builder.EndGroup();

        var textY = contentTop + figureHeight + actorLabelGap;
        var color = style?.Color ?? textColor;
        if (actor.Stereotype is not null)
        {
            builder.AddText(
                center.X,
                textY + shape.StereotypeHeight / 2,
                $"«{actor.Stereotype}»",
                anchor: "middle",
                baseline: "middle",
                fontSize: shape.FontSize,
                fontFamily: options.FontFamily,
                fill: color,
                cssClass: "usecase-stereotype");
            textY += shape.StereotypeHeight + stereotypeGap;
        }

        AddLabel(
            builder,
            center.X,
            textY + shape.LabelHeight / 2,
            actor.Label,
            shape.FontSize,
            color,
            "actor-label",
            options);
    }

    // An icon from a registered pack, fitted to the figure's width. False when the icon is not registered.
    static bool DrawIcon(SvgBuilder builder, string? icon, string color)
    {
        if (icon is null ||
            IconPackRegistry.Resolve(icon) is not { } packIcon)
        {
            return false;
        }

        const double box = 48;
        var scale = box / Math.Max(packIcon.Width, packIcon.Height);
        var left = -packIcon.Width * scale / 2;
        var top = -packIcon.Height * scale / 2;
        // The colour can come from a user `style`, so escape it like any other attribute value.
        color = SvgXml.EscapeAttribute(color);
        builder.AddRawSvg(
            string.Create(
                CultureInfo.InvariantCulture,
                $"<g transform='translate({left:0.##},{top:0.##}) scale({scale:0.####})' style='color:{color}' fill='{color}'>{packIcon.Body}</g>"));
        return true;
    }

    /// <summary>
    /// The business slash: a chord across the actor's head, at 60 degrees, set off its centre. Mermaid
    /// works it out from each figure's head; an icon actor gets a fixed stroke beside its top right.
    /// </summary>
    static string BusinessActorMarker(ActorType type)
    {
        var (centerY, radius) = type switch
        {
            ActorType.Hollow => (-23.0, 9.0),
            ActorType.Awesome => (-21.0, 13.0),
            ActorType.Icon => (double.NaN, double.NaN),
            _ => (-24.0, 12.0)
        };

        if (double.IsNaN(centerY))
        {
            return "M 12 -8 L 26 -26";
        }

        const double angle = Math.PI / 3;
        const double offsetRatio = 0.6;
        var centerOffset = radius * offsetRatio;
        var halfChord = radius * Math.Sqrt(1 - offsetRatio * offsetRatio);
        var directionX = Math.Cos(angle);
        var directionY = -Math.Sin(angle);
        var markerX = centerOffset * -directionY;
        var markerY = centerY + centerOffset * directionX;
        var deltaX = halfChord * directionX;
        var deltaY = halfChord * directionY;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"M {markerX - deltaX:0.##} {markerY - deltaY:0.##} L {markerX + deltaX:0.##} {markerY + deltaY:0.##}");
    }

    static void DrawUseCase(SvgBuilder builder, UseCaseItem shape, RenderOptions options)
    {
        var useCase = shape.UseCase;
        var style = useCase.Style;
        var fill = style?.Fill ?? nodeFill;
        var stroke = style?.Stroke ?? nodeStroke;
        var strokeWidth = style?.StrokeWidth ?? 2;
        var center = shape.Node.Position;
        var radiusX = shape.Width / 2;
        var radiusY = shape.Height / 2;

        if (shape.IsEllipse)
        {
            builder.AddPath(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"M {center.X - radiusX:0.##} {center.Y:0.##} A {radiusX:0.##} {radiusY:0.##} 0 1 0 {center.X + radiusX:0.##} {center.Y:0.##} A {radiusX:0.##} {radiusY:0.##} 0 1 0 {center.X - radiusX:0.##} {center.Y:0.##} Z"),
                fill: fill,
                stroke: stroke,
                strokeWidth: strokeWidth,
                strokeDasharray: style?.StrokeDasharray,
                cssClass: "usecase-element");

            if (useCase.Business)
            {
                builder.AddPath(BusinessUseCaseMarker(center, radiusX, radiusY, shape.LabelWidth), fill: "none", stroke: stroke, strokeWidth: strokeWidth);
            }
        }
        else
        {
            builder.AddRect(
                center.X - radiusX,
                center.Y - radiusY,
                shape.Width,
                shape.Height,
                fill: fill,
                stroke: stroke,
                strokeWidth: strokeWidth,
                cssClass: "usecase-element");
        }

        AddLabel(
            builder,
            center.X,
            center.Y,
            useCase.Label,
            shape.FontSize,
            style?.Color ?? textColor,
            "usecase-label",
            options,
            useCase.Stereotype is null ? null : $"«{useCase.Stereotype}»");
    }

    /// <summary>
    /// The business slash of a use case: a line rising from the bottom of the ellipse just right of the
    /// label to its top near the right end, both ends on the outline.
    /// </summary>
    static string BusinessUseCaseMarker(Position center, double radiusX, double radiusY, double labelWidth)
    {
        const double inset = 2;
        var startX = labelWidth / 2 + inset;
        var endX = radiusX - inset;
        var startY = radiusY * Math.Sqrt(Math.Max(0, 1 - startX * startX / (radiusX * radiusX)));
        var endY = -radiusY * Math.Sqrt(Math.Max(0, 1 - endX * endX / (radiusX * radiusX)));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"M {center.X + startX:0.##} {center.Y + startY:0.##} L {center.X + endX:0.##} {center.Y + endY:0.##}");
    }

    static void DrawNote(SvgBuilder builder, NoteShape shape, RenderOptions options)
    {
        var center = shape.Node.Position;
        builder.AddRect(
            center.X - shape.Width / 2,
            center.Y - shape.Height / 2,
            shape.Width,
            shape.Height,
            fill: noteFill,
            stroke: noteStroke,
            strokeWidth: 1,
            cssClass: "usecase-note");
        AddLabel(
            builder,
            center.X,
            center.Y,
            shape.Note.Text,
            shape.FontSize,
            textColor,
            null,
            options);
    }

    // A JSON table: its identifier in a title row, then a row for each leaf, key on the left, value on the right.
    static void DrawJsonTable(SvgBuilder builder, JsonShape shape, RenderOptions options)
    {
        var table = shape.Table;
        var style = table.Style;
        var fill = style?.Fill ?? nodeFill;
        var stroke = style?.Stroke ?? nodeStroke;
        var strokeWidth = style?.StrokeWidth ?? 1;
        var color = style?.Color ?? textColor;
        var center = shape.Node.Position;
        var left = center.X - shape.Width / 2;
        var top = center.Y - shape.Height / 2;

        builder.AddRect(left, top, shape.Width, shape.Height, fill: fill, stroke: stroke, strokeWidth: strokeWidth, cssClass: "usecase-json-table");
        builder.AddText(
            center.X,
            top + jsonTitleHeight / 2,
            table.Id,
            anchor: "middle",
            baseline: "middle",
            fontSize: shape.FontSize,
            fontFamily: options.FontFamily,
            fontWeight: "bold",
            fill: color,
            cssClass: "usecase-json-title");

        var rowTop = top + jsonTitleHeight;
        foreach (var row in table.Rows)
        {
            builder.AddLine(left, rowTop, left + shape.Width, rowTop, stroke: stroke, strokeWidth: strokeWidth);
            builder.AddText(
                left + jsonCellPadding,
                rowTop + jsonRowHeight / 2,
                row.Key,
                anchor: "start",
                baseline: "middle",
                fontSize: shape.FontSize,
                fontFamily: options.FontFamily,
                fill: color,
                cssClass: "usecase-json-key");
            builder.AddText(
                left + shape.KeyColumnWidth + jsonCellPadding,
                rowTop + jsonRowHeight / 2,
                row.Value,
                anchor: "start",
                baseline: "middle",
                fontSize: shape.FontSize,
                fontFamily: options.FontFamily,
                fill: color,
                cssClass: "usecase-json-value");
            rowTop += jsonRowHeight;
        }

        if (table.Rows.Count > 0)
        {
            var dividerX = left + shape.KeyColumnWidth;
            builder.AddLine(dividerX, top + jsonTitleHeight, dividerX, top + shape.Height, stroke: stroke, strokeWidth: strokeWidth);
        }
    }

    sealed class LayoutGraph : GraphDiagramBase;

    // A node of the layout and what is drawn for it.
    abstract class Shape(string id, double width, double height)
    {
        public string Id { get; } = id;
        public double Width { get; } = width;
        public double Height { get; } = height;

        public Node Node { get; } = new()
        {
            Id = id,
            Width = width,
            Height = height
        };
    }

    sealed class ActorShape(
        UseCaseActor actor,
        double width,
        double height,
        double contentHeight,
        double labelHeight,
        double stereotypeHeight,
        double fontSize) :
        Shape(actor.Id, width, height)
    {
        public UseCaseActor Actor { get; } = actor;
        public double ContentHeight { get; } = contentHeight;
        public double LabelHeight { get; } = labelHeight;
        public double StereotypeHeight { get; } = stereotypeHeight;
        public double FontSize { get; } = fontSize;
    }

    sealed class UseCaseItem(UseCase useCase, string text, double width, double height, double labelWidth, double fontSize) :
        Shape(useCase.Id, width, height)
    {
        public UseCase UseCase { get; } = useCase;
        public string Text { get; } = text;
        public double LabelWidth { get; } = labelWidth;
        public double FontSize { get; } = fontSize;
        public bool IsEllipse => UseCase.Shape == UseCaseShape.Ellipse;
    }

    sealed class NoteShape(UseCaseNote note, double width, double height, double fontSize) :
        Shape(note.Id, width, height)
    {
        public UseCaseNote Note { get; } = note;
        public double FontSize { get; } = fontSize;
    }

    sealed class JsonShape(JsonTable table, double width, double height, double keyColumnWidth, double fontSize) :
        Shape(table.Id, width, height)
    {
        public JsonTable Table { get; } = table;
        public double KeyColumnWidth { get; } = keyColumnWidth;
        public double FontSize { get; } = fontSize;
    }

    readonly record struct BoundaryFrame(double X, double Y, double Width, double Height, double Band, double TabWidth);
}
