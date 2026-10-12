/*
 * This module provides coordinate assignment based on Brandes and Köpf, "Fast
 * and Simple Horizontal Coordinate Assignment."
 */
static class BK
{
    internal sealed class AlignmentResult
    {
        public Dictionary<string, string> Root = new(StringComparer.Ordinal);
        public Dictionary<string, string> Align = new(StringComparer.Ordinal);
    }

    /*
     * Marks all edges in the graph with a type-1 conflict with the "type1Conflict"
     * property. A type-1 conflict is one where a non-inner segment crosses an
     * inner segment. An inner segment is an edge with both incident nodes marked
     * with the "dummy" property.
     *
     * This algorithm scans layer by layer, starting with the second, for type-1
     * conflicts between the current layer and the previous layer. For each layer
     * it scans the nodes from left to right until it reaches one that is incident
     * on an inner segment. It then scans predecessors to determine if they have
     * edges that cross that inner segment. At the end a final scan is done for all
     * nodes on the current rank to see if they cross the last visited inner
     * segment.
     *
     * This algorithm (safely) assumes that a dummy node will only be incident on a
     * single node in the layers being scanned.
     */
    internal static Dictionary<string, Dictionary<string, bool>> FindType1Conflicts(Graph graph, List<List<string?>> layering)
    {
        var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

        List<string?> VisitLayer(List<string?> prevLayer, List<string?> layer)
        {
            var k0 =
                // last visited node in the previous layer that is incident on an inner
                // segment.
                0;
            // Tracks the last node in this layer scanned for crossings with a type-1
            // segment.
            var scanPos = 0;
            var prevLayerLength = prevLayer.Count;
            var lastNode = layer[^1];

            for (var i = 0; i < layer.Count; i++)
            {
                var v = layer[i];
                var w = v is null ? null : FindOtherInnerSegmentNode(graph, v);
                var k1 = w != null ? graph.NodeLabel(w).Order!.Value : prevLayerLength;

                if (w == null && v != lastNode)
                {
                    continue;
                }

                for (var scanIndex = scanPos; scanIndex <= i; scanIndex++)
                {
                    var scanNode = layer[scanIndex];
                    if (scanNode is null)
                    {
                        continue;
                    }

                    foreach (var u in graph.PredecessorsOf(scanNode))
                    {
                        var uLabel = graph.NodeLabel(u);
                        var uPos = uLabel.Order!.Value;
                        if ((uPos < k0 || k1 < uPos) &&
                            !(uLabel.Dummy != null && graph.NodeLabel(scanNode).Dummy != null))
                        {
                            AddConflict(conflicts, u, scanNode);
                        }
                    }
                }

                scanPos = i + 1;
                k0 = k1;
            }

            return layer;
        }

        if (layering.Count != 0)
        {
            var acc = layering[0];
            for (var i = 1; i < layering.Count; i++)
            {
                acc = VisitLayer(acc, layering[i]);
            }
        }

        return conflicts;
    }

