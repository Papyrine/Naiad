namespace Naiad;

/// <summary>How one part of a node's shape is painted, given the node's fill and stroke.</summary>
enum ShapePaint
{
    /// <summary>Filled with the node's fill and outlined with its stroke.</summary>
    Body,

    /// <summary>Filled, with no outline: the backing behind a brace.</summary>
    Fill,

    /// <summary>Outlined, with no fill: a brace, a rim, a rule.</summary>
    Stroke,

    /// <summary>Filled and outlined in the node's stroke colour: a junction, a window's buttons.</summary>
    Solid,

    /// <summary>Neither filled nor outlined unless the node's own style asks for it: a text block.</summary>
    Hidden
}

/// <summary>One path of a node's shape, and how it is painted.</summary>
readonly record struct ShapePart(string Path, ShapePaint Paint = ShapePaint.Body, double? Opacity = null);

/// <summary>
/// Draws the node shapes of Mermaid's flowcharts. The geometry follows Mermaid 12's shapes; the sizes keep
/// Naiad's usual padding around a label and grow by whatever a shape adds beside it, the way Mermaid's do.
/// </summary>
public static class ShapePathGenerator
{
    static readonly CultureInfo culture = CultureInfo.InvariantCulture;

    // The padding Mermaid gives a flowchart label. Shapes that place a feature beside the label (a brace, a
    // triangle's sides) keep it that far away.
    const double labelPadding = 15;

    // Naiad's padding around a label, across and down.
    const double paddingWidth = 30;
    const double paddingHeight = 27;

    static string Rectangle(double x, double y, double width, double height, double rx = 0)
    {
        if (rx > 0)
        {
            return string.Create(
                culture,
                $"""
                 M{x + rx:0.##},{y:0.##}
                 H{x + width - rx:0.##}
                 Q{x + width:0.##},{y:0.##} {x + width:0.##},{y + rx:0.##}
                 V{y + height - rx:0.##}
                 Q{x + width:0.##},{y + height:0.##} {x + width - rx:0.##},{y + height:0.##}
                 H{x + rx:0.##}
                 Q{x:0.##},{y + height:0.##} {x:0.##},{y + height - rx:0.##}
                 V{y + rx:0.##}
                 Q{x:0.##},{y:0.##} {x + rx:0.##},{y:0.##} Z
                 """);
        }

        return string.Create(
            culture,
            $"M{x:0.##},{y:0.##} H{x + width:0.##} V{y + height:0.##} H{x:0.##} Z");
    }

    static string Circle(double cx, double cy, double r) =>
        string.Create(
            culture,
            $"""
             M{cx:0.##},{cy - r:0.##}
             A{r:0.##},{r:0.##} 0 1 1 {cx:0.##},{cy + r:0.##}
             A{r:0.##},{r:0.##} 0 1 1 {cx:0.##},{cy - r:0.##} Z
             """);

    static string Diamond(double cx, double cy, double width, double height)
    {
        var w2 = width / 2;
        var h2 = height / 2;
        return string.Create(
            culture,
            $"""
             M{cx:0.##},{cy - h2:0.##}
             L{cx + w2:0.##},{cy:0.##}
             L{cx:0.##},{cy + h2:0.##}
             L{cx - w2:0.##},{cy:0.##} Z
             """);
    }

    static string Hexagon(double cx, double cy, double width, double height)
    {
        var w4 = width / 4;
        var w2 = width / 2;
        var h2 = height / 2;
        return string.Create(
            culture,
            $"""
             M{cx - w4:0.##},{cy - h2:0.##}
             L{cx + w4:0.##},{cy - h2:0.##}
             L{cx + w2:0.##},{cy:0.##}
             L{cx + w4:0.##},{cy + h2:0.##}
             L{cx - w4:0.##},{cy + h2:0.##}
             L{cx - w2:0.##},{cy:0.##} Z
             """);
    }

    static string Stadium(double x, double y, double width, double height)
    {
        var r = height / 2;
        return string.Create(
            culture,
            $"""
             M{x + r:0.##},{y:0.##}
             H{x + width - r:0.##}
             A{r:0.##},{r:0.##} 0 0 1 {x + width - r:0.##},{y + height:0.##}
             H{x + r:0.##}
             A{r:0.##},{r:0.##} 0 0 1 {x + r:0.##},{y:0.##} Z
             """);
    }

    static string Cylinder(double x, double y, double width, double height)
    {
        var ry = height * 0.1;
        var bodyHeight = height - ry * 2;
        var rx = width / 2;
        return string.Create(
            culture,
            $"""
             M{x:0.##},{y + ry:0.##}
             A{rx:0.##},{ry:0.##} 0 0 1 {x + width:0.##},{y + ry:0.##}
             V{y + ry + bodyHeight:0.##}
             A{rx:0.##},{ry:0.##} 0 0 1 {x:0.##},{y + ry + bodyHeight:0.##} V{y + ry:0.##}
             Z M{x:0.##},{y + ry:0.##}
             A{rx:0.##},{ry:0.##} 0 0 0 {x + width:0.##},{y + ry:0.##}
             """);
    }

    /// <summary>
    /// How far the slanted sides of a parallelogram or trapezoid lean across its height: half of it, as in
    /// Mermaid. The node is widened by twice this, so the label clears the slant at every corner.
    /// </summary>
    internal static double SlantOffset(double height) =>
        height / 2;

    static string Parallelogram(double x, double y, double width, double height)
    {
        var offset = SlantOffset(height);
        return string.Create(
            culture,
            $"""
             M{x + offset:0.##},{y:0.##}
             L{x + width:0.##},{y:0.##}
             L{x + width - offset:0.##},{y + height:0.##}
             L{x:0.##},{y + height:0.##} Z
             """);
    }

    static string ParallelogramAlt(double x, double y, double width, double height)
    {
        var offset = SlantOffset(height);
        return string.Create(
            culture,
            $"""
             M{x:0.##},{y:0.##}
             L{x + width - offset:0.##},{y:0.##}
             L{x + width:0.##},{y + height:0.##}
             L{x + offset:0.##},{y + height:0.##} Z
             """);
    }

    static string Trapezoid(double x, double y, double width, double height)
    {
        var offset = SlantOffset(height);
        return string.Create(
            culture,
            $"""
             M{x + offset:0.##},{y:0.##}
             L{x + width - offset:0.##},{y:0.##}
             L{x + width:0.##},{y + height:0.##}
             L{x:0.##},{y + height:0.##} Z
             """);
    }

