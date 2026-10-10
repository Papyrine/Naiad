namespace Naiad.Diagrams.Architecture;

public class ArchitectureRenderer : IDiagramRenderer<ArchitectureModel>
{
    const double serviceWidth = 100;
    const double serviceHeight = 80;
    const double serviceSpacing = 40;
    const double groupPadding = 20;
    const double iconSize = 32;

    static readonly Dictionary<string, string> iconPaths = new()
    {
        ["cloud"] = "M25,60 Q0,60 0,45 Q0,30 15,30 Q15,15 35,15 Q55,15 55,30 Q70,30 70,45 Q70,60 45,60 Z",
        ["database"] = "M10,20 L10,50 Q25,60 40,50 L40,20 Q25,10 10,20 M10,20 Q25,30 40,20",
        ["disk"] = "M5,40 L5,20 A20,10 0 1,1 45,20 L45,40 A20,10 0 1,1 5,40 M5,20 A20,10 0 1,0 45,20",
        ["internet"] = "M25,5 A20,20 0 1,1 25,45 A20,20 0 1,1 25,5 M5,25 L45,25 M25,5 Q15,25 25,45 M25,5 Q35,25 25,45",
        ["server"] = "M5,10 L45,10 L45,40 L5,40 Z M5,15 L45,15 M8,12.5 A1,1 0 1,1 8,12.49"
    };

    static readonly Dictionary<string, string> iconColors = new()
    {
        ["cloud"] = "#4FC3F7",
        ["database"] = "#81C784",
        ["disk"] = "#FFB74D",
        ["internet"] = "#BA68C8",
        ["server"] = "#90A4AE"
    };

    static readonly string[] groupColors =
    [
        "#E3F2FD", "#E8F5E9", "#FFF3E0", "#F3E5F5",
        "#FCE4EC", "#E0F7FA", "#FFF8E1", "#F1F8E9"
    ];

    const double groupLabelHeight = 24;
    const double groupIconScale = 0.4;
    const double groupIconReservedWidth = 34;
    const double groupIconBox = 28;

    // Services and junctions are placed and connected by id, so two sharing one cannot both be drawn.
    // Mermaid rejects such a diagram, and saying why is better than the dictionary's own complaint.
    static void RejectDuplicateIds(ArchitectureModel model)
    {
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);

        void Claim(string id, string kind)
        {
            if (kinds.TryGetValue(id, out var owner))
            {
                throw new MermaidException($"The {kind} id [{id}] is already in use by another {owner}");
            }

            kinds[id] = kind;
        }

        foreach (var service in model.Services)
        {
            Claim(service.Id, "service");
        }

