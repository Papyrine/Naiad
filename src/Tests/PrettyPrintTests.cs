// Covers RenderOptions.PrettyPrint: the markup is indented, but the document is otherwise unchanged.
public class PrettyPrintTests : TestBase
{
    static RenderOptions Pretty => new()
    {
        PrettyPrint = true
    };

    // Snapshot the raw output (VerifySvg would re-format it). Labels in <foreignObject> stay on one line.
    [Test]
    public Task Flowchart() =>
        Verify(
            Mermaid.Render(
                """
                flowchart LR
                    A[fa:fa-car Car] -->|Yes| B[End]
                """,
                Pretty),
            extension: "svg");

    [Test]
    public Task FlowchartNoHtml() =>
        Verify(
            Mermaid.Render(
                """
                flowchart LR
                    A[Car] -->|Yes| B[End]
                """,
                new()
                {
                    PrettyPrint = true,
                    AllowHtmlElements = false
                }),
            extension: "svg");

    [Test]
    public async Task DefaultIsCompact()
    {
        var svg = Mermaid.Render(
            """
            pie
                "Dogs" : 40
            """);

        await Assert.That(svg).DoesNotContain("\n  <g");
    }

    // Pretty printing only adds whitespace between elements: the parsed documents are identical, so text
    // and HTML label content is untouched.
    [Test]
    [MethodDataSource(typeof(AllowHtmlElementsTests), nameof(AllowHtmlElementsTests.Diagrams))]
    public async Task SameDocument(string name, string input)
    {
        var compact = Mermaid.Render(input);
        IconPackRegistry.Reset();
        var pretty = Mermaid.Render(input, Pretty);

        await Assert.That(pretty).IsNotEqualTo(compact).Because(name);
        await Assert.That(pretty).Contains("\n  <").Because(name);

        var same = XNode.DeepEquals(XDocument.Parse(compact), XDocument.Parse(pretty));
        await Assert.That(same).IsTrue().Because(name);
    }
}
