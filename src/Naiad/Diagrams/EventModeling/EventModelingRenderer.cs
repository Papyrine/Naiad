namespace Naiad.Diagrams.EventModeling;

/// <summary>
/// Draws an event model as Mermaid does: horizontal swimlanes (UI/Automation, Command/Read Model and Events,
/// plus one per namespace and kind), a box per frame placed left to right in timeline order, and an arrow
/// into each frame from the frame it follows from. A frame in a different lane from the one before it starts
/// a little left of that one's right edge, so the timeline reads as a staircase; one in the same lane goes
/// just right of it. Mermaid measures its labels in the DOM; here they are estimated from their character
/// counts.
/// </summary>
public class EventModelingRenderer : IDiagramRenderer<EventModelingModel>
{
    // Mermaid's diagramProps for this diagram.
    const double swimlaneMinHeight = 70;
    const double swimlanePadding = 15;
    const double swimlaneGap = 10;
    const double boxPadding = 10;
    const double boxOverlap = 90;
    const double boxMinWidth = 80;
    const double boxMaxWidth = 450;
    const double boxMinHeight = 80;
    const double boxMaxHeight = 750;
    const double contentStartX = 250;
    const double textMaxWidth = boxMaxWidth - 2 * boxPadding;
    const double boxTextPadding = 10;
    const double swimlaneLabelX = 30;
    const double swimlaneLabelY = 30;

    const double boldCharWidthFactor = 0.6;
    const double monospaceCharWidthFactor = 0.6;
    const string monospaceFont = "monospace";
    const char nonBreakingSpace = (char) 0xA0;

    const string swimlaneFill = "rgb(250,250,250)";
    const string swimlaneStroke = "rgb(240,240,240)";
    const string relationStroke = "#000";
    const string arrowheadFill = "#000000";
    const string textColor = "#333";

    public SvgDocument Render(EventModelingModel model, RenderOptions options)
    {
        var fontSize = (double) options.FontSize;
        var lineHeight = fontSize * SvgBuilder.LineHeightFactor;
        var padding = options.Padding;
        var hasTitle = !string.IsNullOrWhiteSpace(model.Title);
        var titleFontSize = fontSize + 4;
        var titleHeight = hasTitle ? titleFontSize * 1.2 + 15 : 0;

        var builder = new SvgBuilder();
        builder.Options(options);

        if (model.Frames.Count == 0)
        {
            var text = hasTitle ? model.Title! : "Empty diagram";
            var width = Math.Max(200, text.Length * titleFontSize * boldCharWidthFactor + 40);
            builder.Size(width, 100);
            builder.AddText(
                width / 2,
                50,
                text,
                anchor: "middle",
                baseline: "middle",
                fontSize: hasTitle ? titleFontSize : fontSize,
                fontFamily: options.FontFamily,
                fontWeight: hasTitle ? "bold" : null,
                fill: textColor);
            return builder.Build();
        }

        var swimlanes = new List<Swimlane>();
        var boxes = new List<Box>();
        foreach (var frame in model.Frames)
        {
            var swimlane = FindOrAddSwimlane(swimlanes, frame);
            var content = MeasureContent(model, frame, fontSize, lineHeight);
            var width = Math.Clamp(content.Width + 2 * boxTextPadding, boxMinWidth, boxMaxWidth) + 2 * boxPadding;
            var height = Math.Clamp(content.Height + 2 * boxTextPadding, boxMinHeight, boxMaxHeight) + 2 * boxPadding;
            boxes.Add(new(frame, swimlane, content, width, height));
            swimlane.MaxBoxHeight = Math.Max(swimlane.MaxBoxHeight, height);
        }

        // Mermaid orders the lanes by kind (UI/Automation, then Command/Read Model, then Events), each kind's
        // own lane before its namespaced ones, which follow in the order they first appear.
        swimlanes = swimlanes
            .OrderBy(static _ => _.Group)
            .ThenBy(static _ => _.Namespace is null ? 0 : 1)
            .ThenBy(static _ => _.Order)
            .ToList();

        var laneY = titleHeight;
        foreach (var swimlane in swimlanes)
        {
            swimlane.Y = laneY;
            swimlane.Height = Math.Max(swimlaneMinHeight, swimlane.MaxBoxHeight) + 2 * swimlanePadding;
            laneY += swimlane.Height + swimlaneGap;
        }

        // Mermaid starts the boxes at a fixed 250, which a long namespace's lane label runs past.
        var widestLabel = swimlanes.Max(_ => _.Label.Length * fontSize * boldCharWidthFactor);
        var startX = Math.Max(contentStartX, swimlaneLabelX + widestLabel + 2 * boxPadding);

        // Mermaid places a box from the box before it alone, which can land it on an earlier box in its own
        // lane when the timeline comes back to that lane; it is kept clear of that box here.
        Box? previous = null;
        var maxRight = 0.0;
        foreach (var box in boxes)
        {
            double x;
            if (previous is null)
            {
                x = startX;
            }
            else if (previous.Swimlane == box.Swimlane)
            {
                x = previous.X + previous.Width + boxPadding;
            }
            else
            {
                x = previous.X + previous.Width + boxPadding - boxOverlap + boxPadding;
                if (box.Swimlane.Right is { } laneRight)
                {
                    x = Math.Max(x, laneRight + boxPadding);
                }
            }

            box.X = x;
            box.Swimlane.Right = x + box.Width;
            maxRight = Math.Max(maxRight, x + box.Width + boxPadding);
            previous = box;
        }

        var diagramWidth = maxRight + swimlanePadding;
        var diagramHeight = laneY - swimlaneGap;
        builder.Size(diagramWidth + 2 * padding, diagramHeight + 2 * padding);

        if (hasTitle)
        {
            builder.AddText(
                padding + diagramWidth / 2,
                padding + titleHeight / 2 - 5,
                model.Title!,
                anchor: "middle",
                baseline: "middle",
                fontSize: titleFontSize,
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: textColor);
        }

        foreach (var swimlane in swimlanes)
        {
            builder.AddRect(
                padding,
                padding + swimlane.Y,
                diagramWidth,
                swimlane.Height,
                rx: 3,
                fill: swimlaneFill,
                stroke: swimlaneStroke);
            builder.AddText(
                padding + swimlaneLabelX,
                padding + swimlane.Y + swimlaneLabelY,
                swimlane.Label,
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: textColor);
        }

        foreach (var box in boxes)
        {
            DrawBox(builder, box, padding, fontSize, lineHeight, options.FontFamily);
        }

        foreach (var (source, target) in Relations(boxes))
        {
            DrawRelation(builder, source, target, padding);
        }

        return builder.Build();
    }

