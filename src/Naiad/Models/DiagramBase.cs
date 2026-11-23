namespace Naiad;

public abstract class DiagramBase
{
    public string? Title { get; set; }
    public Direction Direction { get; set; } = Direction.TopToBottom;

    /// <summary>
    /// The diagram's <c>accTitle</c>. It is not drawn: the SVG carries it as its <c>&lt;title&gt;</c>, which
    /// the root names with <c>aria-labelledby</c>, as Mermaid writes it.
    /// </summary>
    public string? AccessibilityTitle { get; set; }

    /// <summary>
    /// The diagram's <c>accDescr</c>. It is not drawn: the SVG carries it as its <c>&lt;desc&gt;</c>, which
    /// the root names with <c>aria-describedby</c>, as Mermaid writes it.
    /// </summary>
    public string? AccessibilityDescription { get; set; }

    /// <summary>
    /// The configuration the diagram was given in front matter and <c>%%{init}%%</c> directives, which the
    /// renderer reads its options and theme from.
    /// </summary>
    internal MermaidConfig Config { get; set; } = MermaidConfig.Empty;
}
