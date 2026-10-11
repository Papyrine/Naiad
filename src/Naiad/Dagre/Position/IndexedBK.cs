/// <summary>
/// The x coordinates of <see cref="BK.PositionX(Graph)"/>, worked out over node numbers and arrays.
/// </summary>
/// <remarks>
/// <see cref="BK"/> is the port of dagre's module and reads as dagre does: every node is a name, and the
/// roots, alignments, positions and coordinates of each of its four alignments are dictionaries keyed by
/// those names. On a large graph nearly all of positioning's time went on hashing names to look things
/// up in them. Here each node in the layering is numbered once, layer by layer, and everything after
/// that is an array indexed by the number.
///
/// The steps, their order and their arithmetic are those of <see cref="BK"/> - the same neighbours taken
/// in the same order, the same blocks, the same sums - so the coordinates come out identical, and the
/// tests hold the two to that. Finding conflicts is left to <see cref="BK"/> itself: it is done once, not
/// four times, and is not where the time goes.
/// </remarks>
static class IndexedBK
{
    /// <summary>
    /// Sets the x coordinate of every node in <paramref name="layering"/>. Returns false, having set
    /// nothing, for a layering this cannot number: one with a gap in a layer, or with an edge to a node
    /// that is not in it. Neither arises in a layout, and <see cref="BK.PositionX(Graph)"/> remains for them.
    /// </summary>
    internal static bool TryPositionX(Graph graph, List<List<string>> layering)
    {
        if (Nodes.TryNumber(graph, layering) is not { } nodes)
        {
            return false;
        }

        var count = nodes.Names.Length;
        if (count == 0)
        {
            return true;
        }

        var conflicts = BK.FindType1Conflicts(graph, layering);
        foreach (var (k, v) in BK.FindType2Conflicts(graph, layering))
        {
            conflicts[k] = v;
        }

        var graphLabel = graph.Label;
        var nodeSeparation = graphLabel.NodeSeparation!.Value;
        var edgeSeparation = graphLabel.EdgeSeparation!.Value;

        // The four alignments, in the order BK names them: ul, ur, dl, dr.
        var scratch = new Scratch(count);
        var xss = new double[4][];
        var alignment = 0;
        foreach (var down in new[] { false, true })
        {
            foreach (var biasRight in new[] { false, true })
            {
                AlignVertically(nodes, conflicts, down, biasRight, scratch);
                xss[alignment++] = Compact(nodes, nodeSeparation, edgeSeparation, down, biasRight, scratch);
            }
        }

        AlignCoordinates(xss, SmallestWidth(nodes, xss));

        // Balance: the mean of the two middle candidates for each node.
        Span<double> candidates = stackalloc double[4];
        for (var i = 0; i < count; i++)
        {
            for (var a = 0; a < 4; a++)
            {
                candidates[a] = xss[a][i];
            }

            candidates.Sort();
            nodes.Labels[i].X = (candidates[1] + candidates[2]) / 2;
        }

        return true;
    }

    /// <summary>
    /// The nodes of a layering, numbered layer by layer and left to right, with what positioning reads of
    /// each: its label, its place in its layer counted from either end, and its predecessors and
    /// successors as numbers, in the order the graph holds them.
    /// </summary>
    sealed class Nodes
    {
        public required string[] Names;
        public required NodeLabel[] Labels;

        // LayerStart[l] is the number of the first node of layer l; one more entry closes the last layer.
        public required int[] LayerStart;
        public required int[] Position;
        public required int[] PositionFromRight;

        // A node's predecessors are Predecessors[PredecessorStart[v] .. PredecessorStart[v + 1]).
        public required int[] PredecessorStart;
        public required List<int> Predecessors;
        public required int[] SuccessorStart;
        public required List<int> Successors;

