using Naiad.Diagrams.Info;

public class InfoTests : TestBase
{
    // The real version changes with every release, which would break the snapshots, so a fixed one is drawn.
    [Before(Test)]
    public void FixVersion() => InfoRenderer.VersionOverride = "1.2.3";

    [After(Test)]
    public void ResetVersion() => InfoRenderer.VersionOverride = null;

    [Test]
    public Task Simple() =>
        VerifySvg("info");

    [Test]
    public Task ShowInfo() =>
        VerifySvg(
            """
            info
            showInfo
            """);

    [Test]
    public async Task Parse_Plain()
    {
        var model = Parse("info");

        await Assert.That(model.ShowInfo).IsFalse();
        await Assert.That(model.Title).IsNull();
    }

    [Test]
    public async Task Parse_ShowInfoOnItsOwnLine()
    {
        var model = Parse("info\nshowInfo");

        await Assert.That(model.ShowInfo).IsTrue();
    }

    [Test]
    public async Task Parse_ShowInfoOnTheKeywordLine()
    {
        var model = Parse("info showInfo");

        await Assert.That(model.ShowInfo).IsTrue();
    }

    [Test]
    public async Task Parse_CommentsBlankLinesAndTitle()
    {
        var model = Parse(
            """
            info   %% trailing comment

            %% a comment line
            showInfo

            title Version check
            accTitle: Accessible title
            accDescr {
                A description
                over two lines
            }
            """);

        await Assert.That(model.ShowInfo).IsTrue();
        await Assert.That(model.Title).IsEqualTo("Version check");
    }

    [Test]
    public async Task Parse_RejectsUnknownContent()
    {
        var result = new InfoParser().Parse("info\nsomething else");

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task Parse_RejectsKeywordPrefix()
    {
        var result = new InfoParser().Parse("information");

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task VersionHasNoCommitSuffix()
    {
        InfoRenderer.VersionOverride = null;

        await Assert.That(InfoRenderer.Version).DoesNotContain("+");
        await Assert.That(InfoRenderer.Version).IsNotEmpty();
    }

    [Test]
    public async Task RendersNaiadVersion()
    {
        var svg = Mermaid.Render("info");

        await Assert.That(svg).Contains("Naiad v1.2.3");
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("info", out var plain)).IsTrue();
        await Assert.That(plain).IsEqualTo(DiagramType.Info);
        await Assert.That(Mermaid.TryDetectType("INFO\nshowInfo", out var withShowInfo)).IsTrue();
        await Assert.That(withShowInfo).IsEqualTo(DiagramType.Info);
        await Assert.That(Mermaid.TryDetectType("%% comment\ninfo showInfo", out var afterComment)).IsTrue();
        await Assert.That(afterComment).IsEqualTo(DiagramType.Info);
    }

    // A keyword that only starts with "info" is not the info diagram.
    [Test]
    public async Task DoesNotDetectKeywordPrefix()
    {
        await Assert.That(Mermaid.TryDetectType("information\n", out _)).IsFalse();
        await Assert.That(Mermaid.TryDetectType("infoGraph", out _)).IsFalse();
        await Assert.That(Mermaid.TryDetectType("info-beta", out _)).IsFalse();
    }

    static InfoModel Parse(string input)
    {
        var result = new InfoParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }
}
