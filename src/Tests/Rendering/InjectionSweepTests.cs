// Every place a diagram takes text that it draws gets the same hostile payload: a <script> element and an
// <img> with an event handler. Whatever path the text takes to the SVG (native <text>, an HTML label in a
// <foreignObject>, an attribute, the stylesheet), it must come out as text: the SVG still parses as XML and
// holds no script element and no event-handler attribute. Each slot's payload carries its own marker
// (q1z, q2z, ...), and every marker must still be in the output, so a slot cannot pass by not being drawn.
// A slot that takes a colour or another style value gets the same payload marked s1z, s2z, ...: anything
// but such a value is dropped (CssValue), so its marker is not looked for, but it must still be inert.
public partial class InjectionSweepTests
{
    [Test]
    [MethodDataSource(nameof(Diagrams))]
    public Task HtmlLabels(string name, string input) =>
        AssertInert(name, input, new());

    [Test]
    [MethodDataSource(nameof(Diagrams))]
    public Task TextLabels(string name, string input) =>
        AssertInert(name, input, new() { AllowHtmlElements = false });

    // Slots drawn only as HTML: without HTML there is no web font to draw an icon class with.
    [Test]
    [MethodDataSource(nameof(HtmlOnlyDiagrams))]
    public Task HtmlOnlyLabels(string name, string input) =>
        AssertInert(name, input, new());

    public static IEnumerable<(string Name, string Input)> HtmlOnlyDiagrams()
    {
        yield return ("Mindmap icon",
            $$"""
            mindmap
                root
                    {{P(1)}} ::icon(fa {{P(2)}})
            """);
    }

    static async Task AssertInert(string name, string input, RenderOptions options)
    {
        var svg = Mermaid.Render(input, options);

        var document = XDocument.Parse(svg);
        var injected = document
            .Descendants()
            .Where(_ => _.Name.LocalName == "script" ||
                        _.Attributes().Any(attribute => attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)))
            .Select(_ => _.ToString(SaveOptions.DisableFormatting))
            .ToList();
        await Assert.That(injected).IsEmpty().Because(name);

