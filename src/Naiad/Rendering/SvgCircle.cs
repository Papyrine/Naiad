namespace Naiad;

public class SvgCircle : SvgElement
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double R { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }

    public override void ToXml(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"<circle cx='{Cx:0.##}' cy='{Cy:0.##}' r='{R:0.##}'");

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
