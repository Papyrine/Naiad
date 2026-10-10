// Inputs that used to hang the renderer, kill the process, or escape as a raw framework exception.
public class RobustnessTests
{
    // The column assignment kept pushing the nodes of a cycle to the right of each other, forever.
    [Test]
    [Arguments("cycle", "sankey-beta\nS,A,1\nA,B,1\nB,A,1")]
    [Arguments("self link", "sankey-beta\nS,A,1\nA,A,1")]
    [Arguments("nothing but a cycle", "sankey-beta\nA,B,1\nB,A,1")]
    public async Task Sankey_CircularLink_IsRejected(string name, string input)
    {
        var exception = await Assert.That(() => Mermaid.Render(input)).Throws<MermaidException>().Because(name);

        await Assert.That(exception!.Message).Contains("circular link").Because(name);
    }

    [Test]
    public async Task Sankey_SharedTargets_AreNotMistakenForACycle()
    {
        var svg = Mermaid.Render("sankey-beta\nA,C,1\nB,C,2\nA,D,1\nC,D,3");

        await Assert.That(svg).Contains("<svg");
    }

    [Test]
    [Arguments("stateDiagram-v2")]
    [Arguments("stateDiagram")]
    [Arguments("stateDiagram-v2\n    %% nothing yet")]
    public async Task State_WithNoStates_RendersAnEmptyDiagram(string input)
    {
        var svg = Mermaid.Render(input);

        await Assert.That(svg).Contains("<svg");
        await Assert.That(svg).DoesNotContain("NaN");
    }

    [Test]
    public async Task Architecture_DuplicateServiceId_IsRejectedWithAReason()
    {
        const string input =
            """
            architecture-beta
            service db(database)[Database]
            service db(database)[Database 2]
            """;

        var exception = await Assert.That(() => Mermaid.Render(input)).Throws<MermaidException>();

        await Assert.That(exception!.Message).Contains("The service id [db] is already in use");
    }

    // The layout walked the graph by recursion, as deep as its longest path. A few thousand nodes in a
    // chain overflowed the stack, which cannot be caught and takes the process down. A deliberately small
    // stack makes that depth cheap to reach here: the recursive walks would not survive this.
    [Test]
    [Arguments("flowchart TD", "    N{0} --> N{1}")]
    [Arguments("stateDiagram-v2", "    S{0} --> S{1}")]
    [Arguments("classDiagram", "    C{0} <|-- C{1}")]
    [Arguments("erDiagram", "    E{0} ||--o{{ E{1} : has")]
    public async Task LongChain_DoesNotOverflowTheStack(string header, string link)
    {
        var input = new StringBuilder(header).Append('\n');
        for (var i = 0; i < 2500; i++)
        {
            input.AppendFormat(CultureInfo.InvariantCulture, link, i, i + 1).Append('\n');
        }

        string? svg = null;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    svg = Mermaid.Render(input.ToString());
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            },
            maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        await Assert.That(failure).IsNull();
        await Assert.That(svg).IsNotNull();
    }
}
