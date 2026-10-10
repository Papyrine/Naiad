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
    public LayoutResult BuildLayout(GraphDiagramBase diagram, LayoutOptions options)
    {
        if (diagram.Nodes.Count == 0 &&
            diagram.Subgraphs.Count == 0)
        {
            return new() { Width = 0, Height = 0 };
        }

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
            AddSubgraph(graph, subgraph, subgraphLabels);
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
            var source = ClusterAnchor(clusters, edge.SourceId, edge.TargetId);
            var target = ClusterAnchor(clusters, edge.TargetId, edge.SourceId);

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
    static string ClusterAnchor(Dictionary<string, Subgraph> clusters, string id, string otherEnd)
    {
        if (!clusters.TryGetValue(id, out var cluster))
        {
            return id;
        }

        return FirstLeaf(cluster, otherEnd) ?? FirstLeaf(cluster, null) ?? id;
    }

    static string? FirstLeaf(Subgraph subgraph, string? except)
    {
        foreach (var nodeId in subgraph.NodeIds)
        {
            if (nodeId != except)
            {
                return nodeId;
            }
        }

        foreach (var nested in subgraph.NestedSubgraphs)
        {
            if (FirstLeaf(nested, except) is { } leaf)
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

    static void AddSubgraph(Graph graph, Subgraph subgraph, List<(Subgraph, NodeLabel)> collected)
    {
        // A subgraph with nothing in it is a leaf to dagre, which sizes a cluster from its children and so
        // would leave this one a point. It keeps whatever size its renderer gave it instead.
        var label = new NodeLabel();
        if (subgraph.NodeIds.Count == 0 &&
            subgraph.NestedSubgraphs.Count == 0)
        {
            label.Width = subgraph.Width;
            label.Height = subgraph.Height;
        }

        graph.SetNode(subgraph.Id, label);
        collected.Add((subgraph, label));

        foreach (var nodeId in subgraph.NodeIds)
        {
            graph.SetParent(nodeId, subgraph.Id);
        }

        foreach (var nested in subgraph.NestedSubgraphs)
        {
            AddSubgraph(graph, nested, collected);
            graph.SetParent(nested.Id, subgraph.Id);
        }
    }
}
