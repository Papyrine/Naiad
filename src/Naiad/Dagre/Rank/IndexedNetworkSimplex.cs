/// <summary>
/// The ranks <see cref="NetworkSimplex.Run"/> gives the nodes of a layout graph that are not subgraphs,
/// worked out over node numbers and arrays.
/// </summary>
/// <remarks>
/// <see cref="NetworkSimplex"/> is the port of dagre's module and reads as dagre does. It ranks a copy of
/// the graph without its subgraphs, and a copy of that copy with each set of parallel edges merged into
/// one. Every node is a name and the tree it pivots on is a graph of its own, so every step of a pivot is
/// a run of lookups by name; and after each pivot it numbers the whole tree again and works out every cut
/// value afresh. On a large graph ranking was nearly all of the layout's time.
///
/// Here the nodes are numbered once and the merged edges are held in arrays, read straight off the graph
/// with no copy of it taken. The feasible tree is grown as <see cref="FeasibleTree"/> grows it: from the
/// same node, through the same edges taken in the same order, so it is the same tree, with its edges in
/// the same order. The pivots are then the ones <see cref="NetworkSimplex"/> makes. The first tree edge
/// in that order with a negative cut value leaves, and the edge across the cut with the least slack
/// enters, the first of the graph's edges among equals. What a pivot changes is then brought up to date
/// where it changed, as Graphviz does: the ranks on the side of the tree that moved, the cut values along
/// the cycle the entering edge closes, and the low and lim numbers below the top of that cycle.
///
/// Those numbers are not the ones <see cref="NetworkSimplex"/> hands out, which follow the order its tree
/// happens to keep each node's neighbours in. Nothing reads them except to ask which nodes lie below
/// which, and that is a matter of the tree alone. Cut values updated along the cycle come to the values
/// worked out afresh only if every sum is exact, so a graph whose weights are negative, are not whole
/// numbers, or are large enough for a sum to round is declined. So is a graph the port would not rank as
/// it ranks a layout's - one with a cycle, a self-loop, an edge to a subgraph, a node without a label, or
/// parts the tree cannot join - and each of those is left to <see cref="NetworkSimplex"/>. None of them
/// reaches ranking in a layout.
/// </remarks>
sealed class IndexedNetworkSimplex
{
    // Every whole number below this is held exactly by a double.
    const double exactLimit = 9007199254740992;

    readonly string[] names;
    readonly NodeLabel[] labels;
    readonly int nodeCount;
    readonly int edgeCount;

    // The edges with parallel ones merged, numbered in the order Util.Simplify makes them: the order in
    // which the graph first joins each pair of nodes. A merged edge has the sum of the weights and the
    // greatest of the minimum lengths.
    readonly int[] tail;
    readonly int[] head;
    readonly int[] minlen;
    readonly double[] weight;

    // Each node's edges, kept from edgeStart[v]: those into it up to inEnd[v], then those out of it up to
    // edgeStart[v + 1]. Each side is in the order of the edges' numbers, which is the order the merged
    // graph holds them in.
    readonly int[] edgeStart;
    readonly int[] inEnd;
    readonly int[] incident;

    readonly int[] rank;

    // The tree. Each of its edges is one of the graph's: the merged graph has no more than one edge
    // between two nodes, and nor does the tree.
    readonly bool[] inTree;
    readonly int[] treeNodes;
    int treeSize;
    readonly bool[] isTreeEdge;
    readonly double[] cutValue;

    // Each node's tree edges, in no particular order, kept in the stretch of treeIncident that incident
    // gives the node, since a node has no more tree edges than edges. An edge's place at each of its ends
    // is kept too, so that it can be taken out without a search.
    readonly int[] treeIncident;
    readonly int[] treeDegree;
    readonly int[] slotAtTail;
    readonly int[] slotAtHead;

    // The tree edges in the order the tree holds them, which is the order they joined it in. A pivot takes
    // its leaving edge out, leaving a gap, and puts its entering edge on the end; the gaps are squeezed out
    // when the end is reached.
    readonly int[] treeOrder;
    readonly int[] treeOrderSlot;
    int treeOrderCount;
    int treeOrderFirst;

