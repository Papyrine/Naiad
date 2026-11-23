/// <summary>
/// The limits and the cancellation of the render running on this thread. <see cref="Mermaid"/> opens a scope
/// for the length of a render, so the stages deep inside it, the graph layout above all, keep to them without
/// every renderer handing them down. A render is synchronous, on the thread that asked for it. Outside a
/// scope (a renderer driven on its own, as tests do) nothing is capped and nothing can be cancelled.
/// </summary>
static class RenderScope
{
    [ThreadStatic]
    static RenderLimits? limits;

    [ThreadStatic]
    static Cancel cancel;

    internal static Scope Enter(RenderLimits limits, Cancel cancel)
    {
        var scope = new Scope(RenderScope.limits, RenderScope.cancel);
        RenderScope.limits = limits;
        RenderScope.cancel = cancel;
        return scope;
    }

    internal static void ThrowIfCancelled() =>
        cancel.ThrowIfCancellationRequested();

    internal static void CheckInput(int length)
    {
        if (limits is null ||
            length <= limits.MaxInputLength)
        {
            return;
        }

        throw new MermaidLimitException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"The diagram is {length:N0} characters long, more than the {limits.MaxInputLength:N0} RenderOptions.Limits.MaxInputLength allows"));
    }

    // Before a graph is laid out: its nodes, its edges and how deep its subgraphs nest.
    internal static void CheckGraph(GraphDiagramBase diagram)
    {
        ThrowIfCancelled();
        if (limits is null)
        {
            return;
        }

        if (diagram.Nodes.Count > limits.MaxNodes)
        {
            throw new MermaidLimitException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The diagram has {diagram.Nodes.Count:N0} nodes, more than the {limits.MaxNodes:N0} RenderOptions.Limits.MaxNodes allows"));
        }

        if (diagram.Edges.Count > limits.MaxEdges)
        {
            throw new MermaidLimitException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The diagram has {diagram.Edges.Count:N0} edges, more than the {limits.MaxEdges:N0} RenderOptions.Limits.MaxEdges allows"));
        }

        // Walked with a stack of its own, as the nesting is what is being measured.
        var pending = new Stack<(Subgraph Subgraph, int Depth)>();
        foreach (var subgraph in diagram.Subgraphs)
        {
            pending.Push((subgraph, 1));
        }

        while (pending.TryPop(out var entry))
        {
            CheckSubgraphDepth(entry.Depth);
            foreach (var nested in entry.Subgraph.NestedSubgraphs)
            {
                pending.Push((nested, entry.Depth + 1));
            }
        }
    }

    // For a renderer that lays its subgraphs out one level at a time, which the graph above cannot see.
    internal static void CheckSubgraphDepth(int depth)
    {
        if (limits is null ||
            depth <= limits.MaxSubgraphDepth)
        {
            return;
        }

        throw new MermaidLimitException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"The diagram's subgraphs nest more than the {limits.MaxSubgraphDepth:N0} deep RenderOptions.Limits.MaxSubgraphDepth allows"));
    }

    internal readonly struct Scope(RenderLimits? previousLimits, Cancel previousCancel) :
        IDisposable
    {
        public void Dispose()
        {
            limits = previousLimits;
            cancel = previousCancel;
        }
    }
}
