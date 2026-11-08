namespace Naiad;

public class RenderOptions
{
    public static RenderOptions Default => new();

    public double Padding { get; set; } = 20;
    public int FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Arial, sans-serif";

    // When false, labels are emitted as native SVG <text> instead of <foreignObject>
    // (and the Font Awesome @import is dropped), so the SVG renders without an HTML runtime.
    public bool AllowHtmlElements { get; set; } = true;

    /// <summary>
    /// When true, the SVG is indented one element per line for readability. Elements holding text
    /// (labels, <c>&lt;style&gt;</c>) and <c>&lt;foreignObject&gt;</c> labels are kept on one line, so
    /// the rendered output is unchanged. Defaults to false (compact markup).
    /// </summary>
    public bool PrettyPrint { get; set; }

    /// <summary>
    /// PNG rasterization settings, honoured by the Naiad.Skia and Naiad.ImageSharp render backends.
    /// Ignored by <see cref="Mermaid.Render(string, RenderOptions)"/>, which only produces SVG.
    /// </summary>
    public PngOptions Png { get; set; } = new();
}
