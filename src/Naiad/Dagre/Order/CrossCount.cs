static class CrossCount
{
    public static int Run(Graph graph, List<List<string?>> layering) =>
        Run(graph, layering, new());

    internal static int Run(Graph graph, List<List<string?>> layering, Scratch scratch)
    {
        var cc = 0;
        for (var i = 1; i < layering.Count; ++i)
        {
            cc += TwoLayerCrossCount(graph, layering[i - 1], layering[i], scratch);
        }

        return cc;
    }

    /// <summary>Working storage a caller counting the same graph again and again may keep between counts.</summary>
    internal sealed class Scratch
    {
        public readonly Dictionary<string, int> SouthPos = new(StringComparer.Ordinal);
        public readonly List<SouthEntry> SouthEntries = [];
        public readonly List<SouthEntry> NodeEntries = [];
        public double[] Tree = [];
    }

    internal struct SouthEntry
    {
        public int Pos;
        public double Weight;
    }

    static int TwoLayerCrossCount(Graph graph, List<string?> northLayer, List<string?> southLayer, Scratch scratch)
    {
        // Sort all of the edges between the north and south layers by their position
        // in the north layer and then the south. Map these edges to the position of
        // their head in the south layer.
        var southPos = scratch.SouthPos;
        southPos.Clear();
        for (var i = 0; i < southLayer.Count; i++)
        {
            if (southLayer[i] is { } name)
            {
                southPos[name] = i;
            }
        }

        var southEntries = scratch.SouthEntries;
        var nodeEntries = scratch.NodeEntries;
        southEntries.Clear();
        foreach (var v in northLayer)
        {
            if (v is null)
            {
                continue;
            }

            nodeEntries.Clear();
            foreach (var e in graph.OutEdgesOf(v))
            {
                nodeEntries.Add(
                    new()
                    {
                        Pos = southPos[e.W],
                        Weight = graph.FindEdgeLabel(e).Weight!.Value
                    });
            }

            SortByPosition(nodeEntries);
            southEntries.AddRange(nodeEntries);
        }

        return CountCrossings(southEntries, southLayer.Count, ref scratch.Tree);
    }

    internal static void SortByPosition(List<SouthEntry> entries) =>
        entries.Sort((a, b) => a.Pos - b.Pos);

    /// <summary>
    /// The weighted crossings among edges given as the south-layer position of each edge's head, taken
    /// in order of the north-layer position of its tail. <paramref name="tree"/> is working storage a
    /// caller may keep between calls; it is grown as needed and cleared before use.
    /// </summary>
    internal static int CountCrossings(List<SouthEntry> southEntries, int southCount, ref double[] tree)
    {
        // Build the accumulator tree
        var firstIndex = 1;
        while (firstIndex < southCount)
        {
            firstIndex <<= 1;
        }

        var treeSize = 2 * firstIndex - 1;
        firstIndex -= 1;
        if (tree.Length < treeSize)
        {
            tree = new double[treeSize];
        }
        else
        {
            Array.Clear(tree, 0, treeSize);
        }

        // Calculate the weighted crossings
        var cc = 0d;
        foreach (var entry in southEntries)
        {
            var index = entry.Pos + firstIndex;
            tree[index] += entry.Weight;
            var weightSum = 0d;
            while (index > 0)
            {
                if (index % 2 != 0)
                {
                    weightSum += tree[index + 1];
                }

                index = (index - 1) >> 1;
                tree[index] += entry.Weight;
            }

            cc += entry.Weight * weightSum;
        }

        return (int) cc;
    }
}
