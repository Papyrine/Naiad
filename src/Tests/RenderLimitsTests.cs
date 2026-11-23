// RenderOptions.Limits turns down a diagram too long, or with a graph too large or too deeply nested, before
// the work it would cost is done; a cancelled token stops a render that is under way.
public class RenderLimitsTests
{
    [Test]
    public async Task Defaults_AreOn()
    {
        var limits = new RenderOptions().Limits;

        await Assert.That(limits.MaxInputLength).IsEqualTo(1_000_000);
        await Assert.That(limits.MaxNodes).IsEqualTo(5_000);
        await Assert.That(limits.MaxEdges).IsEqualTo(2_000);
        await Assert.That(limits.MaxSubgraphDepth).IsEqualTo(100);
    }

    [Test]
    public async Task LongInput_IsTurnedDown()
    {
        var input = "pie\n    %% " + new string('x', 1_000_000) + "\n    \"A\" : 1";

        var exception = await Assert.That(() => Mermaid.Render(input)).Throws<MermaidLimitException>();

        await Assert.That(exception!.Message).Contains("MaxInputLength");
        await Assert.That(Mermaid.Render(input, new() { Limits = RenderLimits.Unlimited })).Contains("<svg");
    }

    [Test]
    [Arguments("flowchart", "flowchart LR\n    A --> B\n    B --> C\n    C --> D")]
    [Arguments("class", "classDiagram\n    A <|-- B\n    B <|-- C\n    C <|-- D")]
    [Arguments("ER", "erDiagram\n    A ||--o{ B : has\n    B ||--o{ C : has\n    C ||--o{ D : has")]
    [Arguments("state", "stateDiagram-v2\n    A --> B\n    B --> C\n    C --> D")]
    public async Task TooManyNodes_IsTurnedDown(string name, string input)
    {
        var exception = await Assert.That(() => Mermaid.Render(input, new() { Limits = { MaxNodes = 3 } }))
            .Throws<MermaidLimitException>();

        await Assert.That(exception!.Message).Contains("MaxNodes").Because(name);
        await Assert.That(Mermaid.Render(input, new() { Limits = { MaxNodes = 4 } })).Contains("<svg").Because(name);
    }

    [Test]
    public async Task TooManyEdges_IsTurnedDown()
    {
        const string input = "flowchart LR\n    A --> B\n    A --> C\n    B --> C";

        var exception = await Assert.That(() => Mermaid.Render(input, new() { Limits = { MaxEdges = 2 } }))
            .Throws<MermaidLimitException>();

        await Assert.That(exception!.Message).Contains("MaxEdges");
        await Assert.That(Mermaid.Render(input, new() { Limits = { MaxEdges = 3 } })).Contains("<svg");
    }

    [Test]
    [Arguments("flowchart", "flowchart TD\n", "subgraph S{0}\n", "end\n", "A --> B\n")]
    [Arguments("state", "stateDiagram-v2\n", "state S{0} {{\n", "}}\n", "A --> B\n")]
    public async Task DeepSubgraphs_AreTurnedDown(string name, string header, string open, string close, string body)
    {
        static string Nested(string header, string open, string close, string body, int depth)
        {
            var input = new StringBuilder(header);
            for (var i = 0; i < depth; i++)
            {
                input.AppendFormat(CultureInfo.InvariantCulture, open, i);
            }

            input.Append(body);
            for (var i = 0; i < depth; i++)
            {
                input.AppendFormat(CultureInfo.InvariantCulture, close);
            }

            return input.ToString();
        }

        var options = new RenderOptions { Limits = { MaxSubgraphDepth = 3 } };

        var exception = await Assert.That(() => Mermaid.Render(Nested(header, open, close, body, 4), options))
            .Throws<MermaidLimitException>();

        await Assert.That(exception!.Message).Contains("MaxSubgraphDepth").Because(name);
        await Assert.That(Mermaid.Render(Nested(header, open, close, body, 3), options)).Contains("<svg").Because(name);
    }

    [Test]
    public async Task LimitException_IsAMermaidException() =>
        await Assert.That(new MermaidLimitException("x")).IsAssignableTo<MermaidException>();

    [Test]
    public async Task CancelledToken_StopsBeforeAnyWork()
    {
        using var source = new CancelSource();
        await source.CancelAsync();

        await Assert.That(() => Mermaid.Render("flowchart LR\n    A --> B", null, source.Token))
            .Throws<OperationCanceledException>();
    }

    // A graph whose layout takes seconds (3,000 nodes joined by 9,000 edges, over the default limits) stops
    // soon after the token is cancelled, part-way through.
    [Test]
    public async Task CancelledToken_StopsTheLayout()
    {
        var random = new Random(1);
        var input = new StringBuilder("flowchart TD\n");
        for (var i = 1; i < 3000; i++)
        {
            for (var k = 0; k < 3; k++)
            {
                input.Append(CultureInfo.InvariantCulture, $"    N{random.Next(Math.Max(0, i - 50), i)} --> N{i}\n");
            }
        }

        var options = new RenderOptions { Limits = RenderLimits.Unlimited };
        using var source = new CancelSource(TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();

        await Assert.That(() => Mermaid.Render(input.ToString(), options, source.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(3));
    }
}