        foreach (var junction in model.Junctions)
        {
            Claim(junction.Id, "junction");
        }
    }

    public SvgDocument Render(ArchitectureModel model, RenderOptions options)
    {
        RejectDuplicateIds(model);

        if (model.Services.Count == 0 &&
            model.Groups.Count == 0)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "Empty diagram",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        // Check if any services belong to groups
        var hasGroups = model.Groups.Count > 0 &&
                        model.Services.Any(s => !string.IsNullOrEmpty(s.Parent));

        // Offset for group padding (to make room for group bounds)
        var offsetX = hasGroups ? groupPadding : 0;
        var offsetY = hasGroups ? groupPadding + groupLabelHeight : 0;

        // Assign each service/junction a grid cell, honoring edge directions
        var cells = ComputeCells(model);
        var positions = new Dictionary<string, (double x, double y)>();
        var gridCols = cells.Count == 0 ? 0 : cells.Values.Max(c => c.col) + 1;
        var gridRows = cells.Count == 0 ? 0 : cells.Values.Max(c => c.row) + 1;

        // Content dimensions (with extra space for groups if needed)
        var contentWidth = gridCols * serviceWidth + Math.Max(0, gridCols - 1) * serviceSpacing + (hasGroups ? groupPadding * 2 : 0);
        var contentHeight = gridRows * serviceHeight + Math.Max(0, gridRows - 1) * serviceSpacing + (hasGroups ? groupPadding * 2 + groupLabelHeight : 0);

        var builder = new SvgBuilder();
        builder.Size(contentWidth, contentHeight);
        builder.Padding(options.Padding);

        // Add arrow marker
        builder.AddArrowMarker("arch-arrow", "#666");

        // Position services (calculate positions first, draw later)
        var servicePositions = new Dictionary<string, (double x, double y, double width, double height)>();
        foreach (var service in model.Services)
        {
            var (col, row) = cells[service.Id];
            var x = offsetX + col * (serviceWidth + serviceSpacing);
            var y = offsetY + row * (serviceHeight + serviceSpacing);

            positions[service.Id] = (x + serviceWidth / 2, y + serviceHeight / 2);
            servicePositions[service.Id] = (x, y, serviceWidth, serviceHeight);
        }

        // Position junctions
        var junctionPositions = new Dictionary<string, (double x, double y)>();
        foreach (var junction in model.Junctions)
        {
            var (col, row) = cells[junction.Id];
            var x = offsetX + col * (serviceWidth + serviceSpacing);
            var y = offsetY + row * (serviceHeight + serviceSpacing);

            positions[junction.Id] = (x + serviceWidth / 2, y + serviceHeight / 2);
            junctionPositions[junction.Id] = (x + serviceWidth / 2, y + serviceHeight / 2);
        }

        // Draw groups first (as background), keeping each group's bounds so an edge flagged with
        // {group} can anchor to the group boundary instead of the service.
        var groupBounds = new Dictionary<string, (double x, double y, double width, double height)>();
        var colorIndex = 0;
        foreach (var group in model.Groups)
        {
            var bounds = CalculateGroupBounds(group.Id, model.Services, servicePositions);
            if (!bounds.HasValue)
            {
                continue;
            }

            groupBounds[group.Id] = bounds.Value;
            var color = groupColors[colorIndex % groupColors.Length];
            DrawGroup(builder, group, bounds.Value, color, options);
            colorIndex++;
        }

        // Draw services
        foreach (var service in model.Services)
        {
            var pos = servicePositions[service.Id];
            DrawService(builder, service, pos.x, pos.y, options);
        }

        // Draw junctions
        foreach (var junction in model.Junctions)
        {
            var (x, y) = junctionPositions[junction.Id];
            DrawJunction(builder, x, y);
        }

        // Draw edges
        var servicesById = model.Services.ToDictionary(s => s.Id);
        foreach (var edge in model.Edges)
        {
            if (positions.TryGetValue(edge.SourceId, out var from) &&
                positions.TryGetValue(edge.TargetId, out var to))
            {
                var fromGroup = ResolveGroupBounds(edge.SourceId, edge.SourceGroup, servicesById, groupBounds);
                var toGroup = ResolveGroupBounds(edge.TargetId, edge.TargetGroup, servicesById, groupBounds);
                DrawEdge(builder, from, to, edge, fromGroup, toGroup);
            }
        }

        return builder.Build();
    }

    // Assigns a (col, row) grid cell to every service and junction.
    // When edges are present, neighbours are placed relative to their source
    // according to the edge's source side (e.g. "a:R -- L:b" puts b to the
    // right of a). Diagrams without edges fall back to a square-ish grid in
    // declaration order.
    static Dictionary<string, (int col, int row)> ComputeCells(ArchitectureModel model)
    {
        var ids = model.Services.Select(s => s.Id)
            .Concat(model.Junctions.Select(j => j.Id))
            .ToList();

        var cells = new Dictionary<string, (int col, int row)>();
        if (ids.Count == 0)
        {
            return cells;
        }

        if (model.Edges.Count == 0)
        {
            var cols = (int)Math.Ceiling(Math.Sqrt(ids.Count));
            for (var i = 0; i < ids.Count; i++)
            {
                cells[ids[i]] = (i % cols, i / cols);
            }

            return cells;
        }

        // Build adjacency with directional offsets from the edges
        var idSet = new HashSet<string>(ids);
        var adjacency = ids.ToDictionary(id => id, _ => new List<(string neighbor, int dCol, int dRow)>());
        foreach (var edge in model.Edges)
        {
            if (!idSet.Contains(edge.SourceId) || !idSet.Contains(edge.TargetId))
            {
                continue;
            }

            var (dCol, dRow) = CellOffset(edge.SourceSide);
            adjacency[edge.SourceId].Add((edge.TargetId, dCol, dRow));
            adjacency[edge.TargetId].Add((edge.SourceId, -dCol, -dRow));
        }

        // Place each connected component with a BFS that honours the offsets
        var occupied = new HashSet<(int col, int row)>();
        foreach (var start in ids)
        {
            if (cells.ContainsKey(start))
            {
                continue;
            }

            // Start new components below everything placed so far
            var startCell = (col: 0, row: occupied.Count == 0 ? 0 : occupied.Max(c => c.row) + 2);
            while (occupied.Contains(startCell))
            {
                startCell = (startCell.col + 1, startCell.row);
            }

            var queue = new Queue<string>();
            cells[start] = startCell;
            occupied.Add(startCell);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var (col, row) = cells[current];
                foreach (var (neighbor, dCol, dRow) in adjacency[current])
                {
                    if (cells.ContainsKey(neighbor))
                    {
                        continue;
                    }

                    var cell = (col: col + dCol, row: row + dRow);
                    while (occupied.Contains(cell))
                    {
                        cell = (cell.col + dCol, cell.row + dRow);
                    }

                    cells[neighbor] = cell;
                    occupied.Add(cell);
                    queue.Enqueue(neighbor);
                }
            }
        }

        // Normalize so the minimum column and row are zero
        var minCol = cells.Values.Min(c => c.col);
        var minRow = cells.Values.Min(c => c.row);
        foreach (var id in cells.Keys.ToList())
        {
            var (col, row) = cells[id];
            cells[id] = (col - minCol, row - minRow);
        }

        return cells;
    }

    static (int col, int row) CellOffset(EdgeDirection dir) => dir switch
    {
        EdgeDirection.Left => (-1, 0),
        EdgeDirection.Right => (1, 0),
        EdgeDirection.Top => (0, -1),
        EdgeDirection.Bottom => (0, 1),
        _ => (0, 0)
    };

    static (double x, double y, double width, double height)? CalculateGroupBounds(
        string groupId,
        List<ArchitectureService> services,
        Dictionary<string, (double x, double y, double width, double height)> servicePositions)
    {
        double? minX = null, minY = null, maxX = null, maxY = null;

        foreach (var service in services)
        {
            if (service.Parent == groupId && servicePositions.TryGetValue(service.Id, out var pos))
            {
                minX = minX.HasValue ? Math.Min(minX.Value, pos.x) : pos.x;
                minY = minY.HasValue ? Math.Min(minY.Value, pos.y) : pos.y;
                maxX = maxX.HasValue ? Math.Max(maxX.Value, pos.x + pos.width) : pos.x + pos.width;
                maxY = maxY.HasValue ? Math.Max(maxY.Value, pos.y + pos.height) : pos.y + pos.height;
            }
        }

        if (!minX.HasValue || !minY.HasValue || !maxX.HasValue || !maxY.HasValue)
            return null;

        return (
            minX.Value - groupPadding,
            minY.Value - groupPadding - groupLabelHeight,
            maxX.Value - minX.Value + groupPadding * 2,
            maxY.Value - minY.Value + groupPadding * 2 + groupLabelHeight
        );
    }

    static void DrawGroup(SvgBuilder builder, ArchitectureGroup group,
        (double x, double y, double width, double height) bounds, string color, RenderOptions options)
    {
        var icon = group.Icon ?? "cloud";
        var borderColor = iconColors.GetValueOrDefault(icon, "#90A4AE");

        // Group background with dashed border
        builder.AddRect(
            bounds.x,
            bounds.y,
            bounds.width,
            bounds.height,
            rx: 8,
            fill: color,
            stroke: borderColor,
            strokeWidth: 2,
            style: "stroke-dasharray: 5,3");

        // Group icon (top-left of the header)
        var labelX = bounds.x + groupPadding;
        if (DrawIcon(builder, icon, borderColor, bounds.x + 10, bounds.y + 8, groupIconBox, groupIconScale))
        {
            labelX = bounds.x + 10 + groupIconReservedWidth;
        }

        // Group label
        var label = group.Label ?? group.Id;
        builder.AddText(
            labelX,
            bounds.y + groupLabelHeight / 2 + groupPadding / 2,
            label,
            anchor: "start",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily,
            fontWeight: "bold",
            fill: "#333");
    }

    static void DrawService(SvgBuilder builder, ArchitectureService service, double x, double y, RenderOptions options)
    {
        var icon = service.Icon ?? "server";
        var color = iconColors.GetValueOrDefault(icon, "#90A4AE");

        // Background
        builder.AddRect(
            x, y,
            serviceWidth,
            serviceHeight,
            rx: 8,
            fill: "#FAFAFA",
            stroke: color,
            strokeWidth: 2);

        // Icon
        var iconX = x + (serviceWidth - iconSize) / 2;
        var iconY = y + 8;
        DrawIcon(builder, icon, color, iconX, iconY, iconSize, 0.64);

        // Label
        var label = service.Label ?? service.Id;
        builder.AddText(
            x + serviceWidth / 2,
            y + serviceHeight - 12,
            label,
            anchor: "middle",
            baseline: "middle",
            fontSize: options.FontSize - 2,
            fontFamily: options.FontFamily,
            fill: "#333");
    }

    // Draws an icon at (x,y) within a box-sized square. Resolves "prefix:name"
    // references against the bundled iconify packs, otherwise falls back to the
    // built-in icon paths. Returns false when no icon could be drawn.
    static bool DrawIcon(SvgBuilder builder, string icon, string accent, double x, double y, double box, double builtinScale)
    {
        if (icon.Contains(':') &&
            IconPackRegistry.Resolve(icon) is { } packIcon)
        {
            var scale = box / Math.Max(packIcon.Width, packIcon.Height);
            var drawnWidth = packIcon.Width * scale;
            var drawnHeight = packIcon.Height * scale;
            var tx = x + (box - drawnWidth) / 2;
            var ty = y + (box - drawnHeight) / 2;
            builder.AddRawSvg(string.Create(
                CultureInfo.InvariantCulture,
                $"<g transform='translate({tx:0.##},{ty:0.##}) scale({scale:0.####})' style='color:{accent}'>{packIcon.Body}</g>"));
            return true;
        }

        if (iconPaths.TryGetValue(icon, out var path))
        {
            builder.BeginGroup(transform: string.Create(CultureInfo.InvariantCulture, $"translate({x:0.##},{y:0.##}) scale({builtinScale})"));
            builder.AddPath(path, fill: accent, stroke: "#333", strokeWidth: 1);
            builder.EndGroup();
            return true;
        }

        return false;
    }

    static void DrawJunction(
        SvgBuilder builder,
        double x,
        double y) =>
        builder.AddCircle(x, y, 8, fill: "#666", stroke: "#333", strokeWidth: 1);

    static void DrawEdge(
        SvgBuilder builder,
        (double x, double y) from,
        (double x, double y) to,
        ArchitectureEdge edge,
        (double x, double y, double width, double height)? fromGroup,
        (double x, double y, double width, double height)? toGroup)
    {
        // Anchor each end to the service edge, or to the containing group's boundary when the
        // endpoint was flagged with {group}.
        var (fromX, fromY) = ResolveEndpoint(from, edge.SourceSide, fromGroup);
        var (toX, toY) = ResolveEndpoint(to, edge.TargetSide, toGroup);

        // Draw line
        builder.AddLine(fromX, fromY, toX, toY, stroke: "#666", strokeWidth: 1.5);

        // Draw arrows
        if (edge.TargetArrow)
        {
            var angle = Math.Atan2(toY - fromY, toX - fromX);
            DrawArrow(builder, toX, toY, angle);
        }

        if (edge.SourceArrow)
        {
            var angle = Math.Atan2(fromY - toY, fromX - toX);
            DrawArrow(builder, fromX, fromY, angle);
        }
    }

    // When an endpoint carries the {group} flag, the edge meets the boundary of the group the service
    // belongs to: the axis perpendicular to the side comes from the group rect, the parallel axis from
    // the service centre (so the edge still lines up with the service). Otherwise it meets the service edge.
    static (double x, double y) ResolveEndpoint(
        (double x, double y) center,
        EdgeDirection side,
        (double x, double y, double width, double height)? groupBounds)
    {
        if (groupBounds is { } g)
        {
            return side switch
            {
                EdgeDirection.Left => (g.x, center.y),
                EdgeDirection.Right => (g.x + g.width, center.y),
                EdgeDirection.Top => (center.x, g.y),
                EdgeDirection.Bottom => (center.x, g.y + g.height),
                _ => center
            };
        }

        var offset = GetDirectionOffset(side);
        return (center.x + offset.x * serviceWidth / 2, center.y + offset.y * serviceHeight / 2);
    }

    // Resolves the group an edge endpoint should anchor to, or null when it has no {group} flag, isn't a
    // service, or its service isn't inside a group with bounds.
    static (double x, double y, double width, double height)? ResolveGroupBounds(
        string serviceId,
        string? groupFlag,
        Dictionary<string, ArchitectureService> servicesById,
        Dictionary<string, (double x, double y, double width, double height)> groupBounds)
    {
        if (groupFlag == null ||
            !servicesById.TryGetValue(serviceId, out var service) ||
            string.IsNullOrEmpty(service.Parent) ||
            !groupBounds.TryGetValue(service.Parent, out var bounds))
        {
            return null;
        }

        return bounds;
    }

    static (double x, double y) GetDirectionOffset(EdgeDirection dir) => dir switch
    {
        EdgeDirection.Left => (-1, 0),
        EdgeDirection.Right => (1, 0),
        EdgeDirection.Top => (0, -1),
        EdgeDirection.Bottom => (0, 1),
        _ => (0, 0)
    };

    static void DrawArrow(SvgBuilder builder, double x, double y, double angle)
    {
        const int arrowSize = 8;
        const double arrowAngle = Math.PI / 6;
        var ax1 = x - arrowSize * Math.Cos(angle - arrowAngle);
        var ay1 = y - arrowSize * Math.Sin(angle - arrowAngle);
        var ax2 = x - arrowSize * Math.Cos(angle + arrowAngle);
        var ay2 = y - arrowSize * Math.Sin(angle + arrowAngle);

        builder.AddPath(
            string.Create(CultureInfo.InvariantCulture, $"M {x:0.##} {y:0.##} L {ax1:0.##} {ay1:0.##} L {ax2:0.##} {ay2:0.##} Z"),
            fill: "#666",
            stroke: "none");
    }
}
