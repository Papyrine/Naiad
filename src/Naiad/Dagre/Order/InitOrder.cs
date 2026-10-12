static class InitOrder
{
    public static List<List<string>> Run(Graph graph)
    {
        var visited = new HashSet<string>(graph.NodeCount, StringComparer.Ordinal);

        // How many of the nodes that are not subgraphs lie in each rank, up to the greatest rank among
        // them - none at all when there are no such nodes, which leaves no layers.
        var counts = new List<int>();
        var simpleCount = 0;
        foreach (var (v, node) in graph.EnumerateNodeEntries())
        {
            if (graph.ChildCount(v) != 0)
            {
                continue;
            }

            var rank = node.Rank!.Value;
            while (counts.Count <= rank)
            {
                counts.Add(0);
            }

            counts[rank]++;
            simpleCount++;
        }

        // Those nodes by rank, the ones of a rank in the order the graph has them, each put straight in
        // its place - and a layer for each rank, as long as that rank will make it.
        var layers = new List<List<string>>(counts.Count);
        var next = new int[counts.Count];
        var offset = 0;
        for (var rank = 0; rank < counts.Count; rank++)
        {
            layers.Add(new(counts[rank]));
            next[rank] = offset;
            offset += counts[rank];
        }

        var orderedVs = new string[simpleCount];
        foreach (var (v, node) in graph.EnumerateNodeEntries())
        {
            if (graph.ChildCount(v) == 0)
            {
                orderedVs[next[node.Rank!.Value]++] = v;
            }
        }

        // Depth-first, pre-order, on an explicit stack: the depth is the length of the longest path, and a
        // few thousand nodes in a chain is enough for a recursive walk to overflow the thread's stack. A
        // frame is the walk along one node's successors, read where the graph keeps them.
        var pending = new Stack<OrderedMap<int>.KeyEnumerable.Enumerator>();

        void Visit(string v)
        {
            if (!visited.Add(v))
            {
                return;
            }

            layers[graph.NodeLabel(v).Rank!.Value].Add(v);
            pending.Push(graph.SuccessorsOf(v).GetEnumerator());
        }

        void Dfs(string start)
        {
            Visit(start);
            while (pending.Count > 0)
            {
                var successors = pending.Pop();
                if (!successors.MoveNext())
                {
                    continue;
                }

                // The rest of this node's successors wait under whatever the next one leads to.
                var next = successors.Current;
                pending.Push(successors);
                Visit(next);
            }
        }

        foreach (var v in orderedVs)
        {
            Dfs(v);
        }

        return layers;
    }
}
