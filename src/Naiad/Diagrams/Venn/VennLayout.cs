namespace Naiad.Diagrams.Venn;

/// <summary>
/// A port of the parts of venn.js (@upsetjs/venn.js, which Mermaid draws Venn diagrams with) that lay the
/// circles out: a greedy placement refined by Nelder-Mead against the wanted overlap areas, then
/// normalised (largest circle first, the second one beside it), scaled into the drawing area, and a label
/// position found for every area by maximising its distance from the edges of the region. The
/// constrained-MDS initial layout venn.js also tries for eight or more areas is left out: it starts from
/// random points, so its output differs from run to run.
/// </summary>
static class VennLayout
{
    const double small = 1e-10;

    internal sealed class Circle(string setId, double x, double y, double radius)
    {
        public string SetId { get; } = setId;
        public double X { get; set; } = x;
        public double Y { get; set; } = y;
        public double Radius { get; set; } = radius;
        public double Size { get; init; }

        public Circle Copy() => new(SetId, X, Y, Radius) { Size = Size };
    }

    internal readonly record struct Point(double X, double Y);

    internal sealed record Arc(Circle Circle, double Width, Point P1, Point P2, bool Large);

    internal sealed record Area(IReadOnlyList<string> Sets, double Size)
    {
        public string Key => string.Join('|', Sets);
    }

    internal sealed record Result(List<Circle> Circles, Dictionary<string, Point> TextCentres)
    {
        public Circle Find(string setId) => Circles.First(_ => _.SetId == setId);
    }

    /// <summary>
    /// Lays out <paramref name="areas"/> (each set once, then any overlaps) in a box of the given size.
    /// A text centre is left out for an area that has no region on the drawing.
    /// </summary>
    public static Result Compute(IReadOnlyList<Area> areas, double width, double height, double padding)
    {
        var solution = Venn(areas);
        solution = Normalize(solution, Math.PI / 2);
        var circles = Scale(solution, width, height, padding);
        return new(circles, TextCentres(circles, areas));
    }

    static List<Circle> Venn(IReadOnlyList<Area> input)
    {
        var areas = AddMissingAreas(input);
        var circles = GreedyLayout(areas);

        var initial = new double[circles.Count * 2];
        for (var i = 0; i < circles.Count; i++)
        {
            initial[2 * i] = circles[i].X;
            initial[2 * i + 1] = circles[i].Y;
        }

        var lookup = circles.ToDictionary(_ => _.SetId, StringComparer.Ordinal);
        var solution = NelderMead(
            _ =>
            {
                for (var i = 0; i < circles.Count; i++)
                {
                    circles[i].X = _[2 * i];
                    circles[i].Y = _[2 * i + 1];
                }

                return Loss(lookup, areas);
            },
            initial,
            500,
            1e-6,
            1e-5);

        for (var i = 0; i < circles.Count; i++)
        {
            circles[i].X = solution[2 * i];
            circles[i].Y = solution[2 * i + 1];
        }

        return circles;
    }

    /// <summary>
    /// A pair of sets with no overlap given is meant to be drawn apart, so it is added with a size of 0;
    /// left unknown, the optimiser would let the circles overlap.
    /// </summary>
    static List<Area> AddMissingAreas(IReadOnlyList<Area> areas)
    {
        var result = areas.ToList();
        var ids = new List<string>();
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in areas)
        {
            if (area.Sets.Count == 1)
            {
                ids.Add(area.Sets[0]);
            }
            else if (area.Sets.Count == 2)
            {
                pairs.Add($"{area.Sets[0]};{area.Sets[1]}");
                pairs.Add($"{area.Sets[1]};{area.Sets[0]}");
            }
        }

