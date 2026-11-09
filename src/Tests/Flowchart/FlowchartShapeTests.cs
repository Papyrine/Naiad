// Mermaid's expanded node shapes (`A@{ shape: ... }`), each drawn as Mermaid 12.3 draws it.
public class FlowchartShapeTests : TestBase
{
    // Every labelled shape from Mermaid's shape table, in columns of related shapes.
    [Test]
    public Task ExpandedShapes()
    {
        const string input =
            """
            flowchart TD
                r1@{ shape: rect, label: "Process" } --> r2@{ shape: rounded, label: "Event" }
                r2 --> r3@{ shape: stadium, label: "Terminal" } --> r4@{ shape: fr-rect, label: "Subprocess" }
                r4 --> r5@{ shape: lin-rect, label: "Lined process" } --> r6@{ shape: div-rect, label: "Divided" }
                r6 --> r7@{ shape: notch-rect, label: "Card" } --> r8@{ shape: tag-rect, label: "Tagged process" }
                r8 --> r9@{ shape: st-rect, label: "Processes" }

                d1@{ shape: doc, label: "Document" } --> d2@{ shape: lin-doc, label: "Lined document" }
                d2 --> d3@{ shape: tag-doc, label: "Tagged document" } --> d4@{ shape: docs, label: "Documents" }
                d4 --> d5@{ shape: flag, label: "Paper tape" } --> d6@{ shape: win-pane, label: "Internal storage" }
                d6 --> d7@{ shape: datastore, label: "Datastore" } --> d8@{ shape: text, label: "Text block" }

                c1@{ shape: cyl, label: "Database" } --> c2@{ shape: lin-cyl, label: "Disk storage" }
                c2 --> c3@{ shape: h-cyl, label: "Direct access" } --> c4@{ shape: bow-rect, label: "Stored data" }
                c4 --> c5@{ shape: delay, label: "Delay" } --> c6@{ shape: curv-trap, label: "Display" }
                c6 --> c7@{ shape: notch-pent, label: "Loop limit" } --> c8@{ shape: sl-rect, label: "Manual input" }

                p1@{ shape: diam, label: "Decision" } --> p2@{ shape: hex, label: "Prepare" }
                p2 --> p3@{ shape: lean-r, label: "In out" } --> p4@{ shape: lean-l, label: "Out in" }
                p4 --> p5@{ shape: trap-b, label: "Priority" } --> p6@{ shape: trap-t, label: "Manual" }
                p6 --> p7@{ shape: tri, label: "Extract" } --> p8@{ shape: flip-tri, label: "Manual file" }
                p8 --> p9@{ shape: odd, label: "Odd" }

                b1@{ shape: brace, label: "Comment" } --> b2@{ shape: brace-r, label: "Comment right" }
                b2 --> b3@{ shape: braces, label: "Braces" } --> b4@{ shape: circle, label: "Start" }
                b4 --> b5@{ shape: dbl-circ, label: "Stop" } --> b6@{ shape: folder, label: "Folder" }
                b6 --> b7@{ shape: bucket, label: "Bucket" }

                w1@{ shape: console, label: "Console" } --> w2@{ shape: browser, label: "Browser" }
                w2 --> w3@{ shape: person, label: "Person" } --> w4@{ shape: cloud, label: "Cloud" }
                w4 --> w5@{ shape: bang, label: "Bang" }
            """;

        return VerifySvg(input);
    }

    // The unlabelled markers take Mermaid's fixed sizes and drop their labels; edges stop on the circles.
    [Test]
    public Task Markers()
    {
        const string input =
            """
            flowchart TD
                start@{ shape: sm-circ } --> fork@{ shape: fork, label: "Fork" }
                fork --> a[Left] & b[Right]
                a --> join@{ shape: join }
                b --> sum@{ shape: cross-circ, label: "Summary" }
                sum --> join
                join --> bolt@{ shape: bolt } --> collate@{ shape: hourglass, label: "Collate" }
                collate --> junction@{ shape: f-circ } --> stop@{ shape: fr-circ }
                junction --> c[Elsewhere]
            """;

        return VerifySvg(input);
    }

    // A fork lies across the flow, so it stands upright when the flow runs left to right.
    [Test]
    public Task ForkAcrossHorizontalFlow()
    {
        const string input =
            """
            flowchart LR
                start@{ shape: start } --> fork@{ shape: fork }
                fork --> a[One] & b[Two]
                a & b --> join@{ shape: join } --> stop@{ shape: stop }
            """;

        return VerifySvg(input);
    }

    // Long labels stay inside the slanted sides of parallelograms and trapezoids, and edges coming in at an
    // angle stop on the slanted sides rather than on the box around them.
    [Test]
    public Task SlantedShapesHoldTheirLabels()
    {
        const string input =
            """
            flowchart TD
                a[/A parallelogram with a long label/] --> b[\Leaning the other way with a long label\]
                a --> c[/A trapezoid holding a long label\]
                a --> d[\An inverted trapezoid holding a long label/]
                b --> e@{ shape: tri, label: "Extract the data" }
                c --> e
                d --> f@{ shape: flip-tri, label: "Manual file" }
                e --> f
            """;

        return VerifySvg(input);
    }

    // The example from Mermaid's flowchart docs ("Example Flowchart with New Shapes").
    [Test]
    public Task MermaidDocsExample()
    {
        const string input =
            """
            flowchart RL
                A@{ shape: manual-file, label: "File Handling"}
                B@{ shape: manual-input, label: "User Input"}
                C@{ shape: docs, label: "Multiple Documents"}
                D@{ shape: procs, label: "Process Automation"}
                E@{ shape: paper-tape, label: "Paper Records"}
            """;

        return VerifySvg(input);
    }

    // A style reaches every part of a shape: the brace keeps its backing borderless, the junction takes the
    // fill, and a styled text block shows its box.
    [Test]
    public Task StyledShapes()
    {
        const string input =
            """
            flowchart LR
                a@{ shape: braces, label: "Styled braces" } --> b@{ shape: f-circ }
                b --> c@{ shape: text, label: "Boxed text" } --> d@{ shape: docs, label: "Styled stack" }
                style a fill:#fde68a,stroke:#b45309
                style b fill:#16a34a,stroke:#14532d
                style c fill:#e0f2fe,stroke:#0369a1
                style d fill:#fee2e2,stroke:#dc2626,stroke-width:2px
            """;

        return VerifySvg(input);
    }

    // Multi-line labels grow the shapes that size around them.
    [Test]
    public Task MultiLineLabels()
    {
        const string input =
            """
            flowchart LR
                a@{ shape: lin-doc, label: "First line<br/>second line" } --> b@{ shape: curv-trap, label: "One<br/>two<br/>three" }
                b --> c@{ shape: tri, label: "Up<br/>and up" } --> d@{ shape: cloud, label: "A cloud<br/>of two lines" }
                d --> e@{ shape: brace, label: "A note<br/>over two lines" }
            """;

        return VerifySvg(input);
    }
}
