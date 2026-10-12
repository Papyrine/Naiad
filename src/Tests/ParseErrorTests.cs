// A parse that fails says where it gave up and what it found there. The place is the furthest any rule
// got, so a statement that is readable up to some point is reported at that point, not at its start.
public class ParseErrorTests
{
    [Test]
    [Arguments(
        "text after a statement",
        "flowchart TD\n    A --> B ???\n    B --> D",
        "Failed to parse flowchart: unexpected '???' at line 2, col 13")]
    [Arguments(
        "a link with no node after it",
        "flowchart TD\n    A --> B\n    B -->",
        "Failed to parse flowchart: unexpected '-->' at line 3, col 7")]
    [Arguments(
        "a rule that says why it gave up",
        "flowchart TD\n    A --> B\nB ~~ C",
        "Failed to parse flowchart: an invisible link is at least three tildes at line 3, col 3")]
    [Arguments(
        "a line no rule reads",
        "pie title Pets\n\"Dogs\" : 386\nCats : 85\n\"Rats\" : 15",
        "Failed to parse pie chart: unexpected 'Cats : 85' at line 3, col 1")]
    [Arguments(
        "text after a value, on the last line",
        "pie title Pets\n\"Dogs\" : 386 extra",
        "Failed to parse pie chart: unexpected 'extra' at line 2, col 14")]
    [Arguments(
        "a line no rule reads, after one that is indented",
        "classDiagram\n    class Animal\nnot a class statement\n    class Dog",
        "Failed to parse class diagram: unexpected 'not a class statement' at line 3, col 1")]
    [Arguments(
        "a word that only starts like a keyword",
        "stateDiagram-v2\n    [*] --> Still\n    classy stuff here",
        "Failed to parse state diagram: unexpected 'classy stuff here' at line 3, col 5")]
    [Arguments(
        "a rule that names what it does not know",
        "C4Context\n    title T\n    Foo(a, \"b\")",
        "Failed to parse C4 diagram: Unknown C4 statement 'Foo' at line 3, col 5")]
    [Arguments(
        "a block left open",
        "requirementDiagram\n    requirement a {\n    id: 1",
        "Failed to parse requirement diagram: unexpected end of input at line 3, col 10")]
    [Arguments(
        "a line too long to quote whole",
        "pie\n\"Dogs\" : 386\nthis line is not a slice of the pie and it runs on for a while",
        "Failed to parse pie chart: unexpected 'this line is not a slice of the pie and...' at line 3, col 1")]
    public async Task Message_NamesThePlaceAndWhatWasFound(string name, string input, string expected)
    {
        var exception = await Assert.That(() => Mermaid.Render(input)).Throws<MermaidParseException>().Because(name);

        await Assert.That(exception!.Message).IsEqualTo(expected).Because(name);
    }

    [Test]
    public async Task FailedResult_HasAnErrorAndNoValue()
    {
        var result = new PieParser().Parse("pie\nnot a slice");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error).IsEqualTo("unexpected 'not a slice' at line 2, col 1");
        await Assert.That(() => result.Value).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SuccessfulResult_HasAValueAndNoError()
    {
        var result = new PieParser().Parse("pie\n\"Dogs\" : 386");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Value.Sections.Count).IsEqualTo(1);
    }
}
