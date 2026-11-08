namespace Naiad.Diagrams.Cynefin;

public class CynefinTransition
{
    public required CynefinDomainName From { get; init; }
    public required CynefinDomainName To { get; init; }
    public string? Label { get; init; }
}
