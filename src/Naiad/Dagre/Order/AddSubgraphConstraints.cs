static class AddSubgraphConstraints
{
    public static void Run(Graph graph, Graph constraintGraph, List<string> vs) =>
        Run(new GraphSortLayer(graph), constraintGraph, vs);

    public static void Run(SortLayer graph, Graph constraintGraph, List<string> vs)
    {
        Dictionary<string, string>? prev = null;
        string? rootPrev = null;

        foreach (var v in vs)
        {
            var child = graph.Parent(v);
            while (child != null)
            {
                var parent = graph.Parent(child);
                string? prevChild;
                if (parent == null)
                {
                    prevChild = rootPrev;
                    rootPrev = child;
                }
                else
                {
                    prev ??= new(StringComparer.Ordinal);
                    prevChild = prev.GetValueOrDefault(parent);
                    prev[parent] = child;
                }

                if (prevChild != null && prevChild != child)
                {
                    constraintGraph.SetEdge(prevChild, child);
                    break;
                }

                child = parent;
            }
        }
    }
}
