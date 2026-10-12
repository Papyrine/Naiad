public class UtilTests
{
    public class SimplifyTests
    {
        Graph graph = null!;

        [Before(Test)]
        public void Setup() => graph = new(multigraph: true);

        [Test]
        public async Task CopiesWithoutChangeAGraphWithNoMultiEdges()
        {
            graph.SetEdge("a", "b", new() { Weight = 1, Minlen = 1 });
            var g2 = Util.Simplify(graph);
            var e = g2.FindEdgeLabel("a", "b");
            await Assert.That(e.Weight).IsEqualTo(1);
            await Assert.That(e.Minlen).IsEqualTo(1);
            await Assert.That(g2.Edges().Count).IsEqualTo(1);
        }

        [Test]
        public async Task CollapsesMultiEdges()
        {
            graph.SetEdge("a", "b", new() { Weight = 1, Minlen = 1 });
            graph.SetEdge("a", "b", new() { Weight = 2, Minlen = 2 }, "multi");
            var g2 = Util.Simplify(graph);
            await Assert.That(g2.IsMultigraph).IsFalse();
            var e = g2.FindEdgeLabel("a", "b");
            await Assert.That(e.Weight).IsEqualTo(3);
            await Assert.That(e.Minlen).IsEqualTo(2);
            await Assert.That(g2.Edges().Count).IsEqualTo(1);
        }

        [Test]
        public async Task CopiesTheGraphObject()
        {
            var label = new GraphLabel { NestingRoot = "bar" };
            graph.SetGraph(label);
            var g2 = Util.Simplify(graph);
            await Assert.That(g2.Label).IsSameReferenceAs(label);
        }
    }

    public class AsNonCompoundGraphTests
    {
        Graph graph = null!;

        [Before(Test)]
        public void Setup() => graph = new(multigraph: true, compound: true);

        [Test]
        public async Task CopiesAllNodes()
        {
            var aLabel = new NodeLabel { Width = 1 };
            graph.SetNode("a", aLabel);
            graph.SetNode("b");
            var g2 = Util.AsNonCompoundGraph(graph);
            await Assert.That(g2.NodeLabel("a")).IsSameReferenceAs(aLabel);
            await Assert.That(g2.HasNode("b")).IsTrue();
        }

        [Test]
        public async Task CopiesAllEdges()
        {
            var l1 = new EdgeLabel { Labelpos = LabelPosition.Left };
            var l2 = new EdgeLabel { Labelpos = LabelPosition.Right };
            graph.SetEdge("a", "b", l1);
            graph.SetEdge("a", "b", l2, "multi");
            var g2 = Util.AsNonCompoundGraph(graph);
            await Assert.That(g2.FindEdgeLabel("a", "b").Labelpos).IsEqualTo(LabelPosition.Left);
            await Assert.That(g2.FindEdgeLabel("a", "b", "multi").Labelpos).IsEqualTo(LabelPosition.Right);
        }

        [Test]
        public async Task DoesNotCopyCompoundNodes()
        {
            graph.SetParent("a", "sg1");
            var g2 = Util.AsNonCompoundGraph(graph);
            await Assert.That(g2.Parent("sg1")).IsNull();
            await Assert.That(g2.Parent("a")).IsNull();
            await Assert.That(g2.IsCompound).IsFalse();
        }

        [Test]
        public async Task CopiesTheGraphObject()
        {
            var label = new GraphLabel { NestingRoot = "bar" };
            graph.SetGraph(label);
            var g2 = Util.AsNonCompoundGraph(graph);
            await Assert.That(g2.Label).IsSameReferenceAs(label);
        }
    }

    public class IntersectRectTests
    {
        static NodeLabel Rect(double x, double y, double width, double height) =>
            new() { X = x, Y = y, Width = width, Height = height };

        static async Task ExpectIntersects(NodeLabel rect, Position point)
        {
            var cross = Util.IntersectRect(rect, point);
            if (cross.X != point.X)
            {
                var m = (cross.Y - point.Y) / (cross.X - point.X);
                // toBeCloseTo default precision is 2 digits (|diff| < 0.005).
                await Assert.That(cross.Y - rect.Y!.Value).IsEqualTo(m * (cross.X - rect.X!.Value)).Within(0.005);
            }
        }

        static async Task ExpectTouchesBorder(NodeLabel rect, Position point)
        {
            var cross = Util.IntersectRect(rect, point);
            if (Math.Abs(rect.X!.Value - cross.X) != rect.Width / 2)
            {
                await Assert.That(Math.Abs(rect.Y!.Value - cross.Y)).IsEqualTo(rect.Height / 2);
            }
        }

        [Test]
        public async Task CreatesASlopeThatWillIntersectTheRectanglesCenter()
        {
            var rect = Rect(0, 0, 1, 1);
            await ExpectIntersects(rect, new(2, 6));
            await ExpectIntersects(rect, new(2, -6));
            await ExpectIntersects(rect, new(6, 2));
            await ExpectIntersects(rect, new(-6, 2));
            await ExpectIntersects(rect, new(5, 0));
            await ExpectIntersects(rect, new(0, 5));
        }

        [Test]
        public async Task TouchesTheBorderOfTheRectangle()
        {
            var rect = Rect(0, 0, 1, 1);
            await ExpectTouchesBorder(rect, new(2, 6));
            await ExpectTouchesBorder(rect, new(2, -6));
            await ExpectTouchesBorder(rect, new(6, 2));
            await ExpectTouchesBorder(rect, new(-6, 2));
            await ExpectTouchesBorder(rect, new(5, 0));
            await ExpectTouchesBorder(rect, new(0, 5));
        }

        [Test]
        public async Task ThrowsAnErrorIfThePointIsAtTheCenterOfTheRectangle()
        {
            var rect = Rect(0, 0, 1, 1);
            await Assert.That(() => Util.IntersectRect(rect, new(0, 0))).Throws<Exception>();
        }
    }

    public class BuildLayerMatrixTests
    {
        [Test]
        public async Task CreatesAMatrixBasedOnRankAndOrderOfNodesInTheGraph()
        {
            var graph = new Graph();
            graph.SetNode("a", new() { Rank = 0, Order = 0 });
            graph.SetNode("b", new() { Rank = 0, Order = 1 });
            graph.SetNode("c", new() { Rank = 1, Order = 0 });
            graph.SetNode("d", new() { Rank = 1, Order = 1 });
            graph.SetNode("e", new() { Rank = 2, Order = 0 });

            var expected = new List<List<string?>>
            {
                new() { "a", "b" },
                new() { "c", "d" },
                new() { "e" }
            };
            await Assert.That(Util.BuildLayerMatrix(graph)).IsEquivalentTo(expected);
        }
    }

    public class NormalizeRanksTests
    {
        [Test]
        public async Task AdjustRanksSuchThatAllAreGteZeroAndAtLeastOneIsZero()
        {
            var graph = new Graph();
            graph.SetNode("a", new() {Rank = 3});
            graph.SetNode("b", new() {Rank = 2});
            graph.SetNode("c", new() {Rank = 4});

            Util.NormalizeRanks(graph);

            await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(1);
            await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(0);
            await Assert.That(graph.NodeLabel("c").Rank).IsEqualTo(2);
        }

        [Test]
        public async Task WorksForNegativeRanks()
        {
            var graph = new Graph();
            graph.SetNode("a", new() {Rank = -3});
            graph.SetNode("b", new() {Rank = -2});

            Util.NormalizeRanks(graph);

            await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
            await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(1);
        }

        [Test]
        public async Task DoesNotAssignARankToSubgraphs()
        {
            var graph = new Graph(compound: true);
            graph.SetNode("a", new() {Rank = 0});
            graph.SetNode("sg", new());
            graph.SetParent("a", "sg");

            Util.NormalizeRanks(graph);

            await Assert.That(graph.NodeLabel("sg").Rank).IsNull();
            await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
        }
    }

    public class RemoveEmptyRanksTests
    {
        [Test]
        public async Task RemovesBorderRanksWithoutAnyNodes()
        {
            var graph = new Graph();
            graph.SetGraph(new() {NodeRankFactor = 4});
            graph.SetNode("a", new() {Rank = 0});
            graph.SetNode("b", new() {Rank = 4});
            Util.RemoveEmptyRanks(graph);
            await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
            await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(1);
        }

        [Test]
        public async Task DoesNotRemoveNonBorderRanks()
        {
            var graph = new Graph();
            graph.SetGraph(new() {NodeRankFactor = 4});
            graph.SetNode("a", new() {Rank = 0});
            graph.SetNode("b", new() {Rank = 8});
            Util.RemoveEmptyRanks(graph);
            await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
            await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(2);
        }

        [Test]
        public async Task HandlesParentsWithUndefinedRanks()
        {
            var graph = new Graph(compound: true);
            graph.SetGraph(new() {NodeRankFactor = 3});
            graph.SetNode("a", new() {Rank = 0});
            graph.SetNode("b", new() {Rank = 6});
            graph.SetNode("sg", new());
            graph.SetParent("a", "sg");
            Util.RemoveEmptyRanks(graph);
            await Assert.That(graph.NodeLabel("a").Rank).IsEqualTo(0);
            await Assert.That(graph.NodeLabel("b").Rank).IsEqualTo(2);
            await Assert.That(graph.NodeLabel("sg").Rank).IsNull();
        }
    }

    public class RangeTests
    {
        [Test]
        public async Task BuildsAnArrayToTheLimit()
        {
            var range = Util.Range(4);
            await Assert.That(range.Count).IsEqualTo(4);
            await Assert.That(range.Aggregate((acc, v) => acc + v)).IsEqualTo(6);
        }

        [Test]
        public async Task BuildsAnArrayWithAStart()
        {
            var range = Util.Range(2, 4);
            await Assert.That(range.Count).IsEqualTo(2);
            await Assert.That(range.Aggregate((acc, v) => acc + v)).IsEqualTo(5);
        }

        [Test]
        public async Task BuildsAnArrayWithANegativeStep()
        {
            var range = Util.Range(5, -1, -1);
            await Assert.That(range[0]).IsEqualTo(5);
            await Assert.That(range[5]).IsEqualTo(0);
        }
    }
}
