// Shared State diagram inputs, used by both the snapshot tests (StateTests) and the layout
// overlap guard (StateOverlapTests) so every sampled diagram is covered by both.
static class StateSamples
{
    public const string Simple =
        """
        stateDiagram-v2
            [*] --> Still
            Still --> [*]
        """;

    public const string MultipleStates =
        """
        stateDiagram-v2
            [*] --> Still
            Still --> Moving
            Moving --> Still
            Moving --> Crash
            Crash --> [*]
        """;

    public const string TransitionLabels =
        """
        stateDiagram-v2
            [*] --> Active
            Active --> Inactive : timeout
            Inactive --> Active : reset
            Active --> [*] : shutdown
        """;

    // `direction LR` ranks along X, so the terminal-marker alignment has to work on the cross axis.
    // Aligning it on X unconditionally put the start's child on top of its own neighbour.
    public const string DirectionLeftToRight =
        """
        stateDiagram-v2
            direction LR
            [*] --> Queued
            Queued --> Running
            Running --> Done
            Done --> [*]
        """;

    // A composite is laid out from its own contents and drawn as a container around them. Nested states
    // used to join the outer layout as flat siblings, so the box came out empty with its children beside it.
    public const string CompositeState =
        """
        stateDiagram-v2
            [*] --> Ready
            Ready --> Working
            state Working {
                [*] --> Fetch
                Fetch --> Parse : ok
                Parse --> [*]
            }
            Working --> Done
            Done --> [*]
        """;

    // `[*]` names the start of whichever region it sits in, so the markers have to be scoped per composite.
    public const string NestedCompositeState =
        """
        stateDiagram-v2
            [*] --> Outer
            state Outer {
                [*] --> Mid
                state Mid {
                    [*] --> Leaf
                    Leaf --> [*]
                }
                Mid --> [*]
            }
            Outer --> [*]
        """;

    public const string Description =
        """
        stateDiagram-v2
            state "This is a state description" as s1
            [*] --> s1
            s1 --> [*]
        """;

    public const string ForkJoinState =
        """
        stateDiagram-v2
            state fork_state <<fork>>
            [*] --> fork_state
            fork_state --> State2
            fork_state --> State3
        """;

    public const string ChoiceState =
        """
        stateDiagram-v2
            state choice_state <<choice>>
            [*] --> IsPositive
            IsPositive --> choice_state
            choice_state --> Positive : if n > 0
            choice_state --> Negative : if n < 0
        """;

    public const string StateWithNote =
        """
        stateDiagram-v2
            [*] --> Active
            Active --> [*]
            note right of Active : Important note
        """;

    public const string StateDiagramV1 =
        """
        stateDiagram
            [*] --> Still
            Still --> [*]
        """;

    public const string Complex =
        """
        stateDiagram-v2
            [*] --> Idle

            state "Processing State" as Processing
            state fork_state <<fork>>
            state join_state <<join>>
            state choice_state <<choice>>

            Idle --> Processing : start
            Processing --> fork_state
            fork_state --> TaskA
            fork_state --> TaskB
            TaskA --> join_state
            TaskB --> join_state
            join_state --> choice_state
            choice_state --> Success : if valid
            choice_state --> Error : if invalid
            Success --> Idle : reset
            Error --> Idle : retry
            Success --> [*] : complete

            note right of Processing : This is a processing note
            note left of Error : Error handling
        """;

    // `<br/>` breaks a state description, a transition label and a note over several lines.
    public const string LineBreaks =
        """
        stateDiagram-v2
            [*] --> A
            A : First line<br/>second line
            A --> B : go<br/>now
            B --> C : plain
            note right of B : Note one<br/>note two<br/>note three
            C --> [*]
        """;
}
