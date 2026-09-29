public class NetworkSimplexTests
{
    Graph graph = null!;
    Graph t = null!;
    Graph gansnerGraph = null!;
    Graph gansnerTree = null!;

    [Before(Test)]
    public void Setup()
    {
        graph = new(multigraph: true);
        graph.SetDefaultNodeLabel(_ => new());
        graph.SetDefaultEdgeLabel((_, _, _) => new() {Minlen = 1, Weight = 1});

        t = new(directed: false);
        t.SetDefaultNodeLabel(_ => new());
        t.SetDefaultEdgeLabel((_, _, _) => new());

        gansnerGraph = new();
        gansnerGraph.SetDefaultNodeLabel(_ => new());
        gansnerGraph.SetDefaultEdgeLabel((_, _, _) => new() {Minlen = 1, Weight = 1});
        gansnerGraph
            .SetPath(["a", "b", "c", "d", "h"])
            .SetPath(["a", "e", "graph", "h"])
            .SetPath(["a", "f", "graph"]);

        gansnerTree = new(directed: false);
        gansnerTree.SetDefaultNodeLabel(_ => new());
        gansnerTree.SetDefaultEdgeLabel((_, _, _) => new());
        gansnerTree
            .SetPath(["a", "b", "c", "d", "h", "graph", "e"])
            .SetEdge("graph", "f");
    }

    [Test]
    public async Task CanAssignARankToASingleNode()
    {
        graph.SetNode("a");
        Ns(graph);
        await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
    }

    [Test]
    public async Task CanAssignARankToA2NodeConnectedGraph()
    {
        graph.SetEdge("a", "b");
        Ns(graph);
        await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
        await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(1);
    }

    [Test]
    public async Task CanAssignRanksForADiamond()
    {
        graph.SetPath(["a", "b", "d"]);
        graph.SetPath(["a", "c", "d"]);
        Ns(graph);
        await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
        await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(1);
        await Assert.That(graph.NodeLabel("c").Rank).IsEqualTo(1);
        await Assert.That(graph.NodeLabel("d").Rank).IsEqualTo(2);
    }

    [Test]
    public async Task UsesTheMinlenAttributeOnTheEdge()
    {
        graph.SetPath(["a", "b", "d"]);
        graph.SetEdge("a", "c");
        graph.SetEdge("c", "d", new() {Minlen = 2});
        Ns(graph);
        await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
        // longest path biases towards the lowest rank it can assign. Since the
        // graph has no optimization opportunities we can assume that the longest
        // path ranking is used.
        await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(2);
        await Assert.That(graph.NodeLabel("c").Rank).IsEqualTo(1);
        await Assert.That(graph.NodeLabel("d").Rank).IsEqualTo(3);
    }

    [Test]
    public async Task CanRankTheGansnerGraph()
    {
        graph = gansnerGraph;
        Ns(graph);
        await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
        await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(1);
        await Assert.That(graph.NodeLabel("c").Rank).IsEqualTo(2);
        await Assert.That(graph.NodeLabel("d").Rank).IsEqualTo(3);
        await Assert.That(graph.NodeLabel("h").Rank).IsEqualTo(4);
        await Assert.That(graph.NodeLabel("e").Rank).IsEqualTo(1);
        await Assert.That(graph.NodeLabel("f").Rank).IsEqualTo(1);
        await Assert.That(graph.NodeLabel("graph").Rank).IsEqualTo(2);
    }

    [Test]
    public async Task CanHandleMultiEdges()
    {
        graph.SetPath(["a", "b", "c", "d"]);
        graph.SetEdge("a", "e", new() {Weight = 2, Minlen = 1});
        graph.SetEdge("e", "d");
        graph.SetEdge("b", "c", new() {Weight = 1, Minlen = 2}, "multi");
        Ns(graph);
        await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
        await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(1);
        // b -> c has minlen = 1 and minlen = 2, so it should be 2 ranks apart.
        await Assert.That(graph.NodeLabel("c").Rank).IsEqualTo(3);
        await Assert.That(graph.NodeLabel("d").Rank).IsEqualTo(4);
        await Assert.That(graph.NodeLabel("e").Rank).IsEqualTo(1);
    }

    // describe("leaveEdge")
    [Test]
    public async Task LeaveEdge_ReturnsUndefinedIfThereIsNoEdgeWithANegativeCutvalue()
    {
        var tree = new Graph(directed: false);
        tree.SetEdge("a", "b", new() {Cutvalue = 1});
        tree.SetEdge("b", "c", new() {Cutvalue = 1});
        await Assert.That(NetworkSimplex.LeaveEdge(tree)).IsNull();
    }

    [Test]
    public async Task LeaveEdge_ReturnsAnEdgeIfOneIsFoundWithANegativeCutvalue()
    {
        var tree = new Graph(directed: false);
        tree.SetEdge("a", "b", new() {Cutvalue = 1});
        tree.SetEdge("b", "c", new() {Cutvalue = -1});
        var e = NetworkSimplex.LeaveEdge(tree);
        await Assert.That(e).IsNotNull();
        await Assert.That(e!.V).IsEqualTo("b");
        await Assert.That(e.W).IsEqualTo("c");
    }

    // describe("enterEdge")
    [Test]
    public async Task EnterEdge_FindsAnEdgeFromTheHeadToTailComponent()
    {
        graph.SetNode("a", new() {Rank = 0});
        graph.SetNode("b", new() {Rank = 2});
        graph.SetNode("c", new() {Rank = 3});
        graph.SetPath(["a", "b", "c"]);
        graph.SetEdge("a", "c");
        t.SetPath(["b", "c", "a"]);
        NetworkSimplex.InitLowLimValues(t, "c");

        var f = NetworkSimplex.EnterEdge(t, graph, new("b", "c"));
        await Assert.That(UndirectedEdge(f)).IsEqualTo(UndirectedEdge(new("a", "b")));
    }

    [Test]
    public async Task EnterEdge_WorksWhenTheRootOfTheTreeIsInTheTailComponent()
    {
        graph.SetNode("a", new() {Rank = 0});
        graph.SetNode("b", new() {Rank = 2});
        graph.SetNode("c", new() {Rank = 3});
        graph.SetPath(["a", "b", "c"]);
        graph.SetEdge("a", "c");
        t.SetPath(["b", "c", "a"]);
        NetworkSimplex.InitLowLimValues(t, "b");

        var f = NetworkSimplex.EnterEdge(t, graph, new("b", "c"));
        await Assert.That(UndirectedEdge(f)).IsEqualTo(UndirectedEdge(new("a", "b")));
    }

    [Test]
    public async Task EnterEdge_FindsTheEdgeWithTheLeastSlack()
    {
        graph.SetNode("a", new() {Rank = 0});
        graph.SetNode("b", new() {Rank = 1});
        graph.SetNode("c", new() {Rank = 3});
        graph.SetNode("d", new() {Rank = 4});
        graph.SetEdge("a", "d");
        graph.SetPath(["a", "c", "d"]);
        graph.SetEdge("b", "c");
        t.SetPath(["c", "d", "a", "b"]);
        NetworkSimplex.InitLowLimValues(t, "a");

        var f = NetworkSimplex.EnterEdge(t, graph, new("c", "d"));
        await Assert.That(UndirectedEdge(f)).IsEqualTo(UndirectedEdge(new("b", "c")));
    }

    [Test]
    public async Task EnterEdge_FindsAnAppropriateEdgeForGansnerGraph1()
    {
        graph = gansnerGraph;
        t = gansnerTree;
        RankUtil.LongestPath(graph);
        NetworkSimplex.InitLowLimValues(t, "a");

        var f = NetworkSimplex.EnterEdge(t, graph, new("graph", "h"));
        await Assert.That(UndirectedEdge(f).V).IsEqualTo("a");
        await Assert.That(["e", "f"]).Contains(UndirectedEdge(f).W);
    }

    [Test]
    public async Task EnterEdge_FindsAnAppropriateEdgeForGansnerGraph2()
    {
        graph = gansnerGraph;
        t = gansnerTree;
        RankUtil.LongestPath(graph);
        NetworkSimplex.InitLowLimValues(t, "e");

        var f = NetworkSimplex.EnterEdge(t, graph, new("graph", "h"));
        await Assert.That(UndirectedEdge(f).V).IsEqualTo("a");
        await Assert.That(["e", "f"]).Contains(UndirectedEdge(f).W);
    }

    [Test]
    public async Task EnterEdge_FindsAnAppropriateEdgeForGansnerGraph3()
    {
        graph = gansnerGraph;
        t = gansnerTree;
        RankUtil.LongestPath(graph);
        NetworkSimplex.InitLowLimValues(t, "a");

        var f = NetworkSimplex.EnterEdge(t, graph, new("h", "graph"));
        await Assert.That(UndirectedEdge(f).V).IsEqualTo("a");
        await Assert.That(["e", "f"]).Contains(UndirectedEdge(f).W);
    }

    [Test]
    public async Task EnterEdge_FindsAnAppropriateEdgeForGansnerGraph4()
    {
        graph = gansnerGraph;
        t = gansnerTree;
        RankUtil.LongestPath(graph);
        NetworkSimplex.InitLowLimValues(t, "e");

        var f = NetworkSimplex.EnterEdge(t, graph, new("h", "graph"));
        await Assert.That(UndirectedEdge(f).V).IsEqualTo("a");
        await Assert.That(["e", "f"]).Contains(UndirectedEdge(f).W);
    }

    // describe("initLowLimValues")
    [Test]
    public async Task InitLowLimValues_AssignsLowLimAndParentForEachNodeInATree()
    {
        var graph = new Graph();
        graph.SetDefaultNodeLabel(_ => new());
        graph
            .SetNodes(["a", "b", "c", "d", "e"])
            .SetPath(["a", "b", "a", "c", "d", "c", "e"]);

        NetworkSimplex.InitLowLimValues(graph, "a");

        var a = graph.NodeLabel("a");
        var b = graph.NodeLabel("b");
        var c = graph.NodeLabel("c");
        var d = graph.NodeLabel("d");
        var e = graph.NodeLabel("e");

        var lims = graph.Nodes().Select(v => graph.NodeLabel(v).Lim!.Value).ToList();
        lims.Sort();
        await Assert.That(lims).IsEquivalentTo(new List<int> {1, 2, 3, 4, 5});

        // a should be {low: 1, lim: 5} (root has no parent)
        await Assert.That(a.Low).IsEqualTo(1);
        await Assert.That(a.Lim).IsEqualTo(5);
        await Assert.That(a.Parent).IsNull();

        await Assert.That(b.Parent).IsEqualTo("a");
        await Assert.That(b.Lim!.Value).IsLessThan(a.Lim!.Value);

        await Assert.That(c.Parent).IsEqualTo("a");
        await Assert.That(c.Lim!.Value).IsLessThan(a.Lim!.Value);
        await Assert.That(c.Lim!.Value).IsNotEqualTo(b.Lim!.Value);

        await Assert.That(d.Parent).IsEqualTo("c");
        await Assert.That(d.Lim!.Value).IsLessThan(c.Lim!.Value);

        await Assert.That(e.Parent).IsEqualTo("c");
        await Assert.That(e.Lim!.Value).IsLessThan(c.Lim!.Value);
        await Assert.That(e.Lim!.Value).IsNotEqualTo(d.Lim!.Value);
    }

    // describe("exchangeEdges")
    [Test]
    public async Task ExchangeEdges_ExchangesEdgesAndUpdatesCutValuesAndLowLimNumbers()
    {
        graph = gansnerGraph;
        t = gansnerTree;
        RankUtil.LongestPath(graph);
        NetworkSimplex.InitLowLimValues(t);

        NetworkSimplex.ExchangeEdges(t, graph, new("graph", "h"), new("a", "e"));

        // check new cut values
        await Assert.That(t.FindEdgeLabel("a", "b").Cutvalue).IsEqualTo(2);
        await Assert.That(t.FindEdgeLabel("b", "c").Cutvalue).IsEqualTo(2);
        await Assert.That(t.FindEdgeLabel("c", "d").Cutvalue).IsEqualTo(2);
        await Assert.That(t.FindEdgeLabel("d", "h").Cutvalue).IsEqualTo(2);
        await Assert.That(t.FindEdgeLabel("a", "e").Cutvalue).IsEqualTo(1);
        await Assert.That(t.FindEdgeLabel("e", "graph").Cutvalue).IsEqualTo(1);
        await Assert.That(t.FindEdgeLabel("graph", "f").Cutvalue).IsEqualTo(0);

        // ensure lim numbers look right
        var lims = t.Nodes().Select(v => t.NodeLabel(v).Lim!.Value).ToList();
        lims.Sort();
        await Assert.That(lims).IsEquivalentTo(new List<int> {1, 2, 3, 4, 5, 6, 7, 8});
    }

    [Test]
    public async Task ExchangeEdges_UpdatesRanks()
    {
        graph = gansnerGraph;
        t = gansnerTree;
        RankUtil.LongestPath(graph);
        NetworkSimplex.InitLowLimValues(t);

        NetworkSimplex.ExchangeEdges(t, graph, new("graph", "h"), new("a", "e"));
        Util.NormalizeRanks(graph);

        // check new ranks
        await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
        await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(1);
        await Assert.That(graph.NodeLabel("c").Rank).IsEqualTo(2);
        await Assert.That(graph.NodeLabel("d").Rank).IsEqualTo(3);
        await Assert.That(graph.NodeLabel("e").Rank).IsEqualTo(1);
        await Assert.That(graph.NodeLabel("f").Rank).IsEqualTo(1);
        await Assert.That(graph.NodeLabel("graph").Rank).IsEqualTo(2);
        await Assert.That(graph.NodeLabel("h").Rank).IsEqualTo(4);
    }

    // describe("calcCutValue")
    // Note: we use p for parent, c for child, gc_x for grandchild nodes, and o for
    // other nodes in the tree for these tests.
    [Test]
    public async Task CalcCutValue_WorksForA2NodeTreeWithCToP()
    {
        graph.SetPath(["c", "p"]);
        t.SetPath(["p", "c"]);
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(1);
    }

    [Test]
    public async Task CalcCutValue_WorksForA2NodeTreeWithCFromP()
    {
        graph.SetPath(["p", "c"]);
        t.SetPath(["p", "c"]);
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(1);
    }

    [Test]
    public async Task CalcCutValue_WorksFor3NodeTreeWithGcToCToP()
    {
        graph.SetPath(["gc", "c", "p"]);
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("p", "c");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(3);
    }

    [Test]
    public async Task CalcCutValue_WorksFor3NodeTreeWithGcToCFromP()
    {
        graph.SetEdge("p", "c");
        graph.SetEdge("gc", "c");
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("p", "c");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(-1);
    }

    [Test]
    public async Task CalcCutValue_WorksFor3NodeTreeWithGcFromCToP()
    {
        graph.SetEdge("c", "p");
        graph.SetEdge("c", "gc");
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("p", "c");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(-1);
    }

    [Test]
    public async Task CalcCutValue_WorksFor3NodeTreeWithGcFromCFromP()
    {
        graph.SetPath(["p", "c", "gc"]);
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("p", "c");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(3);
    }

    [Test]
    public async Task CalcCutValue_WorksFor4NodeTreeGcToCToPToO_WithOToC()
    {
        graph.SetEdge("o", "c", new() {Weight = 7});
        graph.SetPath(["gc", "c", "p", "o"]);
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetPath(["c", "p", "o"]);
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(-4);
    }

    [Test]
    public async Task CalcCutValue_WorksFor4NodeTreeGcToCToPToO_WithOFromC()
    {
        graph.SetEdge("c", "o", new() {Weight = 7});
        graph.SetPath(["gc", "c", "p", "o"]);
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetPath(["c", "p", "o"]);
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(10);
    }

    [Test]
    public async Task CalcCutValue_WorksFor4NodeTreeOToGcToCToP_WithOToC()
    {
        graph.SetEdge("o", "c", new() {Weight = 7});
        graph.SetPath(["o", "gc", "c", "p"]);
        t
            .SetEdge("o", "gc");
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("c", "p");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(-4);
    }

    [Test]
    public async Task CalcCutValue_WorksFor4NodeTreeOToGcToCToP_WithOFromC()
    {
        graph.SetEdge("c", "o", new() {Weight = 7});
        graph.SetPath(["o", "gc", "c", "p"]);
        t.SetEdge("o", "gc");
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("c", "p");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(10);
    }

    [Test]
    public async Task CalcCutValue_WorksFor4NodeTreeGcToCFromPToO_WithOToC()
    {
        graph.SetEdge("gc", "c");
        graph.SetEdge("p", "c");
        graph.SetEdge("p", "o");
        graph.SetEdge("o", "c", new() {Weight = 7});
        t.SetEdge("o", "gc");
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("c", "p");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(6);
    }

    [Test]
    public async Task CalcCutValue_WorksFor4NodeTreeGcToCFromPToO_WithOFromC()
    {
        graph.SetEdge("gc", "c");
        graph.SetEdge("p", "c");
        graph.SetEdge("p", "o");
        graph.SetEdge("c", "o", new() {Weight = 7});
        t.SetEdge("o", "gc");
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("c", "p");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(-8);
    }

    [Test]
    public async Task CalcCutValue_WorksFor4NodeTreeOToGcToCFromP_WithOToC()
    {
        graph.SetEdge("o", "c", new() {Weight = 7});
        graph.SetPath(["o", "gc", "c"]);
        graph.SetEdge("p", "c");
        t.SetEdge("o", "gc");
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("c", "p");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(6);
    }

    [Test]
    public async Task CalcCutValue_WorksFor4NodeTreeOToGcToCFromP_WithOFromC()
    {
        graph.SetEdge("c", "o", new() {Weight = 7});
        graph.SetPath(["o", "gc", "c"]);
        graph.SetEdge("p", "c");
        t.SetEdge("o", "gc");
        t.SetEdge("gc", "c", new() {Cutvalue = 3});
        t.SetEdge("c", "p");
        NetworkSimplex.InitLowLimValues(t, "p");

        await Assert.That(NetworkSimplex.CalcCutValue(t, graph, "c")).IsEqualTo(-8);
    }

    // describe("initCutValues")
    [Test]
    public async Task InitCutValues_WorksForGansnerGraph()
    {
        NetworkSimplex.InitLowLimValues(gansnerTree);
        NetworkSimplex.InitCutValues(gansnerTree, gansnerGraph);
        await Assert.That(gansnerTree.FindEdgeLabel("a", "b").Cutvalue).IsEqualTo(3);
        await Assert.That(gansnerTree.FindEdgeLabel("b", "c").Cutvalue).IsEqualTo(3);
        await Assert.That(gansnerTree.FindEdgeLabel("c", "d").Cutvalue).IsEqualTo(3);
        await Assert.That(gansnerTree.FindEdgeLabel("d", "h").Cutvalue).IsEqualTo(3);
        await Assert.That(gansnerTree.FindEdgeLabel("graph", "h").Cutvalue).IsEqualTo(-1);
        await Assert.That(gansnerTree.FindEdgeLabel("e", "graph").Cutvalue).IsEqualTo(0);
        await Assert.That(gansnerTree.FindEdgeLabel("f", "graph").Cutvalue).IsEqualTo(0);
    }

    [Test]
    public async Task InitCutValues_WorksForUpdatedGansnerGraph()
    {
        gansnerTree.RemoveEdge("graph", "h");
        gansnerTree.SetEdge("a", "e");
        NetworkSimplex.InitLowLimValues(gansnerTree);
        NetworkSimplex.InitCutValues(gansnerTree, gansnerGraph);
        await Assert.That(gansnerTree.FindEdgeLabel("a", "b").Cutvalue).IsEqualTo(2);
        await Assert.That(gansnerTree.FindEdgeLabel("b", "c").Cutvalue).IsEqualTo(2);
        await Assert.That(gansnerTree.FindEdgeLabel("c", "d").Cutvalue).IsEqualTo(2);
        await Assert.That(gansnerTree.FindEdgeLabel("d", "h").Cutvalue).IsEqualTo(2);
        await Assert.That(gansnerTree.FindEdgeLabel("a", "e").Cutvalue).IsEqualTo(1);
        await Assert.That(gansnerTree.FindEdgeLabel("e", "graph").Cutvalue).IsEqualTo(1);
        await Assert.That(gansnerTree.FindEdgeLabel("f", "graph").Cutvalue).IsEqualTo(0);
    }

    static void Ns(Graph graph)
    {
        NetworkSimplex.Run(graph);
        Util.NormalizeRanks(graph);
    }

    static EdgeKey UndirectedEdge(EdgeKey e)
    {
        if (string.CompareOrdinal(e.V, e.W) < 0)
        {
            return new(e.V, e.W);
        }

        return new EdgeKey(e.W, e.V);
    }
}