        ids.Sort(StringComparer.Ordinal);
        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
            {
                if (!pairs.Contains($"{ids[i]};{ids[j]}"))
                {
                    result.Add(new([ids[i], ids[j]], 0));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Places the sets one at a time, most overlapped first, each at whichever candidate point (at a right
    /// angle from a placed set, or where its distances to two placed sets agree) gives the least loss.
    /// </summary>
    static List<Circle> GreedyLayout(List<Area> allAreas)
    {
        var circles = new List<Circle>();
        var lookup = new Dictionary<string, Circle>(StringComparer.Ordinal);
        var overlaps = new Dictionary<string, List<(string set, double size, double weight)>>(StringComparer.Ordinal);
        foreach (var area in allAreas.Where(_ => _.Sets.Count == 1))
        {
            var circle = new Circle(area.Sets[0], 1e10, 1e10, Math.Sqrt(area.Size / Math.PI))
            {
                Size = area.Size
            };
            if (lookup.TryAdd(circle.SetId, circle))
            {
                circles.Add(circle);
                overlaps[circle.SetId] = [];
            }
        }

        var areas = allAreas.Where(_ => _.Sets.Count == 2).ToList();
        foreach (var area in areas)
        {
            var left = area.Sets[0];
            var right = area.Sets[1];
            // A circle wholly inside another should not be placed early.
            var weight = area.Size + small >= Math.Min(lookup[left].Size, lookup[right].Size) ? 0 : 1.0;
            overlaps[left].Add((right, area.Size, weight));
            overlaps[right].Add((left, area.Size, weight));
        }

        var mostOverlapped = circles
            .Select(_ => (set: _.SetId, size: overlaps[_.SetId].Sum(overlap => overlap.size * overlap.weight)))
            .OrderByDescending(_ => _.size)
            .ToList();

        var positioned = new HashSet<string>(StringComparer.Ordinal);
        lookup[mostOverlapped[0].set].X = 0;
        lookup[mostOverlapped[0].set].Y = 0;
        positioned.Add(mostOverlapped[0].set);

        for (var i = 1; i < mostOverlapped.Count; i++)
        {
            var setId = mostOverlapped[i].set;
            var circle = lookup[setId];
            var placed = overlaps[setId]
                .Where(_ => positioned.Contains(_.set))
                .OrderByDescending(_ => _.size)
                .ToList();

            var points = new List<Point>();
            for (var j = 0; j < placed.Count; j++)
            {
                var first = lookup[placed[j].set];
                var firstDistance = DistanceFromIntersectArea(circle.Radius, first.Radius, placed[j].size);
                points.Add(new(first.X + firstDistance, first.Y));
                points.Add(new(first.X - firstDistance, first.Y));
                points.Add(new(first.X, first.Y + firstDistance));
                points.Add(new(first.X, first.Y - firstDistance));

                for (var k = j + 1; k < placed.Count; k++)
                {
                    var second = lookup[placed[k].set];
                    var secondDistance = DistanceFromIntersectArea(circle.Radius, second.Radius, placed[k].size);
                    points.AddRange(
                        CircleCircleIntersection(
                            new("", first.X, first.Y, firstDistance),
                            new("", second.X, second.Y, secondDistance)));
                }
            }

            var bestLoss = 1e50;
            var bestPoint = points.Count > 0 ? points[0] : new(0, 0);
            foreach (var point in points)
            {
                circle.X = point.X;
                circle.Y = point.Y;
                var loss = Loss(lookup, areas);
                if (loss < bestLoss)
                {
                    bestLoss = loss;
                    bestPoint = point;
                }
            }

            circle.X = bestPoint.X;
            circle.Y = bestPoint.Y;
            positioned.Add(setId);
        }

        return circles;
    }

    /// <summary>The squared differences between the overlaps drawn and the overlaps wanted.</summary>
    static double Loss(Dictionary<string, Circle> circles, List<Area> areas)
    {
        var output = 0.0;
        foreach (var area in areas)
        {
            if (area.Sets.Count == 1)
            {
                continue;
            }

            double overlap;
            if (area.Sets.Count == 2)
            {
                var left = circles[area.Sets[0]];
                var right = circles[area.Sets[1]];
                overlap = CircleOverlap(left.Radius, right.Radius, Distance(left, right));
            }
            else
            {
                overlap = IntersectionArea(area.Sets.Select(_ => circles[_]).ToList(), out _);
            }

            output += (overlap - area.Size) * (overlap - area.Size);
        }

        return output;
    }

    static double DistanceFromIntersectArea(double radius1, double radius2, double overlap)
    {
        if (Math.Min(radius1, radius2) * Math.Min(radius1, radius2) * Math.PI <= overlap + small)
        {
            return Math.Abs(radius1 - radius2);
        }

        return Bisect(_ => CircleOverlap(radius1, radius2, _) - overlap, 0, radius1 + radius2);
    }

    static double Bisect(Func<double, double> function, double low, double high)
    {
        var lowValue = function(low);
        var highValue = function(high);
        var delta = high - low;

        if (lowValue == 0)
        {
            return low;
        }

        if (highValue == 0)
        {
            return high;
        }

        for (var i = 0; i < 100; i++)
        {
            delta /= 2;
            var middle = low + delta;
            var middleValue = function(middle);
            if (middleValue * lowValue >= 0)
            {
                low = middle;
            }

            if (Math.Abs(delta) < 1e-10 ||
                middleValue == 0)
            {
                return middle;
            }
        }

        return low + delta;
    }

    sealed class Vertex(double[] values, double value)
    {
        public double[] Values { get; } = values;
        public double Value { get; set; } = value;
    }

    /// <summary>The downhill simplex minimiser from fmin, which venn.js uses.</summary>
    static double[] NelderMead(
        Func<double[], double> function,
        double[] start,
        int maxIterations,
        double minErrorDelta,
        double minTolerance)
    {
        const double rho = 1;
        const double chi = 2;
        const double psi = -0.5;
        const double sigma = 0.5;

        var count = start.Length;
        var simplex = new Vertex[count + 1];
        simplex[0] = new((double[]) start.Clone(), function(start));
        for (var i = 0; i < count; i++)
        {
            var point = (double[]) start.Clone();
            point[i] = point[i] != 0 ? point[i] * 1.05 : 0.001;
            simplex[i + 1] = new(point, function(point));
        }

        var centroid = new double[count];
        var reflected = new double[count];
        var contracted = new double[count];
        var expanded = new double[count];

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            // A stable sort, as JavaScript's is.
            simplex = simplex.OrderBy(_ => _.Value).ToArray();

            var maxDifference = 0.0;
            for (var i = 0; i < count; i++)
            {
                maxDifference = Math.Max(maxDifference, Math.Abs(simplex[0].Values[i] - simplex[1].Values[i]));
            }

            if (Math.Abs(simplex[0].Value - simplex[count].Value) < minErrorDelta &&
                maxDifference < minTolerance)
            {
                break;
            }

            for (var i = 0; i < count; i++)
            {
                centroid[i] = 0;
                for (var j = 0; j < count; j++)
                {
                    centroid[i] += simplex[j].Values[i];
                }

                centroid[i] /= count;
            }

            var worst = simplex[count];
            WeightedSum(reflected, 1 + rho, centroid, -rho, worst.Values);
            var reflectedValue = function(reflected);

            if (reflectedValue < simplex[0].Value)
            {
                WeightedSum(expanded, 1 + chi, centroid, -chi, worst.Values);
                var expandedValue = function(expanded);
                if (expandedValue < reflectedValue)
                {
                    Replace(worst, expanded, expandedValue);
                }
                else
                {
                    Replace(worst, reflected, reflectedValue);
                }
            }
            else if (reflectedValue >= simplex[count - 1].Value)
            {
                bool shouldReduce;
                if (reflectedValue > worst.Value)
                {
                    WeightedSum(contracted, 1 + psi, centroid, -psi, worst.Values);
                    var contractedValue = function(contracted);
                    shouldReduce = contractedValue >= worst.Value;
                    if (!shouldReduce)
                    {
                        Replace(worst, contracted, contractedValue);
                    }
                }
                else
                {
                    WeightedSum(contracted, 1 - psi * rho, centroid, psi * rho, worst.Values);
                    var contractedValue = function(contracted);
                    shouldReduce = contractedValue >= reflectedValue;
                    if (!shouldReduce)
                    {
                        Replace(worst, contracted, contractedValue);
                    }
                }

                if (shouldReduce)
                {
                    for (var i = 1; i < simplex.Length; i++)
                    {
                        WeightedSum(simplex[i].Values, 1 - sigma, simplex[0].Values, sigma, simplex[i].Values);
                        simplex[i].Value = function(simplex[i].Values);
                    }
                }
            }
            else
            {
                Replace(worst, reflected, reflectedValue);
            }
        }

        return simplex.OrderBy(_ => _.Value).First().Values;

        static void Replace(Vertex vertex, double[] values, double value)
        {
            Array.Copy(values, vertex.Values, values.Length);
            vertex.Value = value;
        }

        static void WeightedSum(double[] output, double weight1, double[] vector1, double weight2, double[] vector2)
        {
            for (var i = 0; i < output.Length; i++)
            {
                output[i] = weight1 * vector1[i] + weight2 * vector2[i];
            }
        }
    }

    /// <summary>
    /// The area common to all of <paramref name="circles"/>, and the arcs that bound it (for drawing it):
    /// a polygon through the intersection points inside every circle, plus the circular segment on each edge.
    /// </summary>
    public static double IntersectionArea(IReadOnlyList<Circle> circles, out List<Arc> arcs)
    {
        var intersections = new List<(Point point, int first, int second)>();
        for (var i = 0; i < circles.Count; i++)
        {
            for (var j = i + 1; j < circles.Count; j++)
            {
                foreach (var point in CircleCircleIntersection(circles[i], circles[j]))
                {
                    intersections.Add((point, i, j));
                }
            }
        }

        var inner = intersections
            .Where(_ => circles.All(circle => Distance(_.point, circle) < circle.Radius + small))
            .ToList();

        var arcArea = 0.0;
        var polygonArea = 0.0;
        arcs = [];

        if (inner.Count > 1)
        {
            var center = new Point(inner.Average(_ => _.point.X), inner.Average(_ => _.point.Y));
            inner = inner
                .OrderByDescending(_ => Math.Atan2(_.point.X - center.X, _.point.Y - center.Y))
                .ToList();

            var previous = inner[^1];
            foreach (var current in inner)
            {
                var p1 = current.point;
                var p2 = previous.point;
                polygonArea += (p2.X + p1.X) * (p1.Y - p2.Y);

                var midPoint = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
                Arc? arc = null;
                foreach (var parent in (int[]) [current.first, current.second])
                {
                    if (parent != previous.first &&
                        parent != previous.second)
                    {
                        continue;
                    }

                    var circle = circles[parent];
                    var angle1 = Math.Atan2(p1.X - circle.X, p1.Y - circle.Y);
                    var angle2 = Math.Atan2(p2.X - circle.X, p2.Y - circle.Y);
                    var angleDifference = angle2 - angle1;
                    if (angleDifference < 0)
                    {
                        angleDifference += 2 * Math.PI;
                    }

                    var halfway = angle2 - angleDifference / 2;
                    var width = Distance(
                        midPoint,
                        new Point(circle.X + circle.Radius * Math.Sin(halfway), circle.Y + circle.Radius * Math.Cos(halfway)));
                    width = Math.Min(width, circle.Radius * 2);

                    if (arc is null ||
                        arc.Width > width)
                    {
                        arc = new(circle, width, p1, p2, width > circle.Radius);
                    }
                }

                if (arc is not null)
                {
                    arcs.Add(arc);
                    arcArea += SegmentArea(arc.Circle.Radius, arc.Width);
                    previous = current;
                }
            }
        }
        else
        {
            // No points inside them all: either they are disjoint, or the smallest lies inside the rest.
            var smallest = circles.MinBy(_ => _.Radius)!;
            var disjoint = circles.Any(_ => Distance(_, smallest) > Math.Abs(smallest.Radius - _.Radius));
            if (!disjoint)
            {
                arcArea = smallest.Radius * smallest.Radius * Math.PI;
                arcs.Add(
                    new(
                        smallest,
                        smallest.Radius * 2,
                        new(smallest.X, smallest.Y + smallest.Radius),
                        new(smallest.X - small, smallest.Y + smallest.Radius),
                        true));
            }
        }

        return arcArea + polygonArea / 2;
    }

    static double SegmentArea(double radius, double width) =>
        radius * radius * Math.Acos(1 - width / radius) - (radius - width) * Math.Sqrt(width * (2 * radius - width));

    static double Distance(Circle first, Circle second) =>
        Distance(new Point(first.X, first.Y), new Point(second.X, second.Y));

    static double Distance(Point point, Circle circle) =>
        Distance(point, new Point(circle.X, circle.Y));

    static double Distance(Point first, Point second) =>
        Math.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));

