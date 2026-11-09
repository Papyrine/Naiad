static class EdgePath
{
    // Builds a smooth edge path through a set of routed waypoints using a cubic B-spline (d3 curveBasis):
    // the interior waypoints are approximated rather than interpolated, so zig-zagging dummy waypoints
    // produce a gentle curve instead of an S-squiggle. The curve still starts at the first point and ends at
    // the last (so end markers align). A two-point edge stays a straight line. Shared by the graph diagrams
    // (Flowchart, ER, …) that render Dagre-routed edges.
    public static string Build(IReadOnlyList<Position> points)
    {
        var path = new StringBuilder();

        static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        if (points.Count == 2)
        {
            path.Append(CultureInfo.InvariantCulture, $"M{F(points[0].X)},{F(points[0].Y)} L{F(points[1].X)},{F(points[1].Y)}");
            return path.ToString();
        }

        double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
        var stage = 0;

        void Basis(double x, double y) =>
            path.Append(
                CultureInfo.InvariantCulture,
                $" C{F((2 * x0 + x1) / 3)},{F((2 * y0 + y1) / 3)} {F((x0 + 2 * x1) / 3)},{F((y0 + 2 * y1) / 3)} {F((x0 + 4 * x1 + x) / 6)},{F((y0 + 4 * y1 + y) / 6)}");

        foreach (var point in points)
        {
            var x = point.X;
            var y = point.Y;
            switch (stage)
            {
                case 0:
                    stage = 1;
                    path.Append(CultureInfo.InvariantCulture, $"M{F(x)},{F(y)}");
                    break;
                case 1:
                    stage = 2;
                    break;
                case 2:
                    stage = 3;
                    path.Append(CultureInfo.InvariantCulture, $" L{F((5 * x0 + x1) / 6)},{F((5 * y0 + y1) / 6)}");
                    Basis(x, y);
                    break;
                default:
                    Basis(x, y);
                    break;
            }

            x0 = x1;
            x1 = x;
            y0 = y1;
            y1 = y;
        }

        if (stage == 3)
        {
            Basis(x1, y1);
        }

        path.Append(CultureInfo.InvariantCulture, $" L{F(x1)},{F(y1)}");
        return path.ToString();
    }
}
