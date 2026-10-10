namespace Naiad.Diagrams.Sequence;

public class Participant
{
    public required string Id { get; init; }
    public string? Alias { get; set; }
    public ParticipantType Type { get; set; } = ParticipantType.Participant;

    // `create participant X`: the participant appears part-way down, at the first message that involves it.
    public bool IsCreated { get; set; }

    // `destroy X`: the participant's lifeline ends, with a cross, at the last message that involves it.
    public bool IsDestroyed { get; set; }

    public string DisplayName => Alias ?? Id;
}