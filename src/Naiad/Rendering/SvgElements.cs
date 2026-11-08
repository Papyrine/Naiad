namespace Naiad;

public abstract class SvgElement
{
    public string? Id { get; set; }
    public string? Class { get; set; }
    public string? Style { get; set; }
    public string? Transform { get; set; }

    public abstract void ToXml(StringBuilder builder);

    protected void CommonAttributes(StringBuilder builder)
    {
        if (Id is not null)
        {
            builder.AppendAttribute("id", Id);
        }

        if (Class is not null)
        {
            builder.AppendAttribute("class", Class);
        }

        if (Style is not null)
        {
            builder.AppendAttribute("style", Style);
        }

        if (Transform is not null)
        {
            builder.AppendAttribute("transform", Transform);
        }
    }
}

// Emits pre-formed SVG markup verbatim (e.g. an iconify icon body wrapped in a group).
public class SvgRaw : SvgElement
{
    public required string Markup { get; set; }

    public override void ToXml(StringBuilder builder) => builder.Append(Markup);
}