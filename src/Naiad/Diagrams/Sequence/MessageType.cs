namespace Naiad.Diagrams.Sequence;

public enum MessageType
{
    Solid,           // ->>
    SolidArrow,      // ->>
    Dotted,          // -->>
    DottedArrow,     // -->>
    SolidOpen,       // ->
    DottedOpen,      // -->
    SolidCross,      // -x
    DottedCross,     // --x
    SolidAsync,      // -)
    DottedAsync,     // --)
    BiDirectional,       // <<->>
    DottedBiDirectional  // <<-->>
}