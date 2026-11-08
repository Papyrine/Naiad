namespace Naiad;

public class SvgText : SvgElement
{
    public double X { get; init; }
    public double Y { get; init; }
    // When true, don't output x/y attributes (for transformed text)
    public bool OmitXY { get; init; }
    public required string Content { get; init; }
    public string? TextAnchor { get; init; }
    public string? DominantBaseline { get; init; }
    public double? FontSize { get; init; }
    public string? FontFamily { get; init; }
    public string? FontWeight { get; init; }
    public string? Fill { get; init; }

    public override void ToXml(StringBuilder builder)
    {
        builder.Append("<text");

        // For transformed text (OmitXY=true), Mermaid uses: transform, class, style order
        if (OmitXY)
        {
            if (Transform is not null)
            {
                builder.AppendAttribute("transform", Transform);
            }

            if (Class is not null)
            {
                builder.AppendAttribute("class", Class);
            }

            if (Style is not null)
            {
                builder.AppendAttribute("style", Style);
            }
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture, $" x='{X:0.##}' y='{Y:0.##}'");
            if (TextAnchor is not null)
            {
                builder.AppendAttribute("text-anchor", TextAnchor);
            }

            if (DominantBaseline is not null)
            {
                builder.AppendAttribute("dominant-baseline", DominantBaseline);
            }

            if (FontSize is not null)
            {
                builder.Append(CultureInfo.InvariantCulture, $" font-size='{FontSize:0.##}px'");
            }

            if (FontFamily is not null)
            {
                builder.AppendAttribute("font-family", FontFamily);
            }

            // "normal" is the default weight — omit it, keeping only meaningful weights (e.g. bold).
            if (FontWeight is not null and not "normal")
            {
                builder.AppendAttribute("font-weight", FontWeight);
            }

            if (Fill is not null)
            {
                builder.AppendAttribute("fill", Fill);
            }

            CommonAttributes(builder);
        }

        if (string.IsNullOrEmpty(Content))
        {
            builder.Append("/>");
        }
        else
        {
            builder.Append('>');
            SvgXml.AppendText(builder, Content);
            builder.Append("</text>");
        }
    }
}