    static Swimlane FindOrAddSwimlane(List<Swimlane> swimlanes, EventModelingFrame frame)
    {
        var group = frame.Kind switch
        {
            EventModelingEntityKind.Ui or EventModelingEntityKind.Processor => 0,
            EventModelingEntityKind.Command or EventModelingEntityKind.ReadModel => 1,
            _ => 2
        };

        // Mermaid means each namespace and kind to have a lane of its own, but as written it opens a new lane
        // for every namespaced frame; the lane is shared here, as its documentation describes.
        var frameNamespace = frame.Namespace;
        var existing = swimlanes.FirstOrDefault(_ => _.Group == group && _.Namespace == frameNamespace);
        if (existing is not null)
        {
            return existing;
        }

        var label = (group, frameNamespace) switch
        {
            (0, null) => "UI/Automation",
            (1, null) => "Command/Read Model",
            (2, null) => "Events",
            (0, _) => "UI/A: " + frameNamespace,
            (1, _) => "C/RM: " + frameNamespace,
            _ => "Stream: " + frameNamespace
        };

        var swimlane = new Swimlane(group, frameNamespace, label, swimlanes.Count);
        swimlanes.Add(swimlane);
        return swimlane;
    }

    /// <summary>
    /// The text in a frame's box: its name in bold, wrapped to the widest a box gets, then after a blank
    /// line any data it has (inline, or from the data block it refers to) in a monospaced font.
    /// </summary>
    static Content MeasureContent(EventModelingModel model, EventModelingFrame frame, double fontSize, double lineHeight)
    {
        var nameLines = Wrap(frame.Name, (int) Math.Floor(textMaxWidth / (fontSize * boldCharWidthFactor)));

        string? data = null;
        if (frame.DataReference is { } reference)
        {
            data = model.DataBlocks.FirstOrDefault(_ => _.Name == reference)?.Content;
        }

        data ??= frame.InlineData;

        var dataLines = new List<string>();
        if (data is not null)
        {
            var maxChars = (int) Math.Floor(textMaxWidth / (fontSize * monospaceCharWidthFactor));
            foreach (var line in data.Split('\n'))
            {
                dataLines.AddRange(Wrap(line.TrimEnd(), maxChars, keepIndentation: true));
            }
        }

        var nameWidth = nameLines.Max(static _ => _.Length) * fontSize * boldCharWidthFactor;
        var dataWidth = dataLines.Select(static _ => _.Length).DefaultIfEmpty(0).Max() * fontSize * monospaceCharWidthFactor;
        var lineCount = nameLines.Count + (dataLines.Count > 0 ? dataLines.Count + 1 : 0);
        var height = (lineCount - 1) * lineHeight + fontSize * 1.2;
        return new(nameLines, dataLines, Math.Max(nameWidth, dataWidth), height);
    }

