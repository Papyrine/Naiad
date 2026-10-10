namespace Naiad.Diagrams.Timeline;

public class TimelineRenderer : IDiagramRenderer<TimelineModel>
{
    // Geometry taken from Mermaid's timeline renderer (timelineRenderer.ts / svgDraw.js). Every box is
    // a fixed width sitting on a fixed pitch — only its height follows the text it holds — and each
    // period drops a dashed connector through the activity line to its events.
    const double textWidth = 150;
    const double nodePadding = 20;
    const double nodeWidth = textWidth + nodePadding * 2;
    const double nodePitch = 200;
    const double cornerRadius = 5;

    // The activity line starts this far left of the first box and runs this far past the last one.
    const double lineLeadIn = 50;
    const double lineRunOff = 250;

    // Row gaps: section headers to periods, periods to the activity line, a period's top to its first
    // event's top, and between stacked events.
    const double sectionGap = 50;
    const double activityLineGap = 50;
    const double eventDrop = 200;
    const double eventGap = 10;

    // Every box in a row is padded out to the tallest in that row, plus this much slack.
    const double rowSlack = 20;
    const double minEventHeight = 50;

    // Mermaid's line-height for wrapped label text, in ems.
    const double lineHeight = 1.1;

    const double activityStrokeWidth = 4;
    const double connectorStrokeWidth = 2;
    const double underlineStrokeWidth = 3;

    const string activityArrowId = "timeline-arrow";
    const string connectorArrowId = "timeline-connector-arrow";

    /// <summary>
    /// Mermaid's default-theme colour scale as it lands on a timeline: <c>Fill</c> is
    /// <c>cScale{n}</c>, <c>Text</c> is <c>cScaleLabel{n}</c>, and <c>Underline</c> is
    /// <c>cScaleInv{n}</c> — the rule Mermaid draws along each box's bottom edge. <c>EventFill</c> is
    /// <c>Fill</c> put through the <c>filter:brightness(120%)</c> Mermaid applies to event boxes,
    /// folded in here because Naiad emits colours as attributes rather than running CSS filters.
    /// </summary>
    static NodeColors[] palette =
    [
        new("#8686FF", "#A1A1FF", "#FFFFFF", "#FFFFB9"),
        new("#FFFF78", "#FFFF90", "#000000", "#ABABFF"),
        new("#D7FF86", "#FFFFA1", "#000000", "#D0B9FF"),
        new("#C386FF", "#EAA1FF", "#FFFFFF", "#DCFFB9"),
        new("#FF86FF", "#FFA1FF", "#000000", "#B9FFB9"),
        new("#FF86C3", "#FFA1EA", "#000000", "#B9FFDC"),
        new("#FF8686", "#FFA1A1", "#000000", "#B9FFFF"),
        new("#FFC386", "#FFEAA1", "#000000", "#B9DCFF"),
        new("#C3FF86", "#EAFFA1", "#000000", "#DCB9FF"),
        new("#86FFC3", "#A1FFEA", "#000000", "#FFB9DC"),
        new("#86FFFF", "#A1FFFF", "#000000", "#FFB9B9"),
        new("#86C3FF", "#A1EAFF", "#000000", "#FFDCB9")
    ];

