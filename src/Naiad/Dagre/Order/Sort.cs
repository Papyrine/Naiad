static class Sort
{
    static readonly Comparison<ResolvedEntry> leftBias = (entryV, entryW) => Compare(entryV, entryW, false);
    static readonly Comparison<ResolvedEntry> rightBias = (entryV, entryW) => Compare(entryV, entryW, true);
    static readonly Comparison<ResolvedEntry> descendingByI = (entryV, entryW) => entryW.I.CompareTo(entryV.I);

    public static SortResult Run(List<ResolvedEntry> entries, bool biasRight = false) =>
        Run(entries, biasRight, null, null, null, null);

    /// <summary>
    /// <paramref name="scratch"/> lends the lists the sort works in, for a caller whose entries each have
    /// an <see cref="ResolvedEntry.I"/> of their own. <paramref name="first"/> and <paramref name="last"/>,
    /// given together, are put either side of the sorted nodes. <paramref name="into"/> is a list to put
    /// them in, in place of a new one.
    /// </summary>
    internal static SortResult Run(
        List<ResolvedEntry> entries,
        bool biasRight,
        SortScratch? scratch,
        string? first,
        string? last,
        List<string>? into)
    {
        var sortable = scratch?.Sortable ?? [];
        var unsortable = scratch?.Unsortable ?? [];
        sortable.Clear();
        unsortable.Clear();
        var count = 0;
        foreach (var entry in entries)
        {
            count += entry.Vs.Count;
            if (entry.Barycenter != null)
            {
                sortable.Add(entry);
            }
            else
            {
                unsortable.Add(entry);
            }
        }

        // descending by i, stable for ties. Entries that each have an i of their own have no ties.
        if (scratch == null)
        {
            unsortable = unsortable.OrderByDescending(entry => entry.I).ToList();
        }
        else
        {
            unsortable.Sort(descendingByI);
        }

        if (first != null)
        {
            count += 2;
        }

        var vs = into ?? new List<string>(count);
        vs.Clear();
        if (first != null)
        {
            vs.Add(first);
        }

        var sum = 0d;
        var weight = 0d;
        var vsIndex = 0;

        if (biasRight)
        {
            sortable.Sort(rightBias);
        }
        else
        {
            sortable.Sort(leftBias);
        }

        vsIndex = ConsumeUnsortable(vs, unsortable, vsIndex);

        foreach (var entry in sortable)
        {
            vsIndex += entry.Vs.Count;
            vs.AddRange(entry.Vs);
            sum += entry.Barycenter!.Value * entry.Weight!.Value;
            weight += entry.Weight!.Value;
            vsIndex = ConsumeUnsortable(vs, unsortable, vsIndex);
        }

        if (last != null)
        {
            vs.Add(last);
        }

        var result = new SortResult { Vs = vs };
        if (weight != 0)
        {
            result.Barycenter = sum / weight;
            result.Weight = weight;
        }

        return result;
    }

    static int ConsumeUnsortable(List<string> vs, List<ResolvedEntry> unsortable, int index)
    {
        ResolvedEntry? last;
        while (unsortable.Count > 0 && (last = unsortable[^1]).I <= index)
        {
            unsortable.RemoveAt(unsortable.Count - 1);
            vs.AddRange(last.Vs);
            index++;
        }

        return index;
    }

    static int Compare(ResolvedEntry entryV, ResolvedEntry entryW, bool bias)
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
    }
}
