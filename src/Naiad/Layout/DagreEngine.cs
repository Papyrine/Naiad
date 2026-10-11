/// <summary>
/// Lays out a node/edge diagram with the faithful C# port of dagre (<c>Naiad.Dagre</c>) — the same
/// layered/Sugiyama engine Mermaid uses. The diagram is mapped to a compound dagre graph (subgraphs become
/// compound parent nodes), laid out, and the resulting node positions, cluster boxes and routed edge points
/// are read back. This gives Mermaid-equivalent ranking, crossing minimisation, cluster nesting and
/// shape-avoiding edge routing. Dagre aims an edge's end segment straight at its target's border without
/// checking what lies in between, so <see cref="EdgeObstacleRouter"/> re-aims the few that would cut across
/// a node sharing the target's rank.
/// </summary>
/// <remarks>
/// Read-back relies on a contract of <see cref="Layout.Run"/>: it writes the final layout
/// (positions, cluster sizes, routed points) back onto the very label instances passed in here via
/// <c>UpdateInputGraph</c>, rather than returning fresh labels. So we keep a reference to each label we
/// hand to the graph and read results straight off it — no second keyed lookup. If a future re-sync with
/// upstream dagre changes that to return new labels, this read-back must switch back to per-id lookups.
/// </remarks>
class DagreEngine : ILayoutEngine
{
    // Room kept between a subgraph laid out on its own and its box: the title band above, a margin elsewhere.
    const double independentPadding = 20;
    const double independentTitleBand = 40;

    static readonly HashSet<Subgraph> noneCollapsed = [];

    public LayoutResult BuildLayout(GraphDiagramBase diagram, LayoutOptions options)
    {
        if (diagram.Nodes.Count == 0 &&
            diagram.Subgraphs.Count == 0)
        {
            return new() { Width = 0, Height = 0 };
        }

        if (!options.SubgraphDirections)
        {
            return LayOut(diagram, options, noneCollapsed);
        }

        var independent = new List<(Subgraph Subgraph, HashSet<string> Members)>();
        FindIndependent(diagram, diagram.Subgraphs, options.Direction, independent);
        if (independent.Count == 0)
        {
            return LayOut(diagram, options, noneCollapsed);
        }

        // Each independent subgraph is a diagram of its own, laid out in its own direction. It then takes
        // part in the layout around it as a single box of the size that came to, and its contents are
        // moved into the box once the box has a place.
        var inside = new HashSet<string>(StringComparer.Ordinal);
        var collapsed = new HashSet<Subgraph>();
        var parts = new List<(Subgraph Subgraph, PartialGraph Part)>();
        foreach (var (subgraph, members) in independent)
        {
            var part = new PartialGraph();
            foreach (var node in diagram.Nodes)
            {
                if (members.Contains(node.Id))
                {
                    part.AddNode(node);
                }
            }

            foreach (var edge in diagram.Edges)
            {
                if (members.Contains(edge.SourceId) &&
                    members.Contains(edge.TargetId))
                {
                    part.Edges.Add(edge);
                }
            }

            part.Subgraphs.AddRange(subgraph.NestedSubgraphs);

            var size = BuildLayout(
                part,
                new()
                {
                    Direction = subgraph.Direction,
                    NodeSeparation = options.NodeSeparation,
                    RankSeparation = options.RankSeparation,
                    SubgraphDirections = true
                });

            subgraph.Width = size.Width + independentPadding * 2;
            subgraph.Height = size.Height + independentTitleBand + independentPadding;

            inside.UnionWith(members);
            collapsed.Add(subgraph);
            parts.Add((subgraph, part));
        }

        var outer = new PartialGraph();
        foreach (var node in diagram.Nodes)
        {
            if (!inside.Contains(node.Id))
            {
                outer.AddNode(node);
            }
        }

        foreach (var edge in diagram.Edges)
        {
            if (!inside.Contains(edge.SourceId))
            {
                outer.Edges.Add(edge);
            }
        }

        outer.Subgraphs.AddRange(diagram.Subgraphs);

        var result = LayOut(outer, options, collapsed);

        foreach (var (subgraph, part) in parts)
        {
            var dx = subgraph.Bounds.X + independentPadding;
            var dy = subgraph.Bounds.Y + independentTitleBand;

            foreach (var node in part.Nodes)
            {
                node.Position = new(node.Position.X + dx, node.Position.Y + dy);
            }

            foreach (var edge in part.Edges)
            {
                edge.Offset(dx, dy);
            }

            OffsetSubgraphs(part.Subgraphs, dx, dy);
        }

        return result;
    }

    sealed class PartialGraph : GraphDiagramBase;

    static void OffsetSubgraphs(List<Subgraph> subgraphs, double dx, double dy)
    {
        foreach (var subgraph in subgraphs)
        {
            subgraph.Position = new(subgraph.Position.X + dx, subgraph.Position.Y + dy);
            OffsetSubgraphs(subgraph.NestedSubgraphs, dx, dy);
        }
    }

