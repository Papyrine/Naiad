namespace Naiad.Diagrams.Flowchart;

public partial class FlowchartRenderer(ILayoutEngine? layoutEngine = null) :
    IDiagramRenderer<FlowchartModel>
{
    ILayoutEngine layoutEngine = layoutEngine ?? new DagreEngine();

    // Mermaid default colors
    const string nodeFill = "#ECECFF";
    const string nodeStroke = "#9370DB";
    const string edgeStroke = "#333333";

    // Mermaid's default cluster palette (pale yellow), so subgraph boxes read clearly against a white canvas.
    const string subgraphFill = "#ffffde";
    const string subgraphStroke = "#aaaa33";

    // Matches "prefix:name" icon tokens in labels — FontAwesome (fa:fa-bell) or a
    // registered iconify pack (logos:aws). Tokens that resolve to neither stay as text.
    static Regex iconPattern = IconPatternMyRegex();

    [GeneratedRegex("[A-Za-z0-9]+:[A-Za-z0-9][A-Za-z0-9_-]*", RegexOptions.Compiled)]
    private static partial Regex IconPatternMyRegex();

    public SvgDocument Render(FlowchartModel model, RenderOptions options)
    {
        // Calculate node sizes based on text
        foreach (var node in model.Nodes)
        {
            var label = DisplayText(node.Label ?? node.Id);
            var (textForMeasure, iconCount) = AnalyzeLabel(label);
            var textSize = MeasureText(textForMeasure, options.FontSize);
            node.Width = textSize.Width + 30 + iconCount * 20;
            node.Height = textSize.Height + 27;

            // Adjust size for different shapes
            if (node.Shape is NodeShape.Circle or NodeShape.DoubleCircle)
            {
                var diameter = Math.Max(node.Width, node.Height);
                node.Width = diameter;
                node.Height = diameter;
            }
            else if (node.Shape == NodeShape.Diamond)
            {
                node.Width *= 1.4;
                node.Height *= 1.4;
            }
            else if (node.Shape == NodeShape.Asymmetric)
            {
                // The left notch bites into the box, and the label is centred in it, so grow the box by the
                // notch's depth on both sides rather than letting it cut across the text.
                node.Width += ShapePathGenerator.AsymmetricNotch(node.Height) * 2;
            }
            else if (node.Shape == NodeShape.Subroutine)
            {
                // The vertical bars are drawn inside the node, so the label needs the space between them
                // rather than the full width.
                node.Width += ShapePathGenerator.SubroutineBarInset * 2;
            }
        }

        SizeEmptySubgraphs(model.Subgraphs, options);

        // Reserve space for edge labels so the layout keeps a rank gap clear for them.
        foreach (var edge in model.Edges)
        {
            if (string.IsNullOrEmpty(edge.Label))
            {
                continue;
            }

            var labelSize = MeasureText(DisplayText(edge.Label), options.FontSize);
            edge.LabelWidth = labelSize.Width + 16;
            edge.LabelHeight = labelSize.Height + 8;
        }

        // Run layout
        var layoutOptions = new LayoutOptions
        {
            Direction = model.Direction,
            NodeSeparation = 50,
            RankSeparation = 70,
            SubgraphDirections = true
        };
        var layoutResult = layoutEngine.BuildLayout(model, layoutOptions);

        ClipEdgesToShapes(model);

        // Build SVG
        var builder = new SvgBuilder();
        builder.Options(options);
        builder.Size(layoutResult.Width, layoutResult.Height);
        builder.Padding(options.Padding);

        // The arrow/circle/cross markers are only referenced by edges; skip the defs entirely when there are none.
        if (model.Edges.Count > 0)
        {
            builder.AddMermaidArrowMarker();
            builder.AddMermaidCircleMarker();
            builder.AddMermaidCrossMarker();
        }

        // One more set of heads for each colour a `linkStyle` gives a link, keyed by that colour.
        var coloredMarkers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in model.Edges)
        {
            if (edge.Style?.Stroke is { } stroke &&
                !coloredMarkers.ContainsKey(stroke))
            {
                var suffix = "-c" + coloredMarkers.Count.ToString(CultureInfo.InvariantCulture);
                coloredMarkers[stroke] = suffix;
                builder.AddMermaidColoredMarkers(stroke, suffix);
            }
        }

        // Add Mermaid CSS styles
        builder.AddStyles(MermaidStyles.FlowchartStyles);

        // Render subgraph boxes first (behind everything), outermost first.
        RenderSubgraphBoxes(builder, model.Subgraphs);

        // Render edges first (behind nodes)
        foreach (var edge in model.Edges)
        {
            RenderEdge(builder, edge, options.FontSize, coloredMarkers);
        }

        // Render nodes
        foreach (var node in model.Nodes)
        {
            RenderNode(builder, node);
        }

        // Titles go last: an edge crossing into a subgraph passes through the title band, and painting the
        // edge over the text left the title unreadable.
        RenderSubgraphTitles(builder, model.Subgraphs, options);

        return builder.Build();
    }

    /// <summary>
    /// The layout routes every edge to the box around a node. For a shape that does not fill its box that
    /// leaves the edge stopping in mid-air beside it, so each end is pulled in along its own line to where
    /// it meets the shape.
    /// </summary>
    static void ClipEdgesToShapes(FlowchartModel model)
    {
        foreach (var edge in model.Edges)
        {
            var points = edge.Points;
            if (points.Count < 2)
            {
                continue;
            }

            if (model.GetNode(edge.SourceId) is { } source)
            {
                points[0] = ClipToShape(source, points[1], points[0]);
            }

            if (model.GetNode(edge.TargetId) is { } target)
            {
                points[^1] = ClipToShape(target, points[^2], points[^1]);
            }
        }
    }

    // Where the line from the node's centre towards a point leaves the node's shape, or the end the layout
    // gave when the shape fills its box (or the point is already inside it).
    static Position ClipToShape(Node node, Position toward, Position current)
    {
        var dx = toward.X - node.Position.X;
        var dy = toward.Y - node.Position.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            return current;
        }

        var halfWidth = node.Width / 2;
        var halfHeight = node.Height / 2;

        double reach;
        switch (node.Shape)
        {
            case NodeShape.Diamond:
                reach = 1 / (Math.Abs(dx) / halfWidth + Math.Abs(dy) / halfHeight);
                break;

            case NodeShape.Circle:
            case NodeShape.DoubleCircle:
                reach = Math.Min(halfWidth, halfHeight) / length;
                break;

            case NodeShape.Hexagon:
                // Flat top and bottom, and a slanted side from each end of them to the point at mid-height.
                var slanted = 1 / (Math.Abs(dx) / halfWidth + Math.Abs(dy) / (halfHeight * 2));
                var flat = Math.Abs(dy) < 1e-9 ? double.MaxValue : halfHeight / Math.Abs(dy);
                reach = Math.Min(slanted, flat);
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

    // A markdown string is measured, and falls back to plain text, without its markers.
    [return: NotNullIfNotNull(nameof(label))]
    static string? DisplayText(string? label)
    {
        if (label is not null &&
            MarkdownLabel.IsMarkdown(label))
        {
            return MarkdownLabel.Plain(label);
        }

        return label;
    }

    // A subgraph takes its size from what it holds. One that holds nothing is drawn as a box around its
    // title, sized the way a node is around its label.
    static void SizeEmptySubgraphs(IEnumerable<Subgraph> subgraphs, RenderOptions options)
    {
        foreach (var subgraph in subgraphs)
        {
            if (subgraph.NodeIds.Count == 0 &&
                subgraph.NestedSubgraphs.Count == 0)
            {
                var titleSize = MeasureText(LabelLines.Flatten(DisplayText(subgraph.Title ?? subgraph.Id)), options.FontSize);
                subgraph.Width = titleSize.Width + 30;
                subgraph.Height = titleSize.Height + 27;
                continue;
            }

            SizeEmptySubgraphs(subgraph.NestedSubgraphs, options);
        }
    }

    static void RenderSubgraphBoxes(SvgBuilder builder, IEnumerable<Subgraph> subgraphs)
    {
        foreach (var subgraph in subgraphs)
        {
            RenderSubgraphBox(builder, subgraph);

            // Nested subgraphs after their parent so they sit on top of it.
            RenderSubgraphBoxes(builder, subgraph.NestedSubgraphs);
        }
    }

    static void RenderSubgraphTitles(SvgBuilder builder, IEnumerable<Subgraph> subgraphs, RenderOptions options)
    {
        foreach (var subgraph in subgraphs)
        {
            RenderSubgraphTitle(builder, subgraph, options);
            RenderSubgraphTitles(builder, subgraph.NestedSubgraphs, options);
        }
    }

    static void RenderSubgraphBox(SvgBuilder builder, Subgraph graph)
    {
        var bounds = graph.Bounds;
        var style = graph.Style;

        builder.AddRect(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            rx: 6,
            fill: style?.Fill ?? subgraphFill,
            stroke: style?.Stroke ?? subgraphStroke,
            strokeWidth: style?.StrokeWidth ?? 1,
            cssClass: "cluster");
    }

    static void RenderSubgraphTitle(SvgBuilder builder, Subgraph graph, RenderOptions options)
    {
        // The title band has room for one line, so a `<br/>` in it becomes a space.
        var title = LabelLines.Flatten(DisplayText(graph.Title ?? graph.Id));
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        var style = graph.Style;
        var titleY = graph.Bounds.Y + 14;

        // An edge entering the subgraph crosses the title band, and lands on the title itself whenever the
        // node it targets is the one the box is centred on. Backing the text with the box's own fill hides
        // the line behind it - invisible against the box, but the title stays readable.
        var titleSize = MeasureText(title, options.FontSize);
        builder.AddRect(
            graph.Position.X - titleSize.Width / 2 - 6,
            titleY - titleSize.Height / 2 - 2,
            titleSize.Width + 12,
            titleSize.Height + 4,
            fill: style?.Fill ?? subgraphFill,
            stroke: "none");

        builder.AddText(
            graph.Position.X,
            titleY,
            title,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily,
            fontWeight: "bold",
            fill: style?.Color ?? edgeStroke,
            cssClass: "cluster-label");
    }

    static void RenderNode(SvgBuilder builder, Node node)
    {
        var x = node.Position.X - node.Width / 2;
        var y = node.Position.Y - node.Height / 2;

        var shapePath = ShapePathGenerator.GetPath(node.Shape, x, y, node.Width, node.Height);

        var style = node.Style;
        builder.AddPath(
            shapePath,
            fill: style?.Fill ?? nodeFill,
            stroke: style?.Stroke ?? nodeStroke,
            strokeWidth: style?.StrokeWidth ?? 1,
            strokeDasharray: style?.StrokeDasharray);

        // Render label with icon support
        var label = node.Label ?? node.Id;
        string htmlLabel;
        string plainText;
        if (MarkdownLabel.IsMarkdown(label))
        {
            htmlLabel = Paragraph(MarkdownLabel.Html(label), style?.Color);
            plainText = MarkdownLabel.Plain(label);
        }
        else
        {
            htmlLabel = ConvertIconsToHtml(label, style?.Color);
            (plainText, _) = AnalyzeLabel(label);
        }

        builder.AddLabel(
            x, y,
            node.Width,
            node.Height,
            htmlLabel,
            plainText,
            className: "nodeLabel");
    }

    static void RenderEdge(SvgBuilder builder, Edge edge, double fontSize, Dictionary<string, string> coloredMarkers)
    {
        if (edge.Points.Count < 2 ||
            edge.Type == EdgeType.Invisible)
        {
            return;
        }

        var pathData = EdgePath.Build(edge.Points);

        // A `linkStyle` wins over what the link's own spelling asks for.
        var style = edge.Style;

        var strokeDasharray = style?.StrokeDasharray ?? edge.LineStyle switch
        {
            EdgeStyle.Dotted => "2",
            _ => null
        };

        var strokeWidth = style?.StrokeWidth ?? edge.LineStyle switch
        {
            EdgeStyle.Thick => 3.5,
            _ => 2.0
        };

        // Arrow and circle heads follow the link's own colour when it has one. The cross is drawn with a
        // stroke the stylesheet supplies, so it keeps the shared marker.
        var colored = "";
        if (style?.Stroke is { } stroke)
        {
            colored = coloredMarkers.GetValueOrDefault(stroke, "");
        }

        var markerEnd = edge.HasArrowHead ? $"url(#naiad_flowchart-pointEnd{colored})" :
            edge.HasCircleEnd ? $"url(#naiad_flowchart-circleEnd{colored})" :
            edge.HasCrossEnd ? "url(#naiad_flowchart-crossEnd)" : null;

        var markerStart = edge.HasArrowTail ? $"url(#naiad_flowchart-pointStart{colored})" :
            edge.HasCircleTail ? $"url(#naiad_flowchart-circleStart{colored})" :
            edge.HasCrossTail ? "url(#naiad_flowchart-crossStart)" : null;

        builder.AddPath(
            pathData,
            fill: "none",
            stroke: style?.Stroke ?? edgeStroke,
            strokeWidth: strokeWidth,
            strokeDasharray: strokeDasharray,
            markerEnd: markerEnd,
            markerStart: markerStart,
            // The class's stylesheet rule sets the stroke, and a rule beats an attribute, so a link with a
            // stroke of its own goes without the class.
            cssClass: style?.Stroke is null ? "flowchart-link" : null);

        // Render edge label if present
        if (!string.IsNullOrEmpty(edge.Label))
        {
            var labelX = edge.LabelPosition.X;
            var labelY = edge.LabelPosition.Y;
            var plainLabel = DisplayText(edge.Label);
            var labelSize = MeasureText(plainLabel, fontSize);
            var labelWidth = labelSize.Width + 16;
            var labelHeight = labelSize.Height + 8;

            builder.AddEdgeLabelBackground(labelX, labelY, labelWidth, labelHeight);

            var labelHtml = MarkdownLabel.IsMarkdown(edge.Label)
                ? MarkdownLabel.Html(edge.Label)
                : EncodeLines(edge.Label);

            builder.AddLabel(
                labelX - labelWidth / 2,
                labelY - labelHeight / 2,
                labelWidth, labelHeight,
                Paragraph(labelHtml, style?.Color),
                plainLabel,
                className: "edgeLabel");
        }
    }

    /// <summary>
    /// Converts inline icon tokens within a label to HTML: FontAwesome
    /// (<c>fa:fa-name</c>) becomes an <c>&lt;i&gt;</c> element, a registered iconify
    /// pack icon (<c>prefix:name</c>) becomes inline SVG. Other text is HTML-encoded.
    /// </summary>
    static string ConvertIconsToHtml(string text, string? color = null)
    {
        var html = new StringBuilder(
            color is null
                ? "<p>"
                : $"<p style=\"color:{color}\">");
        var lines = LabelLines.Split(text);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                html.Append("<br/>");
            }

            AppendLineHtml(html, lines[i]);
        }

        html.Append("</p>");
        return html.ToString();
    }

    // A label's paragraph, carrying its text colour when it has one of its own.
    static string Paragraph(string innerHtml, string? color)
    {
        if (color is null)
        {
            return $"<p>{innerHtml}</p>";
        }

        return $"<p style=\"color:{color}\">{innerHtml}</p>";
    }

    static void AppendLineHtml(StringBuilder html, string text)
    {
        var lastIndex = 0;

        foreach (Match match in iconPattern.Matches(text))
        {
            if (IconTokenToHtml(match.Value) is not { } iconHtml)
            {
                continue;
            }

            if (match.Index > lastIndex)
            {
                html.Append(WebUtility.HtmlEncode(text[lastIndex..match.Index]));
            }

            html.Append(iconHtml);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            html.Append(WebUtility.HtmlEncode(text[lastIndex..]));
        }
    }

    // HTML-encodes a label line by line, turning its `<br/>` breaks into real ones.
    static string EncodeLines(string text) =>
        string.Join("<br/>", LabelLines.Split(text).Select(WebUtility.HtmlEncode));

    // Removes recognised icon tokens from a label and counts them, for sizing.
    static (string text, int iconCount) AnalyzeLabel(string label)
    {
        var builder = new StringBuilder();
        var lastIndex = 0;
        var count = 0;

        foreach (Match match in iconPattern.Matches(label))
        {
            if (IconTokenToHtml(match.Value) is null)
            {
                continue;
            }

            builder.Append(label, lastIndex, match.Index - lastIndex);
            lastIndex = match.Index + match.Length;
            count++;
        }

        builder.Append(label, lastIndex, label.Length - lastIndex);
        return (builder.ToString(), count);
    }

    // Renders a single "prefix:name" token to inline HTML, or null if it is not an icon.
    static string? IconTokenToHtml(string token)
    {
        var colon = token.IndexOf(':');
        var prefix = token[..colon];
        var name = token[(colon + 1)..];

        // FontAwesome: fa/fab/fas/far + fa-name. Prefer registered geometry — inline SVG the PNG
        // rasterizer can actually draw — and fall back to the webfont <i> (which only resolves in a
        // browser with the Font Awesome CSS) when no matching pack is registered.
        if (prefix is "fa" or "fab" or "fas" or "far" &&
            name.StartsWith("fa-", StringComparison.Ordinal))
        {
            if (IconPackRegistry.Resolve(token) is { } faIcon)
            {
                return InlineIconSvg(faIcon);
            }

            return $"<i class='{prefix} {name}'></i>";
        }

        // Registered iconify pack icon, rendered as inline SVG sized to the text.
        if (IconPackRegistry.Resolve(token) is { } icon)
        {
            return InlineIconSvg(icon);
        }

        return null;
    }

    static string InlineIconSvg(IconPackRegistry.PackIcon icon) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {icon.Width:0.##} {icon.Height:0.##}' style='width:1em;height:1em;vertical-align:-0.125em'>{icon.Body}</svg>");

    // A label is as wide as its longest line and one line pitch taller for each `<br/>` it holds.
    static Size MeasureText(string text, double fontSize)
    {
        var width = LabelLines.WidestLength(text.Trim()) * fontSize * 0.55;
        var height = fontSize * (1.5 + SvgBuilder.LineHeightFactor * (LabelLines.Count(text) - 1));
        return new(width, height);
    }
}
