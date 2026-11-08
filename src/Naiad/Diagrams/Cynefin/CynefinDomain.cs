namespace Naiad.Diagrams.Cynefin;

public class CynefinDomain
{
    public required CynefinDomainName Name { get; init; }
    public List<string> Items { get; } = [];
}
