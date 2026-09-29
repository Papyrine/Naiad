static class BuildLayerGraph
{
    public static Graph Run(Graph graph, int rank, Func<string, OrderedMap<EdgeKey>.ValueEnumerable> edgesOf, List<string>? nodesWithRank = null)
    {
        nodesWithRank ??= graph.Nodes();

        var root = CreateRootNode(graph);
        var result = new Graph(compound: true);
        result.SetGraph(
            new()
            {
                Root = root
            });
        result.SetDefaultNodeLabel(_ =>
        {
            if (graph.TryGetNodeLabel(_, out var lbl))
            {
                return lbl;
            }

            return null!;
        });

        foreach (var v in nodesWithRank)
        {
            var node = graph.NodeLabel(v);
            var parent = graph.Parent(v);

            if (node.Rank != rank &&
                (node is not {MinRank: not null, MaxRank: not null} ||
                 !(node.MinRank <= rank) || !(rank <= node.MaxRank)))
            {
                continue;
            }

            result.SetNode(v);
            result.SetParent(v, parent ?? root);

            // This assumes we have only short edges!
            foreach (var e in edgesOf(v))
            {
                var u = e.V == v ? e.W : e.V;
                var weight = result.TryGetEdgeLabel(u, v, out var existing) ? existing.Weight!.Value : 0;
                result.SetEdge(
                    u,
                    v,
                    new()
                    {
                        Weight = graph.FindEdgeLabel(e).Weight!.Value + weight
                    });
            }

            if (node.MinRank != null)
            {
                result.SetNode(
                    v,
                    new()
                    {
                        BorderLeftId = node.BorderLeft![rank],
                        BorderRightId = node.BorderRight![rank]
                    });
            }
        }

        return result;
    }

    static string CreateRootNode(Graph graph)
    {
        string v;
        while (graph.HasNode(v = graph.UniqueId(DummyNames.Root)))
        {
        }

        return v;
    }
}