    internal static Dictionary<string, Dictionary<string, bool>> FindType2Conflicts(Graph graph, List<List<string?>> layering)
    {
        var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

        void Scan(List<string?> south, int southPos, int southEnd, int prevNorthBorder, int nextNorthBorder)
        {
            for (var i = Math.Max(southPos, 0); i < southEnd && i < south.Count; i++)
            {
                var v = south[i];
                if (v == null)
                {
                    continue;
                }

                if (graph.NodeLabel(v).Dummy == null)
                {
                    continue;
                }

                foreach (var u in graph.PredecessorsOf(v))
                {
                    var uNode = graph.NodeLabel(u);
                    if (uNode.Dummy != null &&
                        (uNode.Order!.Value < prevNorthBorder || uNode.Order!.Value > nextNorthBorder))
                    {
                        AddConflict(conflicts, u, v);
                    }
                }
            }
        }

        List<string?> VisitLayer(List<string?> north, List<string?> south)
        {
            var prevNorthPos = -1;
            var nextNorthPos = -1;
            var southPos = 0;

            // The scan to the end of the layer that closes each step depends only on where it starts and
            // on the border to its north, and those change only at a border node. dagre runs it at every
            // node regardless, which is the same scan repeated for the whole stretch between two border
            // nodes - a layer of n nodes scanned n times over. Scanning records conflicts in a set, so
            // repeating one adds nothing; it is run once for each start and border it is asked for.
            var scannedFrom = -1;
            var scannedBorder = -1;
            var scanned = false;

            for (var southLookahead = 0; southLookahead < south.Count; southLookahead++)
            {
                var v = south[southLookahead];
                if (v is not null &&
                    graph.NodeLabel(v).Dummy == DummyKind.Border)
                {
                    string? firstPred = null;
                    foreach (var predecessor in graph.PredecessorsOf(v))
                    {
                        firstPred = predecessor;
                        break;
                    }

                    if (firstPred != null)
                    {
                        nextNorthPos = graph.NodeLabel(firstPred).Order!.Value;
                        Scan(south, southPos, southLookahead, prevNorthPos, nextNorthPos);
                        southPos = southLookahead;
                        prevNorthPos = nextNorthPos;
                    }
                }

                if (scanned &&
                    scannedFrom == southPos &&
                    scannedBorder == nextNorthPos)
                {
                    continue;
                }

                Scan(south, southPos, south.Count, nextNorthPos, north.Count);
                scannedFrom = southPos;
                scannedBorder = nextNorthPos;
                scanned = true;
            }

            return south;
        }

        if (layering.Count != 0)
        {
            var acc = layering[0];
            for (var i = 1; i < layering.Count; i++)
            {
                acc = VisitLayer(acc, layering[i]);
            }
        }

        return conflicts;
    }

    internal static string? FindOtherInnerSegmentNode(Graph graph, string name)
    {
        if (graph.NodeLabel(name).Dummy != null)
        {
            foreach (var predecessor in graph.PredecessorsOf(name))
            {
                if (graph.NodeLabel(predecessor).Dummy != null)
                {
                    return predecessor;
                }
            }
        }

        return null;
    }

    internal static void AddConflict(Dictionary<string, Dictionary<string, bool>> conflicts, string v, string w)
    {
        if (string.CompareOrdinal(v, w) > 0)
        {
            (v, w) = (w, v);
        }

        if (!conflicts.TryGetValue(v, out var conflictsV))
        {
            conflicts[v] = conflictsV = new(StringComparer.Ordinal);
        }

        conflictsV[w] = true;
    }

    internal static bool HasConflict(Dictionary<string, Dictionary<string, bool>> conflicts, string v, string w)
    {
        if (string.CompareOrdinal(v, w) > 0)
        {
            (v, w) = (w, v);
        }

        return conflicts.TryGetValue(v, out var conflictsV) && conflictsV.ContainsKey(w);
    }

