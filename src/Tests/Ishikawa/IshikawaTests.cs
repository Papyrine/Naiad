using Naiad.Diagrams.Ishikawa;

public class IshikawaTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            ishikawa-beta
                Blurry Photo
                Process
                    Out of focus
                    Shutter speed too slow
                User
                    Shaky hands
                Equipment
                    Dirty lens
                Environment
                    Too dark
            """;

        return VerifySvg(input);
    }

    // The example from Mermaid's documentation: causes nested three deep, with an odd number of causes.
    [Test]
    public Task Nested()
    {
        const string input =
            """
            ishikawa-beta
                Blurry Photo
                Process
                    Out of focus
                    Shutter speed too slow
                    Protective film not removed
                    Beautification filter applied
                User
                    Shaky hands
                Equipment
                    LENS
                        Inappropriate lens
                        Damaged lens
                        Dirty lens
                    SENSOR
                        Damaged sensor
                        Dirty sensor
                Environment
                    Subject moved too quickly
                    Too dark
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DeepHierarchy()
    {
        const string input =
            """
            ishikawa
                Late delivery
                Logistics
                    Carrier
                        Truck breakdown
                            No maintenance schedule
                                Budget cuts
                    Routing
                Warehouse
                    Picking errors
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task CausesWithoutSubCauses()
    {
        const string input =
            """
            ishikawa-beta
                Server outage
                Power
                Network
                Hardware
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task EffectOnly()
    {
        const string input =
            """
            ishikawa-beta
                A rather long problem statement that has to wrap
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task CommentsAndBlankLines()
    {
        const string input =
            """
            %% leading comment
            ishikawa-beta
                Problem

                %% a comment between causes
                Cause A
                    Sub-cause A1
                Cause B<br>on two lines
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_BasicHierarchy()
    {
        var root = Parse(
            """
            ishikawa-beta
                Blurry Photo
                    Process
                        Out of focus
                    User
                        Shaky hands
            """);

        await Assert.That(root.Text).IsEqualTo("Blurry Photo");
        await Assert.That(Describe(root)).IsEqualTo("Blurry Photo(Process(Out of focus),User(Shaky hands))");
    }

    [Test]
    public async Task Parse_UnindentedRoot()
    {
        var root = Parse(
            """
            ishikawa-beta
            Problem
            Cause A
              Subcause A1
            Cause B
            """);

        await Assert.That(Describe(root)).IsEqualTo("Problem(Cause A(Subcause A1),Cause B)");
    }

    // The causes are levelled from the first cause, so an effect indented beyond them changes nothing.
    [Test]
    public async Task Parse_EffectIndentedMoreThanCauses()
    {
        var root = Parse(
            """
            ishikawa-beta
                Problem
            Cause A
              Subcause A1
            Cause B
            """);

        await Assert.That(Describe(root)).IsEqualTo("Problem(Cause A(Subcause A1),Cause B)");
    }

    // A line indented less than the first cause is still a cause, not a sibling of the effect.
    [Test]
    public async Task Parse_OutdentedCauseStaysUnderEffect()
    {
        var root = Parse(
            """
            ishikawa-beta
            Problem
                Cause A
              Cause B
            """);

        await Assert.That(Describe(root)).IsEqualTo("Problem(Cause A,Cause B)");
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("ishikawa-beta\n  Problem", out var beta)).IsTrue();
        await Assert.That(beta).IsEqualTo(DiagramType.Ishikawa);
        await Assert.That(Mermaid.TryDetectType("ISHIKAWA\n  Problem", out var plain)).IsTrue();
        await Assert.That(plain).IsEqualTo(DiagramType.Ishikawa);
    }

    static IshikawaNode Parse(string input)
    {
        var result = new IshikawaParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value.Root!;
    }

    static string Describe(IshikawaNode node) =>
        node.Children.Count == 0
            ? node.Text
            : $"{node.Text}({string.Join(",", node.Children.Select(Describe))})";
}
