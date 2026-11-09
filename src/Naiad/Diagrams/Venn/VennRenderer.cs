namespace Naiad.Diagrams.Venn;

/// <summary>
/// Draws the sets as translucent circles sized and overlapped to match their declared sizes, as Mermaid
/// does with venn.js: each set is labelled in the part of its circle no other set covers, each labelled
/// union in the middle of its overlap, and text nodes are spread over a grid inside their area. The
/// drawing is Mermaid's fixed 800 × 450, with its sizes (set out for a 1600-wide reference and halved).
/// Mermaid measures its labels in the DOM; here they are estimated from their character counts.
/// </summary>
public class VennRenderer : IDiagramRenderer<VennModel>
{
    const double width = 800;
    const double height = 450;
    const double scale = width / 1600;
    const double layoutPadding = 15;
    const double titleFontSize = 32 * scale;
    const double labelFontSize = 48 * scale;
    const double minLabelFontSize = 14;
    const double minTextNodeFontSize = 12;
    const double textNodeFontSize = 40 * scale;
    const double charWidthFactor = 0.55;
    const string textColor = "#333";

    // The default theme's venn1 to venn8: its primary, secondary and tertiary colours, and hue shifts of
    // them, each darkened by 30 or 40 points of lightness.
    static readonly string[] palette =
    [
        "#5353FF", "#FFFF45", "#B5FF20", "#FF53FF",
        "#53FFFF", "#45FF45", "#FF5353", "#45FFFF"
    ];

    public SvgDocument Render(VennModel model, RenderOptions options)
    {
        var builder = new SvgBuilder();
        builder.Options(options);
        builder.Size(width, height);

        var hasTitle = !string.IsNullOrEmpty(model.Title);
        var titleHeight = hasTitle ? 48 * scale : 0;
        if (hasTitle)
        {
            builder.AddText(
                width / 2,
                32 * scale,
                model.Title!,
                anchor: "middle",
                baseline: "middle",
                fontSize: titleFontSize,
                fontFamily: options.FontFamily,
                fill: textColor);
        }

        var areas = RenderAreas(model.Areas);
        if (areas.Count == 0)
        {
            return builder.Build();
        }

        var layout = VennLayout.Compute(
            areas.Select(_ => new VennLayout.Area(_.Sets, _.Size)).ToList(),
            width,
            height - titleHeight,
            layoutPadding);
        var styles = StylesByKey(model.Styles);

        var labelBands = new Dictionary<string, (double top, double bottom)?>(StringComparer.Ordinal);
        var setIndex = 0;
        foreach (var area in areas)
        {
            var key = area.Key;
            styles.TryGetValue(key, out var style);
            var circles = area.Sets.Select(layout.Find).ToList();

            if (area.IsSet)
            {
                var circle = circles[0];
                var baseColor = Clean(style?.GetValueOrDefault("fill")) ?? palette[setIndex % palette.Length];
                setIndex++;
                var stroke = Clean(style?.GetValueOrDefault("stroke")) ?? baseColor;
                var strokeWidth = Clean(style?.GetValueOrDefault("stroke-width")) ?? Format(5 * scale);
                var fillOpacity = Clean(style?.GetValueOrDefault("fill-opacity")) ?? "0.1";
                builder.AddPath(
                    CirclePath(circle.X, circle.Y + titleHeight, circle.Radius),
                    style: $"fill:{baseColor};fill-opacity:{fillOpacity};stroke:{stroke};stroke-width:{strokeWidth};stroke-opacity:0.95");

                var labelColor = Clean(style?.GetValueOrDefault("color")) ?? Darken(baseColor, 30);
                labelBands[key] = AddLabel(builder, layout, area, area.Label ?? area.Sets[0], titleHeight, labelColor, options);
            }
            else
            {
                // An overlap is only filled when styled; otherwise it is there for its label alone.
                if (Clean(style?.GetValueOrDefault("fill")) is { } fill)
                {
                    VennLayout.IntersectionArea(circles, out var arcs);
                    if (arcs.Count > 0)
                    {
                        builder.AddPath(ArcsPath(arcs, titleHeight), style: $"fill:{fill};fill-opacity:1");
                    }
                }

                if (area.Label is { } label)
                {
                    var labelColor = Clean(style?.GetValueOrDefault("color")) ?? textColor;
                    labelBands[key] = AddLabel(builder, layout, area, label, titleHeight, labelColor, options);
                }
            }
        }

        AddTextNodes(builder, layout, areas, model.TextNodes, styles, labelBands, titleHeight, options);
        return builder.Build();
    }

