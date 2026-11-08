namespace Naiad.Web.Services;

/// <summary>
/// The starter diagrams offered in the editor's sample picker — one per diagram type Naiad supports.
/// Each source is a known-good minimal example, mirroring the rendered fixtures under src/test-renders.
/// </summary>
public static class DiagramSamples
{
    public static IReadOnlyList<DiagramSample> All { get; } =
    [
        new("Flowchart",
            """
            flowchart LR
                A[Start] --> B[Process] --> C[End]
            """),
        new("Sequence",
            """
            sequenceDiagram
                Alice->>Bob: Hello Bob
                Bob-->>Alice: Hi Alice
            """),
        new("Class",
            """
            classDiagram
                class Animal {
                    +String name
                    +int age
                    +makeSound()
                }
                Animal <|-- Dog
                Animal <|-- Cat
            """),
        new("State",
            """
            stateDiagram-v2
                [*] --> Still
                Still --> Moving
                Moving --> Still
                Moving --> Crash
                Crash --> [*]
            """),
        new("Entity Relationship",
            """
            erDiagram
                CUSTOMER ||--o{ ORDER : places
                ORDER ||--|{ LINE-ITEM : contains
                CUSTOMER }|..|{ DELIVERY-ADDRESS : uses
            """),
        new("User Journey",
            """
            journey
                title My Working Day
                section Morning
                    Make coffee: 5: Me
                    Check emails: 3: Me
            """),
        new("Gantt",
            """
            gantt
                title Simple Gantt
                Task A :a1, 2024-01-01, 30d
                Task B :b1, 2024-01-15, 20d
            """),
        new("Pie",
            """
            pie
                "Dogs" : 40
                "Cats" : 30
                "Birds" : 20
                "Fish" : 10
            """),
        new("Quadrant",
            """
            quadrantChart
                title Campaign Analysis
                x-axis Low Reach --> High Reach
                y-axis Low Engagement --> High Engagement
                Campaign A: [0.3, 0.6]
                Campaign B: [0.7, 0.8]
            """),
        new("Requirement",
            """
            requirementDiagram

            requirement test_req {
                id: 1
                text: The system shall do something
                risk: high
                verifymethod: test
            }
            """),
        new("Git Graph",
            """
            gitGraph
                commit
                commit
                branch develop
                commit
                checkout main
                merge develop
            """),
        new("C4 Context",
            """
            C4Context
                title System Context diagram
                Person(user, "User", "A user of the system")
                System(system, "System", "The main system")
                Rel(user, system, "Uses")
            """),
        new("Mindmap",
            """
            mindmap
              Root
                Branch A
                Branch B
                Branch C
            """),
        new("Timeline",
            """
            timeline
                2020 : Event One
                2021 : Event Two
                2022 : Event Three
            """),
        new("Sankey",
            """
            sankey-beta
            A,B,10
            A,C,20
            """),
        new("XY Chart",
            """
            xychart-beta
                title "Monthly Sales"
                x-axis [Jan, Feb, Mar, Apr, May]
                y-axis "Revenue" 0 --> 100
                bar [50, 60, 75, 80, 90]
            """),
        new("Block",
            """
            block-beta
                columns 3
                a["Block A"] b["Block B"] c["Block C"]
            """),
        new("Packet",
            """
            packet-beta
            0-15: "Source Port"
            16-31: "Destination Port"
            """),
        new("Kanban",
            """
            kanban
            todo[Todo]
                task1[First Task]
                task2[Second Task]
            done[Done]
                task3[Completed Task]
            """),
        new("Architecture",
            """
            architecture-beta
            group api(cloud)[API]
                service db(database)[Database] in api
                service server(server)[Server] in api
                db:L -- R:server
            """),
        new("Radar",
            """
            radar-beta
            axis A, B, C, D, E
            curve data1["Series1"]{20, 40, 60, 80, 50}
            """),
        new("Treemap",
            """
            treemap-beta
            "Section A"
                "Item 1": 30
                "Item 2": 20
            "Section B"
                "Item 3": 50
            """),
        new("Ishikawa",
            """
            ishikawa-beta
                Blurry Photo
                Process
                    Out of focus
                    Shutter speed too slow
                User
                    Shaky hands
                Equipment
                    Lens
                        Dirty lens
                        Damaged lens
                Environment
                    Too dark
            """),
    ];

    /// <summary>The diagram shown when the editor first loads.</summary>
    public static DiagramSample Default => All[0];

    /// <summary>Looks up a sample by its display name, or null when none matches.</summary>
    public static DiagramSample? Find(string? name) =>
        All.FirstOrDefault(_ => _.Name == name);
}
