namespace Naiad.Diagrams.Railroad;

/// <summary>
/// Draws each rule as a railroad track, one under another, laid out as Mermaid lays them out: the rule's
/// name, a start dot, the definition and an end dot on one line. Terminals are rounded boxes, references to
/// rules square boxes and special sequences dashed boxes. A choice stacks its alternatives centred around the
/// track, an optional item gets a bypass above it and a repetition a loop back below it (and a bypass above
/// when it may be skipped). Mermaid measures its labels in the DOM; here they are estimated from their
/// character counts.
/// </summary>
public class RailroadRenderer : IDiagramRenderer<RailroadModel>
{
    // Mermaid's railroad defaults.
    const double boxPadding = 10;
    const double verticalSeparation = 8;
    const double horizontalSeparation = 10;
    const double arcRadius = 10;
    const double markerRadius = 5;
    const double strokeWidth = 2;
    const double terminalCornerRadius = 10;

    const double charWidthFactor = 0.55;
    const double boldCharWidthFactor = 0.6;
    const double textHeightFactor = 1.2;

    // Mermaid's railroad colours under its default theme, which it derives from the theme variables:
    // terminals from the secondary colour, non-terminals from the primary one and special sequences from the
    // tertiary one, with borders darkened as Mermaid's mkBorder does.
    const string terminalFill = "#FFFFDE";
    const string terminalStroke = "#EEEEBC";
    const string terminalTextColor = "#000021";
    const string nonTerminalFill = "#ECECFF";
    const string nonTerminalStroke = "#C7C7F1";
    const string nonTerminalTextColor = "#131300";
    const string specialFill = "#F9FFEC";
    const string specialStroke = "#E3F1C7";
    const string lineColor = "#333333";
    const string ruleNameColor = "#333";

    public SvgDocument Render(RailroadModel model, RenderOptions options)
    {
        var layout = new Layout(options);
        var builder = new SvgBuilder();
        builder.Options(options);

        var titleFontSize = options.FontSize + 4;
        var hasTitle = !string.IsNullOrEmpty(model.Title);
        var titleHeight = hasTitle ? titleFontSize * textHeightFactor + verticalSeparation * 2 : 0;
        var titleWidth = hasTitle ? model.Title!.Length * titleFontSize * boldCharWidthFactor : 0;

        var rows = model.Rules.Select(layout.MeasureRule).ToList();
        var contentWidth = rows.Count == 0 ? 0 : rows.Max(static _ => _.Width);
        var contentHeight = rows.Sum(static _ => _.Height) + Math.Max(0, rows.Count - 1) * verticalSeparation;

        var width = Math.Max(Math.Max(contentWidth, titleWidth), rows.Count == 0 ? 200 : 0) + options.Padding * 2;
        var height = Math.Max(titleHeight + contentHeight, rows.Count == 0 && !hasTitle ? 100 : 0) + options.Padding * 2;
        builder.Size(width, height);

        if (hasTitle)
        {
            builder.AddText(
                width / 2,
                options.Padding + titleFontSize * textHeightFactor / 2,
                model.Title!,
                anchor: "middle",
                baseline: "middle",
                fontSize: titleFontSize,
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: ruleNameColor);
        }

        var top = options.Padding + titleHeight;
        foreach (var row in rows)
        {
            layout.DrawRule(builder, row, options.Padding, top);
            top += row.Height + verticalSeparation;
        }

        return builder.Build();
    }

    /// <summary>How far an expression reaches: its width, and its height above and below its track.</summary>
    readonly record struct Extent(double Width, double Up, double Down)
    {
        public double Height => Up + Down;
    }

    /// <summary>
    /// A rule laid out as Mermaid lays it out: its name, then 20 further along the start dot, then 20 further
    /// along the definition, then 10 further along the end dot. The track sits at least 20 below the top.
    /// </summary>
    sealed record Row(RailroadRule Rule, string Label, double NameWidth, Extent Definition)
    {
        public double TrackOffset => Math.Max(20, Definition.Up);

        public double DefinitionX => NameWidth + 20;

        public double Width => DefinitionX + Definition.Width + 10 + markerRadius;

        public double Height => Math.Max(40, TrackOffset + Definition.Down + boxPadding * 2);
    }

    sealed class Layout(RenderOptions options)
    {
        readonly Dictionary<RailroadExpression, Extent> extents = new(ReferenceEqualityComparer.Instance);

        double FontSize => options.FontSize;

        double TextWidth(string text, bool bold = false) =>
            text.Length * FontSize * (bold ? boldCharWidthFactor : charWidthFactor);

        double TextHeight => FontSize * textHeightFactor;

