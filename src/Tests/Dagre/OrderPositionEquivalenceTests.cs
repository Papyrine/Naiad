// Ordering and positioning each have a shorter route through them than the one ported from dagre. These
// hold the shorter route to the longer one's answer, over generated graphs wider and denser than the
// hand-written cases elsewhere.
public class OrderPositionEquivalenceTests
{
    // A layered graph with no subgraphs: every edge joins neighbouring ranks, some pairs of nodes are
    // joined more than once, weights vary, and some nodes have no edges at all.
    static Graph LayeredGraph(int seed, bool compound)
    {
        var random = new Random(seed);
        var graph = new Graph(directed: true, multigraph: true, compound: compound);
        graph.SetGraph(new() { NodeSeparation = 50, EdgeSeparation = 20, RankSeparation = 50 });

        var ranks = random.Next(2, 9);
        var layers = new List<List<string>>();
        for (var rank = 0; rank < ranks; rank++)
        {
            var layer = new List<string>();
            var width = random.Next(1, 14);
            for (var i = 0; i < width; i++)
            {
                var name = $"n{rank}_{i}";
                graph.SetNode(name, new() { Rank = rank, Width = 10 + random.Next(60), Height = 20 });
                layer.Add(name);
            }

            layers.Add(layer);
        }

        var edge = 0;
        for (var rank = 0; rank < ranks - 1; rank++)
        {
            var count = random.Next(0, layers[rank].Count * 3 + 1);
            for (var i = 0; i < count; i++)
            {
                var from = layers[rank][random.Next(layers[rank].Count)];
                var to = layers[rank + 1][random.Next(layers[rank + 1].Count)];
                graph.SetEdge(from, to, new() { Weight = random.Next(1, 4) }, "e" + edge++);
            }
        }

        return graph;
    }

    static string Orders(Graph graph) =>
        string.Join(",", graph.NodeEntries().Select(_ => $"{_.Key}={_.Value.Order}"));

    // The sweep that reads a graph with no subgraphs directly gives every node the order the layer graphs
    // give it.
    [Test]
    public async Task FlatSweep_OrdersAsTheLayerGraphsDo()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var compound = seed % 2 == 0;
            var direct = LayeredGraph(seed, compound);
            var throughLayerGraphs = LayeredGraph(seed, compound);

            Order.Run(direct, flatWhenPossible: true);
            Order.Run(throughLayerGraphs, flatWhenPossible: false);

