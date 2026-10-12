static class SortSubgraph
{
    public static SortResult Run(Graph graph, string v, Graph constraintGraph, bool biasRight = false) =>
        Run(new GraphSortLayer(graph), v, constraintGraph, biasRight);

    public static SortResult Run(SortLayer graph, string v, Graph constraintGraph, bool biasRight = false)
    {
        var movable = graph.Children(v);
        graph.Borders(v, out var bl, out var br);
        var subgraphs = new Dictionary<string, SortResult>(StringComparer.Ordinal);

        if (bl != null)
        {
            movable = movable.Where(w => w != bl && w != br).ToList();
        }

        var barycenters = graph.Barycenters(movable);
        foreach (var entry in barycenters)
        {
            if (graph.ChildCount(entry.V) == 0)
            {
                continue;
            }

            var subgraphResult = Run(graph, entry.V, constraintGraph, biasRight);
            subgraphs[entry.V] = subgraphResult;
            if (subgraphResult.Barycenter != null)
            {
                MergeBarycenters(entry, subgraphResult);
            }
        }

        var entries = ResolveConflicts.Run(barycenters, constraintGraph, knownDistinct: true);
        if (subgraphs.Count > 0)
        {
            ExpandSubgraphs(entries, subgraphs);
        }

        var result = Sort.Run(entries, biasRight);

        if (bl != null && br != null)
        {
            result.Vs = [bl, .. result.Vs, br];
            if (graph.FirstPredecessor(bl) is { } blPred)
            {
                var brPred = graph.FirstPredecessor(br)!;
                if (result.Barycenter == null)
                {
                    result.Barycenter = 0;
                    result.Weight = 0;
                }

                result.Barycenter = (result.Barycenter!.Value * result.Weight!.Value +
                                     blPred.Order!.Value + brPred.Order!.Value) /
                                    (result.Weight!.Value + 2);
                result.Weight = result.Weight!.Value + 2;
            }
        }

        return result;
    }

    static void ExpandSubgraphs(List<ResolvedEntry> entries, Dictionary<string, SortResult> subgraphs)
    {
        foreach (var entry in entries)
        {
            var expanded = new List<string>(entry.Vs.Count);
            foreach (var v in entry.Vs)
            {
                if (subgraphs.TryGetValue(v, out var subgraph))
                {
                    expanded.AddRange(subgraph.Vs);
                }
                else
                {
                    expanded.Add(v);
                }
            }

            entry.Vs = expanded;
        }
    }

    static void MergeBarycenters(BarycenterEntry target, SortResult other)
    {
        if (target.Barycenter == null)
        {
            target.Barycenter = other.Barycenter;
            target.Weight = other.Weight;
            return;
        }

        target.Barycenter = (target.Barycenter.Value * target.Weight!.Value +
                             other.Barycenter!.Value * other.Weight!.Value) /
                            (target.Weight!.Value + other.Weight!.Value);
        target.Weight = target.Weight!.Value + other.Weight!.Value;
    }
}
