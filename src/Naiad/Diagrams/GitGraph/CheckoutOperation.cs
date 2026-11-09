namespace Naiad.Diagrams.GitGraph;

public class CheckoutOperation : GitOperation
{
    public required string BranchName { get; init; }

    // Where the branch name starts in the text, to report a branch that does not exist.
    internal int Offset { get; init; }
}
