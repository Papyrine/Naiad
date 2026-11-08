namespace Naiad.Diagrams.Railroad;

/// <summary>
/// A railroad (syntax) diagram: a list of grammar rules, each drawn as its own track. All four of
/// Mermaid's railroad notations (its own constructor syntax, EBNF, ABNF and PEG) parse to this model.
/// </summary>
public class RailroadModel : DiagramBase
{
    public List<RailroadRule> Rules { get; } = [];
}

/// <summary>A named rule and the expression that defines it.</summary>
public sealed record RailroadRule(string Name, RailroadExpression Definition);

public abstract record RailroadExpression;

/// <summary>Literal text, drawn in a rounded box.</summary>
public sealed record RailroadTerminal(string Text) : RailroadExpression;

/// <summary>A reference to another rule, drawn in a square box.</summary>
public sealed record RailroadNonTerminal(string Name) : RailroadExpression;

/// <summary>Text that stands for something the notation cannot spell out, drawn in a dashed box.</summary>
public sealed record RailroadSpecial(string Text) : RailroadExpression;

public sealed record RailroadSequence(IReadOnlyList<RailroadExpression> Items) : RailroadExpression;

public sealed record RailroadChoice(IReadOnlyList<RailroadExpression> Alternatives) : RailroadExpression;

public sealed record RailroadOptional(RailroadExpression Item) : RailroadExpression;

/// <summary>
/// An expression repeated at least <see cref="Min"/> times and at most <see cref="Max"/> times, or without
/// limit when <see cref="Max"/> is null.
/// </summary>
public sealed record RailroadRepetition(RailroadExpression Item, int Min, int? Max) : RailroadExpression;
