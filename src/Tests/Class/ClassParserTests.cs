using Naiad.Diagrams.Class;

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

    [Test]
    public async Task MemberStatements_AddToTheClass()
    {
        var model = new ClassParser().Parse(
            """
            classDiagram
                Animal <|-- Duck
                Animal : +int age
                Animal: +isMammal() bool
                Cat : -String name
            """).Value;

        var animal = model.Classes.Single(_ => _.Id == "Animal");
        await Assert.That(animal.Members.Single().Name).IsEqualTo("age");
        await Assert.That(animal.Members.Single().Type).IsEqualTo("int");
        await Assert.That(animal.Methods.Single().Name).IsEqualTo("isMammal");
        await Assert.That(animal.Methods.Single().ReturnType).IsEqualTo("bool");

        // A class first mentioned by a member statement is created by it.
        var cat = model.Classes.Single(_ => _.Id == "Cat");
        await Assert.That(cat.Members.Single().Name).IsEqualTo("name");
        await Assert.That(model.Relationships.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Annotations_InABodyOrOnTheirOwnLine()
    {
        var model = new ClassParser().Parse(
            """
            classDiagram
                class Shape
                <<interface>> Shape
                class Order {
                    <<Entity>>
                    +int id
                }
                class Color {
                    <<Enumeration>>
                    RED
                }
                <<Aggregate Root>> Basket
            """).Value;

        var shape = model.Classes.Single(_ => _.Id == "Shape");
        await Assert.That(shape.Annotation).IsEqualTo(ClassAnnotation.Interface);
        await Assert.That(shape.AnnotationText).IsNull();

        var order = model.Classes.Single(_ => _.Id == "Order");
        await Assert.That(order.Annotation).IsNull();
        await Assert.That(order.AnnotationText).IsEqualTo("Entity");
        await Assert.That(order.Members.Single().Name).IsEqualTo("id");

        await Assert.That(model.Classes.Single(_ => _.Id == "Color").Annotation)
            .IsEqualTo(ClassAnnotation.Enumeration);
        await Assert.That(model.Classes.Single(_ => _.Id == "Basket").AnnotationText)
            .IsEqualTo("Aggregate Root");

        var svg = Mermaid.Render("classDiagram\n    class Order {\n        <<Entity>>\n    }");
        await Assert.That(svg).Contains("&lt;&lt;Entity&gt;&gt;");
    }

    [Test]
    public async Task Notes_ForTheDiagramAndForAClass()
    {
        const string input =
            """
            classDiagram
                class Duck
                note "General note"
                note for Duck "can fly\ncan swim"
                note for Goose "honks"
            """;

        var model = new ClassParser().Parse(input).Value;

        await Assert.That(model.Notes.Count).IsEqualTo(3);
        await Assert.That(model.Notes[0].ForClassId).IsNull();
        await Assert.That(model.Notes[1].ForClassId).IsEqualTo("Duck");
        await Assert.That(model.Notes[1].Text).IsEqualTo("can fly<br/>can swim");
        await Assert.That(model.Classes.Any(_ => _.Id == "Goose")).IsTrue();

        var svg = Mermaid.Render(input);
        await Assert.That(svg).Contains(">General note<");
        await Assert.That(svg).Contains(">can fly<");
        await Assert.That(svg).Contains(">can swim<");
    }

    [Test]
    public async Task Namespace_GroupsItsClasses()
    {
        const string input =
            """
            classDiagram
                namespace BaseShapes {
                    class Triangle
                    class Rectangle {
                        double width
                    }
                }
                class Circle
                Triangle --> Circle
            """;

        var model = new ClassParser().Parse(input).Value;

        await Assert.That(string.Join(",", model.Classes.Select(_ => $"{_.Id}:{_.Namespace}")))
            .IsEqualTo("Triangle:BaseShapes,Rectangle:BaseShapes,Circle:");
        await Assert.That(model.Classes[1].Members.Single().Name).IsEqualTo("width");
        await Assert.That(Mermaid.Render(input)).Contains(">BaseShapes<");
    }

    [Test]
    [Arguments("classDiagram\n    namespace A {\n    class X")]
    [Arguments("classDiagram\n    class X\n    }")]
    [Arguments("classDiagram\n    namespace A {\n    namespace B {\n    }\n    }")]
    public async Task UnbalancedNamespace_FailsTheParse(string input) =>
        await Assert.That(() => Mermaid.Render(input)).Throws<MermaidParseException>();

    [Test]
    public async Task Lollipop_MarksTheEndItIsWrittenOn()
    {
        var model = new ClassParser().Parse(
            """
            classDiagram
                bar ()-- foo
                foo --() baz
            """).Value;

        await Assert.That(model.Relationships[0].FromMarker).IsEqualTo(RelationshipMarker.Lollipop);
        await Assert.That(model.Relationships[0].ToMarker).IsEqualTo(RelationshipMarker.None);
        await Assert.That(model.Relationships[1].ToMarker).IsEqualTo(RelationshipMarker.Lollipop);
        await Assert.That(Regex.Matches(Mermaid.Render("classDiagram\n    bar ()-- foo"), "<circle").Count).IsEqualTo(1);
    }

    [Test]
    public async Task BacktickAndHyphenatedNames_AreClassNames()
    {
        var model = new ClassParser().Parse(
            """
            classDiagram
                class `Animal Class!`
                class Order-Line {
                    +int qty
                }
                `Animal Class!` --> Order-Line : has
                Order-Line<|--Special-Line
            """).Value;

        await Assert.That(string.Join("|", model.Classes.Select(_ => _.Name)))
            .IsEqualTo("Animal Class!|Order-Line|Special-Line");
        await Assert.That(model.Relationships[0].FromId).IsEqualTo("Animal Class!");
        await Assert.That(model.Relationships[1].FromMarker).IsEqualTo(RelationshipMarker.Triangle);
    }

    [Test]
    public async Task StylingAndInteraction_AreIgnored()
    {
        var model = new ClassParser().Parse(
            """
            classDiagram
                class Animal
                classDef someclass fill:#f96
                cssClass "Animal" someclass
                style Animal fill:#f9f,stroke:#333
                click Animal href "https://example.com"
                link Animal "https://example.com"
                callback Animal "callbackFunction"
                class Dog
            """).Value;

        await Assert.That(string.Join(",", model.Classes.Select(_ => _.Id))).IsEqualTo("Animal,Dog");
    }
}
