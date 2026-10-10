public class ClassParserTests
{
    // Each of these member lines used to end the class body early: the rest of the body, and every
    // statement after the class, were dropped without an error.
    [Test]
    [Arguments("+BigDecimal balance = 0")]
    [Arguments("-Dictionary<string, int> map")]
    [Arguments("+int? maybe")]
    [Arguments("+static create() Acct")]
    [Arguments("- balance : decimal")]
    [Arguments("+ getBalance() decimal")]
    [Arguments("+async run(): Promise<void>")]
    [Arguments("+onClick(handler: (e: Event) => void)")]
    [Arguments("+List~Map~String, int~~ nested")]
    [Arguments("%% a comment")]
    public async Task UnusualBodyLine_DoesNotTruncateTheDiagram(string line)
    {
        var input = $"classDiagram\n    class Acct {{\n        {line}\n        +String owner\n    }}\n    class Dog\n    Acct <|-- Dog";

        var result = new ClassParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(string.Join(",", result.Value.Classes.Select(_ => _.Id))).IsEqualTo("Acct,Dog");
        await Assert.That(result.Value.Relationships.Count).IsEqualTo(1);
        await Assert.That(result.Value.Classes[0].Members.Any(_ => _.Name == "owner")).IsTrue();
    }

    [Test]
    public async Task LooseMembers_KeepTheirText()
    {
        const string input =
            """
            classDiagram
                class Acct {
                    +BigDecimal balance = 0
                    -Dictionary<string, int> map
                    +List~Map~String, int~~ nested
                    - spaced : decimal
                    #counter$
                }
            """;

        var members = new ClassParser().Parse(input).Value.Classes.Single().Members;

        await Assert.That(string.Join("|", members.Select(_ => $"{_.Visibility}:{_.Name}")))
            .IsEqualTo(
                "Public:BigDecimal balance = 0|Private:Dictionary<string, int> map|" +
                "Public:List<Map<String, int>> nested|Private:spaced : decimal|Protected:counter");
        await Assert.That(members[^1].IsStatic).IsTrue();
    }

    [Test]
    public async Task LooseMethods_SplitNameParametersAndReturnType()
    {
        const string input =
            """
            classDiagram
                class Acct {
                    +static create() Acct
                    +async run(): Promise<void>
                    +onClick(handler: (e: Event) => void)
                    + lookup(Map<string, int> index, int key)* bool
                }
            """;

        var methods = new ClassParser().Parse(input).Value.Classes.Single().Methods;

        await Assert.That(methods[0].Name).IsEqualTo("static create");
        await Assert.That(methods[0].ReturnType).IsEqualTo("Acct");
        await Assert.That(methods[1].Name).IsEqualTo("async run");
        await Assert.That(methods[1].ReturnType).IsEqualTo("Promise<void>");
        await Assert.That(methods[2].Parameters.Single().Name).IsEqualTo("handler");
        await Assert.That(methods[2].Parameters.Single().Type).IsEqualTo("(e: Event) => void");
        await Assert.That(methods[2].ReturnType).IsNull();
        await Assert.That(string.Join("|", methods[3].Parameters.Select(_ => $"{_.Name}:{_.Type}")))
            .IsEqualTo("index:Map<string, int>|key:int");
        await Assert.That(methods[3].IsAbstract).IsTrue();
        await Assert.That(methods[3].ReturnType).IsEqualTo("bool");
    }

    [Test]
    public async Task BodyOnOneLine_IsParsed()
    {
        const string input =
            """
            classDiagram
                class Animal { +String name }
                class Empty { }
                class Dog
            """;

        var classes = new ClassParser().Parse(input).Value.Classes;

        await Assert.That(string.Join(",", classes.Select(_ => _.Id))).IsEqualTo("Animal,Empty,Dog");
        await Assert.That(classes[0].Members.Single().Name).IsEqualTo("name");
        await Assert.That(classes[0].Members.Single().Type).IsEqualTo("String");
        await Assert.That(classes[1].Members.Count).IsEqualTo(0);
    }

    [Test]
    public async Task LabelAndCssClass_AreAccepted()
    {
        const string input =
            """
            classDiagram
                class Animal["Animal with a label"]
                class Styled:::someclass
                class Both["Both of them"]:::someclass {
                    +int x
                }
                Animal <|-- Styled
            """;

        var result = new ClassParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        var classes = result.Value.Classes;
        await Assert.That(string.Join(",", classes.Select(_ => _.Id))).IsEqualTo("Animal,Styled,Both");
        await Assert.That(classes[0].Name).IsEqualTo("Animal with a label");
        await Assert.That(classes[1].Name).IsEqualTo("Styled");
        await Assert.That(classes[2].Name).IsEqualTo("Both of them");
        await Assert.That(classes[2].Members.Single().Name).IsEqualTo("x");
        await Assert.That(result.Value.Relationships.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TrailingWhitespace_IsTolerated()
    {
        const string input = "classDiagram  \n    class Animal {  \n        +String name  \n    }  \n    Animal <|-- Dog  \n";

        var result = new ClassParser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Classes.Count).IsEqualTo(2);
        await Assert.That(result.Value.Classes[0].Members.Single().Name).IsEqualTo("name");
        await Assert.That(result.Value.Relationships.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UnclosedBody_FailsTheParse()
    {
        const string input =
            """
            classDiagram
                class Animal {
                    +String name
                class Dog
            """;

        await Assert.That(new ClassParser().Parse(input).Success).IsFalse();
    }
}
