using Naiad.Diagrams.State;

public class StateParserTests
{
    // A composite block names a state that a transition has usually already introduced. Creating a second
    // State for it left the id in model.States twice, and the renderer drew it twice - once as a plain
    // state and once as a container. Declaration order must not change the outcome.
    [Test]
    [Arguments(
        "transition first",
        """
        stateDiagram-v2
            [*] --> Outer
            state Outer {
                [*] --> Inner
            }
            Outer --> Finished
        """)]
    [Arguments(
        "block first",
        """
        stateDiagram-v2
            state Outer {
                [*] --> Inner
            }
            Outer --> Finished
        """)]
    [Arguments(
        "described first",
        """
        stateDiagram-v2
            state "The outer one" as Outer
            [*] --> Outer
            state Outer {
                [*] --> Inner
            }
        """)]
    public async Task CompositeStateIsDeclaredOnce(string name, string input)
    {
        var result = new StateParser().Parse(input);
        await Assert.That(result.Success).IsTrue();

        var outer = result.Value.States.Where(_ => _.Id == "Outer").ToList();
        await Assert.That(outer.Count).IsEqualTo(1).Because($"{name}: Outer should appear once");
        await Assert.That(outer[0].IsComposite).IsTrue().Because($"{name}: Outer should keep its nested states");
    }

    // The description survives the composite block reusing the state that carries it.
    [Test]
    public async Task CompositeKeepsAnEarlierDescription()
    {
        var result = new StateParser().Parse(
            """
            stateDiagram-v2
                state "The outer one" as Outer
                state Outer {
                    [*] --> Inner
                }
            """);

        await Assert.That(result.Success).IsTrue();

        var outer = result.Value.States.Single(_ => _.Id == "Outer");
        await Assert.That(outer.Description).IsEqualTo("The outer one");
        await Assert.That(outer.IsComposite).IsTrue();
    }

    [Test]
    public async Task MultiLineNote_KeepsEachLine()
    {
        var model = new StateParser().Parse(
            """
            stateDiagram-v2
                [*] --> Active
                note right of Active
                    First line
                    second line

                    third line
                end note
                note left of Active : one\ntwo
                Active --> [*]
            """).Value;

        await Assert.That(model.Notes.Count).IsEqualTo(2);
        await Assert.That(model.Notes[0].Text).IsEqualTo("First line<br/>second line<br/>third line");
        await Assert.That(model.Notes[0].Position).IsEqualTo(NotePosition.RightOf);
        await Assert.That(model.Notes[1].Text).IsEqualTo("one<br/>two");
        await Assert.That(model.Transitions.Count).IsEqualTo(2);
    }

    [Test]
    public async Task UnclosedNote_FailsTheParse() =>
        await Assert.That(new StateParser().Parse("stateDiagram-v2\n    note right of A\n    text\n    A --> B").Success)
            .IsFalse();

    [Test]
    public async Task Divider_SplitsACompositeIntoRegions()
    {
        var model = new StateParser().Parse(StateSamples.Concurrency).Value;

        var active = model.States.Single(_ => _.Id == "Active");
        await Assert.That(active.NestedStates.Count).IsEqualTo(3);
        await Assert.That(active.NestedStates.All(_ => _.Type == StateType.Region)).IsTrue();
        await Assert.That(active.NestedTransitions.Count).IsEqualTo(0);

        // Each region keeps its own states and transitions, and its own start marker.
        await Assert.That(string.Join("|", active.NestedStates.Select(_ => string.Join(",", _.NestedStates.Select(s => s.Id)))))
            .IsEqualTo(
                "Active.[*]_start,NumLockOff,NumLockOn|" +
                "Active.region2.[*]_start,CapsLockOff,CapsLockOn|" +
                "Active.region3.[*]_start,ScrollLockOff,ScrollLockOn");
        await Assert.That(string.Join(",", active.NestedStates.Select(_ => _.NestedTransitions.Count))).IsEqualTo("3,2,2");
    }

