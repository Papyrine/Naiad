// Inputs that used to hang the renderer, kill the process, or escape as a raw framework exception.
public class RobustnessTests
{
    // The column assignment kept pushing the nodes of a cycle to the right of each other, forever.
    [Test]
    [Arguments("cycle", "sankey-beta\nS,A,1\nA,B,1\nB,A,1")]
    [Arguments("self link", "sankey-beta\nS,A,1\nA,A,1")]
    [Arguments("nothing but a cycle", "sankey-beta\nA,B,1\nB,A,1")]
    public async Task Sankey_CircularLink_IsRejected(string name, string input)
    {
        var exception = await Assert.That(() => Mermaid.Render(input)).Throws<MermaidException>().Because(name);

        await Assert.That(exception!.Message).Contains("circular link").Because(name);
    }

    [Test]
    public async Task Sankey_SharedTargets_AreNotMistakenForACycle()
    {
        var svg = Mermaid.Render("sankey-beta\nA,C,1\nB,C,2\nA,D,1\nC,D,3");

        await Assert.That(svg).Contains("<svg");
    }

    [Test]
    [Arguments("stateDiagram-v2")]
    [Arguments("stateDiagram")]
    [Arguments("stateDiagram-v2\n    %% nothing yet")]
    public async Task State_WithNoStates_RendersAnEmptyDiagram(string input)
    {
        var svg = Mermaid.Render(input);

        await Assert.That(svg).Contains("<svg");
        await Assert.That(svg).DoesNotContain("NaN");
    }

    // A subgraph holding only empty subgraphs had no leaf to stand in for it, so an edge to it was ranked
    // against the compound node itself and the layout threw.
    [Test]
    [Arguments("edge in", "flowchart TB\n    subgraph S1\n        subgraph S2\n        end\n    end\n    a --> S1")]
    [Arguments("edge out", "flowchart TB\n    subgraph S1\n        subgraph S2\n        end\n    end\n    S1 --> a")]
    [Arguments("two deep", "flowchart LR\n    subgraph S1\n        subgraph S2\n            subgraph S3\n            end\n        end\n    end\n    a --> S1 --> b")]
    public async Task Flowchart_EdgeToSubgraphOfEmptySubgraphs_Renders(string name, string input)
    {
        var svg = Mermaid.Render(input);

        await Assert.That(svg).Contains("class='cluster'").Because(name);
        await Assert.That(svg).DoesNotContain("NaN").Because(name);
    }

    [Test]
    public async Task Architecture_DuplicateServiceId_IsRejectedWithAReason()
    {
        const string input =
            """
            architecture-beta
            service db(database)[Database]
            service db(database)[Database 2]
            """;

        var exception = await Assert.That(() => Mermaid.Render(input)).Throws<MermaidException>();

        await Assert.That(exception!.Message).Contains("The service id [db] is already in use");
    }

    // The layout walked the graph by recursion, as deep as its longest path. A few thousand nodes in a
    // chain overflowed the stack, which cannot be caught and takes the process down. A deliberately small
    // stack makes that depth cheap to reach here: the recursive walks would not survive this.
    [Test]
    [Arguments("flowchart TD", "    N{0} --> N{1}")]
    [Arguments("stateDiagram-v2", "    S{0} --> S{1}")]
    [Arguments("classDiagram", "    C{0} <|-- C{1}")]
    [Arguments("erDiagram", "    E{0} ||--o{{ E{1} : has")]
    public async Task LongChain_DoesNotOverflowTheStack(string header, string link)
    {
        var input = new StringBuilder(header).Append('\n');
        for (var i = 0; i < 2500; i++)
        {
            input.AppendFormat(CultureInfo.InvariantCulture, link, i, i + 1).Append('\n');
        }

        var (svg, failure) = RenderOnSmallStack(input.ToString());

        await Assert.That(failure).IsNull();
        await Assert.That(svg).IsNotNull();
    }

    // The sequence renderer planned its rows by recursing into each block, as deep as the blocks nest.
    // ZenUML folds its braces with a stack of its own, so nothing stopped a few thousand nested `if`s
    // from reaching that recursion and overflowing the stack.
    [Test]
    [Arguments("sequenceDiagram", "    alt c{0}", "    end", "    A->>B: hi")]
    [Arguments("zenuml", "    if(c{0}) {{", "    }}", "    A.m()")]
    public async Task DeepNesting_DoesNotOverflowTheStack(string header, string open, string close, string body)
    {
        const int depth = 1000;
        var input = new StringBuilder(header).Append('\n');
        for (var i = 0; i < depth; i++)
        {
            input.AppendFormat(CultureInfo.InvariantCulture, open, i).Append('\n');
        }

        input.Append(body).Append('\n');
        for (var i = 0; i < depth; i++)
        {
            input.AppendFormat(CultureInfo.InvariantCulture, close).Append('\n');
        }

        var (svg, failure) = RenderOnSmallStack(input.ToString());

        await Assert.That(failure).IsNull();
        await Assert.That(svg).IsNotNull();
    }

    // Block composites nest by recursion, in the grammar and in the layout. Nesting deeper than the stack
    // allows is turned down as a parse error rather than taking the process down.
    [Test]
    public async Task Block_DeepNesting_DoesNotOverflowTheStack()
    {
        const int depth = 1000;
        var input = new StringBuilder("block\n");
        for (var i = 0; i < depth; i++)
        {
            input.Append("block\n");
        }

        input.Append("A\n");
        for (var i = 0; i < depth; i++)
        {
            input.Append("end\n");
        }

        var (svg, failure) = RenderOnSmallStack(input.ToString());

        if (failure is null)
        {
            await Assert.That(svg).IsNotNull();
        }
        else
        {
            await Assert.That(failure).IsTypeOf<MermaidParseException>();
        }
    }