        public Row MeasureRule(RailroadRule rule)
        {
            var label = rule.Name + " =";
            return new(rule, label, TextWidth(label, bold: true) + 20, Measure(rule.Definition));
        }

        public void DrawRule(SvgBuilder builder, Row row, double left, double top)
        {
            var trackY = top + row.TrackOffset;
            var startX = left + row.NameWidth;
            var definitionX = left + row.DefinitionX;
            var definitionEnd = definitionX + row.Definition.Width;

            // Mermaid sets the name on the track's baseline, so it reads as sitting on the line.
            builder.AddText(
                left,
                trackY,
                row.Label,
                fontSize: FontSize,
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: ruleNameColor);

            builder.AddCircle(startX, trackY, markerRadius, fill: lineColor);
            Line(builder, startX + markerRadius, trackY, definitionX);
            Draw(builder, row.Rule.Definition, definitionX, trackY);
            Line(builder, definitionEnd, trackY, definitionEnd + 10 - markerRadius);
            builder.AddCircle(definitionEnd + 10, trackY, markerRadius, fill: lineColor);
        }

        Extent Measure(RailroadExpression expression)
        {
            if (extents.TryGetValue(expression, out var known))
            {
                return known;
            }

            var extent = expression switch
            {
                RailroadTerminal terminal => BoxExtent(terminal.Text),
                RailroadNonTerminal nonTerminal => BoxExtent(nonTerminal.Name),
                RailroadSpecial special => BoxExtent(SpecialText(special)),
                RailroadSequence sequence => MeasureSequence(sequence),
                RailroadChoice choice => MeasureChoice(choice),
                RailroadOptional optional => MeasureOptional(optional),
                RailroadRepetition repetition => MeasureRepetition(repetition),
                _ => throw new MermaidException($"Unknown railroad expression: {expression.GetType().Name}")
            };

            extents[expression] = extent;
            return extent;
        }

        Extent BoxExtent(string text)
        {
            var height = TextHeight + boxPadding * 2;
            return new(TextWidth(text) + boxPadding * 2, height / 2, height / 2);
        }

        static string SpecialText(RailroadSpecial special) => $"? {special.Text} ?";

        Extent MeasureSequence(RailroadSequence sequence)
        {
            var items = sequence.Items.Select(Measure).ToList();
            return new(
                items.Sum(static _ => _.Width) + Math.Max(0, items.Count - 1) * horizontalSeparation,
                items.Max(static _ => _.Up),
                items.Max(static _ => _.Down));
        }

        /// <summary>The alternatives are stacked, and the track runs through the middle of the stack.</summary>
        Extent MeasureChoice(RailroadChoice choice)
        {
            var alternatives = choice.Alternatives.Select(Measure).ToList();
            var height = alternatives.Sum(static _ => _.Height) + (alternatives.Count - 1) * verticalSeparation;
            return new(alternatives.Max(static _ => _.Width) + arcRadius * 4, height / 2, height / 2);
        }

        Extent MeasureOptional(RailroadOptional optional)
        {
            var item = Measure(optional.Item);
            return new(item.Width + arcRadius * 4, item.Up + arcRadius * 2, item.Down);
        }

        Extent MeasureRepetition(RailroadRepetition repetition)
        {
            var item = Measure(repetition.Item);
            var width = item.Width + arcRadius * 4;
            var down = item.Down + arcRadius * 2;
            if (RepetitionLabel(repetition) is { } label)
            {
                width = Math.Max(width, TextWidth(label) + arcRadius * 2);
                down += TextHeight;
            }

            return new(width, item.Up + (repetition.Min == 0 ? arcRadius * 2 : 0), down);
        }

        /// <summary>
        /// The bounds of a repetition other than zero-or-more and one-or-more, which only ABNF can write. Mermaid
        /// draws those as plain loops, which leaves <c>2*4DIGIT</c> looking like <c>1*DIGIT</c>, so the count is
        /// written under the loop.
        /// </summary>
        static string? RepetitionLabel(RailroadRepetition repetition) =>
            (repetition.Min, repetition.Max) switch
            {
                (0 or 1, null) => null,
                (var min, null) => $"{min} or more times",
                (var min, { } max) when min == max => min == 1 ? "once" : $"{min} times",
                (0, { } max) => $"up to {max} times",
                (var min, { } max) => $"{min} to {max} times"
            };

