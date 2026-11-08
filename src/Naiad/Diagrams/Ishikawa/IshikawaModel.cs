namespace Naiad.Diagrams.Ishikawa;

public class IshikawaModel : DiagramBase
{
    /// <summary>The effect (problem) at the fish's head; its children are the causes along the spine.</summary>
    public IshikawaNode? Root { get; set; }
}
