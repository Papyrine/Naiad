namespace Naiad.Diagrams.Cynefin;

/// <summary>
/// Draws the Cynefin framework as Mermaid does: four quadrants (Complex top-left, Complicated top-right,
/// Chaotic bottom-left, Clear bottom-right) split by wavy dashed boundaries, a dark red "cliff" between
/// Chaotic and Clear, a dashed Confusion ellipse in the centre, each domain's name, decision model and
/// practice, its items as badges, and transitions as curved arrows between domain centres.
/// Mermaid draws at a fixed 800 by 600 and lets long item lists spill out of their quadrant; here the
/// canvas grows from that size until every badge fits its region and clears the Confusion ellipse, and
/// the ellipse grows to hold its own items. Text is measured from character counts, not the DOM.
/// </summary>
public class CynefinRenderer : IDiagramRenderer<CynefinModel>
{
    const double baseWidth = 800;
    const double baseHeight = 600;
    const double padding = 40;
    const double boundaryAmplitude = 8;

    // Mermaid hashes the SVG element's id for the waviness, which differs between renders. A fixed seed
    // keeps the output stable, as Mermaid's own seed option does.
    const int seed = 7;

    const double domainFontSize = 16;
    const double itemFontSize = 12;
    const double subtitleFontSize = itemFontSize - 1;
    const double titleFontSize = domainFontSize + 2;
    const double charWidthFactor = 0.55;

    const double itemHeight = 26;
    const double itemPitch = itemHeight + 4;
    const double itemPaddingX = 10;
    const int maxConfusionItems = 3;

    // Room left between a badge and a boundary: the boundaries wander up to their amplitude either side
    // of the centre line, and their control points half as far again.
    const double boundaryClearance = boundaryAmplitude * 2;
    const double edgeClearance = 8;
    const double ellipseClearance = 6;

    const string boundaryColor = "#333333";
    const string cliffColor = "#8B0000";
    const string arrowColor = "#333333";
    const string textColor = "#333";
    const string labelColor = "#131300";

    static readonly CynefinDomainName[] quadrantDomains =
    [
        CynefinDomainName.Complex,
        CynefinDomainName.Complicated,
        CynefinDomainName.Chaotic,
        CynefinDomainName.Clear
    ];

    static string Background(CynefinDomainName name) =>
        name switch
        {
            CynefinDomainName.Complex => "#E8F5E9",
            CynefinDomainName.Complicated => "#E3F2FD",
            CynefinDomainName.Chaotic => "#FBE9E7",
            CynefinDomainName.Clear => "#FFF8E1",
            _ => "#F3E5F5"
        };

    static (string model, string practice) Meta(CynefinDomainName name) =>
        name switch
        {
            CynefinDomainName.Complex => ("Probe → Sense → Respond", "Emergent Practices"),
            CynefinDomainName.Complicated => ("Sense → Analyse → Respond", "Good Practices"),
            CynefinDomainName.Clear => ("Sense → Categorise → Respond", "Best Practices"),
            CynefinDomainName.Chaotic => ("Act → Sense → Respond", "Novel Practices"),
            _ => ("", "Disorder")
        };

