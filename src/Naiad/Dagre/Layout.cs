/// <summary>Top-level layout driver.</summary>
static class Layout
{
    public static Graph Run(Graph graph)
    {
        var layoutGraph = BuildLayoutGraph(graph);
        RunLayout(layoutGraph);
        UpdateInputGraph(graph, layoutGraph);
        return layoutGraph;
    }

    static void RunLayout(Graph graph)
    {
        MakeSpaceForEdgeLabels(graph);
        RemoveSelfEdges(graph);
        Acyclic.Run(graph);
        NestingGraph.Run(graph);
        NetworkSimplex.Run(Util.AsNonCompoundGraph(graph));
        InjectEdgeLabelProxies(graph);
        Util.RemoveEmptyRanks(graph);
        NestingGraph.Cleanup(graph);
        Util.NormalizeRanks(graph);
        AssignRankMinMax(graph);
        RemoveEdgeLabelProxies(graph);
        Normalize.Run(graph);
        ParentDummyChains.Run(graph);
        AddBorderSegments.Run(graph);
        Order.Run(graph);
        InsertSelfEdges(graph);
        CoordinateSystem.Adjust(graph);
        Positioning.Run(graph);
        PositionSelfEdges(graph);
        RemoveBorderNodes(graph);
        Normalize.Undo(graph);
        FixupEdgeLabelCoords(graph);
        CoordinateSystem.Undo(graph);
        TranslateGraph(graph);
        AssignNodeIntersects(graph);
        ReversePointsForReversedEdges(graph);
        Acyclic.Undo(graph);
    }

    /*
     * Copies final layout information from the layout graph back to the input
     * graph. This process only copies whitelisted attributes from the layout graph
     * to the input graph, so it serves as a good place to determine what
     * attributes can influence layout.
     */
    static void UpdateInputGraph(Graph inputGraph, Graph layoutGraph)
    {
        foreach (var v in inputGraph.Nodes())
        {
            if (!inputGraph.TryGetNodeLabel(v, out var inputLabel))
            {
                continue;
            }

            var layoutLabel = layoutGraph.NodeLabel(v);

            inputLabel.X = layoutLabel.X;
            inputLabel.Y = layoutLabel.Y;
            inputLabel.Order = layoutLabel.Order;
            inputLabel.Rank = layoutLabel.Rank;

            if (layoutGraph.ChildCount(v) != 0)
            {
                inputLabel.Width = layoutLabel.Width;
                inputLabel.Height = layoutLabel.Height;
            }
        }

        foreach (var e in inputGraph.Edges())
        {
            var inputLabel = inputGraph.FindEdgeLabel(e);
            var layoutLabel = layoutGraph.FindEdgeLabel(e);

            inputLabel.Points = layoutLabel.Points;
            if (layoutLabel.X.HasValue)
            {
                inputLabel.X = layoutLabel.X;
                inputLabel.Y = layoutLabel.Y;
            }
        }

        inputGraph.Label.Width = layoutGraph.Label.Width;
        inputGraph.Label.Height = layoutGraph.Label.Height;
    }

    /// <summary>
    /// Builds the graph used for layout: copies only the attributes that can influence layout from the
    /// input graph onto a fresh graph, filling in defaults for anything the caller left unset.
    /// </summary>
    static Graph BuildLayoutGraph(Graph inputGraph)
    {
        var graph = new Graph(multigraph: true, compound: true);
        var graphLabel = inputGraph.Label;

        var newGraph = new GraphLabel
        {
            // defaults
            RankSeparation = 50,
            EdgeSeparation = 20,
            NodeSeparation = 50,
            Rankdir = graphLabel.Rankdir
        };
        if (graphLabel.NodeSeparation is { } gNodeSeparation)
        {
            newGraph.NodeSeparation = gNodeSeparation;
        }

        if (graphLabel.EdgeSeparation is { } gEdgeSeparation)
        {
            newGraph.EdgeSeparation = gEdgeSeparation;
        }

        if (graphLabel.RankSeparation is { } gRankSeparation)
        {
            newGraph.RankSeparation = gRankSeparation;
        }

        graph.SetGraph(newGraph);

        foreach (var v in inputGraph.Nodes())
        {
            // Implicit nodes created via SetParent have no label; default a null label to width/height 0.
            var newNode = inputGraph.TryGetNodeLabel(v, out var node)
                ? new NodeLabel
                {
                    Width = node.Width,
                    Height = node.Height,
                    Rank = node.Rank
                }
                : new NodeLabel {Width = 0, Height = 0};

            graph.SetNode(v, newNode);
            var parent = inputGraph.Parent(v);
            if (parent != null)
            {
                graph.SetParent(v, parent);
            }
        }

        foreach (var e in inputGraph.Edges())
        {
            var edge = inputGraph.FindEdgeLabel(e);
            var newEdge = new EdgeLabel
            {
                // Minlen/Weight stay nullable on input (null = unset); default them here.
                Minlen = 1,
                Weight = 1,
                // Width/Height/Labeloffset/Labelpos are non-nullable with type-level defaults; copy through.
                Width = edge.Width,
                Height = edge.Height,
                Labeloffset = edge.Labeloffset,
                Labelpos = edge.Labelpos
            };
            if (edge.Minlen is { } eMinlen)
            {
                newEdge.Minlen = eMinlen;
            }

            if (edge.Weight is { } eWeight)
            {
                newEdge.Weight = eWeight;
            }

            graph.SetEdge(e, newEdge);
        }

        return graph;
    }

