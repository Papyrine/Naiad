namespace Naiad;

public class SvgBuilder
{
    SvgDocument document = new();
    Stack<SvgGroup> groupStack = new();
    double padding;
    double contentWidth;
    double contentHeight;
    bool allowHtmlElements = true;
    double labelFontSize = 14;
    string labelFontFamily = "Arial, sans-serif";

    // Threads render options that affect element emission (currently the HTML/no-HTML
    // label seam and the fonts used for the native-text fallback).
    public void Options(RenderOptions options)
    {
        allowHtmlElements = options.AllowHtmlElements;
        labelFontSize = options.FontSize;
        labelFontFamily = options.FontFamily;
    }

    public void Size(double width, double height)
    {
        contentWidth = width;
        contentHeight = height;
        document.Width = width;
        document.Height = height;
    }

    public void Padding(double padding)
    {
        this.padding = padding;
        // Adjust document size to include padding on all sides
        document.Width = contentWidth + padding * 2;
        document.Height = contentHeight + padding * 2;
    }

    public void DiagramType(string diagramClass, string ariaRoledescription)
    {
        document.DiagramClass = diagramClass;
        document.AriaRoledescription = ariaRoledescription;
    }

    public void AddStyles(string css) => document.CssStyles = css;

    public void AddMarker(
        string id,
        string path,
        double width,
        double height,
        double refX,
        double refY,
        string? fill = null,
        string? markerUnits = null) =>
        document.Defs.Markers.Add(
            new()
            {
                Id = id,
                Path = path,
                MarkerWidth = width,
                MarkerHeight = height,
                RefX = refX,
                RefY = refY,
                Fill = fill,
                MarkerUnits = markerUnits
            });

    public void AddArrowMarker(
        string id = "arrowhead",
        string fill = "#333") =>
        AddMarker(id, "M0,0 L10,3.5 L0,7 Z", 10, 7, 9, 3.5, fill);

    public void AddCrossMarker(string id = "cross") =>
        document.Defs.Markers.Add(
            new()
            {
                Id = id,
                Path = "M1,1 L7,7 M7,1 L1,7",
                MarkerWidth = 8,
                MarkerHeight = 8,
                RefX = 4,
                RefY = 4,
                Fill = "none"
            });

    public void AddMermaidArrowMarker()
    {
        document.Defs.Markers.Add(
            new()
            {
                Id = "naiad_flowchart-pointEnd",
                Path = "M 0 0 L 10 5 L 0 10 z",
                MarkerWidth = 8,
                MarkerHeight = 8,
                // Tip (viewBox x=10) sits at the edge's end vertex — the target node's boundary — so the
                // arrowhead points flush at the node instead of poking under it. (circleEnd/crossEnd below
                // use the same out-of-node placement.)
                RefX = 10,
                RefY = 5,
                ViewBox = "0 0 10 10",
                MarkerUnits = "userSpaceOnUse",
                ClassName = "marker"
            });
        document.Defs.Markers.Add(
            new()
            {
                Id = "naiad_flowchart-pointStart",
                Path = "M 0 5 L 10 10 L 10 0 z",
                MarkerWidth = 8,
                MarkerHeight = 8,
                // Mirror of pointEnd: tip (viewBox x=0) sits at the edge's start vertex (the source node's
                // boundary) so the arrowhead points flush at the node rather than poking under it.
                RefX = 0,
                RefY = 5,
                ViewBox = "0 0 10 10",
                MarkerUnits = "userSpaceOnUse",
                ClassName = "marker"
            });
    }

    public void AddMermaidCircleMarker()
    {
        document.Defs.Markers.Add(
            new()
            {
                Id = "naiad_flowchart-circleEnd",
                Path = "",
                UseCircle = true,
                CircleCx = 5,
                CircleCy = 5,
                CircleR = 5,
                MarkerWidth = 11,
                MarkerHeight = 11,
                RefX = 11,
                RefY = 5,
                ViewBox = "0 0 10 10",
                MarkerUnits = "userSpaceOnUse",
                ClassName = "marker"
            });
        document.Defs.Markers.Add(
            new()
            {
                Id = "naiad_flowchart-circleStart",
                Path = "",
                UseCircle = true,
                CircleCx = 5,
                CircleCy = 5,
                CircleR = 5,
                MarkerWidth = 11,
                MarkerHeight = 11,
                RefX = -1,
                RefY = 5,
                ViewBox = "0 0 10 10",
                MarkerUnits = "userSpaceOnUse",
                ClassName = "marker"
            });
    }

    public void AddMermaidCrossMarker()
    {
        document.Defs.Markers.Add(
            new()
            {
                Id = "naiad_flowchart-crossEnd",
                Path = "M 1,1 l 9,9 M 10,1 l -9,9",
                MarkerWidth = 11,
                MarkerHeight = 11,
                RefX = 12,
                RefY = 5.2,
                ViewBox = "0 0 11 11",
                MarkerUnits = "userSpaceOnUse",
                ClassName = "marker cross",
                StrokeWidth = 2
            });
        document.Defs.Markers.Add(
            new()
            {
                Id = "naiad_flowchart-crossStart",
                Path = "M 1,1 l 9,9 M 10,1 l -9,9",
                MarkerWidth = 11,
                MarkerHeight = 11,
                RefX = -1,
                RefY = 5.2,
                ViewBox = "0 0 11 11",
                MarkerUnits = "userSpaceOnUse",
                ClassName = "marker cross",
                StrokeWidth = 2
            });
    }