    /// <summary>
    /// The areas handed to the layout. A set of size 0 is dropped along with every union naming it, as
    /// venn.js does. A union of three or more sets also gets any of its pairwise overlaps that were not
    /// declared, each a quarter of the smaller set, so the circles overlap far enough for it to show.
    /// </summary>
    static List<VennArea> RenderAreas(List<VennArea> declared)
    {
        var existing = new HashSet<string>(declared.Select(_ => _.Key), StringComparer.Ordinal);
        var setSizes = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var area in declared.Where(_ => _.IsSet))
        {
            setSizes[area.Sets[0]] = area.Size;
        }

        var areas = declared.ToList();
        foreach (var area in declared.Where(_ => _.Sets.Count >= 3))
        {
            for (var i = 0; i < area.Sets.Count - 1; i++)
            {
                for (var j = i + 1; j < area.Sets.Count; j++)
                {
                    var pair = new[] { area.Sets[i], area.Sets[j] };
                    if (existing.Add(string.Join('|', pair)))
                    {
                        var size = setSizes.TryGetValue(pair[0], out var first) && setSizes.TryGetValue(pair[1], out var second)
                            ? Math.Min(first, second) / 4
                            : 2.5;
                        areas.Add(
                            new()
                            {
                                Sets = pair,
                                Size = size
                            });
                    }
                }
            }
        }