    // Mindmap and treemap nest by indentation, and their layout, drawing and totals recursed into each
    // node's children, as deep as the indentation went.
    [Test]
    [Arguments("mindmap", "n{0}", "")]
    [Arguments("treemap-beta", "\"n{0}\"", ": 1")]
    [Arguments("treeView-beta", "\"n{0}\"", "")]
    public async Task DeepIndentation_DoesNotOverflowTheStack(string header, string node, string leafSuffix)
    {
        const int depth = 1000;
        var input = new StringBuilder(header).Append('\n');
        for (var i = 0; i < depth; i++)
        {
            input.Append(' ', i + 2).AppendFormat(CultureInfo.InvariantCulture, node, i);
            if (i == depth - 1)
            {
                input.Append(leafSuffix);
            }

            input.Append('\n');
        }

        var (svg, failure) = RenderOnSmallStack(input.ToString());

        await Assert.That(failure).IsNull();
        await Assert.That(svg).IsNotNull();
    }

    // A deliberately small stack makes a recursive walk's depth cheap to reach. The inputs are larger than the
    // default limits allow, which would turn them down before any walk began.
    static (string? Svg, Exception? Failure) RenderOnSmallStack(string input)
    {
        string? svg = null;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    svg = Mermaid.Render(input, new() { Limits = RenderLimits.Unlimited });
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            },
            maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();
        return (svg, failure);
    }

    // Each of these was found by RandomInputTests, and threw something other than a MermaidException, or
    // never finished.
    [Test]
    [Arguments("subgraph inside one of the same id", "flowchart TD\n    subgraph Review\n        subgraph Review\n            a\n        end\n    end")]
    [Arguments("swimlane inside one of the same id", "swimlane-beta LR\n    subgraph Review\n        subgraph Review\n            a\n        end\n    end")]
    [Arguments("C4 boundary inside one of the same alias", "C4Context\n    Boundary(b, \"B\") {\n        Boundary(b, \"Again\") {\n            Person(p, \"P\")\n        }\n    }")]
    [Arguments("C4 boundaries inside each other's aliases", "C4Context\n    Boundary(a, \"A\") {\n        Boundary(b, \"B\") {\n            Person(p, \"P\")\n        }\n    }\n    Boundary(b, \"B again\") {\n        Boundary(a, \"A again\") {\n            Person(q, \"Q\")\n        }\n    }")]
    [Arguments("gantt duration in days", "gantt\n    dateFormat YYYY-MM-DD\n    Task :t1, 2024-01-01, 1010101010101010101010d")]
    [Arguments("gantt duration in months", "gantt\n    dateFormat YYYY-MM-DD\n    Task :t1, 2024-01-01, 99999999999M")]
    [Arguments("gantt duration in years", "gantt\n    dateFormat YYYY-MM-DD\n    Task :t1, 2024-01-01, 9999999y")]
    [Arguments("gantt tasks past the year 9999", "gantt\n    dateFormat YYYY-MM-DD\n    A :a, 9990-01-01, 36000d\n    B :b, after a, 36000d")]
    [Arguments("gantt tasks centuries apart", "gantt\n    dateFormat YYYY-MM-DD\n    A :a, 0100-01-01, 1d\n    B :b, 9000-01-01, 1d")]
    public async Task Input_IsTurnedDownWithAReason(string name, string input)
    {
        var exception = await Assert.That(() => Mermaid.Render(input)).Throws<MermaidException>().Because(name);

        await Assert.That(exception!.Message).IsNotEmpty().Because(name);
    }

    // A note or an activation naming a participant nothing had declared left the renderer without a column
    // for it, and it threw. Mermaid adds such a participant, as a message would.
    [Test]
    [Arguments("note", "sequenceDiagram\n    Note left of Alice: alone", "Alice")]
    [Arguments("note over two", "sequenceDiagram\n    A->>B: hi\n    Note over B,C: both", "C")]
    [Arguments("activation", "sequenceDiagram\n    A->>B: hi\n    activate Carol\n    deactivate Carol", "Carol")]
    public async Task Sequence_UndeclaredParticipant_IsAdded(string name, string input, string participant)
    {
        var svg = Mermaid.Render(input);

        XDocument.Parse(svg);
        await Assert.That(svg).Contains($">{participant}<").Because(name);
    }

    // Half a surrogate pair in a use case's JSON note made the JSON reader throw an ArgumentException. Such a
    // note is not JSON the reader can take, and is turned down as any other is.
    [Test]
    public async Task UseCase_JsonNoteWithHalfASurrogatePair_IsTurnedDown() =>
        await Assert.That(() => Mermaid.Render("usecase-beta\n    json J @{ \"a\uD800\": \"b\" }"))
            .Throws<MermaidParseException>();

    // C4 boundaries are laid out by recursion, and C4 does not go through the layout that keeps the limit.
    [Test]
    public async Task C4_DeepBoundaries_AreTurnedDown()
    {
        var input = new StringBuilder("C4Context\n");
        for (var i = 0; i < 5; i++)
        {
            input.Append(CultureInfo.InvariantCulture, $"Boundary(b{i}, \"B{i}\") {{\n");
        }

        input.Append("Person(p, \"P\")\n");
        for (var i = 0; i < 5; i++)
        {
            input.Append("}\n");
        }

        var options = new RenderOptions { Limits = { MaxSubgraphDepth = 4 } };
        await Assert.That(() => Mermaid.Render(input.ToString(), options)).Throws<MermaidLimitException>();
        options.Limits.MaxSubgraphDepth = 5;
        await Assert.That(Mermaid.Render(input.ToString(), options)).Contains("<svg");
    }
}
