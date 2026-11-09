namespace Naiad.Diagrams.TreeView;

/// <summary>
/// Draws a tree view as Mermaid does: one row per node, top to bottom in document order, each indented a
/// step further than its parent, under an implicit <c>/</c> root. A vertical line drops from below each
/// parent's label to the middle of its last child's row, and a short horizontal line runs from it to each
/// child. Descriptions line up in one column to the right of the widest label. Mermaid measures its
/// labels in the DOM; here they are estimated from their character counts.
/// </summary>
public class TreeViewRenderer : IDiagramRenderer<TreeViewModel>
{
    const double iconSize = 14;
    const double iconGap = 4;
    const double descriptionGap = 16;
    const double titleGap = 10;
    const string labelColor = "black";
    const string lineColor = "black";
    const string iconColor = "#546e7a";
    const string descriptionColor = "#6a9955";
    const string highlightFill = "rgba(255,193,7,0.15)";
    const string highlightStroke = "#ffc107";

    // The two built-in icons, from Mermaid's own icon set, drawn on a 24 by 24 grid in the current colour.
    const string folderIcon =
        "<path fill='currentColor' d='M10.59 4.59A2 2 0 0 0 9.17 4H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.17z'/>";

    const string fileIcon =
        "<path fill='currentColor' fill-rule='evenodd' d='M6 2a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8.83a2 2 0 0 0-.59-1.42l-4.82-4.82A2 2 0 0 0 13.17 2H6Zm7.5 1.9l4.6 4.6h-3.6a1 1 0 0 1-1-1V3.9Z' clip-rule='evenodd'/>";

    public SvgDocument Render(TreeViewModel model, RenderOptions options)
    {
        // The `treeView` configuration, with Mermaid's defaults.
        var config = model.Config;
        var rowIndent = config.GetNumber("treeView", "rowIndent") ?? 10;
        var paddingX = config.GetNumber("treeView", "paddingX") ?? 5;
        var paddingY = config.GetNumber("treeView", "paddingY") ?? 5;
        var lineThickness = config.GetNumber("treeView", "lineThickness") ?? 1;
        var icons = new IconSettings(
            config.GetBool("treeView", "showIcons") ?? false,
            config.GetString("treeView", "defaultIconPack") ?? "",
            config.GetMap("treeView", "filenameIcons"),
            config.GetMap("treeView", "extensionIcons"));

        var fontSize = (double) options.FontSize;
        var rowHeight = fontSize * SvgBuilder.LineHeightFactor + paddingY * 2;

        // Mermaid hangs the top-level nodes off a root directory named "/", and draws it.
        var root = new TreeViewNode
        {
            Name = "/",
            IsDirectory = true
        };
        root.Children.AddRange(model.Nodes);

        var rows = new List<Row>();
        var lines = new List<(double x1, double y1, double x2, double y2)>();
        Layout(root, 0);

        var contentWidth = rows.Max(static _ => _.X + _.Width);
        var descriptionX = rows.Max(static _ => _.LabelRight) + descriptionGap;
        foreach (var row in rows.Where(static _ => _.Node.Description is not null))
        {
            contentWidth = Math.Max(contentWidth, descriptionX + MeasureText(row.Node.Description!, fontSize, false) + paddingX);
        }

        // Highlight bands reach past the widest row. Mermaid widens the diagram for each band in turn,
        // which leaves later bands longer than earlier ones; here they all end at the same edge.
        var bandRight = contentWidth + 8;
        if (rows.Any(static _ => _.Node.IsHighlighted))
        {
            contentWidth = bandRight + 2;
        }

        var contentHeight = rows.Count * rowHeight;

        var titleFontSize = fontSize + 4;
        var titleHeight = 0.0;
        if (model.Title is { } title)
        {
            titleHeight = titleFontSize * SvgBuilder.LineHeightFactor + titleGap;
            contentWidth = Math.Max(contentWidth, MeasureText(title, titleFontSize, true));
        }

        var offsetX = options.Padding;
        var offsetY = options.Padding + titleHeight;

        var builder = new SvgBuilder();
        builder.Options(options);
        builder.Size(contentWidth + options.Padding * 2, contentHeight + titleHeight + options.Padding * 2);

        if (model.Title is not null)
        {
            builder.AddText(
                offsetX + contentWidth / 2,
                options.Padding + titleFontSize * SvgBuilder.LineHeightFactor / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: titleFontSize,
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: labelColor);
        }

        // The bands go first so the lines and labels are drawn over them.
        foreach (var row in rows.Where(static _ => _.Node.IsHighlighted))
        {
            builder.AddRect(
                row.X + offsetX,
                row.Y + 1 + offsetY,
                bandRight - row.X,
                rowHeight - 2,
                rx: 3,
                fill: highlightFill,
                stroke: highlightStroke,
                strokeWidth: 1);
        }

        foreach (var (x1, y1, x2, y2) in lines)
        {
            builder.AddLine(x1 + offsetX, y1 + offsetY, x2 + offsetX, y2 + offsetY, stroke: lineColor, strokeWidth: lineThickness);
        }

        foreach (var row in rows)
        {
            var centerY = row.Y + rowHeight / 2 + offsetY;
            if (row.Icon is { } icon)
            {
                DrawIcon(builder, icon, row.X + paddingX + offsetX, centerY - iconSize / 2, fontSize, options.FontFamily);
            }

            // Labels are drawn exactly as written, so runs of spaces must not collapse.
            builder.AddText(
                row.LabelX + offsetX,
                centerY,
                row.Node.Name,
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fontWeight: row.Node.IsDirectory ? "bold" : null,
                fill: labelColor,
                cssClass: row.Node.CssClass,
                style: "white-space:pre");

            if (row.Node.Description is { } description)
            {
                builder.AddText(
                    descriptionX + offsetX,
                    centerY,
                    description,
                    baseline: "middle",
                    fontSize: fontSize,
                    fontFamily: options.FontFamily,
                    fill: descriptionColor,
                    style: "font-style:italic;white-space:pre");
            }
        }

        return builder.Build();

        // Places a node's row and then its children's below it, and returns the middle of the node's row.
        double Layout(TreeViewNode node, int depth)
        {
            var x = depth * (rowIndent + paddingX);
            var y = rows.Count * rowHeight;
            var centerY = y + rowHeight / 2;
            var icon = ResolveIcon(node, icons);
            var iconOffset = icon is null ? 0 : iconSize + iconGap;
            var labelWidth = MeasureText(node.Name, fontSize, node.IsDirectory);
            var labelX = x + paddingX + iconOffset;
            rows.Add(new(node, icon, x, y, labelX, labelX + labelWidth, labelWidth + paddingX * 2 + iconOffset));

            // The root's own connector would sit left of the diagram, where Mermaid's view box cuts it off.
            if (depth > 0)
            {
                lines.Add((x - rowIndent, centerY, x, centerY));
            }

            var lastChildCenterY = 0.0;
            foreach (var child in node.Children)
            {
                lastChildCenterY = Layout(child, depth + 1);
            }

            if (node.Children.Count > 0)
            {
                lines.Add((x + paddingX, y + rowHeight, x + paddingX, lastChildCenterY + lineThickness / 2));
            }

            return centerY;
        }
    }