    static double CircleOverlap(double radius1, double radius2, double distance)
    {
        if (distance >= radius1 + radius2)
        {
            return 0;
        }

        if (distance <= Math.Abs(radius1 - radius2))
        {
            return Math.PI * Math.Min(radius1, radius2) * Math.Min(radius1, radius2);
        }

        var width1 = radius1 - (distance * distance - radius2 * radius2 + radius1 * radius1) / (2 * distance);
        var width2 = radius2 - (distance * distance - radius1 * radius1 + radius2 * radius2) / (2 * distance);
        return SegmentArea(radius1, width1) + SegmentArea(radius2, width2);
    }

    static Point[] CircleCircleIntersection(Circle first, Circle second)
    {
        var distance = Distance(first, second);
        var radius1 = first.Radius;
        var radius2 = second.Radius;

        if (distance >= radius1 + radius2 ||
            distance <= Math.Abs(radius1 - radius2))
        {
            return [];
        }

        var along = (radius1 * radius1 - radius2 * radius2 + distance * distance) / (2 * distance);
        var height = Math.Sqrt(radius1 * radius1 - along * along);
        var baseX = first.X + along * (second.X - first.X) / distance;
        var baseY = first.Y + along * (second.Y - first.Y) / distance;
        var offsetX = -(second.Y - first.Y) * (height / distance);
        var offsetY = -(second.X - first.X) * (height / distance);

        return [new(baseX + offsetX, baseY - offsetY), new(baseX - offsetX, baseY + offsetY)];
    }