        public static Nodes? TryNumber(Graph graph, List<List<string>> layering)
        {
            var count = 0;
            foreach (var layer in layering)
            {
                count += layer.Count;
            }

            var names = new string[count];
            var labels = new NodeLabel[count];
            var layerStart = new int[layering.Count + 1];
            var position = new int[count];
            var positionFromRight = new int[count];
            var numbers = new Dictionary<string, int>(count, StringComparer.Ordinal);

            var next = 0;
            for (var l = 0; l < layering.Count; l++)
            {
                var layer = layering[l];
                layerStart[l] = next;
                for (var i = 0; i < layer.Count; i++)
                {
                    var name = layer[i];
                    if (name is null ||
                        !numbers.TryAdd(name, next))
                    {
                        return null;
                    }

                    names[next] = name;
                    labels[next] = graph.NodeLabel(name);
                    position[next] = i;
                    positionFromRight[next] = layer.Count - 1 - i;
                    next++;
                }
            }

            layerStart[layering.Count] = next;

            var predecessorStart = new int[count + 1];
            var successorStart = new int[count + 1];
            var predecessors = new List<int>(count);
            var successors = new List<int>(count);
            for (var v = 0; v < count; v++)
            {
                predecessorStart[v] = predecessors.Count;
                foreach (var u in graph.PredecessorsOf(names[v]))
                {
                    if (!numbers.TryGetValue(u, out var number))
                    {
                        return null;
                    }

                    predecessors.Add(number);
                }

                successorStart[v] = successors.Count;
                foreach (var w in graph.SuccessorsOf(names[v]))
                {
                    if (!numbers.TryGetValue(w, out var number))
                    {
                        return null;
                    }

                    successors.Add(number);
                }
            }

            predecessorStart[count] = predecessors.Count;
            successorStart[count] = successors.Count;

            return new()
            {
                Names = names,
                Labels = labels,
                LayerStart = layerStart,
                Position = position,
                PositionFromRight = positionFromRight,
                PredecessorStart = predecessorStart,
                Predecessors = predecessors,
                SuccessorStart = successorStart,
                Successors = successors
            };
        }
    }

    /// <summary>
    /// Working storage for one alignment, sized once for the graph and used again for each of the four.
    /// A block is a root and the nodes aligned under it; there is at most one per node, and at most one
    /// edge per node between them, since a node adds an edge only to the block on its left.
    /// </summary>
    sealed class Scratch(int count)
    {
        public readonly int[] Root = new int[count];
        public readonly int[] Align = new int[count];
        public readonly List<int> Neighbors = [];

        // The block a root stands for, or -1 for a node that is not a root.
        public readonly int[] BlockOf = new int[count];
        public readonly int[] BlockRoot = new int[count];
        public readonly double[] BlockX = new double[count];
        public readonly bool[] Visited = new bool[count];

        // Each block's edges in and out are chains through the edge arrays, kept in the order added.
        public readonly int[] InHead = new int[count];
        public readonly int[] InTail = new int[count];
        public readonly int[] OutHead = new int[count];
        public readonly int[] OutTail = new int[count];
        public readonly int[] EdgeFrom = new int[count];
        public readonly int[] EdgeTo = new int[count];
        public readonly int[] NextIn = new int[count];
        public readonly int[] NextOut = new int[count];
        public readonly double[] EdgeWeight = new double[count];

        // A block goes on the stack once to start with and once more when first visited, and again for
        // each edge that leads to it.
        public readonly int[] Stack = new int[count * 3 + 1];
    }

    /// <summary>
    /// <see cref="BK.VerticalAlignment"/>: tries to align each node with a median neighbour in the layer
    /// before it, leaving the result in the scratch roots and alignments.
    /// </summary>
    static void AlignVertically(
        Nodes nodes,
        Dictionary<string, Dictionary<string, bool>> conflicts,
        bool down,
        bool biasRight,
        Scratch scratch)
    {
        var root = scratch.Root;
        var align = scratch.Align;
        for (var i = 0; i < root.Length; i++)
        {
            root[i] = i;
            align[i] = i;
        }

        // "Up" aligns against predecessors going down the layers, "down" against successors going up
        // them; biasing right walks each layer from its far end and counts positions from there.
        var position = biasRight ? nodes.PositionFromRight : nodes.Position;
        var neighborStart = down ? nodes.SuccessorStart : nodes.PredecessorStart;
        var neighbors = down ? nodes.Successors : nodes.Predecessors;
        var hasConflicts = conflicts.Count > 0;
        var ws = scratch.Neighbors;
        var layers = nodes.LayerStart.Length - 1;

        for (var step = 0; step < layers; step++)
        {
            var layer = down ? layers - 1 - step : step;
            var start = nodes.LayerStart[layer];
            var end = nodes.LayerStart[layer + 1];
            var prevIdx = -1;

            for (var k = 0; k < end - start; k++)
            {
                var v = biasRight ? end - 1 - k : start + k;

                ws.Clear();
                for (var j = neighborStart[v]; j < neighborStart[v + 1]; j++)
                {
                    ws.Add(neighbors[j]);
                }

                if (ws.Count == 0)
                {
                    continue;
                }

                // Stable sort by position, as BK does it.
                for (var si = 1; si < ws.Count; ++si)
                {
                    var key = ws[si];
                    var keyPos = position[key];
                    var sj = si - 1;
                    while (sj >= 0 && position[ws[sj]] > keyPos)
                    {
                        ws[sj + 1] = ws[sj];
                        --sj;
                    }

                    ws[sj + 1] = key;
                }

                var mp = (ws.Count - 1) / 2.0;
                for (int i = (int) Math.Floor(mp), il = (int) Math.Ceiling(mp); i <= il; ++i)
                {
                    var w = ws[i];
                    var posW = position[w];
                    if (align[v] != v ||
                        prevIdx >= posW)
                    {
                        continue;
                    }

                    if (hasConflicts &&
                        BK.HasConflict(conflicts, nodes.Names[v], nodes.Names[w]))
                    {
                        continue;
                    }

                    var rootW = root[w];
                    align[w] = v;
                    align[v] = root[v] = rootW;
                    prevIdx = posW;
                }
            }
        }
    }

