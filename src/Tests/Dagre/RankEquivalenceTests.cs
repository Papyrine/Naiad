using Naiad.Diagrams.Flowchart;

// Ranking has a shorter route through it than the one ported from dagre: IndexedNetworkSimplex, over node
// numbers and arrays, in place of NetworkSimplex over two copies of the graph. These hold the shorter route
// to the longer one's answer - the same feasible tree, the same pivots in the same order and the same ranks -
// over generated graphs with parallel edges, cycles, a spread of minimum lengths and weights, and subgraphs
// nested inside one another.
public class RankEquivalenceTests
{
    // A connected graph with no cycles. Nodes are added in one random order, edges run along another and are
    // added in a third, some pairs of nodes are joined more than once, and now and then an edge leaves its
    // minimum length or weight unset.
    static Graph RandomDag(int seed, int maxNodes)
    {
        var random = new Random(seed);
        var graph = new Graph(multigraph: true);
        graph.SetGraph(new());

        var count = random.Next(1, maxNodes + 1);
        var position = Enumerable.Range(0, count).OrderBy(_ => random.Next()).ToArray();
        var byPosition = new int[count];
        for (var i = 0; i < count; i++)
        {
            byPosition[position[i]] = i;
        }

        foreach (var i in Enumerable.Range(0, count).OrderBy(_ => random.Next()))
        {
            graph.SetNode($"n{i}", new());
        }

        var pairs = new List<(int From, int To)>();
        for (var p = 1; p < count; p++)
        {
            pairs.Add((byPosition[random.Next(p)], byPosition[p]));
        }

        var extra = random.Next(0, count * 2 + 1);
        for (var i = 0; i < extra && count > 1; i++)
        {
            if (pairs.Count > 0 &&
                random.Next(5) == 0)
            {
                pairs.Add(pairs[random.Next(pairs.Count)]);
                continue;
            }

            var a = random.Next(count);
            var b = random.Next(count);
            if (a == b)
            {
                continue;
            }

            if (position[a] > position[b])
            {
                (a, b) = (b, a);
            }

            pairs.Add((a, b));
        }

        var edge = 0;
        foreach (var (from, to) in pairs.OrderBy(_ => random.Next()))
        {
            int? minlen = random.Next(8) == 0 ? null : random.Next(1, 4);
            double? weight = random.Next(8) == 0 ? null : random.Next(0, 5);
            graph.SetEdge($"n{from}", $"n{to}", new() { Minlen = minlen, Weight = weight }, "e" + edge++);
        }

        return graph;
    }

    // A compound graph as the layout hands it to ranking: nodes in subgraphs nested a few deep, edges between
    // any two of them, cycles among those included, turned round by Acyclic, and the whole tied together by
    // the nesting graph, which adds the border nodes, the nesting edges and the root.
    static Graph RandomNestingGraph(int seed, int maxNodes)
    {
        var random = new Random(seed);
        var graph = new Graph(directed: true, multigraph: true, compound: true);
        graph.SetGraph(new());

        var count = random.Next(1, maxNodes + 1);
        for (var i = 0; i < count; i++)
        {
            graph.SetNode($"n{i}", new() { Width = 10, Height = 10 });
        }

        var groups = random.Next(0, count / 3 + 2);
        for (var g = 0; g < groups; g++)
        {
            graph.SetNode($"g{g}", new());
            if (g > 0 &&
                random.Next(2) == 0)
            {
                graph.SetParent($"g{g}", $"g{random.Next(g)}");
            }
        }

        for (var i = 0; i < count && groups > 0; i++)
        {
            if (random.Next(3) != 0)
            {
                graph.SetParent($"n{i}", $"g{random.Next(groups)}");
            }
        }

        var edges = random.Next(0, count * 2 + 1);
        for (var i = 0; i < edges && count > 1; i++)
        {
            var from = random.Next(count);
            var to = random.Next(count);
            if (from == to)
            {
                continue;
            }

            // Minimum lengths doubled, as the layout doubles them to make room for edge labels.
            graph.SetEdge(
                $"n{from}",
                $"n{to}",
                new() { Minlen = 2 * random.Next(1, 4), Weight = random.Next(1, 4) },
                "e" + i);

            if (random.Next(6) == 0)
            {
                graph.SetEdge($"n{from}", $"n{to}", new() { Minlen = 2, Weight = 1 }, "p" + i);
            }
        }

        Acyclic.Run(graph);
        NestingGraph.Run(graph);
        return graph;
    }

    static string Ranks(Graph graph) =>
        string.Join(",", graph.NodeEntries().Select(_ => $"{_.Key}={_.Value?.Rank}"));

