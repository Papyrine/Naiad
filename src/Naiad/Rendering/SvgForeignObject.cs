namespace Naiad;

public class SvgForeignObject : SvgElement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// The inner XHTML of the label, emitted verbatim inside the wrapping <c>&lt;span&gt;</c> (but for the
    /// control characters XML does not allow, which are dropped). This is a
    /// raw-markup seam: it is written without escaping because callers legitimately pass markup (e.g.
    /// <c>&lt;p&gt;</c> wrappers and <c>&lt;i class&gt;</c> icon elements). Any user-supplied <em>text</em>
    /// placed in here MUST be HTML-encoded by the caller (e.g. via
    /// <see cref="System.Net.WebUtility.HtmlEncode(string)"/>); unencoded text would both break the
    /// document's XML well-formedness and allow markup injection into the rendered SVG.
    /// </summary>
    public required string HtmlContent { get; set; }

    public override void ToXml(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"<foreignObject x='{X:0.##}' y='{Y:0.##}' width='{Width:0.##}' height='{Height:0.##}'");
        CommonAttributes(builder);
        builder.Append('>');
        builder.Append(CultureInfo.InvariantCulture, $"<div xmlns='http://www.w3.org/1999/xhtml' style='display: table-cell; white-space: nowrap; text-align: center; vertical-align: middle; width: {Width:0.##}px; height: {Height:0.##}px;'>");
        // Raw-markup seam: HtmlContent is emitted verbatim. Callers own escaping of any user text (see the
        // HtmlContent doc) — the content is XHTML built by the renderers, not plain text. Only the control
        // characters XML does not allow are dropped, which HTML encoding leaves in the text.
        builder.Append("<span>");
        SvgXml.AppendMarkup(builder, HtmlContent);
        builder.Append("</span>");
        builder.Append("</div>");
        builder.Append("</foreignObject>");
    }
}
