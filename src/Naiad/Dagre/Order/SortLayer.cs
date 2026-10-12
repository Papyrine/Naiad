/// <summary>
/// One rank of the graph as the sweep sorts it: the nodes of the rank hung under a root, subgraphs
/// holding their own, with each node's weighted edges from the rank the sweep has just left.
/// </summary>
/// <remarks>
/// dagre builds this as a graph of its own for every rank - <see cref="BuildLayerGraph"/> - and
/// <see cref="GraphSortLayer"/> reads one of those. <see cref="LightSortLayer"/> holds the same thing in
/// a few lists, which is what ordering uses: the sort asks a layer only the questions below, and a whole
/// graph per rank, twice over, was most of what ordering a diagram with subgraphs cost.
/// </remarks>
abstract class SortLayer
{
    /// <summary>The node every top-level node of the rank hangs under.</summary>
    public abstract string Root { get; }

    /// <summary>The nodes directly under <paramref name="v"/>, in the order they were added. Not to be changed.</summary>
    public abstract List<string> Children(string v);

    public abstract int ChildCount(string v);

    /// <summary>The nodes directly under <paramref name="v"/> but for its border nodes. Not to be changed.</summary>
    public abstract List<string> Movable(string v);

    /// <summary>The border nodes of a subgraph in this rank, or nulls for anything else.</summary>
    public abstract void Borders(string v, out string? left, out string? right);

    /// <summary>
    /// A barycenter for each of <paramref name="movable"/> that has an edge from the rank before. The
    /// entries are the caller's until it next asks for the same nodes.
    /// </summary>
    public abstract List<BarycenterEntry> Barycenters(List<string> movable);

    /// <summary>The label of the first node <paramref name="v"/> has an edge from, or null if it has none.</summary>
    public abstract NodeLabel? FirstPredecessor(string v);

    /// <summary>The node <paramref name="v"/> hangs under, or null for the root.</summary>
    public abstract string? Parent(string v);

    public abstract void SetOrder(string v, int order);
}

/// <summary>A layer read from the graph <see cref="BuildLayerGraph"/> builds for it.</summary>
sealed class GraphSortLayer(Graph graph) : SortLayer
{
    public override string Root => graph.Label.Root!;

    public override List<string> Children(string v) => graph.Children(v);

    public override int ChildCount(string v) => graph.ChildCount(v);

    public override List<string> Movable(string v)
    {
        var movable = graph.Children(v);
        Borders(v, out var left, out var right);
        if (left == null)
        {
            return movable;
        }

        return movable.Where(w => w != left && w != right).ToList();
    }

    public override void Borders(string v, out string? left, out string? right)
    {
        left = null;
        right = null;
        if (graph.TryGetNodeLabel(v, out var label))
        {
            left = label.BorderLeftId;
            right = label.BorderRightId;
        }
    }

    public override List<BarycenterEntry> Barycenters(List<string> movable) =>
        Barycenter.Run(graph, movable);

    public override NodeLabel? FirstPredecessor(string v)
    {
        var predecessors = graph.Predecessors(v);
        if (predecessors is null ||
            predecessors.Count == 0)
        {
            return null;
        }

        return graph.NodeLabel(predecessors[0]);
    }

    public override string? Parent(string v) => graph.Parent(v);

    public override void SetOrder(string v, int order) =>
        graph.NodeLabel(v).Order = order;
}

/// <summary>
/// A layer held as lists, built straight from the graph being ordered. It answers as the layer graph for
/// the same rank would: the same children in the same order, the same edges merged the same way.
/// </summary>
sealed class LightSortLayer : SortLayer
{
    static readonly List<string> none = [];

    readonly Dictionary<string, List<string>> children = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> parents = new(StringComparer.Ordinal);
    readonly Dictionary<string, (string? Left, string? Right)> borders = new(StringComparer.Ordinal);

    // Made the first time they are asked for and kept: a subgraph's children without its borders, and
    // the entries handed out for a list of nodes, which every sweep fills in again.
    readonly Dictionary<string, List<string>> movable = new(StringComparer.Ordinal);
    readonly Dictionary<List<string>, List<BarycenterEntry>> entries = new(ReferenceEqualityComparer.Instance);

    // The nodes that have edges, and for each its place in the lists below: its edges are
    // neighbor[start[i] .. start[i + 1]), one per neighbour, in the order each neighbour was first met.
    readonly Dictionary<string, int> withEdges = new(StringComparer.Ordinal);
    readonly List<int> start = [0];
    readonly List<NodeLabel> neighbor = [];
    readonly List<double> weight = [];

