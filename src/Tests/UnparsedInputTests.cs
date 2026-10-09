// A line that no rule of a diagram's grammar matches has to fail the render. These parsers used to stop at
// such a line and return whatever they had read up to it, so the rest of the diagram was dropped without
// an error. Each input puts the unreadable line at column 0, which is where the parsers gave up quietly.
public class UnparsedInputTests
{
    [Test]
    [Arguments(
        "flowchart",
        """
        flowchart TD
            A --> B
        B ~~ C
            B --> D
        """)]
    [Arguments(
        "class",
        """
        classDiagram
            class Animal
        not a class statement
            class Dog
        """)]
    [Arguments(
        "state",
        """
        stateDiagram-v2
            [*] --> Still
        not a state statement
            Still --> Moving
        """)]
    [Arguments(
        "er",
        """
        erDiagram
            CUSTOMER ||--o{ ORDER : places
        not a relationship
            ORDER ||--|{ ITEM : has
        """)]
    [Arguments(
        "sequence",
        """
        sequenceDiagram
            A->>B: one
        not a message
            B->>A: two
        """)]
    [Arguments(
        "pie",
        """
        pie title Pets
        "Dogs" : 386
        Cats : 85
        "Rats" : 15
        """)]
    [Arguments(
        "gantt",
        """
        gantt
        dateFormat YYYY-MM-DD
        Task A :a1, 2024-01-01, 10d
        todayMarker off
        Task B :a2, after a1, 5d
        """)]
    [Arguments(
        "gitGraph",
        """
        gitGraph
        commit id: "A"
        branch release/1.0
        commit id: "B"
        """)]
    public async Task UnreadableLine_FailsInsteadOfTruncating(string diagram, string input) =>
        await Assert.That(() => Mermaid.Render(input))
            .Throws<MermaidParseException>()
            .Because($"the {diagram} parser must not drop the lines after one it cannot read");
}