    /*
     * Try to align nodes into vertical "blocks" where possible. This algorithm
     * attempts to align a node with one of its median neighbors. If the edge
     * connecting a neighbor is a type-1 conflict then we ignore that possibility.
     * If a previous node has already formed a block with a node after the node
     * we're trying to form a block with, we also ignore that possibility - our
     * blocks would be split in that scenario.
     */
    internal static AlignmentResult VerticalAlignment(
        List<List<string?>> layering,
        Dictionary<string, Dictionary<string, bool>> conflicts,
        Func<string, OrderedMap<int>.KeyEnumerable> neighborFn)
    {
        // Every node gets an entry in each of these, so they are made the size they will reach: grown
        // a step at a time they throw away as much again as they end up holding.
        var nodeCount = 0;
        foreach (var layer in layering)
        {
            nodeCount += layer.Count;
        }

        var root = new Dictionary<string, string>(nodeCount, StringComparer.Ordinal);
        var align = new Dictionary<string, string>(nodeCount, StringComparer.Ordinal);
        var pos = new Dictionary<string, int>(nodeCount, StringComparer.Ordinal);

        // We cache the position here based on the layering because the graph and
        // layering may be out of sync. The layering matrix is manipulated to
        // generate different extreme alignments.
        foreach (var layer in layering)
        {
            for (var order = 0; order < layer.Count; order++)
            {
                var v = layer[order];
                if (v is null)
                {
                    continue;
                }

                root[v] = v;
                align[v] = v;
                pos[v] = order;
            }
        }

        // Reused across nodes: each node fully processes its neighbours before the next clears it.
        var ws = new List<string>();
        foreach (var layer in layering)
        {
            var prevIdx = -1;
            foreach (var v in layer)
            {
                if (v is null)
                {
                    continue;
                }

                ws.Clear();
                foreach (var w in neighborFn(v))
                {
                    ws.Add(w);
                }

                if (ws.Count != 0)
                {
                    // Stable sort by pos. Neighbour lists are tiny, so an insertion sort preserves the
                    // order of equal-pos neighbours without the OrderBy/ToList allocation on this
                    // 4×-per-layout path.
                    for (var si = 1; si < ws.Count; ++si)
                    {
                        var key = ws[si];
                        var keyPos = pos.GetValueOrDefault(key, 0);
                        var sj = si - 1;
                        while (sj >= 0 && pos.GetValueOrDefault(ws[sj], 0) > keyPos)
                        {
                            ws[sj + 1] = ws[sj];
                            --sj;
                        }

                        ws[sj + 1] = key;
                    }

                    var mp = (ws.Count - 1) / 2.0;
                    for (int i = (int) Math.Floor(mp), il = (int) Math.Ceiling(mp); i <= il; ++i)
                    {
                        var w = i >= 0 && i < ws.Count ? ws[i] : null;
                        if (w == null)
                        {
                            continue;
                        }

                        if (pos.TryGetValue(w, out var posW) && align[v] == v &&
                            prevIdx < posW &&
                            !HasConflict(conflicts, v, w))
                        {
                            if (root.TryGetValue(w, out var rootW))
                            {
                                align[w] = v;
                                align[v] = root[v] = rootW;
                                prevIdx = posW;
                            }
                        }
                    }
                }
            }
        }

        return new() { Root = root, Align = align };
    }

    internal static Dictionary<string, double> HorizontalCompaction(
        Graph graph,
        List<List<string?>> layering,
        Dictionary<string, string> root,
        Dictionary<string, string> align,
        bool reverseSep = false)
    {
        // This portion of the algorithm differs from BK due to a number of problems.
        // Instead of their algorithm we construct a new block graph and do two
        // sweeps. The first sweep places blocks with the smallest possible
        // coordinates. The second sweep removes unused space by moving blocks to the
        // greatest coordinates without violating separation.
        var blocks = new Blocks(graph, layering, root, reverseSep);
        var count = blocks.Names.Count;
        var blockXs = new double[count];
        var borderType = reverseSep ? BorderKind.Left : BorderKind.Right;

        // Visits every block after the blocks it depends on: those before it on the first pass, those
        // after it on the second.
        var stack = new List<int>(count);
        var visited = new bool[count];

        void Iterate(bool forward)
        {
            stack.Clear();
            for (var i = 0; i < count; i++)
            {
                stack.Add(i);
            }

            Array.Clear(visited);

            while (stack.Count > 0)
            {
                var elem = stack[^1];
                stack.RemoveAt(stack.Count - 1);

                if (visited[elem])
                {
                    if (forward)
                    {
                        Pass1(elem);
                    }
                    else
                    {
                        Pass2(elem);
                    }

                    continue;
                }

                visited[elem] = true;
                // Put the element back into the stack, so that we can process it
                // again after all of the blocks it depends on are processed.
                stack.Add(elem);
                if ((forward ? blocks.In[elem] : blocks.Out[elem]) is { } next)
                {
                    foreach (var edge in next)
                    {
                        stack.Add(edge.Other);
                    }
                }
            }
        }

        // First pass: smallest coordinate = max over in-edges (no in-edges leaves it 0).
        void Pass1(int elem)
        {
            double acc = 0;
            if (blocks.In[elem] is { } edges)
            {
                foreach (var edge in edges)
                {
                    acc = Math.Max(acc, blockXs[edge.Other] + blocks.Weights[edge.Weight]);
                }
            }

            blockXs[elem] = acc;
        }

        // Second pass: greatest coordinate = min over out-edges (no out-edges leaves min at +Infinity,
        // so the assignment below is skipped).
        void Pass2(int elem)
        {
            var min = double.PositiveInfinity;
            if (blocks.Out[elem] is { } edges)
            {
                foreach (var edge in edges)
                {
                    min = Math.Min(min, blockXs[edge.Other] - blocks.Weights[edge.Weight]);
                }
            }

            var node = graph.NodeLabel(blocks.Names[elem]);
            if (!double.IsPositiveInfinity(min) && node.BorderType != borderType)
            {
                blockXs[elem] = Math.Max(blockXs[elem], min);
            }
        }

        Iterate(forward: true);
        Iterate(forward: false);

        var xs = new Dictionary<string, double>(align.Count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            xs[blocks.Names[i]] = blockXs[i];
        }

        // Assign x coordinates to all nodes
        foreach (var v in align.Keys)
        {
            if (root.TryGetValue(v, out var rootV))
            {
                xs[v] = blocks.Index.TryGetValue(rootV, out var block) ? blockXs[block] : 0;
            }
        }

        return xs;
    }

