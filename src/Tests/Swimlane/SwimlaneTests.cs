using Naiad.Diagrams.Swimlane;

public class SwimlaneTests : TestBase
{
    // The basic example from Mermaid's documentation.
    [Test]
    public Task Basic()
    {
        const string input =
            """
            swimlane-beta LR
              subgraph Customer
                request[Request service]
                receive[Receive update]
              end

              subgraph Support
                triage[Triage request]
                answer[Send answer]
              end

              subgraph Engineering
                investigate[Investigate issue]
                fix[Prepare fix]
              end

              request --> triage
              triage -->|Known issue| answer
              triage -->|Needs code change| investigate
              investigate --> fix --> answer
              answer --> receive
            """;

        return VerifySvg(input);
    }

    // No direction means top to bottom: the lanes are columns titled across the top.
    [Test]
    public Task TopToBottom()
    {
        const string input =
            """
            swimlane-beta
              subgraph Customer
                Browse[Browse catalogue]
                Pay[Pay]
              end
              subgraph Warehouse
                Pick[Pick items]
                Ship[Ship order]
              end
              subgraph Finance
                Invoice[Raise invoice]
              end
              Browse --> Pay
              Pay --> Pick
              Pick --> Ship
              Pay --> Invoice
            """;

        return VerifySvg(input);
    }

    // The shapes the documentation lists, and a loop back to an earlier node.
    [Test]
    public Task Shapes()
    {
        const string input =
            """
            swimlane-beta LR
              subgraph Intake
                start([Start])
                task[Do work]
                fix[Fix issues]
              end

              subgraph Review
                decision{Ready?}
              end

              subgraph Complete
                done((Done))
              end

              start --> task --> decision
              decision -->|Yes| done
              decision -->|No| fix
              fix --> task
            """;

        return VerifySvg(input);
    }

    // Lanes with an id and a display label, and a class applied to a node.
    [Test]
    public Task LaneLabelsAndClasses()
    {
        const string input =
            """
            swimlane-beta LR
              subgraph ops [Operations]
                intake[Receive request]
                plan[Plan work]
              end

              subgraph legal [Legal]
                review[Review contract]
              end

              intake --> plan --> review

              classDef attention fill:#fff2cc,stroke:#d6a500,color:#111;
              class review attention;
            """;

        return VerifySvg(input);
    }

    // The edge forms the documentation lists: open, dotted and thick links alongside arrows.
    [Test]
    public Task EdgeStyles()
    {
        const string input =
            """
            swimlane-beta TB
              subgraph Buyer
                choose[Choose product]
                pay[Pay invoice]
              end

              subgraph Store
                reserve[Reserve stock]
                ship[Ship product]
              end

              choose --- reserve
              reserve -.->|Invoice ready| pay
              pay ==> ship
            """;

        return VerifySvg(input);
    }

    // A decision whose "No" branch loops back to the first lane, from the documentation.
    [Test]
    public Task BackEdge()
    {
        const string input =
            """
            swimlane-beta TB
              subgraph Intake
                collect[Collect request]
                validate[Validate details]
              end

              subgraph Review
                review[Review request]
                decide{Ready?}
              end

              subgraph Delivery
                schedule[Schedule work]
                complete[Complete work]
              end

              collect --> validate --> review --> decide
              decide -->|Yes| schedule --> complete
              decide -->|No| collect
            """;

        return VerifySvg(input);
    }

