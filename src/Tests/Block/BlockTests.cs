using Naiad.Diagrams.Block;

public class BlockTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            block-beta
                columns 3
                a["Block A"] b["Block B"] c["Block C"]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Span()
    {
        const string input =
            """
            block-beta
                columns 3
                a["Wide Block"]:2 b["Normal"]
                c["Full Width"]:3
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DifferentShapes()
    {
        const string input =
            """
            block-beta
                columns 3
                a["Rectangle"] b("Rounded") c(["Stadium"])
                d(("Circle")) e{"Diamond"} f{{"Hexagon"}}
            """;

        return VerifySvg(input);
    }

    // Every shape Mermaid's block diagram has, each sized to its label.
    [Test]
    public Task AllShapes()
    {
        const string input =
            """
            block
                columns 4
                a["Rectangle"] b("Rounded") c(["Stadium"]) d[["Subroutine"]]
                e[("Database")] f(("Circle")) g((("Double"))) h>"Asymmetric"]
                i{"Rhombus"} j{{"Hexagon"}} k[/"Lean right"/] l[\"Lean left"\]
                m[/"Trapezoid"\] n[\"Inverted"/] o
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task BlockArrows()
    {
        const string input =
            """
            block
              blockArrowId<["Label"]>(right)
              blockArrowId2<["Label"]>(left)
              blockArrowId3<["Label"]>(up)
              blockArrowId4<["Label"]>(down)
              blockArrowId5<["Label"]>(x)
              blockArrowId6<["Label"]>(y)
              blockArrowId7<["Label"]>(x, down)
            """;

        return VerifySvg(input);
    }

    // The column width comes from the widest block, so a long label widens every column rather than
    // spilling out of its box.
    [Test]
    public Task LongLabelWidensColumns()
    {
        const string input =
            """
            block
              columns 3
              a["A label that is much longer than the others"] b c
              d e:2
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Column()
    {
        const string input =
            """
            block-beta
                columns 1
                a["First"]
                b["Second"]
                c["Third"]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ManyColumns()
    {
        const string input =
            """
            block-beta
                columns 5
                a b c d e
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MixedLayout()
    {
        const string input =
            """
            block-beta
                columns 4
                header["Header"]:4
                nav["Nav"] content["Content"]:2 side["Sidebar"]
                footer["Footer"]:4
            """;

        return VerifySvg(input);
    }

    // With no `columns`, every block shares one row however the lines are broken, and `space` leaves a
    // gap of as many columns as it asks for.
    [Test]
    public Task SingleRowByDefault()
    {
        const string input =
            """
            block-beta
                a["First"] space b["Second"]
                c["Third"] space:2 d["Fourth"]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task NestedBlock()
    {
        const string input =
            """
            block
                block
                  D
                end
                A["A: I am a wide one"]
            """;

        return VerifySvg(input);
    }

    // A composite with a span of its own, and its own columns.
    [Test]
    public Task CompositeWidths()
    {
        const string input =
            """
            block
              columns 3
              a:3
              block:group1:2
                columns 2
                h i j k
              end
              g
              block:group2:3
                %% columns auto (default)
                l m n o p q r
              end
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task StackedInComposite()
    {
        const string input =
            """
            block
              block
                columns 1
                a["A label"] b c d
              end
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Links()
    {
        const string input =
            """
            block
              columns 5
              A space B space C
              A-->B
              B-- "X" -->C
              D space E space F
              D --- E
              E -.-> F
            """;

        return VerifySvg(input);
    }

    // The opening example of Mermaid's block diagram page: a block arrow, a composite, links from the
    // composite and from inside it, and a styled block.
    [Test]
    public Task CompositeLinksAndStyles()
    {
        const string input =
            """
            block
            columns 1
              db(("DB"))
              blockArrowId6<["&nbsp;&nbsp;&nbsp;"]>(down)
              block:ID
                A
                B["A wide one in the middle"]
                C
              end
              space
              D
              ID --> D
              C --> D
              style B fill:#969,stroke:#333,stroke-width:4px
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Styles()
    {
        const string input =
            """
            block
              A space B space id1 space id2
              A-->B
              id1("Start")-->id2("Stop")
              classDef blue fill:#6e6ce6,stroke:#333,stroke-width:4px;
              class A blue
              style B fill:#bbf,stroke:#f66,stroke-width:2px,color:#fff,stroke-dasharray: 5 5
              style id1 fill:#636,stroke:#333,stroke-width:4px
              style id2 fill:#bbf,stroke:#f66,stroke-width:2px,color:#fff,stroke-dasharray: 5 5
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SystemArchitecture()
    {
        const string input =
            """
            block
              columns 3
              Frontend blockArrowId6<[" "]>(right) Backend
              space:2 down<[" "]>(down)
              Disk left<[" "]>(left) Database[("Database")]

              classDef front fill:#696,stroke:#333;
              classDef back fill:#969,stroke:#333;
              class Frontend front
              class Backend,Database back
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task BusinessProcessFlow()
    {
        const string input =
            """
            block
              columns 3
              Start(("Start")) space:2
              down<[" "]>(down) space:2
              Decision{{"Make Decision"}} right<["Yes"]>(right) Process1["Process A"]
              downAgain<["No"]>(down) space r3<["Done"]>(down)
              Process2["Process B"] r2<["Done"]>(right) End(("End"))

              style Start fill:#969;
              style End fill:#696;
            """;

        return VerifySvg(input);
    }

    // The elements of every line were read as one list running to the end of the input, so a comment after
    // a line of elements failed the whole diagram, and a later `columns 2` was read as two blocks.
    [Test]
    public async Task StatementsBetweenLinesOfElements_AreRead()
    {
        var result = new BlockParser().Parse(
            """
            block-beta
                a b c
                %% a comment
                d
                columns 2
                e["Last"]
            """);

        await Assert.That(result.Error).IsNull();
        await Assert.That(string.Join(",", result.Value.Elements.Select(_ => _.Id))).IsEqualTo("a,b,c,d,e");
        await Assert.That(result.Value.Columns).IsEqualTo(2);
    }

    [Test]
    [Arguments("a[\"x\"]", BlockShape.Rectangle)]
    [Arguments("a(\"x\")", BlockShape.Rounded)]
    [Arguments("a([\"x\"])", BlockShape.Stadium)]
    [Arguments("a((\"x\"))", BlockShape.Circle)]
    [Arguments("a(((\"x\")))", BlockShape.DoubleCircle)]
    [Arguments("a[[\"x\"]]", BlockShape.Subroutine)]
    [Arguments("a[(\"x\")]", BlockShape.Cylinder)]
    [Arguments("a>\"x\"]", BlockShape.Asymmetric)]
    [Arguments("a{\"x\"}", BlockShape.Diamond)]
    [Arguments("a{{\"x\"}}", BlockShape.Hexagon)]
    [Arguments("a[/\"x\"/]", BlockShape.LeanRight)]
    [Arguments("a[\\\"x\"\\]", BlockShape.LeanLeft)]
    [Arguments("a[/\"x\"\\]", BlockShape.Trapezoid)]
    [Arguments("a[\\\"x\"/]", BlockShape.InvertedTrapezoid)]
    [Arguments("a[/x/]", BlockShape.LeanRight)]
    [Arguments("a[\\x/]", BlockShape.InvertedTrapezoid)]
    [Arguments("a(((x)))", BlockShape.DoubleCircle)]
    public async Task Shapes_AreRead(string element, BlockShape shape)
    {
        var block = new BlockParser().Parse($"block\n  {element}").Value.Elements.Single();

        await Assert.That(block.Shape).IsEqualTo(shape);
        await Assert.That(block.Label).IsEqualTo("x");
    }

    [Test]
    [Arguments("(right)", BlockArrowDirection.Right)]
    [Arguments("(left)", BlockArrowDirection.Left)]
    [Arguments("(up)", BlockArrowDirection.Up)]
    [Arguments("(down)", BlockArrowDirection.Down)]
    [Arguments("(x)", BlockArrowDirection.Left | BlockArrowDirection.Right)]
    [Arguments("(y)", BlockArrowDirection.Up | BlockArrowDirection.Down)]
    [Arguments("(x, down)", BlockArrowDirection.Left | BlockArrowDirection.Right | BlockArrowDirection.Down)]
    [Arguments(" ( Up , LEFT )", BlockArrowDirection.Up | BlockArrowDirection.Left)]
    public async Task BlockArrow_IsRead(string directions, BlockArrowDirection expected)
    {
        var block = new BlockParser().Parse($"block\n  arrow<[\"Label\"]>{directions}").Value.Elements.Single();

        await Assert.That(block.Shape).IsEqualTo(BlockShape.BlockArrow);
        await Assert.That(block.Label).IsEqualTo("Label");
        await Assert.That(block.ArrowDirections).IsEqualTo(expected);
    }

    // `&nbsp;` in a label stands for the character, as it does in Mermaid's HTML labels.
    [Test]
    public async Task Label_EntitiesAreDecoded()
    {
        var block = new BlockParser().Parse("block\n  a<[\"&nbsp;&nbsp;\"]>(down) b[\"Fish &amp; Chips\"]").Value;

        await Assert.That(block.Elements[0].Label).IsEqualTo("  ");
        await Assert.That(block.Elements[1].Label).IsEqualTo("Fish & Chips");
    }

    // A link written without spaces was read as a block with dashes in its id.
    [Test]
    [Arguments("A-->B", EdgeType.Arrow, null)]
    [Arguments("A --> B", EdgeType.Arrow, null)]
    [Arguments("A---B", EdgeType.Open, null)]
    [Arguments("A--xB", EdgeType.CrossEnd, null)]
    [Arguments("A --o B", EdgeType.CircleEnd, null)]
    [Arguments("A<-->B", EdgeType.BiDirectional, null)]
    [Arguments("A-.->B", EdgeType.DottedArrow, null)]
    [Arguments("A==>B", EdgeType.ThickArrow, null)]
    [Arguments("A-- \"X\" -->B", EdgeType.Arrow, "X")]
    [Arguments("A -- X --> B", EdgeType.Arrow, "X")]
    [Arguments("A-. \"maybe\" .->B", EdgeType.DottedArrow, "maybe")]
    public async Task Links_AreRead(string line, EdgeType type, string? label)
    {
        var model = new BlockParser().Parse($"block\n  {line}").Value;

        await Assert.That(string.Join(",", model.Elements.Select(_ => _.Id))).IsEqualTo("A,B");
        var edge = model.Edges.Single();
        await Assert.That(edge.SourceId).IsEqualTo("A");
        await Assert.That(edge.TargetId).IsEqualTo("B");
        await Assert.That(edge.Type).IsEqualTo(type);
        await Assert.That(edge.Label).IsEqualTo(label);
    }

    [Test]
    public async Task Links_Chain()
    {
        var model = new BlockParser().Parse("block\n  A --> B --> C").Value;

        await Assert.That(string.Join(",", model.Edges.Select(_ => $"{_.SourceId}>{_.TargetId}"))).IsEqualTo("A>B,B>C");
    }

    // A hyphen inside an id is still part of it.
    [Test]
    public async Task HyphenatedId_IsOneBlock()
    {
        var model = new BlockParser().Parse("block\n  my-block-->other").Value;

        await Assert.That(string.Join(",", model.Elements.Select(_ => _.Id))).IsEqualTo("my-block,other");
    }

    // A block named again in a link takes the label and shape given there, and keeps its place in the grid.
    [Test]
    public async Task LaterMention_UpdatesBlock()
    {
        var model = new BlockParser().Parse(
            """
            block
              id1 space id2
              id1("Start")-->id2("Stop")
            """).Value;

        await Assert.That(string.Join(",", model.Elements.Select(_ => _.Id))).IsEqualTo("id1,space,id2");
        await Assert.That(model.Elements[0].Label).IsEqualTo("Start");
        await Assert.That(model.Elements[0].Shape).IsEqualTo(BlockShape.Rounded);
        await Assert.That(model.Elements[2].Label).IsEqualTo("Stop");
    }

    // An unnamed nested `block … end` was read as two blocks labelled "block" and "end", with what it held
    // flattened into the diagram.
    [Test]
    public async Task UnnamedComposite_HoldsItsBlocks()
    {
        var model = new BlockParser().Parse(
            """
            block
                block
                  D
                end
                A["A: I am a wide one"]
            """).Value;

        await Assert.That(model.Elements.Count).IsEqualTo(2);
        var composite = model.Elements[0];
        await Assert.That(composite.IsComposite).IsTrue();
        await Assert.That(string.Join(",", composite.Children.Select(_ => _.Id))).IsEqualTo("D");
        await Assert.That(model.Elements[1].Id).IsEqualTo("A");
    }

    [Test]
    public async Task NamedComposite_HasSpanAndColumns()
    {
        var model = new BlockParser().Parse(
            """
            block
              columns 3
              block:group1:2
                columns 2
                h i
                block
                  x
                end
              end
              g
            """).Value;

        await Assert.That(model.Columns).IsEqualTo(3);
        var group = model.Elements[0];
        await Assert.That(group.Id).IsEqualTo("group1");
        await Assert.That(group.IsComposite).IsTrue();
        await Assert.That(group.Span).IsEqualTo(2);
        await Assert.That(group.Columns).IsEqualTo(2);
        await Assert.That(string.Join(",", group.Children.Select(_ => _.Id))).IsEqualTo("h,i,#composite2");
        await Assert.That(group.Children[2].Children.Single().Id).IsEqualTo("x");
        await Assert.That(model.Elements[1].Id).IsEqualTo("g");
    }

    // `end` closes a composite only on its own; with a shape it is a block like any other.
    [Test]
    public async Task End_WithShapeIsABlock()
    {
        var model = new BlockParser().Parse("block\n  Start((\"Start\")) End((\"End\"))").Value;

        await Assert.That(string.Join(",", model.Elements.Select(_ => _.Id))).IsEqualTo("Start,End");
    }

    [Test]
    [Arguments("block\n  block\n    a")]
    [Arguments("block\n  a\n  end")]
    public async Task UnbalancedComposite_Fails(string input)
    {
        var result = new BlockParser().Parse(input);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task Styles_AreApplied()
    {
        var model = new BlockParser().Parse(
            """
            block
              A B C
              classDef blue fill:#6e6ce6,stroke:#333,stroke-width:4px;
              class A,C blue
              style B,C fill:#bbf,stroke:#f66,color:#fff,stroke-dasharray: 5 5
            """).Value;

        var a = model.Elements[0].Style!;
        await Assert.That(a.Fill).IsEqualTo("#6e6ce6");
        await Assert.That(a.StrokeWidth).IsEqualTo(4);

        var b = model.Elements[1].Style!;
        await Assert.That(b.Fill).IsEqualTo("#bbf");
        await Assert.That(b.Color).IsEqualTo("#fff");
        await Assert.That(b.StrokeDasharray).IsEqualTo("5 5");

        // A `style` line wins over the class.
        var c = model.Elements[2].Style!;
        await Assert.That(c.Fill).IsEqualTo("#bbf");
        await Assert.That(c.StrokeWidth).IsEqualTo(4);
    }

    // Mermaid draws a style line it cannot make sense of as no style at all, rather than failing.
    [Test]
    public async Task MalformedStyle_IsIgnored()
    {
        var result = new BlockParser().Parse("  block\n    A\n    style A fill#969;");

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Value.Elements.Single().Style?.Fill).IsNull();
    }
}
