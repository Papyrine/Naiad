namespace Naiad.Diagrams.Ishikawa;

/// <summary>
/// Draws a fishbone: the effect in a head on the right, a horizontal spine running left from it, and the
/// causes branching off the spine, alternately above and below it. Each cause's own causes alternate
/// between horizontal bones (off a diagonal) and diagonal bones (off a horizontal), as Mermaid lays them
/// out. Mermaid measures its labels in the DOM; here they are estimated from their character counts.
/// </summary>
public class IshikawaRenderer : IDiagramRenderer<IshikawaModel>
{
    const double spineBaseLength = 250;
    const double boneStub = 30;
    const double boneBase = 60;
    const double bonePerChild = 5;
    const double causeBoxInset = 20;
    const double causeGap = 10;
    const double lineHeightFactor = 1.05;
    const double charWidthFactor = 0.55;
    const string lineColor = "#333333";
    const string fillColor = "#ECECFF";
    const string textColor = "#333";

    static readonly double cosAngle = Math.Cos(82 * Math.PI / 180);
    static readonly double sinAngle = Math.Sin(82 * Math.PI / 180);

    public SvgDocument Render(IshikawaModel model, RenderOptions options)
    {
        if (model.Root is not { } root)
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

        var canvas = new Canvas(options.FontSize);
        var causes = root.Children;

        if (causes.Count == 0)
        {
            DrawHead(canvas, spineBaseLength, root.Text);
            return canvas.Build(options);
        }

        var upperCauses = causes.Where(static (_, index) => index % 2 == 0).ToList();
        var lowerCauses = causes.Where(static (_, index) => index % 2 == 1).ToList();
        var (upperTotal, upperMax) = SideStats(upperCauses);
        var (lowerTotal, lowerMax) = SideStats(lowerCauses);
        var descendantTotal = upperTotal + lowerTotal;

        // The two sides share a pool of bone length in proportion to how many causes hang off each.
        var upperLength = spineBaseLength;
        var lowerLength = spineBaseLength;
        if (descendantTotal > 0)
        {
            const double pool = spineBaseLength * 2;
            const double minLength = spineBaseLength * 0.3;
            upperLength = Math.Max(minLength, pool * upperTotal / descendantTotal);
            lowerLength = Math.Max(minLength, pool * lowerTotal / descendantTotal);
        }

        var minSpacing = options.FontSize * 2.0;
        upperLength = Math.Max(upperLength, upperMax * minSpacing);
        lowerLength = Math.Max(lowerLength, lowerMax * minSpacing);

        var spineY = Math.Max(upperLength, spineBaseLength);

        // The spine is drawn first, so it sits under everything else; its left end is only known once
        // every pair of causes is placed.
        var spine = canvas.ReserveSlot();
        DrawHead(canvas, spineY, root.Text);

        // Causes go out in pairs, one above and one below, each pair to the left of the labels of the last.
        var spineX = -20.0;
        // Mermaid starts each pair at the left edge of the last pair's labels, which leaves a cause box
        // (wider than its text, and centred on the far end of its bone) overlapping the box before it on
        // the same side, or the head. So a pair also starts far enough left that each of its boxes clears
        // the leftmost box on its side so far.
        var upperBoxLeft = 0.0;
        var lowerBoxLeft = 0.0;
        for (var pair = 0; pair * 2 < causes.Count; pair++)
        {
            var upper = Cause.Measure(canvas, causes[pair * 2], upperLength);
            var lower = pair * 2 + 1 < causes.Count ? Cause.Measure(canvas, causes[pair * 2 + 1], lowerLength) : null;

            var startX = Math.Min(spineX, upperBoxLeft - causeGap - upper.RightExtent);
            if (lower is not null)
            {
                startX = Math.Min(startX, lowerBoxLeft - causeGap - lower.RightExtent);
            }

            canvas.ResetTextLeft();
            upperBoxLeft = DrawBranch(canvas, upper, startX, spineY, -1);
            if (lower is not null)
            {
                lowerBoxLeft = DrawBranch(canvas, lower, startX, spineY, 1);
            }

            spineX = Math.Min(startX, canvas.TextLeft);
        }

        canvas.FillSlot(spine, new Line(spineX, spineY, 0, spineY, 2, Arrow: false));
        return canvas.Build(options);
    }

    static (int total, int max) SideStats(List<IshikawaNode> nodes)
    {
        var total = 0;
        var max = 0;
        foreach (var node in nodes)
        {
            var descendants = CountDescendants(node);
            total += descendants;
            max = Math.Max(max, descendants);
        }

        return (total, max);
    }

    static int CountDescendants(IshikawaNode node) =>
        node.Children.Sum(static _ => 1 + CountDescendants(_));

