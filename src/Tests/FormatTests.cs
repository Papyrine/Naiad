// Covers Mermaid.Format: source laid out by its blocks.
public class FormatTests
{
    [Test]
    public async Task SequenceBlocks()
    {
        var mermaid = Mermaid.Format(
            "sequenceDiagram\nAlice->>Bob: Hi   \n\n\n  loop Every minute\nalt ok\nBob-->>Alice: Fine\n        else not ok\nBob-->>Alice: Bad\nend\nend\n");
        await Assert.That(mermaid).IsEqualTo(
            """
            sequenceDiagram
                Alice->>Bob: Hi

                loop Every minute
                    alt ok
                        Bob-->>Alice: Fine
                    else not ok
                        Bob-->>Alice: Bad
                    end
                end

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task FlowchartKeepsFrontMatterAndStrings()
    {
        var mermaid = Mermaid.Format(
            "---\ntitle: Demo\n---\n%%{init: {'theme':'forest'}}%%\nflowchart TD\nsubgraph one\nA[\"`two\n   lines`\"] --> B{Choice}\nend\nclassDef red fill:#f00\n");
        await Assert.That(mermaid).IsEqualTo(
            """
            ---
            title: Demo
            ---
            %%{init: {'theme':'forest'}}%%
            flowchart TD
                subgraph one
                    A["`two
               lines`"] --> B{Choice}
                end
                classDef red fill:#f00

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task BracesAndSections()
    {
        await Assert.That(Mermaid.Format("classDiagram\nclass Animal {\n+name\n}\n")).IsEqualTo(
            "classDiagram\n    class Animal {\n        +name\n    }\n");
        await Assert.That(Mermaid.Format("gantt\ntitle Plan\nsection A\nTask :a1, 2024-01-01, 3d\nsection B\nTask :b1, after a1, 2d\n")).IsEqualTo(
            "gantt\n    title Plan\n    section A\n        Task :a1, 2024-01-01, 3d\n    section B\n        Task :b1, after a1, 2d\n");
    }

    [Test]
    public async Task LeavesIndentationDiagramsAlone()
    {
        const string mindmap = "mindmap\n  root\n    child\n";
        await Assert.That(Mermaid.Format(mindmap)).IsEqualTo(mindmap);
    }

    [Test]
    public async Task IsStable()
    {
        var once = Mermaid.Format("stateDiagram-v2\nstate Active {\n[*] --> On\n--\n[*] --> Idle\n}\n");
        await Assert.That(Mermaid.Format(once)).IsEqualTo(once);
    }

    // Formatting changes only white space, so a formatted diagram still renders.
    [Test]
    public async Task FormattedStillRenders()
    {
        var formatted = Mermaid.Format("flowchart TD\nsubgraph one\nA --> B\nend\n");
        await Assert.That(Mermaid.Render(formatted)).Contains("<svg");
    }

    // The header is found as rendering finds it, so its keyword's case does not matter.
    [Test]
    public async Task HeaderIsCaseInsensitive() =>
        await Assert.That(Mermaid.Format("GANTT\nSECTION A\nTask :a1, 2024-01-01, 3d\n")).IsEqualTo(
            "GANTT\n    SECTION A\n        Task :a1, 2024-01-01, 3d\n");

    // A multi-line init block is not mistaken for the header, and keeps its own indentation.
    [Test]
    public async Task MultiLineInitBlock() =>
        await Assert.That(Mermaid.Format("%%{init: {\n  'theme': 'forest'\n}}%%\nflowchart TD\nA --> B\n")).IsEqualTo(
            "%%{init: {\n  'theme': 'forest'\n}}%%\nflowchart TD\n    A --> B\n");

    [Test]
    public async Task UnknownDiagramUnchanged()
    {
        const string text = "  notADiagram\n x\n";
        await Assert.That(Mermaid.Format(text)).IsEqualTo(text);
    }

    [Test]
    public async Task Parses()
    {
        await Assert.That(Mermaid.Parses("flowchart TD\n    A --> B\n")).IsTrue();
        await Assert.That(Mermaid.Parses("flowchart TD\n    A --> \n")).IsFalse();
        await Assert.That(Mermaid.Parses("notADiagram\n")).IsFalse();
    }

    // Every sample diagram in test-renders that parses still parses once formatted, and formatting is
    // stable. Format falls back to the text as written when it would not parse, so this checks the layout
    // without that fallback.
    [Test]
    public async Task SamplesStillParse()
    {
        var directory = Path.Combine(ProjectFiles.SolutionDirectory, "test-renders");
        var samples = Directory.EnumerateFiles(directory, "*.md")
            .SelectMany(_ => Regex.Matches(File.ReadAllText(_), "```mermaid\r?\n(.*?)```", RegexOptions.Singleline))
            .Select(_ => _.Groups[1].Value)
            .Where(Mermaid.Parses)
            .ToList();
        await Assert.That(samples.Count).IsGreaterThan(100);

        var broken = new List<string>();
        foreach (var sample in samples)
        {
            var formatted = MermaidFormatter.Layout(sample);
            if (!Mermaid.Parses(formatted) ||
                MermaidFormatter.Layout(formatted) != formatted)
            {
                broken.Add(sample);
            }
        }

        await Assert.That(broken).IsEmpty();
    }
}
