namespace Naiad.Diagrams.Sequence;

public class Critical : SequenceElement
{
    public string? Label { get; set; }
    public List<SequenceElement> Elements { get; } = [];
    public List<CriticalOption> OptionBranches { get; } = [];
}