    /// <summary>
    /// Finds the subgraphs to lay out on their own: those that ask for a direction other than the one
    /// around them and have no link between a member and anything outside. A subgraph that qualifies is
    /// not searched further - laying it out finds any inside it - and one that does not passes the
    /// direction around it on to the subgraphs it holds.
    /// </summary>
    static void FindIndependent(
        GraphDiagramBase diagram,
        List<Subgraph> subgraphs,
        Direction enclosing,
        List<(Subgraph, HashSet<string>)> found)
    {
        foreach (var subgraph in subgraphs)
        {
            if (subgraph.Direction != enclosing)
            {
                var members = new HashSet<string>(StringComparer.Ordinal);
                AddMembers(subgraph, members);
                if (members.Count > 0 &&
                    IsSelfContained(diagram, members))
                {
                    found.Add((subgraph, members));
                    continue;
                }
            }

            FindIndependent(diagram, subgraph.NestedSubgraphs, enclosing, found);
        }
    }

    static void AddMembers(Subgraph subgraph, HashSet<string> members)
    {
        foreach (var nodeId in subgraph.NodeIds)
        {
            members.Add(nodeId);
        }

        foreach (var nested in subgraph.NestedSubgraphs)
        {
            members.Add(nested.Id);
            AddMembers(nested, members);
        }
    }

    static bool IsSelfContained(GraphDiagramBase diagram, HashSet<string> members)
    {
        foreach (var edge in diagram.Edges)
        {
            if (members.Contains(edge.SourceId) != members.Contains(edge.TargetId))
            {
                return false;
            }
        }

        return true;
    }

    // Lays out one graph in one direction. A collapsed subgraph takes part as a box of the size it
    // already has, and nothing inside it is looked at.
    static LayoutResult LayOut(GraphDiagramBase diagram, LayoutOptions options, HashSet<Subgraph> collapsed)
    {
        var graph = new Graph(directed: true, multigraph: true, compound: true);
        graph.SetGraph(new()
        {
            Rankdir = options.Direction,
            NodeSeparation = options.NodeSeparation,
            RankSeparation = options.RankSeparation
        });
        graph.SetDefaultEdgeLabel(new EdgeLabel());

        var nodeLabels = new List<(Node Node, NodeLabel Label)>(diagram.Nodes.Count);
        foreach (var node in diagram.Nodes)
        {
            var label = new NodeLabel
            {
                Width = node.Width,
                Height = node.Height
            };
            graph.SetNode(node.Id, label);
            nodeLabels.Add((node, label));
        }

        var subgraphLabels = new List<(Subgraph Subgraph, NodeLabel Label)>();
        foreach (var subgraph in diagram.Subgraphs)
        {
            AddSubgraph(graph, subgraph, subgraphLabels, collapsed);
        }

        var clusters = new Dictionary<string, Subgraph>(subgraphLabels.Count, StringComparer.Ordinal);
        foreach (var (subgraph, _) in subgraphLabels)
        {
            clusters[subgraph.Id] = subgraph;
        }

        var edgeLabels = new List<(Edge Edge, EdgeLabel Label)>(diagram.Edges.Count);
        for (var i = 0; i < diagram.Edges.Count; i++)
        {
            var edge = diagram.Edges[i];
            var label = new EdgeLabel();
            if (!string.IsNullOrEmpty(edge.Label))
            {
                label.Width = edge.LabelWidth;
                label.Height = edge.LabelHeight;
                label.Labelpos = LabelPosition.Center;
            }

            if (edge.MinLength > 1)
            {
                label.Minlen = edge.MinLength;
            }

            // Dagre cannot rank an edge that ends on a cluster, so an edge to or from a subgraph is laid out
            // against one of the nodes inside it and cut back to the subgraph's border afterwards.
            var source = ClusterAnchor(clusters, edge.SourceId, edge.TargetId, collapsed);
            var target = ClusterAnchor(clusters, edge.TargetId, edge.SourceId, collapsed);

            // A unique per-edge name keeps parallel edges (same source/target) distinct in the multigraph.
            graph.SetEdge(source, target, label, "e" + i.ToString(CultureInfo.InvariantCulture));
            edgeLabels.Add((edge, label));
        }

        Layout.Run(graph);

        foreach (var (node, label) in nodeLabels)
        {
            node.Position = new(label.X ?? 0, label.Y ?? 0);
        }

        foreach (var (subgraph, label) in subgraphLabels)
        {
            subgraph.Position = new(label.X ?? 0, label.Y ?? 0);
            subgraph.Width = label.Width;
            subgraph.Height = label.Height;
        }

        foreach (var (edge, label) in edgeLabels)
        {
            edge.Points.Clear();
            if (label.Points != null)
            {
                edge.Points.AddRange(label.Points);
            }

            // Dagre keeps a slot clear for each label and reports its centre. Read it back rather than
            // re-deriving the position from the route: the obstacle router below adds a corner to some
            // routes, which moves their midpoint off the slot that was reserved.
            edge.ClearLabelPosition();
            if (label is { Width: > 0, X: { } labelX, Y: { } labelY })
            {
                edge.LabelPosition = new(labelX, labelY);
            }
        }

        EdgeObstacleRouter.Route(diagram);

        if (clusters.Count > 0)
        {
            foreach (var edge in diagram.Edges)
            {
                if (clusters.TryGetValue(edge.TargetId, out var targetCluster))
                {
                    CutAtBorder(edge.Points, targetCluster.Bounds, atEnd: true);
                }

                if (clusters.TryGetValue(edge.SourceId, out var sourceCluster))
                {
                    CutAtBorder(edge.Points, sourceCluster.Bounds, atEnd: false);
                }
            }
        }

        var graphLabel = graph.Label;
        return new()
        {
            Width = graphLabel.Width ?? 0,
            Height = graphLabel.Height ?? 0
        };
    }

