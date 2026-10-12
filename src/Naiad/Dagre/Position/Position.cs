static class Positioning
{
    internal static void Run(Graph graph) =>
        Run(graph, inPlaceWhenPossible: true);

    /// <summary>
    /// <paramref name="inPlaceWhenPossible"/> lets the graph be positioned as it stands when the copy
    /// dagre takes of it would hold the same thing. Turning it off always takes the copy, which is what
    /// the tests use to hold the two to the same answer. <paramref name="indexedWhenPossible"/> does the
    /// same for <see cref="IndexedBK"/> against <see cref="BK"/>.
    /// </summary>
    internal static void Run(Graph graph, bool inPlaceWhenPossible, bool indexedWhenPossible = true)
    {
        if (!inPlaceWhenPossible ||
            !IsPlain(graph))
        {
            graph = Util.AsNonCompoundGraph(graph);
        }

        var layering = Util.BuildLayerMatrix(graph);
        PositionY(graph, layering);
        if (indexedWhenPossible &&
            IndexedBK.TryPositionX(graph, layering))
        {
            return;
        }

        foreach (var (v, x) in BK.PositionX(graph, layering))
        {
            graph.NodeLabel(v).X = x;
        }
    }

    /// <summary>
    /// Whether positioning the graph directly comes to the same thing as positioning its non-compound
    /// copy. The copy is the graph without its subgraph nodes, sharing every label, and it was the
    /// largest single cost in positioning. Positioning only ever looks at the nodes that have a rank and
    /// at the edges between them, so the copy differs only if a subgraph node is one of those nodes or
    /// sits on one of those edges. By this point in the layout neither is so: a subgraph is carried by
    /// its border nodes, and the edges that tied it into the ranking have been taken out again.
    /// </summary>
    static bool IsPlain(Graph graph)
    {
        if (!graph.IsCompound)
        {
            return true;
        }

        if (graph.ChildCount() == graph.NodeCount)
        {
            return true;
        }

        foreach (var (v, node) in graph.NodeEntries())
        {
            if (graph.ChildCount(v) == 0)
            {
                continue;
            }

            if (node?.Rank != null)
            {
                return false;
            }

            foreach (var _ in graph.InEdgesOf(v))
            {
                return false;
            }

            foreach (var _ in graph.OutEdgesOf(v))
            {
                return false;
            }
        }

        return true;
    }

    static void PositionY(Graph graph, List<List<string?>> layering)
    {
        var graphLabel = graph.Label;
        var rankSep = graphLabel.RankSeparation!.Value;
        double prevY = 0;
        foreach (var layer in layering)
        {
            double maxHeight = 0;
            foreach (var v in layer)
            {
                if (v is null)
                {
                    continue;
                }

                maxHeight = Math.Max(maxHeight, graph.NodeLabel(v).Height);
            }

            foreach (var v in layer)
            {
                if (v is null)
                {
                    continue;
                }

                var node = graph.NodeLabel(v);
                node.Y = prevY + maxHeight / 2;
            }

            prevY += maxHeight + rankSep;
        }
    }
}
