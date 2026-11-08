namespace Naiad.Diagrams.Info;

public class InfoModel : DiagramBase
{
    /// <summary>
    /// Whether the diagram asked for <c>showInfo</c>. Mermaid accepts the flag but draws the same version
    /// line either way, so it is kept only so callers can see it was given.
    /// </summary>
    public bool ShowInfo { get; set; }
}
