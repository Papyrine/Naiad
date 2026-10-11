static class InitOrder
{
    public static List<List<string>> Run(Graph graph)
    {
        var nodes = graph.Nodes();
        var visited = new HashSet<string>(nodes.Count, StringComparer.Ordinal);

        // The nodes that are not subgraphs, and the greatest rank among them - negative infinity when
        // there are none, which leaves no layers.
        var simpleNodes = new List<string>(nodes.Count);
        var greatest = double.NegativeInfinity;
        foreach (var v in nodes)
        {
            if (graph.ChildCount(v) != 0)
            {
                continue;
            }

            simpleNodes.Add(v);
            greatest = Math.Max(greatest, graph.NodeLabel(v).Rank!.Value);
        }

        var maxRank = (int) greatest;
        var layers = new List<List<string>>();
        for (var rank = 0; rank < maxRank + 1; rank++)
        {
            layers.Add([]);
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

        // Stable sort: preserve original order for nodes of equal rank.
        var orderedVs = simpleNodes.OrderBy(_ => graph.NodeLabel(_).Rank!.Value).ToList();
        foreach (var v in orderedVs)
        {
            Dfs(v);
        }

        return layers;
    }
}