    /// <summary>
    /// Turns each cluster of overlapping circles so its largest circle is at its origin and its second
    /// largest lies at <paramref name="orientation"/> from it, mirrored so the third falls on a fixed side;
    /// then lays the clusters out in a grid, largest first.
    /// </summary>
    static List<Circle> Normalize(List<Circle> solution, double orientation)
    {
        var clusters = DisjointClusters(solution.Select(_ => _.Copy()).ToList());

        var measured = new List<(List<Circle> circles, Bounds bounds, double size)>();
        foreach (var cluster in clusters)
        {
            var oriented = Orientate(cluster, orientation);
            var bounds = BoundingBox(oriented);
            measured.Add((oriented, bounds, (bounds.MaxX - bounds.MinX) * (bounds.MaxY - bounds.MinY)));
        }

        measured = measured.OrderByDescending(_ => _.size).ToList();

        var circles = measured[0].circles;
        var returnBounds = measured[0].bounds;
        var spacing = (returnBounds.MaxX - returnBounds.MinX) / 50;

        var index = 1;
        while (index < measured.Count)
        {
            AddCluster(index, right: true, bottom: false);
            AddCluster(index + 1, right: false, bottom: true);
            AddCluster(index + 2, right: true, bottom: true);
            index += 3;
            returnBounds = BoundingBox(circles);
        }

        return circles;

        void AddCluster(int clusterIndex, bool right, bool bottom)
        {
            if (clusterIndex >= measured.Count)
            {
                return;
            }

            var (cluster, bounds, _) = measured[clusterIndex];
            double offsetX;
            double offsetY;

            if (right)
            {
                offsetX = returnBounds.MaxX - bounds.MinX + spacing;
            }
            else
            {
                offsetX = returnBounds.MaxX - bounds.MaxX;
                var centring = (bounds.MaxX - bounds.MinX) / 2 - (returnBounds.MaxX - returnBounds.MinX) / 2;
                if (centring < 0)
                {
                    offsetX += centring;
                }
            }

            if (bottom)
            {
                offsetY = returnBounds.MaxY - bounds.MinY + spacing;
            }
            else
            {
                offsetY = returnBounds.MaxY - bounds.MaxY;
                var centring = (bounds.MaxY - bounds.MinY) / 2 - (returnBounds.MaxY - returnBounds.MinY) / 2;
                if (centring < 0)
                {
                    offsetY += centring;
                }
            }

            foreach (var circle in cluster)
            {
                circle.X += offsetX;
                circle.Y += offsetY;
                circles.Add(circle);
            }
        }
    }

