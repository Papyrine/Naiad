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

        var nodesByRank = NodesByRank(graph);
        // What a light layer holds of a rank's nodes, shared by the two layers of that rank.
        Dictionary<int, LayerShape>? shapes = null;
        if (lightLayers)
        {
            shapes = [];
        }

        var downLayerGraphs = BuildLayers(graph, Util.Range(1, maxRank + 1), graph.InEdgesOf, nodesByRank, shapes);
        var upLayerGraphs = BuildLayers(graph, Util.Range(maxRank - 1, -1, -1), graph.OutEdgesOf, nodesByRank, shapes);

        // Building the light layers numbered the nodes.
        AssignOrder(graph, InitOrder.Run(graph, numbered: lightLayers));

        var bestCC = double.PositiveInfinity;
        List<List<string?>>? best = null;
        // Two matrices, each with room for its rows: the best ordering so far, and the one being tried.
        var spare = EmptyLayerMatrix(nodesByRank, maxRank);
        var unused = EmptyLayerMatrix(nodesByRank, maxRank);
        // The layer graphs are sorted as dagre sorts them, entries made afresh and all.
        SortScratch? scratch = null;
        if (lightLayers)
        {
            scratch = new();
        }

        var crossings = new CrossCount.Scratch();

        for (int i = 0, lastBest = 0; lastBest < 4; ++i, ++lastBest)
        {
            SweepLayerGraphs(i % 2 != 0 ? downLayerGraphs : upLayerGraphs, i % 4 >= 2, scratch);

            var layering = Util.BuildLayerMatrix(graph, spare);
            var cc = CrossCount.Run(graph, layering, crossings);
            // Only a strict improvement replaces the best ordering, as in the dagre Mermaid ships. Letting a
            // sweep that merely ties take over hands the result to the last of the four tied sweeps, which
            // is a right-biased one: it breaks equal barycenters in reverse, so siblings came out mirrored
            // (`A --> B`, `A --> C`, `A --> D` drawn as D, C, B).
            if (cc < bestCC)
            {
                lastBest = 0;
                // The matrix just filled is kept as the best, and the one it replaces is filled next.
                spare = best ?? unused;
                best = layering;
                bestCC = cc;
            }
        }

        AssignOrder(graph, best!);
    }

    static Dictionary<int, List<string>> NodesByRank(Graph graph)
    {
        // Build an index mapping from rank to the nodes with that rank.
        // This helps to avoid a quadratic search for all nodes with the same rank as
        // the current node.
        var nodesByRank = new Dictionary<int, List<string>>();

        // Counted first, so that each rank's list is made once at its final size.
        var counts = new Dictionary<int, int>();
        ForEachRankOfEachNode(graph, (rank, _) => counts[rank] = counts.GetValueOrDefault(rank) + 1);
        foreach (var (rank, count) in counts)
        {
            nodesByRank[rank] = new(count);
        }

        ForEachRankOfEachNode(graph, (rank, node) => nodesByRank[rank].Add(node));
        return nodesByRank;
    }

    static void ForEachRankOfEachNode(Graph graph, Action<int, string> addNodeToRank)
    {
        // Visit the nodes in their original order in the graph, and add each
        // node to the ranks(s) that it belongs to.
        foreach (var (v, node) in graph.EnumerateNodeEntries())
        {
            if (node.Rank != null)
            {
                addNodeToRank(node.Rank.Value, v);
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
                    addNodeToRank(rank, v);
                }
            }
        }
    }

    // A layer for each of `ranks`: light ones if there are `shapes` to build them on, and otherwise the
    // layer graphs dagre builds.
    static List<SortLayer> BuildLayers(
        Graph graph,
        List<int> ranks,
        Func<string, OrderedMap<EdgeKey>.ValueEnumerable> edgesOf,
        Dictionary<int, List<string>> nodesByRank,
        Dictionary<int, LayerShape>? shapes)
    {
        var layers = new List<SortLayer>(ranks.Count);
        LayerEdges? edges = null;
        if (shapes != null)
        {
            edges = new(graph, edgesOf);
        }

        foreach (var rank in ranks)
        {
            var nodesWithRank = nodesByRank.GetValueOrDefault(rank) ?? [];
            if (shapes != null)
            {
                if (!shapes.TryGetValue(rank, out var shape))
                {
                    shapes[rank] = shape = new(graph, rank, nodesWithRank);
                }

                // The root's name is drawn from the graph as a layer graph's would be, so the names the
                // graph hands out afterwards are the same either way.
                var root = BuildLayerGraph.CreateRootNode(graph);
                layers.Add(new LightSortLayer(graph, root, shape, edges!));
            }
            else
            {
                layers.Add(new GraphSortLayer(BuildLayerGraph.Run(graph, rank, edgesOf, nodesWithRank)));
            }
        }

        return layers;
    }

    static List<List<string?>> EmptyLayerMatrix(Dictionary<int, List<string>> nodesByRank, int maxRank)
    {
        var matrix = new List<List<string?>>(maxRank + 1);
        for (var rank = 0; rank <= maxRank; rank++)
        {
            matrix.Add(new(nodesByRank.GetValueOrDefault(rank)?.Count ?? 0));
        }

        return matrix;
    }

    static void SweepLayerGraphs(List<SortLayer> layers, bool biasRight, SortScratch? scratch)
    {
        var cg = new Graph();
        foreach (var layer in layers)
        {
            var sorted = SortSubgraph.Run(layer, layer.Root, cg, biasRight, scratch);
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