    // The port's ranking, step by step as NetworkSimplex.Run takes it, written down as the indexed ranking
    // writes down its own: the feasible tree's edges in order, then each pivot's leaving and entering edge,
    // every edge the way round the graph has it.
    static List<string> ReferenceTrace(Graph graph)
    {
        var simple = Util.Simplify(Util.AsNonCompoundGraph(graph));
        RankUtil.LongestPath(simple);
        var tree = FeasibleTree.Run(simple);

        string Oriented(EdgeKey e)
        {
            if (simple.HasEdge(e.V, e.W))
            {
                return $"{e.V}>{e.W}";
            }

            return $"{e.W}>{e.V}";
        }

        var trace = tree.Edges().Select(_ => "tree " + Oriented(_)).ToList();
        NetworkSimplex.InitLowLimValues(tree);
        NetworkSimplex.InitCutValues(tree, simple);
        while (NetworkSimplex.LeaveEdge(tree) is { } e)
        {
            var f = NetworkSimplex.EnterEdge(tree, simple, e);
            trace.Add($"pivot {Oriented(e)} {f.V}>{f.W}");
            NetworkSimplex.ExchangeEdges(tree, simple, e, f);
        }

        return trace;
    }

    // Ranks the graph three ways from the same seed - NetworkSimplex.Run over the copy, the port driven step
    // by step, and the indexed ranking - and holds the last to the other two. Returns the pivots made.
    static async Task<int> AssertSameRanking(Func<Graph> build, string because)
    {
        var reference = build();
        NetworkSimplex.Run(Util.AsNonCompoundGraph(reference));

        var stepped = build();
        var expected = ReferenceTrace(stepped);

        var indexed = build();
        var actual = new List<string>();
        await Assert.That(IndexedNetworkSimplex.TryRun(indexed, actual)).IsTrue().Because(because);

        await Assert.That(string.Join("\n", actual)).IsEqualTo(string.Join("\n", expected)).Because(because);
        await Assert.That(Ranks(indexed)).IsEqualTo(Ranks(reference)).Because(because);
        await Assert.That(Ranks(stepped)).IsEqualTo(Ranks(reference)).Because(because);
        return actual.Count(_ => _.StartsWith("pivot ", StringComparison.Ordinal));
    }

    [Test]
    public async Task Dags_RankAsThePortRanksThem()
    {
        var pivots = 0;
        for (var seed = 0; seed < 400; seed++)
        {
            pivots += await AssertSameRanking(() => RandomDag(seed, 40), $"seed {seed}");
        }

        // Enough pivots are made for their order to be what is being compared.
        await Assert.That(pivots).IsGreaterThan(1000);
    }

    [Test]
    public async Task NestingGraphs_RankAsThePortRanksThem()
    {
        var pivots = 0;
        for (var seed = 0; seed < 400; seed++)
        {
            pivots += await AssertSameRanking(() => RandomNestingGraph(seed, 30), $"seed {seed}");
        }

        await Assert.That(pivots).IsGreaterThan(500);
    }

    // Fewer graphs, a good deal larger, so that a pivot can change a deep tree far from where it starts and
    // the tree's edges are reordered many times over.
    [Test]
    public async Task LargeGraphs_RankAsThePortRanksThem()
    {
        var pivots = 0;
        for (var seed = 0; seed < 12; seed++)
        {
            pivots += await AssertSameRanking(() => RandomDag(1000 + seed, 250), $"dag seed {seed}");
            pivots += await AssertSameRanking(() => RandomNestingGraph(1000 + seed, 150), $"nesting seed {seed}");
        }

        await Assert.That(pivots).IsGreaterThan(1000);
    }

    static Graph Simple(params (string V, string W, double? Weight)[] edges)
    {
        var graph = new Graph(multigraph: true);
        graph.SetGraph(new());
        foreach (var (v, w, weight) in edges)
        {
            graph.SetNode(v, new());
            graph.SetNode(w, new());
            graph.SetEdge(v, w, new() { Minlen = 1, Weight = weight });
        }

        return graph;
    }

    static Graph WithAnEdgeToASubgraph()
    {
        var graph = new Graph(multigraph: true, compound: true);
        graph.SetGraph(new());
        graph.SetNode("a", new());
        graph.SetNode("b", new());
        graph.SetNode("group", new());
        graph.SetParent("a", "group");
        graph.SetEdge("b", "group", new() { Minlen = 1, Weight = 1 });
        return graph;
    }

    static Graph WithAnUnlabelledNode()
    {
        var graph = Simple(("a", "b", 1));
        graph.SetNode("c");
        return graph;
    }

