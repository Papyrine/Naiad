namespace Naiad.Diagrams.Wardley;

/// <summary>
/// Draws a Wardley map as Mermaid does: a fixed canvas (900x600 unless <c>size</c> says otherwise) with the
/// evolution axis along the bottom split into stages by dashed lines, the visibility axis up the left, and
/// every component placed at its coordinates. Links, evolve trends, pipelines, annotations, notes and
/// (de)accelerators are drawn over that in Mermaid's order. Mermaid measures its labels in the DOM; here
/// they are estimated from their character counts, and the canvas grows to fit any label that would
/// otherwise run off it (Mermaid clips those).
/// </summary>
public class WardleyRenderer : IDiagramRenderer<WardleyModel>
{
    // Mermaid's wardley-beta configuration defaults.
    const double defaultWidth = 900;
    const double defaultHeight = 600;
    const double padding = 48;
    const double nodeRadius = 6;
    const double nodeLabelOffset = 8;
    const double axisFontSize = 12;
    const double labelFontSize = 10;
    const double squareSize = nodeRadius * 1.6;
    const double pipelineBoxHeight = nodeRadius * 4;
    const double strategyRadius = nodeRadius * 2;
    const double charWidthFactor = 0.55;
    const double boldCharWidthFactor = 0.6;

    // Mermaid's default-theme colours for Wardley maps.
    const string lineColor = "#333333";
    const string textColor = "#131300";
    const string fillColor = "white";
    const string evolutionColor = "#dc3545";

    static readonly string[] defaultStages = ["Genesis", "Custom Built", "Product", "Commodity"];

