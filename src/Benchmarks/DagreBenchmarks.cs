using Naiad;

namespace Benchmarks;

[MemoryDiagnoser]
public class DagreBenchmarks
{
    static readonly LayoutOptions Options = new()
    {
        Direction = Direction.TopToBottom,
        NodeSeparation = 50,
        RankSeparation = 70
    };

    FlowchartModel small = null!;
    FlowchartModel medium = null!;
    FlowchartModel large = null!;
    FlowchartModel wide = null!;
    FlowchartModel huge = null!;
    FlowchartModel nested = null!;
    FlowchartModel nestedLarge = null!;
    FlowchartModel cycles = null!;
    FlowchartModel labelled = null!;
    FlowchartModel longEdges = null!;
    FlowchartModel layeredLarge = null!;
    FlowchartModel nestedHuge = null!;

    // Built once rather than before every iteration. A layout reads only the sizes, labels and links of
    // what it is given, and overwrites every position, route and subgraph box it writes, so laying the
    // same model out again does the same work. Set up per iteration, the cases that take microseconds
    // were measured as a single call each, which is mostly timer noise.
    [GlobalSetup]
    public void Setup()
    {
        small = BuildLinearChain(5);
        medium = BuildDiamondGraph(20);
        large = BuildDiamondGraph(100);
        wide = BuildLayeredGraph(layers: 8, perLayer: 6);
        huge = BuildLayeredGraph(layers: 40, perLayer: 10);
        nested = BuildNestedSubgraphs(outer: 6, inner: 3, perInner: 8);
        nestedLarge = BuildNestedSubgraphs(outer: 12, inner: 4, perInner: 10);
        cycles = BuildCyclicGraph(layers: 30, perLayer: 12);
        labelled = BuildLayeredGraph(layers: 20, perLayer: 12, labelled: true);
        longEdges = BuildLongEdges(count: 300);
        layeredLarge = BuildLayeredGraph(layers: 100, perLayer: 20);
        nestedHuge = BuildNestedSubgraphs(outer: 20, inner: 5, perInner: 20);
    }

    [Benchmark]
    public LayoutResult Layout_Linear_5() =>
        new DagreEngine().BuildLayout(small, Options);

    [Benchmark]
    public LayoutResult Layout_Diamond_20() =>
        new DagreEngine().BuildLayout(medium, Options);

    [Benchmark]
    public LayoutResult Layout_Diamond_100() =>
        new DagreEngine().BuildLayout(large, Options);

    [Benchmark]
    public LayoutResult Layout_Layered_8x6() =>
        new DagreEngine().BuildLayout(wide, Options);

    // Four hundred nodes. Costs that grow faster than the graph - a lookup that allocates, a list rebuilt
    // for every edge - are lost in the noise at the sizes above and are most of the time at this one.
    [Benchmark]
    public LayoutResult Layout_Layered_40x10() =>
        new DagreEngine().BuildLayout(huge, Options);

    // Two thousand nodes in the same shape. Ranking makes well over a thousand pivots here, so whatever a
    // pivot costs for each node or edge of the graph is most of the layout.
    [Benchmark]
    public LayoutResult Layout_Layered_100x20() =>
        new DagreEngine().BuildLayout(layeredLarge, Options);

    // Every case above has no subgraphs, so it takes the ordering's shortcut, and ranking meets no nesting
    // but the root that ties the graph together. These have subgraphs inside subgraphs, which give the
    // ranking border nodes, nesting edges and much larger minimum lengths to work through, and send the
    // ordering the long way round. Six groups of three groups of eight is 144 nodes and 24 subgraphs.
    [Benchmark]
    public LayoutResult Layout_Nested_6x3x8() =>
        new DagreEngine().BuildLayout(nested, Options);

    // Twelve groups of four groups of ten: 480 nodes and 60 subgraphs.
    [Benchmark]
    public LayoutResult Layout_Nested_12x4x10() =>
        new DagreEngine().BuildLayout(nestedLarge, Options);

    // Twenty groups of five groups of twenty: 2,000 nodes and 120 subgraphs.
    [Benchmark]
    public LayoutResult Layout_Nested_20x5x20() =>
        new DagreEngine().BuildLayout(nestedHuge, Options);

    // 360 nodes with links back up the ranks, which the layout first has to turn round.
    [Benchmark]
    public LayoutResult Layout_Cycles_30x12() =>
        new DagreEngine().BuildLayout(cycles, Options);

    // 240 nodes and every link labelled: each label takes a rank of its own, half way along its link.
    [Benchmark]
    public LayoutResult Layout_Labelled_20x12() =>
        new DagreEngine().BuildLayout(labelled, Options);

    // A chain of 300 nodes, each also linked to a node up to 45 further on. The long links cross many
    // ranks, and are drawn through a dummy node at every one: several thousand of them.
    [Benchmark]
    public LayoutResult Layout_LongEdges_300() =>
        new DagreEngine().BuildLayout(longEdges, Options);

    static FlowchartModel BuildLinearChain(int count)
    {
        var model = new FlowchartModel { Direction = Direction.LeftToRight };
        for (var i = 0; i < count; i++)
        {
            model.AddNode(MakeNode($"n{i}"));
        }
        for (var i = 0; i < count - 1; i++)
        {
            model.AddEdge(new() { SourceId = $"n{i}", TargetId = $"n{i + 1}" });
        }
        return model;
    }