    /// <summary>
    /// The head is a sail shape whose flat edge sits on the end of the spine: a quadratic curve from the
    /// bottom of that edge, bulging out to the right, back to its top.
    /// </summary>
    static void DrawHead(Canvas canvas, double spineY, string text)
    {
        var fontSize = canvas.FontSize;
        var maxChars = Math.Max(6, (int) Math.Floor(110 / (fontSize * 0.6)));
        var lines = SplitLines(Wrap(text, maxChars));
        var (textWidth, textHeight) = canvas.Measure(lines);
        var width = Math.Max(60, textWidth + 6);
        var height = Math.Max(40, textHeight * 2 + 40);

        canvas.Add(new Head(spineY, width, height));
        canvas.AddText(new Label(width / 2 + 3, spineY, lines, "middle", "middle", Bold: true), trackLeft: false);
    }

    /// <summary>
    /// A cause's bone and the box holding its name, measured before anything is drawn so the pair it is in
    /// can be placed clear of the boxes before it.
    /// </summary>
    sealed record Cause(IshikawaNode Node, double LineLength, string[] Lines, double Width, double Height)
    {
        /// <summary>
        /// A cause with no sub-causes has a short bone, a fifth of its side's length, as Mermaid draws it.
        /// Mermaid lets that get so short its box lands on the spine; here the bone is kept long enough for
        /// the box to clear the spine by a line's height.
        /// </summary>
        public static Cause Measure(Canvas canvas, IshikawaNode node, double length)
        {
            var lines = SplitLines(node.Text);
            var (width, height) = canvas.Measure(lines);
            var lineLength = length * (node.Children.Count > 0 ? 1 : 0.2);
            var minLineLength = (height / 2 + canvas.FontSize) / sinAngle;
            return new(node, Math.Max(lineLength, minLineLength), lines, width, height);
        }

        public double BoxHalfWidth => Width / 2 + causeBoxInset;

        /// <summary>How far right of the bone's start the right edge of its box reaches.</summary>
        public double RightExtent => -cosAngle * LineLength + BoxHalfWidth;
    }

    /// <summary>Draws a cause with its sub-causes, and returns the left edge of the cause's box.</summary>
    static double DrawBranch(Canvas canvas, Cause cause, double startX, double startY, int direction)
    {
        var children = cause.Node.Children;
        var deltaX = -cosAngle * cause.LineLength;
        var deltaY = sinAngle * cause.LineLength * direction;
        var endX = startX + deltaX;
        var endY = startY + deltaY;

        canvas.Add(new Line(startX, startY, endX, endY, 2, Arrow: true));

        // The cause's name sits in a box at the far end of its bone.
        var labelY = endY + 11 * direction;
        var boxLeft = endX - cause.BoxHalfWidth;
        canvas.Add(new Box(boxLeft, labelY - cause.Height / 2 - 2, cause.BoxHalfWidth * 2, cause.Height + 4));
        canvas.AddText(new Label(endX, labelY, cause.Lines, "middle", "middle", Bold: false));

        if (children.Count == 0)
        {
            return boxLeft;
        }

        var (entries, yOrder) = Flatten(children, direction);

        // The sub-causes are spread evenly over the height of the cause's bone, in the order Flatten gives.
        var ys = new double[entries.Count];
        for (var slot = 0; slot < yOrder.Count; slot++)
        {
            ys[yOrder[slot]] = startY + deltaY * (slot + 1) / (entries.Count + 1);
        }

        var root = new Bone(startX, startY, endX, endY, children.Count);
        var bones = new Dictionary<int, Bone>();
        var diagonalY = sinAngle * direction;
        var oddBaseline = direction < 0 ? null : "hanging";

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var y = ys[i];
            var parent = entry.ParentIndex < 0 ? root : bones[entry.ParentIndex];

            double boneX0;
            double boneY0;
            double boneX1;

            if (entry.Depth % 2 == 0)
            {
                // A horizontal bone, off the parent's diagonal at its own height, running left.
                var parentDeltaY = parent.Y1 - parent.Y0;
                boneX0 = Lerp(parent.X0, parent.X1, parentDeltaY == 0 ? 0.5 : (y - parent.Y0) / parentDeltaY);
                boneY0 = y;
                boneX1 = boneX0 - (entry.ChildCount > 0 ? boneBase + entry.ChildCount * bonePerChild : boneStub);
                canvas.Add(new Line(boneX0, y, boneX1, y, 1, Arrow: true));
                canvas.AddText(new Label(boneX1, y, entry.Lines, "end", "middle", Bold: false));
            }
            else
            {
                // A diagonal bone, off an evenly spaced point along the parent's horizontal, slanting to
                // its own height.
                var drawn = parent.ChildrenDrawn++;
                boneX0 = Lerp(parent.X0, parent.X1, (double) (parent.ChildCount - drawn) / (parent.ChildCount + 1));
                boneY0 = parent.Y0;
                boneX1 = boneX0 - cosAngle * ((y - boneY0) / diagonalY);
                canvas.Add(new Line(boneX0, boneY0, boneX1, y, 1, Arrow: true));
                canvas.AddText(new Label(boneX1, y, entry.Lines, "end", oddBaseline, Bold: false));
            }

            if (entry.ChildCount > 0)
            {
                bones[i] = new(boneX0, boneY0, boneX1, y, entry.ChildCount);
            }
        }

