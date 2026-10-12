/// <summary>
/// Ordering for a graph with no subgraphs, worked out over node numbers and arrays.
/// </summary>
/// <remarks>
/// <see cref="Order"/> is the port of dagre's module: it builds a graph for each rank, twice over, and
/// each sweep sorts every one of them through barycenter entries, conflict resolution and a sort, all of
/// it keyed by node name. Without subgraphs none of that machinery has anything to do. Every node of a
/// rank hangs off the one root, there are no borders and no constraints, and a sweep comes down to this
/// for each rank: take a barycenter for each node from its neighbours in the rank before, sort the nodes
/// that have one, and leave the ones that have none where they are.
///
/// So the nodes are numbered once, rank by rank, and each node's edges to the next rank are merged by
/// neighbour once - they do not change between sweeps. A sweep then reads and writes arrays. The
/// arithmetic is dagre's, in dagre's order: the same weights summed the same way, the same tie-breaks,
/// the same crossing count. The orders that come out are identical, and the tests hold the two to that.
/// </remarks>
sealed class FlatOrder
{
    readonly NodeLabel[] labels;

    // The nodes of rank r are numbered LayerStart[r] up to LayerStart[r + 1], in the order the graph
    // holds them. That order is the index dagre's sort breaks ties on.
    readonly int[] layerStart;

    // A node's merged edges to the rank above are In*[InStart[v] .. InStart[v + 1]), and Out* likewise
    // for the rank below.
    readonly int[] inStart;
    readonly int[] inNeighbor;
    readonly double[] inWeight;
    readonly int[] outStart;
    readonly int[] outNeighbor;
    readonly double[] outWeight;

    // Every edge to the rank below, unmerged and in the graph's order, which is what crossings count.
    readonly int[] edgeStart;
    readonly int[] edgeTarget;
    readonly double[] edgeWeight;

    // Where each node currently sits in its rank.
    readonly int[] order;
    readonly int[] best;

    // Working storage for one rank, sized for the widest.
    readonly Entry[] sortable;
    readonly int[] unsortable;
    readonly int[] arranged;
    readonly List<CrossCount.SouthEntry> southEntries = [];
    readonly List<CrossCount.SouthEntry> nodeEntries = [];
    double[] tree = [];

    readonly record struct Entry(double Barycenter, int Index);

    FlatOrder(
        NodeLabel[] labels,
        int[] layerStart,
        (int[] Start, int[] Neighbor, double[] Weight) incoming,
        (int[] Start, int[] Neighbor, double[] Weight) outgoing,
        (int[] Start, int[] Target, double[] Weight) edges)
    {
        this.labels = labels;
        this.layerStart = layerStart;
        (inStart, inNeighbor, inWeight) = incoming;
        (outStart, outNeighbor, outWeight) = outgoing;
        (edgeStart, edgeTarget, edgeWeight) = edges;

        order = new int[labels.Length];
        best = new int[labels.Length];

        var widest = 0;
        for (var rank = 0; rank < layerStart.Length - 1; rank++)
        {
            widest = Math.Max(widest, layerStart[rank + 1] - layerStart[rank]);
        }

        sortable = new Entry[widest];
        unsortable = new int[widest];
        arranged = new int[widest];
    }

    /// <summary>
    /// Orders the graph and returns true, or returns false having changed nothing when the graph is not
    /// one this handles: it has subgraphs, a node without a rank, an edge that does not run from one
    /// rank to the next, or an edge whose weight is not positive. <see cref="Order"/> orders those.
    /// </summary>
    public static bool TryRun(Graph graph, int maxRank)
    {
        if (TryCreate(graph, maxRank) is not { } flat)
        {
            return false;
        }

        flat.Run();
        return true;
    }

