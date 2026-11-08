namespace Naiad;

public class SvgPath : SvgElement
{
    public required string D { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double? StrokeWidth { get; set; }
    public string? StrokeDasharray { get; set; }
    public string? MarkerStart { get; set; }
    public string? MarkerEnd { get; set; }
    public double? Opacity { get; set; }

    public override void ToXml(StringBuilder builder)
    {
        builder.Append($"<path d='{D}'");

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

        if (StrokeDasharray is not null)
        {
            builder.AppendAttribute("stroke-dasharray", StrokeDasharray);
        }

        if (MarkerStart is not null)
        {
            builder.AppendAttribute("marker-start", MarkerStart);
        }

        if (MarkerEnd is not null)
        {
            builder.AppendAttribute("marker-end", MarkerEnd);
        }

        if (Opacity.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" opacity='{Opacity.Value:0.##}'");
        }

        CommonAttributes(builder);
        builder.Append("/>");
    }
}
