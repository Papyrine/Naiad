static class Sort
{
    public static SortResult Run(List<ResolvedEntry> entries, bool biasRight = false)
    {
        var parts = Util.Partition(entries, entry => entry.Barycenter != null);
        var sortable = parts.Lhs;

        // descending by i, stable for ties.
        var unsortable = parts.Rhs.OrderByDescending(entry => entry.I).ToList();

        var vs = new List<List<string>>();
        var sum = 0d;
        var weight = 0d;
        var vsIndex = 0;

        sortable.Sort(CompareWithBias(biasRight));

        vsIndex = ConsumeUnsortable(vs, unsortable, vsIndex);

        foreach (var entry in sortable)
        {
            vsIndex += entry.Vs.Count;
            vs.Add(entry.Vs);
            sum += entry.Barycenter!.Value * entry.Weight!.Value;
            weight += entry.Weight!.Value;
            vsIndex = ConsumeUnsortable(vs, unsortable, vsIndex);
        }

        var result = new SortResult { Vs = vs.SelectMany(x => x).ToList() };
        if (weight != 0)
        {
            result.Barycenter = sum / weight;
            result.Weight = weight;
        }

        return result;
    }

    static int ConsumeUnsortable(List<List<string>> vs, List<ResolvedEntry> unsortable, int index)
    {
        ResolvedEntry? last;
        while (unsortable.Count > 0 && (last = unsortable[^1]).I <= index)
        {
            unsortable.RemoveAt(unsortable.Count - 1);
            vs.Add(last.Vs);
            index++;
        }

        return index;
    }

    static Comparison<ResolvedEntry> CompareWithBias(bool bias) =>
        (entryV, entryW) =>
        {
            if (entryV.Barycenter!.Value < entryW.Barycenter!.Value)
            {
                return -1;
            }

            if (entryV.Barycenter!.Value > entryW.Barycenter!.Value)
            {
                return 1;
            }

            if (!bias)
            {
                return entryV.I - entryW.I;
            }

            return entryW.I - entryV.I;
        };
}
