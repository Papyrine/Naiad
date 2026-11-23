/// <summary>
/// Geometry checks over a drawn graph diagram: what is drawn on top of what, where lines stop, and what
/// falls off the canvas. Each problem is one violation, per offending pair or element, so a count says
/// how many places are wrong rather than only whether anything is. Matters of taste (bends, jogs,
/// balance) are left out: every violation here is something a reader sees as broken.
/// </summary>
static class LayoutLint
{
    public const string ClusterOverlap = "cluster-overlap";
    public const string NodeOverlap = "node-overlap";
    public const string EdgeThroughNode = "edge-through-node";
    public const string EdgeThroughCluster = "edge-through-cluster";
    public const string EdgeEnd = "edge-end";
    public const string LabelOverlap = "label-overlap";
    public const string Canvas = "canvas";

    public static readonly string[] Checks =
    [
        ClusterOverlap,
        NodeOverlap,
        EdgeThroughNode,
        EdgeThroughCluster,
        EdgeEnd,
        LabelOverlap,
        Canvas
    ];

    // Boxes that meet within this are touching rather than overlapping, and a box within this of another's
    // border is inside it: the layouts place things edge to edge, and their arithmetic rounds.
    const double margin = 1;

    // How far inside a node's outline, or a cluster's box, a line must reach to count as passing through
    // it, so a line that grazes the border, or ends a fraction inside it, does not.
    const double inset = 2;

    // How far from the border of the node or cluster it belongs to an edge's end may stop, on either side.
    const double endTolerance = 3;

    // The drawing's coordinates are written to two decimal places.
    const double canvasTolerance = 0.5;

    public record Violation(string Check, string Detail);

    public static List<Violation> Run(DrawnLayout layout)
    {
        var violations = new List<Violation>();
        CheckClusters(layout, violations);
        CheckNodes(layout, violations);
        CheckEdgesThroughNodes(layout, violations);
        CheckEdgesThroughClusters(layout, violations);
        CheckEdgeEnds(layout, violations);
        CheckLabels(layout, violations);
        CheckCanvas(layout, violations);
        return violations;
    }

    // Clusters nest or keep apart, and a node sits wholly inside a cluster or wholly outside it. A box that
    // straddles a cluster's border reads as belonging to both sides of it, or to neither.
    static void CheckClusters(DrawnLayout layout, List<Violation> violations)
    {
        var clusters = layout.Clusters;
        for (var i = 0; i < clusters.Count; i++)
        {
            for (var j = i + 1; j < clusters.Count; j++)
            {
                var a = clusters[i];
                var b = clusters[j];
                if (Overlaps(a.Box, b.Box) &&
                    !Contains(a.Box, b.Box) &&
                    !Contains(b.Box, a.Box))
                {
                    violations.Add(new(ClusterOverlap, $"clusters {a.Id} and {b.Id} partly overlap"));
                }
            }
        }

        foreach (var cluster in clusters)
        {
            foreach (var node in layout.Nodes)
            {
                if (Overlaps(node.Box, cluster.Box) &&
                    !Contains(cluster.Box, node.Box))
                {
                    violations.Add(new(ClusterOverlap, $"node {node.Id} straddles the border of cluster {cluster.Id}"));
                }
            }
        }
    }

