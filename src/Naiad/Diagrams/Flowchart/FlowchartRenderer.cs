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
            var label = node.Label ?? node.Id;
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

        // Reserve space for edge labels so the layout keeps a rank gap clear for them.
        foreach (var edge in model.Edges)
        {
            if (string.IsNullOrEmpty(edge.Label))
            {
                continue;
            }

            var labelSize = MeasureText(edge.Label, options.FontSize);
            edge.LabelWidth = labelSize.Width + 16;
            edge.LabelHeight = labelSize.Height + 8;
        }

        // Run layout
        var layoutOptions = new LayoutOptions
        {
            Direction = model.Direction,
            NodeSeparation = 50,
            RankSeparation = 70
        };
        var layoutResult = layoutEngine.BuildLayout(model, layoutOptions);

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

        // Add Mermaid CSS styles
        builder.AddStyles(MermaidStyles.FlowchartStyles);

        // Render subgraph boxes first (behind everything), outermost first.
        RenderSubgraphBoxes(builder, model.Subgraphs);

        // Render edges first (behind nodes)
        foreach (var edge in model.Edges)
        {
            RenderEdge(builder, edge, options.FontSize);
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
        var title = graph.Title ?? graph.Id;
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
        var htmlLabel = ConvertIconsToHtml(label, style?.Color);
        var (plainText, _) = AnalyzeLabel(label);

        builder.AddLabel(
            x, y,
            node.Width,
            node.Height,
            htmlLabel,
            plainText,
            className: "nodeLabel");
    }

    static void RenderEdge(SvgBuilder builder, Edge edge, double fontSize)
    {
        if (edge.Points.Count < 2)
        {
            return;
        }

        var pathData = EdgePath.Build(edge.Points);

        var strokeDasharray = edge.LineStyle switch
        {
            EdgeStyle.Dotted => "2",
            _ => null
        };

        var strokeWidth = edge.LineStyle switch
        {
            EdgeStyle.Thick => 3.5,
            _ => 2.0
        };

        var markerEnd = edge.HasArrowHead ? "url(#naiad_flowchart-pointEnd)" :
            edge.HasCircleEnd ? "url(#naiad_flowchart-circleEnd)" :
            edge.HasCrossEnd ? "url(#naiad_flowchart-crossEnd)" : null;

        var markerStart = edge.HasArrowTail ? "url(#naiad_flowchart-pointStart)" : null;

        builder.AddPath(
            pathData,
            fill: "none",
            stroke: edgeStroke,
            strokeWidth: strokeWidth,
            strokeDasharray: strokeDasharray,
            markerEnd: markerEnd,
            markerStart: markerStart,
            cssClass: "flowchart-link");

        // Render edge label if present
        if (!string.IsNullOrEmpty(edge.Label))
        {
            var labelX = edge.LabelPosition.X;
            var labelY = edge.LabelPosition.Y;
            var labelSize = MeasureText(edge.Label, fontSize);
            var labelWidth = labelSize.Width + 16;
            var labelHeight = labelSize.Height + 8;

            builder.AddEdgeLabelBackground(labelX, labelY, labelWidth, labelHeight);

            builder.AddLabel(
                labelX - labelWidth / 2,
                labelY - labelHeight / 2,
                labelWidth, labelHeight,
                $"<p>{WebUtility.HtmlEncode(edge.Label)}</p>",
                edge.Label,
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

        html.Append("</p>");
        return html.ToString();
    }

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

    static Size MeasureText(CharSpan text, double fontSize)
    {
        var width = text.Trim().Length * fontSize * 0.55;
        var height = fontSize * 1.5;
        return new(width, height);
    }
}