    static string TrapezoidAlt(double x, double y, double width, double height)
    {
        var offset = SlantOffset(height);
        return string.Create(
            culture,
            $"""
             M{x:0.##},{y:0.##}
             L{x + width:0.##},{y:0.##}
             L{x + width - offset:0.##},{y + height:0.##} {x + offset:0.##},{y + height:0.##} Z
             """);
    }

    /// <summary>
    /// Mermaid's <c>rect_left_inv_arrow</c>: the top- and bottom-left corners are the leftmost points and
    /// the mid-left vertex is pushed back in between them, so the left edge is a notch cut into the box —
    /// not a point sticking out of it.
    /// </summary>
    static string Asymmetric(double x, double y, double width, double height)
    {
        var notch = AsymmetricNotch(height);
        return string.Create(
            culture,
            $"""
             M{x:0.##},{y:0.##}
             L{x + width:0.##},{y:0.##}
             L{x + width:0.##},{y + height:0.##}
             L{x:0.##},{y + height:0.##}
             L{x + notch:0.##},{y + height / 2:0.##} Z
             """);
    }

    /// <summary>
    /// How far <see cref="NodeShape.Asymmetric"/>'s left notch reaches into the box. Callers size the node
    /// so the label clears it.
    /// </summary>
    public static double AsymmetricNotch(double height) =>
        height / 2;

    /// <summary>
    /// How far <see cref="NodeShape.Subroutine"/>'s vertical bars sit in from each side. Fixed rather than a
    /// share of the width, so a wide node does not push its bars further across its own label.
    /// </summary>
    public const double SubroutineBarInset = 8;

    static string Subroutine(double x, double y, double width, double height)
    {
        const double inset = SubroutineBarInset;
        return string.Create(
            culture,
            $"""
             M{x:0.##},{y:0.##}
             H{x + width:0.##}
             V{y + height:0.##} H{x:0.##}
             Z
             M{x + inset:0.##},{y:0.##}
             V{y + height:0.##}
             M{x + width - inset:0.##},{y:0.##}
             V{y + height:0.##}
             """);
    }

    static string DoubleCircle(double cx, double cy, double r)
    {
        var innerR = r * 0.85;
        return $"{Circle(cx, cy, r)} {Circle(cx, cy, innerR)}";
    }

    public static string GetPath(NodeShape shape, double x, double y, double width, double height)
    {
        var parts = GetParts(shape, x, y, width, height);
        if (parts.Length == 1)
        {
            return parts[0].Path;
        }

        return string.Join(" ", parts.Select(_ => _.Path));
    }

    /// <summary>
    /// The paths that draw a shape in the box at (x, y), in painting order. Most shapes are one filled and
    /// outlined path; some put a stroke-only brace over a fill-only backing, or stack a front sheet over the
    /// ones behind it.
    /// </summary>
    internal static ShapePart[] GetParts(NodeShape shape, double x, double y, double width, double height)
    {
        var cx = x + width / 2;
        var cy = y + height / 2;
        var r = Math.Min(width, height) / 2;

        return shape switch
        {
            NodeShape.Rectangle or NodeShape.ForkJoin => [new(Rectangle(x, y, width, height))],
            NodeShape.RoundedRectangle => [new(Rectangle(x, y, width, height, 5))],
            NodeShape.Circle => [new(Circle(cx, cy, r))],
            NodeShape.DoubleCircle => [new(DoubleCircle(cx, cy, r))],
            NodeShape.Diamond => [new(Diamond(cx, cy, width, height))],
            NodeShape.Hexagon => [new(Hexagon(cx, cy, width, height))],
            NodeShape.Stadium => [new(Stadium(x, y, width, height))],
            NodeShape.Cylinder => [new(Cylinder(x, y, width, height))],
            NodeShape.Parallelogram or NodeShape.Lean => [new(Parallelogram(x, y, width, height))],
            NodeShape.ParallelogramAlt or NodeShape.LeanAlt => [new(ParallelogramAlt(x, y, width, height))],
            NodeShape.Trapezoid => [new(Trapezoid(x, y, width, height))],
            NodeShape.TrapezoidAlt => [new(TrapezoidAlt(x, y, width, height))],
            NodeShape.Asymmetric or NodeShape.Odd => [new(Asymmetric(x, y, width, height))],
            NodeShape.Subroutine => [new(Subroutine(x, y, width, height))],
            NodeShape.Document => [new(Document(x, y, width, height))],
            NodeShape.LinedDocument => [new(LinedDocument(x, y, width, height))],
            NodeShape.TaggedDocument => TaggedDocument(x, y, width, height),
            NodeShape.MultiDocument => MultiDocument(x, y, width, height),
            NodeShape.MultiProcess or NodeShape.MultiRect => MultiProcess(x, y, width, height),
            NodeShape.NotchedRectangle or NodeShape.Card => [new(NotchedRectangle(x, y, width, height))],
            NodeShape.LinedRectangle => [new(LinedRectangle(x, y, width, height))],
            NodeShape.SmallCircle => [new(new PathData().Circle(cx, cy, r).ToString())],
            NodeShape.FramedCircle => [new(new PathData().Circle(cx, cy, r).Circle(cx, cy, r * 5 / 14).ToString())],
            NodeShape.FilledCircle => [new(new PathData().Circle(cx, cy, r).ToString(), ShapePaint.Solid)],
            NodeShape.CrossedCircle => [new(CrossedCircle(cx, cy, r))],
            NodeShape.Hourglass => [new(Hourglass(x, y, width, height))],
            NodeShape.CurlyBracket => CurlyBracket(x, y, width, height),
            NodeShape.CurlyBracketRight => CurlyBracketRight(x, y, width, height),
            NodeShape.CurlyBraces => CurlyBraces(x, y, width, height),
            NodeShape.Lightning => [new(Lightning(x, y, width, height))],
            NodeShape.HalfRoundedRectangle => [new(HalfRoundedRectangle(x, y, width, height))],
            NodeShape.TiltedCylinder => [new(TiltedCylinder(x, y, width, height))],
            NodeShape.LinedCylinder => [new(LinedCylinder(x, y, width, height))],
            NodeShape.CurvedTrapezoid => [new(CurvedTrapezoid(x, y, width, height))],
            NodeShape.DividedRectangle => [new(DividedRectangle(x, y, width, height))],
            NodeShape.TriangleUp => [new(new PathData().M(x, y + height).H(x + width).L(cx, y).Z().ToString())],
            NodeShape.TriangleDown => [new(new PathData().M(x, y).H(x + width).L(cx, y + height).Z().ToString())],
            NodeShape.WindowPane => [new(WindowPane(x, y, width, height))],
            NodeShape.NotchedPentagon => [new(NotchedPentagon(x, y, width, height))],
            NodeShape.SlopedRectangle => [new(new PathData().M(x, y + height / 3).L(x + width, y).V(y + height).H(x).Z().ToString())],
            NodeShape.Flag => [new(Flag(x, y, width, height))],
            NodeShape.BowTie => [new(BowTie(x, y, width, height))],
            NodeShape.TaggedRect => TaggedRect(x, y, width, height),
            NodeShape.Datastore => Datastore(x, y, width, height),
            NodeShape.Text => [new(Rectangle(x, y, width, height), ShapePaint.Hidden)],
            NodeShape.Folder => [new(Folder(x, y, width, height))],
            NodeShape.Bucket => Bucket(x, y, width, height),
            NodeShape.Console => [new(new PathData().RoundedBox(x, y, width, height, windowCorner).ToString())],
            NodeShape.Browser => Browser(x, y, width, height),
            NodeShape.Person => Person(x, y, width, height),
            NodeShape.Bang => [new(Bang(x, y, width, height))],
            NodeShape.Cloud => [new(Cloud(x, y, width, height))],
            _ => [new(Rectangle(x, y, width, height))]
        };
    }