    /// <summary>
    /// <see cref="BK.HorizontalCompaction"/> for the alignment left in the scratch: builds the blocks and
    /// the separations between them, places them as far left as they will go and then takes up the
    /// slack, and returns each node's coordinate - negated when biased right, as the caller of the
    /// original negates it.
    /// </summary>
    static double[] Compact(
        Nodes nodes,
        double nodeSeparation,
        double edgeSeparation,
        bool down,
        bool biasRight,
        Scratch scratch)
    {
        var root = scratch.Root;
        var blockOf = scratch.BlockOf;
        var blockRoot = scratch.BlockRoot;
        var inHead = scratch.InHead;
        var inTail = scratch.InTail;
        var outHead = scratch.OutHead;
        var outTail = scratch.OutTail;
        var edgeFrom = scratch.EdgeFrom;
        var edgeTo = scratch.EdgeTo;
        var nextIn = scratch.NextIn;
        var nextOut = scratch.NextOut;
        var edgeWeight = scratch.EdgeWeight;

        Array.Fill(blockOf, -1);
        var blocks = 0;
        var edges = 0;

        int Block(int rootNode)
        {
            var block = blockOf[rootNode];
            if (block >= 0)
            {
                return block;
            }

            block = blocks++;
            blockOf[rootNode] = block;
            blockRoot[block] = rootNode;
            inHead[block] = inTail[block] = outHead[block] = outTail[block] = -1;
            return block;
        }

        var layers = nodes.LayerStart.Length - 1;
        for (var step = 0; step < layers; step++)
        {
            var layer = down ? layers - 1 - step : step;
            var start = nodes.LayerStart[layer];
            var end = nodes.LayerStart[layer + 1];
            var u = -1;

            for (var k = 0; k < end - start; k++)
            {
                var v = biasRight ? end - 1 - k : start + k;
                var vBlock = Block(root[v]);
                if (u >= 0)
                {
                    var uBlock = Block(root[u]);
                    var separation = BK.Separation(nodes.Labels[v], nodes.Labels[u], nodeSeparation, edgeSeparation, biasRight);

                    // The edge from the block on the left, if a layer above has already made one. A block
                    // usually keeps the same neighbour from layer to layer, so the last edge added is
                    // looked at first.
                    var edge = inTail[vBlock];
                    if (edge >= 0 && edgeFrom[edge] != uBlock)
                    {
                        edge = inHead[vBlock];
                        while (edge >= 0 && edgeFrom[edge] != uBlock)
                        {
                            edge = nextIn[edge];
                        }
                    }

                    if (edge >= 0)
                    {
                        edgeWeight[edge] = Math.Max(separation, edgeWeight[edge]);
                    }
                    else
                    {
                        edge = edges++;
                        edgeFrom[edge] = uBlock;
                        edgeTo[edge] = vBlock;
                        edgeWeight[edge] = Math.Max(separation, 0);
                        nextIn[edge] = -1;
                        nextOut[edge] = -1;

                        if (inTail[vBlock] < 0)
                        {
                            inHead[vBlock] = edge;
                        }
                        else
                        {
                            nextIn[inTail[vBlock]] = edge;
                        }

                        inTail[vBlock] = edge;

                        if (outTail[uBlock] < 0)
                        {
                            outHead[uBlock] = edge;
                        }
                        else
                        {
                            nextOut[outTail[uBlock]] = edge;
                        }

                        outTail[uBlock] = edge;
                    }
                }

                u = v;
            }
        }

        var blockX = scratch.BlockX;
        Array.Clear(blockX, 0, blocks);
        var borderType = biasRight ? BorderKind.Left : BorderKind.Right;
        var visited = scratch.Visited;
        var stack = scratch.Stack;

        // Visits every block after the blocks it depends on: those before it on the first pass, those
        // after it on the second.
        void Iterate(bool forward)
        {
            var top = 0;
            for (var i = 0; i < blocks; i++)
            {
                stack[top++] = i;
            }

            Array.Clear(visited, 0, blocks);

            while (top > 0)
            {
                var block = stack[--top];
                if (visited[block])
                {
                    if (forward)
                    {
                        // Smallest coordinate: the furthest any block before it pushes it.
                        double acc = 0;
                        for (var edge = inHead[block]; edge >= 0; edge = nextIn[edge])
                        {
                            acc = Math.Max(acc, blockX[edgeFrom[edge]] + edgeWeight[edge]);
                        }

                        blockX[block] = acc;
                    }
                    else
                    {
                        // Greatest coordinate: as far as the blocks after it leave room for. With none
                        // after it the minimum stays at infinity and the block stays where it is.
                        var min = double.PositiveInfinity;
                        for (var edge = outHead[block]; edge >= 0; edge = nextOut[edge])
                        {
                            min = Math.Min(min, blockX[edgeTo[edge]] - edgeWeight[edge]);
                        }

                        if (!double.IsPositiveInfinity(min) &&
                            nodes.Labels[blockRoot[block]].BorderType != borderType)
                        {
                            blockX[block] = Math.Max(blockX[block], min);
                        }
                    }

                    continue;
                }

                visited[block] = true;

                // Put the block back, to be placed once everything it depends on has been.
                stack[top++] = block;
                if (forward)
                {
                    for (var edge = inHead[block]; edge >= 0; edge = nextIn[edge])
                    {
                        stack[top++] = edgeFrom[edge];
                    }
                }
                else
                {
                    for (var edge = outHead[block]; edge >= 0; edge = nextOut[edge])
                    {
                        stack[top++] = edgeTo[edge];
                    }
                }
            }
        }

        Iterate(forward: true);
        Iterate(forward: false);

        var xs = new double[root.Length];
        for (var v = 0; v < xs.Length; v++)
        {
            var x = blockX[blockOf[root[v]]];
            xs[v] = biasRight ? -x : x;
        }

        return xs;
    }

