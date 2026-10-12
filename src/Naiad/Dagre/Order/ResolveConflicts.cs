static class ResolveConflicts
{
    sealed class MappedEntry
    {
        public int Indegree;
        public List<MappedEntry> In = null!;
        public List<MappedEntry> Out = null!;
        public List<string> Vs = null!;
        public int I;
        public double? Barycenter;
        public double? Weight;
        public bool Merged;
    }

    public static List<ResolvedEntry> Run(List<BarycenterEntry> entries, Graph constraintGraph) =>
        Run(entries, constraintGraph, knownDistinct: false);

    /// <summary>
    /// <paramref name="knownDistinct"/> is for a caller whose entries are the children of one node, which
    /// cannot repeat, and spares checking that they do not.
    /// </summary>
    internal static List<ResolvedEntry> Run(List<BarycenterEntry> entries, Graph constraintGraph, bool knownDistinct)
    {
        if (constraintGraph.EdgeCount == 0 &&
            (knownDistinct || AllDistinct(entries)))
        {
            return Unconstrained(entries);
        }

        var mappedEntries = new Dictionary<string, MappedEntry>(StringComparer.Ordinal);
        var insertionOrder = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var tmp = new MappedEntry
            {
                Indegree = 0,
                In = [],
                Out = [],
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

        foreach (var e in constraintGraph.Edges())
        {
            var hasV = mappedEntries.TryGetValue(e.V, out var entryV);
            var hasW = mappedEntries.TryGetValue(e.W, out var entryW);
            if (hasV && hasW)
            {
                entryW!.Indegree++;
                entryV!.Out.Add(entryW);
            }
        }

        var sourceSet = insertionOrder
            .Select(v => mappedEntries[v])
            .Where(entry => entry.Indegree == 0)
            .ToList();

        return DoResolveConflicts(sourceSet);
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

    static List<ResolvedEntry> DoResolveConflicts(List<MappedEntry> sourceSet)
    {
        var entries = new List<MappedEntry>();

        static Action<MappedEntry> HandleIn(MappedEntry vEntry) =>
            uEntry =>
            {
                if (uEntry.Merged)
                {
                    return;
                }

                if (uEntry.Barycenter == null ||
                    vEntry.Barycenter == null ||
                    uEntry.Barycenter >= vEntry.Barycenter)
                {
                    MergeEntries(vEntry, uEntry);
                }
            };

        Action<MappedEntry> HandleOut(MappedEntry vEntry) =>
            wEntry =>
            {
                wEntry.In.Add(vEntry);
                if (--wEntry.Indegree == 0)
                {
                    sourceSet.Add(wEntry);
                }
            };

        while (sourceSet.Count > 0)
        {
            var entry = sourceSet[^1];
            sourceSet.RemoveAt(sourceSet.Count - 1);
            entries.Add(entry);
            entry.In.Reverse();
            var handleIn = HandleIn(entry);
            foreach (var uEntry in entry.In)
            {
                handleIn(uEntry);
            }

            var handleOut = HandleOut(entry);
            foreach (var wEntry in entry.Out)
            {
                handleOut(wEntry);
            }
        }

        return entries
            .Where(_ => !_.Merged)
            .Select(entry => new ResolvedEntry
            {
                Vs = entry.Vs,
                I = entry.I,
                Barycenter = entry.Barycenter,
                Weight = entry.Weight
            })
            .ToList();
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