        var empty = new HashSet<string>(
            areas
                .Where(_ => _ is { IsSet: true, Size: 0 })
                .Select(_ => _.Sets[0]),
            StringComparer.Ordinal);
        var known = new HashSet<string>(areas.Where(_ => _.IsSet).Select(_ => _.Sets[0]), StringComparer.Ordinal);
        return areas
            .Where(_ => _.Sets.All(set => known.Contains(set) && !empty.Contains(set)))
            .ToList();
    }

    static Dictionary<string, Dictionary<string, string>> StylesByKey(List<VennStyle> styles)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var style in styles)
        {
            if (!result.TryGetValue(style.Key, out var merged))
            {
                merged = new(StringComparer.OrdinalIgnoreCase);
                result[style.Key] = merged;
            }

            foreach (var (property, value) in style.Properties)
            {
                merged[property] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// A set or union label at the centre venn.js finds for its region. Mermaid sizes these at 24px
    /// whatever the region, so a long label runs into its neighbours; here a label is wrapped at word
    /// breaks to the width of its region along the centre's row, moved to the middle of that row if it
    /// does not fit around the centre, and shrunk if a word is still too wide. Returns the label's top
    /// and bottom, in layout coordinates, for the text nodes to keep clear of.
    /// </summary>
    static (double top, double bottom)? AddLabel(
        SvgBuilder builder,
        VennLayout.Result layout,
        VennArea area,
        string label,
        double titleHeight,
        string fill,
        RenderOptions options)
    {
        if (!layout.TextCentres.TryGetValue(area.Key, out var centre))
        {
            return null;
        }

        var labelX = Math.Floor(centre.X);
        var labelY = Math.Floor(centre.Y);
        var span = RegionSpan(layout, area, labelX, labelY);
        var available = span is var (left, right) ? right - left : layout.Find(area.Sets[0]).Radius * 2;

        // A label that fits on one line at three quarters of the size is shrunk rather than broken, so
        // that short labels in small regions stay whole.
        var fontSize = labelFontSize;
        var oneLineWidth = label.Length * fontSize * charWidthFactor;
        List<string> lines;
        if (oneLineWidth * 0.75 <= available * 0.95)
        {
            lines = [label];
        }
        else
        {
            lines = WrapToWidth(label, available * 0.9, fontSize);
        }

        var widest = lines.Max(static _ => _.Length) * fontSize * charWidthFactor;
        if (span is var (spanLeft, spanRight))
        {
            if (widest / 2 > Math.Min(labelX - spanLeft, spanRight - labelX) * 0.95)
            {
                labelX = (spanLeft + spanRight) / 2;
            }

            if (widest > available * 0.95)
            {
                fontSize = Math.Max(minLabelFontSize, fontSize * available * 0.95 / widest);
            }
        }

        // venn.js sets the first line .35em below the centre, less half the extra lines' height.
        const double lineHeight = 1.1;
        var initial = 0.35 - (lines.Count - 1) * lineHeight / 2;
        for (var i = 0; i < lines.Count; i++)
        {
            builder.AddText(
                labelX,
                labelY + titleHeight + (initial + i * lineHeight) * fontSize,
                lines[i],
                anchor: "middle",
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fill: fill);
        }

        return (labelY + (initial - 0.75) * fontSize, labelY + (initial + (lines.Count - 1) * lineHeight + 0.25) * fontSize);
    }

    /// <summary>
    /// The stretch of the row at <paramref name="pointY"/> through <paramref name="pointX"/> that lies inside every
    /// circle of the area and outside every other circle, or null when the point is not in the region.
    /// A circle holding the point itself contains the whole area and so does not bound it.
    /// </summary>
    static (double left, double right)? RegionSpan(VennLayout.Result layout, VennArea area, double pointX, double pointY)
    {
        var left = double.NegativeInfinity;
        var right = double.PositiveInfinity;
        foreach (var circle in layout.Circles)
        {
            var offsetY = pointY - circle.Y;
            if (Math.Abs(offsetY) >= circle.Radius)
            {
                if (area.Sets.Contains(circle.SetId))
                {
                    return null;
                }

                continue;
            }

            var halfChord = Math.Sqrt(circle.Radius * circle.Radius - offsetY * offsetY);
            var chordLeft = circle.X - halfChord;
            var chordRight = circle.X + halfChord;
            if (area.Sets.Contains(circle.SetId))
            {
                left = Math.Max(left, chordLeft);
                right = Math.Min(right, chordRight);
            }
            else if (chordRight <= pointX)
            {
                left = Math.Max(left, chordRight);
            }
            else if (chordLeft >= pointX)
            {
                right = Math.Min(right, chordLeft);
            }
        }

        return left < pointX && pointX < right ? (left, right) : null;
    }

    /// <summary>
    /// The text nodes of each area, laid out as Mermaid lays them: in a near-square grid over a box that
    /// fits inside the area's circles around its label centre, pushed down when the area has a label.
    /// Mermaid's push assumes a one-line label, so rows that would still run into the label (a wrapped
    /// one, or a set's own name, which Mermaid does not count) are moved on down below it.
    /// </summary>
    static void AddTextNodes(
        SvgBuilder builder,
        VennLayout.Result layout,
        List<VennArea> areas,
        List<VennTextNode> textNodes,
        Dictionary<string, Dictionary<string, string>> styles,
        Dictionary<string, (double top, double bottom)?> labelBands,
        double titleHeight,
        RenderOptions options)
    {
        var lineHeight = textNodeFontSize * 1.2;
        foreach (var group in textNodes.GroupBy(_ => _.Key, StringComparer.Ordinal))
        {
            var area = areas.FirstOrDefault(_ => _.Key == group.Key);
            if (area is null ||
                !layout.TextCentres.TryGetValue(group.Key, out var centre))
            {
                continue;
            }

            var nodes = group.ToList();
            var circles = area.Sets.Select(layout.Find).ToList();
            var innerRadius = Math.Max(0, circles.Min(_ => _.Radius - Math.Sqrt(Math.Pow(centre.X - _.X, 2) + Math.Pow(centre.Y - _.Y, 2))));
            if (innerRadius == 0)
            {
                innerRadius = circles.Min(_ => _.Radius) * 0.6;
            }

            var band = labelBands.GetValueOrDefault(group.Key);
            var innerWidth = Math.Max(80 * scale, innerRadius * 2 * 0.95);
            var innerHeight = Math.Max(60 * scale, innerRadius * 2 * 0.95);
            var labelOffsetBase = band is null ? 0 : Math.Min(32 * scale, innerRadius * 0.25);
            var labelOffset = labelOffsetBase + (nodes.Count <= 2 ? 30 * scale : 0);
            var startX = centre.X - innerWidth / 2;
            var startY = centre.Y - innerHeight / 2 + labelOffset;
            var columns = Math.Max(1, (int) Math.Ceiling(Math.Sqrt(nodes.Count)));
            var rows = Math.Max(1, (int) Math.Ceiling((double) nodes.Count / columns));
            var cellWidth = innerWidth / columns;
            var cellHeight = innerHeight / rows;

            // Mermaid puts each node in an HTML box 90% of its cell wide that wraps at word breaks.
            var wrapped = nodes.Select(_ => WrapToWidth(_.Label ?? _.Id, cellWidth * 0.9, textNodeFontSize)).ToList();
            var shift = 0.0;
            if (band is var (labelTop, labelBottom))
            {
                for (var row = 0; row < rows; row++)
                {
                    var rowLines = wrapped.Skip(row * columns).Take(columns).Max(static _ => _.Count);
                    var rowCentre = startY + cellHeight * (row + 0.5);
                    var halfHeight = rowLines * lineHeight / 2;
                    if (rowCentre + halfHeight > labelTop &&
                        rowCentre - halfHeight < labelBottom)
                    {
                        shift = labelBottom + 4 + halfHeight - rowCentre;
                        break;
                    }
                }
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var cellX = startX + cellWidth * (i % columns + 0.5);
                var cellY = startY + shift + cellHeight * (i / columns + 0.5) + titleHeight;
                var fill = styles.TryGetValue(node.Id, out var style)
                    ? Clean(style.GetValueOrDefault("color")) ?? textColor
                    : textColor;

                // The region may narrow away from its centre, so the node is wrapped again to the width
                // the region has at its top and bottom lines where it ends up; twice, since rewrapping
                // changes how far those lines reach.
                var lines = wrapped[i];
                var regionWidth = cellWidth * 0.9;
                for (var pass = 0; pass < 2; pass++)
                {
                    var halfHeight = lines.Count * lineHeight / 2;
                    regionWidth = cellWidth * 0.9;
                    foreach (var edgeY in (double[]) [cellY - halfHeight, cellY + halfHeight])
                    {
                        if (RegionSpan(layout, area, cellX, edgeY - titleHeight) is var (spanLeft, spanRight))
                        {
                            regionWidth = Math.Min(regionWidth, 2 * Math.Min(cellX - spanLeft, spanRight - cellX) * 0.9);
                        }
                    }

                    if (regionWidth < cellWidth * 0.9)
                    {
                        lines = WrapToWidth(node.Label ?? node.Id, regionWidth, textNodeFontSize);
                    }
                }

                // A word too wide to break is shrunk instead.
                var widest = lines.Max(static _ => _.Length) * textNodeFontSize * charWidthFactor;
                var fontSize = widest > regionWidth
                    ? Math.Max(minTextNodeFontSize, textNodeFontSize * regionWidth / widest)
                    : textNodeFontSize;
                var nodeLineHeight = fontSize * 1.2;
                var firstY = cellY - (lines.Count - 1) * nodeLineHeight / 2;
                for (var line = 0; line < lines.Count; line++)
                {
                    builder.AddText(
                        cellX,
                        firstY + line * nodeLineHeight,
                        lines[line],
                        anchor: "middle",
                        baseline: "middle",
                        fontSize: fontSize,
                        fontFamily: options.FontFamily,
                        fill: fill);
                }
            }
        }
    }

    /// <summary>Greedily wraps <paramref name="text"/> at word breaks to lines no wider than <paramref name="maxWidth"/>.</summary>
    static List<string> WrapToWidth(string text, double maxWidth, double fontSize)
    {
        var maxChars = Math.Max(1, (int) Math.Floor(maxWidth / (fontSize * charWidthFactor)));
        var lines = new List<string>();
        foreach (var word in text.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (lines.Count > 0 &&
                lines[^1].Length + 1 + word.Length <= maxChars)
            {
                lines[^1] += " " + word;
            }
            else
            {
                lines.Add(word);
            }
        }

        return lines.Count == 0 ? [text] : lines;
    }

    static string CirclePath(double centerX, double centerY, double radius) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"M {centerX - radius:0.##} {centerY:0.##} A {radius:0.##} {radius:0.##} 0 1 0 {centerX + radius:0.##} {centerY:0.##} A {radius:0.##} {radius:0.##} 0 1 0 {centerX - radius:0.##} {centerY:0.##} Z");

    /// <summary>The outline of an overlap: from the end of the first arc, along each arc in turn.</summary>
    static string ArcsPath(List<VennLayout.Arc> arcs, double offsetY)
    {
        if (arcs.Count == 1)
        {
            var circle = arcs[0].Circle;
            return CirclePath(circle.X, circle.Y + offsetY, circle.Radius);
        }

        var path = new StringBuilder();
        path.Append(CultureInfo.InvariantCulture, $"M {arcs[0].P2.X:0.##} {arcs[0].P2.Y + offsetY:0.##}");
        foreach (var arc in arcs)
        {
            var radius = arc.Circle.Radius;
            path.Append(
                CultureInfo.InvariantCulture,
                $" A {radius:0.##} {radius:0.##} 0 {(arc.Large ? 1 : 0)} 1 {arc.P1.X:0.##} {arc.P1.Y + offsetY:0.##}");
        }

        return path.Append(" Z").ToString();
    }

    static string Format(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// A style value as written, less anything that could break out of the attribute or declaration it
    /// is written into. Empty values count as unset.
    /// </summary>
    static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var cleaned = new string(value.Where(static _ => _ is not ('\'' or '"' or '<' or '>' or '&' or ';')).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>
    /// The colour with its HSL lightness lowered by <paramref name="amount"/> points, as khroma's darken
    /// does; Mermaid draws each set's label in its colour darkened by 30. A colour that cannot be read
    /// falls back to the text colour.
    /// </summary>
    static string Darken(string color, double amount)
    {
        if (!CssColor.TryParse(color, out var rgba))
        {
            return textColor;
        }

        var red = rgba.R / 255.0;
        var green = rgba.G / 255.0;
        var blue = rgba.B / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var lightness = (max + min) / 2;
        var delta = max - min;
        double hue = 0;
        double saturation = 0;
        if (delta > 0)
        {
            saturation = delta / (1 - Math.Abs(2 * lightness - 1));
            if (max == red)
            {
                hue = 60 * ((green - blue) / delta % 6);
            }
            else if (max == green)
            {
                hue = 60 * ((blue - red) / delta + 2);
            }
            else
            {
                hue = 60 * ((red - green) / delta + 4);
            }
        }

        lightness = Math.Clamp(lightness - amount / 100, 0, 1);

        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var sector = (hue + 360) % 360 / 60;
        var second = chroma * (1 - Math.Abs(sector % 2 - 1));
        var (redPart, greenPart, bluePart) = (int) sector switch
        {
            0 => (chroma, second, 0.0),
            1 => (second, chroma, 0.0),
            2 => (0.0, chroma, second),
            3 => (0.0, second, chroma),
            4 => (second, 0.0, chroma),
            _ => (chroma, 0.0, second)
        };
        var match = lightness - chroma / 2;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{Channel(redPart + match):X2}{Channel(greenPart + match):X2}{Channel(bluePart + match):X2}");

        static int Channel(double value) =>
            (int) Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
