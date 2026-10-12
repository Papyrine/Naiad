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
    /// entries may be lent from <paramref name="scratch"/>, for the caller to give back.
    /// </summary>
    public abstract List<BarycenterEntry> Barycenters(List<string> movable, SortScratch? scratch);

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

    public override List<BarycenterEntry> Barycenters(List<string> movable, SortScratch? scratch) =>
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
/// The nodes of one rank and how they nest. It is the same whichever way a sweep is going, so the layer
/// that looks up from a rank and the one that looks down share it.
/// </summary>
sealed class LayerShape
{
    static readonly List<string> none = [];

    // The nodes of the rank, in the order the graph gave them, and of those the ones under no subgraph.
    // Either may be the list it was taken from, when it would have been a copy of it.
    public readonly List<string> Nodes;
    public readonly List<string> Top;

    // Null in a rank no subgraph reaches.
    Dictionary<string, List<string>>? children;
    Dictionary<string, (string? Left, string? Right)>? borders;

    // A subgraph's children without its borders, made the first time they are asked for and kept.
    Dictionary<string, List<string>>? movable;

    /// <summary>
    /// Takes from <paramref name="nodesWithRank"/> the nodes that lie in <paramref name="rank"/>, as
    /// <see cref="BuildLayerGraph.Run"/> does.
    /// </summary>
    public LayerShape(Graph graph, int rank, List<string> nodesWithRank)
    {
        List<string>? nodes = null;
        List<string>? top = null;
        for (var i = 0; i < nodesWithRank.Count; i++)
        {
            var v = nodesWithRank[i];
            var node = graph.NodeLabel(v);
            if (node.Rank != rank &&
                (node is not { MinRank: not null, MaxRank: not null } ||
                 !(node.MinRank <= rank) || !(rank <= node.MaxRank)))
            {
                // The first node left out: from here on the nodes are a list of their own.
                nodes ??= nodesWithRank.GetRange(0, i);
                continue;
            }

            if (graph.Parent(v) is { } parent)
            {
                // The first node under a subgraph: likewise for the ones under none.
                top ??= new(nodes ?? nodesWithRank.GetRange(0, i));
                children ??= new(StringComparer.Ordinal);
                if (!children.TryGetValue(parent, out var siblings))
                {
                    children[parent] = siblings = [];
                }

                siblings.Add(v);
            }
            else
            {
                top?.Add(v);
            }

            nodes?.Add(v);

            if (node.MinRank != null)
            {
                borders ??= new(StringComparer.Ordinal);
                borders[v] = (node.BorderLeft![rank], node.BorderRight![rank]);
            }
        }

        Nodes = nodes ?? nodesWithRank;
        Top = top ?? Nodes;
    }

    public List<string> Children(string v) =>
        children?.GetValueOrDefault(v) ?? none;

    public bool IsSubgraph(string v) =>
        borders != null &&
        borders.ContainsKey(v);

    public void Borders(string v, out string? left, out string? right)
    {
        left = null;
        right = null;
        if (borders != null &&
            borders.TryGetValue(v, out var found))
        {
            (left, right) = found;
        }
    }

    public List<string> Movable(string v)
    {
        var all = Children(v);
        if (borders == null ||
            !borders.TryGetValue(v, out var found) ||
            found.Left == null)
        {
            return all;
        }

        movable ??= new(StringComparer.Ordinal);
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
}

/// <summary>
/// Every node's weighted edges on one side - the edges in, for a sweep going down, or the edges out, for
/// one going up - as one weight per neighbour, in the order each neighbour is first met. That is how a
/// layer graph, having one edge per pair of nodes, holds them, and it is the same for every rank, so the
/// layers of a sweep share it.
/// </summary>
sealed class LayerEdges
{
    // The edges of the node numbered n are neighbor[start[n] .. start[n + 1]).
    readonly int[] start;
    readonly NodeLabel[] neighbor;
    readonly double[] weight;

