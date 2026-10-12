static class Order
{
    // Set by a test to send a whole layout through the layer graphs dagre builds, so that it can be
    // compared with the same layout through the shorter routes. Per thread, and never set otherwise.
    [ThreadStatic]
    internal static bool UseLayerGraphs;

    public static void Run(Graph graph) =>
        Run(graph, flatWhenPossible: !UseLayerGraphs, lightLayers: !UseLayerGraphs);

    /// <summary>
    /// <paramref name="flatWhenPossible"/> hands a graph with no subgraphs to <see cref="FlatOrder"/>.
    /// Turning it off sends every graph through the layers, which is what the tests use to hold the two
    /// to the same answer. <paramref name="lightLayers"/> does the same for <see cref="LightSortLayer"/>
    /// against the layer graphs dagre builds.
    /// </summary>
    internal static void Run(Graph graph, bool flatWhenPossible, bool lightLayers = true)
    {
        // Nothing to order. The greatest rank of no nodes is negative infinity, which as a whole number
        // is not a rank to count layers down from.
        if (graph.NodeCount == 0)
        {
            return;
        }

        var maxRank = (int) Util.MaxRank(graph);

        if (flatWhenPossible &&
            FlatOrder.TryRun(graph, maxRank))
        {
            return;
        }

        var downLayerGraphs = BuildLayers(graph, Util.Range(1, maxRank + 1), graph.InEdgesOf, lightLayers);
        var upLayerGraphs = BuildLayers(graph, Util.Range(maxRank - 1, -1, -1), graph.OutEdgesOf, lightLayers);

        AssignOrder(graph, InitOrder.Run(graph));

        var bestCC = double.PositiveInfinity;
        List<List<string?>>? best = null;

        for (int i = 0, lastBest = 0; lastBest < 4; ++i, ++lastBest)
        {
            SweepLayerGraphs(i % 2 != 0 ? downLayerGraphs : upLayerGraphs, i % 4 >= 2);

            var layering = Util.BuildLayerMatrix(graph);
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

    static List<SortLayer> BuildLayers(
        Graph graph,
        List<int> ranks,
        Func<string, OrderedMap<EdgeKey>.ValueEnumerable> edgesOf,
        bool light)
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

        var layers = new List<SortLayer>(ranks.Count);
        foreach (var rank in ranks)
        {
            var nodesWithRank = nodesByRank.GetValueOrDefault(rank) ?? [];
            if (light)
            {
                // The root's name is drawn from the graph as a layer graph's would be, so the names the
                // graph hands out afterwards are the same either way.
                var root = BuildLayerGraph.CreateRootNode(graph);
                layers.Add(new LightSortLayer(graph, rank, root, edgesOf, nodesWithRank));
            }
            else
            {
                layers.Add(new GraphSortLayer(BuildLayerGraph.Run(graph, rank, edgesOf, nodesWithRank)));
            }
        }

        return layers;
    }

    static void SweepLayerGraphs(List<SortLayer> layers, bool biasRight)
    {
        var cg = new Graph();
        foreach (var layer in layers)
        {
            var sorted = SortSubgraph.Run(layer, layer.Root, cg, biasRight);
            for (var i = 0; i < sorted.Vs.Count; i++)
            {
                layer.SetOrder(sorted.Vs[i], i);
            }

            AddSubgraphConstraints.Run(layer, cg, sorted.Vs);
        }
    }

    static void AssignOrder(Graph graph, IEnumerable<IReadOnlyList<string?>> layering)
    {
        foreach (var layer in layering)
        {
            for (var i = 0; i < layer.Count; i++)
            {
                if (layer[i] is { } name)
                {
                    graph.NodeLabel(name).Order = i;
                }
            }
        }
    }
}
