using Naiad.Diagrams.AgentFlow;

public class AgentFlowTests : TestBase
{
    // The basic example from Mermaid's documentation.
    [Test]
    public Task Basic()
    {
        const string input =
            """
            agentflow-beta TB
              flow reviewer["Review Agent"]
                changes["Gather changes"]@{ shape: input }
                analyse["Analyse diff"]@{ shape: task }
                lint["run_linter"]@{ shape: tool }
                ok["Clean?"]@{ shape: decision }

                changes --> analyse --> lint --> ok
              end
            """;

        return VerifySvg(input);
    }

    // Every alias, left to right, with a reference edge.
    [Test]
    public Task Shapes()
    {
        const string input =
            """
            agentflow-beta LR
              brief["Brief"]@{ shape: input }
              draft["Draft copy"]@{ shape: task }
              spellcheck["spell_check"]@{ shape: tool }
              guide["Style guide"]@{ shape: refdoc }
              publish["Publish"]@{ shape: action }

              brief --> draft --> spellcheck --> publish
              draft -.- guide
            """;

        return VerifySvg(input);
    }

    // The three operators, with labels.
    [Test]
    public Task Edges()
    {
        const string input =
            """
            agentflow-beta TB
              check["Tests pass?"]@{ shape: decision }
              ship["Ship it"]@{ shape: action }
              fix["Fix the build"]@{ shape: task }
              logs["Build logs"]@{ shape: refdoc }

              check -- yes --> ship
              check -- no --> fix
              fix --x check
              fix -.- logs
            """;

        return VerifySvg(input);
    }

    // Flows inside a flow, linked to each other by their ids.
    [Test]
    public Task NestedContainers()
    {
        const string input =
            """
            agentflow-beta TB
              flow team["Content Team"]
                flow researcher["Researcher"]
                  gather["Gather sources"]@{ shape: task }
                end

                flow writer["Writer"]
                  compose["Compose draft"]@{ shape: task }
                end

                researcher --> writer
              end
            """;

        return VerifySvg(input);
    }

    // The defaults example: two flows, nodes outside them and edges between flows.
    [Test]
    public Task ReleaseNotes()
    {
        const string input =
            """
            agentflow-beta TB
              brief["Release brief"]@{ shape: input }
              flow writer["Drafting Agent"]
                draft["Draft the notes"]@{ shape: task }
                lookup["changelog_search"]@{ shape: tool }
                guide["Tone of voice"]@{ shape: refdoc }
                draft --> lookup
                draft -.- guide
              end
              flow reviewer["Review Agent"]
                check["Check the claims"]@{ shape: task }
                ok["Accurate?"]@{ shape: decision }
                check --> ok
              end
              publish["Publish"]@{ shape: action }
              brief --> writer
              writer --> reviewer
              ok --> publish
            """;

        return VerifySvg(input);
    }

    // A node in a global block stays outside the flows that refer to it.
    [Test]
    public Task GlobalBlock()
    {
        const string input =
            """
            agentflow-beta TB
              global
                corpus["Shared corpus"]@{ shape: refdoc }
              end

              flow summariser["Summariser"]
                summarise["Summarise"]@{ shape: task }
                summarise -.- corpus
              end

              flow indexer["Indexer"]
                index["Build index"]@{ shape: task }
                index -.- corpus
              end
            """;

        return VerifySvg(input);
    }

    // A collapsed flow becomes one node, and the edges into and out of it end there.
    [Test]
    public Task Collapsed()
    {
        const string input =
            """
            agentflow-beta TB
              flow intake["Intake"]
                receive["Receive request"]@{ shape: input }
              end

              flow processing["Processing"]
                validate["Validate"]@{ shape: task }
                enrich["Enrich"]@{ shape: task }
                validate --> enrich
              end
              processing@{ view: "collapsed" }

              flow output["Output"]
                publish["Publish"]@{ shape: action }
              end

              receive --> validate
              enrich --> publish
            """;

        return VerifySvg(input);
    }