    static FlatOrder? TryCreate(Graph graph, int maxRank)
    {
        if (maxRank < 0 ||
            graph.ChildCount() != graph.NodeCount)
        {
            return null;
        }

        var count = graph.NodeCount;

        // Rank by rank, keeping the graph's order within each.
        var layerStart = new int[maxRank + 2];
        foreach (var node in graph.EnumerateNodeLabels())
        {
            // A node that spans ranks is a subgraph's border, whatever the graph says about children.
            if (node is not { Rank: { } rank, MinRank: null, MaxRank: null } ||
                rank < 0 ||
                rank > maxRank)
            {
                return null;
            }

            layerStart[rank + 1]++;
        }

        for (var rank = 0; rank <= maxRank; rank++)
        {
            layerStart[rank + 1] += layerStart[rank];
        }

        var next = new int[maxRank + 1];
        Array.Copy(layerStart, next, maxRank + 1);
        var names = new string[count];
        var labels = new NodeLabel[count];
        var rankOf = new int[count];
        foreach (var (name, node) in graph.EnumerateNodeEntries())
        {
            var rank = node.Rank!.Value;
            var number = next[rank]++;
            names[number] = name;
            labels[number] = node;
            rankOf[number] = rank;
            node.Number = number;
        }

        var merged = new List<(int Neighbor, double Weight)>();
        var mergedIndex = new Dictionary<int, int>();

        // A node's edges on one side as one weight per neighbour, in the order each neighbour is first
        // met - which is how a layer graph, having one edge per pair of nodes, would hold them. Returns
        // false for an edge that leaves the neighbouring rank or carries no weight.
        bool Merge(int v, bool incoming)
        {
            merged.Clear();
            mergedIndex.Clear();
            var neighborRank = rankOf[v] + (incoming ? -1 : 1);

            foreach (var e in incoming ? graph.InEdgesOf(names[v]) : graph.OutEdgesOf(names[v]))
            {
                var u = graph.NodeLabel(incoming ? e.V : e.W).Number;
                var weight = graph.FindEdgeLabel(e).Weight!.Value;
                if (rankOf[u] != neighborRank ||
                    !(weight > 0))
                {
                    return false;
                }

                // A handful of neighbours are searched as a list; past that they are indexed, so a node
                // with hundreds of edges does not search hundreds of entries for each one.
                const int indexedFrom = 12;
                var index = -1;
                if (merged.Count < indexedFrom)
                {
                    for (var i = 0; i < merged.Count; i++)
                    {
                        if (merged[i].Neighbor == u)
                        {
                            index = i;
                            break;
                        }
                    }
                }
                else
                {
                    if (mergedIndex.Count == 0)
                    {
                        for (var i = 0; i < merged.Count; i++)
                        {
                            mergedIndex[merged[i].Neighbor] = i;
                        }
                    }

                    index = mergedIndex.GetValueOrDefault(u, -1);
                }

                if (index >= 0)
                {
                    merged[index] = (u, weight + merged[index].Weight);
                    continue;
                }

                merged.Add((u, weight + 0));
                if (mergedIndex.Count > 0)
                {
                    mergedIndex[u] = merged.Count - 1;
                }
            }

            return true;
        }

        var inStart = new int[count + 1];
        var outStart = new int[count + 1];
        var edgeStart = new int[count + 1];

        // Room for every edge of the graph, each of which comes into one node and goes out of one;
        // parallel edges merge and leave some of the merged arrays unused.
        var edgeCount = graph.EdgeCount;
        var inNeighbor = new int[edgeCount];
        var inWeight = new double[edgeCount];
        var outNeighbor = new int[edgeCount];
        var outWeight = new double[edgeCount];
        var edgeTarget = new int[edgeCount];
        var edgeWeight = new double[edgeCount];
        var inCount = 0;
        var outCount = 0;
        var edgesCounted = 0;

        for (var v = 0; v < count; v++)
        {
            inStart[v] = inCount;
            if (!Merge(v, incoming: true))
            {
                return null;
            }

            foreach (var (neighbor, weight) in merged)
            {
                inNeighbor[inCount] = neighbor;
                inWeight[inCount++] = weight;
            }

            outStart[v] = outCount;
            if (!Merge(v, incoming: false))
            {
                return null;
            }

            foreach (var (neighbor, weight) in merged)
            {
                outNeighbor[outCount] = neighbor;
                outWeight[outCount++] = weight;
            }

            edgeStart[v] = edgesCounted;
            foreach (var e in graph.OutEdgesOf(names[v]))
            {
                edgeTarget[edgesCounted] = graph.NodeLabel(e.W).Number;
                edgeWeight[edgesCounted++] = graph.FindEdgeLabel(e).Weight!.Value;
            }
        }

        inStart[count] = inCount;
        outStart[count] = outCount;
        edgeStart[count] = edgesCounted;

        var flat = new FlatOrder(
            labels,
            layerStart,
            (inStart, inNeighbor, inWeight),
            (outStart, outNeighbor, outWeight),
            (edgeStart, edgeTarget, edgeWeight));

        // The starting order is dagre's, from the graph.
        var initial = InitOrder.Run(graph, numbered: true);
        foreach (var layer in initial)
        {
            for (var position = 0; position < layer.Count; position++)
            {
                flat.order[graph.NodeLabel(layer[position]).Number] = position;
            }
        }

        return flat;
    }