    public SvgDocument Render(WardleyModel model, RenderOptions options)
    {
        var width = model.Width ?? defaultWidth;
        var height = model.Height ?? defaultHeight;
        var chartWidth = width - padding * 2;
        var chartHeight = height - padding * 2;
        var fontFamily = options.FontFamily;

        var builder = new SvgBuilder();
        builder.Options(options);
        var bounds = new Bounds(0, 0, width, height);

        double ProjectX(double evolution) => padding + evolution * chartWidth;
        double ProjectY(double visibility) => height - padding - visibility * chartHeight;

        if (!string.IsNullOrEmpty(model.Title))
        {
            AddText(width / 2, padding / 2, model.Title, axisFontSize * 1.05, "middle", "middle", bold: true);
        }

        DrawAxes();
        DrawStages();

        var positions = model.Nodes.ToDictionary(static _ => _.Id, _ => (X: ProjectX(_.Evolution), Y: ProjectY(_.Visibility)));
        var nodesById = model.Nodes.ToDictionary(static _ => _.Id);

        DrawPipelines();
        DrawLinks();
        DrawTrends();
        DrawNodes();
        DrawAnnotations();
        DrawNotes();
        DrawAccelerators();

        builder.Size(width, height);
        var document = builder.Build();
        FitToContent(document, bounds, width, height);
        return document;

        void DrawAxes()
        {
            builder.AddLine(padding, height - padding, width - padding, height - padding, stroke: lineColor, strokeWidth: 1);
            builder.AddLine(padding, padding, padding, height - padding, stroke: lineColor, strokeWidth: 1);

            AddText(padding + chartWidth / 2, height - padding / 4, "Evolution", axisFontSize, "middle", bold: true);

            var labelX = padding / 3;
            var labelY = padding + chartHeight / 2;
            builder.AddText(
                labelX,
                labelY,
                "Visibility",
                anchor: "middle",
                fontSize: axisFontSize,
                fontFamily: fontFamily,
                fontWeight: "bold",
                fill: textColor,
                transform: Invariant($"rotate(-90 {labelX:0.##} {labelY:0.##})"));
        }

        // The stages share the axis equally unless every one of them says where it ends.
        void DrawStages()
        {
            var stages = model.Stages.Count > 0 ? model.Stages : [.. defaultStages];
            var boundaries = model.StageBoundaries.Count == stages.Count ? model.StageBoundaries : null;

            var start = 0.0;
            for (var i = 0; i < stages.Count; i++)
            {
                var end = boundaries?[i] ?? (i + 1.0) / stages.Count;
                var startX = padding + start * chartWidth;
                var endX = padding + end * chartWidth;

                if (i > 0)
                {
                    builder.AddPath(
                        Invariant($"M {startX:0.##} {padding:0.##} L {startX:0.##} {height - padding:0.##}"),
                        stroke: "#000",
                        strokeWidth: 1,
                        strokeDasharray: "5 5",
                        opacity: 0.8);
                }

                AddText((startX + endX) / 2, height - padding / 1.5, stages[i], axisFontSize - 2, "middle");
                start = end;
            }
        }

        // A pipeline is a rounded box around its components, joined in order of evolution by dashed lines,
        // with its parent moved to sit centred on the box's top edge.
        void DrawPipelines()
        {
            foreach (var pipeline in model.Pipelines)
            {
                var members = pipeline.ComponentIds
                    .Where(positions.ContainsKey)
                    .Select(_ => positions[_])
                    .ToList();
                if (members.Count == 0)
                {
                    continue;
                }

                var ordered = pipeline.ComponentIds
                    .Where(nodesById.ContainsKey)
                    .Select(_ => nodesById[_])
                    .OrderBy(static _ => _.Evolution)
                    .Select(_ => positions[_.Id])
                    .ToList();
                for (var i = 0; i < ordered.Count - 1; i++)
                {
                    builder.AddLine(ordered[i].X, ordered[i].Y, ordered[i + 1].X, ordered[i + 1].Y, stroke: lineColor, strokeWidth: 1, strokeDasharray: "4 4");
                }

                const double boxPadding = 15;
                var minX = members.Min(static _ => _.X);
                var maxX = members.Max(static _ => _.X);
                var boxTop = members[^1].Y - pipelineBoxHeight / 2;

                if (positions.ContainsKey(pipeline.ParentId))
                {
                    positions[pipeline.ParentId] = ((minX + maxX) / 2, boxTop - squareSize / 6);
                }

                builder.AddRect(minX - boxPadding, boxTop, maxX - minX + boxPadding * 2, pipelineBoxHeight, rx: 4, fill: "none", stroke: lineColor, strokeWidth: 1.5);
                bounds.Include(minX - boxPadding, boxTop, maxX + boxPadding, boxTop + pipelineBoxHeight);
            }
        }

        void DrawLinks()
        {
            var pipelineMembers = model.Pipelines.ToDictionary(static _ => _.ParentId, static _ => _.ComponentIds.ToHashSet());

            // A link from a pipeline component up to its own parent is implied by the pipeline, so Mermaid
            // leaves it out, and so does a link to or from a node that does not exist.
            var links = model.Links
                .Where(_ => positions.ContainsKey(_.Source) &&
                            positions.ContainsKey(_.Target) &&
                            !(pipelineMembers.TryGetValue(_.Target, out var members) && members.Contains(_.Source)))
                .ToList();

            var labelled = new List<(WardleyLink Link, double DeltaX, double DeltaY, double Distance)>();
            foreach (var link in links)
            {
                var source = positions[link.Source];
                var target = positions[link.Target];
                var deltaX = target.X - source.X;
                var deltaY = target.Y - source.Y;
                var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                if (distance < 0.001)
                {
                    continue;
                }

                var unitX = deltaX / distance;
                var unitY = deltaY / distance;
                var sourceRadius = NodeRadius(link.Source);
                var targetRadius = NodeRadius(link.Target);
                var x1 = source.X + unitX * sourceRadius;
                var y1 = source.Y + unitY * sourceRadius;
                var x2 = target.X - unitX * targetRadius;
                var y2 = target.Y - unitY * targetRadius;

                builder.AddLine(x1, y1, x2, y2, stroke: lineColor, strokeWidth: 1, strokeDasharray: link.Dashed ? "6 6" : null);

                if (link.Flow is WardleyFlow.Forward or WardleyFlow.Bidirectional)
                {
                    AddArrowhead(x2, y2, unitX, unitY, 5, lineColor);
                }

                if (link.Flow is WardleyFlow.Backward or WardleyFlow.Bidirectional)
                {
                    AddArrowhead(x1, y1, -unitX, -unitY, 5, lineColor);
                }

                if (!string.IsNullOrEmpty(link.Label))
                {
                    labelled.Add((link, deltaX, deltaY, distance));
                }
            }

            // Labels go after every line, centred a little above the middle of their link and turned to run
            // along it, never upside down.
            foreach (var (link, deltaX, deltaY, distance) in labelled)
            {
                const double offset = 8;
                var source = positions[link.Source];
                var target = positions[link.Target];
                var labelX = (source.X + target.X) / 2 + deltaY / distance * offset;
                var labelY = (source.Y + target.Y) / 2 - deltaX / distance * offset;
                var angle = Math.Atan2(deltaY, deltaX) * 180 / Math.PI;
                if (angle is > 90 or < -90)
                {
                    angle += 180;
                }

                builder.AddText(
                    labelX,
                    labelY,
                    link.Label!,
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: labelFontSize,
                    fontFamily: fontFamily,
                    fill: textColor,
                    transform: Invariant($"rotate({angle:0.##} {labelX:0.##} {labelY:0.##})"));

                var halfWidth = TextWidth(link.Label!, labelFontSize, false) / 2;
                bounds.Include(labelX - halfWidth, labelY - halfWidth, labelX + halfWidth, labelY + halfWidth);
            }
        }

        // An evolve is a red dashed arrow across to the target evolution, stopping just short of where the
        // node would be.
        void DrawTrends()
        {
            foreach (var trend in model.Trends)
            {
                if (!positions.TryGetValue(trend.NodeId, out var origin))
                {
                    continue;
                }

                // Across at the height the node is drawn at, which for a pipeline's parent is the top of its
                // box rather than its own visibility.
                var targetX = ProjectX(trend.TargetEvolution);
                var targetY = origin.Y;
                var deltaX = targetX - origin.X;
                var deltaY = targetY - origin.Y;
                var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                const double shortenBy = nodeRadius + 2;
                if (distance <= shortenBy)
                {
                    continue;
                }

                var unitX = deltaX / distance;
                var unitY = deltaY / distance;
                var endX = targetX - unitX * shortenBy;
                var endY = targetY - unitY * shortenBy;
                builder.AddLine(origin.X, origin.Y, endX, endY, stroke: evolutionColor, strokeWidth: 1, strokeDasharray: "4 4");
                AddArrowhead(endX, endY, unitX, unitY, 6, evolutionColor);
            }
        }

        void DrawNodes()
        {
            foreach (var node in model.Nodes)
            {
                var (nodeX, nodeY) = positions[node.Id];
                var isAnchor = node.Kind == WardleyNodeKind.Anchor;

                // The sourcing strategy is a larger circle behind the node: light for build, grey for buy,
                // dark for outsource, and for market a white one holding three linked dots.
                switch (node.SourceStrategy)
                {
                    case WardleySourceStrategy.Outsource:
                        builder.AddCircle(nodeX, nodeY, strategyRadius, fill: "#666", stroke: lineColor, strokeWidth: 1);
                        break;
                    case WardleySourceStrategy.Buy:
                        builder.AddCircle(nodeX, nodeY, strategyRadius, fill: "#ccc", stroke: lineColor, strokeWidth: 1);
                        break;
                    case WardleySourceStrategy.Build:
                        builder.AddCircle(nodeX, nodeY, strategyRadius, fill: "#eee", stroke: "#000", strokeWidth: 1);
                        break;
                    case WardleySourceStrategy.Market:
                        builder.AddCircle(nodeX, nodeY, strategyRadius, fill: fillColor, stroke: lineColor, strokeWidth: 1);
                        break;
                }

                if (node.SourceStrategy is not null)
                {
                    bounds.Include(nodeX - strategyRadius, nodeY - strategyRadius, nodeX + strategyRadius, nodeY + strategyRadius);
                }

                if (!node.IsPipelineParent &&
                    node.SourceStrategy != WardleySourceStrategy.Market &&
                    !isAnchor)
                {
                    builder.AddCircle(nodeX, nodeY, nodeRadius, fill: fillColor, stroke: lineColor, strokeWidth: 1);
                }

                if (node.SourceStrategy == WardleySourceStrategy.Market)
                {
                    DrawMarket(nodeX, nodeY);
                }

                if (node.IsPipelineParent)
                {
                    builder.AddRect(nodeX - squareSize / 2, nodeY - squareSize / 2, squareSize, squareSize, fill: fillColor, stroke: lineColor, strokeWidth: 1);
                }

                // Inertia is a thick bar to the right of the node, clear of any strategy circle.
                if (node.Inertia)
                {
                    var offset = node.IsPipelineParent ? squareSize / 2 + 15 : nodeRadius + 15;
                    if (node.SourceStrategy is not null)
                    {
                        offset += nodeRadius + 10;
                    }

                    var barHeight = node.IsPipelineParent ? squareSize : nodeRadius * 2;
                    builder.AddLine(nodeX + offset, nodeY - barHeight / 2, nodeX + offset, nodeY + barHeight / 2, stroke: lineColor, strokeWidth: 6);
                    bounds.Include(nodeX + offset - 3, nodeY - barHeight / 2, nodeX + offset + 3, nodeY + barHeight / 2);
                }
            }

            // Anchors are labelled in bold, centred on their position; everything else above and to the right
            // of its node, further out when a strategy circle surrounds it.
            foreach (var node in model.Nodes)
            {
                var (nodeX, nodeY) = positions[node.Id];
                if (node.Kind == WardleyNodeKind.Anchor)
                {
                    AddText(nodeX + (node.LabelOffsetX ?? 0), nodeY + (node.LabelOffsetY ?? -3), node.Label, labelFontSize, "middle", "middle", bold: true, fill: "#000");
                    continue;
                }

                // Mermaid puts a pipeline component's label where any other component's goes, which is
                // across the top edge of the pipeline's box. It is centred below the box instead.
                if (node is
                    {
                        Kind: WardleyNodeKind.PipelineComponent,
                        LabelOffsetX: null,
                        LabelOffsetY: null
                    })
                {
                    AddText(nodeX, nodeY + pipelineBoxHeight / 2 + labelFontSize + 2, node.Label, labelFontSize, "middle");
                    continue;
                }

                var strategyGap = node.SourceStrategy is null ? 0 : 10;
                var labelX = nodeX + (node.LabelOffsetX ?? nodeLabelOffset + strategyGap);
                var labelY = nodeY + (node.LabelOffsetY ?? -nodeLabelOffset - strategyGap);
                AddText(labelX, labelY, node.Label, labelFontSize, "start");
            }
        }

        void DrawMarket(double nodeX, double nodeY)
        {
            const double dotRadius = nodeRadius * 0.7;
            const double triangleRadius = nodeRadius * 1.2;
            var cornerX = triangleRadius * Math.Cos(Math.PI / 6);
            var cornerY = triangleRadius * Math.Sin(Math.PI / 6);
            (double X, double Y)[] corners =
            [
                (nodeX, nodeY - triangleRadius),
                (nodeX - cornerX, nodeY + cornerY),
                (nodeX + cornerX, nodeY + cornerY)
            ];

            for (var i = 0; i < corners.Length; i++)
            {
                var next = corners[(i + 1) % corners.Length];
                builder.AddLine(corners[i].X, corners[i].Y, next.X, next.Y, stroke: lineColor, strokeWidth: 1);
            }

            foreach (var (cornerCenterX, cornerCenterY) in corners)
            {
                builder.AddCircle(cornerCenterX, cornerCenterY, dotRadius, fill: fillColor, stroke: lineColor, strokeWidth: 2);
            }
        }

        // Each annotation is a numbered circle; the box, when placed, lists their texts in number order and
        // is kept inside the chart.
        void DrawAnnotations()
        {
            foreach (var annotation in model.Annotations)
            {
                var annotationX = ProjectX(annotation.Evolution);
                var annotationY = ProjectY(annotation.Visibility);
                builder.AddCircle(annotationX, annotationY, 10, fill: fillColor, stroke: lineColor, strokeWidth: 1.5);
                builder.AddText(
                    annotationX,
                    annotationY,
                    annotation.Number.ToString(CultureInfo.InvariantCulture),
                    anchor: "middle",
                    baseline: "central",
                    fontSize: 10,
                    fontFamily: fontFamily,
                    fontWeight: "bold",
                    fill: textColor);
                bounds.Include(annotationX - 10, annotationY - 10, annotationX + 10, annotationY + 10);
            }

            if (model.AnnotationsBox is not { } box)
            {
                return;
            }

            var lines = model.Annotations
                .Where(static _ => !string.IsNullOrEmpty(_.Text))
                .OrderBy(static _ => _.Number)
                .Select(static _ => $"{_.Number}. {_.Text}")
                .ToList();
            if (lines.Count == 0)
            {
                return;
            }

            const double boxPadding = 10;
            const double lineHeight = 16;
            const double fontSize = 11;

            // Mermaid pads its measured text by a further 105px; the estimate here only needs a little slack.
            var boxWidth = lines.Max(_ => TextWidth(_, fontSize, false)) + boxPadding * 2 + 10;
            var boxHeight = lines.Count * lineHeight + boxPadding * 2 + fontSize * 1.2 / 2;
            var boxX = Math.Max(padding, Math.Min(ProjectX(box.Evolution), width - padding - boxWidth));
            var boxY = Math.Max(padding, Math.Min(ProjectY(box.Visibility), height - padding - boxHeight));

            builder.AddRect(boxX, boxY, boxWidth, boxHeight, rx: 4, fill: fillColor, stroke: lineColor, strokeWidth: 1.5);
            bounds.Include(boxX, boxY, boxX + boxWidth, boxY + boxHeight);
            for (var i = 0; i < lines.Count; i++)
            {
                AddText(boxX + boxPadding, boxY + boxPadding + (i + 1) * lineHeight, lines[i], fontSize, "start", "middle");
            }
        }

        void DrawNotes()
        {
            foreach (var note in model.Notes)
            {
                AddText(ProjectX(note.Evolution), ProjectY(note.Visibility), note.Text, 11, "start", bold: true);
            }
        }

        // An accelerator is a broad arrow pointing right from its position, a deaccelerator one pointing
        // left from the far end, each named in bold below it.
        void DrawAccelerators()
        {
            const double arrowWidth = 60;
            const double arrowHeight = 30;
            const double headWidth = 20;
            const double headFlare = 8;

            foreach (var accelerator in model.Accelerators)
            {
                var left = ProjectX(accelerator.Evolution);
                var middle = ProjectY(accelerator.Visibility);
                var top = middle - arrowHeight / 2;
                var bottom = middle + arrowHeight / 2;
                var neck = left + arrowWidth - headWidth;
                DrawForce(
                    Invariant($"M {left:0.##} {top:0.##} L {neck:0.##} {top:0.##} L {neck:0.##} {top - headFlare:0.##} L {left + arrowWidth:0.##} {middle:0.##} L {neck:0.##} {bottom + headFlare:0.##} L {neck:0.##} {bottom:0.##} L {left:0.##} {bottom:0.##} Z"),
                    left,
                    middle,
                    accelerator.Name);
            }

            foreach (var deaccelerator in model.Deaccelerators)
            {
                var left = ProjectX(deaccelerator.Evolution);
                var middle = ProjectY(deaccelerator.Visibility);
                var top = middle - arrowHeight / 2;
                var bottom = middle + arrowHeight / 2;
                var neck = left + headWidth;
                DrawForce(
                    Invariant($"M {left + arrowWidth:0.##} {top:0.##} L {neck:0.##} {top:0.##} L {neck:0.##} {top - headFlare:0.##} L {left:0.##} {middle:0.##} L {neck:0.##} {bottom + headFlare:0.##} L {neck:0.##} {bottom:0.##} L {left + arrowWidth:0.##} {bottom:0.##} Z"),
                    left,
                    middle,
                    deaccelerator.Name);
            }

            void DrawForce(string path, double left, double middle, string name)
            {
                builder.AddPath(path, fill: fillColor, stroke: lineColor, strokeWidth: 1);
                bounds.Include(left, middle - arrowHeight / 2 - headFlare, left + arrowWidth, middle + arrowHeight / 2 + headFlare);
                AddText(left + arrowWidth / 2, middle + arrowHeight / 2 + 15, name, 10, "middle", bold: true);
            }
        }

        double NodeRadius(string id) =>
            nodesById[id].IsPipelineParent ? squareSize / Math.Sqrt(2) : nodeRadius;

        // A filled triangle with its tip at the given point, pointing along the given unit direction.
        void AddArrowhead(double tipX, double tipY, double unitX, double unitY, double size, string color)
        {
            var baseX = tipX - unitX * size;
            var baseY = tipY - unitY * size;
            var halfWidth = size / 2;
            builder.AddPath(
                Invariant($"M {tipX:0.##} {tipY:0.##} L {baseX - unitY * halfWidth:0.##} {baseY + unitX * halfWidth:0.##} L {baseX + unitY * halfWidth:0.##} {baseY - unitX * halfWidth:0.##} Z"),
                fill: color,
                stroke: "none");
        }

        // Adds a line of text and widens the content bounds by its estimated extent.
        void AddText(double textX, double textY, string text, double fontSize, string anchor, string? baseline = null, bool bold = false, string fill = textColor)
        {
            builder.AddText(
                textX,
                textY,
                text,
                anchor: anchor,
                baseline: baseline,
                fontSize: fontSize,
                fontFamily: fontFamily,
                fontWeight: bold ? "bold" : null,
                fill: fill);

            var textWidth = TextWidth(text, fontSize, bold);
            var left = anchor switch
            {
                "middle" => textX - textWidth / 2,
                "end" => textX - textWidth,
                _ => textX
            };

            // Without a baseline the text sits on an alphabetic baseline, so most of it is above the y.
            var top = baseline is "middle" or "central" ? textY - fontSize / 2 : textY - fontSize * 0.8;
            bounds.Include(left, top, left + textWidth, top + fontSize);
        }
    }

