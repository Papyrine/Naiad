namespace Naiad.Diagrams.Ishikawa;

public class IshikawaNode
{
    public required string Text { get; init; }
    public List<IshikawaNode> Children { get; } = [];
}
