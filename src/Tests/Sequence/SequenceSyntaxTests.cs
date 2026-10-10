using Naiad.Diagrams.Sequence;

// Sequence syntax that used to be a parse error.
public class SequenceSyntaxTests
{
    static SequenceModel Parse(string input) =>
        new SequenceParser().Parse(input).Value;

    [Test]
    public async Task HyphenatedNames_AreParticipantIds()
    {
        var model = Parse(
            """
            sequenceDiagram
                participant web-app as Web App
                web-app->>api-srv: GET /x
                api-srv-->>web-app: 200
                api-srv-xweb-app: dropped
                Note over web-app,api-srv: both
            """);

        await Assert.That(string.Join(",", model.Participants.Select(_ => _.Id))).IsEqualTo("web-app,api-srv");
        var messages = model.Elements.OfType<Message>().ToList();
        await Assert.That(string.Join(",", messages.Select(_ => $"{_.FromId}>{_.ToId}:{_.Type}")))
            .IsEqualTo("web-app>api-srv:SolidArrow,api-srv>web-app:DottedArrow,api-srv>web-app:SolidCross");
        var note = model.Elements.OfType<Note>().Single();
        await Assert.That(note.ParticipantId).IsEqualTo("web-app");
        await Assert.That(note.OverParticipantId2).IsEqualTo("api-srv");
    }

    [Test]
    [Arguments("<<->>", MessageType.BiDirectional)]
    [Arguments("<<-->>", MessageType.DottedBiDirectional)]
    public async Task TwoHeadedArrow_Parses(string arrow, MessageType type)
    {
        var model = Parse($"sequenceDiagram\n    A{arrow}B: both ways");

        var message = (Message) model.Elements.Single();
        await Assert.That(message.Type).IsEqualTo(type);
        await Assert.That(message.Text).IsEqualTo("both ways");
    }

    [Test]
    public async Task TwoHeadedArrow_HasAHeadAtEachEnd()
    {
        static int Heads(string arrow) =>
            Regex.Matches(Mermaid.Render($"sequenceDiagram\n    A{arrow}B: x"), "<polygon").Count;

        await Assert.That(Heads("<<->>")).IsEqualTo(Heads("->>") + 1);
        await Assert.That(Heads("<<-->>")).IsEqualTo(Heads("-->>") + 1);
    }

    [Test]
    [Arguments("autonumber", true, 1, 1)]
    [Arguments("autonumber 10", true, 10, 1)]
    [Arguments("autonumber 10 5", true, 10, 5)]
    [Arguments("autonumber off", false, 1, 1)]
    public async Task AutoNumber_TakesAStartAndAStep(string line, bool enabled, int start, int step)
    {
        var model = Parse($"sequenceDiagram\n    {line}\n    A->>B: one");

        await Assert.That(model.AutoNumber).IsEqualTo(enabled);
        await Assert.That(model.AutoNumberStart).IsEqualTo(start);
        await Assert.That(model.AutoNumberStep).IsEqualTo(step);
    }

    [Test]
    public async Task AutoNumber_NumbersTheMessages()
    {
        var svg = Mermaid.Render("sequenceDiagram\n    autonumber 10 5\n    A->>B: one\n    B->>A: two\n    A->>B: three");

        await Assert.That(svg).Contains(">10. one<");
        await Assert.That(svg).Contains(">15. two<");
        await Assert.That(svg).Contains(">20. three<");
    }

    [Test]
    public async Task CreateAndDestroy_MarkTheParticipant()
    {
        var model = Parse(
            """
            sequenceDiagram
                Alice->>Bob: Hello
                create participant Carl
                Alice->>Carl: Hi Carl
                create actor Dan as Daniel
                Carl->>Dan: Hi Dan
                destroy Carl
                Alice-xCarl: Bye
            """);

        var carl = model.Participants.Single(_ => _.Id == "Carl");
        var dan = model.Participants.Single(_ => _.Id == "Dan");
        await Assert.That(carl.IsCreated).IsTrue();
        await Assert.That(carl.IsDestroyed).IsTrue();
        await Assert.That(dan.IsCreated).IsTrue();
        await Assert.That(dan.Type).IsEqualTo(ParticipantType.Actor);
        await Assert.That(dan.DisplayName).IsEqualTo("Daniel");
        await Assert.That(model.Participants.Single(_ => _.Id == "Alice").IsCreated).IsFalse();
        await Assert.That(model.Participants.Count).IsEqualTo(4);
    }

    // A created participant is drawn once, where it is created, and a destroyed one once, at the top.
    [Test]
    public async Task CreateAndDestroy_ChangeWhereTheParticipantIsDrawn()
    {
        static int Count(string svg, string name) =>
            Regex.Matches(svg, $">{name}<").Count;

        var svg = Mermaid.Render(
            """
            sequenceDiagram
                participant Alice
                participant Old
                Alice->>Old: Hello
                create participant New
                Alice->>New: Welcome
                destroy Old
                Alice-xOld: Goodbye
            """);

        await Assert.That(Count(svg, "Alice")).IsEqualTo(2);
        await Assert.That(Count(svg, "New")).IsEqualTo(2);
        await Assert.That(Count(svg, "Old")).IsEqualTo(1);
    }

    [Test]
    [Arguments("box", null, null)]
    [Arguments("box Front end", null, "Front end")]
    [Arguments("box Aqua Front end", "Aqua", "Front end")]
    [Arguments("box rgb(33, 66, 99) Back end", "rgb(33, 66, 99)", "Back end")]
    [Arguments("box transparent", "transparent", null)]
    public async Task Box_ParsesColourTitleAndMembers(string header, string? color, string? title)
    {
        var model = Parse($"sequenceDiagram\n    {header}\n    participant A\n    actor B\n    end\n    participant C\n    A->>C: x");

        var box = model.Boxes.Single();
        await Assert.That(box.Color).IsEqualTo(color);
        await Assert.That(box.Title).IsEqualTo(title);
        await Assert.That(string.Join(",", box.ParticipantIds)).IsEqualTo("A,B");
        await Assert.That(model.Participants.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Box_EndDoesNotCloseALaterBlock()
    {
        var model = Parse(
            """
            sequenceDiagram
                box Group
                    participant A
                end
                loop Again
                    A->>B: x
                end
            """);

        await Assert.That(model.Boxes.Single().ParticipantIds.Single()).IsEqualTo("A");
        await Assert.That(model.Elements.Single()).IsTypeOf<Loop>();
    }

    [Test]
    public async Task UnclosedBox_FailsTheParse() =>
        await Assert.That(() => Mermaid.Render("sequenceDiagram\n    box Group\n    participant A\n    A->>B: x"))
            .Throws<MermaidParseException>();

    [Test]
    public async Task InteractionMetadata_IsIgnored()
    {
        var model = Parse(
            """
            sequenceDiagram
                participant Alice
                link Alice: Dashboard @ https://dashboard.contoso.com/alice
                links Alice: {"Wiki": "https://wiki.contoso.com/alice"}
                properties Alice: {"class": "internal"}
                details Alice: Something
                Alice->>Bob: Hi
            """);

        await Assert.That(model.Participants.Count).IsEqualTo(2);
        await Assert.That(model.Elements.Count).IsEqualTo(1);
    }

    [Test]
    public async Task QuotedAlias_LosesItsQuotes()
    {
        var model = Parse("sequenceDiagram\n    participant U as \"The User\"\n    U->>B: x");

        await Assert.That(model.Participants[0].DisplayName).IsEqualTo("The User");
    }
}
