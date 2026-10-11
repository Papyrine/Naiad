static class NetworkSimplex
{
    /*
     * The network simplex algorithm assigns ranks to each node in the input graph
     * and iteratively improves the ranking to reduce the length of edges.
     *
     * Preconditions:
     *
     *    1. The input graph must be a DAG.
     *    2. All nodes in the graph must have an object value.
     *    3. All edges in the graph must have "minlen" and "weight" attributes.
     *
     * Postconditions:
     *
     *    1. All nodes in the graph will have an assigned "rank" attribute that has
     *       been optimized by the network simplex algorithm. Ranks start at 0.
     */
    public static void Run(Graph graph)
    {
        graph = Util.Simplify(graph);
        RankUtil.LongestPath(graph);

        var t = FeasibleTree.Run(graph);
        var scratch = new Scratch();
        InitLowLimAndCutValues(t, graph, scratch);

        while (LeaveEdge(t) is { } e)
        {
            var f = EnterEdge(t, graph, e);
            ExchangeEdges(t, graph, e, f, scratch);
        }
    }

    // Working storage for the walk each pivot makes over the tree, kept from one pivot to the next.
    sealed class Scratch
    {
        public readonly HashSet<string> Visited = new(StringComparer.Ordinal);
        public readonly List<string> Neighbors = [];
        public readonly List<string> Postorder = [];
        public readonly Stack<(string V, string? Parent, int Low, int Next, int End)> Pending = new();
    }

    /// <summary>
    /// <see cref="InitLowLimValues(Graph)"/> followed by <see cref="InitCutValues"/> in one walk. Both
    /// visit the tree depth-first from its first node, taking each node's neighbours in the same order,
    /// so the post-order the cut values are assigned in is the order the lim numbers are handed out in.
    /// The walk keeps that order, and its neighbour lists sit end to end in one buffer rather than a new
    /// list for every node.
    /// </summary>
    static void InitLowLimAndCutValues(Graph tree, Graph graph, Scratch scratch)
    {
        var visited = scratch.Visited;
        var neighbors = scratch.Neighbors;
        var postorder = scratch.Postorder;
        var pending = scratch.Pending;
        visited.Clear();
        neighbors.Clear();
        postorder.Clear();
        pending.Clear();

        var nextLim = 1;

        void Enter(string node, string? parent)
        {
            visited.Add(node);
            var start = neighbors.Count;

            // The tree has one edge at most between two nodes, so a node's predecessors and successors
            // never overlap and together are its neighbours in the order Graph.Neighbors gives them.
            foreach (var predecessor in tree.PredecessorsOf(node))
            {
                neighbors.Add(predecessor);
            }

            foreach (var successor in tree.SuccessorsOf(node))
            {
                neighbors.Add(successor);
            }

            pending.Push((node, parent, nextLim, start, neighbors.Count));
        }

        Enter(tree.Nodes()[0], null);
        while (pending.Count > 0)
        {
            var frame = pending.Pop();
            if (frame.Next == frame.End)
            {
                var label = tree.NodeLabel(frame.V);
                label.Low = frame.Low;
                label.Lim = nextLim++;
                label.Parent = frame.Parent;
                postorder.Add(frame.V);
                continue;
            }

            pending.Push(frame with { Next = frame.Next + 1 });
            var w = neighbors[frame.Next];
            if (!visited.Contains(w))
            {
                Enter(w, frame.V);
            }
        }

        // Every node but the root, which is the last one finished and has no parent edge to value.
        for (var i = 0; i < postorder.Count - 1; i++)
        {
            AssignCutValue(tree, graph, postorder[i]);
        }
    }

    /*
     * Initializes cut values for all edges in the tree.
     */
    public static void InitCutValues(Graph tree, Graph graph)
    {
        var visitedNodes = Alg.Postorder(tree, tree.Nodes());
        visitedNodes = visitedNodes.GetRange(0, visitedNodes.Count - 1);
        foreach (var v in visitedNodes)
        {
            AssignCutValue(tree, graph, v);
        }
    }

    public static void AssignCutValue(Graph tree, Graph graph, string child)
    {
        var childLab = tree.NodeLabel(child);
        var parent = childLab.Parent!;
        var edge = tree.FindEdgeLabel(child, parent);
        edge.Cutvalue = CalcCutValue(tree, graph, child);
    }

    /*
     * Given the tight tree, its graph, and a child in the graph calculate and
     * return the cut value for the edge between the child and its parent.
     */
    public static double CalcCutValue(Graph tree, Graph graph, string child)
    {
        var childLab = tree.NodeLabel(child);
        var parent = childLab.Parent!;
        // True if the child is on the tail end of the edge in the directed graph
        var childIsTail = true;
        // The graph's view of the tree edge we're inspecting
        if (!graph.TryGetEdgeLabel(child, parent, out var graphEdge))
        {
            childIsTail = false;
            graphEdge = graph.FindEdgeLabel(parent, child);
        }

        // The accumulated cut value for the edge between this node and its parent
        var cutValue = graphEdge.Weight!.Value;

        if (!graph.HasNode(child))
        {
            return cutValue;
        }

        // The edges into the child and then the edges out of it, read where they are kept rather than
        // gathered into a list of their own first.
        foreach (var edge in graph.InEdgesOf(child))
        {
            cutValue += EdgeCutValue(tree, graph, edge, child, parent, childIsTail);
        }

        foreach (var edge in graph.OutEdgesOf(child))
        {
            // A self-loop is among the edges in as well, and has been counted there.
            if (edge.V != edge.W)
            {
                cutValue += EdgeCutValue(tree, graph, edge, child, parent, childIsTail);
            }
        }

        return cutValue;
    }

    // What one edge at the child, other than the one to its parent, adds to the child's cut value.
    static double EdgeCutValue(Graph tree, Graph graph, EdgeKey edge, string child, string parent, bool childIsTail)
    {
        var isOutEdge = edge.V == child;
        var other = isOutEdge ? edge.W : edge.V;

        if (other == parent)
        {
            return 0;
        }

        var pointsToHead = isOutEdge == childIsTail;
        var otherWeight = graph.FindEdgeLabel(edge).Weight!.Value;

        var value = pointsToHead ? otherWeight : -otherWeight;
        if (!tree.TryGetEdgeLabel(child, other, out var treeEdge))
        {
            return value;
        }

        var otherCutValue = treeEdge.Cutvalue!.Value;
        return value + (pointsToHead ? -otherCutValue : otherCutValue);
    }

    public static void InitLowLimValues(Graph tree) =>
        InitLowLimValues(tree, tree.Nodes()[0]);

    public static void InitLowLimValues(Graph tree, string root) =>
        DfsAssignLowLim(tree, new(StringComparer.Ordinal), 1, root, null);

    // Depth-first, post-order, on an explicit stack rather than by recursion: the tree is as deep as the
    // graph's longest path, and a long chain of nodes overflows the thread's stack.
    public static int DfsAssignLowLim(Graph tree, Dictionary<string, bool> visited, int nextLim, string v, string? parent)
    {
        var pending = new Stack<(string V, string? Parent, int Low, List<string>? Neighbors, int Next)>();

        void Enter(string node, string? nodeParent)
        {
            visited[node] = true;
            pending.Push((node, nodeParent, nextLim, tree.Neighbors(node), 0));
        }

        Enter(v, parent);
        while (pending.Count > 0)
        {
            var frame = pending.Pop();
            if (frame.Neighbors is null ||
                frame.Next == frame.Neighbors.Count)
            {
                var label = tree.NodeLabel(frame.V);
                label.Low = frame.Low;
                label.Lim = nextLim++;
                label.Parent = frame.Parent;
                continue;
            }

            pending.Push(frame with { Next = frame.Next + 1 });
            var w = frame.Neighbors[frame.Next];
            if (!visited.ContainsKey(w))
            {
                Enter(w, frame.V);
            }
        }

        return nextLim;
    }

    public static EdgeKey? LeaveEdge(Graph tree)
    {
        foreach (var e in tree.EnumerateEdges())
        {
            if (tree.FindEdgeLabel(e).Cutvalue!.Value < 0)
            {
                return e;
            }
        }

        return null;
    }

    public static EdgeKey EnterEdge(Graph tree, Graph graph, EdgeKey edge)
    {
        var v = edge.V;
        var w = edge.W;

        // For the rest of this function we assume that v is the tail and w is the
        // head, so if we don't have this edge in the graph we should flip it to
        // match the correct orientation.
        if (!graph.HasEdge(v, w))
        {
            v = edge.W;
            w = edge.V;
        }

        var vLabel = tree.NodeLabel(v);
        var wLabel = tree.NodeLabel(w);
        var tailLabel = vLabel;
        var flip = false;

        // If the root is in the tail of the edge then we need to flip the logic that
        // checks for the head and tail nodes in the candidates function below.
        if (vLabel.Lim!.Value > wLabel.Lim!.Value)
        {
            tailLabel = wLabel;
            flip = true;
        }

        // Find the minimum-slack candidate in a single pass: folding the filter into the scan avoids
        // snapshotting the whole edge list (and re-computing the incumbent's slack) on every pivot.
        EdgeKey? acc = null;
        var accSlack = 0;
        foreach (var candidate in graph.EnumerateEdges())
        {
            if (flip == IsDescendant(tree.NodeLabel(candidate.V), tailLabel) &&
                flip != IsDescendant(tree.NodeLabel(candidate.W), tailLabel))
            {
                var slack = RankUtil.Slack(graph, candidate);
                if (acc == null || slack < accSlack)
                {
                    acc = candidate;
                    accSlack = slack;
                }
            }
        }

        return acc!;
    }

    public static void ExchangeEdges(Graph t, Graph graph, EdgeKey e, EdgeKey f) =>
        ExchangeEdges(t, graph, e, f, new());

    static void ExchangeEdges(Graph t, Graph graph, EdgeKey e, EdgeKey f, Scratch scratch)
    {
        var v = e.V;
        var w = e.W;
        t.RemoveEdge(v, w);
        t.SetEdge(f.V, f.W, new());
        InitLowLimAndCutValues(t, graph, scratch);

        // A node's rank is set from its parent's, so parents go first. The walk finished every node
        // after its children, which makes its order backwards the one wanted; the root comes first there
        // and keeps the rank it has.
        var postorder = scratch.Postorder;
        for (var i = postorder.Count - 2; i >= 0; i--)
        {
            UpdateRank(t, graph, postorder[i]);
        }
    }

    public static void UpdateRanks(Graph t, Graph graph)
    {
        var root = t.Nodes().FirstOrDefault(v =>
        {
            var node = t.NodeLabel(v);
            return node.Parent == null;
        });
        if (root == null)
        {
            return;
        }

        var vs = Alg.Preorder(t, [root]);
        vs = vs.GetRange(1, vs.Count - 1);
        foreach (var v in vs)
        {
            UpdateRank(t, graph, v);
        }
    }

    static void UpdateRank(Graph t, Graph graph, string v)
    {
        var treeNode = t.NodeLabel(v);
        var parent = treeNode.Parent!;
        var flipped = false;
        if (!graph.TryGetEdgeLabel(v, parent, out var edge))
        {
            edge = graph.FindEdgeLabel(parent, v);
            flipped = true;
        }

        graph.NodeLabel(v).Rank = graph.NodeLabel(parent).Rank!.Value + (flipped ? edge.Minlen!.Value : -edge.Minlen!.Value);
    }

    /*
     * Returns true if the specified node is descendant of the root node per the
     * assigned low and lim attributes in the tree.
     */
    static bool IsDescendant(NodeLabel vLabel, NodeLabel rootLabel) =>
        rootLabel.Low!.Value <= vLabel.Lim!.Value && vLabel.Lim!.Value <= rootLabel.Lim!.Value;
}
