using System.Net;
using System.Numerics;
using Naiad.Diagrams.Class;
using Naiad.Diagrams.EntityRelationship;
using Naiad.Diagrams.Flowchart;
using Naiad.Diagrams.State;

/// <summary>
/// What a graph diagram drew, reduced to the boxes and lines the layout checks read, all in the layout's
/// own coordinates. Nodes and clusters are kept apart: a line must not run through a node other than its
/// own ends, where it crosses a cluster (a subgraph, namespace or composite state) to reach what is inside.
/// </summary>
record DrawnLayout(
    Rect Canvas,
    List<DrawnNode> Nodes,
    List<DrawnCluster> Clusters,
    List<DrawnEdge> Edges,
    List<DrawnLabel> Labels);

// Outline is the shape's border as a closed polygon, which for most shapes is the corners of the box.
record DrawnNode(string Id, Rect Box, Position[] Outline);

record DrawnCluster(string Id, Rect Box);

// The line as drawn, with any curve flattened to a polyline.
record DrawnEdge(string SourceId, string TargetId, List<Position> Points);

record DrawnLabel(string Text, Rect Box);

/// <summary>
/// Renders a Flowchart, Class, ER or State diagram and reads back what it drew. Each renderer is driven
/// directly, rather than through <see cref="Mermaid.Render"/>, because the geometry lives in the models
/// it lays out and those are only reachable from outside when the caller supplies them.
/// </summary>
static partial class DrawnLayouts
{
    public static DrawnLayout Render(string input)
    {
        // Front matter, init blocks and comments ahead of the diagram's keyword are skipped the way
        // rendering skips them. None of these four renderers reads the configuration they carry.
        if (!Mermaid.TryFindDiagram(input, out var type, out var start))
        {
            throw new($"No diagram found in: {input}");
        }

        var body = input[start..];
        var options = RenderOptions.Default;
        return type switch
        {
            DiagramType.Flowchart => RenderFlowchart(body, options),
            DiagramType.Class => RenderClass(body, options),
            DiagramType.EntityRelationship => RenderEntityRelationship(body, options),
            DiagramType.State => RenderState(body, options),
            _ => throw new($"The layout lint does not cover {type} diagrams")
        };
    }

    static DrawnLayout RenderFlowchart(string input, RenderOptions options)
    {
        var model = Parse(new FlowchartParser(), input);
        var document = new FlowchartRenderer().Render(model, options);

        // An invisible link takes part in the layout but is never drawn.
        var edges = model.Edges
            .Where(_ => _.Type != EdgeType.Invisible)
            .Select(Curve)
            .OfType<DrawnEdge>()
            .ToList();

        return new(
            Canvas(document),
            model.Nodes.Select(_ => new DrawnNode(_.Id, Box(_), FlowchartOutline(_))).ToList(),
            Clusters(model.Subgraphs),
            edges,
            Labels(document));
    }

    static DrawnLayout RenderClass(string input, RenderOptions options)
    {
        var model = Parse(new ClassParser(), input);
        var layout = new RecordingLayoutEngine();
        var document = new ClassRenderer(layout).Render(model, options);
        var graph = layout.Graph!;

        // The graph holds a node for each class and each note, a cluster for each namespace, and an edge
        // for each relationship and each note's connector.
        return new(
            Canvas(document),
            graph.Nodes.Select(BoxNode).ToList(),
            Clusters(graph.Subgraphs),
            graph.Edges.Select(Curve).OfType<DrawnEdge>().ToList(),
            Labels(document));
    }

    static DrawnLayout RenderEntityRelationship(string input, RenderOptions options)
    {
        var model = Parse(new ERParser(), input);
        var layout = new RecordingLayoutEngine();
        var document = new ERRenderer(layout).Render(model, options);
        var graph = layout.Graph!;

        // A self-relationship ignores its route and is drawn as a fixed loop off the entity's right side,
        // so its route says nothing about what was drawn. Its label is still read from the drawing.
        var edges = graph.Edges
            .Where(_ => _.SourceId != _.TargetId)
            .Select(Curve)
            .OfType<DrawnEdge>()
            .ToList();

        return new(
            Canvas(document),
            graph.Nodes.Select(BoxNode).ToList(),
            [],
            edges,
            Labels(document));
    }