    // The tree walked depth-first from node 0, its root, with each node numbered as the walk finishes it:
    // lim is the node's number and low the lowest number below it, so the nodes below a node are those
    // numbered from its low to its lim. parentEdge is the tree edge towards the root.
    readonly int[] low;
    readonly int[] lim;
    readonly int[] parentEdge;
    readonly int[] nodeByLim;

    // The explicit stack of the depth-first walks: a node, and where in its edges the walk has got to.
    readonly int[] stackNode;
    readonly int[] stackSlot;

    IndexedNetworkSimplex(
        string[] names,
        NodeLabel[] labels,
        int nodeCount,
        int edgeCount,
        int[] tail,
        int[] head,
        int[] minlen,
        double[] weight)
    {
        this.names = names;
        this.labels = labels;
        this.nodeCount = nodeCount;
        this.edgeCount = edgeCount;
        this.tail = tail;
        this.head = head;
        this.minlen = minlen;
        this.weight = weight;

        edgeStart = new int[nodeCount + 1];
        inEnd = new int[nodeCount];
        incident = new int[edgeCount * 2];
        var inCount = new int[nodeCount];
        for (var e = 0; e < edgeCount; e++)
        {
            inCount[head[e]]++;
            edgeStart[tail[e] + 1]++;
            edgeStart[head[e] + 1]++;
        }

        for (var v = 0; v < nodeCount; v++)
        {
            edgeStart[v + 1] += edgeStart[v];
        }

        var nextIn = new int[nodeCount];
        var nextOut = new int[nodeCount];
        for (var v = 0; v < nodeCount; v++)
        {
            nextIn[v] = edgeStart[v];
            inEnd[v] = edgeStart[v] + inCount[v];
            nextOut[v] = inEnd[v];
        }

        for (var e = 0; e < edgeCount; e++)
        {
            incident[nextIn[head[e]]++] = e;
            incident[nextOut[tail[e]]++] = e;
        }

        rank = new int[nodeCount];
        inTree = new bool[nodeCount];
        treeNodes = new int[nodeCount];
        isTreeEdge = new bool[edgeCount];
        cutValue = new double[edgeCount];
        treeIncident = new int[edgeCount * 2];
        treeDegree = new int[nodeCount];
        slotAtTail = new int[edgeCount];
        slotAtHead = new int[edgeCount];
        treeOrder = new int[Math.Max(4, nodeCount * 2)];
        treeOrderSlot = new int[edgeCount];
        low = new int[nodeCount];
        lim = new int[nodeCount];
        parentEdge = new int[nodeCount];
        nodeByLim = new int[nodeCount + 1];
        stackNode = new int[nodeCount + 1];
        stackSlot = new int[nodeCount + 1];
    }

    /// <summary>
    /// Ranks the nodes of <paramref name="graph"/> that are not subgraphs, as <see cref="NetworkSimplex.Run"/>
    /// ranks the graph's non-compound copy. Returns false, having ranked nothing, for a graph this declines.
    /// <paramref name="trace"/>, when given, is filled with the edges of the feasible tree in their order
    /// and then the leaving and entering edge of every pivot, which is what the tests compare.
    /// </summary>
    internal static bool TryRun(Graph graph, List<string>? trace = null)
    {
        if (TryBuild(graph) is not { } network ||
            !network.TryRank(trace))
        {
            return false;
        }

        for (var v = 0; v < network.nodeCount; v++)
        {
            network.labels[v].Rank = network.rank[v];
        }

        return true;
    }

