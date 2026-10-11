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

    [IterationSetup]
    public void Setup()
    {
        small = BuildLinearChain(5);
        medium = BuildDiamondGraph(20);
        large = BuildDiamondGraph(100);
        wide = BuildLayeredGraph(layers: 8, perLayer: 6);
        huge = BuildLayeredGraph(layers: 40, perLayer: 10);
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

    static FlowchartModel BuildLayeredGraph(int layers, int perLayer)
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
                model.AddEdge(new()
                {
                    SourceId = $"n{layer}_{index}",
                    TargetId = $"n{layer + 1}_{index}"
                });
                if (index + 1 < perLayer)
                {
                    model.AddEdge(new()
                    {
                        SourceId = $"n{layer}_{index}",
                        TargetId = $"n{layer + 1}_{index + 1}"
                    });
                }
            }
        }
        return model;
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