    public SvgDocument Render(CynefinModel model, RenderOptions options)
    {
        var layout = Fit(model);
        var width = layout.Width;
        var height = layout.Height;

        var builder = new SvgBuilder();
        builder.Options(options);
        builder.Size(width + padding * 2, height + padding * 2);
        builder.BeginGroup(transform: string.Create(CultureInfo.InvariantCulture, $"translate({padding}, {padding})"));

        foreach (var name in quadrantDomains)
        {
            var region = layout.Regions[name];
            builder.AddRect(region.X, region.Y, region.Width, region.Height, fill: Background(name), style: "fill-opacity: 0.4");
        }

        builder.AddPath(FoldPath(width, height), fill: "none", stroke: boundaryColor, strokeWidth: 2, strokeDasharray: "6 3");
        builder.AddPath(HorizontalBoundary(width, height), fill: "none", stroke: boundaryColor, strokeWidth: 2, strokeDasharray: "6 3");
        builder.AddPath(CliffPath(width, height), fill: "none", stroke: cliffColor, strokeWidth: 4);

        // Mermaid gives the ellipse a fill opacity; it has no stroke of its own to fade, so the fill is
        // drawn with an opacity and the dashed outline over it separately.
        var ellipse = EllipsePath(width / 2, height / 2, layout.EllipseRadiusX, layout.EllipseRadiusY);
        builder.AddPath(ellipse, fill: Background(CynefinDomainName.Confusion), opacity: 0.5);
        builder.AddPath(ellipse, fill: "none", stroke: boundaryColor, strokeWidth: 1.5, strokeDasharray: "4 2");

        foreach (var text in layout.Texts)
        {
            builder.AddText(
                text.X,
                text.Y,
                text.Content,
                anchor: "middle",
                baseline: "middle",
                fontSize: text.FontSize,
                fontFamily: options.FontFamily,
                fontWeight: text.Bold ? "bold" : null,
                fill: text.Bold ? labelColor : textColor,
                style: text.Italic ? "font-style: italic" : null);
        }

        foreach (var badge in layout.Badges)
        {
            builder.AddRect(
                badge.X,
                badge.Y,
                badge.Width,
                itemHeight,
                rx: 4,
                fill: Background(badge.Domain),
                stroke: boundaryColor,
                strokeWidth: 1,
                style: badge.Overflow ? "fill-opacity: 0.6; stroke-dasharray: 3 2" : "fill-opacity: 0.95");
            builder.AddText(
                badge.X + badge.Width / 2,
                badge.Y + itemHeight / 2,
                badge.Text,
                anchor: "middle",
                baseline: "central",
                fontSize: itemFontSize,
                fontFamily: options.FontFamily,
                fill: textColor);
        }

        foreach (var transition in model.Transitions)
        {
            DrawTransition(builder, layout, transition, options);
        }

        builder.EndGroup();

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                padding + width / 2,
                padding / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: titleFontSize,
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: labelColor);
        }

        return builder.Build();
    }

    static double TextWidth(string text, double fontSize) => text.Length * fontSize * charWidthFactor;

    static double BadgeWidth(string text) => TextWidth(text, itemFontSize) + itemPaddingX * 2;

    /// <summary>
    /// Lays the diagram out at Mermaid's size, then grows the canvas a step at a time in whichever
    /// direction is short until every badge fits its quadrant and clears the Confusion ellipse.
    /// </summary>
    static Layout Fit(CynefinModel model)
    {
        var width = baseWidth;
        var height = baseHeight;
        var layout = Arrange(model, width, height);
        for (var step = 0; step < 40 && (layout.NeedsWidth || layout.NeedsHeight); step++)
        {
            width *= layout.NeedsWidth ? 1.1 : 1;
            height *= layout.NeedsHeight ? 1.1 : 1;
            layout = Arrange(model, width, height);
        }

        return layout;
    }

    static Layout Arrange(CynefinModel model, double width, double height)
    {
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        var layout = new Layout(width, height);

        foreach (var name in quadrantDomains)
        {
            var left = name is CynefinDomainName.Complex or CynefinDomainName.Chaotic ? 0 : halfWidth;
            var top = name is CynefinDomainName.Complex or CynefinDomainName.Complicated ? 0 : halfHeight;
            layout.Regions[name] = new(left, top, halfWidth, halfHeight);
        }

        var cliffReach = width * 0.03 * 1.5;
        foreach (var name in quadrantDomains)
        {
            var region = layout.Regions[name];
            var centerX = region.X + region.Width / 2;
            var centerY = region.Y + region.Height / 2;
            var (decisionModel, practice) = Meta(name);
            var isTop = region.Y == 0;

            var texts = new List<Text>
            {
                new(name, centerX, centerY - 30, name.ToString(), domainFontSize, Bold: true, Italic: false),
                new(name, centerX, centerY - 10, decisionModel, subtitleFontSize, Bold: false, Italic: true),
                new(name, centerX, centerY + 5, practice, subtitleFontSize, Bold: false, Italic: true)
            };
            layout.Texts.AddRange(texts);

            var items = model.Domains.TryGetValue(name, out var domain) ? domain.Items : [];
            var badges = items
                .Select((_, index) => Badge.Centered(name, _, centerX, centerY + 25 + index * itemPitch, Overflow: false))
                .ToList();
            layout.Badges.AddRange(badges);

            var content = texts.Select(static _ => _.Bounds).Concat(badges.Select(static _ => _.Bounds)).ToList();
            layout.ContentBounds[name] = Rect.Union(content);

            // The fold runs down the inner edge of each quadrant; the cliff also swings out into the
            // bottom two.
            var innerClearance = isTop ? boundaryClearance : Math.Max(boundaryClearance, cliffReach + edgeClearance);
            var widest = content.Max(static _ => _.Width);
            if (widest / 2 > region.Width / 2 - Math.Max(innerClearance, edgeClearance))
            {
                layout.NeedsWidth = true;
            }

            var bottomLimit = isTop ? region.Y + region.Height - boundaryClearance : region.Y + region.Height - edgeClearance;
            if (badges.Count > 0 &&
                badges[^1].Bounds.Bottom > bottomLimit)
            {
                layout.NeedsHeight = true;
            }
        }

        ArrangeConfusion(model, layout);

        // A quadrant's text or badges running under the ellipse needs more room all round.
        var overlaps = layout.Texts.Where(static _ => _.Domain != CynefinDomainName.Confusion).Select(static _ => _.Bounds)
            .Concat(layout.Badges.Where(static _ => _.Domain != CynefinDomainName.Confusion).Select(static _ => _.Bounds))
            .Any(_ => IntersectsEllipse(_.Inflate(ellipseClearance), halfWidth, halfHeight, layout.EllipseRadiusX, layout.EllipseRadiusY));
        if (overlaps)
        {
            layout.NeedsWidth = true;
            layout.NeedsHeight = true;
        }

        return layout;
    }

    /// <summary>
    /// Places the Confusion label, subtitle and up to three items (and a "+N more" badge for the rest), as
    /// Mermaid does, and sizes the ellipse around them. Mermaid hangs the items below the label whatever
    /// their number, so they run out of the ellipse; here the block is centred and the ellipse grown
    /// from its usual 15% of the canvas until it holds the block.
    /// </summary>
    static void ArrangeConfusion(CynefinModel model, Layout layout)
    {
        var centerX = layout.Width / 2;
        var centerY = layout.Height / 2;
        var items = model.Domains.TryGetValue(CynefinDomainName.Confusion, out var domain) ? domain.Items : [];
        var shown = items.Take(maxConfusionItems).ToList();
        var overflow = items.Count - shown.Count;
        var rows = shown.Count + (overflow > 0 ? 1 : 0);

        // The block runs from the top of the label, 20 above the centre, to the bottom of the last badge.
        var shift = rows > 0 ? (30 * rows - 2) / 2.0 : 0;
        var texts = new List<Text>
        {
            new(CynefinDomainName.Confusion, centerX, centerY - 10 - shift, "Confusion", domainFontSize, Bold: true, Italic: false),
            new(CynefinDomainName.Confusion, centerX, centerY + 8 - shift, Meta(CynefinDomainName.Confusion).practice, subtitleFontSize, Bold: false, Italic: true)
        };
        layout.Texts.AddRange(texts);

        var startY = centerY + 22 - shift;
        var badges = shown
            .Select((_, index) => Badge.Centered(CynefinDomainName.Confusion, _, centerX, startY + index * itemPitch, Overflow: false))
            .ToList();
        if (overflow > 0)
        {
            badges.Add(Badge.Centered(CynefinDomainName.Confusion, $"+{overflow} more", centerX, startY + shown.Count * itemPitch, Overflow: true));
        }

        layout.Badges.AddRange(badges);

        var block = Rect.Union(texts.Select(static _ => _.Bounds).Concat(badges.Select(static _ => _.Bounds)).ToList());
        var halfBlockWidth = Math.Max(centerX - block.X, block.Right - centerX) + ellipseClearance;
        var halfBlockHeight = Math.Max(centerY - block.Y, block.Bottom - centerY) + ellipseClearance;

        // A box fits in an ellipse when its corner does; growing both radii by the same factor keeps
        // Mermaid's proportions.
        var radiusX = layout.Width * 0.15;
        var radiusY = layout.Height * 0.15;
        var scale = Math.Sqrt(Math.Pow(halfBlockWidth / radiusX, 2) + Math.Pow(halfBlockHeight / radiusY, 2));
        scale = Math.Max(1, scale);
        layout.EllipseRadiusX = radiusX * scale;
        layout.EllipseRadiusY = radiusY * scale;
        layout.ContentBounds[CynefinDomainName.Confusion] = new(
            centerX - layout.EllipseRadiusX,
            centerY - layout.EllipseRadiusY,
            layout.EllipseRadiusX * 2,
            layout.EllipseRadiusY * 2);
    }

    /// <summary>
    /// Scaling the axes turns the ellipse into a circle and the box into another box, so the point of the
    /// box nearest the centre is the clamped centre in either space.
    /// </summary>
    static bool IntersectsEllipse(Rect rect, double centerX, double centerY, double radiusX, double radiusY)
    {
        var nearestX = Math.Clamp(centerX, rect.X, rect.Right);
        var nearestY = Math.Clamp(centerY, rect.Y, rect.Bottom);
        var normalX = (nearestX - centerX) / radiusX;
        var normalY = (nearestY - centerY) / radiusY;
        return normalX * normalX + normalY * normalY < 1;
    }

    /// <summary>
    /// Mermaid draws a quadratic curve between the domain centres, bowed to one side by 15% of its length,
    /// with the label above its control point. Running centre to centre puts the line and its arrowhead
    /// over the domains' own text, so here the same curve is cut back to where it leaves the text and
    /// badges of the domain it starts from and meets those of the domain it points at (the ellipse, for
    /// Confusion).
    /// </summary>
    static void DrawTransition(SvgBuilder builder, Layout layout, CynefinTransition transition, RenderOptions options)
    {
        var (startX, startY) = layout.Center(transition.From);
        var (endX, endY) = layout.Center(transition.To);
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length == 0)
        {
            return;
        }

        // The bow is 15% of the length, perpendicular to the line between the centres.
        var controlX = (startX + endX) / 2 - deltaY * 0.15;
        var controlY = (startY + endY) / 2 + deltaX * 0.15;

        const int samples = 200;
        var startT = 0.0;
        for (var i = 0; i <= samples / 2; i++)
        {
            startT = (double) i / samples;
            var (pointX, pointY) = Quadratic(startX, startY, controlX, controlY, endX, endY, startT);
            if (!IsInside(layout, transition.From, pointX, pointY))
            {
                break;
            }
        }

        var endT = 1.0;
        for (var i = samples; i >= samples / 2; i--)
        {
            endT = (double) i / samples;
            var (pointX, pointY) = Quadratic(startX, startY, controlX, controlY, endX, endY, endT);
            if (!IsInside(layout, transition.To, pointX, pointY))
            {
                break;
            }
        }

        startT = Math.Min(startT, 0.4);
        endT = Math.Max(endT, 0.6);

        // The piece of a quadratic between two parameters is itself a quadratic, with its ends on the curve
        // and its control point given by the curve's blossom at the two parameters.
        var (fromX, fromY) = Quadratic(startX, startY, controlX, controlY, endX, endY, startT);
        var (toX, toY) = Quadratic(startX, startY, controlX, controlY, endX, endY, endT);
        var middleWeight = (1 - startT) * endT + startT * (1 - endT);
        var pieceControlX = (1 - startT) * (1 - endT) * startX + middleWeight * controlX + startT * endT * endX;
        var pieceControlY = (1 - startT) * (1 - endT) * startY + middleWeight * controlY + startT * endT * endY;

        builder.AddPath(
            string.Create(
                CultureInfo.InvariantCulture,
                $"M{fromX:0.##},{fromY:0.##} Q{pieceControlX:0.##},{pieceControlY:0.##} {toX:0.##},{toY:0.##}"),
            fill: "none",
            stroke: arrowColor,
            strokeWidth: 2);
        AddArrowhead(builder, pieceControlX, pieceControlY, toX, toY);

        if (!string.IsNullOrEmpty(transition.Label))
        {
            AddTransitionLabel(builder, layout, transition.Label, startX, startY, controlX, controlY, endX, endY, options);
        }
    }

    /// <summary>
    /// Mermaid sets the label above the curve's control point, which lies beyond the curve on the side it
    /// bows towards: for the transitions between neighbouring quadrants that is the middle of the diagram,
    /// where the label lands on the Confusion ellipse or a boundary. Here it sits beside the curve's apex
    /// on the side away from the middle, on a pale backing so a boundary running under it stays readable.
    /// </summary>
    static void AddTransitionLabel(
        SvgBuilder builder,
        Layout layout,
        string label,
        double startX,
        double startY,
        double controlX,
        double controlY,
        double endX,
        double endY,
        RenderOptions options)
    {
        var (apexX, apexY) = Quadratic(startX, startY, controlX, controlY, endX, endY, 0.5);

        // The label moves off the curve square to the line between its ends, to whichever side faces away
        // from the middle. A curve through the middle has no outside, and takes the side it bows towards.
        var chordX = endX - startX;
        var chordY = endY - startY;
        var chordLength = Math.Sqrt(chordX * chordX + chordY * chordY);
        var normalX = -chordY / chordLength;
        var normalY = chordX / chordLength;
        var outward = normalX * (apexX - layout.Width / 2) + normalY * (apexY - layout.Height / 2);
        if (Math.Abs(outward) < 1)
        {
            outward = normalX * (controlX - apexX) + normalY * (controlY - apexY);
        }

        if (outward < 0)
        {
            normalX = -normalX;
            normalY = -normalY;
        }

        var width = TextWidth(label, subtitleFontSize);
        var height = subtitleFontSize * 1.4;
        const double gap = 6;
        var distance = gap + Math.Abs(normalX) * (width / 2 + 3) + Math.Abs(normalY) * height / 2;
        var centerX = apexX + normalX * distance;
        var centerY = apexY + normalY * distance;

        builder.AddRect(
            centerX - width / 2 - 3,
            centerY - height / 2,
            width + 6,
            height,
            rx: 3,
            fill: "#FFFFFF",
            style: "fill-opacity: 0.8");
        builder.AddText(
            centerX,
            centerY,
            label,
            anchor: "middle",
            baseline: "middle",
            fontSize: subtitleFontSize,
            fontFamily: options.FontFamily,
            fill: textColor);
    }

    static bool IsInside(Layout layout, CynefinDomainName name, double pointX, double pointY)
    {
        if (name == CynefinDomainName.Confusion)
        {
            var normalX = (pointX - layout.Width / 2) / layout.EllipseRadiusX;
            var normalY = (pointY - layout.Height / 2) / layout.EllipseRadiusY;
            return normalX * normalX + normalY * normalY < 1;
        }

        return layout.ContentBounds[name].Inflate(8).Contains(pointX, pointY);
    }

    static (double x, double y) Quadratic(double x0, double y0, double x1, double y1, double x2, double y2, double parameter)
    {
        var inverse = 1 - parameter;
        return (
            inverse * inverse * x0 + 2 * inverse * parameter * x1 + parameter * parameter * x2,
            inverse * inverse * y0 + 2 * inverse * parameter * y1 + parameter * parameter * y2);
    }

    /// <summary>
    /// Mermaid's arrow marker as a plain path: a triangle 12 long and 12 wide at a stroke width of 2,
    /// whose tip reaches just past the end of the line, pointing along the curve's final tangent.
    /// </summary>
    static void AddArrowhead(SvgBuilder builder, double controlX, double controlY, double tipX, double tipY)
    {
        var deltaX = tipX - controlX;
        var deltaY = tipY - controlY;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length == 0)
        {
            return;
        }

        var unitX = deltaX / length;
        var unitY = deltaY / length;
        var pointX = tipX + unitX * 1.2;
        var pointY = tipY + unitY * 1.2;
        var baseX = pointX - unitX * 12;
        var baseY = pointY - unitY * 12;
        builder.AddPath(
            string.Create(
                CultureInfo.InvariantCulture,
                $"M{pointX:0.##},{pointY:0.##} L{baseX - unitY * 6:0.##},{baseY + unitX * 6:0.##} L{baseX + unitY * 6:0.##},{baseY - unitX * 6:0.##} Z"),
            fill: arrowColor);
    }

    /// <summary>Mermaid's mulberry32 generator, giving a value in [0, 1) for a seed.</summary>
    static double SeededRandom(int value)
    {
        unchecked
        {
            var state = value + 0x6d2b79f5;
            state = (state ^ (int) ((uint) state >> 15)) * (state | 1);
            state ^= state + (state ^ (int) ((uint) state >> 7)) * (state | 61);
            return (uint) (state ^ (int) ((uint) state >> 14)) / 4294967296.0;
        }
    }

    /// <summary>The vertical boundary: seven cubic segments through jittered points on the centre line.</summary>
    static string FoldPath(double width, double height)
    {
        const int segments = 7;
        var centerX = width / 2;
        var points = Enumerable.Range(0, segments + 1)
            .Select(_ => (x: centerX + SeededRandom(seed + _ * 17) * boundaryAmplitude * 2 - boundaryAmplitude, y: _ * height / segments))
            .ToList();

        var path = new StringBuilder();
        path.Append(CultureInfo.InvariantCulture, $"M{points[0].x:0.##},{points[0].y:0.##}");
        for (var i = 0; i < segments; i++)
        {
            var (startX, startY) = points[i];
            var (endX, endY) = points[i + 1];
            var middleY = (startY + endY) / 2;
            var offset = boundaryAmplitude * 1.5 * (i % 2 == 0 ? 1 : -1) * SeededRandom(seed + i * 31 + 7);
            path.Append(CultureInfo.InvariantCulture, $" C{startX + offset:0.##},{middleY:0.##} {endX - offset:0.##},{middleY:0.##} {endX:0.##},{endY:0.##}");
        }

        return path.ToString();
    }

    /// <summary>The horizontal boundary, drawn as the fold is but across, from its own seed.</summary>
    static string HorizontalBoundary(double width, double height)
    {
        const int segments = 7;
        const int horizontalSeed = seed + 100;
        var centerY = height / 2;
        var points = Enumerable.Range(0, segments + 1)
            .Select(_ => (x: _ * width / segments, y: centerY + SeededRandom(horizontalSeed + _ * 23) * boundaryAmplitude * 2 - boundaryAmplitude))
            .ToList();

        var path = new StringBuilder();
        path.Append(CultureInfo.InvariantCulture, $"M{points[0].x:0.##},{points[0].y:0.##}");
        for (var i = 0; i < segments; i++)
        {
            var (startX, startY) = points[i];
            var (endX, endY) = points[i + 1];
            var middleX = (startX + endX) / 2;
            var offset = boundaryAmplitude * 1.5 * (i % 2 == 0 ? 1 : -1) * SeededRandom(horizontalSeed + i * 37 + 11);
            path.Append(CultureInfo.InvariantCulture, $" C{middleX:0.##},{startY + offset:0.##} {middleX:0.##},{endY - offset:0.##} {endX:0.##},{endY:0.##}");
        }

        return path.ToString();
    }

    /// <summary>The cliff: a steep S-curve from the centre down to the bottom edge.</summary>
    static string CliffPath(double width, double height)
    {
        var centerX = width / 2;
        var top = height * 0.5;
        var span = height - top;
        var amplitude = width * 0.03;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"M{centerX:0.##},{top:0.##} C{centerX + amplitude:0.##},{top + span * 0.2:0.##} {centerX - amplitude * 1.5:0.##},{top + span * 0.55:0.##} {centerX + amplitude * 0.5:0.##},{top + span * 0.75:0.##} C{centerX - amplitude:0.##},{top + span * 0.85:0.##} {centerX + amplitude * 0.3:0.##},{top + span * 0.95:0.##} {centerX:0.##},{height:0.##}");
    }

    static string EllipsePath(double centerX, double centerY, double radiusX, double radiusY) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"M{centerX - radiusX:0.##},{centerY:0.##} A{radiusX:0.##},{radiusY:0.##} 0 1,1 {centerX + radiusX:0.##},{centerY:0.##} A{radiusX:0.##},{radiusY:0.##} 0 1,1 {centerX - radiusX:0.##},{centerY:0.##} Z");

    readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;

        public bool Contains(double pointX, double pointY) =>
            pointX >= X && pointX <= Right && pointY >= Y && pointY <= Bottom;

        public Rect Inflate(double amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

        public static Rect Union(List<Rect> rects)
        {
            var left = rects.Min(static _ => _.X);
            var top = rects.Min(static _ => _.Y);
            return new(left, top, rects.Max(static _ => _.Right) - left, rects.Max(static _ => _.Bottom) - top);
        }
    }

    /// <summary>A line of text centred on its point, both ways.</summary>
    record Text(CynefinDomainName Domain, double X, double Y, string Content, double FontSize, bool Bold, bool Italic)
    {
        public Rect Bounds
        {
            get
            {
                var width = TextWidth(Content, FontSize);
                return new(X - width / 2, Y - FontSize * 0.6, width, FontSize * 1.2);
            }
        }
    }

    record Badge(CynefinDomainName Domain, string Text, double X, double Y, double Width, bool Overflow)
    {
        public static Badge Centered(CynefinDomainName domain, string text, double centerX, double top, bool Overflow)
        {
            var width = BadgeWidth(text);
            return new(domain, text, centerX - width / 2, top, width, Overflow);
        }

        public Rect Bounds => new(X, Y, Width, itemHeight);
    }

    sealed class Layout(double width, double height)
    {
        public double Width { get; } = width;
        public double Height { get; } = height;
        public Dictionary<CynefinDomainName, Rect> Regions { get; } = [];
        public Dictionary<CynefinDomainName, Rect> ContentBounds { get; } = [];
        public List<Text> Texts { get; } = [];
        public List<Badge> Badges { get; } = [];
        public double EllipseRadiusX { get; set; }
        public double EllipseRadiusY { get; set; }
        public bool NeedsWidth { get; set; }
        public bool NeedsHeight { get; set; }

        /// <summary>The centre of a domain, where Mermaid starts and ends its transitions.</summary>
        public (double x, double y) Center(CynefinDomainName name)
        {
            if (name == CynefinDomainName.Confusion)
            {
                return (Width / 2, Height / 2);
            }

            var region = Regions[name];
            return (region.CenterX, region.CenterY);
        }
    }
}