    // The State renderer routes its transitions itself, after the layout, so their lines are known only
    // to the renderer. Its self-check bookkeeping records every box and segment it draws, by id.
    static DrawnLayout RenderState(string input, RenderOptions options)
    {
        var model = Parse(new StateParser(), input);
        var renderer = new StateRenderer
        {
            TrackLayout = true
        };
        var document = renderer.Render(model, options);

        var states = new Dictionary<string, State>(StringComparer.Ordinal);
        AddStates(states, model.States);

        var nodes = new List<DrawnNode>();
        var clusters = new List<DrawnCluster>();
        foreach (var tracked in renderer.TrackedNodes)
        {
            var box = new Rect(tracked.X, tracked.Y, tracked.Width, tracked.Height);
            var id = tracked.Id ?? tracked.Label;
            states.TryGetValue(id, out var state);

            // A composite (a region included) is a container its contents are drawn inside.
            if (state is { IsComposite: true })
            {
                clusters.Add(new(id, box));
                continue;
            }

            nodes.Add(new(id, box, StateOutline(state, box)));
        }

        return new(Canvas(document), nodes, clusters, Transitions(renderer.TrackedLines), Labels(document));
    }

    static void AddStates(Dictionary<string, State> states, List<State> nested)
    {
        foreach (var state in nested)
        {
            states.TryAdd(state.Id, state);
            AddStates(states, state.NestedStates);
        }
    }

    // A transition is tracked as the run of segments it was drawn with, one after another, so a run of
    // segments between the same two states, each starting where the last ended, is one transition.
    static List<DrawnEdge> Transitions(IReadOnlyList<StateRenderer.LineBounds> lines)
    {
        var edges = new List<DrawnEdge>();
        StateRenderer.LineBounds? previous = null;
        foreach (var line in lines)
        {
            var start = new Position(line.X1, line.Y1);
            var continues = previous is not null &&
                            previous.FromId == line.FromId &&
                            previous.ToId == line.ToId &&
                            previous.Label == line.Label &&
                            Math.Abs(previous.X2 - line.X1) < 0.01 &&
                            Math.Abs(previous.Y2 - line.Y1) < 0.01;
            if (!continues)
            {
                edges.Add(new(line.FromId ?? "", line.ToId ?? "", [start]));
            }

            edges[^1].Points.Add(new(line.X2, line.Y2));
            previous = line;
        }

        return edges;
    }

    // The outline the State renderer clips transitions to: a circle for the start and end markers, a
    // diamond for a choice, and the box for the rest.
    static Position[] StateOutline(State? state, Rect box)
    {
        var center = new Position(box.X + box.Width / 2, box.Y + box.Height / 2);
        return state?.Type switch
        {
            StateType.Start or StateType.End => Circle(center, Math.Min(box.Width, box.Height) / 2),
            StateType.Choice => Diamond(center, box.Width / 2, box.Height / 2),
            _ => Corners(box)
        };
    }

    // The outline FlowchartRenderer.ClipToShape pulls each edge end in to: a diamond, a circle, a
    // hexagon, the polygon the shape generator gives, or the box for the shapes that fill it.
    static Position[] FlowchartOutline(Node node)
    {
        var box = Box(node);
        var center = node.Position;
        var halfWidth = node.Width / 2;
        var halfHeight = node.Height / 2;
        return node.Shape switch
        {
            NodeShape.Diamond => Diamond(center, halfWidth, halfHeight),
            NodeShape.Circle or
                NodeShape.DoubleCircle or
                NodeShape.SmallCircle or
                NodeShape.FramedCircle or
                NodeShape.FilledCircle or
                NodeShape.CrossedCircle => Circle(center, Math.Min(halfWidth, halfHeight)),
            NodeShape.Hexagon =>
            [
                new(center.X - halfWidth, center.Y),
                new(center.X - halfWidth / 2, center.Y - halfHeight),
                new(center.X + halfWidth / 2, center.Y - halfHeight),
                new(center.X + halfWidth, center.Y),
                new(center.X + halfWidth / 2, center.Y + halfHeight),
                new(center.X - halfWidth / 2, center.Y + halfHeight)
            ],
            _ => ShapePathGenerator.Outline(node.Shape, box.X, box.Y, box.Width, box.Height) ?? Corners(box)
        };
    }

    static Position[] Diamond(Position center, double halfWidth, double halfHeight) =>
    [
        new(center.X, center.Y - halfHeight),
        new(center.X + halfWidth, center.Y),
        new(center.X, center.Y + halfHeight),
        new(center.X - halfWidth, center.Y)
    ];