    // The worked example: a connector, metadata on flows and tools, and nesting two deep.
    [Test]
    public Task WorkedExample()
    {
        const string input =
            """
            agentflow-beta TB
              connector llm["LLM API"]
              llm@{ protocol: "http", endpoint: "https://api.example.com/chat" }

              flow coffee_team["Coffee Team"]
                city["city"]@{ shape: input, value: "Stockholm" }

                flow researcher["Researcher"]
                  research["research_location"]@{ shape: tool, params: "city :: String", returns: "Report" }
                  write["write_copy"]@{ shape: tool, connectorRef: "llm.chat", returns: "CoffeeCopy" }
                  city --> research --> write
                end
                researcher@{ instruction: "Research the city and draft English coffee copy citing sources." }

                flow designer["Designer"]
                  render["generate_html"]@{ shape: tool, connectorRef: "llm.chat", returns: "String" }
                  brand["Nordic Brand Guide"]@{ shape: refdoc }
                  render -.- brand
                end

                researcher --> designer
              end
            """;

        return VerifySvg(input);
    }

    // A title from front matter, a connector, and multi-line metadata.
    [Test]
    public Task TitleAndConnector()
    {
        const string input =
            """
            ---
            title: Issue triage
            ---
            agentflow-beta LR
              connector github["GitHub API"]
              github@{ protocol: "http", endpoint: "https://api.github.com" }

              title["Issue title"]@{ shape: input }
              create["create_issue"]@{
                shape: tool
                connectorRef: "github.create_issue"
              }

              title --> create
              create -.- github
            """;

        return VerifySvg(input);
    }