    // dagre's loop: sweep up and down, biased left then right, until four sweeps running have failed to
    // cut the crossings, and keep the best order seen.
    void Run()
    {
        var bestCC = double.PositiveInfinity;

        for (int i = 0, lastBest = 0; lastBest < 4; ++i, ++lastBest)
        {
            Sweep(down: i % 2 != 0, biasRight: i % 4 >= 2);

            // Only a strict improvement replaces the best ordering, as in Order.Run.
            var cc = CountCrossings();
            if (cc < bestCC)
            {
                lastBest = 0;
                Array.Copy(order, best, order.Length);
                bestCC = cc;
            }
        }

        for (var v = 0; v < labels.Length; v++)
        {
            labels[v].Order = best[v];
        }
    }

    void Sweep(bool down, bool biasRight)
    {
        var ranks = layerStart.Length - 1;
        if (down)
        {
            for (var rank = 1; rank < ranks; rank++)
            {
                SortLayer(rank, inStart, inNeighbor, inWeight, biasRight);
            }

            return;
        }

        for (var rank = ranks - 2; rank >= 0; rank--)
        {
            SortLayer(rank, outStart, outNeighbor, outWeight, biasRight);
        }
    }

    void SortLayer(int rank, int[] start, int[] neighbor, double[] weight, bool biasRight)
    {
        var first = layerStart[rank];
        var size = layerStart[rank + 1] - first;
        var sortableCount = 0;
        var unsortableCount = 0;

        // A node's index is its place among the rank's nodes as the graph holds them.
        for (var index = 0; index < size; index++)
        {
            var v = first + index;
            if (start[v] == start[v + 1])
            {
                unsortable[unsortableCount++] = index;
                continue;
            }

            var sum = 0d;
            var total = 0d;
            for (var j = start[v]; j < start[v + 1]; j++)
            {
                sum += weight[j] * order[neighbor[j]];
                total += weight[j];
            }

            sortable[sortableCount++] = new(sum / total, index);
        }

        sortable.AsSpan(0, sortableCount).Sort(new EntryComparer(biasRight));

        // dagre's sort: the nodes with a barycenter go down in sorted order, and a node without one is
        // slipped in as soon as the row has grown to its index.
        var placed = 0;
        var nextUnsortable = 0;

        void PlaceUnsortable()
        {
            while (nextUnsortable < unsortableCount &&
                   unsortable[nextUnsortable] <= placed)
            {
                arranged[placed++] = unsortable[nextUnsortable++];
            }
        }

        PlaceUnsortable();
        for (var i = 0; i < sortableCount; i++)
        {
            arranged[placed++] = sortable[i].Index;
            PlaceUnsortable();
        }

        for (var position = 0; position < placed; position++)
        {
            order[first + arranged[position]] = position;
        }
    }

    // By barycenter, and between equal barycenters by index - backwards when biased right.
    readonly struct EntryComparer(bool biasRight) : IComparer<Entry>
    {
        public int Compare(Entry x, Entry y)
        {
            if (x.Barycenter < y.Barycenter)
            {
                return -1;
            }

            if (x.Barycenter > y.Barycenter)
            {
                return 1;
            }

            if (!biasRight)
            {
                return x.Index - y.Index;
            }

            return y.Index - x.Index;
        }
    }

    // The crossings between each rank and the next, counted as CrossCount counts them.
    int CountCrossings()
    {
        var crossings = 0;
        for (var rank = 1; rank < layerStart.Length - 1; rank++)
        {
            var northFirst = layerStart[rank - 1];
            var northSize = layerStart[rank] - northFirst;
            var southSize = layerStart[rank + 1] - layerStart[rank];

            // The north rank left to right.
            for (var index = 0; index < northSize; index++)
            {
                arranged[order[northFirst + index]] = northFirst + index;
            }

            southEntries.Clear();
            for (var position = 0; position < northSize; position++)
            {
                var v = arranged[position];
                nodeEntries.Clear();
                for (var j = edgeStart[v]; j < edgeStart[v + 1]; j++)
                {
                    nodeEntries.Add(
                        new()
                        {
                            Pos = order[edgeTarget[j]],
                            Weight = edgeWeight[j]
                        });
                }

                CrossCount.SortByPosition(nodeEntries);
                southEntries.AddRange(nodeEntries);
            }

            crossings += CrossCount.CountCrossings(southEntries, southSize, ref tree);
        }

        return crossings;
    }
}
