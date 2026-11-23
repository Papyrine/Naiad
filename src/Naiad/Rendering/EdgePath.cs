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

    /// <summary>
    /// The curve <see cref="Build"/> draws through <paramref name="points"/>, as a polyline: the same line and
    /// cubic segments, each cubic cut into <paramref name="steps"/> straight pieces. For testing what the
    /// drawn edge passes through, which the waypoints alone do not tell, since the curve cuts their corners.
    /// </summary>
    internal static List<Position> Flatten(IReadOnlyList<Position> points, int steps = 12)
    {
        var flat = new List<Position>();
        if (points.Count < 3)
        {
            flat.AddRange(points);
            return flat;
        }

        flat.Add(points[0]);
        var current = Mix(points[0], 5, points[1], 1, 6);
        flat.Add(current);

        void Cubic(Position a, Position b, Position next)
        {
            var control1 = Mix(a, 2, b, 1, 3);
            var control2 = Mix(a, 1, b, 2, 3);
            var end = new Position((a.X + 4 * b.X + next.X) / 6, (a.Y + 4 * b.Y + next.Y) / 6);
            for (var step = 1; step <= steps; step++)
            {
                var t = (double) step / steps;
                var u = 1 - t;
                flat.Add(
                    new(
                        u * u * u * current.X + 3 * u * u * t * control1.X + 3 * u * t * t * control2.X + t * t * t * end.X,
                        u * u * u * current.Y + 3 * u * u * t * control1.Y + 3 * u * t * t * control2.Y + t * t * t * end.Y));
            }

            current = end;
        }

        for (var i = 2; i < points.Count; i++)
        {
            Cubic(points[i - 2], points[i - 1], points[i]);
        }

        Cubic(points[^2], points[^1], points[^1]);
        flat.Add(points[^1]);
        return flat;
    }

    static Position Mix(Position a, double weightA, Position b, double weightB, double total) =>
        new((weightA * a.X + weightB * b.X) / total, (weightA * a.Y + weightB * b.Y) / total);
}
