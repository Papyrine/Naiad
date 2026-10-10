using Naiad.Diagrams.EntityRelationship;

public class ERParserTests
{
    [Test]
    public async Task StandaloneEntities_AreDeclared()
    {
        var model = new ERParser().Parse(
            """
            erDiagram
                CUSTOMER
                ORDER["Customer order"]
                "Line Item"
                EMPTY {}
                SPACED { }
                CUSTOMER ||--o{ ORDER : places
            """).Value;

        await Assert.That(string.Join("|", model.Entities.Select(_ => _.Name)))
            .IsEqualTo("CUSTOMER|ORDER|Line Item|EMPTY|SPACED");
        await Assert.That(model.Entities[1].Alias).IsEqualTo("Customer order");
        await Assert.That(model.Relationships.Count).IsEqualTo(1);
    }

    [Test]
    public async Task QuotedNames_WorkInDefinitionsAndRelationships()
    {
        var model = new ERParser().Parse(
            """
            erDiagram
                "Customer Account" ||--o{ "Order (open)" : places
                "Customer Account" {
                    string name
                }
            """).Value;

        await Assert.That(string.Join("|", model.Entities.Select(_ => _.Name)))
            .IsEqualTo("Customer Account|Order (open)");
        await Assert.That(model.Entities[0].Attributes.Single().Name).IsEqualTo("name");
        await Assert.That(model.Relationships.Single().ToEntity).IsEqualTo("Order (open)");
    }

    [Test]
    public async Task SeveralKeys_AreAllKept()
    {
        const string input =
            """
            erDiagram
                LINK {
                    int left PK, FK
                    int right pk,fk,uk "all three"
                    int plain
                    int single UK
                }
            """;

        var attributes = new ERParser().Parse(input).Value.Entities.Single().Attributes;

        await Assert.That(string.Join("|", attributes.Select(_ => string.Join(",", _.KeyTypes))))
            .IsEqualTo("PrimaryKey,ForeignKey|PrimaryKey,ForeignKey,UniqueKey||UniqueKey");
        await Assert.That(attributes[0].KeyType).IsEqualTo(AttributeKeyType.PrimaryKey);
        await Assert.That(attributes[1].Comment).IsEqualTo("all three");
        await Assert.That(attributes[2].KeyType).IsEqualTo(AttributeKeyType.None);

        var svg = Mermaid.Render(input);
        await Assert.That(svg).Contains(">PK,FK<");
        await Assert.That(svg).Contains(">PK,FK,UK<");
    }

    [Test]
    [Arguments("only one", "zero or one", Cardinality.ExactlyOne, Cardinality.ZeroOrOne)]
    [Arguments("1", "one or zero", Cardinality.ExactlyOne, Cardinality.ZeroOrOne)]
    [Arguments("zero or more", "one or more", Cardinality.ZeroOrMore, Cardinality.OneOrMore)]
    [Arguments("zero or many", "one or many", Cardinality.ZeroOrMore, Cardinality.OneOrMore)]
    [Arguments("many(0)", "many(1)", Cardinality.ZeroOrMore, Cardinality.OneOrMore)]
    [Arguments("0+", "1+", Cardinality.ZeroOrMore, Cardinality.OneOrMore)]
    [Arguments("||", "one or more", Cardinality.ExactlyOne, Cardinality.OneOrMore)]
    [Arguments("One Or More", "o{", Cardinality.OneOrMore, Cardinality.ZeroOrMore)]
    public async Task WordCardinalities(string left, string right, Cardinality expectedLeft, Cardinality expectedRight)
    {
        var relationship = new ERParser().Parse($"erDiagram\n    CAR {left} to {right} NAMED-DRIVER : allows").Value.Relationships.Single();

        await Assert.That(relationship.FromEntity).IsEqualTo("CAR");
        await Assert.That(relationship.ToEntity).IsEqualTo("NAMED-DRIVER");
        await Assert.That(relationship.FromCardinality).IsEqualTo(expectedLeft);
        await Assert.That(relationship.ToCardinality).IsEqualTo(expectedRight);
        await Assert.That(relationship.Identifying).IsTrue();
        await Assert.That(relationship.Label).IsEqualTo("allows");
    }