    static FlowchartModel BuildDiamondGraph(int count)
    {
        var model = new FlowchartModel { Direction = Direction.TopToBottom };
        for (var i = 0; i < count; i++)
        {
            model.AddNode(MakeNode($"n{i}"));
        }
        for (var i = 0; i < count - 1; i++)
        {
            model.AddEdge(new() { SourceId = $"n{i}", TargetId = $"n{i + 1}" });
            if (i + 2 < count)
            {
                model.AddEdge(new() { SourceId = $"n{i}", TargetId = $"n{i + 2}" });
            }
        }
        return model;
    }

    static FlowchartModel BuildLayeredGraph(int layers, int perLayer, bool labelled = false)
    {
        var model = new FlowchartModel { Direction = Direction.TopToBottom };
        for (var layer = 0; layer < layers; layer++)
        {
            for (var index = 0; index < perLayer; index++)
            {
                model.AddNode(MakeNode($"n{layer}_{index}"));
            }
        }
        for (var layer = 0; layer < layers - 1; layer++)
        {
            for (var index = 0; index < perLayer; index++)
            {
                model.AddEdge(MakeEdge($"n{layer}_{index}", $"n{layer + 1}_{index}", labelled));
                if (index + 1 < perLayer)
                {
                    model.AddEdge(MakeEdge($"n{layer}_{index}", $"n{layer + 1}_{index + 1}", labelled));
                }
            }
        }
        return model;
    }

    // Groups of groups of nodes. Inside each inner group the nodes run in a chain with a branch every
    // third node; each inner group leads to the next one in its outer group, the outer groups lead one to
    // the next, and every outer group has a link that skips over the one after it.
    static FlowchartModel BuildNestedSubgraphs(int outer, int inner, int perInner)
    {
        var model = new FlowchartModel { Direction = Direction.TopToBottom };
        for (var o = 0; o < outer; o++)
        {
            var outerGroup = new Subgraph { Id = $"outer{o}", Title = $"Outer {o}" };
            for (var i = 0; i < inner; i++)
            {
                var innerGroup = new Subgraph { Id = $"inner{o}_{i}", Title = $"Inner {o}-{i}" };
                for (var n = 0; n < perInner; n++)
                {
                    var id = $"n{o}_{i}_{n}";
                    model.AddNode(MakeNode(id));
                    innerGroup.NodeIds.Add(id);
                }

                for (var n = 1; n < perInner; n++)
                {
                    model.AddEdge(new() { SourceId = $"n{o}_{i}_{n - 1}", TargetId = $"n{o}_{i}_{n}" });
                    if (n % 3 == 0 &&
                        n + 1 < perInner)
                    {
                        model.AddEdge(new() { SourceId = $"n{o}_{i}_{n - 2}", TargetId = $"n{o}_{i}_{n + 1}" });
                    }
                }

                if (i > 0)
                {
                    model.AddEdge(new() { SourceId = $"n{o}_{i - 1}_{perInner - 1}", TargetId = $"n{o}_{i}_0" });
                    model.AddEdge(new() { SourceId = $"n{o}_{i - 1}_{perInner / 2}", TargetId = $"n{o}_{i}_{perInner / 2}" });
                }

                outerGroup.NestedSubgraphs.Add(innerGroup);
            }

            model.Subgraphs.Add(outerGroup);
            if (o > 0)
            {
                model.AddEdge(new() { SourceId = $"n{o - 1}_{inner - 1}_{perInner - 1}", TargetId = $"n{o}_0_0" });
            }

            if (o > 1)
            {
                model.AddEdge(new() { SourceId = $"n{o - 2}_0_{perInner / 2}", TargetId = $"n{o}_{inner - 1}_1" });
            }
        }

        return model;
    }

    // A layered graph whose last node in every few links back to a node a few layers up.
    static FlowchartModel BuildCyclicGraph(int layers, int perLayer)
    {
        var model = BuildLayeredGraph(layers, perLayer);
        for (var layer = 3; layer < layers; layer++)
        {
            for (var index = layer % 4; index < perLayer; index += 4)
            {
                model.AddEdge(new() { SourceId = $"n{layer}_{index}", TargetId = $"n{layer - 3}_{(index + 1) % perLayer}" });
            }
        }

        return model;
    }

    static FlowchartModel BuildLongEdges(int count)
    {
        var model = BuildLinearChain(count);
        model.Direction = Direction.TopToBottom;
        for (var i = 0; i < count; i++)
        {
            var target = i + 5 + i * 7 % 41;
            if (target < count)
            {
                model.AddEdge(new() { SourceId = $"n{i}", TargetId = $"n{target}" });
            }
        }

        return model;
    }

    static Edge MakeEdge(string source, string target, bool labelled)
    {
        if (!labelled)
        {
            return new() { SourceId = source, TargetId = target };
        }

        return new()
        {
            SourceId = source,
            TargetId = target,
            Label = "label",
            LabelWidth = 40,
            LabelHeight = 16
        };
    }

    static Node MakeNode(string id) =>
        new()
        {
            Id = id,
            Label = id,
            Width = 80,
            Height = 40
        };
}
