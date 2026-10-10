namespace Naiad.Diagrams.State;

public enum StateType
{
    Normal,
    Start,      // [*] as source
    End,        // [*] as target
    Fork,       // <<fork>>
    Join,       // <<join>>
    Choice,     // <<choice>>
    Region      // one of the concurrent parts a `--` divides a composite into
}