            await Assert.That(Orders(direct)).IsEqualTo(Orders(throughLayerGraphs)).Because($"seed {seed}");
        }
    }

    // A graph with a subgraph still goes through the layer graphs, whichever way it is asked.
    [Test]
    public async Task GraphWithASubgraph_IsOrderedTheSameEitherWay()
    {
        static Graph Build()
        {
            var graph = new Graph(compound: true);
            graph.SetDefaultEdgeLabel(new EdgeLabel { Weight = 1 });
            foreach (var (name, rank) in new[] { ("a", 0), ("b", 0), ("c", 1), ("d", 1), ("e", 2) })
            {
                graph.SetNode(name, new() { Rank = rank });
            }

            graph.SetNode("group", new());
            graph.SetParent("c", "group");
            graph.SetParent("d", "group");
            graph.SetEdge("a", "d");
            graph.SetEdge("b", "c");
            graph.SetEdge("c", "e");
            graph.SetEdge("d", "e");
            return graph;
        }

        var direct = Build();
        var throughLayerGraphs = Build();
        Order.Run(direct, flatWhenPossible: true);
        Order.Run(throughLayerGraphs, flatWhenPossible: false);

        await Assert.That(Orders(direct)).IsEqualTo(Orders(throughLayerGraphs));
    }

    // dagre's own loop for type-2 conflicts, scanning to the end of the layer at every node. The port
    // skips a scan it has already made; the conflicts found must be the same.
    static Dictionary<string, Dictionary<string, bool>> Type2ConflictsByRepeatedScan(Graph graph, List<List<string>> layering)
    {
        var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

        void Scan(List<string> south, int southPos, int southEnd, int prevNorthBorder, int nextNorthBorder)
        {
            for (var i = southPos; i < southEnd; i++)
            {
                var v = south[i];
                if (graph.NodeLabel(v).Dummy == null)
                {
                    continue;
                }

                foreach (var u in graph.Predecessors(v)!)
                {
                    var uNode = graph.NodeLabel(u);
                    if (uNode.Dummy != null &&
                        (uNode.Order!.Value < prevNorthBorder || uNode.Order!.Value > nextNorthBorder))
                    {
                        BK.AddConflict(conflicts, u, v);
                    }
                }
            }
        }

        for (var layer = 1; layer < layering.Count; layer++)
        {
            var north = layering[layer - 1];
            var south = layering[layer];
            var prevNorthPos = -1;
            var nextNorthPos = -1;
            var southPos = 0;

            for (var southLookahead = 0; southLookahead < south.Count; southLookahead++)
            {
                var v = south[southLookahead];
                if (graph.NodeLabel(v).Dummy == DummyKind.Border)
                {
                    var predecessors = graph.Predecessors(v)!;
                    if (predecessors.Count != 0)
                    {
                        nextNorthPos = graph.NodeLabel(predecessors[0]).Order!.Value;
                        Scan(south, southPos, southLookahead, prevNorthPos, nextNorthPos);
                        southPos = southLookahead;
                        prevNorthPos = nextNorthPos;
                    }
                }

                Scan(south, southPos, south.Count, nextNorthPos, north.Count);
            }
        }

        return conflicts;
    }

    static string Describe(Dictionary<string, Dictionary<string, bool>> conflicts) =>
        string.Join(
            ";",
            conflicts
                .OrderBy(_ => _.Key, StringComparer.Ordinal)
                .Select(_ => $"{_.Key}:{string.Join(",", _.Value.Keys.OrderBy(key => key, StringComparer.Ordinal))}"));

    [Test]
    public async Task Type2Conflicts_MatchScanningAtEveryNode()
    {
        var found = 0;
        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            var graph = LayeredGraph(seed, compound: false);
            Order.Run(graph);

            // Scatter dummies and borders, which is what a type-2 conflict is made of.
            foreach (var (_, label) in graph.NodeEntries())
            {
                label.Dummy = random.Next(4) switch
                {
                    0 => DummyKind.Edge,
                    1 => DummyKind.Border,
                    _ => null
                };
            }

            var layering = Util.BuildLayerMatrix(graph);
            var expected = Type2ConflictsByRepeatedScan(graph, layering);
            var actual = BK.FindType2Conflicts(graph, layering);

            await Assert.That(Describe(actual)).IsEqualTo(Describe(expected)).Because($"seed {seed}");
            found += expected.Count;
        }

        // The generated graphs do contain conflicts, so the comparison above is not of empty with empty.
        await Assert.That(found).IsGreaterThan(100);
    }

    // The same coordinates from compaction over its own block lists as over the block graph: the graph
    // is the reference here, walked the way dagre walks it.
    [Test]
    public async Task HorizontalCompaction_MatchesTheBlockGraph()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var graph = LayeredGraph(seed, compound: false);
            Order.Run(graph);
            var layering = Util.BuildLayerMatrix(graph);
            var conflicts = BK.FindType1Conflicts(graph, layering);

            foreach (var reverseSep in new[] { false, true })
            {
                var alignment = BK.VerticalAlignment(layering, conflicts, graph.PredecessorsOf);
                var actual = BK.HorizontalCompaction(graph, layering, alignment.Root, alignment.Align, reverseSep);
                var expected = CompactionOverBlockGraph(graph, layering, alignment.Root, alignment.Align, reverseSep);

                await Assert.That(actual.Count).IsEqualTo(expected.Count).Because($"seed {seed}");
                foreach (var (v, x) in expected)
                {
                    await Assert.That(actual[v]).IsEqualTo(x).Because($"seed {seed}, node {v}");
                }
            }
        }
    }

    static Dictionary<string, double> CompactionOverBlockGraph(
        Graph graph,
        List<List<string>> layering,
        Dictionary<string, string> root,
        Dictionary<string, string> align,
        bool reverseSep)
    {
        var xs = new Dictionary<string, double>(StringComparer.Ordinal);
        var blockGraph = BK.BuildBlockGraph(graph, layering, root, reverseSep);
        var borderType = reverseSep ? BorderKind.Left : BorderKind.Right;

        void Iterate(Action<string> set, Func<string, List<string>?> next)
        {
            var stack = blockGraph.Nodes();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (stack.Count > 0)
            {
                var elem = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (!visited.Add(elem))
                {
                    set(elem);
                    continue;
                }

                stack.Add(elem);
                stack.AddRange(next(elem) ?? []);
            }
        }

        Iterate(
            elem =>
            {
                double acc = 0;
                foreach (var e in blockGraph.InEdgesOf(elem))
                {
                    acc = Math.Max(acc, xs.GetValueOrDefault(e.V, 0) + (blockGraph.FindEdgeLabel(e).Weight ?? 0));
                }

                xs[elem] = acc;
            },
            blockGraph.Predecessors);

        Iterate(
            elem =>
            {
                var min = double.PositiveInfinity;
                foreach (var e in blockGraph.OutEdgesOf(elem))
                {
                    min = Math.Min(min, xs.GetValueOrDefault(e.W, 0) - (blockGraph.FindEdgeLabel(e).Weight ?? 0));
                }

                if (!double.IsPositiveInfinity(min) &&
                    graph.NodeLabel(elem).BorderType != borderType)
                {
                    xs[elem] = Math.Max(xs.GetValueOrDefault(elem, 0), min);
                }
            },
            blockGraph.Successors);

        foreach (var v in align.Keys)
        {
            if (root.TryGetValue(v, out var rootV))
            {
                xs[v] = xs.GetValueOrDefault(rootV, 0);
            }
        }

        return xs;
    }

    // With no constraints every entry comes back alone, last first, as the general resolution leaves them.
    [Test]
    public async Task ResolveConflicts_WithoutConstraints_ReturnsEntriesLastFirst()
    {
        var entries = new List<BarycenterEntry>
        {
            new() { V = "a", Barycenter = 2, Weight = 3 },
            new() { V = "b" },
            new() { V = "c", Barycenter = 1, Weight = 1 }
        };

        var resolved = ResolveConflicts.Run(entries, new Graph());

        await Assert.That(string.Join("|", resolved.Select(_ => $"{string.Join(",", _.Vs)}:{_.I}:{_.Barycenter}:{_.Weight}")))
            .IsEqualTo("c:2:1:1|b:1::|a:0:2:3");
    }

    // An entry named twice takes the general route, where the later one replaces the earlier in its place.
    [Test]
    public async Task ResolveConflicts_WithARepeatedEntry_KeepsTheLaterOne()
    {
        var entries = new List<BarycenterEntry>
        {
            new() { V = "a", Barycenter = 2, Weight = 3 },
            new() { V = "b", Barycenter = 5, Weight = 1 },
            new() { V = "a", Barycenter = 9, Weight = 4 }
        };

        var resolved = ResolveConflicts.Run(entries, new Graph());

        await Assert.That(string.Join("|", resolved.Select(_ => $"{string.Join(",", _.Vs)}:{_.I}:{_.Barycenter}:{_.Weight}")))
            .IsEqualTo("b:1:5:1|a:2:9:4");
    }
}
