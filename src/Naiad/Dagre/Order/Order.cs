static class Order
{
    public static void Run(Graph graph)
    {
        var maxRank = (int) Util.MaxRank(graph);
        var downLayerGraphs = BuildLayerGraphs(graph, Util.Range(1, maxRank + 1), graph.InEdgesOf);
        var upLayerGraphs = BuildLayerGraphs(graph, Util.Range(maxRank - 1, -1, -1), graph.OutEdgesOf);

        var layering = InitOrder.Run(graph);
        AssignOrder(graph, layering);

        var bestCC = double.PositiveInfinity;
        List<List<string>>? best = null;

        for (int i = 0, lastBest = 0; lastBest < 4; ++i, ++lastBest)
        {
            SweepLayerGraphs(i % 2 != 0 ? downLayerGraphs : upLayerGraphs, i % 4 >= 2);

            layering = Util.BuildLayerMatrix(graph);
            var cc = CrossCount.Run(graph, layering);
            // Only a strict improvement replaces the best ordering, as in the dagre Mermaid ships. Letting a
            // sweep that merely ties take over hands the result to the last of the four tied sweeps, which
            // is a right-biased one: it breaks equal barycenters in reverse, so siblings came out mirrored
            // (`A --> B`, `A --> C`, `A --> D` drawn as D, C, B).
            if (cc < bestCC)
            {
                lastBest = 0;
                // shallow copy: BuildLayerMatrix builds fresh inner lists on every sweep.
                best = new(layering);
                bestCC = cc;
            }
        }

        AssignOrder(graph, best!);
    }

    static List<Graph> BuildLayerGraphs(Graph graph, List<int> ranks, Func<string, OrderedMap<EdgeKey>.ValueEnumerable> edgesOf)
    {
        // Build an index mapping from rank to the nodes with that rank.
        // This helps to avoid a quadratic search for all nodes with the same rank as
        // the current node.
        var nodesByRank = new Dictionary<int, List<string>>();

        void AddNodeToRank(int rank, string node)
        {
            if (!nodesByRank.TryGetValue(rank, out var list))
            {
                list = [];
                nodesByRank[rank] = list;
            }

            list.Add(node);
        }

        // Visit the nodes in their original order in the graph, and add each
        // node to the ranks(s) that it belongs to.
        foreach (var (v, node) in graph.NodeEntries())
        {
            if (node.Rank != null)
            {
                AddNodeToRank(node.Rank.Value, v);
            }

            // If there is a range of ranks, add it to each, but skip the `node.rank` which
            // has already had the node added.
            if (node is not
                {
                    MinRank: not null,
                    MaxRank: not null
                })
            {
                continue;
            }

            for (var rank = node.MinRank.Value; rank <= node.MaxRank.Value; rank++)
            {
                if (rank != node.Rank)
                {
                    // Don't add this node to its `node.rank` twice.
                    AddNodeToRank(rank, v);
                }
            }
        }

        return ranks.Select(rank =>
            BuildLayerGraph.Run(graph, rank, edgesOf, nodesByRank.GetValueOrDefault(rank) ?? [])).ToList();
    }

    static void SweepLayerGraphs(List<Graph> layerGraphs, bool biasRight)
    {
        var cg = new Graph();
        foreach (var lg in layerGraphs)
        {
            var root = lg.Label.Root!;
            var sorted = SortSubgraph.Run(lg, root, cg, biasRight);
            for (var i = 0; i < sorted.Vs.Count; i++)
            {
                lg.NodeLabel(sorted.Vs[i]).Order = i;
            }

            AddSubgraphConstraints.Run(lg, cg, sorted.Vs);
        }
    }

    static void AssignOrder(Graph graph, List<List<string>> layering)
    {
        foreach (var layer in layering)
        {
            for (var i = 0; i < layer.Count; i++)
            {
                graph.NodeLabel(layer[i]).Order = i;
            }
        }
    }
}