    readonly record struct BlockEdge(int Other, int Weight);

    /// <summary>
    /// The block graph of <see cref="BuildBlockGraph"/> as plain lists: a block for every root, in the
    /// order the roots are met, and an edge from each block to the next one along in a layer, weighted
    /// with the widest separation any layer asks of that pair. Compaction builds one of these for each of
    /// its four alignments, and as general-purpose graphs they were most of what positioning allocated.
    /// </summary>
    sealed class Blocks
    {
        public readonly Dictionary<string, int> Index;
        public readonly List<string> Names = [];
        public readonly List<List<BlockEdge>?> In = [];
        public readonly List<List<BlockEdge>?> Out = [];
        public readonly List<double> Weights = [];

        public Blocks(Graph graph, List<List<string?>> layering, Dictionary<string, string> root, bool reverseSep)
        {
            Index = new(StringComparer.Ordinal);
            var edges = new Dictionary<long, int>();
            var graphLabel = graph.Label;
            var sepFn = Sep(graphLabel.NodeSeparation!.Value, graphLabel.EdgeSeparation!.Value, reverseSep);

            foreach (var layer in layering)
            {
                string? u = null;
                foreach (var v in layer)
                {
                    if (v is null)
                    {
                        continue;
                    }

                    if (!root.TryGetValue(v, out var vRoot))
                    {
                        continue;
                    }

                    var vBlock = Block(vRoot);
                    if (u != null &&
                        root.TryGetValue(u, out var uRoot))
                    {
                        var uBlock = Block(uRoot);
                        var separation = sepFn(graph, v, u);
                        var key = ((long) uBlock << 32) | (uint) vBlock;
                        if (edges.TryGetValue(key, out var existing))
                        {
                            Weights[existing] = Math.Max(separation, Weights[existing]);
                        }
                        else
                        {
                            var weight = Weights.Count;
                            edges[key] = weight;
                            Weights.Add(Math.Max(separation, 0));
                            (In[vBlock] ??= []).Add(new(uBlock, weight));
                            (Out[uBlock] ??= []).Add(new(vBlock, weight));
                        }
                    }

                    u = v;
                }
            }
        }

        int Block(string name)
        {
            if (Index.TryGetValue(name, out var index))
            {
                return index;
            }

            index = Names.Count;
            Index[name] = index;
            Names.Add(name);
            In.Add(null);
            Out.Add(null);
            return index;
        }
    }

