/// <summary>One path of a node's shape, and how it is painted.</summary>
readonly record struct ShapePart(string Path, ShapePaint Paint = ShapePaint.Body, double? Opacity = null);
