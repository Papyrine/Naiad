static class InitOrder
{
    public static List<List<string>> Run(Graph graph)
    {
        var visited = new Dictionary<string, bool>(StringComparer.Ordinal);
        var simpleNodes = graph.Nodes().Where(_ => graph.ChildCount(_) == 0).ToList();
        var simpleNodesRanks = simpleNodes.Select(_ => (double) graph.NodeLabel(_).Rank!.Value).ToList();
        var maxRank = (int) Util.ApplyMax(simpleNodesRanks);
        var layers = Util.Range(maxRank + 1).Select(_ => new List<string>()).ToList();

        // Depth-first, pre-order, on an explicit stack: the depth is the length of the longest path, and a
        // few thousand nodes in a chain is enough for a recursive walk to overflow the thread's stack.
        var pending = new Stack<(List<string> Successors, int Next)>();

        void Visit(string v)
        {
            if (!visited.TryAdd(v, true))
            {
                return;
            }

            layers[graph.NodeLabel(v).Rank!.Value].Add(v);
            if (graph.Successors(v) is { Count: > 0 } successors)
            {
                pending.Push((successors, 0));
            }
        }

        void Dfs(string start)
        {
            Visit(start);
            while (pending.Count > 0)
            {
                var (successors, next) = pending.Pop();
                if (next + 1 < successors.Count)
                {
                    pending.Push((successors, next + 1));
                }

                Visit(successors[next]);
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