    /// <summary>
    /// The node an edge end is laid out against. For a plain node that is the node itself; for a subgraph
    /// it is a node inside it, preferring one that is not the edge's other end so an edge between a
    /// subgraph and one of its own members does not collapse into a self-loop. An empty subgraph has no
    /// node to stand in for it, and being a leaf as far as dagre is concerned it needs none.
    /// </summary>
    static string ClusterAnchor(
        Dictionary<string, Subgraph> clusters,
        string id,
        string otherEnd,
        HashSet<Subgraph> collapsed)
    {
        if (!clusters.TryGetValue(id, out var cluster))
        {
            return id;
        }

        return FirstLeaf(cluster, otherEnd, collapsed) ?? FirstLeaf(cluster, null, collapsed) ?? id;
    }

    // A collapsed subgraph is a leaf itself: the nodes inside it are not in this layout.
    static string? FirstLeaf(Subgraph subgraph, string? except, HashSet<Subgraph> collapsed)
    {
        if (collapsed.Contains(subgraph))
        {
            return subgraph.Id;
        }

        foreach (var nodeId in subgraph.NodeIds)
        {
            if (nodeId != except)
            {
                return nodeId;
            }
        }

        foreach (var nested in subgraph.NestedSubgraphs)
        {
            if (FirstLeaf(nested, except, collapsed) is { } leaf)
            {
                return leaf;
            }
        }

        return null;
    }

    /// <summary>
    /// Shortens a route that was laid out to a node inside a subgraph so that it stops where it crosses the
    /// subgraph's border. <paramref name="atEnd"/> says which end of the route lies inside the box. A route
    /// that never leaves the box - its other end is a member of the same subgraph - is left as it is.
    /// </summary>
    static void CutAtBorder(List<Position> points, Naiad.Rect box, bool atEnd)
    {
        if (!atEnd)
        {
            points.Reverse();
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var outside = points[i];
            var inside = points[i + 1];
            if (Contains(box, outside) ||
                !Contains(box, inside))
            {
                continue;
            }

            points.RemoveRange(i + 1, points.Count - i - 1);
            points.Add(BorderCrossing(box, outside, inside));
            break;
        }

        if (!atEnd)
        {
            points.Reverse();
        }
    }

    static bool Contains(Naiad.Rect box, Position point) =>
        point.X > box.Left &&
        point.X < box.Right &&
        point.Y > box.Top &&
        point.Y < box.Bottom;

    // Where the segment from a point outside the box to one inside it meets the border: the furthest along
    // the segment that any of the four sides it actually crosses is reached.
    static Position BorderCrossing(Naiad.Rect box, Position outside, Position inside)
    {
        var dx = inside.X - outside.X;
        var dy = inside.Y - outside.Y;
        double enter = 0;

        if (dx > 0)
        {
            enter = Math.Max(enter, (box.Left - outside.X) / dx);
        }
        else if (dx < 0)
        {
            enter = Math.Max(enter, (box.Right - outside.X) / dx);
        }

        if (dy > 0)
        {
            enter = Math.Max(enter, (box.Top - outside.Y) / dy);
        }
        else if (dy < 0)
        {
            enter = Math.Max(enter, (box.Bottom - outside.Y) / dy);
        }

        return new(outside.X + dx * enter, outside.Y + dy * enter);
    }

    static void AddSubgraph(
        Graph graph,
        Subgraph subgraph,
        List<(Subgraph, NodeLabel)> collected,
        HashSet<Subgraph> collapsed)
    {
        // A subgraph with nothing in it is a leaf to dagre, which sizes a cluster from its children and so
        // would leave this one a point. It keeps whatever size its renderer gave it instead. So does one
        // that has been laid out on its own, whose contents take no part here.
        var label = new NodeLabel();
        var isCollapsed = collapsed.Contains(subgraph);
        if (isCollapsed ||
            (subgraph.NodeIds.Count == 0 &&
             subgraph.NestedSubgraphs.Count == 0))
        {
            label.Width = subgraph.Width;
            label.Height = subgraph.Height;
        }

        graph.SetNode(subgraph.Id, label);
        collected.Add((subgraph, label));

        if (isCollapsed)
        {
            return;
        }

        foreach (var nodeId in subgraph.NodeIds)
        {
            graph.SetParent(nodeId, subgraph.Id);
        }

        foreach (var nested in subgraph.NestedSubgraphs)
        {
            AddSubgraph(graph, nested, collected, collapsed);
            graph.SetParent(nested.Id, subgraph.Id);
        }
    }
}