    // Numbers the nodes that are not subgraphs in the order the graph has them, and merges the edges as
    // Util.Simplify does: the weights added up in the order the edges come, from zero, and the largest
    // minimum length kept, counting a missing one as 1.
    static IndexedNetworkSimplex? TryBuild(Graph graph)
    {
        var names = new string[graph.NodeCount];
        var labels = new NodeLabel[graph.NodeCount];
        var nodeCount = 0;
        foreach (var (v, node) in graph.EnumerateNodeEntries())
        {
            if (graph.ChildCount(v) != 0)
            {
                // A subgraph is not ranked. One with a label is marked so that an edge to it is
                // recognised below; an edge to one without a label is caught by the missing label.
                node?.Number = -1;
                continue;
            }

            if (node is null)
            {
                return null;
            }

            node.Number = nodeCount;
            names[nodeCount] = v;
            labels[nodeCount] = node;
            nodeCount++;
        }

        if (nodeCount == 0)
        {
            return null;
        }

        var tail = new int[graph.EdgeCount];
        var head = new int[graph.EdgeCount];
        var minlen = new int[graph.EdgeCount];
        var weight = new double[graph.EdgeCount];
        // Keyed by the pair itself. A long made of the two numbers hashes to the one xor the other, which
        // puts every edge between neighbouring numbers - a chain - into a handful of buckets.
        var merged = new Dictionary<(int V, int W), int>(graph.EdgeCount);
        var edgeCount = 0;
        foreach (var e in graph.EnumerateEdges())
        {
            if (!graph.TryGetNodeLabel(e.V, out var vLabel) ||
                !graph.TryGetNodeLabel(e.W, out var wLabel))
            {
                return null;
            }

            var v = vLabel.Number;
            var w = wLabel.Number;
            if (v < 0 ||
                w < 0 ||
                v == w ||
                graph.FindEdgeLabel(e) is not { } label)
            {
                return null;
            }

            var key = (v, w);
            if (!merged.TryGetValue(key, out var index))
            {
                index = edgeCount++;
                merged[key] = index;
                tail[index] = v;
                head[index] = w;
                minlen[index] = 1;
            }

            weight[index] += label.Weight ?? 0;
            minlen[index] = Math.Max(minlen[index], label.Minlen ?? 1);
        }

        // Whole, non-negative weights whose cut values, and every partial sum on the way to one, stay
        // below the point where a double starts to round. A cut value is a signed sum of distinct weights,
        // so none is larger than their total, and a partial sum adds at most one weight and one cut value
        // for each of a node's edges.
        var total = 0d;
        for (var e = 0; e < edgeCount; e++)
        {
            var value = weight[e];
            if (!(value >= 0) ||
                Math.Floor(value) != value)
            {
                return null;
            }

            total += value;
        }

        if (!(total * (edgeCount + 2d) < exactLimit))
        {
            return null;
        }

        return new(names, labels, nodeCount, edgeCount, tail, head, minlen, weight);
    }

    bool TryRank(List<string>? trace)
    {
        if (!AssignLongestPathRanks() ||
            !GrowFeasibleTree())
        {
            return false;
        }

        if (trace != null)
        {
            for (var i = treeOrderFirst; i < treeOrderCount; i++)
            {
                trace.Add("tree " + Describe(treeOrder[i]));
            }
        }

        // Every node but the root, children before their parents: a cut value is worked out from those of
        // the tree edges below it.
        Number(0, -1, 1);
        for (var number = 1; number < nodeCount; number++)
        {
            var v = nodeByLim[number];
            cutValue[parentEdge[v]] = CutValue(v);
        }

        while (LeaveEdge() is var leaving and >= 0)
        {
            var entering = EnterEdge(leaving);
            if (entering < 0)
            {
                return false;
            }

            trace?.Add($"pivot {Describe(leaving)} {Describe(entering)}");
            Exchange(leaving, entering);
        }

        return true;
    }

    string Describe(int e) =>
        names[tail[e]] + ">" + names[head[e]];

    int Slack(int e) =>
        rank[head[e]] - rank[tail[e]] - minlen[e];