    /*
     * This idea comes from the Gansner paper: to account for edge labels in our
     * layout we split each rank in half by doubling minlen and halving ranksep.
     * Then we can place labels at these mid-points between nodes.
     *
     * We also add some minimal padding to the width to push the label for the edge
     * away from the edge itself a bit.
     */
    static void MakeSpaceForEdgeLabels(Graph graph)
    {
        var graphLabel = graph.Label;
        graphLabel.RankSeparation /= 2;
        foreach (var e in graph.Edges())
        {
            var edge = graph.FindEdgeLabel(e);
            edge.Minlen *= 2;
            if (edge.Labelpos == LabelPosition.Center)
            {
                continue;
            }

            if (graphLabel.Rankdir is Direction.TopToBottom or Direction.BottomToTop)
            {
                edge.Width += edge.Labeloffset;
            }
            else
            {
                edge.Height += edge.Labeloffset;
            }
        }
    }

    /*
     * Creates temporary dummy nodes that capture the rank in which each edge's
     * label is going to, if it has one of non-zero width and height. We do this
     * so that we can safely remove empty ranks while preserving balance for the
     * label's position.
     */
    static void InjectEdgeLabelProxies(Graph graph)
    {
        foreach (var e in graph.Edges())
        {
            var edge = graph.FindEdgeLabel(e);
            if (edge.Width == 0 || edge.Height == 0)
            {
                continue;
            }

            var v = graph.NodeLabel(e.V);
            var w = graph.NodeLabel(e.W);
            // {rank: (w.rank - v.rank) / 2 + v.rank, e: e}
            var label = new NodeLabel
            {
                Rank = (w.Rank!.Value - v.Rank!.Value) / 2 + v.Rank!.Value,
                EdgeKey = e
            };
            Util.AddDummyNode(graph, DummyKind.EdgeProxy, label, DummyNames.EdgeProxy);
        }
    }

    static void AssignRankMinMax(Graph graph)
    {
        foreach (var node in graph.NodeLabels())
        {
            if (node.BorderTop != null)
            {
                node.MinRank = graph.NodeLabel(node.BorderTop).Rank;
                node.MaxRank = graph.NodeLabel(node.BorderBottom!).Rank;
            }
        }
    }

    static void RemoveEdgeLabelProxies(Graph graph)
    {
        foreach (var (v, node) in graph.NodeEntries())
        {
            if (node.Dummy == DummyKind.EdgeProxy)
            {
                graph.FindEdgeLabel(node.EdgeKey!).LabelRank = node.Rank;
                graph.RemoveNode(v);
            }
        }
    }

    static void TranslateGraph(Graph graph)
    {
        var minX = double.PositiveInfinity;
        var maxX = 0.0;
        var minY = double.PositiveInfinity;
        var maxY = 0.0;
        var graphLabel = graph.Label;

        void GetExtremes(double x, double y, double w, double h)
        {
            minX = Math.Min(minX, x - w / 2);
            maxX = Math.Max(maxX, x + w / 2);
            minY = Math.Min(minY, y - h / 2);
            maxY = Math.Max(maxY, y + h / 2);
        }

        foreach (var node in graph.NodeLabels())
        {
            GetExtremes(node.X!.Value, node.Y!.Value, node.Width, node.Height);
        }

        foreach (var e in graph.Edges())
        {
            var edge = graph.FindEdgeLabel(e);
            if (edge.X.HasValue)
            {
                GetExtremes(edge.X!.Value, edge.Y!.Value, edge.Width, edge.Height);
            }
        }

        foreach (var node in graph.NodeLabels())
        {
            node.X -= minX;
            node.Y -= minY;
        }

        foreach (var e in graph.Edges())
        {
            var edge = graph.FindEdgeLabel(e);
            var points = edge.Points!;
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                points[i] = p with { X = p.X - minX, Y = p.Y - minY };
            }

            if (edge.X.HasValue)
            {
                edge.X -= minX;
            }

            if (edge.Y.HasValue)
            {
                edge.Y -= minY;
            }
        }