    static List<Circle> Orientate(List<Circle> cluster, double orientation)
    {
        var circles = cluster.OrderByDescending(_ => _.Radius).ToList();

        var largestX = circles[0].X;
        var largestY = circles[0].Y;
        foreach (var circle in circles)
        {
            circle.X -= largestX;
            circle.Y -= largestY;
        }

        if (circles.Count == 2 &&
            Distance(circles[0], circles[1]) < Math.Abs(circles[1].Radius - circles[0].Radius))
        {
            // A set inside another is pushed to one side of it, so the two remain distinguishable.
            circles[1].X = circles[0].X + circles[0].Radius - circles[1].Radius - 1e-10;
            circles[1].Y = circles[0].Y;
        }

        if (circles.Count > 1)
        {
            var rotation = Math.Atan2(circles[1].X, circles[1].Y) - orientation;
            var cos = Math.Cos(rotation);
            var sin = Math.Sin(rotation);
            foreach (var circle in circles)
            {
                var originalX = circle.X;
                var originalY = circle.Y;
                circle.X = cos * originalX - sin * originalY;
                circle.Y = sin * originalX + cos * originalY;
            }
        }

        if (circles.Count > 2)
        {
            var angle = Math.Atan2(circles[2].X, circles[2].Y) - orientation;
            while (angle < 0)
            {
                angle += 2 * Math.PI;
            }

            while (angle > 2 * Math.PI)
            {
                angle -= 2 * Math.PI;
            }

            if (angle > Math.PI)
            {
                var slope = circles[1].Y / (1e-10 + circles[1].X);
                foreach (var circle in circles)
                {
                    var along = (circle.X + slope * circle.Y) / (1 + slope * slope);
                    circle.X = 2 * along - circle.X;
                    circle.Y = 2 * along * slope - circle.Y;
                }
            }
        }

        return circles;
    }

