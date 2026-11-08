namespace Naiad;

public class SvgEllipse : SvgElement
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double Rx { get; set; }
    public double Ry { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }

    public override void ToXml(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"<ellipse cx='{Cx:0.##}' cy='{Cy:0.##}' rx='{Rx:0.##}' ry='{Ry:0.##}'");

        if (Fill is not null)
        {
            builder.AppendAttribute("fill", Fill);
        }

        if (Stroke is not null)
        {
            builder.AppendAttribute("stroke", Stroke);
        }

        CommonAttributes(builder);
        builder.Append("/>");
    }
}
