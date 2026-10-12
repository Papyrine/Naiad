public class BkTests
{
    static Dictionary<string, string> StrMap(params (string K, string V)[] entries)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in entries)
        {
            d[k] = v;
        }

        return d;
    }

    static Dictionary<string, double> NumMap(params (string K, double V)[] entries)
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (k, v) in entries)
        {
            d[k] = v;
        }

        return d;
    }

    static async Task AssertStrMapEqual(Dictionary<string, string> actual, Dictionary<string, string> expected)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        foreach (var (k, v) in expected)
        {
            await Assert.That(actual.ContainsKey(k)).IsTrue();
            await Assert.That(actual[k]).IsEqualTo(v);
        }
    }

    static async Task AssertNumMapEqual(Dictionary<string, double> actual, Dictionary<string, double> expected)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        foreach (var (k, v) in expected)
        {
            await Assert.That(actual.ContainsKey(k)).IsTrue();
            await Assert.That(actual[k]).IsEqualTo(v).Within(0.0001);
        }
    }

    static List<string> Preds(Graph graph, string v) => graph.Predecessors(v) ?? [];

    // ======================================================================
    // findType1Conflicts
    // ======================================================================
    public class FindType1Conflicts
    {
        Graph graph = null!;
        List<List<string?>> layering = null!;

        [Before(Test)]
        public void Setup()
        {
            graph = new();
            graph.SetGraph(new() {NodeSeparation = 50, EdgeSeparation = 20, RankSeparation = 50});
            graph.SetDefaultEdgeLabel(new EdgeLabel());
            graph.SetNode("a", new() {Rank = 0, Order = 0});
            graph.SetNode("b", new() {Rank = 0, Order = 1});
            graph.SetNode("c", new() {Rank = 1, Order = 0});
            graph.SetNode("d", new() {Rank = 1, Order = 1});
            // Set up crossing
            graph.SetEdge("a", "d");
            graph.SetEdge("b", "c");

            layering = Util.BuildLayerMatrix(graph);
        }

        [Test]
        public async Task DoesNotMarkEdgesThatHaveNoConflict()
        {
            graph.RemoveEdge("a", "d");
            graph.RemoveEdge("b", "c");
            graph.SetEdge("a", "c");
            graph.SetEdge("b", "d");

            var conflicts = BK.FindType1Conflicts(graph, layering);
            await Assert.That(BK.HasConflict(conflicts, "a", "c")).IsFalse();
            await Assert.That(BK.HasConflict(conflicts, "b", "d")).IsFalse();
        }

        [Test]
        public async Task DoesNotMarkType0ConflictsNoDummies()
        {
            var conflicts = BK.FindType1Conflicts(graph, layering);
            await Assert.That(BK.HasConflict(conflicts, "a", "d")).IsFalse();
            await Assert.That(BK.HasConflict(conflicts, "b", "c")).IsFalse();
        }

        [Test]
        [Arguments("a")]
        [Arguments("b")]
        [Arguments("c")]
        [Arguments("d")]
        public async Task DoesNotMarkType0ConflictsWhenOneIsDummy(string v)
        {
            graph.NodeLabel(v).Dummy = DummyKind.Edge;

            var conflicts = BK.FindType1Conflicts(graph, layering);
            await Assert.That(BK.HasConflict(conflicts, "a", "d")).IsFalse();
            await Assert.That(BK.HasConflict(conflicts, "b", "c")).IsFalse();
        }

        [Test]
        [Arguments("a")]
        [Arguments("b")]
        [Arguments("c")]
        [Arguments("d")]
        public async Task DoesMarkType1ConflictsWhenOneIsNonDummy(string v)
        {
            foreach (var w in new[] {"a", "b", "c", "d"})
            {
                if (v != w)
                {
                    graph.NodeLabel(w).Dummy = DummyKind.Edge;
                }
            }

            var conflicts = BK.FindType1Conflicts(graph, layering);
            if (v is "a" or "d")
            {
                await Assert.That(BK.HasConflict(conflicts, "a", "d")).IsTrue();
                await Assert.That(BK.HasConflict(conflicts, "b", "c")).IsFalse();
            }
            else
            {
                await Assert.That(BK.HasConflict(conflicts, "a", "d")).IsFalse();
                await Assert.That(BK.HasConflict(conflicts, "b", "c")).IsTrue();
            }
        }

        [Test]
        public async Task DoesNotMarkType2ConflictsAllDummies()
        {
            foreach (var v in new[] {"a", "b", "c", "d"})
            {
                graph.NodeLabel(v).Dummy = DummyKind.Edge;
            }

            var conflicts = BK.FindType1Conflicts(graph, layering);
            await Assert.That(BK.HasConflict(conflicts, "a", "d")).IsFalse();
            await Assert.That(BK.HasConflict(conflicts, "b", "c")).IsFalse();
            BK.FindType1Conflicts(graph, layering);
        }
    }

    // ======================================================================
    // findType2Conflicts
    // ======================================================================
    public class FindType2Conflicts
    {
        Graph graph = null!;
        List<List<string?>> layering = null!;

        [Before(Test)]
        public void Setup()
        {
            graph = new();
            graph.SetGraph(new() {NodeSeparation = 50, EdgeSeparation = 20, RankSeparation = 50});
            graph.SetDefaultEdgeLabel(new EdgeLabel());
            graph.SetNode("a", new() {Rank = 0, Order = 0});
            graph.SetNode("b", new() {Rank = 0, Order = 1});
            graph.SetNode("c", new() {Rank = 1, Order = 0});
            graph.SetNode("d", new() {Rank = 1, Order = 1});
            // Set up crossing
            graph.SetEdge("a", "d");
            graph.SetEdge("b", "c");

            layering = Util.BuildLayerMatrix(graph);
        }

        [Test]
        public async Task MarksType2ConflictsFavoringBorderSegments1()
        {
            foreach (var v in new[] {"a", "d"})
            {
                graph.NodeLabel(v).Dummy = DummyKind.Edge;
            }

            foreach (var v in new[] {"b", "c"})
            {
                graph.NodeLabel(v).Dummy = DummyKind.Border;
            }

            var conflicts = BK.FindType2Conflicts(graph, layering);
            await Assert.That(BK.HasConflict(conflicts, "a", "d")).IsTrue();
            await Assert.That(BK.HasConflict(conflicts, "b", "c")).IsFalse();
            BK.FindType1Conflicts(graph, layering);
        }

        [Test]
        public async Task MarksType2ConflictsFavoringBorderSegments2()
        {
            foreach (var v in new[] {"b", "c"})
            {
                graph.NodeLabel(v).Dummy = DummyKind.Edge;
            }

            foreach (var v in new[] {"a", "d"})
            {
                graph.NodeLabel(v).Dummy = DummyKind.Border;
            }

            var conflicts = BK.FindType2Conflicts(graph, layering);
            await Assert.That(BK.HasConflict(conflicts, "a", "d")).IsFalse();
            await Assert.That(BK.HasConflict(conflicts, "b", "c")).IsTrue();
            BK.FindType1Conflicts(graph, layering);
        }
    }

    // ======================================================================
    // hasConflict
    // ======================================================================
    public class HasConflict
    {
        [Test]
        public async Task CanTestForAType1ConflictRegardlessOfEdgeOrientation()
        {
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
            BK.AddConflict(conflicts, "b", "a");
            await Assert.That(BK.HasConflict(conflicts, "a", "b")).IsTrue();
            await Assert.That(BK.HasConflict(conflicts, "b", "a")).IsTrue();
        }

        [Test]
        public async Task WorksForMultipleConflictsWithTheSameNode()
        {
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
            BK.AddConflict(conflicts, "a", "b");
            BK.AddConflict(conflicts, "a", "c");
            await Assert.That(BK.HasConflict(conflicts, "a", "b")).IsTrue();
            await Assert.That(BK.HasConflict(conflicts, "a", "c")).IsTrue();
        }
    }

    // ======================================================================
    // verticalAlignment
    // ======================================================================
    public class VerticalAlignment
    {
        Graph graph = null!;

        [Before(Test)]
        public void Setup()
        {
            graph = new();
            graph.SetGraph(new() {NodeSeparation = 50, EdgeSeparation = 20, RankSeparation = 50});
        }

        [Test]
        public async Task AlignsWithItselfIfTheNodeHasNoAdjacencies()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0});
            graph.SetNode("b", new() {Rank = 1, Order = 0});

            var layering = Util.BuildLayerMatrix(graph);
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

            var result = BK.VerticalAlignment(layering, conflicts, v => graph.PredecessorsOf(v));
            await AssertStrMapEqual(result.Root, StrMap(("a", "a"), ("b", "b")));
            await AssertStrMapEqual(result.Align, StrMap(("a", "a"), ("b", "b")));
        }

        [Test]
        public async Task AlignsWithItsSoleAdjacency()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0});
            graph.SetNode("b", new() {Rank = 1, Order = 0});
            graph.SetEdge("a", "b");

            var layering = Util.BuildLayerMatrix(graph);
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

            var result = BK.VerticalAlignment(layering, conflicts, v => graph.PredecessorsOf(v));
            await AssertStrMapEqual(result.Root, StrMap(("a", "a"), ("b", "a")));
            await AssertStrMapEqual(result.Align, StrMap(("a", "b"), ("b", "a")));
        }

        [Test]
        public async Task AlignsWithItsLeftMedianWhenPossible()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0});
            graph.SetNode("b", new() {Rank = 0, Order = 1});
            graph.SetNode("c", new() {Rank = 1, Order = 0});
            graph.SetEdge("a", "c");
            graph.SetEdge("b", "c");

            var layering = Util.BuildLayerMatrix(graph);
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

            var result = BK.VerticalAlignment(layering, conflicts, v => graph.PredecessorsOf(v));
            await AssertStrMapEqual(result.Root, StrMap(("a", "a"), ("b", "b"), ("c", "a")));
            await AssertStrMapEqual(result.Align, StrMap(("a", "c"), ("b", "b"), ("c", "a")));
        }

        [Test]
        public async Task AlignsCorrectlyRegardlessOfNodeNameOrInsertionOrder()
        {
            // This test ensures that we're actually properly sorting nodes by
            // position when searching for candidates.
            graph.SetNode("b", new() {Rank = 0, Order = 1});
            graph.SetNode("c", new() {Rank = 1, Order = 0});
            graph.SetNode("z", new() {Rank = 0, Order = 0});
            graph.SetEdge("z", "c");
            graph.SetEdge("b", "c");

            var layering = Util.BuildLayerMatrix(graph);
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

            var result = BK.VerticalAlignment(layering, conflicts, v => graph.PredecessorsOf(v));
            await AssertStrMapEqual(result.Root, StrMap(("z", "z"), ("b", "b"), ("c", "z")));
            await AssertStrMapEqual(result.Align, StrMap(("z", "c"), ("b", "b"), ("c", "z")));
        }

        [Test]
        public async Task AlignsWithItsRightMedianWhenLeftIsUnavailable()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0});
            graph.SetNode("b", new() {Rank = 0, Order = 1});
            graph.SetNode("c", new() {Rank = 1, Order = 0});
            graph.SetEdge("a", "c");
            graph.SetEdge("b", "c");

            var layering = Util.BuildLayerMatrix(graph);
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

            BK.AddConflict(conflicts, "a", "c");

            var result = BK.VerticalAlignment(layering, conflicts, v => graph.PredecessorsOf(v));
            await AssertStrMapEqual(result.Root, StrMap(("a", "a"), ("b", "b"), ("c", "b")));
            await AssertStrMapEqual(result.Align, StrMap(("a", "a"), ("b", "c"), ("c", "b")));
        }

        [Test]
        public async Task AlignsWithNeitherMedianIfBothAreUnavailable()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0});
            graph.SetNode("b", new() {Rank = 0, Order = 1});
            graph.SetNode("c", new() {Rank = 1, Order = 0});
            graph.SetNode("d", new() {Rank = 1, Order = 1});
            graph.SetEdge("a", "d");
            graph.SetEdge("b", "c");
            graph.SetEdge("b", "d");

            var layering = Util.BuildLayerMatrix(graph);
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

            var result = BK.VerticalAlignment(layering, conflicts, v => graph.PredecessorsOf(v));
            // c will align with b, so d will not be able to align with a, because
            // (a,d) and (c,b) cross.
            await AssertStrMapEqual(result.Root, StrMap(("a", "a"), ("b", "b"), ("c", "b"), ("d", "d")));
            await AssertStrMapEqual(result.Align, StrMap(("a", "a"), ("b", "c"), ("c", "b"), ("d", "d")));
        }

        [Test]
        public async Task AlignsWithTheSingleMedianForAnOddNumberOfAdjacencies()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0});
            graph.SetNode("b", new() {Rank = 0, Order = 1});
            graph.SetNode("c", new() {Rank = 0, Order = 2});
            graph.SetNode("d", new() {Rank = 1, Order = 0});
            graph.SetEdge("a", "d");
            graph.SetEdge("b", "d");
            graph.SetEdge("c", "d");

            var layering = Util.BuildLayerMatrix(graph);
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

            var result = BK.VerticalAlignment(layering, conflicts, v => graph.PredecessorsOf(v));
            await AssertStrMapEqual(result.Root, StrMap(("a", "a"), ("b", "b"), ("c", "c"), ("d", "b")));
            await AssertStrMapEqual(result.Align, StrMap(("a", "a"), ("b", "d"), ("c", "c"), ("d", "b")));
        }

        [Test]
        public async Task AlignsBlocksAcrossMultipleLayers()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0});
            graph.SetNode("b", new() {Rank = 1, Order = 0});
            graph.SetNode("c", new() {Rank = 1, Order = 1});
            graph.SetNode("d", new() {Rank = 2, Order = 0});
            graph.SetPath(["a", "b", "d"]);
            graph.SetPath(["a", "c", "d"]);

            var layering = Util.BuildLayerMatrix(graph);
            var conflicts = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

            var result = BK.VerticalAlignment(layering, conflicts, v => graph.PredecessorsOf(v));
            await AssertStrMapEqual(result.Root, StrMap(("a", "a"), ("b", "a"), ("c", "c"), ("d", "a")));
            await AssertStrMapEqual(result.Align, StrMap(("a", "b"), ("b", "d"), ("c", "c"), ("d", "a")));
        }
    }

    // ======================================================================
    // horizontalCompaction
    // ======================================================================
    public class HorizontalCompaction
    {
        Graph graph = null!;

        [Before(Test)]
        public void Setup()
        {
            graph = new();
            graph.SetGraph(new() {NodeSeparation = 50, EdgeSeparation = 20, RankSeparation = 50});
        }

        [Test]
        public async Task PlacesTheCenterOfASingleNodeGraphAtOrigin()
        {
            var root = StrMap(("a", "a"));
            var align = StrMap(("a", "a"));
            graph.SetNode("a", new() {Rank = 0, Order = 0});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
        }

        [Test]
        public async Task SeparatesAdjacentNodesBySpecifiedNodeSeparation()
        {
            var root = StrMap(("a", "a"), ("b", "b"));
            var align = StrMap(("a", "a"), ("b", "b"));
            graph.Label.NodeSeparation = 100;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 200});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
            await Assert.That(xs["b"]).IsEqualTo(100 / 2.0 + 100 + 200 / 2.0);
        }

        [Test]
        public async Task SeparatesAdjacentEdgesBySpecifiedNodeSeparation()
        {
            var root = StrMap(("a", "a"), ("b", "b"));
            var align = StrMap(("a", "a"), ("b", "b"));
            graph.Label.EdgeSeparation = 20;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100, Dummy = DummyKind.Edge});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 200, Dummy = DummyKind.Edge});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
            await Assert.That(xs["b"]).IsEqualTo(100 / 2.0 + 20 + 200 / 2.0);
        }

        [Test]
        public async Task AlignsTheCentersOfNodesInTheSameBlock()
        {
            var root = StrMap(("a", "a"), ("b", "a"));
            var align = StrMap(("a", "b"), ("b", "a"));
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100});
            graph.SetNode("b", new() {Rank = 1, Order = 0, Width = 200});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
            await Assert.That(xs["b"]).IsEqualTo(0d);
        }

        [Test]
        public async Task SeparatesBlocksWithTheAppropriateSeparation()
        {
            var root = StrMap(("a", "a"), ("b", "a"), ("c", "c"));
            var align = StrMap(("a", "b"), ("b", "a"), ("c", "c"));
            graph.Label.NodeSeparation = 75;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100});
            graph.SetNode("b", new() {Rank = 1, Order = 1, Width = 200});
            graph.SetNode("c", new() {Rank = 1, Order = 0, Width = 50});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(50 / 2.0 + 75 + 200 / 2.0);
            await Assert.That(xs["b"]).IsEqualTo(50 / 2.0 + 75 + 200 / 2.0);
            await Assert.That(xs["c"]).IsEqualTo(0d);
        }

        [Test]
        public async Task SeparatesClassesWithTheAppropriateSeparation()
        {
            var root = StrMap(("a", "a"), ("b", "b"), ("c", "c"), ("d", "b"));
            var align = StrMap(("a", "a"), ("b", "d"), ("c", "c"), ("d", "b"));
            graph.Label.NodeSeparation = 75;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 200});
            graph.SetNode("c", new() {Rank = 1, Order = 0, Width = 50});
            graph.SetNode("d", new() {Rank = 1, Order = 1, Width = 80});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
            await Assert.That(xs["b"]).IsEqualTo(100 / 2.0 + 75 + 200 / 2.0);
            await Assert.That(xs["c"]).IsEqualTo(100 / 2.0 + 75 + 200 / 2.0 - 80 / 2.0 - 75 - 50 / 2.0);
            await Assert.That(xs["d"]).IsEqualTo(100 / 2.0 + 75 + 200 / 2.0);
        }

        [Test]
        public async Task ShiftsClassesByMaxSepFromTheAdjacentBlock1()
        {
            var root = StrMap(("a", "a"), ("b", "b"), ("c", "a"), ("d", "b"));
            var align = StrMap(("a", "c"), ("b", "d"), ("c", "a"), ("d", "b"));
            graph.Label.NodeSeparation = 75;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 50});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 150});
            graph.SetNode("c", new() {Rank = 1, Order = 0, Width = 60});
            graph.SetNode("d", new() {Rank = 1, Order = 1, Width = 70});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
            await Assert.That(xs["b"]).IsEqualTo(50 / 2.0 + 75 + 150 / 2.0);
            await Assert.That(xs["c"]).IsEqualTo(0d);
            await Assert.That(xs["d"]).IsEqualTo(50 / 2.0 + 75 + 150 / 2.0);
        }

        [Test]
        public async Task ShiftsClassesByMaxSepFromTheAdjacentBlock2()
        {
            var root = StrMap(("a", "a"), ("b", "b"), ("c", "a"), ("d", "b"));
            var align = StrMap(("a", "c"), ("b", "d"), ("c", "a"), ("d", "b"));
            graph.Label.NodeSeparation = 75;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 50});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 70});
            graph.SetNode("c", new() {Rank = 1, Order = 0, Width = 60});
            graph.SetNode("d", new() {Rank = 1, Order = 1, Width = 150});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
            await Assert.That(xs["b"]).IsEqualTo(60 / 2.0 + 75 + 150 / 2.0);
            await Assert.That(xs["c"]).IsEqualTo(0d);
            await Assert.That(xs["d"]).IsEqualTo(60 / 2.0 + 75 + 150 / 2.0);
        }

        [Test]
        public async Task CascadesClassShift()
        {
            var root = StrMap(("a", "a"), ("b", "b"), ("c", "c"), ("d", "d"), ("e", "b"), ("f", "f"), ("graph", "d"));
            var align = StrMap(("a", "a"), ("b", "e"), ("c", "c"), ("d", "graph"), ("e", "b"), ("f", "f"), ("graph", "d"));
            graph.Label.NodeSeparation = 75;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 50});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 50});
            graph.SetNode("c", new() {Rank = 1, Order = 0, Width = 50});
            graph.SetNode("d", new() {Rank = 1, Order = 1, Width = 50});
            graph.SetNode("e", new() {Rank = 1, Order = 2, Width = 50});
            graph.SetNode("f", new() {Rank = 2, Order = 0, Width = 50});
            graph.SetNode("graph", new() {Rank = 2, Order = 1, Width = 50});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);

            // Use f as 0, everything is relative to it
            await Assert.That(xs["a"]).IsEqualTo(xs["b"] - 50 / 2.0 - 75 - 50 / 2.0);
            await Assert.That(xs["b"]).IsEqualTo(xs["e"]);
            await Assert.That(xs["c"]).IsEqualTo(xs["f"]);
            await Assert.That(xs["d"]).IsEqualTo(xs["c"] + 50 / 2.0 + 75 + 50 / 2.0);
            await Assert.That(xs["e"]).IsEqualTo(xs["d"] + 50 / 2.0 + 75 + 50 / 2.0);
            await Assert.That(xs["graph"]).IsEqualTo(xs["f"] + 50 / 2.0 + 75 + 50 / 2.0);
        }

        [Test]
        public async Task HandlesLabelposL()
        {
            var root = StrMap(("a", "a"), ("b", "b"), ("c", "c"));
            var align = StrMap(("a", "a"), ("b", "b"), ("c", "c"));
            graph.Label.EdgeSeparation = 50;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100, Dummy = DummyKind.Edge});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 200, Dummy = DummyKind.EdgeLabel, Labelpos = LabelPosition.Left});
            graph.SetNode("c", new() {Rank = 0, Order = 2, Width = 300, Dummy = DummyKind.Edge});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
            await Assert.That(xs["b"]).IsEqualTo(xs["a"] + 100 / 2.0 + 50 + 200);
            await Assert.That(xs["c"]).IsEqualTo(xs["b"] + 0 + 50 + 300 / 2.0);
        }

        [Test]
        public async Task HandlesLabelposC()
        {
            var root = StrMap(("a", "a"), ("b", "b"), ("c", "c"));
            var align = StrMap(("a", "a"), ("b", "b"), ("c", "c"));
            graph.Label.EdgeSeparation = 50;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100, Dummy = DummyKind.Edge});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 200, Dummy = DummyKind.EdgeLabel, Labelpos = LabelPosition.Center});
            graph.SetNode("c", new() {Rank = 0, Order = 2, Width = 300, Dummy = DummyKind.Edge});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
            await Assert.That(xs["b"]).IsEqualTo(xs["a"] + 100 / 2.0 + 50 + 200 / 2.0);
            await Assert.That(xs["c"]).IsEqualTo(xs["b"] + 200 / 2.0 + 50 + 300 / 2.0);
        }

        [Test]
        public async Task HandlesLabelposR()
        {
            var root = StrMap(("a", "a"), ("b", "b"), ("c", "c"));
            var align = StrMap(("a", "a"), ("b", "b"), ("c", "c"));
            graph.Label.EdgeSeparation = 50;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100, Dummy = DummyKind.Edge});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 200, Dummy = DummyKind.EdgeLabel, Labelpos = LabelPosition.Right});
            graph.SetNode("c", new() {Rank = 0, Order = 2, Width = 300, Dummy = DummyKind.Edge});

            var xs = BK.HorizontalCompaction(graph, Util.BuildLayerMatrix(graph), root, align);
            await Assert.That(xs["a"]).IsEqualTo(0d);
            await Assert.That(xs["b"]).IsEqualTo(xs["a"] + 100 / 2.0 + 50 + 0);
            await Assert.That(xs["c"]).IsEqualTo(xs["b"] + 200 + 50 + 300 / 2.0);
        }
    }

    // ======================================================================
    // alignCoordinates
    // ======================================================================
    public class AlignCoordinates
    {
        [Test]
        public async Task AlignsASingleNode()
        {
            var ul = NumMap(("a", 50));
            var ur = NumMap(("a", 100));
            var dl = NumMap(("a", 50));
            var dr = NumMap(("a", 200));
            var xss = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                ["ul"] = ul,
                ["ur"] = ur,
                ["dl"] = dl,
                ["dr"] = dr
            };

            BK.AlignCoordinates(xss, ul);

            await AssertNumMapEqual(xss["ul"], NumMap(("a", 50)));
            await AssertNumMapEqual(xss["ur"], NumMap(("a", 50)));
            await AssertNumMapEqual(xss["dl"], NumMap(("a", 50)));
            await AssertNumMapEqual(xss["dr"], NumMap(("a", 50)));
        }

        [Test]
        public async Task AlignsMultipleNodes()
        {
            var ul = NumMap(("a", 50), ("b", 1000));
            var ur = NumMap(("a", 100), ("b", 900));
            var dl = NumMap(("a", 150), ("b", 800));
            var dr = NumMap(("a", 200), ("b", 700));
            var xss = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                ["ul"] = ul,
                ["ur"] = ur,
                ["dl"] = dl,
                ["dr"] = dr
            };

            BK.AlignCoordinates(xss, ul);

            await AssertNumMapEqual(xss["ul"], NumMap(("a", 50), ("b", 1000)));
            await AssertNumMapEqual(xss["ur"], NumMap(("a", 200), ("b", 1000)));
            await AssertNumMapEqual(xss["dl"], NumMap(("a", 50), ("b", 700)));
            await AssertNumMapEqual(xss["dr"], NumMap(("a", 500), ("b", 1000)));
        }
    }

    // ======================================================================
    // findSmallestWidthAlignment
    // ======================================================================
    public class FindSmallestWidthAlignment
    {
        Graph graph = null!;

        [Before(Test)]
        public void Setup()
        {
            graph = new();
            graph.SetGraph(new() {NodeSeparation = 50, EdgeSeparation = 20, RankSeparation = 50});
        }

        [Test]
        public Task FindsTheAlignmentWithTheSmallestWidth()
        {
            graph.SetNode("a", new() {Width = 50});
            graph.SetNode("b", new() {Width = 50});

            var xss = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                ["ul"] = NumMap(("a", 0), ("b", 1000)),
                ["ur"] = NumMap(("a", -5), ("b", 1000)),
                ["dl"] = NumMap(("a", 5), ("b", 2000)),
                ["dr"] = NumMap(("a", 0), ("b", 200))
            };

            var result = BK.FindSmallestWidthAlignment(graph, xss);
            return AssertNumMapEqual(result, NumMap(("a", 0), ("b", 200)));
        }

        [Test]
        public Task TakesNodeWidthIntoAccount()
        {
            graph.SetNode("a", new() {Width = 50});
            graph.SetNode("b", new() {Width = 50});
            graph.SetNode("c", new() {Width = 200});

            var xss = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                ["ul"] = NumMap(("a", 0), ("b", 100), ("c", 75)),
                ["ur"] = NumMap(("a", 0), ("b", 100), ("c", 80)),
                ["dl"] = NumMap(("a", 0), ("b", 100), ("c", 85)),
                ["dr"] = NumMap(("a", 0), ("b", 100), ("c", 90))
            };

            var result = BK.FindSmallestWidthAlignment(graph, xss);
            return AssertNumMapEqual(result, NumMap(("a", 0), ("b", 100), ("c", 75)));
        }
    }

    // ======================================================================
    // balance
    // ======================================================================
    public class Balance
    {
        [Test]
        public Task AlignsASingleNodeToTheSharedMedianValue()
        {
            var xss = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                ["ul"] = NumMap(("a", 0)),
                ["ur"] = NumMap(("a", 100)),
                ["dl"] = NumMap(("a", 100)),
                ["dr"] = NumMap(("a", 200))
            };

            return AssertNumMapEqual(BK.Balance(xss), NumMap(("a", 100)));
        }

        [Test]
        public Task AlignsASingleNodeToTheAverageOfDifferentMedianValues()
        {
            var xss = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                ["ul"] = NumMap(("a", 0)),
                ["ur"] = NumMap(("a", 75)),
                ["dl"] = NumMap(("a", 125)),
                ["dr"] = NumMap(("a", 200))
            };

            return AssertNumMapEqual(BK.Balance(xss), NumMap(("a", 100)));
        }

        [Test]
        public Task BalancesMultipleNodes()
        {
            var xss = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                ["ul"] = NumMap(("a", 0), ("b", 50)),
                ["ur"] = NumMap(("a", 75), ("b", 0)),
                ["dl"] = NumMap(("a", 125), ("b", 60)),
                ["dr"] = NumMap(("a", 200), ("b", 75))
            };

            return AssertNumMapEqual(BK.Balance(xss), NumMap(("a", 100), ("b", 55)));
        }
    }

    // ======================================================================
    // positionX
    // ======================================================================
    public class PositionX
    {
        Graph graph = null!;

        [Before(Test)]
        public void Setup()
        {
            graph = new();
            graph.SetGraph(new() {NodeSeparation = 50, EdgeSeparation = 20, RankSeparation = 50});
        }

        [Test]
        public Task PositionsASingleNodeAtOrigin()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100});
            return AssertNumMapEqual(BK.PositionX(graph), NumMap(("a", 0)));
        }

        [Test]
        public Task PositionsASingleNodeBlockAtOrigin()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 100});
            graph.SetNode("b", new() {Rank = 1, Order = 0, Width = 100});
            graph.SetEdge("a", "b");
            return AssertNumMapEqual(BK.PositionX(graph), NumMap(("a", 0), ("b", 0)));
        }

        [Test]
        public Task PositionsASingleNodeBlockAtOriginEvenWhenTheirSizesDiffer()
        {
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 40});
            graph.SetNode("b", new() {Rank = 1, Order = 0, Width = 500});
            graph.SetNode("c", new() {Rank = 2, Order = 0, Width = 20});
            graph.SetPath(["a", "b", "c"]);
            return AssertNumMapEqual(BK.PositionX(graph), NumMap(("a", 0), ("b", 0), ("c", 0)));
        }

        [Test]
        public Task CentersANodeIfItIsAPredecessorOfTwoSameSizedNodes()
        {
            graph.Label.NodeSeparation = 10;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 20});
            graph.SetNode("b", new() {Rank = 1, Order = 0, Width = 50});
            graph.SetNode("c", new() {Rank = 1, Order = 1, Width = 50});
            graph.SetEdge("a", "b");
            graph.SetEdge("a", "c");

            var pos = BK.PositionX(graph);
            var a = pos["a"];
            return AssertNumMapEqual(pos, NumMap(("a", a), ("b", a - (25 + 5)), ("c", a + (25 + 5))));
        }

        [Test]
        public Task ShiftsBlocksOnBothSidesOfAlignedBlock()
        {
            graph.Label.NodeSeparation = 10;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 50});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 60});
            graph.SetNode("c", new() {Rank = 1, Order = 0, Width = 70});
            graph.SetNode("d", new() {Rank = 1, Order = 1, Width = 80});
            graph.SetEdge("b", "c");

            var pos = BK.PositionX(graph);
            var b = pos["b"];
            var c = b;
            return AssertNumMapEqual(pos, NumMap(
                ("a", b - 60 / 2.0 - 10 - 50 / 2.0),
                ("b", b),
                ("c", c),
                ("d", c + 70 / 2.0 + 10 + 80 / 2.0)));
        }

        [Test]
        public Task AlignsInnerSegments()
        {
            graph.Label.NodeSeparation = 10;
            graph.Label.EdgeSeparation = 10;
            graph.SetNode("a", new() {Rank = 0, Order = 0, Width = 50, Dummy = DummyKind.Edge});
            graph.SetNode("b", new() {Rank = 0, Order = 1, Width = 60});
            graph.SetNode("c", new() {Rank = 1, Order = 0, Width = 70});
            graph.SetNode("d", new() {Rank = 1, Order = 1, Width = 80, Dummy = DummyKind.Edge});
            graph.SetEdge("b", "c");
            graph.SetEdge("a", "d");

            var pos = BK.PositionX(graph);
            var a = pos["a"];
            var d = a;
            return AssertNumMapEqual(
                pos,
                NumMap(
                    ("a", a),
                    ("b", a + 50 / 2.0 + 10 + 60 / 2.0),
                    ("c", d - 70 / 2.0 - 10 - 80 / 2.0),
                    ("d", d)));
        }
    }
}
