using Naiad.Diagrams.Flowchart;

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
    static Dictionary<string, Dictionary<string, bool>> Type2ConflictsByRepeatedScan(Graph graph, List<List<string?>> layering)
    {
        var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

        void Scan(List<string?> south, int southPos, int southEnd, int prevNorthBorder, int nextNorthBorder)
        {
            for (var i = southPos; i < southEnd; i++)
            {
                var v = south[i];
                if (v is null)
                {
                    continue;
                }

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
                if (v is not null &&
                    graph.NodeLabel(v).Dummy == DummyKind.Border)
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
        List<List<string?>> layering,
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

        var resolved = ResolveConflicts.Run(entries, new());

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

        var resolved = ResolveConflicts.Run(entries, new());

        await Assert.That(string.Join("|", resolved.Select(_ => $"{string.Join(",", _.Vs)}:{_.I}:{_.Barycenter}:{_.Weight}")))
            .IsEqualTo("b:1:5:1|a:2:9:4");
    }

    static string Coordinates(Graph graph) =>
        string.Join(
            ",",
            graph.NodeEntries().Select(_ => string.Create(CultureInfo.InvariantCulture, $"{_.Key}=({_.Value?.X:R},{_.Value?.Y:R})")));

    // Positioning the graph as it stands gives every node the place that positioning dagre's copy of it
    // gives.
    [Test]
    public async Task PositioningInPlace_MatchesPositioningTheCopy()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var compound = seed % 2 == 0;
            var inPlace = LayeredGraph(seed, compound);
            var copied = LayeredGraph(seed, compound);
            Order.Run(inPlace);
            Order.Run(copied);

            Positioning.Run(inPlace, inPlaceWhenPossible: true);
            Positioning.Run(copied, inPlaceWhenPossible: false);

            await Assert.That(Coordinates(inPlace)).IsEqualTo(Coordinates(copied)).Because($"seed {seed}");
        }
    }

    // A subgraph as positioning meets it: a node with children, no rank of its own and no edges, carried
    // in the ranks by its border nodes. That is still positioned in place, to the same result.
    static Graph WithASubgraph(int? subgraphRank)
    {
        var graph = new Graph(directed: true, multigraph: true, compound: true);
        graph.SetGraph(new() { NodeSeparation = 50, EdgeSeparation = 20, RankSeparation = 50 });
        graph.SetDefaultEdgeLabel(new EdgeLabel { Weight = 1 });

        graph.SetNode("a", new() { Rank = 0, Order = 0, Width = 40, Height = 20 });
        graph.SetNode("b", new() { Rank = 0, Order = 1, Width = 60, Height = 20 });
        graph.SetNode("left", new() { Rank = 1, Order = 0, Width = 0, Height = 0, Dummy = DummyKind.Border, BorderType = BorderKind.Left });
        graph.SetNode("c", new() { Rank = 1, Order = 1, Width = 50, Height = 30 });
        graph.SetNode("d", new() { Rank = 1, Order = 2, Width = 30, Height = 30 });
        graph.SetNode("right", new() { Rank = 1, Order = 3, Width = 0, Height = 0, Dummy = DummyKind.Border, BorderType = BorderKind.Right });
        graph.SetNode("e", new() { Rank = 2, Order = 0, Width = 70, Height = 20 });

        graph.SetNode("group", new() { Rank = subgraphRank, Order = subgraphRank is null ? null : 4 });
        foreach (var child in new[] { "left", "c", "d", "right" })
        {
            graph.SetParent(child, "group");
        }

        graph.SetEdge("a", "c");
        graph.SetEdge("b", "d");
        graph.SetEdge("c", "e");
        graph.SetEdge("d", "e");
        return graph;
    }

    [Test]
    [Arguments(false)]
    // A subgraph node with a rank of its own would be laid out with the rest, and the copy leaves it
    // out. That takes the copy whichever way it is asked - and so agrees.
    [Arguments(true)]
    public async Task PositioningAGraphWithASubgraph_IsTheSameEitherWay(bool rankedSubgraph)
    {
        int? rank = rankedSubgraph ? 1 : null;
        var inPlace = WithASubgraph(rank);
        var copied = WithASubgraph(rank);

        Positioning.Run(inPlace, inPlaceWhenPossible: true);
        Positioning.Run(copied, inPlaceWhenPossible: false);

        await Assert.That(Coordinates(inPlace)).IsEqualTo(Coordinates(copied));
        await Assert.That(inPlace.NodeLabel("c").X).IsNotNull();
    }

    [Test]
    public async Task MaxRank_CountsANodeWithoutARankAsAlmostZero()
    {
        var graph = new Graph();
        await Assert.That(Util.MaxRank(graph)).IsEqualTo(double.NegativeInfinity);

        graph.SetNode("unranked", new());
        await Assert.That(Util.MaxRank(graph)).IsEqualTo(double.Epsilon);

        graph.SetNode("low", new() { Rank = 0 });
        await Assert.That(Util.MaxRank(graph)).IsEqualTo(double.Epsilon);

        graph.SetNode("high", new() { Rank = 3 });
        graph.SetNode("mid", new() { Rank = 2 });
        await Assert.That(Util.MaxRank(graph)).IsEqualTo(3);
    }

    // A generated graph dressed as positioning meets one: ordered, with dummies, borders of either side
    // and labels set to one side scattered through it, so that conflicts of both types, border blocks
    // and every branch of the separation between neighbours all occur.
    static Graph DressedGraph(int seed)
    {
        var random = new Random(seed * 7919 + 13);
        var graph = LayeredGraph(seed, compound: false);
        Order.Run(graph);

        foreach (var (_, label) in graph.NodeEntries())
        {
            switch (random.Next(6))
            {
                case 0:
                    label.Dummy = DummyKind.Edge;
                    break;
                case 1:
                    label.Dummy = DummyKind.Border;
                    label.BorderType = random.Next(2) == 0 ? BorderKind.Left : BorderKind.Right;
                    break;
                case 2:
                    label.Dummy = DummyKind.EdgeLabel;
                    label.Labelpos = random.Next(3) switch
                    {
                        0 => LabelPosition.Left,
                        1 => LabelPosition.Right,
                        _ => LabelPosition.Center
                    };
                    break;
            }
        }

        return graph;
    }

    // Positioning over node numbers and arrays puts every node exactly where positioning over names and
    // dictionaries puts it.
    [Test]
    public async Task IndexedPositioning_MatchesTheDictionaryVersion()
    {
        var withConflicts = 0;
        for (var seed = 0; seed < 400; seed++)
        {
            var indexed = DressedGraph(seed);
            var reference = DressedGraph(seed);
            var layering = Util.BuildLayerMatrix(indexed);

            var expected = BK.PositionX(reference);
            await Assert.That(IndexedBK.TryPositionX(indexed, layering)).IsTrue();

            await Assert.That(expected.Count).IsEqualTo(indexed.NodeCount).Because($"seed {seed}");
            foreach (var (v, x) in expected)
            {
                await Assert.That(indexed.NodeLabel(v).X).IsEqualTo(x).Because($"seed {seed}, node {v}");
            }

            if (BK.FindType1Conflicts(reference, layering).Count > 0 &&
                BK.FindType2Conflicts(reference, layering).Count > 0)
            {
                withConflicts++;
            }
        }

        // Enough of the graphs carry conflicts of both types for the merge of the two to be exercised.
        await Assert.That(withConflicts).IsGreaterThan(50);
    }

    [Test]
    public async Task PositioningRun_IsTheSameIndexedOrNot()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var indexed = DressedGraph(seed);
            var reference = DressedGraph(seed);

            Positioning.Run(indexed, inPlaceWhenPossible: true, indexedWhenPossible: true);
            Positioning.Run(reference, inPlaceWhenPossible: true, indexedWhenPossible: false);

            await Assert.That(Coordinates(indexed)).IsEqualTo(Coordinates(reference)).Because($"seed {seed}");
        }
    }

    [Test]
    public async Task IndexedPositioning_OfAnEmptyGraph_DoesNothing() =>
        await Assert.That(IndexedBK.TryPositionX(new(), [])).IsTrue();

    // A layering it cannot number is declined, with nothing positioned, and left to the dictionary version.
    [Test]
    public async Task IndexedPositioning_DeclinesALayeringWithAGap()
    {
        var graph = DressedGraph(3);
        var layering = Util.BuildLayerMatrix(graph);
        layering[0].Add(null!);

        await Assert.That(IndexedBK.TryPositionX(graph, layering)).IsFalse();
        await Assert.That(graph.NodeLabels().All(_ => _.X is null)).IsTrue();
    }

    [Test]
    public async Task IndexedPositioning_DeclinesAnEdgeToANodeOutsideTheLayering()
    {
        var graph = new Graph();
        graph.SetGraph(new() { NodeSeparation = 50, EdgeSeparation = 20, RankSeparation = 50 });
        graph.SetDefaultEdgeLabel(new EdgeLabel());
        graph.SetNode("a", new() { Rank = 0, Order = 0, Width = 10 });
        graph.SetNode("b", new() { Rank = 1, Order = 0, Width = 10 });
        graph.SetNode("unranked", new() { Width = 10 });
        graph.SetEdge("a", "b");
        graph.SetEdge("unranked", "b");

        await Assert.That(IndexedBK.TryPositionX(graph, Util.BuildLayerMatrix(graph))).IsFalse();
        await Assert.That(graph.NodeLabel("a").X).IsNull();
    }

    // A node with far more neighbours than the few the merge searches as a list, many of them joined to
    // it several times over, so the indexed side of merging a node's edges is taken.
    [Test]
    public async Task FlatOrder_WithAHubOfManyNeighbours_OrdersAsTheLayerGraphsDo()
    {
        static Graph Build(int seed)
        {
            var random = new Random(seed);
            var graph = new Graph(directed: true, multigraph: true, compound: true);
            graph.SetNode("hub", new() { Rank = 1 });
            graph.SetNode("other", new() { Rank = 1 });
            var edge = 0;
            for (var i = 0; i < 40; i++)
            {
                graph.SetNode($"top{i}", new() { Rank = 0 });
                graph.SetNode($"bottom{i}", new() { Rank = 2 });
            }

            for (var i = 0; i < 120; i++)
            {
                graph.SetEdge($"top{random.Next(40)}", random.Next(4) == 0 ? "other" : "hub", new() { Weight = random.Next(1, 5) }, "e" + edge++);
                graph.SetEdge(random.Next(4) == 0 ? "other" : "hub", $"bottom{random.Next(40)}", new() { Weight = random.Next(1, 5) }, "e" + edge++);
            }

            return graph;
        }

        for (var seed = 0; seed < 20; seed++)
        {
            var direct = Build(seed);
            var throughLayerGraphs = Build(seed);
            Order.Run(direct, flatWhenPossible: true);
            Order.Run(throughLayerGraphs, flatWhenPossible: false);

            await Assert.That(Orders(direct)).IsEqualTo(Orders(throughLayerGraphs)).Because($"seed {seed}");
        }
    }

    // What the direct ordering does not take on is left alone by it and ordered by the layer graphs: an
    // edge that skips a rank, and an edge with no weight.
    [Test]
    [Arguments(true, 1)]
    [Arguments(false, 0)]
    public async Task FlatOrder_DeclinesWhatItCannotOrder(bool longEdge, int weight)
    {
        Graph Build()
        {
            var graph = new Graph(directed: true, multigraph: true, compound: true);
            foreach (var (name, rank) in new[] { ("a", 0), ("b", 0), ("c", 1), ("d", 1), ("e", 2), ("f", 2) })
            {
                graph.SetNode(name, new() { Rank = rank });
            }

            graph.SetEdge("a", "d", new() { Weight = 1 });
            graph.SetEdge("b", "c", new() { Weight = 1 });
            graph.SetEdge("c", "f", new() { Weight = weight });
            graph.SetEdge("d", "e", new() { Weight = 1 });
            if (longEdge)
            {
                graph.SetNode("g", new() { Rank = 3 });
                graph.SetEdge("e", "g", new() { Weight = 1 });
                graph.SetEdge("c", "g", new() { Weight = 1 });
            }

            return graph;
        }

        var untouched = Build();
        await Assert.That(FlatOrder.TryRun(untouched, (int) Util.MaxRank(untouched))).IsFalse();
        await Assert.That(untouched.NodeLabels().All(_ => _.Order is null)).IsTrue();

        var direct = Build();
        var throughLayerGraphs = Build();
        if (longEdge)
        {
            // The crossing count only knows neighbouring ranks, so the layer graphs reject this too.
            await Assert.That(() => Order.Run(direct, flatWhenPossible: true)).Throws<KeyNotFoundException>();
            await Assert.That(() => Order.Run(throughLayerGraphs, flatWhenPossible: false)).Throws<KeyNotFoundException>();
            return;
        }

        Order.Run(direct, flatWhenPossible: true);
        Order.Run(throughLayerGraphs, flatWhenPossible: false);
        await Assert.That(Orders(direct)).IsEqualTo(Orders(throughLayerGraphs));
    }

    // An empty graph has nothing to order, and ordering it returns at once - it used to count layers
    // down from a rank of negative infinity, which never finished.
    [Test]
    public async Task OrderingAnEmptyGraph_DoesNothing()
    {
        var graph = new Graph(compound: true);
        await Assert.That(FlatOrder.TryRun(graph, (int) Util.MaxRank(graph))).IsFalse();

        Order.Run(graph, flatWhenPossible: true);
        Order.Run(graph, flatWhenPossible: false);
        await Assert.That(graph.NodeCount).IsEqualTo(0);
    }

    // A flowchart with subgraphs, some nested and some empty of links, and edges that run within a
    // subgraph, between subgraphs and to a subgraph as a whole.
    static string FlowchartWithSubgraphs(int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder("flowchart TD\n");
        var groups = random.Next(2, 7);
        var open = 0;
        for (var group = 0; group < groups; group++)
        {
            if (open == 0 && random.Next(3) == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"    subgraph outer{group} [Outer {group}]\n");
                open = random.Next(1, 3);
            }

            text.Append(CultureInfo.InvariantCulture, $"    subgraph sg{group} [Group {group}]\n");
            var size = random.Next(1, 6);
            for (var i = 0; i < size; i++)
            {
                text.Append(CultureInfo.InvariantCulture, $"        S{group}_{i}[Node {group}-{i}]\n");
            }

            for (var i = 0; i < size; i++)
            {
                if (random.Next(2) == 0)
                {
                    text.Append(CultureInfo.InvariantCulture, $"        S{group}_{random.Next(size)} --> S{group}_{i}\n");
                }
            }

            text.Append("    end\n");
            if (open > 0 && --open == 0)
            {
                text.Append("    end\n");
            }
        }

        if (open > 0)
        {
            text.Append("    end\n");
        }

        var links = random.Next(groups, groups * 4);
        for (var i = 0; i < links; i++)
        {
            var from = random.Next(groups);
            var to = random.Next(groups);
            text.Append(CultureInfo.InvariantCulture, $"    S{from}_0 --> S{to}_0\n");
            if (random.Next(4) == 0 && from != to)
            {
                text.Append(CultureInfo.InvariantCulture, $"    lone{i}[Lone {i}] --> sg{to}\n");
            }
        }

        return text.ToString();
    }

    static string Describe(FlowchartModel model)
    {
        var text = new StringBuilder();
        foreach (var node in model.Nodes)
        {
            text.Append(CultureInfo.InvariantCulture, $"{node.Id}@{node.Position.X:R},{node.Position.Y:R};");
        }

        foreach (var edge in model.Edges)
        {
            text.Append(CultureInfo.InvariantCulture, $"{edge.SourceId}>{edge.TargetId}:");
            foreach (var point in edge.Points)
            {
                text.Append(CultureInfo.InvariantCulture, $"{point.X:R},{point.Y:R} ");
            }
        }

        void Subgraphs(List<Subgraph> subgraphs)
        {
            foreach (var subgraph in subgraphs)
            {
                text.Append(CultureInfo.InvariantCulture, $"[{subgraph.Id}@{subgraph.Position.X:R},{subgraph.Position.Y:R} {subgraph.Width:R}x{subgraph.Height:R}]");
                Subgraphs(subgraph.NestedSubgraphs);
            }
        }

        Subgraphs(model.Subgraphs);
        return text.ToString();
    }

    static string LayOut(string input, bool throughLayerGraphs)
    {
        var model = new FlowchartParser().Parse(input).Value;
        Order.UseLayerGraphs = throughLayerGraphs;
        try
        {
            new FlowchartRenderer().Render(model, RenderOptions.Default);
        }
        finally
        {
            Order.UseLayerGraphs = false;
        }

        return Describe(model);
    }

    // A whole layout of a diagram with subgraphs comes out the same - every node, every edge point,
    // every subgraph box - whether its ranks are ordered through light layers or through the layer
    // graphs dagre builds.
    [Test]
    public async Task LayoutWithSubgraphs_IsTheSameThroughLightLayers()
    {
        for (var seed = 0; seed < 150; seed++)
        {
            var input = FlowchartWithSubgraphs(seed);

            await Assert.That(LayOut(input, throughLayerGraphs: false))
                .IsEqualTo(LayOut(input, throughLayerGraphs: true))
                .Because($"seed {seed}:\n{input}");
        }
    }

    // And a whole layout of a diagram without them, through the ordering that needs no layers at all.
    [Test]
    public async Task LayoutWithoutSubgraphs_IsTheSameThroughTheFlatOrder()
    {
        for (var seed = 0; seed < 60; seed++)
        {
            var random = new Random(seed);
            var text = new StringBuilder("flowchart TD\n");
            var nodes = random.Next(3, 30);
            for (var i = 0; i < nodes * 2; i++)
            {
                text.Append(CultureInfo.InvariantCulture, $"    N{random.Next(nodes)} -->|l{i}| N{random.Next(nodes)}\n");
            }

            var input = text.ToString();
            await Assert.That(LayOut(input, throughLayerGraphs: false))
                .IsEqualTo(LayOut(input, throughLayerGraphs: true))
                .Because($"seed {seed}:\n{input}");
        }
    }
}
