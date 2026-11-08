namespace Naiad;

public class SvgPolygon : SvgElement
{
    public List<Position> Points { get; } = [];
    public string? Fill { get; set; }
    public string? Stroke { get; set; }

    public override void ToXml(StringBuilder builder)
    {
        builder.Append("<polygon points='");
        for (var i = 0; i < Points.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            var point = Points[i];
            builder.Append(CultureInfo.InvariantCulture, $"{point.X:0.##},{point.Y:0.##}");
        }

        builder.Append('\'');

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
