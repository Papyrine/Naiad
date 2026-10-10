/// <summary>Depth-first traversal (pre/post-order) used by the network-simplex ranking pass.</summary>
static class Alg
{
    static T Reduce<T>(Graph graph, IReadOnlyList<string> vs, bool postorder, Func<T, string, T> fn, T acc)
    {
        List<string> Navigation(string v) =>
            (graph.IsDirected ? graph.Successors(v) : graph.Neighbors(v)) ?? [];

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in vs)
        {
            if (!graph.HasNode(v))
            {
                throw new InvalidOperationException("Graph does not have node: " + v);
            }

            acc = DoReduce(v, postorder, visited, Navigation, fn, acc);
        }

        return acc;
    }

    // Depth-first on an explicit stack rather than by recursion, so a long path cannot overflow the
    // thread's stack.
    static T DoReduce<T>(string start, bool postorder, HashSet<string> visited, Func<string, List<string>> navigation, Func<T, string, T> fn, T acc)
    {
        var pending = new Stack<(string V, List<string> Next, int Index)>();

        void Enter(string v)
        {
            if (!visited.Add(v))
            {
                return;
            }

            if (!postorder)
            {
                acc = fn(acc, v);
            }

            pending.Push((v, navigation(v), 0));
        }

        Enter(start);
        while (pending.Count > 0)
        {
            var (v, next, index) = pending.Pop();
            if (index == next.Count)
            {
                if (postorder)
                {
                    acc = fn(acc, v);
                }

                continue;
            }

            pending.Push((v, next, index + 1));
            Enter(next[index]);
        }

        return acc;
    }

    static List<string> Dfs(Graph graph, IReadOnlyList<string> vs, bool postorder)
    {
        var acc = new List<string>();
        Reduce(graph, vs, postorder, (a, v) =>
        {
            a.Add(v);
            return a;
        }, acc);
        return acc;
    }

    public static List<string> Preorder(Graph graph, IReadOnlyList<string> vs) => Dfs(graph, vs, false);

    public static List<string> Postorder(Graph graph, IReadOnlyList<string> vs) => Dfs(graph, vs, true);
}