    /// <summary><see cref="BK.FindSmallestWidthAlignment"/>: the first of the alignments to be narrowest.</summary>
    static double[] SmallestWidth(Nodes nodes, double[][] xss)
    {
        var currentMin = double.PositiveInfinity;
        double[]? current = null;

        foreach (var xs in xss)
        {
            var max = double.NegativeInfinity;
            var min = double.PositiveInfinity;
            for (var v = 0; v < xs.Length; v++)
            {
                var halfWidth = nodes.Labels[v].Width / 2;
                max = Math.Max(xs[v] + halfWidth, max);
                min = Math.Min(xs[v] - halfWidth, min);
            }

            var width = max - min;
            if (width < currentMin)
            {
                currentMin = width;
                current = xs;
            }
        }

        return current!;
    }

    /// <summary>
    /// <see cref="BK.AlignCoordinates"/>: slides each alignment so that the left-biased ones start where
    /// the narrowest starts and the right-biased ones end where it ends.
    /// </summary>
    static void AlignCoordinates(double[][] xss, double[] alignTo)
    {
        var (alignToMin, alignToMax) = Range(alignTo);

        for (var alignment = 0; alignment < xss.Length; alignment++)
        {
            var xs = xss[alignment];
            if (ReferenceEquals(xs, alignTo))
            {
                continue;
            }

            // ul and dl are the left-biased alignments.
            var (min, max) = Range(xs);
            var delta = alignment % 2 == 0
                ? alignToMin - min
                : alignToMax - max;

            if (delta != 0)
            {
                for (var v = 0; v < xs.Length; v++)
                {
                    xs[v] += delta;
                }
            }
        }
    }

    static (double Min, double Max) Range(double[] values)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var value in values)
        {
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        return (min, max);
    }
}
