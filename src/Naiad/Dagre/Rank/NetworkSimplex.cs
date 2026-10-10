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
        InitLowLimValues(t);
        InitCutValues(t, graph);

        while (LeaveEdge(t) is { } e)
        {
            var f = EnterEdge(t, graph, e);
            ExchangeEdges(t, graph, e, f);
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

        var nodeEdges = graph.NodeEdges(child);
        if (nodeEdges != null)
        {
            foreach (var edge in nodeEdges)
            {
                var isOutEdge = edge.V == child;
                var other = isOutEdge ? edge.W : edge.V;

                if (other == parent)
                {
                    continue;
                }

                var pointsToHead = isOutEdge == childIsTail;
                var otherWeight = graph.FindEdgeLabel(edge).Weight!.Value;

                cutValue += pointsToHead ? otherWeight : -otherWeight;
                if (!IsTreeEdge(tree, child, other))
                {
                    continue;
                }

                var treeEdge = tree.FindEdgeLabel(child, other);
                var otherCutValue = treeEdge.Cutvalue!.Value;
                cutValue += pointsToHead ? -otherCutValue : otherCutValue;
            }
        }

        return cutValue;
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

    public static void ExchangeEdges(Graph t, Graph graph, EdgeKey e, EdgeKey f)
    {
        var v = e.V;
        var w = e.W;
        t.RemoveEdge(v, w);
        t.SetEdge(f.V, f.W, new());
        InitLowLimValues(t);
        InitCutValues(t, graph);
        UpdateRanks(t, graph);
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
    }

    /*
     * Returns true if the edge is in the tree.
     */
    static bool IsTreeEdge(Graph tree, string u, string v) =>
        tree.HasEdge(u, v);

    /*
     * Returns true if the specified node is descendant of the root node per the
     * assigned low and lim attributes in the tree.
     */
    static bool IsDescendant(NodeLabel vLabel, NodeLabel rootLabel) =>
        rootLabel.Low!.Value <= vLabel.Lim!.Value && vLabel.Lim!.Value <= rootLabel.Lim!.Value;
}
