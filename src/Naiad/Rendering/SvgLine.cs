namespace Naiad;

public class SvgLine : SvgElement
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }
    public string? StrokeDasharray { get; set; }

    public override void ToXml(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"<line x1='{X1:0.##}' y1='{Y1:0.##}' x2='{X2:0.##}' y2='{Y2:0.##}'");

        if (Stroke is not null)
        {
            builder.AppendAttribute("stroke", Stroke);
        }

        // stroke-width 1 is the SVG default — omit it.
        if (StrokeWidth is { } strokeWidth && strokeWidth != 1)
        {
            builder.Append(CultureInfo.InvariantCulture, $" stroke-width='{strokeWidth:0.##}'");
        }

        if (StrokeDasharray is not null)
        {
            builder.AppendAttribute("stroke-dasharray", StrokeDasharray);
        }

        CommonAttributes(builder);
        builder.Append("/>");
    }
}