    /// <summary>Adds a <c>&lt;foreignObject&gt;</c> carrying raw label markup.</summary>
    /// <remarks>
    /// <paramref name="htmlContent"/> is pre-built XHTML, emitted verbatim (see
    /// <see cref="SvgForeignObject.HtmlContent"/>). Any user-supplied text within it must already be
    /// HTML-encoded by the caller.
    /// </remarks>
    public void AddForeignObject(
        double x,
        double y,
        double width,
        double height,
        string htmlContent,
        string? className = null)
    {
        var foreignObject = new SvgForeignObject
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            HtmlContent = htmlContent,
            Class = className
        };
        AddElement(foreignObject);
    }

    /// <summary>
    /// Adds a box-centered label. With HTML allowed this is a <c>&lt;foreignObject&gt;</c> carrying the
    /// rich html; otherwise it is a native <c>&lt;text&gt;</c> built from the already-structured plainText
    /// (icons dropped, since they need a web font we do not embed). Empty labels emit nothing.
    /// </summary>
    /// <remarks>
    /// <paramref name="html"/> is pre-built XHTML used on the foreignObject path, emitted verbatim (see
    /// <see cref="SvgForeignObject.HtmlContent"/>). Any user-supplied text within it must already be
    /// HTML-encoded by the caller; the native-text fallback escapes <paramref name="plainText"/> itself.
    /// </remarks>
    public void AddLabel(
        double x,
        double y,
        double width,
        double height,
        string html,
        string plainText,
        string? className = null)
    {
        if (allowHtmlElements)
        {
            AddForeignObject(x, y, width, height, html, className);
            return;
        }

        var text = plainText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        AddTextLines(
            x + width / 2,
            y + height / 2,
            text,
            anchor: "middle",
            baseline: "middle",
            fontSize: labelFontSize,
            fontFamily: labelFontFamily,
            cssClass: className);
    }

    // Mermaid's default theme draws a translucent grey pill behind edge labels so the routed line
    // doesn't read through the text. Shared by every graph diagram (Flowchart, Class, ER, State) so the
    // colour and centering math stay in one place.
    public const string EdgeLabelBackground = "rgba(232,232,232,0.8)";

    // Background pill for an edge label, centred on (centerX, centerY). Flowchart draws its label text
    // itself (via the HTML-capable AddLabel), so it uses this directly; the text-based families go
    // through AddEdgeLabel below.
    public void AddEdgeLabelBackground(double centerX, double centerY, double width, double height) =>
        AddRect(
            centerX - width / 2,
            centerY - height / 2,
            width,
            height,
            fill: EdgeLabelBackground,
            stroke: "none",
            cssClass: "edgeLabel");

    // Background pill plus a centred <text> label, for the text-based diagram families (Class, ER, State).
    // fill controls the text colour only; pass null to inherit.
    public void AddEdgeLabel(
        double centerX,
        double centerY,
        double width,
        double height,
        string text,
        double fontSize,
        string? fontFamily,
        string? fill = null)
    {
        AddEdgeLabelBackground(centerX, centerY, width, height);
        AddTextLines(
            centerX,
            centerY,
            text,
            anchor: "middle",
            baseline: "middle",
            fontSize: fontSize,
            fontFamily: fontFamily,
            fill: fill);
    }

    public void BeginGroup(string? id = null, string? cssClass = null, string? transform = null)
    {
        var group = new SvgGroup
        {
            Id = id,
            Class = cssClass,
            Transform = transform
        };

        if (groupStack.TryPeek(out var parent))
        {
            parent.Children.Add(group);
        }
        else
        {
            document.Elements.Add(group);
        }

        groupStack.Push(group);
    }

    public void EndGroup()
    {
        if (groupStack.Count > 0)
        {
            groupStack.Pop();
        }
    }

    public void AddRect(
        double x,
        double y,
        double width,
        double height,
        double rx = 0,
        string? fill = null,
        string? stroke = null,
        double? strokeWidth = null,
        string? id = null,
        string? cssClass = null,
        string? style = null)
    {
        var rect = new SvgRect
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Rx = rx,
            Ry = rx,
            Fill = fill,
            Stroke = stroke,
            StrokeWidth = strokeWidth,
            Id = id,
            Class = cssClass,
            Style = style
        };
        AddElement(rect);
    }

    public void AddRectNoXY(double width, double height, string? style = null)
    {
        var rect = new SvgRectNoXY
        {
            Width = width,
            Height = height,
            Style = style
        };
        AddElement(rect);
    }

    public void AddCircle(
        double cx,
        double cy,
        double r,
        string? fill = null,
        string? stroke = null,
        double? strokeWidth = null,
        string? cssClass = null)
    {
        var circle = new SvgCircle
        {
            Cx = cx,
            Cy = cy,
            R = r,
            Fill = fill,
            Stroke = stroke,
            StrokeWidth = strokeWidth,
            Class = cssClass
        };
        AddElement(circle);
    }

    public void AddEllipse(
        double cx,
        double cy,
        double rx,
        double ry,
        string? fill = null,
        string? stroke = null)
    {
        var ellipse = new SvgEllipse
        {
            Cx = cx,
            Cy = cy,
            Rx = rx,
            Ry = ry,
            Fill = fill,
            Stroke = stroke
        };
        AddElement(ellipse);
    }

    public void AddLine(
        double x1,
        double y1,
        double x2,
        double y2,
        string? stroke = null,
        double? strokeWidth = null,
        string? strokeDasharray = null)
    {
        var line = new SvgLine
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeWidth = strokeWidth,
            StrokeDasharray = strokeDasharray
        };
        AddElement(line);
    }

    public void AddPath(
        string d,
        string? fill = null,
        string? stroke = null,
        double? strokeWidth = null,
        string? strokeDasharray = null,
        string? markerStart = null,
        string? markerEnd = null,
        double? opacity = null,
        string? cssClass = null)
    {
        var path = new SvgPath
        {
            D = d,
            Fill = fill,
            Stroke = stroke,
            StrokeWidth = strokeWidth,
            StrokeDasharray = strokeDasharray,
            MarkerStart = markerStart,
            MarkerEnd = markerEnd,
            Opacity = opacity,
            Class = cssClass
        };
        AddElement(path);
    }

    public void AddRawSvg(string markup) =>
        AddElement(new SvgRaw {Markup = markup});

    public void AddPolygon(
        IEnumerable<Position> points,
        string? fill = null,
        string? stroke = null)
    {
        var polygon = new SvgPolygon
        {
            Fill = fill,
            Stroke = stroke
        };
        polygon.Points.AddRange(points);
        AddElement(polygon);
    }

    public void AddText(
        double x,
        double y,
        string content,
        string? anchor = null,
        string? baseline = null,
        double? fontSize = null,
        string? fontFamily = null,
        string? fontWeight = null,
        string? fill = null,
        string? id = null,
        string? cssClass = null,
        string? transform = null,
        string? style = null,
        bool omitXY = false)
    {
        var text = new SvgText
        {
            X = x,
            Y = y,
            OmitXY = omitXY,
            Content = content,
            TextAnchor = anchor,
            DominantBaseline = baseline,
            FontSize = fontSize,
            FontFamily = fontFamily,
            FontWeight = fontWeight,
            Fill = fill,
            Id = id,
            Class = cssClass,
            Transform = transform,
            Style = style
        };
        AddElement(text);
    }

    /// <summary>Line pitch of stacked label lines, as a multiple of the font size.</summary>
    public const double LineHeightFactor = 1.2;

    /// <summary>
    /// Adds a label that may hold <c>&lt;br/&gt;</c> line breaks, as one <c>&lt;text&gt;</c> per line. The
    /// baseline says where the block sits relative to <paramref name="y"/>: <c>middle</c> centres it there,
    /// <c>bottom</c> rests its last line there and grows upwards, and anything else starts the first line
    /// there and grows downwards. A label without a break is exactly <see cref="AddText"/>.
    /// </summary>
    public void AddTextLines(
        double x,
        double y,
        string content,
        string? anchor = null,
        string? baseline = null,
        double? fontSize = null,
        string? fontFamily = null,
        string? fontWeight = null,
        string? fill = null,
        string? cssClass = null)
    {
        var lines = LabelLines.Split(content);
        var lineHeight = (fontSize ?? labelFontSize) * LineHeightFactor;
        var firstY = baseline switch
        {
            "middle" => y - (lines.Length - 1) * lineHeight / 2,
            "bottom" => y - (lines.Length - 1) * lineHeight,
            _ => y
        };

        for (var i = 0; i < lines.Length; i++)
        {
            AddText(
                x,
                firstY + i * lineHeight,
                lines[i],
                anchor: anchor,
                baseline: baseline,
                fontSize: fontSize,
                fontFamily: fontFamily,
                fontWeight: fontWeight,
                fill: fill,
                cssClass: cssClass);
        }
    }

    void AddElement(SvgElement element)
    {
        if (groupStack.TryPeek(out var parent))
        {
            parent.Children.Add(element);
        }
        else
        {
            document.Elements.Add(element);
        }
    }

    public SvgDocument Build()
    {
        // If padding is set, wrap all elements in a transform group
        if (padding > 0 &&
            document.Elements.Count > 0)
        {
            var paddingGroup = new SvgGroup
            {
                Transform = string.Create(CultureInfo.InvariantCulture, $"translate({padding:0.##},{padding:0.##})")
            };
            paddingGroup.Children.AddRange(document.Elements);
            document.Elements.Clear();
            document.Elements.Add(paddingGroup);
        }

        return document;
    }
}
