namespace Naiad;

/// <summary>
/// Dagre ranks nodes so that edges cross the gaps between ranks, but it aims an edge's end segment straight
/// at the target's border without checking what sits in between. When the target shares a rank with a
/// neighbour that lies between it and the incoming segment, that segment cuts across the neighbour's body —
/// in <c>ComplexPipeline</c> two edges into "Postgres" ran through the "Transactional outbox" cylinder.
///
/// This re-aims a blocked end segment into an L: along the incoming direction until it is over (or beside)
/// the target, then straight in, entering through whichever border that approach reaches. Only a segment
/// that actually crosses a node is touched, and only when both legs of the replacement are themselves
/// clear — a blocked edge that cannot be improved is left exactly as dagre routed it.
/// </summary>
static class EdgeObstacleRouter
{
    // Kept clear of a node an edge is routed around. The rendered path is a B-spline through these
    // waypoints and so cuts its corners, and the margin is what stops that shortcut clipping the node.
    const double clearance = 14;

    public static void Route(GraphDiagramBase diagram)
    {
        if (diagram.Nodes.Count < 3)
        {
            return;
        }

        // Every node's box once, and the same box grown by the clearance, which is what a route is tested
        // against. An edge's blockers are all of them but its own two ends, so the ends are passed along
        // to be skipped rather than a list of the rest being built for every edge.
        var count = diagram.Nodes.Count;
        var boxes = new Rect[count];
        var grown = new Rect[count];
        var indexById = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var node = diagram.Nodes[i];
            boxes[i] = Rect.Around(node);
            grown[i] = boxes[i].Grow(clearance);
            indexById[node.Id] = i;
        }

        foreach (var edge in diagram.Edges)
        {
            if (edge.Points.Count < 2 ||
                !indexById.TryGetValue(edge.TargetId, out var target) ||
                !indexById.TryGetValue(edge.SourceId, out var source))
            {
                continue;
            }

            var blockers = new Blockers(grown, source, target);
            ReAimEnd(edge.Points, boxes[target], blockers, atEnd: true);
            ReAimEnd(edge.Points, boxes[source], blockers, atEnd: false);
        }
    }

    // The grown boxes of every node except the two an edge joins.
    readonly record struct Blockers(Rect[] Grown, int Source, int Target);

    /// <summary>
    /// Replaces the segment joining <paramref name="node"/> to the rest of the route with an L when the
    /// straight version crosses a blocker. <paramref name="atEnd"/> picks which end of the polyline is
    /// being re-aimed; the geometry is identical either way, only the indices differ.
    /// </summary>
    static void ReAimEnd(List<Position> points, Rect node, Blockers blockers, bool atEnd)
    {
        var terminal = atEnd ? points.Count - 1 : 0;
        var neighbour = atEnd ? points.Count - 2 : 1;

        var anchor = points[neighbour];
        if (!Blocked(anchor, points[terminal], blockers))
        {
            return;
        }

        // Approach along whichever axis leaves the anchor outside the node's span, so the final leg runs
        // straight into a border rather than skimming along one.
        var vertical = anchor.Y < node.Top - clearance || anchor.Y > node.Bottom + clearance;

        var corner = vertical
            ? new Position(node.CenterX, anchor.Y)
            : new Position(anchor.X, node.CenterY);

        var border = vertical
            ? new Position(node.CenterX, anchor.Y < node.CenterY ? node.Top : node.Bottom)
            : new Position(anchor.X < node.CenterX ? node.Left : node.Right, node.CenterY);

        if (Blocked(anchor, corner, blockers) || Blocked(corner, border, blockers))
        {
            return;
        }

        points[terminal] = border;
        points.Insert(atEnd ? terminal : 1, corner);
    }

    static bool Blocked(Position a, Position b, Blockers blockers)
    {
        // The box around the segment: a blocker that lies wholly to one side of it cannot be crossed, and
        // saying so takes four comparisons where the clipping test takes a good deal more.
        var left = Math.Min(a.X, b.X);
        var right = Math.Max(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var bottom = Math.Max(a.Y, b.Y);

        var grown = blockers.Grown;
        for (var i = 0; i < grown.Length; i++)
        {
            if (i == blockers.Source ||
                i == blockers.Target)
            {
                continue;
            }

            ref readonly var blocker = ref grown[i];
            if (blocker.Left > right ||
                blocker.Right < left ||
                blocker.Top > bottom ||
                blocker.Bottom < top)
            {
                continue;
            }

            if (blocker.Crosses(a, b))
            {
                return true;
            }
        }

        return false;
    }

    readonly record struct Rect(double Left, double Top, double Right, double Bottom)
    {
        public static Rect Around(Node node) =>
            new(
                node.Position.X - node.Width / 2,
                node.Position.Y - node.Height / 2,
                node.Position.X + node.Width / 2,
                node.Position.Y + node.Height / 2);

        public double CenterX => (Left + Right) / 2;

        public double CenterY => (Top + Bottom) / 2;

        public Rect Grow(double amount) =>
            new(Left - amount, Top - amount, Right + amount, Bottom + amount);

        /// <summary>Whether the segment a-b passes through this rectangle (Liang-Barsky clipping).</summary>
        public bool Crosses(Position a, Position b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            double enter = 0;
            double exit = 1;

            Span<double> deltas = [-dx, dx, -dy, dy];
            Span<double> distances = [a.X - Left, Right - a.X, a.Y - Top, Bottom - a.Y];

            for (var i = 0; i < 4; i++)
            {
                if (deltas[i] == 0)
                {
                    if (distances[i] < 0)
                    {
                        return false;
                    }

                    continue;
                }

                var t = distances[i] / deltas[i];
                if (deltas[i] < 0)
                {
                    enter = Math.Max(enter, t);
                }
                else
                {
                    exit = Math.Min(exit, t);
                }
            }

            return enter < exit;
        }
    }
}