    static Position[] Circle(Position center, double radius) =>
        Enumerable.Range(0, 48)
            .Select(_ => _ * Math.PI / 24)
            .Select(_ => new Position(center.X + radius * Math.Cos(_), center.Y + radius * Math.Sin(_)))
            .ToArray();

    public static Position[] Corners(Rect box) =>
    [
        new(box.Left, box.Top),
        new(box.Right, box.Top),
        new(box.Right, box.Bottom),
        new(box.Left, box.Bottom)
    ];

    static Rect Box(Node node) =>
        new(node.Position.X - node.Width / 2, node.Position.Y - node.Height / 2, node.Width, node.Height);

    static DrawnNode BoxNode(Node node) =>
        new(node.Id, Box(node), Corners(Box(node)));

    static List<DrawnCluster> Clusters(List<Subgraph> subgraphs)
    {
        var clusters = new List<DrawnCluster>();
        foreach (var subgraph in subgraphs)
        {
            clusters.Add(new(subgraph.Id, subgraph.Bounds));
            clusters.AddRange(Clusters(subgraph.NestedSubgraphs));
        }

        return clusters;
    }

    // The graph diagrams draw an edge as a B-spline through its routed points (EdgePath), which cuts the
    // corners the route turns. A node near a corner can be clear of the route and under the curve, or the
    // other way round, so the checks read the curve, flattened by the same code the PNG backends use.
    static DrawnEdge? Curve(Edge edge)
    {
        if (edge.Points.Count < 2)
        {
            return null;
        }

        var points = PathFlattener.Flatten(EdgePath.Build(edge.Points))
            .SelectMany(_ => _.Points)
            .Select(_ => new Position(_.X, _.Y))
            .ToList();
        return new(edge.SourceId, edge.TargetId, points);
    }

    // The label chips as drawn: every diagram paints one behind each edge label, and paints the label's
    // text straight after it.
    static List<DrawnLabel> Labels(SvgDocument document)
    {
        var labels = new List<DrawnLabel>();
        AddLabels(labels, document.Elements);
        return labels;
    }

    static void AddLabels(List<DrawnLabel> labels, List<SvgElement> elements)
    {
        for (var index = 0; index < elements.Count; index++)
        {
            switch (elements[index])
            {
                case SvgGroup group:
                    AddLabels(labels, group.Children);
                    break;
                case SvgRect { Class: "edgeLabel" } chip:
                    var text = "";
                    if (index + 1 < elements.Count)
                    {
                        text = elements[index + 1] switch
                        {
                            SvgText label => label.Content,
                            SvgForeignObject label => label.HtmlContent,
                            _ => ""
                        };
                    }

                    labels.Add(new(WebUtility.HtmlDecode(TagRegex().Replace(text, "")), new(chip.X, chip.Y, chip.Width, chip.Height)));
                    break;
            }
        }
    }

    // The canvas is the viewBox. The drawing sits inside groups that only move it, one for the padding and
    // one more for a title band, so taking their offsets off puts the viewBox in the layout's coordinates.
    static Rect Canvas(SvgDocument document)
    {
        var viewBox = document.ViewBox
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(_ => double.Parse(_, CultureInfo.InvariantCulture))
            .ToArray();

        var offset = Vector2.Zero;
        var elements = document.Elements;
        while (elements is [SvgGroup { Id: null, Class: null, Transform: { } transform } wrapper, ..])
        {
            offset += SvgTransform.Parse(transform).Translation;
            elements = wrapper.Children;
        }

        return new(viewBox[0] - offset.X, viewBox[1] - offset.Y, viewBox[2], viewBox[3]);
    }

    static TModel Parse<TModel>(IDiagramParser<TModel> parser, string input)
        where TModel : DiagramBase
    {
        var result = parser.Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}

/// <summary>
/// Lays out with dagre, as the renderers do by default, and keeps the graph it was given. The Class and
/// ER renderers lay out a graph of their own making from the model they are handed, and this is the one
/// way to read its geometry back once they are done.
/// </summary>
class RecordingLayoutEngine : ILayoutEngine
{
    readonly DagreEngine dagre = new();

    public GraphDiagramBase? Graph { get; private set; }

    public LayoutResult BuildLayout(GraphDiagramBase diagram, LayoutOptions options)
    {
        Graph = diagram;
        return dagre.BuildLayout(diagram, options);
    }
}