    static List<List<Circle>> DisjointClusters(List<Circle> circles)
    {
        var parents = Enumerable.Range(0, circles.Count).ToArray();

        for (var i = 0; i < circles.Count; i++)
        {
            for (var j = i + 1; j < circles.Count; j++)
            {
                if (Distance(circles[i], circles[j]) + 1e-10 < circles[i].Radius + circles[j].Radius)
                {
                    parents[Find(j)] = Find(i);
                }
            }
        }

        var clusters = new Dictionary<int, List<Circle>>();
        var order = new List<int>();
        for (var i = 0; i < circles.Count; i++)
        {
            var root = Find(i);
            if (!clusters.TryGetValue(root, out var cluster))
            {
                cluster = [];
                clusters[root] = cluster;
                order.Add(root);
            }

            cluster.Add(circles[i]);
        }

        return order.Select(_ => clusters[_]).ToList();

        int Find(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }

            return index;
        }
    }

    readonly record struct Bounds(double MinX, double MaxX, double MinY, double MaxY);

    static Bounds BoundingBox(List<Circle> circles) =>
        new(
            circles.Min(_ => _.X - _.Radius),
            circles.Max(_ => _.X + _.Radius),
            circles.Min(_ => _.Y - _.Radius),
            circles.Max(_ => _.Y + _.Radius));

    /// <summary>Scales the circles to fit inside the padded box, centred in it.</summary>
    static List<Circle> Scale(List<Circle> circles, double width, double height, double padding)
    {
        width -= 2 * padding;
        height -= 2 * padding;

        var bounds = BoundingBox(circles);
        if (bounds.MaxX == bounds.MinX ||
            bounds.MaxY == bounds.MinY)
        {
            return circles;
        }

        var scaling = Math.Min(width / (bounds.MaxX - bounds.MinX), height / (bounds.MaxY - bounds.MinY));
        var offsetX = (width - (bounds.MaxX - bounds.MinX) * scaling) / 2;
        var offsetY = (height - (bounds.MaxY - bounds.MinY) * scaling) / 2;

        return circles
            .Select(_ => new Circle(
                _.SetId,
                padding + offsetX + (_.X - bounds.MinX) * scaling,
                padding + offsetY + (_.Y - bounds.MinY) * scaling,
                scaling * _.Radius)
            {
                Size = _.Size
            })
            .ToList();
    }

    static Dictionary<string, Point> TextCentres(List<Circle> circles, IReadOnlyList<Area> areas)
    {
        var result = new Dictionary<string, Point>(StringComparer.Ordinal);
        var overlapped = OverlappingCircles(circles);
        foreach (var area in areas)
        {
            var members = new HashSet<string>(area.Sets, StringComparer.Ordinal);
            // A circle wholly inside one of the area's own does not count against its label position.
            var exclude = new HashSet<string>(area.Sets.SelectMany(_ => overlapped[_]), StringComparer.Ordinal);

            var interior = circles.Where(_ => members.Contains(_.SetId)).ToList();
            if (interior.Count != area.Sets.Count)
            {
                continue;
            }

            var exterior = circles.Where(_ => !members.Contains(_.SetId) && !exclude.Contains(_.SetId)).ToList();
            if (TextCentre(interior, exterior) is { } centre)
            {
                result[area.Key] = centre;
            }
        }

        return result;
    }

    static Dictionary<string, List<string>> OverlappingCircles(List<Circle> circles)
    {
        var result = circles.ToDictionary(_ => _.SetId, _ => new List<string>(), StringComparer.Ordinal);
        for (var i = 0; i < circles.Count; i++)
        {
            var first = circles[i];
            for (var j = i + 1; j < circles.Count; j++)
            {
                var second = circles[j];
                var distance = Distance(first, second);
                if (distance + second.Radius <= first.Radius + 1e-10)
                {
                    result[second.SetId].Add(first.SetId);
                }
                else if (distance + first.Radius <= second.Radius + 1e-10)
                {
                    result[first.SetId].Add(second.SetId);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The point inside every interior circle and outside every exterior one that lies furthest from all
    /// their edges, or null when the region does not exist.
    /// </summary>
    static Point? TextCentre(List<Circle> interior, List<Circle> exterior)
    {
        var points = new List<Point>();
        foreach (var circle in interior)
        {
            points.Add(new(circle.X, circle.Y));
            points.Add(new(circle.X + circle.Radius / 2, circle.Y));
            points.Add(new(circle.X - circle.Radius / 2, circle.Y));
            points.Add(new(circle.X, circle.Y + circle.Radius / 2));
            points.Add(new(circle.X, circle.Y - circle.Radius / 2));
        }

        var initial = points[0];
        var margin = Margin(points[0]);
        for (var i = 1; i < points.Count; i++)
        {
            var candidate = Margin(points[i]);
            if (candidate >= margin)
            {
                initial = points[i];
                margin = candidate;
            }
        }

        var solution = NelderMead(_ => -Margin(new(_[0], _[1])), [initial.X, initial.Y], 500, 1e-10, 1e-10);
        var result = new Point(solution[0], solution[1]);

        if (interior.All(_ => Distance(result, _) <= _.Radius) &&
            exterior.All(_ => Distance(result, _) >= _.Radius))
        {
            return result;
        }

        if (interior.Count == 1)
        {
            return new(interior[0].X, interior[0].Y);
        }

        IntersectionArea(interior, out var arcs);
        if (arcs.Count == 0)
        {
            return null;
        }

        if (arcs.Count == 1)
        {
            return new(arcs[0].Circle.X, arcs[0].Circle.Y);
        }

        if (exterior.Count > 0)
        {
            return TextCentre(interior, []);
        }

        return new(arcs.Average(_ => _.P1.X), arcs.Average(_ => _.P1.Y));

        double Margin(Point current)
        {
            var value = double.MaxValue;
            foreach (var circle in interior)
            {
                value = Math.Min(value, circle.Radius - Distance(current, circle));
            }

            foreach (var circle in exterior)
            {
                value = Math.Min(value, Distance(current, circle) - circle.Radius);
            }

            return value;
        }
    }
}