        // What the SVG shows or carries: its text and its attribute values, decoded.
        var shown = string.Join(
            '\n',
            document.DescendantNodes().OfType<XText>().Select(_ => _.Value)
                .Concat(document.Descendants().SelectMany(_ => _.Attributes()).Select(_ => _.Value)));
        var missing = MarkerRegex()
            .Matches(input)
            .Select(_ => _.Value)
            .Distinct()
            .Where(_ => !shown.Contains(_, StringComparison.Ordinal))
            .ToList();
        await Assert.That(missing).IsEmpty().Because(name);
    }

    [GeneratedRegex(@"q\d+z")]
    private static partial Regex MarkerRegex();

    // The payload for slot n.
    static string P(int n) => $"<script>q{n}z</script><img src=x onerror=q{n}z>";

    // The payload for a slot that takes a colour or another style value.
    static string S(int n) => $"<script>s{n}z</script><img src=x onerror=s{n}z>";

    public static IEnumerable<(string Name, string Input)> Diagrams()
    {
        static (string, string) Case(string name, string input) => (name, input);

        yield return Case("Flowchart", $$"""
            ---
            title: {{P(1)}}
            ---
            flowchart LR
                A[{{P(2)}}] -->|{{P(3)}}| B({{P(4)}})
                B -- {{P(5)}} --> C{{{P(6)}}}
                C -.->|"{{P(7)}}"| D["`**bold** {{P(8)}}`"]
                D --> E@{ shape: cyl, label: "{{P(9)}}" }
                E -->|"`**bold** {{P(10)}}`"| F(({{P(11)}}))
                F == {{P(12)}} ==> G>{{P(13)}}]
                G --> H[/"{{P(14)}}"/]
                H --> I[({{P(15)}})]
                I --> J([{{P(16)}}])
                J --> K[[{{P(17)}}]]
                subgraph S1 [{{P(18)}}]
                    L((({{P(19)}})))
                end
                subgraph {{P(20)}}
                    M["{{P(21)}}"]
                end
                subgraph S3 ["`**bold** {{P(22)}}`"]
                    N
                end
                style N fill:{{S(23)}}
                classDef hot stroke:{{S(24)}}
                class M hot
                linkStyle 0 stroke:{{S(25)}}
                N -. {{P(26)}} .-> O["first<br/>{{P(27)}}"]
                O -->|first<br/>{{P(28)}}| A
            """);
        yield return Case("Block", $$"""
            ---
            title: {{P(1)}}
            ---
            block-beta
                columns 3
                A["{{P(2)}}"] B({{P(3)}}) C["`**bold** {{P(4)}}`"]
                D<["{{P(5)}}"]>(right) E{{{P(6)}}} F(({{P(7)}}))
                block:G["{{P(8)}}"]
                    H[{{P(9)}}]
                end
                block:I["`**bold** {{P(10)}}`"]
                    J
                end
                A -- "{{P(11)}}" --> B
                B -- "`**bold** {{P(12)}}`" --> C
                style J fill:{{S(13)}}
                classDef hot stroke:{{S(14)}}
                class H hot
            """);
        yield return Case("AgentFlow", $$"""
            ---
            title: {{P(1)}}
            ---
            agentflow-beta TB
                connector gh["{{P(2)}}"]
                a["{{P(3)}}"]@{ shape: task } -- {{P(4)}} --> b["`**bold** {{P(5)}}`"]
                b -->|{{P(6)}}| c@{ shape: tool, label: "{{P(7)}}" }
                c --x|"`**bold** {{P(8)}}`"| d({{P(9)}})
                d -.- gh
                flow f1["{{P(10)}}"]
                    e["{{P(11)}}"]
                end
                flow f2["`**bold** {{P(12)}}`"]
                    g{{{P(13)}}}
                end
                flow f3["{{P(14)}}"]
                    h
                end
                f3@{ view: collapsed }
                e --> g
                style g fill:{{S(15)}}
                classDef hot stroke:{{S(16)}}
                class e hot
            """);
        yield return Case("Swimlane", $$"""
            ---
            title: {{P(1)}}
            ---
            swimlane-beta LR
                subgraph L1 [{{P(2)}}]
                    a[{{P(3)}}] -->|{{P(4)}}| b["`**bold** {{P(5)}}`"]
                    subgraph N1 [{{P(6)}}]
                        c({{P(7)}})
                    end
                end
                subgraph {{P(8)}}
                    d{{{P(9)}}}
                end
                subgraph L3 ["`**bold** {{P(10)}}`"]
                    e@{ shape: cyl, label: "{{P(11)}}" }
                end
                b -- {{P(12)}} --> d
                d -->|"`**bold** {{P(13)}}`"| e
                c --> a
                style e fill:{{S(14)}}
            """);
        yield return Case("Class", $$"""
            ---
            title: {{P(1)}}
            ---
            classDiagram
                class A["{{P(2)}}"] {
                    +{{P(3)}}
                    +{{P(4)}}({{P(5)}}) {{P(6)}}
                }
                class `{{P(7)}}`
                class B~{{P(8)}}~
                A "{{P(9)}}" --> "{{P(10)}}" B : {{P(11)}}
                A : +{{P(12)}}
                note "{{P(13)}}"
                note for A "{{P(14)}}"
                namespace NS {
                    class C["{{P(15)}}"]
                }
                class D <<<script q16z<img src=x onerror=q16z>>
            """);
        yield return Case("State", $$"""
            ---
            title: {{P(1)}}
            ---
            stateDiagram-v2
                state "{{P(2)}}" as A
                B : {{P(3)}}
                [*] --> A
                A --> B : {{P(4)}}
                note right of A : {{P(5)}}
                note left of B
                    {{P(6)}}
                end note
                state "{{P(7)}}" as C {
                    D : {{P(8)}}
                    [*] --> D
                }
                B --> C
            """);
        yield return Case("EntityRelationship", $$"""
            ---
            title: {{P(1)}}
            ---
            erDiagram
                "{{P(2)}}" ||--o{ ORDER : {{P(3)}}
                ORDER["{{P(4)}}"] {
                    string id PK "{{P(5)}}"
                }
                ITEM["**bold** {{P(6)}}"]
                ORDER }|..|{ ITEM : "{{P(7)}}"
            """);
        yield return Case("Requirement", $$"""
            ---
            title: {{P(1)}}
            ---
            requirementDiagram
                requirement "{{P(2)}}" {
                    id: {{P(3)}}
                    text: {{P(4)}}
                    risk: high
                    verifymethod: test
                }
                element "{{P(5)}}" {
                    type: {{P(6)}}
                    docRef: {{P(7)}}
                }
                "{{P(5)}}" - satisfies -> "{{P(2)}}"
            """);
        yield return Case("C4", $$"""
            C4Container
                title {{P(1)}}
                Person(user, "{{P(2)}}", "{{P(3)}}")
                System_Ext(ext, "{{P(4)}}", "{{P(5)}}")
                Enterprise_Boundary(eb, "{{P(6)}}") {
                    System_Boundary(sb, "{{P(7)}}") {
                        Container(api, "{{P(8)}}", "{{P(9)}}", "{{P(10)}}")
                        ContainerDb(db, "{{P(11)}}", $techn="{{P(12)}}", $descr="{{P(13)}}")
                    }
                    Boundary(b1, "{{P(14)}}", "{{P(15)}}") {
                        Component(cmp, "{{P(16)}}", "{{P(17)}}", "{{P(18)}}")
                    }
                }
                Rel(user, api, "{{P(19)}}", "{{P(20)}}")
                BiRel(api, db, "{{P(21)}}")
                Rel_U(cmp, ext, "{{P(22)}}", $techn="{{P(23)}}")
            """);
        yield return Case("C4Deployment", $$"""
            C4Deployment
                title {{P(1)}}
                Deployment_Node(dn, "{{P(2)}}", "{{P(3)}}") {
                    Node(n1, "{{P(4)}}", $type="{{P(5)}}") {
                        ContainerQueue(q, "{{P(6)}}", "{{P(7)}}", "{{P(8)}}")
                    }
                }
                SystemDb(sdb, "{{P(9)}}", "{{P(10)}}")
                RelIndex(1, q, sdb, "{{P(11)}}", "{{P(12)}}")
            """);
        yield return Case("Architecture", $$"""
            architecture-beta
                group g1(cloud)[{{P(1)}}]
                group g2(database)[{{P(2)}}] in g1
                service s1(server)[{{P(3)}}] in g1
                service s2(disk)[{{P(4)}}] in g2
                s1:R -[{{P(5)}}]- L:s2
            """);
        yield return Case("UseCase", $$"""
            ---
            title: {{P(1)}}
            ---
            usecase-beta
                actor A("{{P(2)}}")
                actor "{{P(3)}}"
                actor B("`**bold** {{P(4)}}`")
                actor C("{{P(5)}}") <<<script q6z<img src=x onerror=q6z>>
                systemBoundary S1("{{P(7)}}")
                    U1("{{P(8)}}")
                    U2["{{P(9)}}"]
                    U3("`**bold** {{P(10)}}`") <<<script q11z<img src=x onerror=q11z>>
                end
                systemBoundary "{{P(12)}}"
                    U4
                end
                systemBoundary S3["`**bold** {{P(13)}}`"]
                    U5
                end
                "{{P(14)}}"
                A -- {{P(15)}} --> U1
                B -- "`**bold** {{P(16)}}`" --> U2
                C -- "{{P(17)}}" --> U3
                note for U1 "{{P(18)}}"
                note for U2 "`**bold** {{P(19)}}`"
                json J @{ "{{P(20)}}": "{{P(21)}}" }
                style U4 fill:{{S(22)}}
                U5 <-- {{P(23)}} -- A
            """);
        yield return Case("EventModeling", $$"""
            eventmodeling
                title {{P(1)}}
                tf 01 ui Cart {{{P(2)}}}
                tf 02 cmd Add "{{P(3)}}"
                tf 03 evt Added [[D1]]
                data D1 {
                  {{P(4)}}
                }
            """);

        yield return Case("Sequence", $$"""
            sequenceDiagram
                title {{P(1)}}
                box Aqua {{P(2)}}
                participant A as {{P(3)}}
                actor B as {{P(4)}}
                end
                box {{P(5)}}
                participant C as {{P(6)}}
                end
                A->>B: {{P(7)}}
                B-->>A: {{P(8)}}
                A->>A: {{P(9)}}
                Note right of A: {{P(10)}}
                Note over A,B: {{P(11)}}
                Note left of B: {{P(12)}}
                loop {{P(13)}}
                    A->>C: {{P(14)}}
                end
                alt {{P(15)}}
                    A->>B: x
                else {{P(16)}}
                    B->>A: x
                end
                opt {{P(17)}}
                    A->>B: x
                end
                par {{P(18)}}
                    A->>B: x
                and {{P(19)}}
                    B->>C: x
                end
                critical {{P(20)}}
                    A->>B: x
                option {{P(21)}}
                    B->>A: x
                end
                break {{P(22)}}
                    A->>B: x
                end
                rect {{S(23)}}
                    A->>B: x
                end
                create participant D as {{P(24)}}
                A->>D: {{P(25)}}
            """);
        yield return Case("ZenUML", $$"""
            zenuml
                title {{P(1)}}
                @Starter("{{P(2)}}")
                @Actor A as {{P(3)}}
                B as "{{P(4)}}"
                "{{P(5)}}"
                // {{P(6)}}
                A->B: {{P(7)}}
                A->"{{P(5)}}": {{P(8)}}
                A.method({{P(9)}}) {
                    return {{P(10)}}
                }
                "{{P(11)}}".call()
                new "{{P(12)}}"
                obj = new "{{P(13)}}"()
                if({{P(14)}}) {
                    A.x()
                } else if({{P(15)}}) {
                    B.x()
                } else {
                    A.x()
                }
                while({{P(16)}}) {
                    A.x()
                }
                opt({{P(17)}}) {
                    B.x()
                }
                par({{P(18)}}) {
                    A.x()
                }
                critical({{P(19)}}) {
                    B.x()
                }
                try {
                    A.x()
                } catch({{P(20)}}) {
                    B.x()
                } finally {
                    A.x()
                }
            """);
        yield return Case("Gantt", $$"""
            gantt
                title {{P(1)}}
                dateFormat YYYY-MM-DD
                section {{P(2)}}
                {{P(3)}} :a1, 2024-01-01, 3d
                {{P(4)}} :milestone, m1, 2024-01-04, 0d
                {{P(5)}} :done, after a1, 2d
                section {{P(6)}}
                {{P(7)}} :crit, active, 2024-01-02, 2d
            """);
        yield return Case("GanttCompact", $$"""
            ---
            title: {{P(1)}}
            displayMode: compact
            ---
            gantt
                dateFormat YYYY-MM-DD
                section {{P(2)}}
                {{P(3)}} :a1, 2024-01-01, 3d
                {{P(4)}} :a2, 2024-01-05, 2d
                {{P(5)}} :milestone, m1, 2024-01-08, 0d
            """);
        yield return Case("Timeline", $$"""
            timeline
                title {{P(1)}}
                section {{P(2)}}
                    {{P(3)}} : {{P(4)}} : {{P(5)}}
                             : {{P(6)}}
                section {{P(7)}}
                    {{P(8)}} : {{P(9)}}
            """);
        yield return Case("UserJourney", $$"""
            journey
                title {{P(1)}}
                section {{P(2)}}
                    {{P(3)}}: 5: {{P(4)}}, {{P(5)}}
                    {{P(6)}}: 3: {{P(4)}}
                section {{P(7)}}
                    {{P(8)}}: 1
            """);
        yield return Case("GitGraph", $$"""
            ---
            title: {{P(1)}}
            config:
              gitGraph:
                mainBranchName: {{P(2)}}
            ---
            gitGraph
                commit id: "{{P(3)}}" tag: "{{P(4)}}"
                branch develop
                checkout develop
                commit id: '{{P(5)}}' type: HIGHLIGHT
                commit tag: "{{P(6)}}"
                branch feature
                checkout feature
                commit id: "{{P(7)}}"
                checkout develop
                merge feature id: "{{P(8)}}" tag: "{{P(9)}}"
                cherry-pick id: "{{P(3)}}" tag: "{{P(10)}}"
            """);
        yield return Case("GitGraphVertical", $$"""
            %%{init: {"gitGraph": {"mainBranchName": "{{P(1)}}", "rotateCommitLabel": false} } }%%
            gitGraph TB:
                commit id: "{{P(2)}}" tag: "{{P(3)}}"
                branch develop
                checkout develop
                commit id: "{{P(4)}}"
                branch feature
                checkout feature
                commit
                checkout develop
                merge feature id: "{{P(5)}}" tag: "{{P(6)}}"
            """);
        yield return Case("Kanban", $$"""
            ---
            title: {{P(1)}}
            ---
            kanban
                col1[{{P(2)}}]
                    t1[{{P(3)}}]@{ assigned: '{{P(4)}}', ticket: {{P(5)}}, priority: 'High' }
                    {{P(6)}}
                {{P(7)}}
                    t2[{{P(8)}}]@{ assigned: "{{P(9)}}" }
            """);
        yield return Case("Pie", $$"""
            pie showData title {{P(1)}}
                "{{P(2)}}" : 40
                '{{P(3)}}' : 30
            """);
        yield return Case("Quadrant", $$"""
            quadrantChart
                title {{P(1)}}
                x-axis {{P(2)}} --> {{P(3)}}
                y-axis "{{P(4)}}" --> "{{P(5)}}"
                quadrant-1 {{P(6)}}
                quadrant-2 {{P(7)}}
                quadrant-3 {{P(8)}}
                quadrant-4 {{P(9)}}
                {{P(10)}}: [0.3, 0.6]
                {{P(11)}}: [0.7, 0.2]
            """);
        yield return Case("XYChart", $$"""
            ---
            config:
              themeVariables:
                xyChart:
                  plotColorPalette: {{S(8)}}
            ---
            xychart-beta
                title "{{P(1)}}"
                x-axis "{{P(2)}}" [{{P(3)}}, "{{P(4)}}"]
                y-axis "{{P(5)}}" 0 --> 100
                bar "{{P(6)}}" [10, 20]
                line "{{P(7)}}" [30, 40]
            """);
        yield return Case("XYChartHorizontal", $$"""
            xychart-beta horizontal
                title {{P(1)}}
                x-axis "{{P(2)}}" [{{P(3)}}, {{P(4)}}]
                y-axis "{{P(5)}}" 0 --> 100
                bar "{{P(6)}}" [10, 20]
                line "{{P(7)}}" [30, 40]
            """);
        yield return Case("Radar", $$"""
            radar-beta
                title {{P(1)}}
                axis a["{{P(2)}}"], b[{{P(3)}}], c
                curve x["{{P(4)}}"]{1, 2, 3}, y[{{P(5)}}]{3, 2, 1}
            """);

        yield return Case("Mindmap", $$$"""
            mindmap
                root(({{{P(1)}}}))
                    {{{P(2)}}}
                        a[{{{P(3)}}}]
                            ({{{P(4)}}})
                            {{{{{P(5)}}}}}
                            )){{{P(6)}}}((
                            ){{{P(7)}}}(
                            b)){{{P(8)}}}((
                        "`**bold** {{{P(9)}}}`"
                    {{{P(10)}}} ::icon(fa fa-book)
                    {{{P(11)}}} :::urgent large
            """);

        yield return Case("Treemap", $$"""
            treemap-beta
            "{{P(1)}}"
                "{{P(2)}}": 20
                "{{P(3)}}"
                    "{{P(4)}}": 5
                    "{{P(5)}}": 5
            "{{P(6)}}": 30
            """);

        yield return Case("Treemap front matter title", $$"""
            ---
            title: {{P(1)}}
            ---
            treemap-beta
            "{{P(2)}}": 1
            """);

        yield return Case("TreeView", $$"""
            treeView-beta
                title {{P(1)}}
                {{P(2)}}/ ## {{P(3)}}
                    {{P(4)}} ## {{P(5)}}
                    "{{P(6)}}"
                        "{{P(7)}}/" ## {{P(8)}}
                            {{P(9)}}
            """);

        yield return Case("Sankey", $$"""
            ---
            title: {{P(1)}}
            config:
              sankey:
                prefix: {{P(2)}}
                suffix: {{P(3)}}
                nodeColors:
                  {{P(4)}}: {{S(5)}}
            ---
            sankey-beta
            {{P(4)}},{{P(6)}},10
            "{{P(7)}}","{{P(8)}}",5
            {{P(6)}},"{{P(9)}} ""q"" x",3
            """);

        yield return Case("Packet", $$"""
            packet-beta
                title {{P(1)}}
                0-15: "{{P(2)}}"
                16-31: {{P(3)}}
                32: "{{P(4)}}"
                +7: {{P(5)}}
                40-95: "{{P(6)}}"
            """);

        yield return Case("Venn", $$"""
            venn-beta
                title {{P(1)}}
                set "{{P(2)}}"
                set B["{{P(3)}}"]
                set C[{{P(4)}}]
                union "{{P(2)}}",B["{{P(5)}}"]
                union B,C[{{P(6)}}]
                text "{{P(7)}}"
                text B t1["{{P(8)}}"]
                text "{{P(2)}}" "{{P(9)}}"
                style B fill:"{{S(10)}}", color:"{{S(11)}}"
                style "{{P(2)}}" stroke:"{{S(12)}}", stroke-width:"{{S(13)}}", fill-opacity:"{{S(14)}}"
                style B,C fill:"{{S(15)}}", color:"{{S(16)}}"
                style t1 color:"{{S(17)}}"
            """);

        yield return Case("Wardley", $$"""
            wardley-beta
                title {{P(1)}}
                evolution "{{P(2)}}" -> "{{P(3)}}" / '{{P(4)}}' -> "{{P(5)}}" -> "{{P(6)}}"
                anchor "{{P(7)}}" [0.95, 0.6]
                component "{{P(8)}}" [0.8, 0.5]
                component "{{P(9)}}" [0.6, 0.3] label [10, -10] (build)
                component "{{P(10)}}" [0.4, 0.7] (market) inertia
                pipeline "{{P(10)}}" {
                    component "{{P(11)}}" [0.6]
                    component '{{P(12)}}' [0.8]
                }
                "{{P(7)}}" -> "{{P(8)}}"
                "{{P(8)}}" +'{{P(13)}}'> "{{P(9)}}"
                "{{P(9)}}" -.-> "{{P(10)}}" ; {{P(14)}}
                note "{{P(15)}}" [0.2, 0.2]
                annotations [0.9, 0.05]
                annotation 1, [0.5, 0.5] "{{P(16)}}"
                accelerator "{{P(17)}}" [0.3, 0.5]
                deaccelerator "{{P(18)}}" [0.2, 0.7]
                evolve "{{P(8)}}" 0.9
            """);

        yield return Case("Ishikawa", $$"""
            ishikawa-beta
                {{P(1)}}
                {{P(2)}}
                    {{P(3)}}
                        {{P(4)}}
                            {{P(5)}}
                {{P(6)}}
                    {{P(7)}}
            """);

        yield return Case("Cynefin", $$"""
            cynefin-beta
                title {{P(1)}}
                complex "{{P(2)}}" '{{P(3)}}'
                    "{{P(4)}}"
                complicated
                    "{{P(5)}}"
                clear "{{P(6)}}"
                chaotic "{{P(7)}}"
                confusion "{{P(8)}}" "{{P(9)}}" "{{P(10)}}"
                complex --> complicated : "{{P(11)}}"
                clear --> chaotic : '{{P(12)}}'
            """);

        yield return Case("Railroad", $$"""
            railroad-beta
                title {{P(1)}}
                rule = sequence(terminal("{{P(2)}}"), nonterminal("{{P(3)}}"), special('{{P(4)}}'), choice(terminal("{{P(5)}}"), optional(nonterminal("{{P(6)}}"))), zeroOrMore(terminal("{{P(7)}}")), oneOrMore(special("{{P(8)}}")));
            """);

        yield return Case("Railroad EBNF", $$"""
            railroad-ebnf-beta
                title "{{P(1)}}"
                rule ::= "{{P(2)}}" ? {{P(3)}} ? | [ '{{P(4)}}' ] , { "{{P(5)}}" }+ | other - "{{P(6)}}" ;
            """);

        yield return Case("Railroad ABNF", $$"""
            railroad-abnf-beta
                title {{P(1)}}
                rule = "{{P(2)}}" / 1*3"{{P(3)}}" / [ "{{P(4)}}" ] other ;
            """);

        yield return Case("Railroad PEG", $$"""
            railroad-peg-beta
                title {{P(1)}}
                Rule <- "{{P(2)}}" / &'{{P(3)}}' !"{{P(4)}}" ('{{P(5)}}')* ;
            """);
    }
}
