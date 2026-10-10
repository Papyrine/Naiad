public class MermaidTests
{
    [Test]
    public async Task TryDetectType_KnownKeywords_ResolveToType()
    {
        await Assert.That(Detect("flowchart LR\n    A --> B")).IsEqualTo(DiagramType.Flowchart);
        await Assert.That(Detect("graph TD\n    A --> B")).IsEqualTo(DiagramType.Flowchart);
        await Assert.That(Detect("sequenceDiagram\n    A->>B: hi")).IsEqualTo(DiagramType.Sequence);
        // -v2 / -beta suffixes ride on the same StartsWith prefix the renderer matches.
        await Assert.That(Detect("stateDiagram-v2\n    [*] --> Idle")).IsEqualTo(DiagramType.State);
        await Assert.That(Detect("sankey-beta\n    A,B,1")).IsEqualTo(DiagramType.Sankey);

        static DiagramType Detect(string input)
        {
            if (Mermaid.TryDetectType(input, out var type))
            {
                return type;
            }

            throw new($"No diagram type detected for: {input}");
        }
    }

    [Test]
    public async Task TryDetectType_SkipsLeadingInitBlock()
    {
        var detected = Mermaid.TryDetectType(
            """
            %%{init: {"theme": "dark"}}%%
            flowchart TD
                A --> B
            """,
            out var type);

        await Assert.That(detected).IsTrue();
        await Assert.That(type).IsEqualTo(DiagramType.Flowchart);
    }

    [Test]
    public async Task TryDetectType_EmptyOrUnknown_ReturnsFalse()
    {
        await Assert.That(Mermaid.TryDetectType(null, out _)).IsFalse();
        await Assert.That(Mermaid.TryDetectType("   ", out _)).IsFalse();
        await Assert.That(Mermaid.TryDetectType("notADiagram foo", out _)).IsFalse();
    }

    const string plainFlowchart =
        """
        flowchart TD
            A[Alpha] --> B[Beta]
        """;

    // Each of these used to fail with "Unknown diagram type", because whatever came first was taken for
    // the diagram's opening keyword.
    [Test]
    [Arguments(
        "comment",
        """
        %% a comment
        flowchart TD
            A[Alpha] --> B[Beta]
        """)]
    [Arguments(
        "several comments and blank lines",
        """
        %% first

        %% second
        flowchart TD
            A[Alpha] --> B[Beta]
        """)]
    [Arguments(
        "comment then init block",
        """
        %% a comment
        %%{init: {"theme": "dark"}}%%
        flowchart TD
            A[Alpha] --> B[Beta]
        """)]
    [Arguments(
        "init block then comment",
        """
        %%{init: {"theme": "dark"}}%%
        %% a comment
        flowchart TD
            A[Alpha] --> B[Beta]
        """)]
    [Arguments(
        "front matter",
        """
        ---
        config:
          theme: dark
        ---
        flowchart TD
            A[Alpha] --> B[Beta]
        """)]
    [Arguments(
        "front matter then comment",
        """
        ---
        config:
          theme: dark
        ---
        %% a comment
        flowchart TD
            A[Alpha] --> B[Beta]
        """)]
    public async Task Preamble_IsSkipped(string name, string input)
    {
        await Assert.That(Mermaid.Render(input)).IsEqualTo(Mermaid.Render(plainFlowchart)).Because(name);

        await Assert.That(Mermaid.TryDetectType(input, out var type)).IsTrue().Because(name);
        await Assert.That(type).IsEqualTo(DiagramType.Flowchart).Because(name);
    }

    [Test]
    public async Task Preamble_WithWindowsLineEndings_IsSkipped()
    {
        const string input = "---\r\ntitle: Pets\r\n---\r\n%% a comment\r\npie\r\n    \"Dogs\" : 3\r\n    \"Cats\" : 1\r\n";

        var svg = Mermaid.Render(input);

        await Assert.That(svg).Contains(">Pets<");
        await Assert.That(svg).Contains("Dogs");
    }

    [Test]
    [Arguments("title: Pets")]
    [Arguments("title: \"Pets\"")]
    [Arguments("title: 'Pets'")]
    [Arguments("title:    Pets   ")]
    public async Task FrontMatterTitle_TitlesTheDiagram(string titleLine)
    {
        var input = $"---\n{titleLine}\nconfig:\n  title: not this one\n---\npie\n    \"Dogs\" : 3\n    \"Cats\" : 1";

        var svg = Mermaid.Render(input);

        await Assert.That(svg).Contains(">Pets<");
        await Assert.That(svg).DoesNotContain("not this one");
    }

    [Test]
    public async Task FrontMatterTitle_YieldsToTheDiagramsOwnTitle()
    {
        const string input =
            """
            ---
            title: From front matter
            ---
            pie title From the diagram
                "Dogs" : 3
                "Cats" : 1
            """;

        var svg = Mermaid.Render(input);

        await Assert.That(svg).Contains("From the diagram");
        await Assert.That(svg).DoesNotContain("From front matter");
    }

    [Test]
    public async Task UnclosedFrontMatter_IsNotADiagram()
    {
        const string input =
            """
            ---
            title: Pets
            pie
                "Dogs" : 3
            """;

        await Assert.That(() => Mermaid.Render(input)).Throws<MermaidException>();
        await Assert.That(Mermaid.TryDetectType(input, out _)).IsFalse();
    }

    [Test]
    public async Task OnlyAComment_IsNotADiagram()
    {
        await Assert.That(() => Mermaid.Render("%% nothing here")).Throws<MermaidException>();
        await Assert.That(Mermaid.TryDetectType("%% nothing here", out _)).IsFalse();
    }

    // Mermaid introduced these diagrams with a `-beta` keyword and accepts them without it too. The plain
    // spelling used to be rejected by the parser although the type was detected from it.
    [Test]
    [Arguments(
        "packet",
        """
        packet-beta
        0-15: "Source Port"
        16-31: "Destination Port"
        """)]
    [Arguments(
        "block",
        """
        block-beta
            columns 3
            a["Block A"] b["Block B"] c["Block C"]
        """)]
    [Arguments(
        "sankey",
        """
        sankey-beta
        A,B,10
        A,C,20
        """)]
    [Arguments(
        "xychart",
        """
        xychart-beta
            title "Monthly Sales"
            x-axis [Jan, Feb, Mar, Apr, May]
            y-axis "Revenue" 0 --> 100
            bar [50, 60, 75, 80, 90]
        """)]
    [Arguments(
        "treemap",
        """
        treemap-beta
        "Alpha": 40
        "Beta": 30
        """)]
    [Arguments(
        "radar",
        """
        radar-beta
        axis A, B, C, D, E
        curve data1["Series1"]{20, 40, 60, 80, 50}
        """)]
    [Arguments(
        "architecture",
        """
        architecture-beta
        service db(database)[Database]
        """)]
    public async Task BetaKeyword_IsAcceptedWithAndWithoutTheSuffix(string keyword, string input)
    {
        var withSuffix = Mermaid.Render(input);
        var plain = Mermaid.Render(input.Replace($"{keyword}-beta", keyword));
        var upperCase = Mermaid.Render(input.Replace($"{keyword}-beta", keyword.ToUpperInvariant()));

        await Assert.That(plain).IsEqualTo(withSuffix).Because(keyword);
        await Assert.That(upperCase).IsEqualTo(withSuffix).Because(keyword);
    }

    [Test]
    public async Task BetaKeyword_FollowedByAnythingElse_FailsTheParse() =>
        await Assert.That(() => Mermaid.Render("packet-gamma\n0-15: \"Source Port\""))
            .Throws<MermaidParseException>();
}
