/// <summary>
/// Makes a graph acyclic by reversing edges that participate in cycles, then restores them later.
/// </summary>
static class Acyclic
{
    /// <summary>Id prefix for the synthetic edges created when reversing a cycle edge.</summary>
    const string ReversedEdgePrefix = "rev";

    public static void Run(Graph graph)
    {
        foreach (var e in DfsFas(graph))
        {
            var label = graph.FindEdgeLabel(e);
            graph.RemoveEdge(e);
            label.ForwardName = e.Name;
            label.Reversed = true;
            graph.SetEdge(e.W, e.V, label, graph.UniqueId(ReversedEdgePrefix));
        }
    }

    static List<EdgeKey> DfsFas(Graph graph)
    {
        var fas = new List<EdgeKey>();
        var stack = new Dictionary<string, bool>(StringComparer.Ordinal);
        var visited = new Dictionary<string, bool>(StringComparer.Ordinal);

        // Depth-first on an explicit stack rather than by recursion, so a long path cannot overflow the
        // thread's stack. `stack` holds the nodes on the current path; an edge back onto it closes a cycle.
        var pending = new Stack<(string V, List<EdgeKey> OutEdges, int Next)>();

        void Enter(string v)
        {
            if (!visited.TryAdd(v, true))
            {
                return;
            }

            stack[v] = true;
            pending.Push((v, [..graph.OutEdgesOf(v)], 0));
        }

        void Dfs(string start)
        {
            Enter(start);
            while (pending.Count > 0)
            {
                var (v, outEdges, next) = pending.Pop();
                if (next == outEdges.Count)
                {
                    stack.Remove(v);
                    continue;
                }

                pending.Push((v, outEdges, next + 1));
                var e = outEdges[next];
                if (stack.ContainsKey(e.W))
                {
                    fas.Add(e);
                }
                else
                {
                    Enter(e.W);
                }
            }
        }

        foreach (var v in graph.Nodes())
        {
            Dfs(v);
        }

        return fas;
    }

    public static void Undo(Graph graph)
    {
        foreach (var e in graph.Edges())
        {
            var label = graph.FindEdgeLabel(e);
            if (label.Reversed == true)
            {
                graph.RemoveEdge(e);

                var forwardName = label.ForwardName;
                label.Reversed = null;
                label.ForwardName = null;
                graph.SetEdge(e.W, e.V, label, forwardName);
            }
        }
    }
}