    [Test]
    public async Task OptionallyTo_IsNonIdentifying()
    {
        var relationships = new ERParser().Parse(
            """
            erDiagram
                A 1 optionally to 0+ B : loose
                A || .. o{ C : spaced
            """).Value.Relationships;

        await Assert.That(relationships[0].Identifying).IsFalse();
        await Assert.That(relationships[1].Identifying).IsFalse();
        await Assert.That(relationships[1].ToCardinality).IsEqualTo(Cardinality.ZeroOrMore);
    }

    [Test]
    [Arguments("TB", Direction.TopToBottom)]
    [Arguments("LR", Direction.LeftToRight)]
    [Arguments("RL", Direction.RightToLeft)]
    [Arguments("BT", Direction.BottomToTop)]
    public async Task Direction_IsRead(string keyword, Direction expected)
    {
        var model = new ERParser().Parse($"erDiagram\n    direction {keyword}\n    A ||--o{{ B : has").Value;

        await Assert.That(model.Direction).IsEqualTo(expected);
        await Assert.That(model.Entities.Count).IsEqualTo(2);
    }

    [Test]
    public async Task BodyComments_BlankLinesAndTrailingWhitespace_AreTolerated()
    {
        var model = new ERParser().Parse(
            "erDiagram  \n    CUSTOMER {  \n        %% identity\n        string name  \n\n        %% contact\n        string email PK  \n    }  \n    CUSTOMER ||--o{ ORDER : places  \n").Value;

        await Assert.That(string.Join(",", model.Entities[0].Attributes.Select(_ => _.Name))).IsEqualTo("name,email");
        await Assert.That(model.Entities.Count).IsEqualTo(2);
        await Assert.That(model.Relationships.Single().Label).IsEqualTo("places");
    }

    // None of these is drawn, and none of them may be mistaken for an entity or stop the parse.
    [Test]
    [Arguments("accTitle: The title")]
    [Arguments("accDescr: The description")]
    [Arguments("accDescr {\n        Several\n        lines\n    }")]
    [Arguments("classDef highlight fill:#f9f,stroke:#333")]
    [Arguments("class CUSTOMER, ORDER highlight")]
    [Arguments("style CUSTOMER fill:#f9f")]
    public async Task UndrawnLine_IsIgnored(string line)
    {
        var result = new ERParser().Parse($"erDiagram\n    CUSTOMER ||--o{{ ORDER : places\n    {line}\n    ORDER ||--|{{ ITEM : contains");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(string.Join(",", result.Value.Entities.Select(_ => _.Name))).IsEqualTo("CUSTOMER,ORDER,ITEM");
    }

    [Test]
    public async Task StyleClassOnAnEntity_IsDropped()
    {
        var model = new ERParser().Parse(
            """
            erDiagram
                CAR:::highlight ||--o{ DRIVER:::quiet,dim : has
                GARAGE:::highlight
                OWNER["The owner"]:::highlight {
                    string name
                }
            """).Value;

        await Assert.That(string.Join(",", model.Entities.Select(_ => _.Name))).IsEqualTo("CAR,DRIVER,GARAGE,OWNER");
        await Assert.That(model.Entities[3].Alias).IsEqualTo("The owner");
        await Assert.That(model.Relationships.Single().Label).IsEqualTo("has");
    }

    [Test]
    [Arguments("erDiagram\n    CUSTOMER ||--o{")]
    [Arguments("erDiagram\n    CUSTOMER {\n        string name")]
    [Arguments("erDiagram\n    CUSTOMER maybe to 1 ORDER")]
    public async Task BrokenStatement_FailsTheParse(string input) =>
        await Assert.That(new ERParser().Parse(input).Success).IsFalse();
}
