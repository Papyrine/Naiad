/// <summary>
/// Working storage for sorting the layers of a graph, kept from one sort to the next so that a sweep
/// does not make an entry and three lists afresh for every node of every rank.
/// </summary>
/// <remarks>
/// Sorting a subgraph sorts the subgraphs inside it first and only then resolves and sorts its own
/// entries, so one sort is finished with everything here before the next takes it up.
/// </remarks>
sealed class SortScratch
{
    readonly List<ResolvedEntry> pool = [];
    readonly List<List<string>> single = [];
    int used;

    public readonly List<ResolvedEntry> Resolved = [];
    public readonly List<ResolvedEntry> Sortable = [];
    public readonly List<ResolvedEntry> Unsortable = [];

    /// <summary>An entry for the one node <paramref name="entry"/> names, good until <see cref="Release"/>.</summary>
    public ResolvedEntry Rent(BarycenterEntry entry, int i)
    {
        if (used == pool.Count)
        {
            pool.Add(new());
            single.Add(new(1));
        }

        var vs = single[used];
        vs.Clear();
        vs.Add(entry.V);

        var result = pool[used++];
        result.Vs = vs;
        result.I = i;
        result.Barycenter = null;
        result.Weight = null;
        if (entry.Barycenter != null)
        {
            result.Barycenter = entry.Barycenter;
            result.Weight = entry.Weight;
        }

        return result;
    }

    public void Release() =>
        used = 0;
}