    // Nodes are compared by their boxes: the layouts keep boxes apart, whatever shape is drawn inside one.
    static void CheckNodes(DrawnLayout layout, List<Violation> violations)
    {
        var nodes = layout.Nodes;
        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = i + 1; j < nodes.Count; j++)
            {
                if (Overlaps(nodes[i].Box, nodes[j].Box))
                {
                    violations.Add(new(NodeOverlap, $"nodes {nodes[i].Id} and {nodes[j].Id} overlap"));
                }
            }
        }
    }

    // A line under a node is hidden by it, or shows through it, and either way reads as joining it. The
    // node's own outline is what the line is checked against, so passing the empty corner of a diamond's
    // or a circle's box is not counted.
    static void CheckEdgesThroughNodes(DrawnLayout layout, List<Violation> violations)
    {
        foreach (var edge in layout.Edges)
        {
            foreach (var node in layout.Nodes)
            {
                if (node.Id == edge.SourceId ||
                    node.Id == edge.TargetId)
                {
                    continue;
                }

                if (PassesThrough(edge.Points, node.Box, node.Outline))
                {
                    violations.Add(new(EdgeThroughNode, $"edge {edge.SourceId}->{edge.TargetId} passes through node {node.Id}"));
                }
            }
        }
    }

    // A line crosses into a cluster to reach something inside it. One that runs through a cluster holding
    // neither of its ends reads as reaching into it when it does not. The State renderer's own self-check
    // counts this as a line under a node, since it draws a composite state as one.
    static void CheckEdgesThroughClusters(DrawnLayout layout, List<Violation> violations)
    {
        var boxes = new Dictionary<string, Rect>(StringComparer.Ordinal);
        foreach (var node in layout.Nodes)
        {
            boxes.TryAdd(node.Id, node.Box);
        }

        foreach (var cluster in layout.Clusters)
        {
            boxes.TryAdd(cluster.Id, cluster.Box);
        }

        foreach (var edge in layout.Edges)
        {
            foreach (var cluster in layout.Clusters)
            {
                if (Holds(cluster, edge.SourceId) ||
                    Holds(cluster, edge.TargetId))
                {
                    continue;
                }

                if (PassesThrough(edge.Points, cluster.Box, DrawnLayouts.Corners(cluster.Box)))
                {
                    violations.Add(new(EdgeThroughCluster, $"edge {edge.SourceId}->{edge.TargetId} passes through cluster {cluster.Id}"));
                }
            }
        }

        // Whether the end is the cluster, or drawn inside it. Going by the drawing rather than by declared
        // membership keeps this to what a reader can see.
        bool Holds(DrawnCluster cluster, string id) =>
            id == cluster.Id ||
            (boxes.TryGetValue(id, out var box) && Contains(cluster.Box, box));
    }

    // Each end of an edge stops on the border of what it joins: the node's outline (or its box, for an
    // end the renderer left on the box), or the cluster's border for an edge that joins a cluster. An end
    // short of it leaves the line in mid-air; one past it runs under the node it belongs to.
    static void CheckEdgeEnds(DrawnLayout layout, List<Violation> violations)
    {
        var nodes = new Dictionary<string, DrawnNode>(StringComparer.Ordinal);
        foreach (var node in layout.Nodes)
        {
            nodes.TryAdd(node.Id, node);
        }

        var clusters = new Dictionary<string, DrawnCluster>(StringComparer.Ordinal);
        foreach (var cluster in layout.Clusters)
        {
            clusters.TryAdd(cluster.Id, cluster);
        }

        foreach (var edge in layout.Edges)
        {
            CheckEnd(edge, "start", edge.Points[0], edge.SourceId);
            CheckEnd(edge, "end", edge.Points[^1], edge.TargetId);
        }

        void CheckEnd(DrawnEdge edge, string end, Position point, string id)
        {
            if (DistanceToBorder(point, id) is { } distance and > endTolerance)
            {
                violations.Add(new(EdgeEnd, $"edge {edge.SourceId}->{edge.TargetId} {end} stops {distance:0.#}px from the border of {id}"));
            }
        }

        // Null for an end joined to neither a node nor a cluster, which has no border to be on.
        double? DistanceToBorder(Position point, string id)
        {
            if (nodes.TryGetValue(id, out var node))
            {
                return Math.Min(DistanceToOutline(point, node.Outline), DistanceToOutline(point, DrawnLayouts.Corners(node.Box)));
            }

            if (clusters.TryGetValue(id, out var cluster))
            {
                return DistanceToOutline(point, DrawnLayouts.Corners(cluster.Box));
            }

            return null;
        }
    }

    // A label is drawn on an opaque chip, so whatever it overlaps is hidden, or hides it.
    static void CheckLabels(DrawnLayout layout, List<Violation> violations)
    {
        var labels = layout.Labels;
        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            foreach (var node in layout.Nodes)
            {
                if (Overlaps(label.Box, node.Box))
                {
                    violations.Add(new(LabelOverlap, $"label '{label.Text}' overlaps node {node.Id}"));
                }
            }

            for (var j = i + 1; j < labels.Count; j++)
            {
                if (Overlaps(label.Box, labels[j].Box))
                {
                    violations.Add(new(LabelOverlap, $"labels '{label.Text}' and '{labels[j].Text}' overlap"));
                }
            }
        }
    }

    // Anything outside the viewBox is cut off.
    static void CheckCanvas(DrawnLayout layout, List<Violation> violations)
    {
        var canvas = layout.Canvas;

        bool Inside(Position point) =>
            point.X >= canvas.Left - canvasTolerance &&
            point.X <= canvas.Right + canvasTolerance &&
            point.Y >= canvas.Top - canvasTolerance &&
            point.Y <= canvas.Bottom + canvasTolerance;

        bool BoxInside(Rect box) =>
            Inside(new(box.Left, box.Top)) &&
            Inside(new(box.Right, box.Bottom));

        foreach (var node in layout.Nodes.Where(_ => !BoxInside(_.Box)))
        {
            violations.Add(new(Canvas, $"node {node.Id} is off the canvas"));
        }

        foreach (var cluster in layout.Clusters.Where(_ => !BoxInside(_.Box)))
        {
            violations.Add(new(Canvas, $"cluster {cluster.Id} is off the canvas"));
        }

        foreach (var label in layout.Labels.Where(_ => !BoxInside(_.Box)))
        {
            violations.Add(new(Canvas, $"label '{label.Text}' is off the canvas"));
        }

        foreach (var edge in layout.Edges.Where(_ => !_.Points.TrueForAll(Inside)))
        {
            violations.Add(new(Canvas, $"edge {edge.SourceId}->{edge.TargetId} runs off the canvas"));
        }
    }

    static bool Overlaps(Rect a, Rect b) =>
        a.Left < b.Right - margin &&
        b.Left < a.Right - margin &&
        a.Top < b.Bottom - margin &&
        b.Top < a.Bottom - margin;

    static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - margin &&
        inner.Right <= outer.Right + margin &&
        inner.Top >= outer.Top - margin &&
        inner.Bottom <= outer.Bottom + margin;

    // Walks each segment a pixel at a time, looking for a point deeper inside the outline than the inset.
    static bool PassesThrough(List<Position> points, Rect box, Position[] outline)
    {
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var from = points[i];
            var to = points[i + 1];

            // Nothing of a segment whose bounds miss the inset box can be deep inside the outline.
            if (Math.Max(from.X, to.X) <= box.Left + inset ||
                Math.Min(from.X, to.X) >= box.Right - inset ||
                Math.Max(from.Y, to.Y) <= box.Top + inset ||
                Math.Min(from.Y, to.Y) >= box.Bottom - inset)
            {
                continue;
            }

            var steps = Math.Max(1, (int)Math.Ceiling(Distance(from, to)));
            for (var step = 0; step <= steps; step++)
            {
                var t = (double)step / steps;
                var point = new Position(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
                if (IsInside(point, outline) &&
                    DistanceToOutline(point, outline) > inset)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Even-odd ray casting, so a concave outline is handled as well as a convex one.
    static bool IsInside(Position point, Position[] outline)
    {
        var inside = false;
        for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
        {
            var a = outline[i];
            var b = outline[j];
            if (a.Y > point.Y != b.Y > point.Y &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    static double DistanceToOutline(Position point, Position[] outline)
    {
        var nearest = double.MaxValue;
        for (var i = 0; i < outline.Length; i++)
        {
            nearest = Math.Min(nearest, DistanceToSegment(point, outline[i], outline[(i + 1) % outline.Length]));
        }

        return nearest;
    }

    static double DistanceToSegment(Position point, Position a, Position b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-12)
        {
            return Distance(point, a);
        }

        var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(point, new(a.X + dx * t, a.Y + dy * t));
    }

    static double Distance(Position a, Position b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