    /// <summary>
    /// Whether the shape shows its label. Mermaid's small markers (start and stop circles, forks, the
    /// hourglass, the bolt, the junction and the crossed circle) drop it.
    /// </summary>
    internal static bool HasLabel(NodeShape shape) =>
        shape is not (NodeShape.SmallCircle or
            NodeShape.FramedCircle or
            NodeShape.FilledCircle or
            NodeShape.CrossedCircle or
            NodeShape.ForkJoin or
            NodeShape.Hourglass or
            NodeShape.Lightning);

    /// <summary>
    /// The size of a node of the given shape around a label of the given size. A fork lies across the
    /// flow, so it stands upright in a left-to-right chart.
    /// </summary>
    internal static Size NodeSize(NodeShape shape, Size label, Direction direction)
    {
        var width = label.Width + paddingWidth;
        var height = label.Height + paddingHeight;

        switch (shape)
        {
            case NodeShape.Circle or NodeShape.DoubleCircle:
                var diameter = Math.Max(width, height);
                return new(diameter, diameter);

            case NodeShape.Diamond:
                return new(width * 1.4, height * 1.4);

            case NodeShape.Asymmetric or NodeShape.Odd:
                // The left notch bites into the box, and the label is centred in it, so grow the box by the
                // notch's depth on both sides rather than letting it cut across the text.
                return new(width + AsymmetricNotch(height) * 2, height);

            case NodeShape.Subroutine:
                // The vertical bars are drawn inside the node, so the label needs the space between them
                // rather than the full width.
                return new(width + SubroutineBarInset * 2, height);

            case NodeShape.Parallelogram or
                NodeShape.ParallelogramAlt or
                NodeShape.Lean or
                NodeShape.LeanAlt or
                NodeShape.Trapezoid:
                // Each slanted side leans half the height across, and Mermaid gives the label half a padding
                // clear of it at the narrow corners.
                return new(label.Width + labelPadding + SlantOffset(height) * 2, height);

            case NodeShape.TrapezoidAlt:
                return new(width + SlantOffset(height) * 2, height);

            case NodeShape.SmallCircle or NodeShape.FramedCircle or NodeShape.FilledCircle:
                return new(14, 14);

            case NodeShape.CrossedCircle:
                return new(60, 60);

            case NodeShape.Hourglass:
                return new(30, 30);

            case NodeShape.Lightning:
                return new(35, 70);

            case NodeShape.ForkJoin:
                if (direction is Direction.LeftToRight or Direction.RightToLeft)
                {
                    return new(10, 70);
                }

                return new(70, 10);

            case NodeShape.Text:
                return new(label.Width + labelPadding, label.Height + labelPadding);

            case NodeShape.LinedRectangle:
                return new(width + linedRectangleFrame * 2, height);

            case NodeShape.Document or NodeShape.LinedDocument or NodeShape.TaggedDocument:
                // A wave an eighth of the height deep runs along the foot, its crest and trough both
                // outside the label's box.
                var documentHeight = height * 1.25;
                if (shape == NodeShape.Document)
                {
                    return new(width, documentHeight);
                }

                // The lined and tagged documents are a tenth wider, for the rule or the fold.
                return new(width * 1.1, documentHeight);

            case NodeShape.MultiDocument:
                // Mermaid pads the stack's label box by a further half padding, and offsets each of the
                // three sheets by 10.
                return new(width + stackOffset * 2, (height + labelPadding) * multiDocumentScale + stackOffset * 2);

            case NodeShape.MultiProcess or NodeShape.MultiRect:
                return new(width + processOffset * 2, height + processOffset * 2);

            case NodeShape.WindowPane:
                return new(width + windowPaneBand, height + windowPaneBand);

            case NodeShape.CurlyBracket or NodeShape.CurlyBracketRight or NodeShape.CurlyBraces:
                // The brace's curls reach a tenth of the label box's height (at least 5) beyond it above
                // and below, and its point the same distance out from its spine.
                var braceWidth = label.Width + labelPadding;
                var curl = Math.Max(5, height / 10);
                var curls = shape == NodeShape.CurlyBraces ? 2.5 : 2;
                return new(braceWidth + curl * curls, height + curl * 2);

            case NodeShape.HalfRoundedRectangle:
                return new(width + CapClearance(height, label.Height) * 2, height);

            case NodeShape.CurvedTrapezoid:
                var side = Math.Max(height / 4, CapClearance(height, label.Height));
                return new(Math.Max(20, Math.Max(width + side * 2, width * 1.25)), height);

            case NodeShape.TiltedCylinder:
                // The left cap pushes the label right and the visible part of the right cap takes room
                // after it; the far side of the right cap lies beyond both.
                return new(width + EllipseDepth(height) * 3, height);

            case NodeShape.LinedCylinder:
                return new(width, height + EllipseDepth(width) * 3);

            case NodeShape.BowTie:
                return new(width + EllipseDepth(height), height);

            case NodeShape.DividedRectangle:
                return new(width, height * 1.2);

            case NodeShape.SlopedRectangle:
                return new(width, height * 1.5);

            case NodeShape.Flag:
                return new(width, height * 1.5);

            case NodeShape.TaggedRect:
                return new(width + height * 0.2, height);

            case NodeShape.TriangleUp or NodeShape.TriangleDown:
                // As tall as it is wide, with the label against the base: at its far edge the sides are
                // still a quarter padding clear of it.
                var triangle = label.Width + labelPadding + label.Height;
                return new(triangle, triangle);

            case NodeShape.Folder:
                return new(Math.Max(width, 90), height + Math.Clamp(height * 0.16, 8, 14));

            case NodeShape.Bucket:
                var bucketWidth = Math.Max(width, 80);
                return new(bucketWidth, height + BucketRim(bucketWidth) * 2);

            case NodeShape.Console:
                return new(Math.Max(width, 90), height + consoleBand);

            case NodeShape.Browser:
                return new(Math.Max(width, 90), height + browserBar);

            case NodeShape.Person:
                var personWidth = Math.Max(width, 100);
                return new(personWidth, height + PersonHead(personWidth) * (2 - personOverlap));

            case NodeShape.Bang:
                // Mermaid pads the bang's label box by five half-paddings each side across and four down,
                // and the bang reaches a quarter of that box beyond it.
                return new((label.Width + labelPadding * 5) * 1.25, (label.Height + labelPadding * 4) * 1.25);

            case NodeShape.Cloud:
                var cloud = CloudBounds(label.Width + labelPadding, label.Height + labelPadding);
                return new(cloud.Width, cloud.Height);
        }

        return new(width, height);
    }

