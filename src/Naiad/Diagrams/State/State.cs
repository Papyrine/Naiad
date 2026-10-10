namespace Naiad.Diagrams.State;

public class State
{
    public required string Id { get; init; }
    public string? Description { get; set; }
    public StateType Type { get; set; } = StateType.Normal;
    public List<State> NestedStates { get; } = [];
    public List<StateTransition> NestedTransitions { get; } = [];

    /// <summary>The direction a composite lays its own contents out in, when it declares one.</summary>
    public Direction? Direction { get; set; }

    // Layout properties
    public Position Position { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    // The width of a composite's laid-out contents, which its box may be wider than.
    internal double InteriorWidth { get; set; }

    public bool IsComposite => NestedStates.Count > 0;
}