    public SvgDocument Render(TimelineModel model, RenderOptions options)
    {
        if (model.Sections.Count == 0 ||
            model.Sections.All(_ => _.Periods.Count == 0))
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "Empty timeline",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        var sections = model.Sections;
        var periods = sections.SelectMany(_ => _.Periods).ToList();

        // A timeline written without any `section` line still parses into one (unnamed) section. That
        // is the case Mermaid colours per period; once there are named sections the colour belongs to
        // the section, and every period under it shares it.
        var hasSections = sections.Any(_ => !string.IsNullOrEmpty(_.Name));

        // An empty section still claims a slot, so the header of a trailing empty one is what sets the
        // right edge — which makes the slot count, not the period count, the width to lay out against.
        var slotCount = sections.Sum(_ => Math.Max(_.Periods.Count, 1));

        var headerHeight = hasSections
            ? sections.Max(_ => NodeHeight(WrapLines(_.Name, options).Count, options)) + rowSlack
            : 0;
        var periodHeight = periods.Max(_ => NodeHeight(WrapLines(_.Label, options).Count, options)) + rowSlack;
        var eventStackHeight = periods.Max(_ => EventStackHeight(_, options));

        var titleFontSize = options.FontSize * 2.0;
        var titleHeight = string.IsNullOrEmpty(model.Title) ? 0 : titleFontSize * 1.5;

        var headerY = titleHeight;
        var periodY = headerY + (hasSections ? headerHeight + sectionGap : 0);
        var activityLineY = periodY + periodHeight + activityLineGap;
        var eventY = periodY + eventDrop;
        var connectorTop = periodY + periodHeight;
        var connectorBottom = connectorTop + eventDrop + eventStackHeight;

        var contentRight = lineLeadIn + (slotCount - 1) * nodePitch + nodeWidth;
        var width = contentRight + lineRunOff;

        var builder = new SvgBuilder();
        builder.Size(width, connectorBottom);
        builder.Padding(options.Padding);
        AddArrowMarkers(builder);

        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                width / 2,
                titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: titleFontSize,
                fontFamily: options.FontFamily,
                fontWeight: "bold");
        }

        var slot = 0;
        var periodIndex = 0;
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            var sectionColors = palette[sectionIndex % palette.Length];
            var slots = Math.Max(section.Periods.Count, 1);

            if (hasSections)
            {
                // The header spans its periods exactly: the first one's left edge to the last one's right.
                DrawNode(
                    builder,
                    SlotX(slot),
                    headerY,
                    (slots - 1) * nodePitch + nodeWidth,
                    headerHeight,
                    WrapLines(section.Name, options),
                    sectionColors.Fill,
                    sectionColors,
                    options);
            }

            foreach (var period in section.Periods)
            {
                var colors = hasSections ? sectionColors : palette[periodIndex % palette.Length];
                var periodX = SlotX(slot);

                DrawNode(
                    builder,
                    periodX,
                    periodY,
                    nodeWidth,
                    periodHeight,
                    WrapLines(period.Label, options),
                    colors.Fill,
                    colors,
                    options);

                // Drawn before the event boxes so they cover it, the way Mermaid stacks them: the
                // connector reads as dashes between the boxes rather than through them.
                var centreX = periodX + nodeWidth / 2;
                builder.AddPath(
                    LinePath(centreX, connectorTop, centreX, connectorBottom),
                    fill: "none",
                    stroke: "#000000",
                    strokeWidth: connectorStrokeWidth,
                    strokeDasharray: "5,5",
                    markerEnd: $"url(#{connectorArrowId})");

                var nextEventY = eventY;
                foreach (var periodEvent in period.Events)
                {
                    var lines = WrapLines(periodEvent, options);
                    var height = EventHeight(lines.Count, options);
                    DrawNode(builder, periodX, nextEventY, nodeWidth, height, lines, colors.EventFill, colors, options);
                    nextEventY += height + eventGap;
                }

                slot++;
                periodIndex++;
            }

            slot += slots - section.Periods.Count;
        }

        // Last, so it sits over the dashed connectors it crosses.
        builder.AddPath(
            LinePath(0, activityLineY, width, activityLineY),
            fill: "none",
            stroke: "#000000",
            strokeWidth: activityStrokeWidth,
            markerEnd: $"url(#{activityArrowId})");

        return builder.Build();
    }

    static double SlotX(int slot) => lineLeadIn + slot * nodePitch;

    static void AddArrowMarkers(SvgBuilder builder)
    {
        // Mermaid's arrowhead is 6x4 at refX 5 / refY 2, sized in stroke-width units. Naiad's
        // rasterizers read every marker as userSpaceOnUse, so the multiplication by the line's stroke
        // width is baked in here — one marker per line weight — leaving browsers and both PNG backends
        // drawing the same arrow. The fill is the SVG default black, stated explicitly because the
        // rasterizers fall back to #333 for a marker that does not name one.
        builder.AddMarker(activityArrowId, "M0,0 V16 L24,8 Z", 24, 16, 20, 8, "#000000", "userSpaceOnUse");
        builder.AddMarker(connectorArrowId, "M0,0 V8 L12,4 Z", 12, 8, 10, 4, "#000000", "userSpaceOnUse");
    }

    /// <summary>
    /// Draws one box: Mermaid's rounded-top-corner background, the inverted-colour rule along its
    /// bottom edge, and the wrapped label.
    /// </summary>
    static void DrawNode(
        SvgBuilder builder,
        double x,
        double y,
        double width,
        double height,
        List<string> lines,
        string fill,
        NodeColors colors,
        RenderOptions options)
    {
        builder.AddPath(NodePath(x, y, width, height), fill: fill);

        // The stroke straddles the bottom edge, so the rule reads as a band under the box.
        builder.AddLine(
            x,
            y + height,
            x + width,
            y + height,
            stroke: colors.Underline,
            strokeWidth: underlineStrokeWidth);

        // Mermaid hangs the label off the top of the box rather than centring it: the first line's
        // centre sits one em below half the padding, and each further line one line-height on.
        for (var index = 0; index < lines.Count; index++)
        {
            builder.AddText(
                x + width / 2,
                y + nodePadding / 2 + options.FontSize * (1 + lineHeight * index),
                lines[index],
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily,
                fill: colors.Text);
        }
    }

    /// <summary>A rectangle with rounded top corners and square bottom ones.</summary>
    static string NodePath(double x, double y, double width, double height) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"M{x:0.##} {y + height - cornerRadius:0.##} v{-(height - 2 * cornerRadius):0.##} " +
            $"q0,-{cornerRadius:0.##},{cornerRadius:0.##},-{cornerRadius:0.##} " +
            $"h{width - 2 * cornerRadius:0.##} " +
            $"q{cornerRadius:0.##},0,{cornerRadius:0.##},{cornerRadius:0.##} " +
            $"v{height - cornerRadius:0.##} H{x:0.##} Z");

    static string LinePath(double x1, double y1, double x2, double y2) =>
        string.Create(CultureInfo.InvariantCulture, $"M {x1:0.##} {y1:0.##} L {x2:0.##} {y2:0.##}");

    static double EventStackHeight(TimePeriod period, RenderOptions options)
    {
        if (period.Events.Count == 0)
        {
            return 0;
        }

        return period.Events.Sum(_ => EventHeight(WrapLines(_, options).Count, options)) +
               eventGap * (period.Events.Count - 1);
    }

    static double EventHeight(int lineCount, RenderOptions options) =>
        Math.Max(NodeHeight(lineCount, options), minEventHeight);

    /// <summary>
    /// The height Mermaid gives a box holding a label of <paramref name="lineCount"/> lines: the text's
    /// own height, plus half a line of leading, plus the box padding.
    /// </summary>
    static double NodeHeight(int lineCount, RenderOptions options) =>
        options.FontSize * (1.19 + lineHeight * (Math.Max(lineCount, 1) - 1) + lineHeight * 0.5) + nodePadding;

    /// <summary>
    /// Greedy word wrap at the box's text width, as Mermaid's <c>wrap</c> does. A <c>&lt;br&gt;</c> in the
    /// text always starts a new line, and each of the pieces it separates is wrapped on its own.
    /// </summary>
    static List<string> WrapLines(string? text, RenderOptions options)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return lines;
        }

        foreach (var piece in LabelLines.Split(text))
        {
            WrapInto(lines, piece, options);
        }

        return lines;
    }

    static void WrapInto(List<string> lines, string text, RenderOptions options)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0)
            {
                if (MeasureText($"{line} {word}", options.FontSize) > textWidth)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                else
                {
                    line.Append(' ');
                }
            }

            line.Append(word);
        }

        lines.Add(line.ToString());
    }

    static double MeasureText(string text, double fontSize) =>
        text.Length * fontSize * 0.55;

    readonly record struct NodeColors(string Fill, string EventFill, string Text, string Underline);
}