        /// <summary>Draws an expression with its track entering on the left at (<paramref name="x"/>, <paramref name="y"/>).</summary>
        void Draw(SvgBuilder builder, RailroadExpression expression, double x, double y)
        {
            switch (expression)
            {
                case RailroadTerminal terminal:
                    DrawBox(builder, terminal.Text, x, y, terminalCornerRadius, terminalFill, terminalStroke, terminalTextColor, null);
                    break;
                case RailroadNonTerminal nonTerminal:
                    DrawBox(builder, nonTerminal.Name, x, y, 0, nonTerminalFill, nonTerminalStroke, nonTerminalTextColor, null);
                    break;
                case RailroadSpecial special:
                    DrawBox(builder, SpecialText(special), x, y, 0, specialFill, specialStroke, nonTerminalTextColor, "5,3");
                    break;
                case RailroadSequence sequence:
                    DrawSequence(builder, sequence, x, y);
                    break;
                case RailroadChoice choice:
                    DrawChoice(builder, choice, x, y);
                    break;
                case RailroadOptional optional:
                    DrawOptional(builder, optional, x, y);
                    break;
                case RailroadRepetition repetition:
                    DrawRepetition(builder, repetition, x, y);
                    break;
            }
        }

        void DrawBox(SvgBuilder builder, string text, double x, double y, double cornerRadius, string fill, string stroke, string textColor, string? dash)
        {
            var extent = BoxExtent(text);
            var top = y - extent.Up;
            if (dash is null)
            {
                builder.AddRect(x, top, extent.Width, extent.Height, rx: cornerRadius, fill: fill, stroke: stroke, strokeWidth: strokeWidth);
            }
            else
            {
                // A rect cannot be dashed through the builder, so the dashed outline is a path over a plain fill.
                builder.AddRect(x, top, extent.Width, extent.Height, fill: fill);
                builder.AddPath(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"M {x:0.##} {top:0.##} H {x + extent.Width:0.##} V {top + extent.Height:0.##} H {x:0.##} Z"),
                    fill: "none",
                    stroke: stroke,
                    strokeWidth: strokeWidth,
                    strokeDasharray: dash);
            }

            builder.AddText(
                x + extent.Width / 2,
                y,
                text,
                anchor: "middle",
                baseline: "middle",
                fontSize: FontSize,
                fontFamily: options.FontFamily,
                fill: textColor);
        }

        void DrawSequence(SvgBuilder builder, RailroadSequence sequence, double x, double y)
        {
            for (var i = 0; i < sequence.Items.Count; i++)
            {
                var item = sequence.Items[i];
                var width = Measure(item).Width;
                Draw(builder, item, x, y);
                x += width;
                if (i < sequence.Items.Count - 1)
                {
                    Line(builder, x, y, x + horizontalSeparation);
                    x += horizontalSeparation;
                }
            }
        }

        /// <summary>
        /// Each alternative is centred in the width of the widest, and joined to the track on either side by a
        /// curve; one that sits exactly on the track is joined by a straight line.
        /// </summary>
        void DrawChoice(SvgBuilder builder, RailroadChoice choice, double x, double y)
        {
            var extent = Measure(choice);
            var innerWidth = extent.Width - arcRadius * 4;
            var right = x + extent.Width;
            var top = y - extent.Up;

            foreach (var alternative in choice.Alternatives)
            {
                var alternativeExtent = Measure(alternative);
                var alternativeY = top + alternativeExtent.Up;
                var alternativeX = x + arcRadius * 2 + (innerWidth - alternativeExtent.Width) / 2;
                var alternativeRight = alternativeX + alternativeExtent.Width;

                Draw(builder, alternative, alternativeX, alternativeY);

                var entry = new PathBuilder()
                    .MoveTo(x, y)
                    .Curve(x, y, alternativeY)
                    .LineTo(alternativeX, alternativeY);
                builder.AddPath(entry.Build(), fill: "none", stroke: lineColor, strokeWidth: strokeWidth);

                // The exit bend starts where it will end exactly on the right edge.
                var curveWidth = CurveWidth(y, alternativeY);
                var exit = new PathBuilder()
                    .MoveTo(alternativeRight, alternativeY)
                    .LineTo(right - curveWidth, alternativeY)
                    .Curve(right - curveWidth, alternativeY, y);
                builder.AddPath(exit.Build(), fill: "none", stroke: lineColor, strokeWidth: strokeWidth);

                top += alternativeExtent.Height + verticalSeparation;
            }
        }

        void DrawOptional(SvgBuilder builder, RailroadOptional optional, double x, double y)
        {
            var extent = Measure(optional);
            var item = Measure(optional.Item);
            var itemX = x + arcRadius * 2;
            var right = x + extent.Width;

            Line(builder, x, y, itemX);
            Draw(builder, optional.Item, itemX, y);
            Line(builder, itemX + item.Width, y, right);
            DrawBypass(builder, x, right, y, y - item.Up - arcRadius * 2);
        }

