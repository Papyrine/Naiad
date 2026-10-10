public class FlowchartTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            flowchart LR
                A[Start] --> B[Process] --> C[End]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        const string input =
            """
            flowchart TD
                A[Christmas] -->|Get money| B(Go shopping)
                B --> C{Let me think}
                C -->|One| D[Laptop]
                C -->|Two| E[iPhone]
                C -->|Three| F[fa:fa-car Car]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ComplexPipeline()
    {
        // A large request-lifecycle flowchart that stresses the layout: every supported node shape,
        // all edge styles (solid, dotted, thick-less here but bidirectional and dotted), pipe edge
        // labels, nested subgraphs, edges that cross subgraph boundaries, retry cycles
        // (AUTH<->REF and WK->JOB->RT->BO->WK) that Acyclic must break and restore, and classDef/class
        // styling applied across the diagram.
        const string input =
            """
            flowchart TD
                U([Client application]) --> REQ>HTTP request]
                REQ --> CDN{{Edge / CDN}}
                CDN -->|hit| CRES([Cached edge response])
                CDN -->|miss| GW{{API Gateway}}

                subgraph gateway [Gateway and Security]
                    GW --> RL{Rate limit OK?}
                    RL -->|no| E429[[429 Too Many Requests]]
                    RL -->|yes| AUTH{Token valid?}
                    AUTH -->|expired| REF(Refresh token)
                    REF --> AUTH
                    AUTH -->|no| E401[[401 Unauthorized]]
                    AUTH -->|yes| RBAC{Scope allowed?}
                    RBAC -->|no| E403[[403 Forbidden]]
                    RBAC -->|yes| ROUTE[Route to service]
                end

                subgraph app [Application Services]
                    ROUTE --> VAL{Payload valid?}
                    VAL -->|no| E422[[422 Unprocessable]]
                    VAL -->|yes| CHK{Cache hit?}
                    CHK -->|yes| SHAPE(Shape response)
                    CHK -->|no| ORCH[[Request orchestrator]]

                    subgraph resil [Resilience layer]
                        ORCH --> SVCA(Catalog)
                        ORCH --> SVCB(Pricing)
                        ORCH --> SVCC(Inventory)
                        SVCA --> CB{Circuit closed?}
                        SVCB --> CB
                        SVCC --> CB
                        CB -->|open| FALL(Stale or fallback)
                        CB -->|closed| AGG[Aggregate]
                        FALL --> AGG
                    end

                    AGG --> SHAPE
                end

                subgraph data [Data and Cache]
                    SVCA <--> PG[(Postgres)]
                    SVCC <--> PG
                    SVCB <--> RD[(Redis)]
                    CHK -.->|lookup| RD
                    SHAPE -.->|write-through| RD
                    ORCH --> WQ{Mutation?}
                    WQ -->|yes| TX[Begin transaction]
                    TX --> PG
                    TX --> OBX[(Transactional outbox)]
                    WQ -->|no| AGG
                end

                subgraph bg [Background Processing]
                    OBX -.-> MB{{Message broker}}
                    MB --> WK[[Worker pool]]
                    WK --> JOB{Job result?}
                    JOB -->|retryable| RT{Under retry limit?}
                    RT -->|yes| BO(Exponential backoff)
                    BO --> WK
                    RT -->|no| DLQ[(Dead-letter queue)]
                    JOB -->|fatal| DLQ
                    JOB -->|success| NOTE(Dispatch notifications)
                end

                SHAPE --> R200([200 OK])
                R200 --> END(((Request complete)))
                CRES --> END
                NOTE --> END

                subgraph obs [Observability]
                    LOG[(Logs)]
                    MET[(Metrics)]
                    TRC[(Traces)]
                end

                GW -.->|span| TRC
                ORCH -.->|timing| MET
                WK -.->|structured| LOG
                E401 -.->|audit| LOG
                E429 -.->|audit| LOG
                DLQ -.->|alert| MET

                classDef error fill:#fee2e2,stroke:#dc2626,color:#991b1b;
                classDef service fill:#dcfce7,stroke:#16a34a,color:#14532d;
                classDef store fill:#dbeafe,stroke:#2563eb,color:#1e3a8a;
                classDef terminal fill:#ede9fe,stroke:#7c3aed,color:#4c1d95;
                class E429,E401,E403,E422,DLQ error;
                class SVCA,SVCB,SVCC,FALL service;
                class PG,RD,OBX,LOG,MET,TRC store;
                class CRES,R200,END terminal;
            """;

        return VerifySvg(input);
    }

    // The same diagram written with idiomatic Mermaid, now that the parser supports it: inline
    // `-- text -->` / `-. text .->` / `== text ==>` edge labels, `:::class` shorthand, `classDef`/`class`
    // (skipped), `direction` inside a subgraph, a `[/parallelogram/]` node, and a `linkStyle` line (skipped).
    [Test]
    public Task FullFeaturedSyntax()
    {
        const string input =
            """
            flowchart TD
                A([Request]):::entry -- submit --> B{Authenticated?}
                B -- no --> C[[401 Unauthorized]]:::error
                B -- yes --> D[/Validate payload/]
                D == process ==> E(Handler)
                E -. lookup .-> F[(Cache)]

                subgraph worker [Async Worker]
                    direction LR
                    E --> G{Retry?}
                    G -- yes --> E
                    G -- no --> H(((Complete)))
                end

                classDef entry fill:#dbeafe,stroke:#2563eb,stroke-width:2px;
                classDef error fill:#fee2e2,stroke:#dc2626,stroke-width:2px;
                class B,G decision;
                linkStyle default stroke:#94a3b8,stroke-width:1.5px;
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task IconPackIcon()
    {
        const string input =
            """
            flowchart LR
                A[sample:box Storage] --> B[sample:ring Cache]
            """;

        // A registered iconify pack icon (prefix:name) renders inline, like FontAwesome.
        const string pack =
            """
            {
              "prefix": "sample",
              "width": 24,
              "height": 24,
              "icons": {
                "box": {"body": "<rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"3\" fill=\"currentColor\"/>"},
                "ring": {"body": "<circle cx=\"12\" cy=\"12\" r=\"8\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/>"}
              }
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(pack));
        IconPack.Load(stream);

        return VerifySvg(input);
    }

    [Test]
    public async Task FontAwesomeWebfontWithoutPack()
    {
        // With no pack registered a fa: token falls back to the FontAwesome webfont <i>, which only
        // resolves in a browser with the Font Awesome CSS (and so is blank in PNG output).
        var svg = Mermaid.Render(
            """
            flowchart LR
                A[fa:fa-car Car]
            """);

        await Assert.That(svg).Contains("<i class='fa fa-car'>");
    }

    [Test]
    public async Task FontAwesomeResolvesRegisteredPack()
    {
        // A pack registered under the "fa" prefix supplies real geometry, so the token becomes inline
        // SVG (which the PNG rasterizer can draw) instead of the webfont <i>.
        const string pack =
            """
            {"prefix":"fa","width":24,"height":24,"icons":{"fa-car":{"body":"<path d=\"M4 4h16v16H4z\" fill=\"currentColor\"/>"}}}
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(pack));
        IconPack.Load(stream);

        var svg = Mermaid.Render(
            """
            flowchart LR
                A[fa:fa-car Car]
            """);

        await Assert.That(svg).Contains("M4 4h16v16H4z");
    }

    [Test]
    public Task Shapes()
    {
        const string input =
            """
            flowchart TD
                A[Rectangle]
                B(Rounded)
                C{Diamond}
                D((Circle))
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task EdgeLabels()
    {
        const string input =
            """
            flowchart LR
                A --> |Yes| B
                A --> |No| C
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task GraphKeyword()
    {
        const string input =
            """
            graph TD
                A --> B --> C
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task LeadingAndTrailingWhitespace()
    {
        const string input =
            """
            flowchart LR
                A[Start] --> B[Process] --> C[End]
            """;

        var expected = Mermaid.Render(input);
        var actual = Mermaid.Render("\r\n\r\n" + input + "\r\n\r\n");

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public Task Subgraphs()
    {
        const string input =
            """
            flowchart TB
                Start[Start] --> A

                subgraph frontend [Frontend]
                    A[Web UI] --> B[Mobile UI]
                end

                subgraph backend [Backend]
                    C[API] --> D[(Database)]
                end

                A --> C
                B --> C
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task NestedSubgraphs()
    {
        const string input =
            """
            flowchart TB
                User[User] --> A

                subgraph system [Banking System]
                    subgraph api [API Application]
                        A[Controller] --> B[Service]
                    end
                    B --> C[(Database)]
                end
            """;

        return VerifySvg(input);
    }

    // A link may start or end on a subgraph rather than on a node inside it; the edge stops at the box.
    [Test]
    public Task EdgesToSubgraphs()
    {
        const string input =
            """
            flowchart TB
                Client[Client] --> gateway
                subgraph gateway [Gateway]
                    Auth[Auth] --> Route[Router]
                end
                subgraph services [Services]
                    Orders[Orders]
                    Billing[Billing]
                end
                gateway --> services
                services -->|writes| DB[(Database)]
            """;

        return VerifySvg(input);
    }

    // Quoted labels lose their quotes and may hold the characters that would otherwise close the shape;
    // `<br/>` breaks a node or edge label over several lines; and `o--o` / `x--x` carry their circle or
    // cross at both ends.
    [Test]
    public Task QuotedLabelsAndLineBreaks()
    {
        const string input =
            """
            flowchart LR
                A["Text (with) [brackets]"] --> B("Round {x}")
                B --> C{"Is it?"}
                C -->|"quoted pipe"| D[Line one<br/>Line two<br>Line three]
                C -- "quoted inline" --> E(["Stadium<br/>two lines"])
                D o--o F[["Sub"]]
                E x--x F
                F <--> G[(Database<br />server)]
                G -->|first<br/>second| H{{"Hex"}}
            """;

        return VerifySvg(input);
    }
}