    /// <summary>Numbers the nodes of <paramref name="graph"/> in the order it has them, and takes each one's edges from <paramref name="edgesOf"/>.</summary>
    public LayerEdges(Graph graph, Func<string, OrderedMap<EdgeKey>.ValueEnumerable> edgesOf)
    {
        start = new int[graph.NodeCount + 1];
        // Room for every edge; parallel edges merge and leave some of it unused.
        neighbor = new NodeLabel[graph.EdgeCount];
        weight = new double[graph.EdgeCount];

        // The neighbours of the node in hand. A handful are searched as a list; past that they are
        // indexed, so a node with hundreds of edges does not search hundreds of names for each one.
        const int indexedFrom = 12;
        var names = new List<string>();
        Dictionary<string, int>? indexed = null;
        var used = 0;
        var number = 0;

        foreach (var (v, node) in graph.EnumerateNodeEntries())
        {
            node.Number = number;
            names.Clear();
            indexed?.Clear();
            var first = used;

            // This assumes we have only short edges!
            foreach (var e in edgesOf(v))
            {
                var u = e.V == v ? e.W : e.V;
                var edgeWeight = graph.FindEdgeLabel(e).Weight!.Value;
                var index = -1;
                if (names.Count < indexedFrom)
                {
                    index = names.IndexOf(u);
                }
                else
                {
                    indexed ??= new(StringComparer.Ordinal);
                    if (indexed.Count == 0)
                    {
                        for (var i = 0; i < names.Count; i++)
                        {
                            indexed[names[i]] = i;
                        }
                    }

                    index = indexed.GetValueOrDefault(u, -1);
                }

                if (index >= 0)
                {
                    weight[first + index] = edgeWeight + weight[first + index];
                    continue;
                }

                if (indexed is { Count: > 0 })
                {
                    indexed[u] = names.Count;
                }

                names.Add(u);
                neighbor[used] = graph.NodeLabel(u);
                weight[used] = edgeWeight + 0;
                used++;
            }

            start[++number] = used;
        }
    }

    /// <summary>The barycenter of <paramref name="node"/>'s neighbours and the weight behind it, if it has any.</summary>
    public bool TryBarycenter(NodeLabel node, out double barycenter, out double total)
    {
        var from = start[node.Number];
        var to = start[node.Number + 1];
        var sum = 0d;
        total = 0;
        for (var j = from; j < to; j++)
        {
            sum += weight[j] * neighbor[j].Order!.Value;
            total += weight[j];
        }

        barycenter = sum / total;
        return from != to;
    }

    /// <summary>The first neighbour of <paramref name="node"/>, or null if it has none.</summary>
    public NodeLabel? First(NodeLabel node)
    {
        var from = start[node.Number];
        if (from == start[node.Number + 1])
        {
            return null;
        }

        return neighbor[from];
    }
}

/// <summary>
/// A layer held as lists, built straight from the graph being ordered. It answers as the layer graph for
/// the same rank would: the same children in the same order, the same edges merged the same way.
/// </summary>
sealed class LightSortLayer(Graph graph, string root, LayerShape shape, LayerEdges edges) : SortLayer
{
    public override string Root => root;

    public override List<string> Children(string v)
    {
        if (v == root)
        {
            return shape.Top;
        }

        return shape.Children(v);
    }

    public override int ChildCount(string v) =>
        Children(v).Count;

    public override List<string> Movable(string v)
    {
        if (v == root)
        {
            return shape.Top;
        }

        return shape.Movable(v);
    }

    public override void Borders(string v, out string? left, out string? right) =>
        shape.Borders(v, out left, out right);

    public override List<BarycenterEntry> Barycenters(List<string> movable, SortScratch? scratch)
    {
        var result = scratch?.RentBarycenters(movable);
        if (result == null)
        {
            result = new(movable.Count);
            foreach (var v in movable)
            {
                result.Add(new() { V = v });
            }
        }

        foreach (var entry in result)
        {
            if (edges.TryBarycenter(graph.NodeLabel(entry.V), out var barycenter, out var total))
            {
                entry.Barycenter = barycenter;
                entry.Weight = total;
            }
        }

        return result;
    }

    public override NodeLabel? FirstPredecessor(string v) =>
        edges.First(graph.NodeLabel(v));

    // Every node of the rank hangs under its subgraph, or under the root if it has none.
    public override string? Parent(string v)
    {
        if (v == root)
        {
            return null;
        }

        return graph.Parent(v) ?? root;
    }

    // A subgraph has a label of its own in a layer graph, made for that one rank, so an order given to
    // it there goes nowhere. Every other node's label is the graph's.
    public override void SetOrder(string v, int order)
    {
        if (shape.IsSubgraph(v))
        {
            return;
        }

        graph.NodeLabel(v).Order = order;
    }
}
