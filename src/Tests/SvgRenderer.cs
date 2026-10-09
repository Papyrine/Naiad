/// <summary>
/// Rasterizes Naiad's SVG output to PNG in-process using Svg.Skia (no browser).
///
/// Svg.Skia cannot render &lt;foreignObject&gt; (embedded XHTML), which is how
/// Mermaid/Naiad emit flowchart node and edge labels. So before rasterizing we
/// flatten each &lt;foreignObject&gt; label into a centered &lt;text&gt; element.
/// The flattening is applied only to the rasterization input — the verified
/// .svg files keep their original &lt;foreignObject&gt; markup to match Mermaid.
/// </summary>
public static class SvgRenderer
{
    static readonly XNamespace svgNs = "http://www.w3.org/2000/svg";

    const float scale = 2f;

    public static byte[] RenderToPng(string svg)
    {
        var flattened = FlattenForeignObjects(svg);

        using var skSvg = new SKSvg();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(flattened));
        if (skSvg.Load(input) == null)
        {
            throw new("Svg.Skia failed to load the SVG.");
        }

        using var output = new MemoryStream();
        skSvg.Save(output, SKColors.White, SKEncodedImageFormat.Png, 100, scale, scale);
        return output.ToArray();
    }

    static string FlattenForeignObjects(string svg)
    {
        var document = XDocument.Parse(svg);

        foreach (var foreignObject in document.Descendants(svgNs + "foreignObject").ToList())
        {
            var lines = ExtractLines(foreignObject);

            // foreignObjects without text are icon glyphs (e.g. font-awesome <i>),
            // which need a web font we do not embed. Drop them rather than render tofu.
            if (lines.Count == 0)
            {
                foreignObject.Remove();
                continue;
            }

            var x = Double(foreignObject.Attribute("x"));
            var y = Double(foreignObject.Attribute("y"));
            var width = Double(foreignObject.Attribute("width"));
            var height = Double(foreignObject.Attribute("height"));

            // One <text> per <br/>-separated line, the block centred in the box.
            const double lineHeight = 16 * 1.2;
            var firstY = y + height / 2 - (lines.Count - 1) * lineHeight / 2;
            foreignObject.ReplaceWith(
                lines.Select((line, index) =>
                    new XElement(
                        svgNs + "text",
                        new XAttribute("x", Format(x + width / 2)),
                        new XAttribute("y", Format(firstY + index * lineHeight)),
                        new XAttribute("text-anchor", "middle"),
                        new XAttribute("dominant-baseline", "middle"),
                        new XAttribute("font-size", "16px"),
                        new XAttribute("font-family", "trebuchet ms, verdana, arial, sans-serif"),
                        new XAttribute("fill", "#333"),
                        line)));
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    // The label's text, split where its <p> holds a <br/>. A label that is all whitespace has no lines.
    static List<string> ExtractLines(XElement foreignObject)
    {
        var lines = new List<string>();
        var paragraph = foreignObject.Descendants().FirstOrDefault(_ => _.Name.LocalName == "p");
        if (paragraph is null)
        {
            return lines;
        }

        var line = new StringBuilder();
        foreach (var node in paragraph.Nodes())
        {
            switch (node)
            {
                case XElement {Name.LocalName: "br"}:
                    lines.Add(line.ToString().Trim());
                    line.Clear();
                    break;
                case XElement element:
                    line.Append(element.Value);
                    break;
                case XText text:
                    line.Append(text.Value);
                    break;
            }
        }

        lines.Add(line.ToString().Trim());
        if (lines.All(string.IsNullOrWhiteSpace))
        {
            lines.Clear();
        }

        return lines;
    }

    static double Double(XAttribute? attribute) =>
        double.Parse(attribute!.Value, CultureInfo.InvariantCulture);

    static string Format(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
