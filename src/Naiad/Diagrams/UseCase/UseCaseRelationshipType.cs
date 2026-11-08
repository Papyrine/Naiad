namespace Naiad.Diagrams.UseCase;

public enum UseCaseRelationshipType
{
    /// <summary>A solid line, <c>--&gt;</c> and its variants.</summary>
    Association,

    /// <summary><c>..&gt; : include</c>, a dashed arrow labelled «include».</summary>
    Include,

    /// <summary><c>..&gt; : extend</c>, a dashed arrow labelled «extend».</summary>
    Extend,

    /// <summary><c>--|&gt;</c>, a solid line ending in a hollow triangle.</summary>
    Generalization
}
