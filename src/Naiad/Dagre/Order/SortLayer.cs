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
/// The nodes of one rank and how they nest. It is the same whichever way a sweep is going, so the layer
/// that looks up from a rank and the one that looks down share it.
/// </summary>
sealed class LayerShape
{
    static readonly List<string> none = [];

    // The nodes of the rank, in the order the graph gave them, and of those the ones under no subgraph.
    public readonly List<string> Nodes;
    public readonly List<string> Top;

    // Null in a rank no subgraph reaches.
    Dictionary<string, List<string>>? children;
    Dictionary<string, (string? Left, string? Right)>? borders;

    // Made the first time they are asked for and kept: a subgraph's children without its borders, and
    // the entries handed out for a list of nodes, which every sweep fills in again.
    Dictionary<string, List<string>>? movable;
    readonly Dictionary<List<string>, List<BarycenterEntry>> entries = new(1, ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Takes from <paramref name="nodesWithRank"/> the nodes that lie in <paramref name="rank"/>, as
    /// <see cref="BuildLayerGraph.Run"/> does.
    /// </summary>
    public LayerShape(Graph graph, int rank, List<string> nodesWithRank)
    {
        Nodes = new(nodesWithRank.Count);
        Top = new(nodesWithRank.Count);
        foreach (var v in nodesWithRank)
        {
            var node = graph.NodeLabel(v);
            if (node.Rank != rank &&
                (node is not { MinRank: not null, MaxRank: not null } ||
                 !(node.MinRank <= rank) || !(rank <= node.MaxRank)))
            {
                continue;
            }

            Nodes.Add(v);
            if (graph.Parent(v) is { } parent)
            {
                children ??= new(StringComparer.Ordinal);
                if (!children.TryGetValue(parent, out var siblings))
                {
                    children[parent] = siblings = [];
                }

                siblings.Add(v);
            }
            else
            {
                Top.Add(v);
            }

            if (node.MinRank != null)
            {
                borders ??= new(StringComparer.Ordinal);
                borders[v] = (node.BorderLeft![rank], node.BorderRight![rank]);
            }
        }
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

    /// <summary>An entry for each of <paramref name="nodes"/>, the same ones every time.</summary>
    public List<BarycenterEntry> Entries(List<string> nodes)
    {
        if (entries.TryGetValue(nodes, out var result))
        {
            return result;
        }

        result = new(nodes.Count);
        foreach (var v in nodes)
        {
            result.Add(new() { V = v });
        }

        entries[nodes] = result;
        return result;
    }
}

/// <summary>
/// A layer held as lists, built straight from the graph being ordered. It answers as the layer graph for
/// the same rank would: the same children in the same order, the same edges merged the same way.
/// </summary>
sealed class LightSortLayer : SortLayer
{
    // The nodes that have edges, and for each its place in the arrays below: its edges are
    // neighbor[start[i] .. start[i + 1]), one per neighbour, in the order each neighbour was first met.
    readonly Dictionary<string, int> withEdges;
    readonly int[] start;
    readonly NodeLabel[] neighbor;
    readonly double[] weight;

    readonly Graph graph;
    readonly LayerShape shape;

    public override string Root { get; }

    /// <summary>
    /// Builds the layer for the rank of <paramref name="shape"/>, taking each node's edges from
    /// <paramref name="edgesOf"/>.
    /// </summary>
    public LightSortLayer(
        Graph graph,
        string root,
        LayerShape shape,
        Func<string, OrderedMap<EdgeKey>.ValueEnumerable> edgesOf)
    {
        Root = root;
        this.graph = graph;
        this.shape = shape;

        // Counted first, so that everything below is made once at its final size.
        var nodes = shape.Nodes;
        var edgeCount = 0;
        var nodeCount = 0;
        foreach (var v in nodes)
        {
            var before = edgeCount;
            foreach (var _ in edgesOf(v))
            {
                edgeCount++;
            }

            if (edgeCount != before)
            {
                nodeCount++;
            }
        }

        withEdges = new(nodeCount, StringComparer.Ordinal);
        start = new int[nodeCount + 1];
        // Room for an edge to every neighbour; parallel edges merge and leave some of it unused.
        neighbor = new NodeLabel[edgeCount];
        weight = new double[edgeCount];
        var names = new string[edgeCount];
        var used = 0;

        foreach (var v in nodes)
        {
            // This assumes we have only short edges! One edge per neighbour, its weight the sum of the
            // weights of every edge to that neighbour, added in the order the edges come.
            var first = used;
            foreach (var e in edgesOf(v))
            {
                var u = e.V == v ? e.W : e.V;
                var edgeWeight = graph.FindEdgeLabel(e).Weight!.Value;
                var index = -1;
                for (var i = first; i < used; i++)
                {
                    if (names[i] == u)
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                {
                    names[used] = u;
                    neighbor[used] = graph.NodeLabel(u);
                    weight[used] = edgeWeight + 0;
                    used++;
                }
                else
                {
                    weight[index] = edgeWeight + weight[index];
                }
            }

            if (used != first)
            {
                start[withEdges.Count + 1] = used;
                withEdges[v] = withEdges.Count;
            }
        }
    }

    public override List<string> Children(string v)
    {
        if (v == Root)
        {
            return shape.Top;
        }

        return shape.Children(v);
    }

    public override int ChildCount(string v) =>
        Children(v).Count;

    public override List<string> Movable(string v)
    {
        if (v == Root)
        {
            return shape.Top;
        }

        return shape.Movable(v);
    }

    public override void Borders(string v, out string? left, out string? right) =>
        shape.Borders(v, out left, out right);

    public override List<BarycenterEntry> Barycenters(List<string> movable)
    {
        var result = shape.Entries(movable);
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

    // Every node of the rank hangs under its subgraph, or under the root if it has none.
    public override string? Parent(string v)
    {
        if (v == Root)
        {
            return null;
        }

        return graph.Parent(v) ?? Root;
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
