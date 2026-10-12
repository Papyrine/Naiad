/// <summary>
/// Working storage for sorting the layers of a graph, kept from one sort to the next so that a sweep
/// does not make two entries and several lists afresh for every node of every rank.
/// </summary>
/// <remarks>
/// Sorting a subgraph takes barycenter entries for its children, sorts the subgraphs among them, and
/// only then resolves and sorts its own entries. So the barycenter entries are lent as a stack, the
/// innermost sort giving its back first, and everything else is one sort's at a time.
/// </remarks>
sealed class SortScratch
{
    readonly List<ResolvedEntry> pool = [];
    readonly List<List<string>> single = [];
    int used;

    readonly List<BarycenterEntry> barycenters = [];
    readonly List<List<BarycenterEntry>> barycenterLists = [];
    int barycentersUsed;
    int barycenterListsUsed;

    public readonly List<ResolvedEntry> Resolved = [];
    public readonly List<ResolvedEntry> Sortable = [];
    public readonly List<ResolvedEntry> Unsortable = [];
    public readonly List<int> Constrained = [];

    /// <summary>Where the sorted nodes of a whole layer go, for the sweep to read before it sorts the next.</summary>
    public readonly List<string> Layer = [];

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

    /// <summary>An entry without a barycenter for each of <paramref name="nodes"/>, good until given to <see cref="Return"/>.</summary>
    public List<BarycenterEntry> RentBarycenters(List<string> nodes)
    {
        if (barycenterListsUsed == barycenterLists.Count)
        {
            barycenterLists.Add([]);
        }

        var result = barycenterLists[barycenterListsUsed++];
        result.Clear();
        foreach (var v in nodes)
        {
            if (barycentersUsed == barycenters.Count)
            {
                barycenters.Add(new());
            }

            var entry = barycenters[barycentersUsed++];
            entry.V = v;
            entry.Barycenter = null;
            entry.Weight = null;
            result.Add(entry);
        }

        return result;
    }

    /// <summary>Takes back the entries lent last. Entries that were not lent from here are left alone.</summary>
    public void Return(List<BarycenterEntry> entries)
    {
        if (barycenterListsUsed == 0 ||
            barycenterLists[barycenterListsUsed - 1] != entries)
        {
            return;
        }

        barycenterListsUsed--;
        barycentersUsed -= entries.Count;
    }
}