    /// <summary>
    /// Where a node's label goes: a box centred where Mermaid centres the label in the shape, as wide as the
    /// room the shape leaves for it.
    /// </summary>
    internal static Rect LabelBox(NodeShape shape, double x, double y, double width, double height, Size label)
    {
        switch (shape)
        {
            case NodeShape.LinedRectangle:
                return new(x + linedRectangleFrame, y, width - linedRectangleFrame, height);

            case NodeShape.Document or NodeShape.LinedDocument:
                // Above the wave, which dips an eighth of the label box's height below it.
                var wave = height / 10;
                if (shape == NodeShape.Document)
                {
                    return new(x, y, width, height - wave * 2);
                }

                var rule = LinedDocumentRule(width);
                return new(x + rule, y, width - rule, height - wave * 2);

            case NodeShape.TaggedDocument:
                return new(x, y, width, height - height / 10);

            case NodeShape.MultiDocument:
                // On the front sheet, above its wave.
                var stackAmplitude = MultiDocumentLabelHeight(height) / 8;
                return new(x, y + stackOffset * 2, width - stackOffset * 2, height - stackOffset * 2 - stackAmplitude * 2);

            case NodeShape.MultiProcess or NodeShape.MultiRect:
                return new(x, y + processOffset * 2, width - processOffset * 2, height - processOffset * 2);

            case NodeShape.WindowPane:
                return new(x + windowPaneBand, y + windowPaneBand, width - windowPaneBand, height - windowPaneBand);

            case NodeShape.CurlyBracket:
                // Mermaid sets the label against the brace, with the padding after it. The label is centred
                // in the room right of the curls instead, so text wider than its estimate cannot run back
                // over the brace.
                var curl = BraceRadius(height);
                return new(x + curl * 2, y, width - curl * 2, height);

            case NodeShape.TiltedCylinder:
                // Between the left cap and the visible edge of the right one.
                return new(x, y, width - EllipseDepth(height) * 2, height);

            case NodeShape.LinedCylinder:
                // Below the top ellipse.
                var depth = EllipseDepth(width);
                return new(x, y + depth * 2, width, height - depth * 2);

            case NodeShape.DividedRectangle:
                var band = height - height / 1.2;
                return new(x, y + band, width, height - band);

            case NodeShape.SlopedRectangle:
                return new(x, y + height / 3, width, height * 2 / 3);

            case NodeShape.TriangleUp:
                // Resting half a padding above the base.
                return new(x, y + height - labelPadding / 2 - label.Height, width, label.Height);

            case NodeShape.TriangleDown:
                return new(x, y + labelPadding / 2, width, label.Height);

            case NodeShape.Folder:
                var tab = FolderTab(height);
                return new(x, y + tab, width, height - tab);

            case NodeShape.Console:
                return new(x, y + consoleBand, width, height - consoleBand);

            case NodeShape.Browser:
                return new(x, y + browserBar, width, height - browserBar);

            case NodeShape.Person:
                var bodyTop = PersonBodyTop(y, width);
                return new(x, bodyTop, width, y + height - bodyTop);

            case NodeShape.Bang:
                var bangWidth = width / 1.25;
                var bangHeight = height / 1.25;
                return new(x + bangWidth * 0.1, y + bangHeight * 0.1, bangWidth, bangHeight);

            case NodeShape.Cloud:
                var (cloudWidth, cloudHeight, bounds) = FitCloud(width, height);
                return new(x - bounds.X, y - bounds.Y, cloudWidth, cloudHeight);
        }

        return new(x, y, width, height);
    }

    /// <summary>
    /// The outline of a shape whose sides slant away from its box, so an edge can be stopped where it meets
    /// the shape rather than the box. Null for shapes that fill their box, or that are handled as circles.
    /// </summary>
    internal static Position[]? Outline(NodeShape shape, double x, double y, double width, double height)
    {
        var right = x + width;
        var bottom = y + height;
        var offset = SlantOffset(height);
        return shape switch
        {
            NodeShape.Parallelogram or NodeShape.Lean =>
                [new(x + offset, y), new(right, y), new(right - offset, bottom), new(x, bottom)],
            NodeShape.ParallelogramAlt or NodeShape.LeanAlt =>
                [new(x, y), new(right - offset, y), new(right, bottom), new(x + offset, bottom)],
            NodeShape.Trapezoid =>
                [new(x + offset, y), new(right - offset, y), new(right, bottom), new(x, bottom)],
            NodeShape.TrapezoidAlt =>
                [new(x, y), new(right, y), new(right - offset, bottom), new(x + offset, bottom)],
            NodeShape.TriangleUp =>
                [new(x, bottom), new(right, bottom), new(x + width / 2, y)],
            NodeShape.TriangleDown =>
                [new(x, y), new(right, y), new(x + width / 2, bottom)],
            NodeShape.SlopedRectangle =>
                [new(x, y + height / 3), new(right, y), new(right, bottom), new(x, bottom)],
            NodeShape.NotchedPentagon =>
            [
                new(x + width * 0.1, y), new(x + width * 0.9, y), new(right, y + height * 0.2),
                new(right, bottom), new(x, bottom), new(x, y + height * 0.2)
            ],
            _ => null
        };
    }

