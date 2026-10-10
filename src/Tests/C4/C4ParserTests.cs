using Naiad.Diagrams.C4;

public class C4ParserTests
{
    [Test]
    public async Task CapturesRelationshipDirections()
    {
        const string input =
            """
            C4Context
                System(a, "A")
                System(b, "B")
                Rel(a, b, "default")
                Rel_D(a, b, "down")
                Rel_U(a, b, "up")
                Rel_L(a, b, "left")
                Rel_R(a, b, "right")
                Rel_Back(b, a, "back")
                Rel_Neighbor(a, b, "neighbor")
            """;

        var result = new C4Parser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Relationships.Select(_ => _.Direction))
            .IsEquivalentTo(
            [
                C4RelationshipDirection.Default,
                C4RelationshipDirection.Down,
                C4RelationshipDirection.Up,
                C4RelationshipDirection.Left,
                C4RelationshipDirection.Right,
                C4RelationshipDirection.Back,
                C4RelationshipDirection.Neighbor
            ]);
    }

    [Test]
    public async Task CapturesRelationshipTechnology()
    {
        const string input =
            """
            C4Context
                System(a, "A")
                System(b, "B")
                Rel(a, b, "Uses", "HTTPS")
            """;

        var result = new C4Parser().Parse(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Value.Relationships.Count).IsEqualTo(1);
        await Assert.That(result.Value.Relationships[0].Technology).IsEqualTo("HTTPS");
    }

    [Test]
    [Arguments("Person", C4ElementType.Person, false)]
    [Arguments("Person_Ext", C4ElementType.Person, true)]
    [Arguments("System", C4ElementType.System, false)]
    [Arguments("SystemDb_Ext", C4ElementType.SystemDb, true)]
    [Arguments("SystemQueue", C4ElementType.SystemQueue, false)]
    [Arguments("SystemQueue_Ext", C4ElementType.SystemQueue, true)]
    [Arguments("Container", C4ElementType.Container, false)]
    [Arguments("ContainerDb_Ext", C4ElementType.ContainerDb, true)]
    [Arguments("ContainerQueue", C4ElementType.ContainerQueue, false)]
    [Arguments("Component_Ext", C4ElementType.Component, true)]
    [Arguments("ComponentDb", C4ElementType.ComponentDb, false)]
    [Arguments("ComponentDb_Ext", C4ElementType.ComponentDb, true)]
    [Arguments("ComponentQueue", C4ElementType.ComponentQueue, false)]
    [Arguments("ComponentQueue_Ext", C4ElementType.ComponentQueue, true)]
    public async Task EveryElementKeyword(string keyword, C4ElementType type, bool external)
    {
        var element = new C4Parser().Parse($"C4Context\n    {keyword}(thing, \"The thing\")").Value.Elements.Single();

        await Assert.That(element.Id).IsEqualTo("thing");
        await Assert.That(element.Label).IsEqualTo("The thing");
        await Assert.That(element.Type).IsEqualTo(type);
        await Assert.That(element.IsExternal).IsEqualTo(external);
    }

    [Test]
    public async Task ElementArguments_ByPositionAndByName()
    {
        var elements = new C4Parser().Parse(
            """
            C4Container
                Person(user, "User", "Has, a comma", $tags="v1", $link="https://example.com")
                System(sys, "System", $descr="Named description")
                Container(api, "API", "Go", "Serves requests", $sprite="go")
                Container(web, "Web", "", "No technology")
                Component(job, "Job", $techn="Cron", $descr="Runs nightly")
                System( spaced , "Spaced" )
                System(bare)
            """).Value.Elements;

        await Assert.That(elements[0].Description).IsEqualTo("Has, a comma");
        await Assert.That(elements[1].Description).IsEqualTo("Named description");
        await Assert.That(elements[2].Technology).IsEqualTo("Go");
        await Assert.That(elements[2].Description).IsEqualTo("Serves requests");
        await Assert.That(elements[3].Technology).IsNull();
        await Assert.That(elements[3].Description).IsEqualTo("No technology");
        await Assert.That(elements[4].Technology).IsEqualTo("Cron");
        await Assert.That(elements[4].Description).IsEqualTo("Runs nightly");
        await Assert.That(elements[5].Id).IsEqualTo("spaced");
        await Assert.That(elements[6].Label).IsEqualTo("bare");
    }

    [Test]
    public async Task Boundaries_OfEveryKind()
    {
        var boundaries = new C4Parser().Parse(
            """
            C4Deployment
                Boundary(b1, "Plain")
                {
                }
                Boundary(b2, "Typed", "Team") {
                    Enterprise_Boundary(b3, "Enterprise") {
                        System_Boundary(b4, "System", $tags="v1") {
                            Container_Boundary(b5, "Container") {
                                System(inner, "Inner")
                            }
                        }
                    }
                }
                Deployment_Node(n1, "Server", "Ubuntu 22.04", "Hosts the API") {
                    Node_L(n2, "Left") {
                    }
                    Node_R(n3, "Right", $type="Docker") {
                    }
                }
            """).Value.Boundaries;

        await Assert.That(string.Join(",", boundaries.Select(_ => $"{_.Id}:{_.Type}:{_.TypeLabel}")))
            .IsEqualTo(
                "b1:Generic:,b2:Generic:Team,b3:Enterprise:,b4:System:,b5:Container:," +
                "n1:Deployment:Ubuntu 22.04,n2:Node:,n3:Node:Docker");
        await Assert.That(boundaries[4].ParentBoundaryId).IsEqualTo("b4");
        await Assert.That(boundaries[4].ElementIds.Single()).IsEqualTo("inner");
    }

    [Test]
    public async Task BoundaryType_IsShownInTheCaption()
    {
        var svg = Mermaid.Render(
            """
            C4Context
                Boundary(b1, "Typed", "Product team") {
                    System(a, "A")
                }
                Boundary(b2, "Untyped") {
                    System(b, "B")
                }
                System_Boundary(b3, "System") {
                    System(c, "C")
                }
            """);

        await Assert.That(svg).Contains(">[Product team]<");
        await Assert.That(svg).Contains(">[System]<");
        await Assert.That(Regex.Matches(svg, @">\[[^<]*\]<").Count).IsEqualTo(2);
    }

    [Test]
    public async Task LongDirectionNames_BiRelAndRelIndex()
    {
        var relationships = new C4Parser().Parse(
            """
            C4Dynamic
                System(a, "A")
                System(b, "B")
                Rel_Up(a, b, "up")
                Rel_Down(a, b, "down")
                Rel_Left(a, b, "left")
                Rel_Right(a, b, "right")
                BiRel(a, b, "both", "HTTPS")
                RelIndex(3, a, b, "third", "gRPC")
                Rel(a, b, "named", $techn="AMQP", $tags="async")
                Rel(a, b)
            """).Value.Relationships;

        await Assert.That(string.Join(",", relationships.Select(_ => _.Direction)))
            .IsEqualTo("Up,Down,Left,Right,Default,Default,Default,Default");
        await Assert.That(string.Join(",", relationships.Select(_ => _.IsBidirectional)))
            .IsEqualTo("False,False,False,False,True,False,False,False");
        await Assert.That(relationships[4].Technology).IsEqualTo("HTTPS");
        await Assert.That(relationships[5].From).IsEqualTo("a");
        await Assert.That(relationships[5].To).IsEqualTo("b");
        await Assert.That(relationships[5].Label).IsEqualTo("third");
        await Assert.That(relationships[5].Technology).IsEqualTo("gRPC");
        await Assert.That(relationships[6].Technology).IsEqualTo("AMQP");
        await Assert.That(relationships[7].Label).IsNull();
    }

    // A two-way relationship has an arrowhead at each end; an ordinary one has one.
    [Test]
    [Arguments("Rel", 1)]
    [Arguments("BiRel", 2)]
    public async Task ArrowheadCount(string keyword, int expected)
    {
        var svg = Mermaid.Render($"C4Context\n    System(a, \"A\")\n    System(b, \"B\")\n    {keyword}(a, b, \"uses\")");

        await Assert.That(Regex.Matches(svg, @"fill=.#666. stroke=.none.|stroke=.none. fill=.#666.|<path[^>]*Z[^>]*fill=.#666.").Count)
            .IsEqualTo(expected);
    }

    [Test]
    public async Task DynamicDiagram_IsDetectedAndRendered()
    {
        const string input =
            """
            C4Dynamic
                title Sign in
                Container(spa, "App")
                Container(api, "API")
                RelIndex(1, spa, api, "Calls")
            """;

        await Assert.That(new C4Parser().Parse(input).Value.Type).IsEqualTo(C4DiagramType.Dynamic);
        await Assert.That(Mermaid.Render(input)).Contains(">Calls<");
    }

    // None of these is drawn, and none of them may stop the parse or add anything to the diagram.
    [Test]
    [Arguments("UpdateElementStyle(a, $fontColor=\"red\", $bgColor=\"grey\")")]
    [Arguments("UpdateRelStyle(a, b, $textColor=\"blue\", $offsetX=\"5\")")]
    [Arguments("UpdateBoundaryStyle(a, $borderColor=\"red\")")]
    [Arguments("UpdateLayoutConfig($c4ShapeInRow=\"3\", $c4BoundaryInRow=\"1\")")]
    [Arguments("AddElementTag(\"v1\", $bgColor=\"green\")")]
    [Arguments("AddRelTag(\"async\", $lineColor=\"orange\")")]
    [Arguments("SHOW_LEGEND()")]
    [Arguments("LAYOUT_LEFT_RIGHT()")]
    [Arguments("accTitle: The title")]
    [Arguments("accDescr: The description")]
    [Arguments("accDescr {\n        Several\n        lines\n    }")]
    public async Task UndrawnStatement_IsIgnored(string line)
    {
        var result = new C4Parser().Parse($"C4Context\n    System(a, \"A\")\n    {line}\n    System(b, \"B\")\n    Rel(a, b, \"uses\")");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(string.Join(",", result.Value.Elements.Select(_ => _.Id))).IsEqualTo("a,b");
        await Assert.That(result.Value.Relationships.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("C4Context\n    Gadget(a, \"A\")")]
    [Arguments("C4Context\n    System(a, \"A\"")]
    [Arguments("C4Context\n    System(a, \"A\") {\n    }")]
    [Arguments("C4Context\n    System_Boundary(b, \"B\")")]
    [Arguments("C4Context\n    System_Boundary(b, \"B\") {\n        System(a, \"A\")")]
    public async Task BrokenStatement_FailsTheParse(string input) =>
        await Assert.That(new C4Parser().Parse(input).Success).IsFalse();
}