    // Bottom to top and right to left run the flow the other way, with the lane titles where they were.
    [Test]
    public Task BottomToTop()
    {
        const string input =
            """
            swimlane-beta BT
              subgraph Sales
                lead[Qualify lead]
                quote[Prepare quote]
              end
              subgraph Finance
                approve[Approve quote]
              end
              lead --> quote --> approve
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task RightToLeft()
    {
        const string input =
            """
            swimlane-beta RL
              subgraph Sales
                lead[Qualify lead]
                quote[Prepare quote]
              end
              subgraph Finance
                approve[Approve quote]
              end
              lead --> quote --> approve
            """;

        return VerifySvg(input);
    }

    // A front matter title, a subgraph nested in a lane drawn as a box, a node outside every lane, an
    // empty lane and the accessibility directives.
    [Test]
    public Task TitleNestedAndOutside()
    {
        const string input =
            """
            ---
            title: Order handling
            ---
            swimlane-beta TB
              accTitle: Order handling
              accDescr: Orders move from the shop to the warehouse.
              subgraph Shop
                order[Take order]
                subgraph checks [Checks]
                  fraud[Fraud check]
                end
              end
              subgraph Warehouse
                pack[Pack]
              end
              subgraph Later
              end
              order --> fraud --> pack --> archive[Archive]
            """;

        return VerifySvg(input);
    }

    // Several nodes of one rank in one lane, fanning out and back in.
    [Test]
    public Task FanOut()
    {
        const string input =
            """
            swimlane-beta TB
              subgraph Planning
                plan[Plan release]
              end
              subgraph Teams
                web[Web app]
                api[API]
                docs[Docs]
              end
              subgraph Release
                ship[Ship]
              end
              plan --> web & api & docs
              web & api & docs --> ship
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_LanesNodesAndEdges()
    {
        var model = Parse(
            """
            swimlane-beta LR
              subgraph sales [Sales team]
                lead[Qualify lead]
                quote(Prepare quote)
              end
              subgraph Finance
                approve{Approve?}
              end
              lead --> quote -->|send| approve
            """);

        await Assert.That(model.Direction).IsEqualTo(Direction.LeftToRight);
        await Assert.That(model.Subgraphs.Select(_ => $"{_.Id}:{_.Title}:{string.Join(",", _.NodeIds)}"))
            .IsEquivalentTo(["sales:Sales team:lead,quote", "Finance:Finance:approve"]);
        await Assert.That(model.GetNode("quote")!.Shape).IsEqualTo(NodeShape.RoundedRectangle);
        await Assert.That(model.GetNode("approve")!.Shape).IsEqualTo(NodeShape.Diamond);
        await Assert.That(model.Edges.Select(_ => $"{_.SourceId}>{_.TargetId}:{_.Label}"))
            .IsEquivalentTo(["lead>quote:", "quote>approve:send"]);
    }

    [Test]
    public async Task Parse_DefaultsToTopToBottom()
    {
        var model = Parse(
            """
            swimlane-beta
              subgraph Sales
                lead[Qualify lead]
              end
            """);

        await Assert.That(model.Direction).IsEqualTo(Direction.TopToBottom);
    }

    // A node listed in two lanes stays in the first; the second lane, emptied by that, is dropped. A lane
    // left empty on purpose, or emptied but named by an edge, is kept.
    [Test]
    public async Task Parse_SharedNodeDropsEmptiedLane()
    {
        var model = Parse(
            """
            swimlane-beta
              subgraph One
                shared[Shared]
              end
              subgraph Two
                shared
              end
              subgraph Three
                shared
              end
              subgraph Empty
              end
              shared --> Three
            """);

        await Assert.That(model.Subgraphs.Select(_ => _.Id)).IsEquivalentTo(["One", "Three", "Empty"]);
        await Assert.That(model.Subgraphs[0].NodeIds).IsEquivalentTo(["shared"]);
    }

    [Test]
    public async Task Parse_RejectsOtherKeywords()
    {
        await Assert.That(new SwimlaneParser().Parse("swimlane\n  subgraph A\n  end").Success).IsFalse();
        await Assert.That(new SwimlaneParser().Parse("flowchart LR\n  A --> B").Success).IsFalse();
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("swimlane-beta LR\n  subgraph A\n  end", out var type)).IsTrue();
        await Assert.That(type).IsEqualTo(DiagramType.Swimlane);
        await Assert.That(Mermaid.TryDetectType("swimlane LR\n  subgraph A\n  end", out _)).IsFalse();
    }

    static SwimlaneModel Parse(string input)
    {
        var result = new SwimlaneParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }
}