    internal static Graph BuildBlockGraph(
        Graph graph,
        List<List<string?>> layering,
        Dictionary<string, string> root,
        bool reverseSep)
    {
        var blockGraph = new Graph();
        var graphLabel = graph.Label;
        var sepFn = Sep(graphLabel.NodeSeparation!.Value, graphLabel.EdgeSeparation!.Value, reverseSep);

        foreach (var layer in layering)
        {
            string? u = null;
            foreach (var v in layer)
            {
                if (v is not null &&
                    root.TryGetValue(v, out var vRoot))
                {
                    blockGraph.SetNode(vRoot);
                    if (u != null)
                    {
                        if (root.TryGetValue(u, out var uRoot))
                        {
                            var prevMaxVal = blockGraph.TryGetEdgeLabel(uRoot, vRoot, out var prevMax) ? prevMax.Weight ?? 0 : 0;
                            blockGraph.SetEdge(
                                uRoot,
                                vRoot,
                                new()
                                {
                                    Weight = Math.Max(sepFn(graph, v, u), prevMaxVal)
                                });
                        }
                    }

                    u = v;
                }
            }
        }

        return blockGraph;
    }

    /*
     * Returns the alignment that has the smallest width of the given alignments.
     */
    internal static Dictionary<string, double> FindSmallestWidthAlignment(Graph graph, Dictionary<string, Dictionary<string, double>> xss)
    {
        var currentMin = double.PositiveInfinity;
        Dictionary<string, double>? currentXs = null;

        foreach (var xs in xss.Values)
        {
            var max = double.NegativeInfinity;
            var min = double.PositiveInfinity;

            foreach (var (v, x) in xs)
            {
                var halfWidth = Width(graph, v) / 2;

                max = Math.Max(x + halfWidth, max);
                min = Math.Min(x - halfWidth, min);
            }

            var newMin = max - min;
            if (newMin < currentMin)
            {
                currentMin = newMin;
                currentXs = xs;
            }
        }

        return currentXs!;
    }

    /*
     * Align the coordinates of each of the layout alignments such that
     * left-biased alignments have their minimum coordinate at the same point as
     * the minimum coordinate of the smallest width alignment and right-biased
     * alignments have their maximum coordinate at the same point as the maximum
     * coordinate of the smallest width alignment.
     */
    internal static void AlignCoordinates(Dictionary<string, Dictionary<string, double>> xss, Dictionary<string, double> alignTo)
    {
        var alignToVals = alignTo.Values.ToList();
        var alignToMin = Util.ApplyMin(alignToVals);
        var alignToMax = Util.ApplyMax(alignToVals);

        foreach (var vert in new[] { "u", "d" })
        {
            foreach (var horiz in new[] { "l", "r" })
            {
                var alignment = vert + horiz;
                var xs = xss.GetValueOrDefault(alignment);

                if (xs == null || ReferenceEquals(xs, alignTo))
                {
                    continue;
                }

                var xsVals = xs.Values.ToList();
                var delta = alignToMin - Util.ApplyMin(xsVals);
                if (horiz != "l")
                {
                    delta = alignToMax - Util.ApplyMax(xsVals);
                }

                if (delta != 0)
                {
                    // xs is xss[alignment] (guarded != alignTo above); mutate it in place.
                    foreach (var key in xs.Keys.ToList())
                    {
                        xs[key] += delta;
                    }
                }
            }
        }
    }

    internal static Dictionary<string, double> Balance(Dictionary<string, Dictionary<string, double>> xss)
    {
        var ulMap = xss.GetValueOrDefault("ul");
        if (ulMap == null)
        {
            return new(StringComparer.Ordinal);
        }

        // Build a new dictionary: each key reads every alignment (which includes ulMap), so mutating
        // ulMap in place would corrupt later reads. The candidates for a key are sorted in one buffer
        // that every key reuses.
        var alignments = xss.Values.ToArray();
        var candidates = new double[alignments.Length];
        var balanced = new Dictionary<string, double>(ulMap.Count, StringComparer.Ordinal);
        foreach (var key in ulMap.Keys)
        {
            for (var i = 0; i < alignments.Length; i++)
            {
                candidates[i] = alignments[i].GetValueOrDefault(key, 0);
            }

            Array.Sort(candidates);
            var lower = candidates.Length > 1 ? candidates[1] : 0;
            var upper = candidates.Length > 2 ? candidates[2] : 0;
            balanced[key] = (lower + upper) / 2;
        }

        return balanced;
    }