    // How far a round end as tall as the shape, r = height / 2, curves in across a label of the given
    // height: Mermaid widens the delay and display shapes by this so the label clears it.
    static double CapClearance(double height, double labelHeight)
    {
        var r = height / 2;
        return r - Math.Sqrt(Math.Max(0, r * r - labelHeight * labelHeight / 4));
    }

    // The depth Mermaid gives an elliptical end spanning a shape: half the span over 2.5 + span / 50. The
    // horizontal cylinder, the stored-data bow tie and the lined cylinder all use it.
    static double EllipseDepth(double span) =>
        span / 2 / (2.5 + span / 50);

    const double cardNotch = 12;

    static string NotchedRectangle(double x, double y, double width, double height) =>
        new PathData()
            .M(x + cardNotch, y)
            .H(x + width)
            .V(y + height)
            .H(x)
            .V(y + cardNotch)
            .Z()
            .ToString();

    const double linedRectangleFrame = 8;

    static string LinedRectangle(double x, double y, double width, double height) =>
        new PathData()
            .Box(x, y, width, height)
            .M(x + linedRectangleFrame, y)
            .V(y + height)
            .ToString();

    static string CrossedCircle(double cx, double cy, double r)
    {
        var reach = r * Math.Cos(Math.PI / 4);
        return new PathData()
            .Circle(cx, cy, r)
            .M(cx - reach, cy + reach)
            .L(cx + reach, cy - reach)
            .M(cx + reach, cy + reach)
            .L(cx - reach, cy - reach)
            .ToString();
    }

    static string Hourglass(double x, double y, double width, double height) =>
        new PathData()
            .M(x, y)
            .H(x + width)
            .L(x, y + height)
            .H(x + width)
            .Z()
            .ToString();

    // Mermaid's brace curls with a radius of a tenth of its label box's height, at least 5, and the node is
    // that box with a curl above and below; so from the node's height it is a twelfth.
    static double BraceRadius(double height) =>
        Math.Max(5, height / 12);

    // A left brace with its point at x: from the foot of its lower curl, 2r in from the point, up to the end
    // of its upper one. The spine runs r in from the point.
    static PathData LeftBrace(PathData path, double x, double y, double height, double r, bool move)
    {
        var cy = y + height / 2;
        path.MoveOrLine(move, x + r * 2, y + height);
        return path
            .A(r, r, false, true, x + r, y + height - r)
            .V(cy + r)
            .A(r, r, false, false, x, cy)
            .A(r, r, false, false, x + r, cy - r)
            .V(y + r)
            .A(r, r, false, true, x + r * 2, y);
    }

    // The mirror image: a right brace with its point at x, from its upper curl down to its lower one.
    static PathData RightBrace(PathData path, double x, double y, double height, double r, bool move)
    {
        var cy = y + height / 2;
        path.MoveOrLine(move, x - r * 2, y);
        return path
            .A(r, r, false, true, x - r, y + r)
            .V(cy - r)
            .A(r, r, false, false, x, cy)
            .A(r, r, false, false, x - r, cy + r)
            .V(y + height - r)
            .A(r, r, false, true, x - r * 2, y + height);
    }

    // A comment: the brace is drawn over a backing that fills the rest of the box, with no outline.
    static ShapePart[] CurlyBracket(double x, double y, double width, double height)
    {
        var r = BraceRadius(height);
        var backing = LeftBrace(new(), x, y, height, r, true)
            .H(x + width)
            .V(y + height)
            .Z();
        var brace = LeftBrace(new(), x, y, height, r, true);
        return [new(backing.ToString(), ShapePaint.Fill), new(brace.ToString(), ShapePaint.Stroke)];
    }

    static ShapePart[] CurlyBracketRight(double x, double y, double width, double height)
    {
        var r = BraceRadius(height);
        var backing = RightBrace(new PathData().M(x, y), x + width, y, height, r, false)
            .H(x)
            .Z();
        var brace = RightBrace(new(), x + width, y, height, r, true);
        return [new(backing.ToString(), ShapePaint.Fill), new(brace.ToString(), ShapePaint.Stroke)];
    }

    static ShapePart[] CurlyBraces(double x, double y, double width, double height)
    {
        var r = BraceRadius(height);
        var backing = RightBrace(LeftBrace(new(), x, y, height, r, true), x + width, y, height, r, false)
            .Z();
        var braces = RightBrace(LeftBrace(new(), x, y, height, r, true), x + width, y, height, r, true);
        return [new(backing.ToString(), ShapePaint.Fill), new(braces.ToString(), ShapePaint.Stroke)];
    }

    // Mermaid's bolt is 35 across and 70 down: two strokes 7 apart, each running from one side to the
    // other across half the height.
    static string Lightning(double x, double y, double width, double height)
    {
        var below = y + height * 38.5 / 70;
        var above = y + height * 31.5 / 70;
        return new PathData()
            .M(x + width, y)
            .L(x, below)
            .L(x + width * 21 / 35, below)
            .L(x, y + height)
            .L(x + width, above)
            .L(x + width * 14 / 35, above)
            .Z()
            .ToString();
    }

    static string HalfRoundedRectangle(double x, double y, double width, double height)
    {
        var r = height / 2;
        return new PathData()
            .M(x, y)
            .H(x + width - r)
            .A(r, r, false, true, x + width - r, y + height)
            .H(x)
            .Z()
            .ToString();
    }

    static string TiltedCylinder(double x, double y, double width, double height)
    {
        var rx = EllipseDepth(height);
        var ry = height / 2;
        var left = x + rx;
        var right = x + width - rx;
        return new PathData()
            .M(left, y + height)
            .A(rx, ry, false, true, left, y)
            .H(right)
            .A(rx, ry, false, true, right, y + height)
            .Z()
            .M(right, y)
            .A(rx, ry, false, false, right, y + height)
            .ToString();
    }