    [Test]
    public async Task Divider_WithNothingAfterIt_AddsNoRegion()
    {
        var model = new StateParser().Parse(
            """
            stateDiagram-v2
                state Active {
                    A --> B
                    --
                }
                --
                Active --> C
            """).Value;

        var active = model.States.Single(_ => _.Id == "Active");
        await Assert.That(active.NestedStates.Single().Type).IsEqualTo(StateType.Region);
        await Assert.That(string.Join(",", model.States.Select(_ => _.Id))).IsEqualTo("Active,C");
    }

    [Test]
    public async Task CompositeDeclaredWithADescription_KeepsBoth()
    {
        const string input =
            """
            stateDiagram-v2
                state "Handling the order" as Handling {
                    [*] --> Picking
                }
                Handling --> Shipped
            """;

        var handling = new StateParser().Parse(input).Value.States.Single(_ => _.Id == "Handling");

        await Assert.That(handling.Description).IsEqualTo("Handling the order");
        await Assert.That(handling.IsComposite).IsTrue();

        var svg = Mermaid.Render(input);
        await Assert.That(svg).Contains(">Handling the order<");
        await Assert.That(svg).DoesNotContain(">Handling<");
    }

    [Test]
    public async Task DirectionInsideAComposite_IsThatComposites()
    {
        var model = new StateParser().Parse(
            """
            stateDiagram-v2
                direction TB
                state Outer {
                    direction LR
                    A --> B
                }
            """).Value;

        await Assert.That(model.Direction).IsEqualTo(Direction.TopToBottom);

        var outer = model.States.Single();
        await Assert.That(outer.Direction).IsEqualTo(Direction.LeftToRight);

        new StateRenderer().Render(model, RenderOptions.Default);
        var a = outer.NestedStates.Single(_ => _.Id == "A");
        var b = outer.NestedStates.Single(_ => _.Id == "B");
        await Assert.That(b.Position.X).IsGreaterThan(a.Position.X);
        await Assert.That(Math.Abs(a.Position.Y - b.Position.Y)).IsLessThan(1);
    }

    // None of these is drawn, and none of them may be mistaken for a state or stop the parse.
    [Test]
    [Arguments("accTitle: The title")]
    [Arguments("accDescr: The description")]
    [Arguments("accDescr {\n        Several\n        lines\n    }")]
    [Arguments("classDef movement font-style:italic")]
    [Arguments("classDef badBadEvent fill:#f00,color:white,font-weight:bold")]
    [Arguments("class A, B movement")]
    [Arguments("style A fill:#f9f")]
    [Arguments("click A href \"https://example.com\"")]
    [Arguments("hide empty description")]
    [Arguments("scale 350 width")]
    public async Task UndrawnLine_IsIgnored(string line)
    {
        var result = new StateParser().Parse($"stateDiagram-v2\n    [*] --> A\n    {line}\n    A --> B");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(string.Join(",", result.Value.States.Select(_ => _.Id))).IsEqualTo("[*]_start,A,B");
        await Assert.That(result.Value.Transitions.Count).IsEqualTo(2);
    }

    [Test]
    public async Task StyleClassOnAState_IsDropped()
    {
        var model = new StateParser().Parse(
            """
            stateDiagram-v2
                [*] --> Still:::calm
                Still:::calm --> Moving:::movement : go
                Crash:::bad-event
                state Moving:::movement
            """).Value;

        await Assert.That(string.Join(",", model.States.Select(_ => _.Id))).IsEqualTo("[*]_start,Still,Moving,Crash");
        await Assert.That(model.Transitions[1].Label).IsEqualTo("go");
    }

    [Test]
    public async Task SquareBracketTypes_AndTrailingWhitespace()
    {
        var model = new StateParser().Parse(
            "stateDiagram-v2  \n    state f [[fork]]  \n    state c [[choice]]\n    state Outer {  \n        A --> B  \n    }  \n    B : Desc  \n").Value;

        await Assert.That(model.States.Single(_ => _.Id == "f").Type).IsEqualTo(StateType.Fork);
        await Assert.That(model.States.Single(_ => _.Id == "c").Type).IsEqualTo(StateType.Choice);

        var outer = model.States.Single(_ => _.Id == "Outer");
        await Assert.That(outer.NestedStates.Single(_ => _.Id == "B").Description).IsEqualTo("Desc");
    }
}
