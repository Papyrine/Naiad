namespace Naiad;

public class SvgGradient
{
    public required string Id { get; init; }
    public List<SvgGradientStop> Stops { get; } = [];
    public bool IsRadial { get; set; }

    public void ToXml(StringBuilder builder)
    {
        var tag = IsRadial ? "radialGradient" : "linearGradient";
        builder.Append($"<{tag}");
        builder.AppendAttribute("id", Id);
        builder.Append('>');

        foreach (var stop in Stops)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<stop offset='{stop.Offset}%' style='stop-color:{SvgXml.EscapeAttribute(stop.Color)}' />");
        }

        builder.Append($"</{tag}>");
    }
}