    // A cylinder with a second front arc a tenth of the way down its body.
    static string LinedCylinder(double x, double y, double width, double height)
    {
        var rx = width / 2;
        var ry = EllipseDepth(width);
        var top = y + ry;
        var body = height - ry * 2;
        var rule = top + body * 0.1;
        return new PathData()
            .M(x, top)
            .A(rx, ry, false, false, x + width, top)
            .A(rx, ry, false, false, x, top)
            .V(top + body)
            .A(rx, ry, false, false, x + width, top + body)
            .V(top)
            .M(x, rule)
            .A(rx, ry, false, false, x + width, rule)
            .ToString();
    }

    static string CurvedTrapezoid(double x, double y, double width, double height)
    {
        var r = height / 2;
        var point = height / 4;
        return new PathData()
            .M(x + width - r, y)
            .H(x + point)
            .L(x, y + r)
            .L(x + point, y + height)
            .H(x + width - r)
            .A(r, r, false, false, x + width - r, y)
            .Z()
            .ToString();
    }

    // The label box with a band a fifth of its height above it.
    static string DividedRectangle(double x, double y, double width, double height) =>
        new PathData()
            .Box(x, y, width, height)
            .M(x, y + height - height / 1.2)
            .H(x + width)
            .ToString();

    const double windowPaneBand = 10;

    static string WindowPane(double x, double y, double width, double height) =>
        new PathData()
            .Box(x, y, width, height)
            .M(x, y + windowPaneBand)
            .H(x + width)
            .M(x + windowPaneBand, y)
            .V(y + height)
            .ToString();

    static string NotchedPentagon(double x, double y, double width, double height) =>
        new PathData()
            .M(x + width * 0.1, y)
            .H(x + width * 0.9)
            .L(x + width, y + height * 0.2)
            .V(y + height)
            .H(x)
            .V(y + height * 0.2)
            .Z()
            .ToString();

    // Paper tape: one cycle of a wave along the top and the bottom, in step, each an eighth of the label
    // box's height deep.
    static string Flag(double x, double y, double width, double height)
    {
        var amplitude = height / 1.5 / 8;
        var frequency = 2 * Math.PI / width;
        var bottom = new SineWave(x, frequency, y + height - amplitude, amplitude);
        var top = bottom with
        {
            Baseline = y + amplitude
        };
        var path = new PathData().M(x, bottom.Baseline);
        bottom.Draw(path, x, x + width);
        path.V(top.At(x + width));
        top.Draw(path, x + width, x);
        return path.Z().ToString();
    }

    // Stored data: a convex left end and a concave right one.
    static string BowTie(double x, double y, double width, double height)
    {
        var rx = EllipseDepth(height);
        var ry = height / 2;
        return new PathData()
            .M(x + width, y)
            .H(x + rx)
            .A(rx, ry, false, false, x + rx, y + height)
            .H(x + width)
            .A(rx, ry, false, true, x + width, y)
            .Z()
            .ToString();
    }

    // A box with a fold a fifth of its height across its bottom-right corner.
    static ShapePart[] TaggedRect(double x, double y, double width, double height)
    {
        var tag = height * 0.2;
        var fold = new PathData()
            .M(x + width - tag, y + height)
            .H(x + width)
            .V(y + height - tag)
            .Z();
        return [new(Rectangle(x, y, width, height)), new(fold.ToString())];
    }

    // A data flow diagram store: ruled above and below, open at the sides.
    static ShapePart[] Datastore(double x, double y, double width, double height)
    {
        var rules = new PathData()
            .M(x, y)
            .H(x + width)
            .M(x, y + height)
            .H(x + width);
        return [new(Rectangle(x, y, width, height), ShapePaint.Fill), new(rules.ToString(), ShapePaint.Stroke)];
    }

    // The wavy foot of Mermaid's documents: 0.8 of a sine cycle across the full width, rising and falling by
    // an eighth of the label box's height. The node is that box plus the wave's crest and trough.
    static SineWave DocumentFoot(double x, double y, double width, double height)
    {
        var amplitude = height / 10;
        return new(x, 2 * Math.PI * 0.8 / width, y + height - amplitude, amplitude);
    }

    static PathData DocumentOutline(SineWave foot, double x, double y, double width)
    {
        var path = new PathData().M(x, foot.At(x));
        return foot.Draw(path, x, x + width)
            .V(y)
            .H(x)
            .Z();
    }

    static string Document(double x, double y, double width, double height) =>
        DocumentOutline(DocumentFoot(x, y, width, height), x, y, width).ToString();

    // The lined document's rule stands a twentieth of its label box in from the left, the box being
    // a tenth narrower than the node.
    static double LinedDocumentRule(double width) =>
        width / 1.1 * 0.05;

    static string LinedDocument(double x, double y, double width, double height)
    {
        var foot = DocumentFoot(x, y, width, height);
        var rule = x + LinedDocumentRule(width);
        return DocumentOutline(foot, x, y, width)
            .M(rule, y)
            .V(foot.At(rule))
            .ToString();
    }

    // A document with its bottom-right corner folded up: the fold runs from the foot a fifth of the label
    // box in from the right edge up to the edge 0.405 of the box's height above the foot's baseline, which
    // is where Mermaid's arithmetic puts it.
    static ShapePart[] TaggedDocument(double x, double y, double width, double height)
    {
        var foot = DocumentFoot(x, y, width, height);
        var right = x + width;
        var tagLeft = right - width / 1.1 * 0.2;
        var tag = new PathData()
            .M(tagLeft, foot.At(tagLeft))
            .L(right, foot.Baseline - height / 1.25 * 0.405)
            .V(foot.At(right));
        foot.Draw(tag, right, tagLeft).Z();
        return [new(DocumentOutline(foot, x, y, width).ToString()), new(tag.ToString())];
    }

    // Mermaid offsets the three sheets of its stacked shapes from each other by these.
    const double stackOffset = 10;
    const double processOffset = 5;

    // The stacked document's label box is padded by 1.5 paddings rather than one; its sheets are that
    // box plus half a wave, and the node adds the offsets and the front sheet's trough.
    const double multiDocumentScale = 1 + 1.5 / 8;

    static double MultiDocumentLabelHeight(double height) =>
        (height - stackOffset * 2) / multiDocumentScale;