    /// <summary>
    /// <see cref="RankUtil.LongestPath"/>: each node as far down as its successors allow, a node without
    /// any at 0. That is one rank for each node whatever order the nodes are reached in, so the walk takes
    /// them in their own order. It returns false on meeting a cycle, a precondition the port does not check.
    /// </summary>
    bool AssignLongestPathRanks()
    {
        // 0 for a node not reached yet, 1 for one the walk is below, 2 for one ranked.
        var state = new byte[nodeCount];
        for (var start = 0; start < nodeCount; start++)
        {
            if (state[start] != 0)
            {
                continue;
            }

            state[start] = 1;
            stackNode[0] = start;
            stackSlot[0] = inEnd[start];
            var depth = 1;
            while (depth > 0)
            {
                var v = stackNode[depth - 1];
                var slot = stackSlot[depth - 1];
                var end = edgeStart[v + 1];
                if (slot == end)
                {
                    // As the port works it out: in doubles, with no successor counting as 0.
                    var lowest = double.PositiveInfinity;
                    for (var s = inEnd[v]; s < end; s++)
                    {
                        var e = incident[s];
                        lowest = Math.Min(lowest, (double) rank[head[e]] - minlen[e]);
                    }

                    if (double.IsPositiveInfinity(lowest))
                    {
                        lowest = 0;
                    }

                    rank[v] = (int) lowest;
                    state[v] = 2;
                    depth--;
                    continue;
                }

                stackSlot[depth - 1] = slot + 1;
                var w = head[incident[slot]];
                if (state[w] == 1)
                {
                    return false;
                }

                if (state[w] == 0)
                {
                    state[w] = 1;
                    stackNode[depth] = w;
                    stackSlot[depth] = inEnd[w];
                    depth++;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// <see cref="FeasibleTree.Run"/>, step for step: the tree starts at node 0 and takes in what tight
    /// edges reach; while nodes remain outside it, the tree is shifted by the least slack of an edge
    /// leaving it, the first such edge among equals, and every node of the tree is walked from again.
    /// Returns false for a graph in pieces, which the tree cannot span.
    /// </summary>
    bool GrowFeasibleTree()
    {
        AddTreeNode(0);
        TightTree();
        while (treeSize < nodeCount)
        {
            var edge = MinSlackEdge();
            if (edge < 0)
            {
                return false;
            }

            var slack = Slack(edge);
            var delta = inTree[tail[edge]] ? slack : -slack;
            for (var i = 0; i < treeSize; i++)
            {
                rank[treeNodes[i]] += delta;
            }

            TightTree();
        }

        return true;
    }

    // The nodes in the tree when the walk starts, each walked from in the order it joined. A node the walk
    // takes in is walked from as it joins, and not again in this round.
    void TightTree()
    {
        var known = treeSize;
        for (var i = 0; i < known; i++)
        {
            TightFrom(treeNodes[i]);
        }
    }

    // Depth-first along tight edges to nodes outside the tree, a node's edges into it before those out of
    // it, as Graph.NodeEdges lists them.
    void TightFrom(int start)
    {
        if (edgeStart[start] == edgeStart[start + 1])
        {
            return;
        }

        stackNode[0] = start;
        stackSlot[0] = edgeStart[start];
        var depth = 1;
        while (depth > 0)
        {
            depth--;
            var v = stackNode[depth];
            var slot = stackSlot[depth];
            if (slot + 1 < edgeStart[v + 1])
            {
                stackSlot[depth] = slot + 1;
                depth++;
            }

            var e = incident[slot];
            var w = tail[e] == v ? head[e] : tail[e];
            if (inTree[w] ||
                Slack(e) != 0)
            {
                continue;
            }

            AddTreeNode(w);
            AddTreeEdge(e);
            if (edgeStart[w] < edgeStart[w + 1])
            {
                stackNode[depth] = w;
                stackSlot[depth] = edgeStart[w];
                depth++;
            }
        }
    }

    // The first edge with one end in the tree and the least slack, or -1 if there is no such edge.
    int MinSlackEdge()
    {
        var best = -1;
        var bestSlack = 0;
        for (var e = 0; e < edgeCount; e++)
        {
            if (inTree[tail[e]] == inTree[head[e]])
            {
                continue;
            }

            var slack = Slack(e);
            if (best < 0 ||
                slack < bestSlack)
            {
                best = e;
                bestSlack = slack;
            }
        }

        return best;
    }

    void AddTreeNode(int v)
    {
        inTree[v] = true;
        treeNodes[treeSize++] = v;
    }

    void AddTreeEdge(int e)
    {
        isTreeEdge[e] = true;

        var t = tail[e];
        var slot = edgeStart[t] + treeDegree[t]++;
        treeIncident[slot] = e;
        slotAtTail[e] = slot;

        var h = head[e];
        slot = edgeStart[h] + treeDegree[h]++;
        treeIncident[slot] = e;
        slotAtHead[e] = slot;

        if (treeOrderCount == treeOrder.Length)
        {
            SqueezeTreeOrder();
        }

        treeOrderSlot[e] = treeOrderCount;
        treeOrder[treeOrderCount++] = e;
    }

    void RemoveTreeEdge(int e)
    {
        isTreeEdge[e] = false;
        RemoveFromNode(e, tail[e], slotAtTail[e]);
        RemoveFromNode(e, head[e], slotAtHead[e]);

        treeOrder[treeOrderSlot[e]] = -1;
        while (treeOrderFirst < treeOrderCount &&
               treeOrder[treeOrderFirst] < 0)
        {
            treeOrderFirst++;
        }
    }

    // Moves the node's last tree edge into the place of the one taken out.
    void RemoveFromNode(int e, int v, int slot)
    {
        var last = edgeStart[v] + --treeDegree[v];
        var moved = treeIncident[last];
        treeIncident[slot] = moved;
        if (moved == e)
        {
            return;
        }

        if (tail[moved] == v)
        {
            slotAtTail[moved] = slot;
            return;
        }

        slotAtHead[moved] = slot;
    }

    // The tree has one edge fewer than it has nodes, and room for twice that, so squeezing out the gaps
    // always leaves room at the end.
    void SqueezeTreeOrder()
    {
        var next = 0;
        for (var i = treeOrderFirst; i < treeOrderCount; i++)
        {
            var e = treeOrder[i];
            if (e < 0)
            {
                continue;
            }

            treeOrderSlot[e] = next;
            treeOrder[next++] = e;
        }

        treeOrderCount = next;
        treeOrderFirst = 0;
    }

    /// <summary>
    /// Numbers the nodes below and including <paramref name="start"/>, from <paramref name="nextLim"/> up,
    /// as a depth-first walk away from <paramref name="startParentEdge"/> finishes them.
    /// </summary>
    void Number(int start, int startParentEdge, int nextLim)
    {
        parentEdge[start] = startParentEdge;
        low[start] = nextLim;
        stackNode[0] = start;
        stackSlot[0] = edgeStart[start];
        var depth = 1;
        while (depth > 0)
        {
            var v = stackNode[depth - 1];
            var slot = stackSlot[depth - 1];
            if (slot == edgeStart[v] + treeDegree[v])
            {
                lim[v] = nextLim;
                nodeByLim[nextLim] = v;
                nextLim++;
                depth--;
                continue;
            }

            stackSlot[depth - 1] = slot + 1;
            var e = treeIncident[slot];
            if (e == parentEdge[v])
            {
                continue;
            }

            var w = tail[e] == v ? head[e] : tail[e];
            parentEdge[w] = e;
            low[w] = nextLim;
            stackNode[depth] = w;
            stackSlot[depth] = edgeStart[w];
            depth++;
        }
    }

    bool IsBelow(int v, int top) =>
        low[top] <= lim[v] &&
        lim[v] <= lim[top];

    /// <summary>
    /// <see cref="NetworkSimplex.CalcCutValue"/>: the weight of the edge to the child's parent, and for each
    /// of the child's other edges, those into it and then those out of it, its weight and the cut value of
    /// the tree edge it is, if it is one, each with the sign that edge's direction gives it.
    /// </summary>
    double CutValue(int child)
    {
        var toParent = parentEdge[child];
        var childIsTail = tail[toParent] == child;
        var sum = weight[toParent];
        var end = edgeStart[child + 1];
        var outFrom = inEnd[child];
        for (var slot = edgeStart[child]; slot < end; slot++)
        {
            var e = incident[slot];
            if (e == toParent)
            {
                continue;
            }

            var pointsToHead = (slot >= outFrom) == childIsTail;
            var value = pointsToHead ? weight[e] : -weight[e];
            if (isTreeEdge[e])
            {
                var other = cutValue[e];
                value += pointsToHead ? -other : other;
            }

            sum += value;
        }

        return sum;
    }

    // The first tree edge, in the order the tree holds them, with a negative cut value; -1 if none has one.
    int LeaveEdge()
    {
        for (var i = treeOrderFirst; i < treeOrderCount; i++)
        {
            var e = treeOrder[i];
            if (e >= 0 &&
                cutValue[e] < 0)
            {
                return e;
            }
        }

        return -1;
    }

    /// <summary>
    /// <see cref="NetworkSimplex.EnterEdge"/>: of the edges that cross between the two sides the leaving
    /// edge parts, in the direction opposite to it, the one with the least slack, and the first of those by
    /// number. Every such edge has one end below the leaving edge and one end above it, so the search runs
    /// over the edges of whichever side has fewer nodes. Returns -1 if there is no such edge, which with
    /// weights that are not negative does not happen.
    /// </summary>
    int EnterEdge(int leaving)
    {
        // The leaving edge's end away from the root, and whether that is its head.
        var child = tail[leaving];
        var flip = false;
        if (lim[child] > lim[head[leaving]])
        {
            child = head[leaving];
            flip = true;
        }

        var childLow = low[child];
        var childLim = lim[child];
        var best = -1;
        var bestSlack = 0;

        void Search(int fromNumber, int toNumber)
        {
            for (var number = fromNumber; number <= toNumber; number++)
            {
                var v = nodeByLim[number];
                for (var slot = edgeStart[v]; slot < edgeStart[v + 1]; slot++)
                {
                    var e = incident[slot];
                    var tailLim = lim[tail[e]];
                    var headLim = lim[head[e]];
                    if (flip != (childLow <= tailLim && tailLim <= childLim) ||
                        flip == (childLow <= headLim && headLim <= childLim))
                    {
                        continue;
                    }

                    var slack = Slack(e);
                    if (best < 0 ||
                        slack < bestSlack ||
                        (slack == bestSlack && e < best))
                    {
                        best = e;
                        bestSlack = slack;
                    }
                }
            }
        }

        var belowCount = childLim - childLow + 1;
        if (belowCount <= nodeCount - belowCount)
        {
            Search(childLow, childLim);
        }
        else
        {
            Search(1, childLow - 1);
            Search(childLim + 1, nodeCount);
        }

        return best;
    }

    /// <summary>
    /// <see cref="NetworkSimplex.ExchangeEdges(Graph, Graph, EdgeKey, EdgeKey)"/>, done as Graphviz's
    /// network simplex does it. The port ranks every node afresh from its parent; the root keeps its rank
    /// and every tree edge is tight, so that moves only the nodes below the leaving edge, all by the slack
    /// of the entering edge. The cut values that change are those along the path the entering edge closes
    /// into a cycle, each by the leaving edge's cut value one way or the other, and the entering edge takes
    /// the negative of it. The nodes below the top of that path are the same nodes before and after, so
    /// they alone are numbered again, into the same numbers.
    /// </summary>
    void Exchange(int leaving, int entering)
    {
        var below = tail[leaving];
        if (lim[below] > lim[head[leaving]])
        {
            below = head[leaving];
        }

        var slack = Slack(entering);
        if (slack != 0)
        {
            var delta = slack;
            if (IsBelow(head[entering], below))
            {
                delta = -slack;
            }

            for (var number = low[below]; number <= lim[below]; number++)
            {
                rank[nodeByLim[number]] += delta;
            }
        }

        var leavingCutValue = cutValue[leaving];
        var top = UpdateCutValuesUpTo(tail[entering], head[entering], leavingCutValue, true);
        UpdateCutValuesUpTo(head[entering], tail[entering], leavingCutValue, false);

        RemoveTreeEdge(leaving);
        AddTreeEdge(entering);
        cutValue[entering] = -leavingCutValue;

        Number(top, parentEdge[top], low[top]);
    }

    // Graphviz's treeupdate: from v up the tree to the first node with w below it, the top of the cycle,
    // adding the leaving edge's cut value to each tree edge passed or taking it away, as the edge points
    // with the walk or against it. Returns the top.
    int UpdateCutValuesUpTo(int v, int w, double leavingCutValue, bool forward)
    {
        while (!IsBelow(w, v))
        {
            var e = parentEdge[v];
            var add = forward;
            if (v != tail[e])
            {
                add = !forward;
            }

            if (add)
            {
                cutValue[e] += leavingCutValue;
            }
            else
            {
                cutValue[e] -= leavingCutValue;
            }

            v = lim[tail[e]] > lim[head[e]] ? tail[e] : head[e];
        }

        return v;
    }
}
