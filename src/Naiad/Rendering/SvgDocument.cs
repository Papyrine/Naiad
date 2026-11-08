namespace Naiad;

public class SvgDocument
{
    public double Width { get; set; }
    public double Height { get; set; }
    public string? ViewBoxOverride { get; set; }
    public string ViewBox => ViewBoxOverride ?? string.Create(CultureInfo.InvariantCulture, $"0 0 {Width:0.######} {Height:0.##}");
    public List<SvgElement> Elements { get; } = [];
    public SvgDefs Defs { get; } = new();
    public string? CssStyles { get; set; }

    // Mermaid compatibility properties
    public string Id { get; set; } = "naiad";
    public string? DiagramClass { get; set; }
    public string? AriaRoledescription { get; set; }
    public string? FontAwesomeImport { get; set; } = "@import url(\"https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.7.2/css/all.min.css\");";

    public void ToXml(StringBuilder builder)
    {
        // Build mermaid-compatible SVG root element (attribute order matches Mermaid exactly)
        builder.Append($"<svg id='{Id}' width='100%' xmlns='http://www.w3.org/2000/svg'");

        if (!string.IsNullOrEmpty(DiagramClass))
        {
            builder.AppendAttribute("class", DiagramClass);
        }

        builder.Append($" viewBox='{ViewBox}'");
        builder.Append(CultureInfo.InvariantCulture, $" style='max-width: {Width:0.######}px;'");

        builder.Append(" role='graphics-document document'");

        if (!string.IsNullOrEmpty(AriaRoledescription))
        {
            builder.AppendAttribute("aria-roledescription", AriaRoledescription);
        }

        builder.Append('>');

        // Font Awesome import
        if (!string.IsNullOrEmpty(FontAwesomeImport))
        {
            builder.Append($"<style xmlns='http://www.w3.org/1999/xhtml'>{FontAwesomeImport}</style>");
        }

        // Main CSS styles
        if (CssStyles is not null)
        {
            builder.Append(
                $"""
                 <style>
                 {CssStyles}
                 </style>
                 """);
        }

        if (Defs.HasContent)
        {
            Defs.ToXml(builder);
        }

        foreach (var element in Elements)
        {
            element.ToXml(builder);
        }

        builder.Append("</svg>");
    }
}