    static double TextWidth(string text, double fontSize, bool bold) =>
        text.Length * fontSize * (bold ? boldCharWidthFactor : charWidthFactor);

    static string Invariant(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Mermaid draws on a fixed canvas and clips whatever runs past it. Here the canvas grows to take in
    /// anything that does, shifting the drawing right or down when it runs off the left or top.
    /// </summary>
    static void FitToContent(SvgDocument document, Bounds bounds, double width, double height)
    {
        const double margin = 4;
        var minX = bounds.MinX < 0 ? bounds.MinX - margin : 0;
        var minY = bounds.MinY < 0 ? bounds.MinY - margin : 0;
        var maxX = bounds.MaxX > width ? bounds.MaxX + margin : width;
        var maxY = bounds.MaxY > height ? bounds.MaxY + margin : height;

        document.Width = maxX - minX;
        document.Height = maxY - minY;

        if (minX < 0 || minY < 0)
        {
            var group = new SvgGroup
            {
                Transform = Invariant($"translate({-minX:0.##},{-minY:0.##})")
            };
            group.Children.AddRange(document.Elements);
            document.Elements.Clear();
            document.Elements.Add(group);
        }
    }

    sealed class Bounds(double minX, double minY, double maxX, double maxY)
    {
        public double MinX { get; private set; } = minX;
        public double MinY { get; private set; } = minY;
        public double MaxX { get; private set; } = maxX;
        public double MaxY { get; private set; } = maxY;

        public void Include(double left, double top, double right, double bottom)
        {
            MinX = Math.Min(MinX, left);
            MinY = Math.Min(MinY, top);
            MaxX = Math.Max(MaxX, right);
            MaxY = Math.Max(MaxY, bottom);
        }
    }
}