    readonly Graph graph;

    public override string Root { get; }

    /// <summary>
    /// Builds the layer for <paramref name="rank"/> from the nodes that lie in it, as
    /// <see cref="BuildLayerGraph.Run"/> does, taking each node's edges from <paramref name="edgesOf"/>.
    /// </summary>
    public LightSortLayer(
        Graph graph,
        int rank,
        string root,
        Func<string, OrderedMap<EdgeKey>.ValueEnumerable> edgesOf,
        List<string> nodesWithRank)
    {
        Root = root;
        this.graph = graph;
        var merged = new List<(string U, double Weight)>();

        foreach (var v in nodesWithRank)
        {
            var node = graph.NodeLabel(v);
            if (node.Rank != rank &&
                (node is not { MinRank: not null, MaxRank: not null } ||
                 !(node.MinRank <= rank) || !(rank <= node.MaxRank)))
            {
                continue;
            }

            var parent = graph.Parent(v) ?? root;
            parents[v] = parent;
            if (!children.TryGetValue(parent, out var siblings))
            {
                children[parent] = siblings = [];
            }

            siblings.Add(v);

            // This assumes we have only short edges! One edge per neighbour, its weight the sum of the
            // weights of every edge to that neighbour, added in the order the edges come.
            merged.Clear();
            foreach (var e in edgesOf(v))
            {
                var u = e.V == v ? e.W : e.V;
                var edgeWeight = graph.FindEdgeLabel(e).Weight!.Value;
                var index = -1;
                for (var i = 0; i < merged.Count; i++)
                {
                    if (merged[i].U == u)
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                {
                    merged.Add((u, edgeWeight + 0));
                }
                else
                {
                    merged[index] = (u, edgeWeight + merged[index].Weight);
                }
            }

            if (merged.Count > 0)
            {
                withEdges[v] = start.Count - 1;
                foreach (var (u, edgeWeight) in merged)
                {
                    neighbor.Add(graph.NodeLabel(u));
                    weight.Add(edgeWeight);
                }

                start.Add(neighbor.Count);
            }

            if (node.MinRank != null)
            {
                borders[v] = (node.BorderLeft![rank], node.BorderRight![rank]);
            }
        }
    }

    public override List<string> Children(string v) =>
        children.GetValueOrDefault(v) ?? none;

    public override int ChildCount(string v)
    {
        if (children.TryGetValue(v, out var list))
        {
            return list.Count;
        }

        return 0;
    }

    public override List<string> Movable(string v)
    {
        var all = Children(v);
        if (!borders.TryGetValue(v, out var found) ||
            found.Left == null)
        {
            return all;
        }

        if (movable.TryGetValue(v, out var kept))
        {
            return kept;
        }

        kept = new(all.Count);
        foreach (var w in all)
        {
            if (w != found.Left &&
                w != found.Right)
            {
                kept.Add(w);
            }
        }

        movable[v] = kept;
        return kept;
    }

    public override void Borders(string v, out string? left, out string? right)
    {
        left = null;
        right = null;
        if (borders.TryGetValue(v, out var found))
        {
            (left, right) = found;
        }
    }

    public override List<BarycenterEntry> Barycenters(List<string> movable)
    {
        if (!entries.TryGetValue(movable, out var result))
        {
            result = new(movable.Count);
            foreach (var v in movable)
            {
                result.Add(new() { V = v });
            }

            entries[movable] = result;
        }

        foreach (var entry in result)
        {
            entry.Barycenter = null;
            entry.Weight = null;
            if (!withEdges.TryGetValue(entry.V, out var index))
            {
                continue;
            }

            var sum = 0d;
            var total = 0d;
            for (var j = start[index]; j < start[index + 1]; j++)
            {
                sum += weight[j] * neighbor[j].Order!.Value;
                total += weight[j];
            }

            entry.Barycenter = sum / total;
            entry.Weight = total;
        }

        return result;
    }

    public override NodeLabel? FirstPredecessor(string v)
    {
        if (withEdges.TryGetValue(v, out var index))
        {
            return neighbor[start[index]];
        }

        return null;
    }

    public override string? Parent(string v) =>
        parents.GetValueOrDefault(v);

    // A subgraph has a label of its own in a layer graph, made for that one rank, so an order given to
    // it there goes nowhere. Every other node's label is the graph's.
    public override void SetOrder(string v, int order)
    {
        if (borders.ContainsKey(v))
        {
            return;
        }

        graph.NodeLabel(v).Order = order;
    }
}
