namespace Naiad.Diagrams.Treemap;

public class TreemapNode
{
    public required string Name { get; init; }
    public double? Value { get; set; }
    public List<TreemapNode> Children { get; } = [];
    public string? CssClass { get; set; }

    public bool IsLeaf => Children.Count == 0;

    public double TotalValue
    {
        get
        {
            if (IsLeaf)
            {
                return Value ?? 0;
            }

            // Summed bottom up over a list of the subtree rather than by recursing into the children:
            // nesting is as deep as the input's indentation, and a recursion that deep overflows the stack,
            // which cannot be caught and takes the process down.
            var nodes = new List<TreemapNode>();
            var pending = new Stack<TreemapNode>();
            pending.Push(this);
            while (pending.TryPop(out var node))
            {
                nodes.Add(node);
                foreach (var child in node.Children)
                {
                    pending.Push(child);
                }
            }

            // Every node comes after its parent in the list, so walking it backwards totals the children
            // before the node that sums them.
            var totals = new Dictionary<TreemapNode, double>(nodes.Count);
            for (var i = nodes.Count - 1; i >= 0; i--)
            {
                var node = nodes[i];
                if (node.IsLeaf)
                {
                    totals[node] = node.Value ?? 0;
                    continue;
                }

                double total = 0;
                foreach (var child in node.Children)
                {
                    total += totals[child];
                }

                totals[node] = total;
            }

            return totals[this];
        }
    }
}