    static ShapePart[] MultiDocument(double x, double y, double width, double height)
    {
        var labelHeight = MultiDocumentLabelHeight(height);
        var amplitude = labelHeight / 8;
        var frontWidth = width - stackOffset * 2;
        var baseline = y + stackOffset * 2 + labelHeight + amplitude / 2;
        var foot = new SineWave(x, 2 * Math.PI * 0.8 / frontWidth, baseline, amplitude);
        var end = foot.At(x + frontWidth);

        var outline = new PathData()
            .M(x, y + stackOffset * 2)
            .V(baseline);
        foot.Draw(outline, x, x + frontWidth)
            .V(end - stackOffset)
            .H(x + frontWidth + stackOffset)
            .V(end - stackOffset * 2)
            .H(x + width)
            .V(y)
            .H(x + stackOffset * 2)
            .V(y + stackOffset)
            .H(x + stackOffset)
            .V(y + stackOffset * 2)
            .Z();

        var front = new PathData()
            .M(x + stackOffset, y + stackOffset * 2)
            .H(x + frontWidth)
            .V(end - stackOffset)
            .H(x + frontWidth + stackOffset)
            .V(y + stackOffset)
            .H(x + stackOffset)
            .Z();

        return [new(outline.ToString()), new(front.ToString())];
    }

    static ShapePart[] MultiProcess(double x, double y, double width, double height)
    {
        const double offset = processOffset;
        var outline = new PathData()
            .M(x, y + offset * 2)
            .V(y + height)
            .H(x + width - offset * 2)
            .V(y + height - offset)
            .H(x + width - offset)
            .V(y + height - offset * 2)
            .H(x + width)
            .V(y)
            .H(x + offset * 2)
            .V(y + offset)
            .H(x + offset)
            .V(y + offset * 2)
            .Z();
        var front = new PathData()
            .M(x + offset, y + offset * 2)
            .H(x + width - offset * 2)
            .V(y + height - offset)
            .H(x + width - offset)
            .V(y + offset)
            .H(x + offset)
            .Z();
        return [new(outline.ToString()), new(front.ToString())];
    }

    // Mermaid's folder tab is 0.16 of the body's height, between 8 and 14, and the node is body and tab.
    static double FolderTab(double height) =>
        Math.Clamp(height * 0.16 / 1.16, 8, 14);

    static string Folder(double x, double y, double width, double height)
    {
        var tabHeight = FolderTab(height);
        var tabWidth = Math.Max(width * 0.38, 28);
        return new PathData()
            .M(x, y)
            .H(x + tabWidth)
            .V(y + tabHeight)
            .H(x + width)
            .V(y + height)
            .H(x)
            .Z()
            .ToString();
    }

    static double BucketRim(double width) =>
        Math.Clamp(width * 0.08, 5, 12);

    // An open-topped bucket narrowing to a curved base, with its rim drawn as a whole ellipse.
    static ShapePart[] Bucket(double x, double y, double width, double height)
    {
        var rim = BucketRim(width);
        var top = y + rim;
        var bottom = y + height - rim;
        var cx = x + width / 2;
        var foot = width * 0.72 / 2;
        var body = new PathData()
            .M(x, top)
            .L(cx - foot, bottom)
            .A(foot, rim, false, false, cx + foot, bottom)
            .L(x + width, top)
            .A(width / 2, rim, false, false, x, top)
            .Z();
        var rimPath = new PathData().Ellipse(cx, top, width / 2, rim);
        return [new(body.ToString()), new(rimPath.ToString(), ShapePaint.Stroke)];
    }

    const double windowCorner = 12;
    const double consoleBand = 20;
    const double browserBar = 18;

    // A browser window: a bar across the top holding three buttons and an address field.
    static ShapePart[] Browser(double x, double y, double width, double height)
    {
        var buttons = new PathData();
        for (var i = 0; i < 3; i++)
        {
            buttons.Circle(x + 12 + i * 9, y + browserBar / 2, 2.5);
        }

        var bar = new PathData()
            .M(x, y + browserBar)
            .H(x + width);
        var address = new PathData().RoundedBox(x + 44, y + 4, Math.Max(width - 56, 10), browserBar - 8, 3);
        return
        [
            new(new PathData().RoundedBox(x, y, width, height, windowCorner).ToString()),
            new(bar.ToString(), ShapePaint.Stroke),
            new(buttons.ToString(), ShapePaint.Solid),
            new(address.ToString(), ShapePaint.Stroke, 0.6)
        ];
    }

    // The head's radius is 0.23 of the width, between 16 and 56, and it overlaps the body by 0.27 of itself.
    const double personOverlap = 0.27;

    static double PersonHead(double width) =>
        Math.Clamp(width * 0.23, 16, 56);

    static double PersonBodyTop(double y, double width) =>
        y + PersonHead(width) * (2 - personOverlap);

    static ShapePart[] Person(double x, double y, double width, double height)
    {
        var head = PersonHead(width);
        var bodyTop = PersonBodyTop(y, width);
        var bodyHeight = y + height - bodyTop;
        var body = new PathData().RoundedBox(x, bodyTop, width, bodyHeight, Math.Min(width * 0.177, bodyHeight * 0.45));
        return [new(body.ToString()), new(new PathData().Circle(x + width / 2, y + head, head).ToString())];
    }

    // Mermaid's bang, drawn as it is from the corner of its label box: scallops reaching a tenth of the box
    // beyond it on the left and top and 0.15 beyond on the right and bottom.
    static string Bang(double x, double y, double width, double height)
    {
        var w = width / 1.25;
        var h = height / 1.25;
        var r = w * 0.15;
        return new PathData()
            .M(x + w * 0.1, y + h * 0.1)
            .RelativeArc(r, r, false, w * 0.25, -h * 0.1)
            .RelativeArc(r, r, false, w * 0.25, 0)
            .RelativeArc(r, r, false, w * 0.25, 0)
            .RelativeArc(r, r, false, w * 0.25, h * 0.1)
            .RelativeArc(r, r, false, w * 0.15, h * 0.33)
            .RelativeArc(r * 0.8, r * 0.8, false, 0, h * 0.34)
            .RelativeArc(r, r, false, -w * 0.15, h * 0.33)
            .RelativeArc(r, r, false, -w * 0.25, h * 0.15)
            .RelativeArc(r, r, false, -w * 0.25, 0)
            .RelativeArc(r, r, false, -w * 0.25, 0)
            .RelativeArc(r, r, false, -w * 0.25, -h * 0.15)
            .RelativeArc(r, r, false, -w * 0.1, -h * 0.33)
            .RelativeArc(r * 0.8, r * 0.8, false, 0, -h * 0.34)
            .RelativeArc(r, r, false, w * 0.1, -h * 0.33)
            .Z()
            .ToString();
    }

