namespace Naiad.Diagrams.Sequence;

public class SequenceModel : DiagramBase
{
    public List<Participant> Participants { get; } = [];
    public List<SequenceElement> Elements { get; } = [];
    public bool AutoNumber { get; set; }

    // `autonumber 10 5`: the first message's number and the step to the next.
    public int AutoNumberStart { get; set; } = 1;
    public int AutoNumberStep { get; set; } = 1;

    // `box ... end` groups of participants.
    public List<ParticipantBox> Boxes { get; } = [];
}