    // Bracket shapes, `&` groups, pipe labels, comments and styling.
    [Test]
    public Task FlowchartForms()
    {
        const string input =
            """
            agentflow-beta TB
              %% Bracket forms map onto agent flow's shapes.
              start(Start) --> pick{Route?}
              pick -->|left| left[[Left tool]] & right{{Right action}}
              left --> done[/Done/]
              right --x done
              classDef hot fill:#fdd,stroke:#c33
              class pick hot
              style done fill:#dfd
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_ShapesAndLabels()
    {
        var model = Parse(
            """
            agentflow-beta LR
              a["Task"]@{ shape: task }
              b["Tool"]@{ shape: tool }
              c["Input"]@{ shape: input }
              d["Decision"]@{ shape: decision }
              e["Doc"]@{ shape: refdoc }
              f["Action"]@{ shape: action }
              g
              h["Old"]@{ shape: cylinder }
              i@{ shape: hexagon, label: "From metadata" }
            """);

        await Assert.That(model.Direction).IsEqualTo(Direction.LeftToRight);
        await Assert.That(string.Join(",", model.Nodes.Select(_ => _.Shape))).IsEqualTo(
            "RoundedRectangle,Subroutine,Parallelogram,Diamond,LinedDocument,Hexagon,RoundedRectangle,RoundedRectangle,Hexagon");
        await Assert.That(string.Join(",", model.Nodes.Select(_ => _.Label))).IsEqualTo(
            "Task,Tool,Input,Decision,Doc,Action,g,Old,From metadata");
    }

    [Test]
    public async Task Parse_EdgeSemantics()
    {
        var model = Parse(
            """
            agentflow-beta
              a --> b --> c
              a -.- d
              c --x a
              a -- yes --> e
              e -->|done| f
              f e1@--> g
              e1@{ animate: true }
            """);

        await Assert.That(model.Direction).IsEqualTo(Direction.TopToBottom);
        await Assert.That(string.Join(";", model.AgentEdges.Select(_ => $"{_.SourceId}-{_.Semantic}-{_.TargetId}:{_.Label}"))).IsEqualTo(
            "a-Sequence-b:;b-Sequence-c:;a-Reference-d:;c-Failure-a:;a-Sequence-e:yes;e-Sequence-f:done;f-Sequence-g:");

        var named = model.AgentEdges.Single(_ => _.Id == "e1");
        await Assert.That(named.Metadata["animate"]).IsEqualTo("true");
        await Assert.That(model.GetNode("e1")).IsNull();
    }

    [Test]
    public async Task Parse_Containers()
    {
        var model = Parse(
            """
            agentflow-beta TB
              flow team["Team"]
                flow inner["Inner"]
                  x
                end
                y --> x
              end
              flow
                z
              end
            """);

        var team = model.GetContainer("team")!;
        await Assert.That(team.Title).IsEqualTo("Team");
        await Assert.That(string.Join(",", team.NodeIds)).IsEqualTo("y");
        await Assert.That(team.NestedSubgraphs.Single().Id).IsEqualTo("inner");
        await Assert.That(string.Join(",", model.GetContainer("inner")!.NodeIds)).IsEqualTo("x");

        // The anonymous flow takes Mermaid's generated id, counted in the order blocks close.
        var anonymous = model.Subgraphs[1];
        await Assert.That(anonymous.Id).IsEqualTo("subGraph2");
        await Assert.That(anonymous.Title).IsNull();
        await Assert.That(string.Join(",", anonymous.NodeIds)).IsEqualTo("z");
    }

    [Test]
    public async Task Parse_GlobalNodesStayTopLevel()
    {
        var model = Parse(
            """
            agentflow-beta TB
              flow one["One"]
                a -.- shared
              end
              global
                shared["Shared"]@{ shape: refdoc }
              end
            """);

        await Assert.That(string.Join(",", model.GetContainer("one")!.NodeIds)).IsEqualTo("a");
        await Assert.That(model.GetNode("shared")!.Label).IsEqualTo("Shared");
    }

    [Test]
    public async Task Parse_MetadataAndConnectors()
    {
        var model = Parse(
            """
            agentflow-beta TB
              connector gh["GitHub"]@{ protocol: "http" }
              fetch@{
                shape: tool
                params: "city :: String, country"
                retry: 2
                __proto__: bad
              }
              flow agent["Agent"]
                fetch
              end
              agent@{ view: collapsed, model: "claude" }
            """);

        var connector = (AgentFlowNode) model.GetNode("gh")!;
        await Assert.That(connector.Kind).IsEqualTo(AgentFlowNodeKind.Connector);
        await Assert.That(connector.Label).IsEqualTo("GitHub");
        await Assert.That(connector.Metadata["protocol"]).IsEqualTo("http");

        var fetch = (AgentFlowNode) model.GetNode("fetch")!;
        await Assert.That(fetch.Shape).IsEqualTo(NodeShape.Subroutine);
        await Assert.That(fetch.Metadata["params"]).IsEqualTo("city :: String, country");
        await Assert.That(fetch.Metadata["retry"]).IsEqualTo("2");
        await Assert.That(fetch.Metadata.ContainsKey("__proto__")).IsFalse();

        var agent = model.GetContainer("agent")!;
        await Assert.That(agent.Collapsed).IsTrue();
        await Assert.That(agent.Metadata["model"]).IsEqualTo("claude");
        await Assert.That(model.GetNode("agent")).IsNull();
    }

    [Test]
    public async Task Parse_RejectsUnclosedFlow()
    {
        var result = new AgentFlowParser().Parse(
            """
            agentflow-beta
              flow a
                b
            """);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task Parse_RejectsUnsupportedOperator()
    {
        var result = new AgentFlowParser().Parse(
            """
            agentflow-beta
              a ==> b
            """);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("agentflow-beta TB\n  a --> b", out var type)).IsTrue();
        await Assert.That(type).IsEqualTo(DiagramType.AgentFlow);
        await Assert.That(Mermaid.TryDetectType("agentflow-beta", out _)).IsTrue();

        // Mermaid knows the diagram by its -beta keyword alone, spelled in lower case.
        await Assert.That(Mermaid.TryDetectType("agentflow\n  a --> b", out _)).IsFalse();
        await Assert.That(Mermaid.TryDetectType("AGENTFLOW-BETA\n  a --> b", out _)).IsFalse();
        await Assert.That(Mermaid.TryDetectType("agentflow-betas\n  a --> b", out _)).IsFalse();
    }

    static AgentFlowModel Parse(string input)
    {
        var result = new AgentFlowParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }
}
