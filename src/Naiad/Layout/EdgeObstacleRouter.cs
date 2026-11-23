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

        ClearClusters(diagram, grown, indexById);
    }

    // How far a drawn curve may reach into a cluster it does not belong to before it counts as running
    // through it, so one that grazes a border is left alone.
    const double clusterInset = 2;

    /// <summary>
    /// Dagre runs a long edge down beside a cluster it passes and turns it towards its target on the rank of
    /// the cluster's border. The route stays outside, but the drawn curve, a B-spline that cuts its corners,
    /// rounds that turn through the cluster's corner: in <c>ComplexPipeline</c> five edges ran through
    /// subgraphs that held neither of their ends. Such a turn gets a point that carries the straight run on
    /// past the border, so the curve turns outside. Only an edge whose curve enters such a cluster is touched,
    /// and the point is kept only if the curve then enters fewer of them and the new legs clear every node.
    /// </summary>
    static void ClearClusters(GraphDiagramBase diagram, Rect[] grown, Dictionary<string, int> indexById)
    {
        var clusters = Clusters(diagram.Subgraphs);
        if (clusters.Count == 0)
        {
            return;
        }

        var foreign = new List<Rect>();
        foreach (var edge in diagram.Edges)
        {
            if (edge.Points.Count < 3)
            {
                continue;
            }

            foreign.Clear();
            foreach (var (box, members) in clusters)
            {
                if (!members.Contains(edge.SourceId) &&
                    !members.Contains(edge.TargetId))
                {
                    foreign.Add(box.Grow(-clusterInset));
                }
            }

            if (foreign.Count == 0)
            {
                continue;
            }

            // An end that is a subgraph has no node box of its own to be skipped.
            var source = indexById.GetValueOrDefault(edge.SourceId, -1);
            var target = indexById.GetValueOrDefault(edge.TargetId, -1);
            var blockers = new Blockers(grown, source, target);

            // Each pass turns one corner outside; an edge passing several clusters may need a few.
            for (var pass = 0; pass < 4; pass++)
            {
                if (!TurnOutside(edge.Points, foreign, blockers))
                {
                    break;
                }
            }
        }
    }

    // Adds a point to the turn nearest where the curve first enters a foreign cluster, if one helps.
    static bool TurnOutside(List<Position> points, List<Rect> foreign, Blockers blockers)
    {
        var flat = EdgePath.Flatten(points);
        if (FirstEntry(flat, foreign) is not { } entry)
        {
            return false;
        }

        var entered = Entered(flat, foreign);

        // The turn the curve rounds into the cluster is the waypoint nearest where it goes in.
        var turn = 1;
        for (var i = 2; i < points.Count - 1; i++)
        {
            if (Distance(points[i], entry) < Distance(points[turn], entry))
            {
                turn = i;
            }
        }

        var before = points[turn - 1];
        var at = points[turn];
        var after = points[turn + 1];

        // The straight run leads into the turn or away from it: carry it on past the turn, on whichever side
        // the long leg is not.
        Span<(Position Corner, int Index, Position From, Position To)> candidates =
        [
            (Beyond(before, at, after), turn + 1, at, after),
            (Beyond(after, at, before), turn, before, at)
        ];

        foreach (var (corner, index, from, to) in candidates)
        {
            if (double.IsNaN(corner.X) ||
                Blocked(from, corner, blockers) ||
                Blocked(corner, to, blockers))
            {
                continue;
            }

            points.Insert(index, corner);
            if (Entered(EdgePath.Flatten(points), foreign) < entered)
            {
                return true;
            }

            points.RemoveAt(index);
        }

        return false;
    }

    // The point the clearance past `at` along the line from `from`, or NaN when there is no line to follow or
    // the far leg is too short to take the step.
    static Position Beyond(Position from, Position at, Position far)
    {
        var length = Distance(from, at);
        if (length == 0 ||
            Distance(at, far) < clearance * 2)
        {
            return new(double.NaN, double.NaN);
        }

        return new(
            at.X + (at.X - from.X) / length * clearance,
            at.Y + (at.Y - from.Y) / length * clearance);
    }

    static Position? FirstEntry(List<Position> curve, List<Rect> boxes)
    {
        for (var i = 1; i < curve.Count; i++)
        {
            foreach (var box in boxes)
            {
                if (box.Crosses(curve[i - 1], curve[i]))
                {
                    return new((curve[i - 1].X + curve[i].X) / 2, (curve[i - 1].Y + curve[i].Y) / 2);
                }
            }
        }

        return null;
    }

    // How many of the boxes the curve enters.
    static int Entered(List<Position> curve, List<Rect> boxes)
    {
        var count = 0;
        foreach (var box in boxes)
        {
            for (var i = 1; i < curve.Count; i++)
            {
                if (box.Crosses(curve[i - 1], curve[i]))
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    static double Distance(Position a, Position b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // Each cluster's box, with the ids of everything inside it: its nodes and subgraphs at any depth, and its
    // own id, for an edge that ends on the cluster itself. Walked with a stack of its own.
    static List<(Rect Box, HashSet<string> Members)> Clusters(List<Subgraph> subgraphs)
    {
        var clusters = new List<(Rect Box, HashSet<string> Members)>();
        var pending = new Stack<Subgraph>(subgraphs);
        while (pending.TryPop(out var subgraph))
        {
            var members = new HashSet<string>(StringComparer.Ordinal);
            var inside = new Stack<Subgraph>();
            inside.Push(subgraph);
            while (inside.TryPop(out var nested))
            {
                members.Add(nested.Id);
                members.UnionWith(nested.NodeIds);
                foreach (var deeper in nested.NestedSubgraphs)
                {
                    inside.Push(deeper);
                }
            }

            clusters.Add(
                (new(
                    subgraph.Position.X - subgraph.Width / 2,
                    subgraph.Position.Y - subgraph.Height / 2,
                    subgraph.Position.X + subgraph.Width / 2,
                    subgraph.Position.Y + subgraph.Height / 2),
                    members));
            foreach (var nested in subgraph.NestedSubgraphs)
            {
                pending.Push(nested);
            }
        }

        return clusters;
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
            ? anchor with { X = node.CenterX }
            : anchor with { Y = node.CenterY };

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
