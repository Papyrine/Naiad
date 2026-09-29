namespace Naiad.Diagrams.Requirement;

public class RequirementRenderer : IDiagramRenderer<RequirementModel>
{
    const double boxPadding = 12;
    const double typeLineHeight = 18;
    const double nameLineHeight = 22;
    const double rowHeight = 18;
    const double separatorGap = 10;
    const double minBoxWidth = 160;
    const double columnSpacing = 90;
    const double rowSpacing = 50;
    const double titleHeight = 40;

    const string requirementColor = "#C8E6C9";
    const string requirementStroke = "#4CAF50";
    const string elementColor = "#BBDEFB";
    const string elementStroke = "#2196F3";

    public SvgDocument Render(RequirementModel model, RenderOptions options)
    {
        if (model.Requirements.Count == 0 && model.Elements.Count == 0)
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

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;

        // Requirements go in the left column and elements in the right, each box sized to the rows it
        // actually carries so the canvas ends up the size of its content.
        var requirements = model.Requirements.Select(_ => BuildRequirementBox(_, options)).ToList();
        var elements = model.Elements.Select(_ => BuildElementBox(_, options)).ToList();

        var leftWidth = requirements.Count > 0 ? requirements.Max(_ => _.Width) : 0;
        var rightWidth = elements.Count > 0 ? elements.Max(_ => _.Width) : 0;
        var gap = leftWidth > 0 && rightWidth > 0 ? columnSpacing : 0;

        var contentWidth = leftWidth + gap + rightWidth;
        if (!string.IsNullOrEmpty(model.Title))
        {
            contentWidth = Math.Max(contentWidth, MeasureText(model.Title, options.FontSize + 4, true));
        }

        var contentHeight = Math.Max(StackHeight(requirements), StackHeight(elements));

        var width = contentWidth + options.Padding * 2;
        var height = contentHeight + options.Padding * 2 + titleOffset;

        var builder = new SvgBuilder();
        builder.Size(width, height);
        builder.AddArrowMarker("reqarrow", "#666");

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

        var boxes = new Dictionary<string, NodeBox>();
        var top = options.Padding + titleOffset;

        StackColumn(builder, requirements, options.Padding, leftWidth, top, boxes, options);
        StackColumn(builder, elements, options.Padding + leftWidth + gap, rightWidth, top, boxes, options);

        foreach (var relation in model.Relations)
        {
            if (boxes.TryGetValue(relation.Source, out var from) &&
                boxes.TryGetValue(relation.Target, out var to))
            {
                DrawRelation(builder, from, to, relation.Type, options);
            }
        }

        return builder.Build();
    }

    static double StackHeight(List<Box> boxes)
    {
        if (boxes.Count == 0)
        {
            return 0;
        }

        return boxes.Sum(_ => _.Height) + (boxes.Count - 1) * rowSpacing;
    }

    static void StackColumn(SvgBuilder builder, List<Box> boxes, double columnX, double columnWidth,
        double top, Dictionary<string, NodeBox> positions, RenderOptions options)
    {
        var y = top;
        foreach (var box in boxes)
        {
            // Centre each box in its column so columns of differing widths still line up.
            var x = columnX + (columnWidth - box.Width) / 2;
            positions[box.Name] = new(x + box.Width / 2, y + box.Height / 2, box.Width, box.Height);
            DrawBox(builder, box, x, y, options);
            y += box.Height + rowSpacing;
        }
    }

    static Box BuildRequirementBox(Requirement requirement, RenderOptions options)
    {
        var typeLabel = requirement.Type switch
        {
            RequirementType.FunctionalRequirement => "Functional Requirement",
            RequirementType.InterfaceRequirement => "Interface Requirement",
            RequirementType.PerformanceRequirement => "Performance Requirement",
            RequirementType.PhysicalRequirement => "Physical Requirement",
            RequirementType.DesignConstraint => "Design Constraint",
            _ => "Requirement"
        };

        var rows = new List<string>();
        if (!string.IsNullOrEmpty(requirement.Id))
        {
            rows.Add($"Id: {requirement.Id}");
        }

        if (!string.IsNullOrEmpty(requirement.Text))
        {
            rows.Add($"Text: {requirement.Text}");
        }

        if (requirement.Risk.HasValue)
        {
            rows.Add($"Risk: {requirement.Risk.Value}");
        }

        if (requirement.VerifyMethod.HasValue)
        {
            rows.Add($"Verification: {requirement.VerifyMethod.Value}");
        }

        return CreateBox(requirement.Name, typeLabel, rows, requirementColor, requirementStroke, options);
    }

    static Box BuildElementBox(RequirementElement element, RenderOptions options)
    {
        var rows = new List<string>();
        if (!string.IsNullOrEmpty(element.Type))
        {
            rows.Add($"Type: {element.Type}");
        }

        if (!string.IsNullOrEmpty(element.DocRef))
        {
            rows.Add($"Doc Ref: {element.DocRef}");
        }

        return CreateBox(element.Name, "Element", rows, elementColor, elementStroke, options);
    }

