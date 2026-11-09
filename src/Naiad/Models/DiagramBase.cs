namespace Naiad;

public abstract class DiagramBase
{
    public string? Title { get; set; }
    public Direction Direction { get; set; } = Direction.TopToBottom;

    /// <summary>
    /// The configuration the diagram was given in front matter and <c>%%{init}%%</c> directives, which the
    /// renderer reads its options and theme from.
    /// </summary>
    internal MermaidConfig Config { get; set; } = MermaidConfig.Empty;
}