        /// <summary>
        /// The loop leaves the right of the item, drops below it, runs back and rejoins the track on its left.
        /// A repetition that may be skipped altogether also has a bypass above it.
        /// </summary>
        void DrawRepetition(SvgBuilder builder, RailroadRepetition repetition, double x, double y)
        {
            var extent = Measure(repetition);
            var item = Measure(repetition.Item);
            var itemX = x + (extent.Width - item.Width) / 2;
            var itemRight = itemX + item.Width;
            var right = x + extent.Width;

            Line(builder, x, y, itemX);
            Draw(builder, repetition.Item, itemX, y);
            Line(builder, itemRight, y, right);

            var loopY = y + item.Down + arcRadius;
            var loop = new PathBuilder()
                .MoveTo(itemRight, y)
                .Arc(true, itemRight + arcRadius, y + arcRadius)
                .LineTo(itemRight + arcRadius, loopY)
                .Arc(true, itemRight, loopY + arcRadius)
                .LineTo(itemX, loopY + arcRadius)
                .Arc(true, itemX - arcRadius, loopY)
                .LineTo(itemX - arcRadius, y + arcRadius)
                .Arc(true, itemX, y);
            builder.AddPath(loop.Build(), fill: "none", stroke: lineColor, strokeWidth: strokeWidth);

            if (RepetitionLabel(repetition) is { } label)
            {
                builder.AddText(
                    x + extent.Width / 2,
                    loopY + arcRadius + TextHeight / 2 + 2,
                    label,
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: FontSize * 0.85,
                    fontFamily: options.FontFamily,
                    fontWeight: null,
                    fill: ruleNameColor,
                    style: "font-style:italic");
            }

            if (repetition.Min == 0)
            {
                DrawBypass(builder, x, right, y, y - item.Up - arcRadius * 2);
            }
        }

        /// <summary>A track that rises from (<paramref name="left"/>, <paramref name="y"/>), runs along at <paramref name="topY"/> and drops back at <paramref name="right"/>.</summary>
        static void DrawBypass(SvgBuilder builder, double left, double right, double y, double topY)
        {
            var path = new PathBuilder()
                .MoveTo(left, y)
                .Curve(left, y, topY)
                .LineTo(right - arcRadius * 2, topY)
                .Curve(right - arcRadius * 2, topY, y);
            builder.AddPath(path.Build(), fill: "none", stroke: lineColor, strokeWidth: strokeWidth);
        }

        static void Line(SvgBuilder builder, double fromX, double y, double toX)
        {
            if (toX > fromX)
            {
                builder.AddPath(
                    new PathBuilder().MoveTo(fromX, y).LineTo(toX, y).Build(),
                    fill: "none",
                    stroke: lineColor,
                    strokeWidth: strokeWidth);
            }
        }
    }

    /// <summary>How far right a <see cref="PathBuilder.Curve"/> between the two heights reaches.</summary>
    static double CurveWidth(double fromY, double toY) =>
        Math.Min(arcRadius, Math.Abs(toY - fromY) / 2) * 2;

    sealed class PathBuilder
    {
        StringBuilder data = new();

        public PathBuilder MoveTo(double x, double y) => Append($"M {x:0.##} {y:0.##} ");

        public PathBuilder LineTo(double x, double y) => Append($"L {x:0.##} {y:0.##} ");

        /// <summary>A quarter-circle arc of the standard radius, clockwise when <paramref name="clockwise"/>.</summary>
        public PathBuilder Arc(bool clockwise, double x, double y) => Arc(arcRadius, clockwise, x, y);

        PathBuilder Arc(double radius, bool clockwise, double x, double y) =>
            Append($"A {radius:0.##} {radius:0.##} 0 0 {(clockwise ? 1 : 0)} {x:0.##} {y:0.##} ");

        /// <summary>
        /// An S-bend heading right from (<paramref name="x"/>, <paramref name="fromY"/>), which the path must
        /// be at, to the height <paramref name="toY"/>: a quarter turn, a vertical run and a quarter turn back.
        /// Heights closer than two radii apart get tighter turns so the bend does not double back; it then ends
        /// short of two radii along, as <see cref="CurveWidth"/> says.
        /// </summary>
        public PathBuilder Curve(double x, double fromY, double toY)
        {
            if (toY == fromY)
            {
                return this;
            }

            var radius = Math.Min(arcRadius, Math.Abs(toY - fromY) / 2);
            var down = toY > fromY;
            var direction = down ? 1 : -1;
            Arc(radius, down, x + radius, fromY + radius * direction);
            Append($"L {x + radius:0.##} {toY - radius * direction:0.##} ");
            return Arc(radius, !down, x + radius * 2, toY);
        }

        PathBuilder Append(FormattableString text)
        {
            data.Append(text.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public string Build() => data.ToString().TrimEnd();
    }
}