    static Box CreateBox(string name, string typeLabel, List<string> rows, string fill, string stroke,
        RenderOptions options)
    {
        var header = $"<<{typeLabel}>>";

        var textWidth = Math.Max(
            MeasureText(header, options.FontSize - 3),
            MeasureText(name, options.FontSize, true));
        foreach (var row in rows)
        {
            textWidth = Math.Max(textWidth, MeasureText(row, options.FontSize - 3));
        }

        var height = boxPadding + typeLineHeight + nameLineHeight;
        if (rows.Count > 0)
        {
            height += separatorGap + rows.Count * rowHeight;
        }

        height += boxPadding;

        return new(
            name,
            header,
            rows,
            fill,
            stroke,
            Math.Max(minBoxWidth, textWidth + boxPadding * 2),
            height);
    }

    static void DrawBox(SvgBuilder builder, Box box, double x, double y, RenderOptions options)
    {
        builder.AddRect(
            x,
            y,
            box.Width,
            box.Height,
            rx: 5,
            fill: box.Fill,
            stroke: box.Stroke,
            strokeWidth: 2);

        var centerX = x + box.Width / 2;
        var cursor = y + boxPadding;

        builder.AddText(
            centerX,
            cursor + typeLineHeight / 2,
            box.Header,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize - 3,
            fontFamily: options.FontFamily,
            fill: "#666");
        cursor += typeLineHeight;

        builder.AddText(
            centerX,
            cursor + nameLineHeight / 2,
            box.Name,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily,
            fontWeight: "bold",
            fill: "#333");
        cursor += nameLineHeight;

        if (box.Rows.Count == 0)
        {
            return;
        }

        cursor += separatorGap / 2;
        builder.AddLine(x, cursor, x + box.Width, cursor, stroke: box.Stroke, strokeWidth: 1);
        cursor += separatorGap / 2;

        foreach (var row in box.Rows)
        {
            builder.AddText(
                x + boxPadding,
                cursor + rowHeight / 2,
                row,
                anchor: "start",
                baseline: "middle",
                fontSize: options.FontSize - 3,
                fontFamily: options.FontFamily,
                fill: "#333");
            cursor += rowHeight;
        }
    }

    static void DrawRelation(SvgBuilder builder, NodeBox from, NodeBox to, RelationType type,
        RenderOptions options)
    {
        var angle = Math.Atan2(to.CenterY - from.CenterY, to.CenterX - from.CenterX);
        var (fromX, fromY) = ClipToBorder(from, angle);
        var (toX, toY) = ClipToBorder(to, angle + Math.PI);

        // Mermaid draws containment solid and every other relation dashed.
        var dashArray = type == RelationType.Contains ? null : "10,7";

        builder.AddLine(
            fromX,
            fromY,
            toX,
            toY,
            stroke: "#666",
            strokeWidth: 1.5,
            strokeDasharray: dashArray);

        // Draw arrowhead
        const int arrowSize = 8;
        const double arrowAngle = Math.PI / 6;
        var ax1 = toX - arrowSize * Math.Cos(angle - arrowAngle);
        var ay1 = toY - arrowSize * Math.Sin(angle - arrowAngle);
        var ax2 = toX - arrowSize * Math.Cos(angle + arrowAngle);
        var ay2 = toY - arrowSize * Math.Sin(angle + arrowAngle);

        builder.AddPath(
            string.Create(CultureInfo.InvariantCulture, $"M {toX:0.##} {toY:0.##} L {ax1:0.##} {ay1:0.##} L {ax2:0.##} {ay2:0.##} Z"),
            fill: "#666",
            stroke: "none");

        // Label, pushed off the line along its perpendicular. How far depends on which way the edge runs:
        // clearing a vertical line means moving half the label's width, a horizontal one half its height.
        var label = $"<<{type.ToString().ToLowerInvariant()}>>";
        var labelFontSize = options.FontSize - 3;
        var offset = Math.Abs(Math.Sin(angle)) * (MeasureText(label, labelFontSize) / 2 + 6) +
                     Math.Abs(Math.Cos(angle)) * (labelFontSize / 2 + 6);

        var perpX = Math.Sin(angle);
        var perpY = -Math.Cos(angle);
        if (perpY > 0)
        {
            // Keep labels on the upper side whichever way round the edge was declared.
            perpX = -perpX;
            perpY = -perpY;
        }

        builder.AddText(
            (fromX + toX) / 2 + perpX * offset,
            (fromY + toY) / 2 + perpY * offset,
            label,
            anchor: "middle",
            baseline: "middle",
            fontSize: labelFontSize,
            fontFamily: options.FontFamily,
            fill: "#666");
    }

    /// <summary>
    /// The point where a ray leaving the box centre at <paramref name="angle"/> crosses the box border.
    /// Scaling by the nearer of the two axis limits lands on the rectangle itself; scaling both by the
    /// half-extents would trace the inscribed ellipse and leave diagonal edges starting inside the box.
    /// </summary>
    static (double x, double y) ClipToBorder(NodeBox box, double angle)
    {
        var dx = Math.Cos(angle);
        var dy = Math.Sin(angle);

        var scaleX = Math.Abs(dx) < 1e-9 ? double.PositiveInfinity : box.Width / 2 / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < 1e-9 ? double.PositiveInfinity : box.Height / 2 / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);

        return (box.CenterX + dx * scale, box.CenterY + dy * scale);
    }

    static double MeasureText(string text, double fontSize, bool bold = false) =>
        text.Length * fontSize * (bold ? 0.65 : 0.55);

    readonly record struct Box(
        string Name,
        string Header,
        List<string> Rows,
        string Fill,
        string Stroke,
        double Width,
        double Height);

    readonly record struct NodeBox(double CenterX, double CenterY, double Width, double Height);
}
