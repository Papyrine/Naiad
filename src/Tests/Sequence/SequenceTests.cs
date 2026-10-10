public class SequenceTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            sequenceDiagram
                Alice->>Bob: Hello Bob
                Bob-->>Alice: Hi Alice
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Participants()
    {
        const string input =
            """
            sequenceDiagram
                participant A as Alice
                participant B as Bob
                A->>B: Hello
                B->>A: Hi
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Actors()
    {
        const string input =
            """
            sequenceDiagram
                actor User
                participant Server
                User->>Server: Request
                Server-->>User: Response
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Activation()
    {
        const string input =
            """
            sequenceDiagram
                Alice->>+Bob: Hello
                Bob-->>-Alice: Hi
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Notes()
    {
        const string input =
            """
            sequenceDiagram
                Alice->>Bob: Hello
                Note right of Bob: Bob thinks
                Bob-->>Alice: Hi
                Note over Alice,Bob: Conversation
            """;

        return VerifySvg(input);
    }

    // A note left of the first participant hangs off the left edge, so the whole diagram has to shift
    // right rather than the note being clipped.
    [Test]
    public Task NoteLeftOfFirstParticipant()
    {
        const string input =
            """
            sequenceDiagram
                Alice->>Bob: Hello
                Note left of Alice: Alice considers it at length
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ExplicitActivation()
    {
        const string input =
            """
            sequenceDiagram
                Alice->>Bob: Hello
                activate Bob
                Bob-->>Alice: Hi
                deactivate Bob
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task AutoNumber()
    {
        const string input =
            """
            sequenceDiagram
                autonumber
                Alice->>Bob: Hello
                Bob->>Alice: Hi
                Alice->>Bob: How are you?
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DifferentArrows()
    {
        const string input =
            """
            sequenceDiagram
                A->>B: Solid arrow
                A-->>B: Dotted arrow
                A->B: Solid open
                A-->B: Dotted open
                A-xB: Solid cross
                A--xB: Dotted cross
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Title()
    {
        const string input =
            """
            sequenceDiagram
                title Authentication Flow
                Client->>Server: Login request
                Server-->>Client: Token
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        const string input =
            """
            sequenceDiagram
                title Complete Authentication Flow
                autonumber

                actor User
                participant Client as Web Client
                participant Auth as Auth Service
                participant DB as Database
                participant Email as Email Service

                User->>+Client: Enter credentials
                Client->>+Auth: POST /login
                Auth->>+DB: Query user
                DB-->>-Auth: User data
                Note right of Auth: Validate credentials
                Auth->>Auth: Generate JWT
                Note right of Auth: Token expires in 24h
                Auth-->>-Client: 200 OK + Token
                Client->>+Email: Send welcome email
                Email-->>-Client: Email sent
                Client-->>-User: Show dashboard
                Note over User,DB: Session established
            """;

        return VerifySvg(input);
    }

    // Every kind of block: a frame with its name in the corner and its condition alongside, a dashed
    // divider for each `else` / `and` / `option`, nesting, and a `rect` background.
    [Test]
    public Task Blocks()
    {
        const string input =
            """
            sequenceDiagram
                participant Alice
                participant Bob
                participant John
                Alice->>Bob: Hello
                loop Every minute
                    Bob->>Alice: ping
                end
                alt is sick
                    Bob->>Alice: Not so good
                else is well
                    Bob->>Alice: Feeling fresh
                end
                opt Extra response
                    Bob->>Alice: Thanks
                end
                par Alice to Bob
                    Alice->>Bob: Hello Bob
                and Alice to John
                    Alice->>John: Hello John
                end
                critical Establish a connection
                    Alice->>Bob: connect
                option Network timeout
                    Alice->>Alice: Log error
                end
                break when it fails
                    Alice->>Bob: failed
                end
                rect rgb(191, 223, 255)
                    Note over Alice,Bob: Highlighted
                    Alice->>John: Bye
                end
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task NestedBlocks()
    {
        const string input =
            """
            sequenceDiagram
                loop Outer
                    Alice->>Bob: before
                    alt A
                        Bob->>John: one
                    else B
                        loop Inner
                            John->>Bob: two
                            Note right of John: Inside both
                        end
                    end
                end
                Alice->>John: after
            """;

        return VerifySvg(input);
    }

    // `<br/>` breaks participant names, message labels and notes over several lines, and each makes room
    // for the lines it holds.
    [Test]
    public Task LineBreaks()
    {
        const string input =
            """
            sequenceDiagram
                participant A as Alice<br/>Johnson
                actor B as Bob<br/>the Builder
                participant C
                A->>B: Hello Bob<br/>how are you?
                B-->>A: Fine
                Note over A,B: First line<br/>second line<br/>third line
                A->>A: Think<br/>hard
                B->>C: One<br/>two<br/>three
                Note right of C: Single
            """;

        return VerifySvg(input);
    }
}