        graphLabel.Width = maxX - minX;
        graphLabel.Height = maxY - minY;
    }

    static void AssignNodeIntersects(Graph graph)
    {
        foreach (var e in graph.Edges())
        {
            var edge = graph.FindEdgeLabel(e);
            var nodeV = graph.NodeLabel(e.V);
            var nodeW = graph.NodeLabel(e.W);
            Position p1;
            Position p2;
            if (edge.Points == null)
            {
                edge.Points = [];
                p1 = new(nodeW.X!.Value, nodeW.Y!.Value);
                p2 = new(nodeV.X!.Value, nodeV.Y!.Value);
            }
            else
            {
                p1 = edge.Points[0];
                p2 = edge.Points[^1];
            }

            edge.Points.Insert(0, Util.IntersectRect(nodeV, p1));
            edge.Points.Add(Util.IntersectRect(nodeW, p2));
        }
    }

    static void FixupEdgeLabelCoords(Graph graph)
    {
        foreach (var e in graph.Edges())
        {
            var edge = graph.FindEdgeLabel(e);
            if (!edge.X.HasValue)
            {
                continue;
            }

            if (edge.Labelpos is LabelPosition.Left or LabelPosition.Right)
            {
                edge.Width -= edge.Labeloffset;
            }

            switch (edge.Labelpos)
            {
                case LabelPosition.Left:
                    edge.X -= edge.Width / 2 + edge.Labeloffset;
                    break;
                case LabelPosition.Right:
                    edge.X += edge.Width / 2 + edge.Labeloffset;
                    break;
            }
        }
    }

    static void ReversePointsForReversedEdges(Graph graph)
    {
        foreach (var e in graph.Edges())
        {
            var edge = graph.FindEdgeLabel(e);
            if (edge.Reversed == true)
            {
                edge.Points!.Reverse();
            }
        }
    }

    static void RemoveBorderNodes(Graph graph)
    {
        foreach (var (v, node) in graph.NodeEntries())
        {
            if (graph.ChildCount(v) == 0)
            {
                continue;
            }

            var t = graph.NodeLabel(node.BorderTop!);
            var b = graph.NodeLabel(node.BorderBottom!);
            var l = graph.NodeLabel(node.BorderLeft![node.BorderLeft!.Count - 1]);
            var r = graph.NodeLabel(node.BorderRight![node.BorderRight!.Count - 1]);

            node.Width = Math.Abs(r.X!.Value - l.X!.Value);
            node.Height = Math.Abs(b.Y!.Value - t.Y!.Value);
            node.X = l.X!.Value + node.Width / 2;
            node.Y = t.Y!.Value + node.Height / 2;
        }

        foreach (var (v, node) in graph.NodeEntries())
        {
            if (node.Dummy == DummyKind.Border)
            {
                graph.RemoveNode(v);
            }
        }
    }

    static void RemoveSelfEdges(Graph graph)
    {
        foreach (var e in graph.Edges())
        {
            if (e.V == e.W)
            {
                var node = graph.NodeLabel(e.V);
                node.SelfEdges ??= [];
                node.SelfEdges.Add(new() { E = e, Label = graph.FindEdgeLabel(e) });
                graph.RemoveEdge(e);
            }
        }
    }

    static void InsertSelfEdges(Graph graph)
    {
        var layers = Util.BuildLayerMatrix(graph);
        foreach (var layer in layers)
        {
            var orderShift = 0;
            for (var i = 0; i < layer.Count; i++)
            {
                var v = layer[i];
                if (v is null)
                {
                    continue;
                }

                var node = graph.NodeLabel(v);
                node.Order = i + orderShift;
                foreach (var selfEdge in node.SelfEdges ?? [])
                {
                    Util.AddDummyNode(
                        graph,
                        DummyKind.SelfEdge,
                        new()
                        {
                            Width = selfEdge.Label.Width,
                            Height = selfEdge.Label.Height,
                            Rank = node.Rank,
                            Order = i + ++orderShift,
                            EdgeKey = selfEdge.E,
                            EdgeLabel = selfEdge.Label
                        },
                        DummyNames.SelfEdge);
                }

                node.SelfEdges = null;
            }
        }
    }

    static void PositionSelfEdges(Graph graph)
    {
        foreach (var (v, node) in graph.NodeEntries())
        {
            if (node.Dummy != DummyKind.SelfEdge)
            {
                continue;
            }

            var selfNode = graph.NodeLabel(node.EdgeKey!.V);
            var x = selfNode.X!.Value + selfNode.Width / 2;
            var y = selfNode.Y!.Value;
            var dx = node.X!.Value - x;
            var dy = selfNode.Height / 2;
            var label = node.EdgeLabel!;
            graph.SetEdge(node.EdgeKey!, label);
            graph.RemoveNode(v);
            label.Points =
            [
                new(x + 2 * dx / 3, y - dy),
                new(x + 5 * dx / 6, y - dy),
                new(x + dx, y),
                new(x + 5 * dx / 6, y + dy),
                new(x + 2 * dx / 3, y + dy)
            ];
            label.X = node.X;
            label.Y = node.Y;
        }
    }
}