    /// <summary>
    /// Greedily wraps text at spaces to lines of at most <paramref name="maxChars"/>, splitting a word that
    /// is longer than a whole line. With <paramref name="keepIndentation"/> a line that fits is left as it
    /// is, leading spaces included, as data is laid out.
    /// </summary>
    static List<string> Wrap(string text, int maxChars, bool keepIndentation = false)
    {
        maxChars = Math.Max(1, maxChars);
        if (text.Length <= maxChars)
        {
            return [keepIndentation ? text : text.Trim()];
        }

        var lines = new List<string>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;
            while (rest.Length > 0)
            {
                if (lines.Count > 0 &&
                    lines[^1].Length + 1 + rest.Length <= maxChars)
                {
                    lines[^1] += " " + rest;
                    break;
                }

                var piece = rest.Length > maxChars ? rest[..maxChars] : rest;
                lines.Add(piece);
                rest = rest[piece.Length..];
            }
        }

        return lines.Count == 0 ? [""] : lines;
    }

    /// <summary>
    /// The arrows to draw. A frame with <c>-&gt;&gt;</c> sources is connected from those alone. Otherwise,
    /// as Mermaid infers it, a frame follows from the nearest frame before it that is in a different lane,
    /// except for the first frame and for a reset frame, which follow from nothing. Mermaid only finds a
    /// source written before the frame naming it; one written after is found here too.
    /// </summary>
    static IEnumerable<(Box source, Box target)> Relations(List<Box> boxes)
    {
        for (var i = 0; i < boxes.Count; i++)
        {
            var target = boxes[i];
            var frame = target.Frame;
            if (frame.SourceFrameIds.Count > 0)
            {
                foreach (var sourceId in frame.SourceFrameIds.Distinct())
                {
                    var source = boxes.FirstOrDefault(_ => _.Frame.Id == sourceId);
                    if (source is not null &&
                        source != target)
                    {
                        yield return (source, target);
                    }
                }

                continue;
            }

            if (i == 0 ||
                frame.IsReset)
            {
                continue;
            }

            for (var j = i - 1; j >= 0; j--)
            {
                if (boxes[j].Swimlane != target.Swimlane)
                {
                    yield return (boxes[j], target);
                    break;
                }
            }
        }
    }

    static void DrawBox(SvgBuilder builder, Box box, double padding, double fontSize, double lineHeight, string fontFamily)
    {
        var x = padding + box.X;
        var y = padding + box.Swimlane.Y + swimlanePadding;
        var visual = Visual(box.Frame.Kind);
        builder.AddRect(x, y, box.Width, box.Height, rx: 3, fill: visual.Fill, stroke: visual.Stroke);

        // The text is centred in the box as a block; the name is centred across the box and the data starts
        // at its inner left edge, as Mermaid's foreignObject lays them out.
        var content = box.Content;
        var lineCount = content.NameLines.Count + (content.DataLines.Count > 0 ? content.DataLines.Count + 1 : 0);
        var firstY = y + box.Height / 2 - (lineCount - 1) * lineHeight / 2;
        var centerX = x + box.Width / 2;
        for (var i = 0; i < content.NameLines.Count; i++)
        {
            builder.AddText(
                centerX,
                firstY + i * lineHeight,
                content.NameLines[i],
                anchor: "middle",
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: fontFamily,
                fontWeight: "bold",
                fill: textColor);
        }

        var dataX = x + boxPadding + boxTextPadding;
        for (var i = 0; i < content.DataLines.Count; i++)
        {
            // Spaces are made non-breaking, as Mermaid does, so the data's indentation survives.
            builder.AddText(
                dataX,
                firstY + (content.NameLines.Count + 1 + i) * lineHeight,
                content.DataLines[i].Replace(' ', nonBreakingSpace),
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: monospaceFont,
                fill: textColor);
        }
    }

    /// <summary>
    /// A straight line from two thirds of the way along the source box to a third of the way along the
    /// target, leaving from whichever edge faces the target, as Mermaid draws it. Two boxes in the same lane
    /// (which only <c>-&gt;&gt;</c> can connect) are joined side to side instead, where Mermaid's line would
    /// cross both of them.
    /// </summary>
    static void DrawRelation(SvgBuilder builder, Box source, Box target, double padding)
    {
        var sourceTop = padding + source.Swimlane.Y + swimlanePadding;
        var targetTop = padding + target.Swimlane.Y + swimlanePadding;
        double sourceX;
        double sourceY;
        double targetX;
        double targetY;

        if (source.Swimlane == target.Swimlane)
        {
            var rightwards = target.X >= source.X;
            sourceX = padding + source.X + (rightwards ? source.Width : 0);
            targetX = padding + target.X + (rightwards ? 0 : target.Width);
            sourceY = sourceTop + source.Height / 2;
            targetY = targetTop + target.Height / 2;
        }
        else
        {
            sourceX = padding + source.X + source.Width * 2 / 3;
            targetX = padding + target.X + target.Width / 3;
            if (source.Swimlane.Y > target.Swimlane.Y)
            {
                sourceY = sourceTop;
                targetY = targetTop + target.Height;
            }
            else
            {
                sourceY = sourceTop + source.Height;
                targetY = targetTop;
            }
        }

        builder.AddPath(
            string.Create(CultureInfo.InvariantCulture, $"M{sourceX:0.##} {sourceY:0.##} L{targetX:0.##} {targetY:0.##}"),
            fill: "none",
            stroke: relationStroke,
            strokeWidth: 1,
            cssClass: "em-relation");
        AddArrowhead(builder, sourceX, sourceY, targetX, targetY);
    }

    /// <summary>
    /// Mermaid's arrowhead marker, a triangle 10 long and 7 wide with its tip on the end of the line, drawn
    /// as a path rather than a marker.
    /// </summary>
    static void AddArrowhead(SvgBuilder builder, double fromX, double fromY, double tipX, double tipY)
    {
        var deltaX = tipX - fromX;
        var deltaY = tipY - fromY;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length == 0)
        {
            return;
        }

        var unitX = deltaX / length;
        var unitY = deltaY / length;
        const double size = 10;
        const double halfWidth = 3.5;
        var baseX = tipX - unitX * size;
        var baseY = tipY - unitY * size;
        builder.AddPath(
            string.Create(
                CultureInfo.InvariantCulture,
                $"M{tipX:0.##} {tipY:0.##} L{baseX - unitY * halfWidth:0.##} {baseY + unitX * halfWidth:0.##} L{baseX + unitY * halfWidth:0.##} {baseY - unitX * halfWidth:0.##} Z"),
            fill: arrowheadFill);
    }

    /// <summary>Mermaid's default fill and stroke for each kind of entity, the colours of the event modeling cheat sheet.</summary>
    static (string Fill, string Stroke) Visual(EventModelingEntityKind kind) =>
        kind switch
        {
            EventModelingEntityKind.Ui => ("white", "#dbdada"),
            EventModelingEntityKind.Processor => ("#edb3f6", "#b88cbf"),
            EventModelingEntityKind.ReadModel => ("#d3f1a2", "#a3b732"),
            EventModelingEntityKind.Command => ("#bcd6fe", "#679ac3"),
            _ => ("#ffb778", "#c19a0f")
        };

    sealed class Swimlane(int group, string? laneNamespace, string label, int order)
    {
        /// <summary>0 for UI/Automation, 1 for Command/Read Model, 2 for Events.</summary>
        public int Group { get; } = group;

        public string? Namespace { get; } = laneNamespace;

        public string Label { get; } = label;

        /// <summary>The order the lane was first seen in, which orders a kind's namespaced lanes.</summary>
        public int Order { get; } = order;

        public double MaxBoxHeight { get; set; }

        public double Y { get; set; }

        public double Height { get; set; }

        /// <summary>The right edge of the last box placed in the lane, if any.</summary>
        public double? Right { get; set; }
    }

    sealed record Content(List<string> NameLines, List<string> DataLines, double Width, double Height);

    sealed class Box(EventModelingFrame frame, Swimlane swimlane, Content content, double width, double height)
    {
        public EventModelingFrame Frame { get; } = frame;

        public Swimlane Swimlane { get; } = swimlane;

        public Content Content { get; } = content;

        public double Width { get; } = width;

        public double Height { get; } = height;

        public double X { get; set; }
    }
}
