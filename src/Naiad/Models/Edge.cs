namespace Naiad;

public class Edge
{
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public string? Label { get; init; }
    public EdgeType Type { get; init; } = EdgeType.Arrow;
    public EdgeStyle LineStyle { get; init; } = EdgeStyle.Solid;

    // Optional layout hint (e.g. "place these on the same rank").
    public RankConstraint RankConstraint { get; set; } = RankConstraint.None;

    // Size to reserve for this edge's label so the layout keeps nodes clear of
    // it (0 = no reservation). Used for long edges whose label sits on a rank.
    public double LabelWidth { get; set; }
    public double LabelHeight { get; set; }

    // The fewest ranks the edge may span. Mermaid's longer links (`--->`, `-..->`, `====`) ask for more
    // than the default one.
    public int MinLength { get; init; } = 1;

    // Layout properties (set by layout engine)
    public List<Position> Points { get; } = [];

    // Where the layout put the label, when it reserved a place for one.
    Position? laidOutLabelPosition;

    /// <summary>
    /// The centre of the edge label. A layout that reserves room for the label says where that room is,
    /// and the label belongs there even if the route is adjusted afterwards; without that, it is the
    /// midpoint of the route.
    /// </summary>
    public Position LabelPosition
    {
        set => laidOutLabelPosition = value;
        get
        {
            if (laidOutLabelPosition is { } laidOut)
            {
                return laidOut;
            }

            if (Points.Count == 0)
            {
                return Position.Zero;
            }

            if (Points.Count == 1)
            {
                return Points[0];
            }

            // Return midpoint of the edge path
            var midIndex = Points.Count / 2;
            if (Points.Count % 2 == 0)
            {
                var p1 = Points[midIndex - 1];
                var p2 = Points[midIndex];
                return new((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
            }

            return Points[midIndex];
        }
    }

    internal void ClearLabelPosition() =>
        laidOutLabelPosition = null;

    public bool HasArrowHead =>
        Type is
            EdgeType.Arrow or
            EdgeType.DottedArrow or
            EdgeType.ThickArrow or
            EdgeType.BiDirectional;

    // A two-headed edge carries the same head at its source as at its target: `<-->` an arrow, `o--o` a
    // circle, `x--x` a cross.
    public bool HasArrowTail => Type is EdgeType.BiDirectional;

    public bool HasCircleEnd => Type is EdgeType.CircleEnd or EdgeType.BiDirectionalCircle;
    public bool HasCircleTail => Type is EdgeType.BiDirectionalCircle;

    public bool HasCrossEnd => Type is EdgeType.CrossEnd or EdgeType.BiDirectionalCross;
    public bool HasCrossTail => Type is EdgeType.BiDirectionalCross;
}