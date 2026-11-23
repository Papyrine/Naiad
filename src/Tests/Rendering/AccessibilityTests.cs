// A diagram's accessibility title and description (`accTitle: ...`, `accDescr: ...`, `accDescr { ... }`) are
// not drawn. As Mermaid writes them, they become the SVG's <title> and <desc>, its first children, which
// the root names with aria-labelledby and aria-describedby.
public class AccessibilityTests
{
    static readonly XNamespace svg = "http://www.w3.org/2000/svg";

    [Test]
    [MethodDataSource(nameof(Diagrams))]
    public async Task TitleAndDescription_AreWrittenForScreenReaders(string name, string input)
    {
        var root = XDocument.Parse(Mermaid.Render(input)).Root!;

        await Assert.That((string?) root.Attribute("aria-labelledby")).IsEqualTo("chart-title-naiad").Because(name);
        await Assert.That((string?) root.Attribute("aria-describedby")).IsEqualTo("chart-desc-naiad").Because(name);

        var first = root.Elements().Take(2).ToList();
        await Assert.That(first[0].Name).IsEqualTo(svg + "title").Because(name);
        await Assert.That((string?) first[0].Attribute("id")).IsEqualTo("chart-title-naiad").Because(name);
        await Assert.That(first[0].Value).IsEqualTo("The title").Because(name);
        await Assert.That(first[1].Name).IsEqualTo(svg + "desc").Because(name);
        await Assert.That((string?) first[1].Attribute("id")).IsEqualTo("chart-desc-naiad").Because(name);
        await Assert.That(first[1].Value).IsEqualTo("The description").Because(name);
    }

    // Each line of a block loses its indentation, and the blank lines go, as Mermaid reads it.
    [Test]
    [Arguments("flowchart", "flowchart LR\n    accDescr {\n        First line\n          second line\n\n    }\n    A --> B")]
    [Arguments("sequence", "sequenceDiagram\n    accDescr {\n        First line\n          second line\n\n    }\n    A->>B: hi")]
    [Arguments("tree view", "treeView-beta\n    accDescr {\n        First line\n          second line\n\n    }\n    root/")]
    [Arguments("info", "info\n    accDescr {\n        First line\n          second line\n\n    }")]
    public async Task DescriptionBlock_IsJoinedLineByLine(string name, string input)
    {
        var root = XDocument.Parse(Mermaid.Render(input)).Root!;

        await Assert.That(root.Element(svg + "desc")!.Value).IsEqualTo("First line\nsecond line").Because(name);
        await Assert.That(root.Element(svg + "title")).IsNull().Because(name);
        await Assert.That(root.Attribute("aria-labelledby")).IsNull().Because(name);
    }

    [Test]
    public async Task Text_IsEscaped()
    {
        var svgText = Mermaid.Render("flowchart LR\n    accTitle: <b>Tom & 'Jerry'</b>\n    A --> B");

        var root = XDocument.Parse(svgText).Root!;
        await Assert.That(root.Element(svg + "title")!.Value).IsEqualTo("<b>Tom & 'Jerry'</b>");
        await Assert.That(svgText).DoesNotContain("<b>");
    }

    // As in Mermaid, a later statement replaces an earlier one.
    [Test]
    public async Task LastStatement_Wins()
    {
        var root = XDocument.Parse(Mermaid.Render("pie\n    accTitle: First\n    accTitle: Second\n    \"A\" : 1")).Root!;

        await Assert.That(root.Element(svg + "title")!.Value).IsEqualTo("Second");
    }

    [Test]
    public async Task Without_ThemNothingIsAdded()
    {
        var svgText = Mermaid.Render("flowchart LR\n    A --> B");

        await Assert.That(svgText).DoesNotContain("<title");
        await Assert.That(svgText).DoesNotContain("<desc");
        await Assert.That(svgText).DoesNotContain("aria-labelledby");
        await Assert.That(svgText).DoesNotContain("aria-describedby");
    }

    public static IEnumerable<(string Name, string Input)> Diagrams()
    {
        const string lines = "    accTitle: The title\n    accDescr: The description\n";

        yield return ("Flowchart", "flowchart LR\n" + lines + "    A --> B");
        yield return ("Sequence", "sequenceDiagram\n" + lines + "    A->>B: hi");
        yield return ("Class", "classDiagram\n" + lines + "    A <|-- B");
        yield return ("State", "stateDiagram-v2\n" + lines + "    [*] --> A");
        yield return ("EntityRelationship", "erDiagram\n" + lines + "    A ||--o{ B : has");
        yield return ("Gantt", "gantt\n" + lines + "    dateFormat YYYY-MM-DD\n    section S\n    Task :a1, 2024-01-01, 3d");
        yield return ("Pie", "pie\n" + lines + "    \"Dogs\" : 1");
        yield return ("Packet", "packet-beta\n" + lines + "    0-15: \"Port\"");
        yield return ("Requirement", "requirementDiagram\n" + lines + "    requirement r {\n        id: 1\n    }");
        yield return ("C4", "C4Context\n" + lines + "    Person(u, \"User\")");
        yield return ("Cynefin", "cynefin-beta\n" + lines + "    complex\n        \"a\"");
        yield return ("EventModeling", "eventmodeling\n" + lines + "    tf 01 evt Added");
        yield return ("Info", "info\n" + lines);
        yield return ("Railroad", "railroad-beta\n" + lines + "    rule = terminal(\"a\");");
        yield return ("TreeView", "treeView-beta\n" + lines + "    root/\n        file");
        yield return ("Wardley", "wardley-beta\n" + lines + "    component \"A\" [0.5, 0.5]");
        yield return ("AgentFlow", "agentflow-beta TB\n" + lines + "    a --> b");
        yield return ("UseCase", "usecase-beta\n" + lines + "    actor A");
    }
}