    static Graph Declined(string name) =>
        name switch
        {
            "cycle" => Simple(("a", "b", 1), ("b", "c", 1), ("c", "a", 1)),
            "self-loop" => Simple(("a", "b", 1), ("b", "b", 1)),
            "in pieces" => Simple(("a", "b", 1), ("c", "d", 1)),
            "fractional weight" => Simple(("a", "b", 1), ("a", "c", 0.5)),
            "negative weight" => Simple(("a", "b", 1), ("a", "c", -1)),
            "weight too large to sum exactly" => Simple(("a", "b", 4e15), ("a", "c", 1)),
            "edge to a subgraph" => WithAnEdgeToASubgraph(),
            "node without a label" => WithAnUnlabelledNode(),
            _ => new(compound: true)
        };

    // What the indexed ranking does not take on it declines with nothing ranked, and leaves to the port.
    [Test]
    [Arguments("cycle")]
    [Arguments("self-loop")]
    [Arguments("in pieces")]
    [Arguments("fractional weight")]
    [Arguments("negative weight")]
    [Arguments("weight too large to sum exactly")]
    [Arguments("edge to a subgraph")]
    [Arguments("node without a label")]
    [Arguments("no nodes")]
    public async Task DeclinesWhatThePortAloneRanks(string name)
    {
        var graph = Declined(name);
        await Assert.That(IndexedNetworkSimplex.TryRun(graph)).IsFalse();
        await Assert.That(graph.NodeLabels().All(_ => _.Rank is null)).IsTrue();
    }

    // A graph whose weights only the port can sum the way it does is still ranked, by the port.
    [Test]
    public async Task ADeclinedGraphIsRankedByThePort()
    {
        var throughLayout = Simple(("a", "b", 0.5), ("b", "c", 1.5), ("a", "c", 0.25));
        var reference = Simple(("a", "b", 0.5), ("b", "c", 1.5), ("a", "c", 0.25));

        NetworkSimplex.RunOnLayoutGraph(throughLayout);
        NetworkSimplex.Run(Util.AsNonCompoundGraph(reference));

        await Assert.That(Ranks(throughLayout)).IsEqualTo(Ranks(reference));
        await Assert.That(throughLayout.NodeLabel("c").Rank).IsNotNull();
    }

    // A flowchart with subgraphs nested in one another, links that run back up the chart, labelled links
    // and links asking for extra length.
    static string Flowchart(int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder("flowchart TD\n");
        var groups = random.Next(0, 6);
        var nodes = random.Next(3, 30);
        var placed = 0;
        var open = 0;
        for (var group = 0; group < groups && placed < nodes; group++)
        {
            text.Append(CultureInfo.InvariantCulture, $"    subgraph sg{group} [Group {group}]\n");
            open++;
            var size = random.Next(1, 6);
            for (var i = 0; i < size && placed < nodes; i++)
            {
                text.Append(CultureInfo.InvariantCulture, $"        N{placed}[Node {placed}]\n");
                placed++;
            }

            if (random.Next(2) == 0)
            {
                text.Append("    end\n");
                open--;
            }
        }

        for (; open > 0; open--)
        {
            text.Append("    end\n");
        }

        string[] arrows = ["-->", "--->", "---->"];
        var links = random.Next(nodes, nodes * 3);
        for (var i = 0; i < links; i++)
        {
            var arrow = arrows[random.Next(arrows.Length)];
            var label = random.Next(3) == 0 ? $"|l{i}|" : "";
            text.Append(CultureInfo.InvariantCulture, $"    N{random.Next(nodes)} {arrow}{label} N{random.Next(nodes)}\n");
        }

        if (groups > 1)
        {
            text.Append(CultureInfo.InvariantCulture, $"    N{random.Next(nodes)} --> sg{random.Next(groups)}\n");
        }

        return text.ToString();
    }

    static string LayOut(string input, bool throughGraphCopies)
    {
        var model = new FlowchartParser().Parse(input).Value;
        NetworkSimplex.UseGraphCopies = throughGraphCopies;
        try
        {
            new FlowchartRenderer().Render(model, RenderOptions.Default);
        }
        finally
        {
            NetworkSimplex.UseGraphCopies = false;
        }

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

    // A whole layout comes out the same - every node, every edge point, every subgraph box - whether it is
    // ranked over arrays or through the copies dagre ranks.
    [Test]
    public async Task Layouts_AreTheSameRankedEitherWay()
    {
        for (var seed = 0; seed < 150; seed++)
        {
            var input = Flowchart(seed);

            await Assert.That(LayOut(input, throughGraphCopies: false))
                .IsEqualTo(LayOut(input, throughGraphCopies: true))
                .Because($"seed {seed}:\n{input}");
        }
    }
}
