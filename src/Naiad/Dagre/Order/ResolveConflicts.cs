static class ResolveConflicts
{
    static readonly List<MappedEntry> none = [];

    sealed class MappedEntry
    {
        public int Indegree;
        // Made when there is something to put in them, which for most entries is never.
        public List<MappedEntry>? In;
        public List<MappedEntry>? Out;
        public List<string> Vs = null!;
        public int I;
        public double? Barycenter;
        public double? Weight;
        public bool Merged;
    }

    public static List<ResolvedEntry> Run(List<BarycenterEntry> entries, Graph constraintGraph)
    {
        if (constraintGraph.EdgeCount == 0 &&
            AllDistinct(entries))
        {
            return Unconstrained(entries);
        }

        return Constrained(entries, null, constraintGraph, []);
    }

    /// <summary>
    /// For entries that are the children of one node, which cannot repeat. A constraint lies between two
    /// nodes of the constraint graph, so an entry that is not one of those is neither merged nor waited
    /// on, and is answered directly from <paramref name="scratch"/>; only the rest go through the
    /// bookkeeping. The entries come back in no particular order - each has an
    /// <see cref="ResolvedEntry.I"/> of its own, which is all <see cref="Sort"/> places them by - and are
    /// good until the scratch is released.
    /// </summary>
    internal static List<ResolvedEntry> Run(List<BarycenterEntry> entries, Graph constraintGraph, SortScratch scratch)
    {
        var resolved = scratch.Resolved;
        resolved.Clear();

        List<int>? constrained = null;
        if (constraintGraph.EdgeCount != 0)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (constraintGraph.HasNode(entries[i].V))
                {
                    if (constrained == null)
                    {
                        constrained = scratch.Constrained;
                        constrained.Clear();
                    }

                    constrained.Add(i);
                }
            }

            if (constrained is { Count: < 2 })
            {
                constrained = null;
            }
        }

        var next = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (constrained != null &&
                next < constrained.Count &&
                constrained[next] == i)
            {
                next++;
                continue;
            }

            resolved.Add(scratch.Rent(entries[i], i));
        }

        if (constrained != null)
        {
            Constrained(entries, constrained, constraintGraph, resolved);
        }

        return resolved;
    }

    // The entries at the places in `only`, or all of them, resolved against the constraints and added
    // to `resolved`.
    static List<ResolvedEntry> Constrained(
        List<BarycenterEntry> entries,
        List<int>? only,
        Graph constraintGraph,
        List<ResolvedEntry> resolved)
    {
        var count = only?.Count ?? entries.Count;
        var mappedEntries = new Dictionary<string, MappedEntry>(count, StringComparer.Ordinal);
        var insertionOrder = new List<string>(count);
        for (var k = 0; k < count; k++)
        {
            var i = only?[k] ?? k;
            var entry = entries[i];
            var tmp = new MappedEntry
            {
                Indegree = 0,
                Vs = [entry.V],
                I = i
            };
            if (entry.Barycenter != null)
            {
                tmp.Barycenter = entry.Barycenter;
                tmp.Weight = entry.Weight;
            }

            if (!mappedEntries.ContainsKey(entry.V))
            {
                insertionOrder.Add(entry.V);
            }

            mappedEntries[entry.V] = tmp;
        }

        foreach (var e in constraintGraph.EnumerateEdges())
        {
            var hasV = mappedEntries.TryGetValue(e.V, out var entryV);
            var hasW = mappedEntries.TryGetValue(e.W, out var entryW);
            if (hasV && hasW)
            {
                entryW!.Indegree++;
                entryV!.Out ??= [];
                entryV.Out.Add(entryW);
            }
        }

        var sourceSet = new List<MappedEntry>(insertionOrder.Count);
        foreach (var v in insertionOrder)
        {
            var entry = mappedEntries[v];
            if (entry.Indegree == 0)
            {
                sourceSet.Add(entry);
            }
        }

        DoResolveConflicts(sourceSet, resolved);
        return resolved;
    }

    /// <summary>
    /// With no constraints nothing is merged and nothing waits on anything else: every entry is a source,
    /// and they come off the source set last first. That is the usual case - constraints only arise from
    /// subgraphs - so it is answered directly, without the bookkeeping the general case builds per entry.
    /// </summary>
    static List<ResolvedEntry> Unconstrained(List<BarycenterEntry> entries)
    {
        var resolved = new List<ResolvedEntry>(entries.Count);
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            var result = new ResolvedEntry
            {
                Vs = [entry.V],
                I = i
            };
            if (entry.Barycenter != null)
            {
                result.Barycenter = entry.Barycenter;
                result.Weight = entry.Weight;
            }

            resolved.Add(result);
        }

        return resolved;
    }

    // An entry named twice replaces the first in the general case, which the shortcut above would not
    // reproduce. Nodes of a rank are distinct, so this is a check that passes; a few entries are compared
    // directly and more than that through a set.
    static bool AllDistinct(List<BarycenterEntry> entries)
    {
        if (entries.Count <= 8)
        {
            for (var i = 1; i < entries.Count; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    if (entries[i].V == entries[j].V)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        var seen = new HashSet<string>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!seen.Add(entry.V))
            {
                return false;
            }
        }

        return true;
    }

    static void DoResolveConflicts(List<MappedEntry> sourceSet, List<ResolvedEntry> resolved)
    {
        var entries = new List<MappedEntry>(sourceSet.Count);

        while (sourceSet.Count > 0)
        {
            var entry = sourceSet[^1];
            sourceSet.RemoveAt(sourceSet.Count - 1);
            entries.Add(entry);
            entry.In?.Reverse();
            foreach (var uEntry in entry.In ?? none)
            {
                if (uEntry.Merged)
                {
                    continue;
                }

                if (uEntry.Barycenter == null ||
                    entry.Barycenter == null ||
                    uEntry.Barycenter >= entry.Barycenter)
                {
                    MergeEntries(entry, uEntry);
                }
            }

            foreach (var wEntry in entry.Out ?? none)
            {
                wEntry.In ??= [];
                wEntry.In.Add(entry);
                if (--wEntry.Indegree == 0)
                {
                    sourceSet.Add(wEntry);
                }
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Merged)
            {
                continue;
            }

            resolved.Add(
                new()
                {
                    Vs = entry.Vs,
                    I = entry.I,
                    Barycenter = entry.Barycenter,
                    Weight = entry.Weight
                });
        }
    }

    static void MergeEntries(MappedEntry target, MappedEntry source)
    {
        var sum = 0d;
        var weight = 0d;

        if (target.Weight is { } tw && tw != 0)
        {
            sum += target.Barycenter!.Value * tw;
            weight += tw;
        }

        if (source.Weight is { } sw && sw != 0)
        {
            sum += source.Barycenter!.Value * sw;
            weight += sw;
        }

        target.Vs = [.. source.Vs, .. target.Vs];
        target.Barycenter = sum / weight;
        target.Weight = weight;
        target.I = Math.Min(source.I, target.I);
        source.Merged = true;
    }
}
