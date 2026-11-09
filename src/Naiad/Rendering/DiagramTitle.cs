/// <summary>
/// The title Mermaid draws centred above a flowchart, class, state, ER or git graph diagram, taken from the
/// front matter's <c>title:</c>. The diagram is laid out without it, then moved down to make room and
/// widened where the title is wider than the drawing.
/// </summary>
static class DiagramTitle
{
    // Mermaid's title text is 18px, with its band about twice that above the drawing.
    const double fontSize = 18;
    const double bandHeight = fontSize * 2;
    const double sidePadding = 10;

    public static void Add(SvgDocument document, string? title, RenderOptions options)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var (minX, minY, width, height) = ViewBox(document);
        var titleWidth = title.Length * fontSize * 0.55 + sidePadding * 2;
        var newWidth = Math.Max(width, titleWidth);
        var shiftX = (newWidth - width) / 2;

        var drawing = new SvgGroup
        {
            Transform = string.Create(CultureInfo.InvariantCulture, $"translate({shiftX:0.##},{bandHeight:0.##})")
        };
        drawing.Children.AddRange(document.Elements);
        document.Elements.Clear();
        document.Elements.Add(drawing);
        document.Elements.Add(
            new SvgText
            {
                X = minX + newWidth / 2,
                Y = minY + bandHeight / 2,
                Content = title,
                TextAnchor = "middle",
                DominantBaseline = "middle",
                FontSize = fontSize,
                FontFamily = options.FontFamily,
                Fill = "#333",
                Class = "diagramTitleText"
            });

        // The rendered size keeps its ratio to the viewBox, which some diagrams scale.
        var scaleX = width > 0 ? document.Width / width : 1;
        var scaleY = height > 0 ? document.Height / height : 1;
        document.Width = newWidth * scaleX;
        document.Height = (height + bandHeight) * scaleY;
        if (document.ViewBoxOverride is not null)
        {
            document.ViewBoxOverride = string.Create(
                CultureInfo.InvariantCulture,
                $"{minX:0.##} {minY:0.##} {newWidth:0.##} {height + bandHeight:0.##}");
        }
    }

    static (double minX, double minY, double width, double height) ViewBox(SvgDocument document)
    {
        if (document.ViewBoxOverride is { } viewBox)
        {
            var parts = viewBox.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4)
            {
                return (
                    double.Parse(parts[0], CultureInfo.InvariantCulture),
                    double.Parse(parts[1], CultureInfo.InvariantCulture),
                    double.Parse(parts[2], CultureInfo.InvariantCulture),
                    double.Parse(parts[3], CultureInfo.InvariantCulture));
            }
        }

        return (0, 0, document.Width, document.Height);
    }
}