        return boxLeft;
    }

    /// <summary>
    /// The sub-causes of one cause, flattened so each can be given a height without recursing while
    /// drawing. Even depths are placed in pre-order (nearer the spine than their own children) and odd ones
    /// in post-order, which keeps each diagonal bone inside its parent's wedge. Above the spine the
    /// siblings are taken in reverse, so the first still ends up nearest the spine.
    /// </summary>
    static (List<Entry> entries, List<int> yOrder) Flatten(List<IshikawaNode> children, int direction)
    {
        var entries = new List<Entry>();
        var yOrder = new List<int>();
        Walk(children, -1, 2);
        return (entries, yOrder);

        void Walk(List<IshikawaNode> nodes, int parentIndex, int depth)
        {
            var ordered = direction == -1 ? Enumerable.Reverse(nodes) : nodes.AsEnumerable();
            foreach (var child in ordered)
            {
                var index = entries.Count;
                entries.Add(new(SplitLines(Wrap(child.Text, 15)), depth, parentIndex, child.Children.Count));
                if (depth % 2 == 0)
                {
                    yOrder.Add(index);
                    Walk(child.Children, index, depth + 1);
                }
                else
                {
                    Walk(child.Children, index, depth + 1);
                    yOrder.Add(index);
                }
            }
        }
    }

    /// <summary>Greedily wraps <paramref name="text"/> at word breaks to lines of at most <paramref name="maxChars"/>.</summary>
    static string Wrap(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

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

        return string.Join('\n', lines);
    }

    static string[] SplitLines(string text) =>
        text.Split('\n').SelectMany(LabelLines.Split).ToArray();

    static double Lerp(double from, double to, double fraction) =>
        from + (to - from) * fraction;

    record Entry(string[] Lines, int Depth, int ParentIndex, int ChildCount);

    sealed class Bone(double x0, double y0, double x1, double y1, int childCount)
    {
        public double X0 { get; } = x0;
        public double Y0 { get; } = y0;
        public double X1 { get; } = x1;
        public double Y1 { get; } = y1;
        public int ChildCount { get; } = childCount;
        public int ChildrenDrawn { get; set; }
    }

    abstract record Shape;

    /// <summary>A bone. One with an arrow has its head at the start, pointing at what it feeds into.</summary>
    record Line(double X1, double Y1, double X2, double Y2, double StrokeWidth, bool Arrow) : Shape;

    record Head(double SpineY, double Width, double Height) : Shape;

    record Box(double X, double Y, double Width, double Height) : Shape;

    /// <summary>
    /// A label of one or more lines centred on <see cref="Y"/>. A null baseline is the alphabetic one, so
    /// each line rests on its y.
    /// </summary>
    record Label(double X, double Y, string[] Lines, string Anchor, string? Baseline, bool Bold) : Shape;

    /// <summary>
    /// Collects the shapes in drawing order along with the extent of everything drawn, so the diagram can
    /// be shifted to start at the padding once its leftmost and topmost points are known.
    /// </summary>
    sealed class Canvas(double fontSize)
    {
        List<Shape?> shapes = [];
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        public double FontSize { get; } = fontSize;

        /// <summary>The left edge of the leftmost label added since the last <see cref="ResetTextLeft"/>.</summary>
        public double TextLeft { get; private set; }

        public void ResetTextLeft() => TextLeft = double.MaxValue;

        public int ReserveSlot()
        {
            shapes.Add(null);
            return shapes.Count - 1;
        }

        public void FillSlot(int slot, Line line)
        {
            shapes[slot] = line;
            Include(line.X1, line.Y1);
            Include(line.X2, line.Y2);
        }

        public double LineHeight => FontSize * lineHeightFactor;

        public (double width, double height) Measure(string[] lines) =>
            (lines.Max(static _ => _.Length) * FontSize * charWidthFactor,
                (lines.Length - 1) * LineHeight + FontSize * 1.2);

        public void Add(Shape shape)
        {
            shapes.Add(shape);
            switch (shape)
            {
                case Line line:
                    Include(line.X1, line.Y1);
                    Include(line.X2, line.Y2);
                    if (line.Arrow)
                    {
                        var size = ArrowSize(line);
                        Include(line.X1 - size, line.Y1 - size);
                        Include(line.X1 + size, line.Y1 + size);
                    }

                    break;
                case Head head:
                    Include(0, head.SpineY - head.Height / 2);
                    Include(head.Width * 1.2, head.SpineY + head.Height / 2);
                    break;
                case Box box:
                    Include(box.X, box.Y);
                    Include(box.X + box.Width, box.Y + box.Height);
                    break;
            }
        }

        public void AddText(Label label, bool trackLeft = true)
        {
            shapes.Add(label);
            var (width, _) = Measure(label.Lines);
            var left = label.Anchor switch
            {
                "middle" => label.X - width / 2,
                "end" => label.X - width,
                _ => label.X
            };

            var firstY = label.Y - (label.Lines.Length - 1) * LineHeight / 2;
            var lastY = firstY + (label.Lines.Length - 1) * LineHeight;
            var (above, below) = label.Baseline switch
            {
                "middle" => (FontSize * 0.6, FontSize * 0.6),
                "hanging" => (0, FontSize * 1.2),
                _ => (FontSize * 0.9, FontSize * 0.3)
            };

            Include(left, firstY - above);
            Include(left + width, lastY + below);
            if (trackLeft)
            {
                TextLeft = Math.Min(TextLeft, left);
            }
        }

        void Include(double x, double y)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        static double ArrowSize(Line line) => 6 * line.StrokeWidth;

        public SvgDocument Build(RenderOptions options)
        {
            var offsetX = options.Padding - minX;
            var offsetY = options.Padding - minY;

            var builder = new SvgBuilder();
            builder.Options(options);
            builder.Size(maxX - minX + options.Padding * 2, maxY - minY + options.Padding * 2);

            foreach (var shape in shapes)
            {
                switch (shape)
                {
                    case Line line:
                        builder.AddLine(
                            line.X1 + offsetX,
                            line.Y1 + offsetY,
                            line.X2 + offsetX,
                            line.Y2 + offsetY,
                            stroke: lineColor,
                            strokeWidth: line.StrokeWidth);
                        if (line.Arrow)
                        {
                            AddArrowhead(builder, line, offsetX, offsetY);
                        }

                        break;
                    case Head head:
                        var top = head.SpineY - head.Height / 2 + offsetY;
                        var bottom = head.SpineY + head.Height / 2 + offsetY;
                        builder.AddPath(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"M {offsetX:0.##} {top:0.##} L {offsetX:0.##} {bottom:0.##} Q {head.Width * 2.4 + offsetX:0.##} {head.SpineY + offsetY:0.##} {offsetX:0.##} {top:0.##} Z"),
                            fill: fillColor,
                            stroke: lineColor,
                            strokeWidth: 2);
                        break;
                    case Box box:
                        builder.AddRect(
                            box.X + offsetX,
                            box.Y + offsetY,
                            box.Width,
                            box.Height,
                            fill: fillColor,
                            stroke: lineColor,
                            strokeWidth: 2);
                        break;
                    case Label label:
                        var firstY = label.Y - (label.Lines.Length - 1) * LineHeight / 2;
                        for (var i = 0; i < label.Lines.Length; i++)
                        {
                            builder.AddText(
                                label.X + offsetX,
                                firstY + i * LineHeight + offsetY,
                                label.Lines[i],
                                anchor: label.Anchor,
                                baseline: label.Baseline,
                                fontSize: FontSize,
                                fontFamily: options.FontFamily,
                                fontWeight: label.Bold ? "bold" : null,
                                fill: textColor);
                        }

                        break;
                }
            }

            return builder.Build();
        }

        /// <summary>
        /// A filled triangle with its tip on the start of the line, pointing back along it: the shape of
        /// Mermaid's arrow marker, which scales with the stroke width. Drawn as a path rather than a marker.
        /// </summary>
        static void AddArrowhead(SvgBuilder builder, Line line, double offsetX, double offsetY)
        {
            var deltaX = line.X1 - line.X2;
            var deltaY = line.Y1 - line.Y2;
            var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (length == 0)
            {
                return;
            }

            var unitX = deltaX / length;
            var unitY = deltaY / length;
            var size = ArrowSize(line);
            var halfWidth = size / 2;
            var tipX = line.X1 + offsetX;
            var tipY = line.Y1 + offsetY;
            var baseX = tipX - unitX * size;
            var baseY = tipY - unitY * size;
            builder.AddPath(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"M {tipX:0.##} {tipY:0.##} L {baseX - unitY * halfWidth:0.##} {baseY + unitX * halfWidth:0.##} L {baseX + unitY * halfWidth:0.##} {baseY - unitX * halfWidth:0.##} Z"),
                fill: lineColor);
        }
    }
}
