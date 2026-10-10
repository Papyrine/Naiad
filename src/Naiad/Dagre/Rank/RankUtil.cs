static class RankUtil
{
    // One node of LongestPath's walk: its out-edges, how many have been dealt with, and the lowest rank
    // they allow so far.
    sealed class LongestPathFrame(Graph graph, string v)
    {
        public readonly NodeLabel Label = graph.NodeLabel(v);
        public readonly List<EdgeKey> OutEdges = [..graph.OutEdgesOf(v)];
        public int Next;
        public double Rank = double.PositiveInfinity;
    }

    /*
     * Initializes ranks for the input graph using the longest path algorithm. This
     * algorithm scales well and is fast in practice, it yields rather poor
     * solutions. Nodes are pushed to the lowest layer possible, leaving the bottom
     * ranks wide and leaving edges longer than necessary. However, due to its
     * speed, this algorithm is good for getting an initial ranking that can be fed
     * into other algorithms.
     *
     * This algorithm does not normalize layers because it will be used by other
     * algorithms in most cases. If using this algorithm directly, be sure to
     * run normalize at the end.
     *
     * Pre-conditions:
     *
     *    1. Input graph is a DAG.
     *    2. Input graph node labels can be assigned properties.
     *
     * Post-conditions:
     *
     *    1. Each node will be assign an (unnormalized) "rank" property.
     */
    public static void LongestPath(Graph graph)
    {
        var visited = new Dictionary<string, bool>(StringComparer.Ordinal);

        // Depth-first, post-order, on an explicit stack rather than by recursion: the depth is the length
        // of the longest path, which for a long chain of nodes overflows the thread's stack.
        var pending = new Stack<LongestPathFrame>();

        void Dfs(string start)
        {
            if (!visited.TryAdd(start, true))
            {
                return;
            }

            pending.Push(new(graph, start));
            while (pending.Count > 0)
            {
                var frame = pending.Peek();
                if (frame.Next == frame.OutEdges.Count)
                {
                    if (double.IsPositiveInfinity(frame.Rank))
                    {
                        frame.Rank = 0;
                    }

                    frame.Label.Rank = (int) frame.Rank;
                    pending.Pop();
                    continue;
                }

                // An unranked target is ranked first; the edge is looked at again once it has been.
                var e = frame.OutEdges[frame.Next];
                if (visited.TryAdd(e.W, true))
                {
                    pending.Push(new(graph, e.W));
                    continue;
                }

                frame.Next++;
                var candidate = (double) graph.NodeLabel(e.W).Rank!.Value - graph.FindEdgeLabel(e).Minlen!.Value;
                if (candidate < frame.Rank)
                {
                    frame.Rank = candidate;
                }
            }
        }

        foreach (var v in graph.Sources())
        {
            Dfs(v);
        }
    }

    /*
     * Returns the amount of slack for the given edge. The slack is defined as the
     * difference between the length of the edge and its minimum length.
     */
    public static int Slack(Graph graph, EdgeKey edge) =>
        graph.NodeLabel(edge.W).Rank!.Value - graph.NodeLabel(edge.V).Rank!.Value - graph.FindEdgeLabel(edge).Minlen!.Value;
}
