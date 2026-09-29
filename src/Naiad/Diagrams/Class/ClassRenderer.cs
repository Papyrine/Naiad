namespace Naiad.Diagrams.Class;

public class ClassRenderer :
    IDiagramRenderer<ClassModel>
{
    const double classPadding = 10;
    const double lineHeight = 20;
    const double minWidth = 100;
    const double separatorHeight = 1;

    // Far enough along the edge to clear both the class border and the relationship marker.
    const double cardinalityDistance = 22;
    const double cardinalityOffset = 10;

    public SvgDocument Render(ClassModel model, RenderOptions options)
    {
        // Convert to graph diagram for layout
        var graphModel = ConvertToGraphModel(model, options);

        // Run layout
        var layoutOptions = new LayoutOptions
        {
            Direction = model.Direction,
            NodeSeparation = 60,
            RankSeparation = 80
        };
        var dagreEngine = new DagreEngine();
        var layoutResult = dagreEngine.BuildLayout(graphModel, layoutOptions);

        // Build SVG
        var builder = new SvgBuilder();
        builder.Size(layoutResult.Width, layoutResult.Height);
        builder.Padding(options.Padding);
        builder.AddArrowMarker();
        builder.AddArrowMarker("arrowhead-open");

        // Add relationship markers
        AddRelationshipMarkers(builder);

        // Render edges first (behind nodes). Each relationship is paired with the edge Dagre routed for it
        // (edges are built in relationship order), so edges curve, parallel relationships separate, and the
        // routing comes from the shared layout rather than straight lines computed here.
        for (var index = 0; index < model.Relationships.Count; index++)
        {
            RenderRelationship(builder, model.Relationships[index], graphModel.Edges[index], options);
        }

        // Render class boxes
        foreach (var classDef in model.Classes)
        {
            var node = graphModel.GetNode(classDef.Id);
            if (node != null)
            {
                RenderClassBox(builder, classDef, node, options);
            }
        }

        return builder.Build();
    }

    static GraphDiagramBase ConvertToGraphModel(ClassModel model, RenderOptions options)
    {
        var graph = new FlowchartModel
        {
            Direction = model.Direction
        };

        // Create nodes for each class
        foreach (var classDef in model.Classes)
        {
            var (width, height) = CalculateClassSize(classDef, options);
            var node = new Node
            {
                Id = classDef.Id,
                Label = classDef.Name,
                Width = width,
                Height = height
            };
            graph.AddNode(node);
        }

        // Create edges for each relationship
        foreach (var rel in model.Relationships)
        {
            var edge = new Edge
            {
                SourceId = rel.FromId,
                TargetId = rel.ToId,
                Label = rel.Label,
                Type = EdgeType.Arrow
            };
            graph.AddEdge(edge);
        }

        return graph;
    }

    static (double width, double height) CalculateClassSize(ClassDefinition classDef, RenderOptions options)
    {
        // Calculate width based on longest text
        var maxTextWidth = MeasureText(classDef.Name, options.FontSize, true);

        if (classDef.Annotation.HasValue)
        {
            var annotationText = $"<<{classDef.Annotation.Value.ToString().ToLower()}>>";
            maxTextWidth = Math.Max(maxTextWidth, MeasureText(annotationText, options.FontSize - 2));
        }

        foreach (var member in classDef.Members)
        {
            var text = FormatMember(member);
            maxTextWidth = Math.Max(maxTextWidth, MeasureText(text, options.FontSize));
        }

        foreach (var method in classDef.Methods)
        {
            var text = FormatMethod(method);
            maxTextWidth = Math.Max(maxTextWidth, MeasureText(text, options.FontSize));
        }

        var width = Math.Max(minWidth, maxTextWidth + classPadding * 2);

        // Calculate height
        var height = classPadding; // Top padding
        if (classDef.Annotation.HasValue)
        {
            height += lineHeight;
        }
        height += lineHeight; // Class name
        height += classPadding; // After name

        if (classDef.Members.Count > 0)
        {
            height += separatorHeight;
            height += classDef.Members.Count * lineHeight;
        }

        if (classDef.Methods.Count > 0)
        {
            height += separatorHeight;
            height += classDef.Methods.Count * lineHeight;
        }

        height += classPadding; // Bottom padding

        return (width, height);
    }

    static void RenderClassBox(SvgBuilder builder, ClassDefinition classDef, Node node, RenderOptions options)
    {
        var x = node.Position.X - node.Width / 2;
        var y = node.Position.Y - node.Height / 2;
        var width = node.Width;
        var height = node.Height;

        // Background
        var fillColor = classDef.Annotation switch
        {
            ClassAnnotation.Interface => "#E8F4FD",
            ClassAnnotation.Abstract => "#FFF3E0",
            ClassAnnotation.Enumeration => "#E8F5E9",
            _ => "#FFFFDE"
        };

        builder.AddRect(
            x,
            y,
            width,
            height,
            rx: 0,
            fill: fillColor,
            stroke: "#333",
            strokeWidth: 1);

        var currentY = y + classPadding;
        var centerX = node.Position.X;

        // Annotation
        if (classDef.Annotation.HasValue)
        {
            var annotationText = $"<<{classDef.Annotation.Value.ToString().ToLower()}>>";
            builder.AddText(
                centerX,
                currentY + lineHeight / 2,
                annotationText,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily,
                fontWeight: "normal",
                fill: "#666");
            currentY += lineHeight;
        }

        // Class name
        builder.AddText(
            centerX,
            currentY + lineHeight / 2,
            classDef.Name,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily,
            fontWeight: "bold");
        currentY += lineHeight + classPadding;

        // Members separator and list
        if (classDef.Members.Count > 0)
        {
            builder.AddLine(
                x,
                currentY,
                x + width,
                currentY,
                stroke: "#333",
                strokeWidth: 1);
            currentY += separatorHeight;

            foreach (var member in classDef.Members)
            {
                var memberText = FormatMember(member);
                builder.AddText(
                    x + classPadding,
                    currentY + lineHeight / 2,
                    memberText,
                    anchor: "start",
                    baseline: "middle",
                    fontSize: options.FontSize,
                    fontFamily: options.FontFamily);
                currentY += lineHeight;
            }
        }

        // Methods separator and list
        if (classDef.Methods.Count > 0)
        {
            builder.AddLine(
                x,
                currentY,
                x + width,
                currentY,
                stroke: "#333",
                strokeWidth: 1);
            currentY += separatorHeight;

            foreach (var method in classDef.Methods)
            {
                var methodText = FormatMethod(method);
                builder.AddText(
                    x + classPadding,
                    currentY + lineHeight / 2,
                    methodText,
                    anchor: "start",
                    baseline: "middle",
                    fontSize: options.FontSize,
                    fontFamily: options.FontFamily);
                currentY += lineHeight;
            }
        }
    }

    static void RenderRelationship(SvgBuilder builder, ClassRelationship rel, Edge edge, RenderOptions options)
    {
        var points = edge.Points;
        if (points.Count < 2)
        {
            return;
        }

        var dashArray = rel.IsDashed ? "5,5" : null;

        // Dagre-routed, B-spline-smoothed path shared with the other graph diagrams.
        builder.AddPath(
            EdgePath.Build(points),
            fill: "none",
            stroke: "#333",
            strokeWidth: 1,
            strokeDasharray: dashArray);

        // Each marker sits on the end the author wrote it on — the triangle of `Animal <|-- Dog` belongs
        // to Animal — oriented by the curve's tangent as it arrives there.
        var start = points[0];
        var end = points[^1];
        DrawRelationshipMarker(builder, rel.FromMarker, start.X, start.Y, points[1].X, points[1].Y);
        DrawRelationshipMarker(builder, rel.ToMarker, end.X, end.Y, points[^2].X, points[^2].Y);

        // Draw label if present
        if (!string.IsNullOrEmpty(rel.Label))
        {
            var label = edge.LabelPosition;
            var labelFontSize = options.FontSize - 2;
            builder.AddEdgeLabel(
                label.X,
                label.Y,
                MeasureText(rel.Label, labelFontSize) + 8,
                labelFontSize + 4,
                rel.Label,
                labelFontSize,
                options.FontFamily);
        }

        // Draw cardinalities near each end
        DrawCardinality(builder, rel.FromCardinality, start, points[1], options);
        DrawCardinality(builder, rel.ToCardinality, end, points[^2], options);
    }

    /// <summary>
    /// Places a cardinality beside the line just clear of the node it belongs to. The offset follows the
    /// edge's own direction rather than a fixed screen direction, so the label stays outside the class box
    /// (which paints over it) whichever way the edge leaves.
    /// </summary>
    static void DrawCardinality(SvgBuilder builder, string? text, Position at, Position toward, RenderOptions options)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var dx = toward.X - at.X;
        var dy = toward.Y - at.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001)
        {
            return;
        }

        dx /= length;
        dy /= length;

        builder.AddText(
            at.X + dx * cardinalityDistance - dy * cardinalityOffset,
            at.Y + dy * cardinalityDistance + dx * cardinalityOffset,
            text,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize - 2,
            fontFamily: options.FontFamily);
    }

    static void DrawRelationshipMarker(SvgBuilder builder, RelationshipMarker marker, double x, double y, double fromX, double fromY)
    {
        var angle = Math.Atan2(y - fromY, x - fromX);
        const double markerSize = 10.0;

        switch (marker)
        {
            case RelationshipMarker.Triangle:
                var points = GetTrianglePoints(x, y, angle, markerSize);
                builder.AddPolygon(points, fill: "#fff", stroke: "#333");
                break;

            case RelationshipMarker.FilledDiamond:
                var diamondPoints = GetDiamondPoints(x, y, angle, markerSize);
                builder.AddPolygon(diamondPoints, fill: "#333", stroke: "#333");
                break;

            case RelationshipMarker.HollowDiamond:
                var aggDiamondPoints = GetDiamondPoints(x, y, angle, markerSize);
                builder.AddPolygon(aggDiamondPoints, fill: "#fff", stroke: "#333");
                break;

            case RelationshipMarker.Arrow:
                var arrowPoints = GetArrowPoints(x, y, angle, markerSize);
                builder.AddPolygon(arrowPoints, fill: "#333");
                break;
        }
    }

    static Position[] GetTrianglePoints(double x, double y, double angle, double size)
    {
        var backAngle1 = angle + Math.PI - Math.PI / 6;
        var backAngle2 = angle + Math.PI + Math.PI / 6;

        return
        [
            new(x, y),
            new(x + size * Math.Cos(backAngle1), y + size * Math.Sin(backAngle1)),
            new(x + size * Math.Cos(backAngle2), y + size * Math.Sin(backAngle2))
        ];
    }

    static Position[] GetDiamondPoints(double x, double y, double angle, double size)
    {
        var halfSize = size / 2;
        return
        [
            new(x, y),
            new(x - halfSize * Math.Cos(angle) + halfSize * Math.Sin(angle),
                y - halfSize * Math.Sin(angle) - halfSize * Math.Cos(angle)),
            new(x - size * Math.Cos(angle), y - size * Math.Sin(angle)),
            new(x - halfSize * Math.Cos(angle) - halfSize * Math.Sin(angle),
                y - halfSize * Math.Sin(angle) + halfSize * Math.Cos(angle))
        ];
    }

    static Position[] GetArrowPoints(double x, double y, double angle, double size)
    {
        var backAngle1 = angle + Math.PI - Math.PI / 6;
        var backAngle2 = angle + Math.PI + Math.PI / 6;

        return
        [
            new(x, y),
            new(x + size * Math.Cos(backAngle1), y + size * Math.Sin(backAngle1)),
            new(x + size * Math.Cos(backAngle2), y + size * Math.Sin(backAngle2))
        ];
    }

    static void AddRelationshipMarkers(SvgBuilder builder)
    {
        // Inheritance marker (hollow triangle)
        builder.AddMarker("inheritance", "M0,0 L10,5 L0,10 Z", 12, 12, 10, 5, "#fff");
        // Composition marker (filled diamond)
        builder.AddMarker("composition", "M0,5 L5,0 L10,5 L5,10 Z", 12, 12, 10, 5, "#333");
        // Aggregation marker (hollow diamond)
        builder.AddMarker("aggregation", "M0,5 L5,0 L10,5 L5,10 Z", 12, 12, 10, 5, "#fff");
    }

    static string FormatMember(ClassMember member)
    {
        var visibility = GetVisibilitySymbol(member.Visibility);
        var typeStr = !string.IsNullOrEmpty(member.Type) ? $" : {member.Type}" : "";
        return $"{visibility}{member.Name}{typeStr}{Classifier(member.IsStatic, false)}";
    }

    static string FormatMethod(ClassMethod method)
    {
        var visibility = GetVisibilitySymbol(method.Visibility);
        var parameters = string.Join(", ", method.Parameters.Select(FormatParameter));
        var returnTypeStr = !string.IsNullOrEmpty(method.ReturnType) ? $" : {method.ReturnType}" : "";
        return $"{visibility}{method.Name}({parameters}){Classifier(method.IsStatic, method.IsAbstract)}{returnTypeStr}";
    }

    static string FormatParameter(MethodParameter parameter)
    {
        if (string.IsNullOrEmpty(parameter.Type))
        {
            return parameter.Name;
        }

        return $"{parameter.Name}: {parameter.Type}";
    }

    // Mermaid's trailing classifier: $ for static, * for abstract.
    static string Classifier(bool isStatic, bool isAbstract) =>
        (isStatic, isAbstract) switch
        {
            (true, _) => "$",
            (_, true) => "*",
            _ => ""
        };

    static string GetVisibilitySymbol(Visibility visibility) =>
        visibility switch
        {
            Visibility.Public => "+ ",
            Visibility.Private => "- ",
            Visibility.Protected => "# ",
            Visibility.PackagePrivate => "~ ",
            _ => ""
        };

    static double MeasureText(string text, double fontSize, bool bold = false)
    {
        var factor = bold ? 0.65 : 0.55;
        return text.Length * fontSize * factor;
    }
}

// Temporary model for layout - reusing flowchart structure
file class FlowchartModel : GraphDiagramBase;