    // The `treeView` options that decide which icon a node shows.
    sealed record IconSettings(
        bool ShowIcons,
        string DefaultIconPack,
        Dictionary<string, object?>? FilenameIcons,
        Dictionary<string, object?>? ExtensionIcons);

    /// <summary>
    /// The icon a node shows, as Mermaid picks it. An explicit <c>icon()</c> always wins, and
    /// <c>icon(none)</c> shows none. Otherwise only with <c>showIcons</c>: a file named in
    /// <c>filenameIcons</c>, or whose extension is in <c>extensionIcons</c> (either may say <c>none</c>),
    /// takes that icon, and anything else the built-in file or folder icon. An unprefixed name is a
    /// built-in icon if there is one by that name, else one from <c>defaultIconPack</c>.
    /// </summary>
    static string? ResolveIcon(TreeViewNode node, IconSettings settings)
    {
        if (node.Icon == "none")
        {
            return null;
        }

        if (node.Icon is not null)
        {
            return QualifyIcon(node.Icon, settings);
        }

        if (!settings.ShowIcons)
        {
            return null;
        }

        if (!node.IsDirectory)
        {
            var detected = settings.FilenameIcons?.GetValueOrDefault(node.Name) as string;
            var dot = node.Name.LastIndexOf('.');
            if (string.IsNullOrEmpty(detected) &&
                dot > 0)
            {
                var extension = node.Name[dot..].ToLowerInvariant();
                detected = settings.ExtensionIcons?.GetValueOrDefault(extension) as string ??
                           settings.ExtensionIcons?.GetValueOrDefault(extension[1..]) as string;
            }

            if (detected == "none")
            {
                return null;
            }

            if (!string.IsNullOrEmpty(detected))
            {
                return QualifyIcon(detected, settings);
            }
        }

        if (node.IsDirectory)
        {
            return "folder";
        }

        return "file";
    }

    static string QualifyIcon(string icon, IconSettings settings)
    {
        if (icon.Contains(':') ||
            icon is "file" or "folder" ||
            settings.DefaultIconPack.Length == 0)
        {
            return icon;
        }

        return $"{settings.DefaultIconPack}:{icon}";
    }

    static void DrawIcon(SvgBuilder builder, string icon, double x, double y, double fontSize, string fontFamily)
    {
        var body = icon switch
        {
            "file" or "mermaid-treeview:file" => fileIcon,
            "folder" or "mermaid-treeview:folder" => folderIcon,
            _ => null
        };

        var width = 24.0;
        var height = 24.0;
        if (body is null &&
            icon.Contains(':') &&
            IconPackRegistry.Resolve(icon) is { } packIcon)
        {
            body = packIcon.Body;
            width = packIcon.Width;
            height = packIcon.Height;
        }

        if (body is null)
        {
            // Mermaid draws an icon it cannot find as a question mark.
            builder.AddText(
                x + iconSize / 2,
                y + iconSize / 2,
                "?",
                anchor: "middle",
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: fontFamily,
                fontWeight: "bold",
                fill: iconColor);
            return;
        }

        var scale = iconSize / Math.Max(width, height);
        var left = x + (iconSize - width * scale) / 2;
        var top = y + (iconSize - height * scale) / 2;
        builder.AddRawSvg(string.Create(
            CultureInfo.InvariantCulture,
            $"<g transform='translate({left:0.##},{top:0.##}) scale({scale:0.####})' style='color:{iconColor}'>{body}</g>"));
    }

    static double MeasureText(string text, double fontSize, bool bold) =>
        text.Length * fontSize * (bold ? 0.65 : 0.55);

    /// <summary>
    /// A laid-out row. <see cref="X"/> is where the connector from the parent ends, <see cref="LabelRight"/>
    /// the estimated right edge of the label (descriptions align from the rightmost), and
    /// <see cref="Width"/> that of the icon and label with their padding.
    /// </summary>
    sealed record Row(TreeViewNode Node, string? Icon, double X, double Y, double LabelX, double LabelRight, double Width);
}
