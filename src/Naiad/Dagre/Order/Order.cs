static class Order
{
    public static void Run(Graph graph) =>
        Run(graph, flatWhenPossible: true);

    /// <summary>
    /// <paramref name="flatWhenPossible"/> lets a graph with no subgraphs be swept straight off the graph
    /// itself. Turning it off sends every graph through the layer graphs, which is what the tests use to
    /// hold the two to the same answer.
    /// </summary>
    internal static void Run(Graph graph, bool flatWhenPossible)
    {
        var maxRank = (int) Util.MaxRank(graph);

        List<string>[]? flatLayers = null;
        if (flatWhenPossible)
        {
            flatLayers = FlatLayers(graph, maxRank);
        }

        List<Graph>? downLayerGraphs = null;
        List<Graph>? upLayerGraphs = null;
        FlatSweep? flatSweep = null;
        if (flatLayers is null)
        {
            downLayerGraphs = BuildLayerGraphs(graph, Util.Range(1, maxRank + 1), graph.InEdgesOf);
            upLayerGraphs = BuildLayerGraphs(graph, Util.Range(maxRank - 1, -1, -1), graph.OutEdgesOf);
        }
        else
        {
            flatSweep = new(graph, flatLayers);
        }

        var layering = InitOrder.Run(graph);
        AssignOrder(graph, layering);

        var bestCC = double.PositiveInfinity;
        List<List<string>>? best = null;

        for (int i = 0, lastBest = 0; lastBest < 4; ++i, ++lastBest)
        {
            if (flatSweep is null)
            {
                SweepLayerGraphs((i % 2 != 0 ? downLayerGraphs : upLayerGraphs)!, i % 4 >= 2);
            }
            else
            {
                flatSweep.Run(down: i % 2 != 0, biasRight: i % 4 >= 2);
            }

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

    /// <summary>
    /// The nodes of each rank, in the order the graph holds them - or null when the graph has subgraphs,
    /// which only the layer graphs can order.
    /// </summary>
    static List<string>[]? FlatLayers(Graph graph, int maxRank)
    {
        if (maxRank < 0 ||
            graph.ChildCount() != graph.NodeCount)
        {
            return null;
        }

        var layers = new List<string>[maxRank + 1];
        for (var i = 0; i < layers.Length; i++)
        {
            layers[i] = [];
        }

        foreach (var (v, node) in graph.NodeEntries())
        {
            // A node that spans ranks is a subgraph's border, whatever the graph says about children.
            if (node is { MinRank: not null, MaxRank: not null })
            {
                return null;
            }

            if (node.Rank is { } rank)
            {
                if (rank < 0 || rank > maxRank)
                {
                    return null;
                }

                layers[rank].Add(v);
            }
        }

        return layers;
    }

    /// <summary>
    /// The sweep for a graph with no subgraphs. Building a layer graph for each rank, twice over, is most
    /// of what ordering costs, and with no subgraphs it buys nothing: every node of a rank hangs off the
    /// one root, there are no borders and no constraints, and sorting the rank comes down to a barycenter
    /// for each of its nodes. This takes those barycenters from the graph's own edges - merged by
    /// neighbour and summed in the order the layer graph would hold them, so the arithmetic is the same
    /// to the last bit - and hands them to the same conflict resolution and sort.
    /// </summary>
    sealed class FlatSweep(Graph graph, List<string>[] layers)
    {
        readonly Graph noConstraints = new();
        readonly List<(string U, double Weight)> merged = [];
        readonly Dictionary<string, int> mergedIndex = new(StringComparer.Ordinal);

        public void Run(bool down, bool biasRight)
        {
            if (down)
            {
                for (var rank = 1; rank < layers.Length; rank++)
                {
                    SortLayer(layers[rank], incoming: true, biasRight);
                }

                return;
            }

            for (var rank = layers.Length - 2; rank >= 0; rank--)
            {
                SortLayer(layers[rank], incoming: false, biasRight);
            }
        }

        void SortLayer(List<string> movable, bool incoming, bool biasRight)
        {
            var barycenters = new List<BarycenterEntry>(movable.Count);
            foreach (var v in movable)
            {
                MergeEdges(v, incoming);
                if (merged.Count == 0)
                {
                    barycenters.Add(new() { V = v });
                    continue;
                }

                var sum = 0d;
                var weight = 0d;
                foreach (var (u, edgeWeight) in merged)
                {
                    sum += edgeWeight * graph.NodeLabel(u).Order!.Value;
                    weight += edgeWeight;
                }

                barycenters.Add(new() { V = v, Barycenter = sum / weight, Weight = weight });
            }

            var sorted = Sort.Run(ResolveConflicts.Run(barycenters, noConstraints), biasRight);
            for (var i = 0; i < sorted.Vs.Count; i++)
            {
                graph.NodeLabel(sorted.Vs[i]).Order = i;
            }
        }

        // The node's edges to the neighbouring rank as one weight per neighbour, in the order each
        // neighbour is first met - which is how the layer graph, having one edge per pair, holds them.
        void MergeEdges(string v, bool incoming)
        {
            merged.Clear();
            mergedIndex.Clear();

            foreach (var e in incoming ? graph.InEdgesOf(v) : graph.OutEdgesOf(v))
            {
                var u = e.V == v ? e.W : e.V;
                var weight = graph.FindEdgeLabel(e).Weight!.Value;

                var index = IndexOf(u);
                if (index < 0)
                {
                    merged.Add((u, weight + 0));
                    if (mergedIndex.Count > 0)
                    {
                        mergedIndex[u] = merged.Count - 1;
                    }

                    continue;
                }

                merged[index] = (u, weight + merged[index].Weight);
            }
        }

        // A handful of neighbours are searched as a list. Past that they are indexed, so a node with
        // hundreds of edges does not search hundreds of entries for each one.
        int IndexOf(string u)
        {
            const int indexedFrom = 12;
            if (merged.Count < indexedFrom)
            {
                for (var i = 0; i < merged.Count; i++)
                {
                    if (merged[i].U == u)
                    {
                        return i;
                    }
                }

                return -1;
            }

            if (mergedIndex.Count == 0)
            {
                for (var i = 0; i < merged.Count; i++)
                {
                    mergedIndex[merged[i].U] = i;
                }
            }

            return mergedIndex.GetValueOrDefault(u, -1);
        }
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
