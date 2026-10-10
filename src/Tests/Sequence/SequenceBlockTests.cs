using Naiad.Diagrams.Sequence;
using Rect = Naiad.Diagrams.Sequence.Rect;

public class SequenceBlockTests
{
    static SequenceModel Parse(string input) =>
        new SequenceParser().Parse(input).Value;

    [Test]
    public async Task Blocks_NestTheElementsBetweenTheirMarkers()
    {
        var model = Parse(
            """
            sequenceDiagram
                A->>B: before
                loop Every minute
                    B->>A: ping
                    opt Maybe
                        A->>B: pong
                    end
                end
                A->>B: after
            """);

        await Assert.That(model.Elements.Count).IsEqualTo(3);
        var loop = (Loop) model.Elements[1];
        await Assert.That(loop.Label).IsEqualTo("Every minute");
        await Assert.That(loop.Elements.Count).IsEqualTo(2);
        var opt = (Opt) loop.Elements[1];
        await Assert.That(opt.Condition).IsEqualTo("Maybe");
        await Assert.That(((Message) opt.Elements.Single()).Text).IsEqualTo("pong");
        await Assert.That(((Message) model.Elements[2]).Text).IsEqualTo("after");
    }

    [Test]
    public async Task Branches_GoToTheBlockTheyContinue()
    {
        var model = Parse(
            """
            sequenceDiagram
                alt first
                    A->>B: one
                else second
                    A->>B: two
                else
                    A->>B: three
                end
                par left
                    A->>B: four
                and right
                    A->>C: five
                end
                critical must
                    A->>B: six
                option fallback
                    A->>B: seven
                end
            """);

        var alt = (Alt) model.Elements[0];
        await Assert.That(alt.Condition).IsEqualTo("first");
        await Assert.That(alt.Elements.Count).IsEqualTo(1);
        await Assert.That(string.Join("|", alt.ElseBranches.Select(_ => _.Condition))).IsEqualTo("second|");
        await Assert.That(((Message) alt.ElseBranches[1].Elements.Single()).Text).IsEqualTo("three");

        var par = (Par) model.Elements[1];
        await Assert.That(par.AndBranches.Single().Label).IsEqualTo("right");

        var critical = (Critical) model.Elements[2];
        await Assert.That(critical.Label).IsEqualTo("must");
        await Assert.That(critical.OptionBranches.Single().Label).IsEqualTo("fallback");

        // Participants named only inside blocks are still picked up.
        await Assert.That(string.Join(",", model.Participants.Select(_ => _.Id))).IsEqualTo("A,B,C");
    }

    [Test]
    public async Task Rect_KeepsItsColour()
    {
        var model = Parse(
            """
            sequenceDiagram
                rect rgba(0, 0, 255, .1)
                    A->>B: one
                end
            """);

        var rect = (Rect) model.Elements.Single();
        await Assert.That(rect.Color).IsEqualTo("rgba(0, 0, 255, .1)");

        var svg = Mermaid.Render("sequenceDiagram\n    rect rgba(0, 0, 255, .1)\n    A->>B: one\n    end");
        await Assert.That(svg).Contains("fill='rgba(0, 0, 255, .1)'");
    }

    [Test]
    public async Task Frames_ShowTheirKindAndLabels()
    {
        var svg = Mermaid.Render(
            """
            sequenceDiagram
                alt is sick
                    Bob->>Alice: Not so good
                else is well
                    Bob->>Alice: Feeling fresh
                end
            """);

        await Assert.That(svg).Contains(">alt<");
        await Assert.That(svg).Contains(">[is sick]<");
        await Assert.That(svg).Contains(">[is well]<");
        await Assert.That(svg).Contains("stroke-dasharray='3,3'");
    }

    // A block adds height for its header, and nothing that was at a given place without it moves up.
    [Test]
    public async Task Frame_MakesRoomForItself()
    {
        static double Height(string input) =>
            double.Parse(
                Regex.Match(Mermaid.Render(input), @"viewBox='0 0 [\d.]+ ([\d.]+)'").Groups[1].Value,
                CultureInfo.InvariantCulture);

        var plain = Height("sequenceDiagram\n    A->>B: one\n    B->>A: two");
        var framed = Height("sequenceDiagram\n    loop Again\n    A->>B: one\n    B->>A: two\n    end");

        await Assert.That(framed).IsGreaterThan(plain);
    }

    // A word that only begins with a block keyword is not a block marker.
    [Test]
    [Arguments("    android->>B: hi")]
    [Arguments("    optimus->>B: hi")]
    [Arguments("    endpoint->>B: hi")]
    public async Task KeywordPrefix_IsNotABlockMarker(string line)
    {
        var model = Parse($"sequenceDiagram\n{line}");

        await Assert.That(model.Elements.Single()).IsTypeOf<Message>();
    }

    [Test]
    [Arguments("stray end", "sequenceDiagram\n    A->>B: one\n    end")]
    [Arguments("missing end", "sequenceDiagram\n    loop Again\n    A->>B: one")]
    [Arguments("else outside alt", "sequenceDiagram\n    loop Again\n    else no\n    A->>B: one\n    end")]
    [Arguments("and outside par", "sequenceDiagram\n    and no\n    A->>B: one")]
    [Arguments("option outside critical", "sequenceDiagram\n    alt x\n    option no\n    end")]
    public async Task UnbalancedBlocks_FailTheParse(string name, string input) =>
        await Assert.That(() => Mermaid.Render(input)).Throws<MermaidParseException>().Because(name);
}
