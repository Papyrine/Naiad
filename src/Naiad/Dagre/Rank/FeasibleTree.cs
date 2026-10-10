static class FeasibleTree
{
    /*
     * Constructs a spanning tree with tight edges and adjusted the input node's
     * ranks to achieve this. A tight edge is one that is has a length that matches
     * its "minlen" attribute.
     *
     * The basic structure for this function is derived from Gansner, et al., "A
     * Technique for Drawing Directed Graphs."
     *
     * Pre-conditions:
     *
     *    1. Graph must be a DAG.
     *    2. Graph must be connected.
     *    3. Graph must have at least one node.
     *    5. Graph nodes must have been previously assigned a "rank" property that
     *       respects the "minlen" property of incident edges.
     *    6. Graph edges must have a "minlen" property.
     *
     * Post-conditions:
     *
     *    - Graph nodes will have their rank adjusted to ensure that all edges are
     *      tight.
     *
     * Returns a tree (undirected graph) that is constructed using only "tight"
     * edges.
     */
    public static Graph Run(Graph graph)
    {
        var tree = new Graph(directed: false);

        // Choose arbitrary node from which to start our tree
        var nodes = graph.Nodes();
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException("Graph must have at least one node");
        }

        var start = nodes[0];
        var size = graph.NodeCount;
        tree.SetNode(start, new());

        while (TightTree(tree, graph) < size)
        {
            var edge = FindMinSlackEdge(tree, graph);
            if (edge == null)
            {
                break;
            }

            var delta = tree.HasNode(edge.V) ? RankUtil.Slack(graph, edge) : -RankUtil.Slack(graph, edge);
            ShiftRanks(tree, graph, delta);
        }

        return tree;
    }

    /*
     * Finds a maximal tree of tight edges and returns the number of nodes in the
     * tree.
     */
    static int TightTree(Graph tree, Graph graph)
    {
        // Depth-first on an explicit stack rather than by recursion, so a long tight path cannot overflow
        // the thread's stack. Each edge is tested when the walk reaches it, after everything the earlier
        // edges led to has joined the tree.
        var pending = new Stack<(string V, List<EdgeKey> Edges, int Next)>();

        void Enter(string v)
        {
            if (graph.NodeEdges(v) is { Count: > 0 } nodeEdges)
            {
                pending.Push((v, nodeEdges, 0));
            }
        }

        void Dfs(string start)
        {
            Enter(start);
            while (pending.Count > 0)
            {
                var (v, edges, next) = pending.Pop();
                if (next + 1 < edges.Count)
                {
                    pending.Push((v, edges, next + 1));
                }

                var e = edges[next];
                var edgeV = e.V;
                var w = v == edgeV ? e.W : edgeV;
                if (!tree.HasNode(w) && RankUtil.Slack(graph, e) == 0)
                {
                    tree.SetNode(w, new());
                    tree.SetEdge(v, w, new());
                    Enter(w);
                }
            }
        }

        foreach (var v in tree.Nodes())
        {
            Dfs(v);
        }

        return tree.NodeCount;
    }

    /*
     * Finds the edge with the smallest slack that is incident on tree and returns
     * it.
     */
    static EdgeKey? FindMinSlackEdge(Graph tree, Graph graph)
    {
        var edges = graph.Edges();

        var accSlack = double.PositiveInfinity;
        EdgeKey? accEdge = null;
        foreach (var edge in edges)
        {
            var edgeSlack = double.PositiveInfinity;
            if (tree.HasNode(edge.V) != tree.HasNode(edge.W))
            {
                edgeSlack = RankUtil.Slack(graph, edge);
            }

            if (edgeSlack < accSlack)
            {
                accSlack = edgeSlack;
                accEdge = edge;
            }
        }

        return accEdge;
    }

    static void ShiftRanks(Graph tree, Graph graph, int delta)
    {
        foreach (var v in tree.Nodes())
        {
            graph.NodeLabel(v).Rank += delta;
        }
    }
}
