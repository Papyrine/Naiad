namespace Naiad;

public class SvgRect : SvgElement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rx { get; set; }
    public double Ry { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }

    public override void ToXml(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"<rect x='{X:0.##}' y='{Y:0.##}' width='{Width:0.##}' height='{Height:0.##}'");

        if (Rx > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $" rx='{Rx:0.##}'");
        }

        if (Ry > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $" ry='{Ry:0.##}'");
        }

        if (Fill is not null)
        {
            builder.AppendAttribute("fill", Fill);
        }

        if (Stroke is not null)
        {
            builder.AppendAttribute("stroke", Stroke);
        }

        // stroke-width 1 is the SVG default — omit it.
        if (StrokeWidth is { } strokeWidth && strokeWidth != 1)
        {
            builder.Append(CultureInfo.InvariantCulture, $" stroke-width='{strokeWidth:0.##}'");
        }

        CommonAttributes(builder);
        builder.Append("/>");
    }
}