    internal static Dictionary<string, double> PositionX(Graph graph) =>
        PositionX(graph, Util.BuildLayerMatrix(graph));

    internal static Dictionary<string, double> PositionX(Graph graph, List<List<string?>> layering)
    {
        var conflicts = FindType1Conflicts(graph, layering);
        var type2 = FindType2Conflicts(graph, layering);
        foreach (var (k, v) in type2)
        {
            conflicts[k] = v;
        }

        var xss = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (var vert in new[] { "u", "d" })
        {
            // "u" aligns against predecessors top-down; "d" against successors bottom-up.
            Func<string, OrderedMap<int>.KeyEnumerable> neighborFn =
                vert == "u" ? graph.PredecessorsOf : graph.SuccessorsOf;
            var vertLayering = vert == "u" ? layering : Enumerable.Reverse(layering).ToList();

            foreach (var horiz in new[] { "l", "r" })
            {
                // "r" biases right: reverse each layer and negate the resulting coordinates.
                var biasRight = horiz == "r";
                var adjustedLayering = biasRight
                    ? vertLayering.Select(inner => Enumerable.Reverse(inner).ToList()).ToList()
                    : vertLayering;

                var align = VerticalAlignment(adjustedLayering, conflicts, neighborFn);
                var xs = HorizontalCompaction(graph, adjustedLayering, align.Root, align.Align, biasRight);
                if (biasRight)
                {
                    // xs is freshly returned by HorizontalCompaction and not aliased; negate in place.
                    foreach (var key in xs.Keys.ToList())
                    {
                        xs[key] = -xs[key];
                    }
                }

                xss[vert + horiz] = xs;
            }
        }

        var smallestWidth = FindSmallestWidthAlignment(graph, xss);
        AlignCoordinates(xss, smallestWidth);
        return Balance(xss);
    }

    internal static Func<Graph, string, string, double> Sep(double nodeSep, double edgeSep, bool reverseSep) =>
        (graph, v, w) => Separation(graph.NodeLabel(v), graph.NodeLabel(w), nodeSep, edgeSep, reverseSep);

    // The room two neighbours in a layer need between their centres.
    internal static double Separation(NodeLabel vLabel, NodeLabel wLabel, double nodeSep, double edgeSep, bool reverseSep)
    {
        double sum = 0;
        double? delta = null;

        sum += vLabel.Width / 2;
        if (vLabel.Labelpos != null)
        {
            switch (vLabel.Labelpos)
            {
                case LabelPosition.Left:
                    delta = -vLabel.Width / 2;
                    break;
                case LabelPosition.Right:
                    delta = vLabel.Width / 2;
                    break;
            }
        }

        if (delta is { } d1 && d1 != 0)
        {
            sum += reverseSep ? d1 : -d1;
        }

        delta = null;

        sum += (vLabel.Dummy != null ? edgeSep : nodeSep) / 2;
        sum += (wLabel.Dummy != null ? edgeSep : nodeSep) / 2;

        sum += wLabel.Width / 2;
        if (wLabel.Labelpos != null)
        {
            switch (wLabel.Labelpos)
            {
                case LabelPosition.Left:
                    delta = wLabel.Width / 2;
                    break;
                case LabelPosition.Right:
                    delta = -wLabel.Width / 2;
                    break;
            }
        }

        if (delta is { } d2 && d2 != 0)
        {
            sum += reverseSep ? d2 : -d2;
        }

        return sum;
    }

    internal static double Width(Graph graph, string v) => graph.NodeLabel(v).Width;
}
