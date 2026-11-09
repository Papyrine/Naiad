static class ParentDummyChains
{
    sealed class PostorderNum
    {
        public int Low;
        public int Lim;
    }

    sealed class PathData
    {
        public List<string?> Path = [];
        public string? Lca;
    }

    public static void Run(Graph graph)
    {
        var postorderNums = Postorder(graph);

        foreach (var v0 in graph.Label.DummyChains!)
        {
            var v = v0;
            var node = graph.NodeLabel(v);
            var edgeObj = node.EdgeKey!;
            var pathData = FindPath(graph, postorderNums, edgeObj.V, edgeObj.W);
            var path = pathData.Path;
            var lca = pathData.Lca;
            var pathIdx = 0;
            var pathV = path[pathIdx];
            var ascending = true;

            while (v != edgeObj.W)
            {
                node = graph.NodeLabel(v);

                if (ascending)
                {
                    while ((pathV = path[pathIdx]) != lca &&
                        graph.NodeLabel(pathV!).MaxRank!.Value < node.Rank!.Value)
                    {
                        pathIdx++;
                    }

                    if (pathV == lca)
                    {
                        ascending = false;
                    }
                }

                if (!ascending)
                {
                    while (pathIdx < path.Count - 1 &&
                        graph.NodeLabel(path[pathIdx + 1]!).MinRank!.Value <= node.Rank!.Value)
                    {
                        pathIdx++;
                    }

                    pathV = path[pathIdx];
                }

                if (pathV != null)
                {
                    graph.SetParent(v, pathV);
                }

                v = graph.Successors(v)![0];
            }
        }
    }

    // Find a path from v to w through the lowest common ancestor (LCA). Return the
    // full path and the LCA.
    static PathData FindPath(
        Graph graph,
        Dictionary<string, PostorderNum> postorderNums,
        string v,
        string w)
    {
        var vPath = new List<string?>();
        var wPath = new List<string?>();
        var low = Math.Min(postorderNums[v].Low, postorderNums[w].Low);
        var lim = Math.Max(postorderNums[v].Lim, postorderNums[w].Lim);

        // Traverse up from v to find the LCA
        var parent = v;
        do
        {
            parent = graph.Parent(parent);
            vPath.Add(parent);
        } while (parent != null &&
            (postorderNums[parent].Low > low || lim > postorderNums[parent].Lim));
        var lca = parent;

        // Traverse from w to LCA
        var wParent = w;
        while ((wParent = graph.Parent(wParent)!) != lca)
        {
            wPath.Add(wParent);
        }

        wPath.Reverse();
        List<string?> path =
        [
            .. vPath,
            .. wPath
        ];
        return new()
        {
            Path = path,
            Lca = lca
        };
    }

    static Dictionary<string, PostorderNum> Postorder(Graph graph)
    {
        var result = new Dictionary<string, PostorderNum>(StringComparer.Ordinal);
        var lim = 0;

        void Dfs(string v)
        {
            var low = lim;
            foreach (var child in graph.Children(v))
            {
                Dfs(child);
            }

            result[v] = new() { Low = low, Lim = lim++ };
        }

        foreach (var v in graph.Children())
        {
            Dfs(v);
        }

        return result;
    }
}
