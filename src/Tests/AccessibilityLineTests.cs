using Naiad.Diagrams.Class;

// The accessibility title and description (`accTitle: ...`, `accDescr: ...` and the multi-line
// `accDescr { ... }`) are valid in every Mermaid diagram. They change nothing that is drawn here, but a
// diagram that holds them must still parse, and they must not be read as something else.
public class AccessibilityLineTests
{
    [Test]
    [Arguments("class", "classDiagram\n    accTitle: Class title\n    accDescr: A description\n    A <|-- B")]
    [Arguments("class, multi-line", "classDiagram\n    accDescr {\n        over\n        lines\n    }\n    A <|-- B")]
    [Arguments("requirement, multi-line", "requirementDiagram\n    accDescr {\n        over\n        lines\n    }\n    requirement r {\n        id: 1\n    }")]
    [Arguments("sequence", "sequenceDiagram\n    accTitle: My title\n    accDescr: A description\n    A->>B: hi")]
    [Arguments("sequence, multi-line", "sequenceDiagram\n    accDescr {\n        over\n        lines\n    }\n    A->>B: hi")]
    public async Task Diagram_WithAccessibilityLines_Renders(string name, string input)
    {
        var svg = Mermaid.Render(input);

        await Assert.That(svg).DoesNotContain(">accTitle<").Because(name);
        await Assert.That(svg).DoesNotContain(">accDescr<").Because(name);
        await Assert.That(svg).DoesNotContain(">over<").Because(name);
    }

    // `accTitle: x` was read as a member added to a class called accTitle, which was then drawn.
    [Test]
    public async Task Class_AccessibilityLinesAreNotClasses()
    {
        var result = new ClassParser().Parse("classDiagram\n    accTitle: Class title\n    accDescr: A description\n    A <|-- B");

        await Assert.That(result.Value.Classes.Select(_ => _.Id)).IsEquivalentTo(["A", "B"]);
    }
}
