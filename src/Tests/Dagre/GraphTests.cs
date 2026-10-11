public class GraphTests
{
    public class FindEdgeLabelTests
    {
        [Test]
        public async Task ThrowsWhenTheEdgeDoesNotExist()
        {
            var graph = new Graph();
            graph.SetNode("a");
            graph.SetNode("b");

            await Assert.That(() => graph.FindEdgeLabel("a", "b")).Throws<KeyNotFoundException>();
        }

        [Test]
        public async Task ThrowsForTheEdgeOverloadWhenTheEdgeDoesNotExist()
        {
            var graph = new Graph();

            await Assert.That(() => graph.FindEdgeLabel(new("a", "b"))).Throws<KeyNotFoundException>();
        }

        [Test]
        public async Task ReturnsTheLabelWhenTheEdgeExists()
        {
            var graph = new Graph();
            var label = new EdgeLabel { Weight = 2 };
            graph.SetEdge("a", "b", label);

            await Assert.That(graph.FindEdgeLabel("a", "b")).IsSameReferenceAs(label);
        }
    }

    public class TryGetEdgeLabelTests
    {
        [Test]
        public async Task ReturnsFalseWhenTheEdgeDoesNotExist()
        {
            var graph = new Graph();
            graph.SetNode("a");
            graph.SetNode("b");

            await Assert.That(graph.TryGetEdgeLabel("a", "b", out _)).IsFalse();
        }

        [Test]
        public async Task ReturnsTrueWithTheLabelWhenTheEdgeExists()
        {
            var graph = new Graph();
            var label = new EdgeLabel { Weight = 2 };
            graph.SetEdge("a", "b", label);

            await Assert.That(graph.TryGetEdgeLabel("a", "b", out var found)).IsTrue();
            await Assert.That(found).IsSameReferenceAs(label);
        }
    }

    public class NodeLabelTests
    {
        [Test]
        public async Task ThrowsWhenTheNodeDoesNotExist()
        {
            var graph = new Graph();

            await Assert.That(() => graph.NodeLabel("a")).Throws<KeyNotFoundException>();
        }

        [Test]
        public async Task ThrowsWhenTheNodeExistsButHasNoLabel()
        {
            var graph = new Graph();
            graph.SetNode("a"); // no label → the default node-label fn yields null

            await Assert.That(graph.HasNode("a")).IsTrue();
            await Assert.That(() => graph.NodeLabel("a")).Throws<KeyNotFoundException>();
        }

        [Test]
        public async Task ReturnsTheLabelWhenTheNodeHasOne()
        {
            var graph = new Graph();
            var label = new NodeLabel { Width = 2 };
            graph.SetNode("a", label);

            await Assert.That(graph.NodeLabel("a")).IsSameReferenceAs(label);
        }
    }

    public class TryGetNodeLabelTests
    {
        [Test]
        public async Task ReturnsFalseWhenTheNodeDoesNotExist()
        {
            var graph = new Graph();

            await Assert.That(graph.TryGetNodeLabel("a", out _)).IsFalse();
        }

        [Test]
        public async Task ReturnsFalseWhenTheNodeExistsButHasNoLabel()
        {
            var graph = new Graph();
            graph.SetNode("a");

            await Assert.That(graph.TryGetNodeLabel("a", out _)).IsFalse();
        }

        [Test]
        public async Task ReturnsTrueWithTheLabelWhenTheNodeHasOne()
        {
            var graph = new Graph();
            var label = new NodeLabel { Width = 2 };
            graph.SetNode("a", label);

            await Assert.That(graph.TryGetNodeLabel("a", out var found)).IsTrue();
            await Assert.That(found).IsSameReferenceAs(label);
        }
    }

    // An unnamed edge is found by its ends without going through its id. These hold that second index
    // to the first: whatever is set, replaced or removed has to read the same either way.
    public class EdgeLookupTests
    {
        [Test]
        public async Task UnnamedEdge_IsFoundByItsEndsAndByItsKey()
        {
            var graph = new Graph();
            var label = new EdgeLabel { Weight = 3 };
            graph.SetEdge("a", "b", label);

            await Assert.That(graph.HasEdge("a", "b")).IsTrue();
            await Assert.That(graph.HasEdge("b", "a")).IsFalse();
            await Assert.That(graph.TryGetEdgeLabel("a", "b", out var found)).IsTrue();
            await Assert.That(found).IsSameReferenceAs(label);
            await Assert.That(graph.FindEdgeLabel(graph.Edges().Single())).IsSameReferenceAs(label);
            await Assert.That(graph.FindEdgeLabel(new EdgeKey("a", "b"))).IsSameReferenceAs(label);
        }

        [Test]
        public async Task ReplacingTheLabel_ReplacesItForBothLookups()
        {
            var graph = new Graph();
            graph.SetEdge("a", "b", new EdgeLabel { Weight = 1 });
            var replacement = new EdgeLabel { Weight = 2 };
            graph.SetEdge("a", "b", replacement);

            await Assert.That(graph.FindEdgeLabel("a", "b")).IsSameReferenceAs(replacement);
            await Assert.That(graph.FindEdgeLabel(graph.Edges().Single())).IsSameReferenceAs(replacement);
            await Assert.That(graph.Edges().Count).IsEqualTo(1);
        }

        [Test]
        public async Task RemovedEdge_IsGoneFromBothLookups()
        {
            var graph = new Graph();
            graph.SetEdge("a", "b", new EdgeLabel());
            graph.SetEdge("b", "c", new EdgeLabel());
            var key = graph.Edges()[0];

            graph.RemoveEdge("a", "b");

            await Assert.That(graph.HasEdge("a", "b")).IsFalse();
            await Assert.That(graph.TryGetEdgeLabel("a", "b", out _)).IsFalse();
            await Assert.That(() => graph.FindEdgeLabel(key)).Throws<KeyNotFoundException>();
            await Assert.That(graph.HasEdge("b", "c")).IsTrue();
        }

        [Test]
        public async Task RemovingANode_RemovesItsEdgesFromTheLookup()
        {
            var graph = new Graph();
            graph.SetEdge("a", "b", new EdgeLabel());
            graph.SetEdge("b", "c", new EdgeLabel());

            graph.RemoveNode("b");

            await Assert.That(graph.HasEdge("a", "b")).IsFalse();
            await Assert.That(graph.HasEdge("b", "c")).IsFalse();
        }

        [Test]
        public async Task UndirectedEdge_IsFoundFromEitherEnd()
        {
            var graph = new Graph(directed: false);
            var label = new EdgeLabel { Weight = 5 };
            graph.SetEdge("b", "a", label);

            await Assert.That(graph.HasEdge("a", "b")).IsTrue();
            await Assert.That(graph.HasEdge("b", "a")).IsTrue();
            await Assert.That(graph.FindEdgeLabel("a", "b")).IsSameReferenceAs(label);
            await Assert.That(graph.FindEdgeLabel("b", "a")).IsSameReferenceAs(label);

            graph.RemoveEdge("a", "b");
            await Assert.That(graph.HasEdge("b", "a")).IsFalse();
        }

        // In a multigraph a named edge between two nodes is not the unnamed one between them.
        [Test]
        public async Task NamedEdge_IsKeptApartFromTheUnnamedOne()
        {
            var graph = new Graph(multigraph: true);
            var named = new EdgeLabel { Weight = 1 };
            graph.SetEdge("a", "b", named, "first");

            await Assert.That(graph.HasEdge("a", "b")).IsFalse();
            await Assert.That(graph.HasEdge("a", "b", "first")).IsTrue();
            await Assert.That(graph.TryGetEdgeLabel("a", "b", out _)).IsFalse();

            var unnamed = new EdgeLabel { Weight = 2 };
            graph.SetEdge("a", "b", unnamed);
            await Assert.That(graph.FindEdgeLabel("a", "b")).IsSameReferenceAs(unnamed);
            await Assert.That(graph.FindEdgeLabel("a", "b", "first")).IsSameReferenceAs(named);

            graph.RemoveEdge("a", "b", "first");
            await Assert.That(graph.HasEdge("a", "b")).IsTrue();
            await Assert.That(graph.HasEdge("a", "b", "first")).IsFalse();
        }

        // A key remembers the id a graph looked it up by. That must not make it a different key, nor leak
        // from a directed graph into an undirected one, where the same ends give a different id.
        [Test]
        public async Task EdgeKey_EqualityIgnoresWhatItHasBeenLookedUpIn()
        {
            var directed = new Graph();
            directed.SetEdge("b", "a", new EdgeLabel());
            var undirected = new Graph(directed: false);
            var undirectedLabel = new EdgeLabel();
            undirected.SetEdge("a", "b", undirectedLabel);

            var used = new EdgeKey("b", "a");
            var fresh = new EdgeKey("b", "a");
            directed.FindEdgeLabel(used);

            await Assert.That(used).IsEqualTo(fresh);
            await Assert.That(used.GetHashCode()).IsEqualTo(fresh.GetHashCode());
            await Assert.That(new HashSet<EdgeKey> { used }.Contains(fresh)).IsTrue();
            await Assert.That(undirected.FindEdgeLabel(used)).IsSameReferenceAs(undirectedLabel);
            await Assert.That(new EdgeKey("a", "b", "x")).IsNotEqualTo(new EdgeKey("a", "b"));
        }
    }
}