    // Mermaid's cloud, drawn from the corner of its label box w by h.
    static string CloudPath(double x, double y, double w, double h)
    {
        var r1 = w * 0.15;
        var r2 = w * 0.25;
        var r3 = w * 0.35;
        var r4 = w * 0.2;
        return new PathData()
            .M(x, y)
            .RelativeArc(r1, r1, true, w * 0.25, -w * 0.1)
            .RelativeArc(r3, r3, true, w * 0.4, -w * 0.1)
            .RelativeArc(r2, r2, true, w * 0.35, w * 0.2)
            .RelativeArc(r1, r1, true, w * 0.15, h * 0.35)
            .RelativeArc(r4, r4, true, -w * 0.15, h * 0.65)
            .RelativeArc(r2, r1, true, -w * 0.25, w * 0.15)
            .RelativeArc(r3, r3, true, -w * 0.5, 0)
            .RelativeArc(r1, r1, true, -w * 0.25, -w * 0.15)
            .RelativeArc(r1, r1, true, -w * 0.1, -h * 0.35)
            .RelativeArc(r4, r4, true, w * 0.1, -h * 0.65)
            .Z()
            .ToString();
    }

    // The cloud's lobes bulge past its label box by amounts that depend on both of the box's sides, so the
    // node's size is measured from the drawn outline.
    static Rect CloudBounds(double w, double h)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var subPath in PathFlattener.Flatten(CloudPath(0, 0, w, h)))
        {
            foreach (var point in subPath.Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        return new(minX, minY, maxX - minX, maxY - minY);
    }

    // Finds the label box whose cloud fills a node of the given size, with the outline's bounds relative
    // to the box's corner. The width is nearly 1.25 boxes and the height the box's plus a share of its
    // width, so a few rounds of correcting each converge.
    static (double Width, double Height, Rect Bounds) FitCloud(double width, double height)
    {
        var w = width / 1.25;
        var h = Math.Max(1, height - w * 0.48);
        var bounds = CloudBounds(w, h);
        for (var i = 0; i < 6; i++)
        {
            w *= width / bounds.Width;
            h = Math.Max(1, h + height - bounds.Height);
            bounds = CloudBounds(w, h);
        }

        return (w, h, bounds);
    }

    static string Cloud(double x, double y, double width, double height)
    {
        var (w, h, bounds) = FitCloud(width, height);
        return CloudPath(x - bounds.X, y - bounds.Y, w, h);
    }

    /// <summary>
    /// y = baseline + amplitude · sin(frequency · (x − origin)): the waves along Mermaid's documents and
    /// paper tape.
    /// </summary>
    readonly record struct SineWave(double Origin, double Frequency, double Baseline, double Amplitude)
    {
        public double At(double x) =>
            Baseline + Amplitude * Math.Sin(Frequency * (x - Origin));

        double Slope(double x) =>
            Amplitude * Frequency * Math.Cos(Frequency * (x - Origin));

        /// <summary>
        /// Continues a path, which must be at the wave's point at <paramref name="from"/>, along the wave to
        /// <paramref name="to"/>: cubic Béziers each spanning at most an eighth of a cycle and matching the
        /// wave's slope at both ends, which keeps them within a hair of the curve.
        /// </summary>
        public PathData Draw(PathData path, double from, double to)
        {
            var segments = Math.Max(2, (int) Math.Ceiling(Math.Abs(Frequency * (to - from)) / (Math.PI / 4)));
            var step = (to - from) / segments;
            for (var i = 0; i < segments; i++)
            {
                var start = from + step * i;
                var end = start + step;
                path.C(
                    start + step / 3,
                    At(start) + Slope(start) * step / 3,
                    end - step / 3,
                    At(end) - Slope(end) * step / 3,
                    end,
                    At(end));
            }

            return path;
        }
    }

    /// <summary>Builds SVG path data, two decimal places at most, commands separated by spaces.</summary>
    sealed class PathData
    {
        StringBuilder builder = new();

        PathData Command(char command)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(command);
            return this;
        }

        PathData Number(double value)
        {
            builder.Append(culture, $"{value:0.##}");
            return this;
        }

        PathData Point(double x, double y)
        {
            builder.Append(culture, $"{x:0.##},{y:0.##}");
            return this;
        }

        public PathData M(double x, double y) =>
            Command('M').Point(x, y);

        public PathData L(double x, double y) =>
            Command('L').Point(x, y);

        public PathData MoveOrLine(bool move, double x, double y)
        {
            if (move)
            {
                return M(x, y);
            }

            return L(x, y);
        }

        public PathData H(double x) =>
            Command('H').Number(x);

        public PathData V(double y) =>
            Command('V').Number(y);

        public PathData A(double rx, double ry, bool large, bool sweep, double x, double y)
        {
            Command('A');
            builder.Append(culture, $"{rx:0.##},{ry:0.##} 0 {(large ? 1 : 0)} {(sweep ? 1 : 0)} ");
            return Point(x, y);
        }

        public PathData RelativeArc(double rx, double ry, bool sweep, double dx, double dy)
        {
            Command('a');
            builder.Append(culture, $"{rx:0.##},{ry:0.##} 0 0 {(sweep ? 1 : 0)} ");
            return Point(dx, dy);
        }

        public PathData C(double x1, double y1, double x2, double y2, double x, double y)
        {
            Command('C').Point(x1, y1);
            builder.Append(' ');
            Point(x2, y2);
            builder.Append(' ');
            return Point(x, y);
        }

        public PathData Z() =>
            Command('Z');

        public PathData Box(double x, double y, double width, double height) =>
            M(x, y)
                .H(x + width)
                .V(y + height)
                .H(x)
                .Z();

        public PathData RoundedBox(double x, double y, double width, double height, double r)
        {
            r = Math.Min(r, Math.Min(width, height) / 2);
            return M(x + r, y)
                .H(x + width - r)
                .A(r, r, false, true, x + width, y + r)
                .V(y + height - r)
                .A(r, r, false, true, x + width - r, y + height)
                .H(x + r)
                .A(r, r, false, true, x, y + height - r)
                .V(y + r)
                .A(r, r, false, true, x + r, y)
                .Z();
        }

        public PathData Ellipse(double cx, double cy, double rx, double ry) =>
            M(cx - rx, cy)
                .A(rx, ry, true, false, cx + rx, cy)
                .A(rx, ry, true, false, cx - rx, cy)
                .Z();

        public PathData Circle(double cx, double cy, double r) =>
            Ellipse(cx, cy, r, r);

        public override string ToString() =>
            builder.ToString();
    }
}
