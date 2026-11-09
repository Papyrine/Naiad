using Naiad.Diagrams.Wardley;

public class WardleyTests : TestBase
{
    // The basic example from Mermaid's documentation.
    [Test]
    public Task TeaShop()
    {
        const string input =
            """
            wardley-beta
            title Tea Shop Value Chain

            anchor Business [0.95, 0.63]
            component Cup of Tea [0.79, 0.61]
            component Tea [0.63, 0.81]
            component Hot Water [0.52, 0.80]
            component Kettle [0.43, 0.35]
            component Power [0.10, 0.70]

            Business -> Cup of Tea
            Cup of Tea -> Tea
            Cup of Tea -> Hot Water
            Hot Water -> Kettle
            Kettle -> Power

            evolve Kettle 0.62
            evolve Power 0.89

            note "Standardising power allows Kettles to evolve faster" [0.30, 0.49]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SourcingStrategyAndInertia()
    {
        const string input =
            """
            wardley-beta
            title Sourcing Strategy

            anchor Customer [0.80, 0.95]
            component Custom App [0.45, 0.85] (build)
            component Off-the-shelf Tool [0.85, 0.65] (buy)
            component Managed Service [0.60, 0.40] (outsource)
            component Cloud Platform [0.95, 0.25] (market)
            component Legacy System [0.30, 0.40] (inertia)
            component Database [0.20, 0.55] (buy) (inertia)

            Customer -> Custom App
            Custom App -> Off-the-shelf Tool
            Custom App -> Managed Service
            Off-the-shelf Tool -> Cloud Platform
            Managed Service -> Legacy System
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task LinkTypes()
    {
        const string input =
            """
            wardley-beta
            title Link Types

            component User [0.90, 0.95]
            component App [0.75, 0.75]
            component API [0.60, 0.60]
            component Cache [0.65, 0.45]
            component Database [0.15, 0.80]
            component Queue [0.35, 0.30]

            User -> App
            App +> API
            API -> Database; persists
            API +<> Cache
            Cache +'backup'> Database
            API -.-> Queue
            Queue +< Database
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Pipeline()
    {
        const string input =
            """
            wardley-beta
            title Pipeline Evolution

            anchor User [0.85, 0.50]
            component Database [0.40, 0.60]

            pipeline Database {
              component "File System" [0.25]
              component "SQL DB" [0.50]
              component "NoSQL" [0.70]
              component "Cloud DB" [0.85]
            }

            User -> Database
            "SQL DB" -> Database
            """;

        return VerifySvg(input);
    }

    // A pipeline's parent is drawn on the top of its box, not at its own visibility. Its evolve arrow took
    // its end height from the visibility, so it ran at a slant from the box.
    [Test]
    public Task PipelineParentEvolves()
    {
        const string input =
            """
            wardley-beta
            anchor User [0.85, 0.50]
            component Database [0.40, 0.30]

            pipeline Database {
              component "SQL DB" [0.20]
              component "NoSQL" [0.45]
            }

            User -> Database
            evolve Database 0.80
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task CustomStageWidths()
    {
        const string input =
            """
            wardley-beta
            title Custom Widths

            evolution Genesis@0.2 -> Custom@0.4 -> Product@0.75 -> Commodity@1.0

            component Novel [0.75, 0.15]
            component Bespoke [0.70, 0.35]
            component Product [0.65, 0.65]
            component Utility [0.60, 0.90]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DualLabelStages()
    {
        const string input =
            """
            wardley-beta
            title Dual Label Stages

            evolution Genesis / Concept -> Custom / Emerging -> Product / Converging -> Commodity / Accepted

            component Novel Idea [0.05, 0.20]
            component Custom Solution [0.35, 0.50]
            component Product [0.65, 0.70]
            component Utility [0.95, 0.90]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task AnnotationsAndForces()
    {
        const string input =
            """
            wardley-beta
            title Annotations

            component API [0.60, 0.70]
            component Cache [0.50, 0.55]
            component Database [0.40, 0.40]

            API -> Cache
            Cache -> Database

            annotations [0.10, 0.90]
            annotation 1,[0.60, 0.65] "Critical component"
            annotation 2,[0.50, 0.50] "Performance layer"
            annotation 3,[0.40, 0.35] "Data persistence"

            accelerator "AI Adoption" [0.85, 0.25]
            deaccelerator "Legacy Constraints" [0.15, 0.60]
            """;

        return VerifySvg(input);
    }

    // The complete example from Mermaid's documentation, on a custom canvas size.
    [Test]
    public Task CompleteExample()
    {
        const string input =
            """
            wardley-beta
            title Software Platform Strategy
            size [1100, 800]

            evolution Genesis@0.25 -> Custom@0.5 -> Product@0.75 -> Commodity@1.0

            anchor Customer [0.90, 0.95]

            component "Mobile App" [0.80, 0.85] (build)
            component "Web App" [0.75, 0.80] label [-60, 10] (build)
            component "API Gateway" [0.70, 0.65] (buy)
            component "Auth Service" [0.60, 0.55] (outsource)
            component "Database" [0.50, 0.45] (buy) (inertia)
            component "Cloud Platform" [0.30, 0.95] (market)

            Customer -> "Mobile App"
            Customer -> "Web App"
            "Mobile App" -> "API Gateway"
            "Web App" -> "API Gateway"
            "API Gateway" -> "Auth Service"
            "API Gateway" -> "Database"
            "Database" -> "Cloud Platform"

            evolve "API Gateway" 0.85
            evolve "Database" 0.75

            accelerator "Cloud Native" [0.20, 0.85]
            deaccelerator "Legacy Data" [0.45, 0.35]

            annotations [0.10, 0.20]
            annotation 1,[0.78, 0.82] "User touchpoints"
            annotation 2,[0.70, 0.60] "Integration layer"
            annotation 3,[0.50, 0.40] "Data persistence"

            note "Build mobile-first experience" [0.85, 0.90]
            note "Migrate to cloud-native database" [0.60, 0.50]
            """;

        return VerifySvg(input);
    }

    // Labels near the right edge would run off Mermaid's fixed canvas; here it grows to hold them.
    [Test]
    public Task LabelsBeyondCanvas()
    {
        const string input =
            """
            wardley-beta
            %% a comment after the keyword
            component A component with a rather long name [0.50, 0.98] %% trailing comment
            component Edge [0.99, 0.50] label [-120, -10]
            note "A note that starts close to the right edge" [0.20, 0.90]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Empty()
    {
        const string input = "wardley-beta";

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_ComponentsAndDecorators()
    {
        var model = Parse(
            """
            wardley-beta
            title My Map
            size [800, 1000]
            component real-time processing [0.55, 0.40]
            component "Custom Service" [0.55, 0.35] label [-50, 10] (buy) inertia
            anchor end-user [0.90, 0.95]
            component Percent [50, 25] (market)
            """);

        await Assert.That(model.Title).IsEqualTo("My Map");
        await Assert.That(model.Width).IsEqualTo(800);
        await Assert.That(model.Height).IsEqualTo(1000);

        // Anchors come first, as Mermaid adds them first.
        await Assert.That(string.Join(",", model.Nodes.Select(_ => _.Id))).IsEqualTo("end-user,real-time processing,Custom Service,Percent");

        var custom = model.Nodes.Single(_ => _.Id == "Custom Service");
        await Assert.That(custom.Visibility).IsEqualTo(0.55);
        await Assert.That(custom.Evolution).IsEqualTo(0.35);
        await Assert.That(custom.LabelOffsetX).IsEqualTo(-50);
        await Assert.That(custom.LabelOffsetY).IsEqualTo(10);
        await Assert.That(custom.SourceStrategy).IsEqualTo(WardleySourceStrategy.Buy);
        await Assert.That(custom.Inertia).IsTrue();

        var percent = model.Nodes.Single(_ => _.Id == "Percent");
        await Assert.That(percent.Visibility).IsEqualTo(0.5);
        await Assert.That(percent.Evolution).IsEqualTo(0.25);
        await Assert.That(percent.SourceStrategy).IsEqualTo(WardleySourceStrategy.Market);

        await Assert.That(model.Nodes[0].Kind).IsEqualTo(WardleyNodeKind.Anchor);
    }

    [Test]
    public async Task Parse_Links()
    {
        var model = Parse(
            """
            wardley-beta
            component A [0.9, 0.9]
            component real-time B [0.5, 0.5]
            A -> real-time B
            A-->real-time B
            A -.-> real-time B
            A +> real-time B
            A +< real-time B
            A +<> real-time B
            A +'backup'> real-time B
            A -> real-time B; annotated
            """);

        await Assert.That(model.Links.Count).IsEqualTo(8);
        await Assert.That(model.Links.All(_ => _ is { Source: "A", Target: "real-time B" })).IsTrue();
        await Assert.That(model.Links[2].Dashed).IsTrue();
        await Assert.That(model.Links[3].Flow).IsEqualTo(WardleyFlow.Forward);
        await Assert.That(model.Links[4].Flow).IsEqualTo(WardleyFlow.Backward);
        await Assert.That(model.Links[5].Flow).IsEqualTo(WardleyFlow.Bidirectional);
        await Assert.That(model.Links[6].Flow).IsEqualTo(WardleyFlow.Forward);
        await Assert.That(model.Links[6].Label).IsEqualTo("backup");
        await Assert.That(model.Links[7].Label).IsEqualTo("annotated");
        await Assert.That(model.Links[0].Flow).IsEqualTo(WardleyFlow.None);
    }

    [Test]
    public async Task Parse_PipelineAndEvolve()
    {
        var model = Parse(
            """
            wardley-beta
            component Database [0.40, 0.60]
            pipeline Database {
              component "File System" [0.25]

              %% a comment inside the pipeline
              component SQL DB [0.50] label [5, -5]
            }
            "File System" -> Database
            evolve Database 0.8
            evolve Missing 0.9
            """);

        var pipeline = model.Pipelines.Single();
        await Assert.That(pipeline.ParentId).IsEqualTo("Database");
        await Assert.That(string.Join(",", pipeline.ComponentIds)).IsEqualTo("Database_File System,Database_SQL DB");
        await Assert.That(model.Nodes.Single(_ => _.Id == "Database").IsPipelineParent).IsTrue();

        var sql = model.Nodes.Single(_ => _.Id == "Database_SQL DB");
        await Assert.That(sql.Label).IsEqualTo("SQL DB");
        await Assert.That(sql.Visibility).IsEqualTo(0.40);
        await Assert.That(sql.Evolution).IsEqualTo(0.50);
        await Assert.That(sql.LabelOffsetX).IsEqualTo(5);
        await Assert.That(sql.Kind).IsEqualTo(WardleyNodeKind.PipelineComponent);

        // A link names a pipeline component by its label.
        await Assert.That(model.Links.Single().Source).IsEqualTo("Database_File System");

        var trend = model.Trends.Single();
        await Assert.That(trend.NodeId).IsEqualTo("Database");
        await Assert.That(trend.TargetEvolution).IsEqualTo(0.8);
    }

    // An evolve names a pipeline component by its label, as a link does. It looked the name up as an id
    // only, and a component's id has its pipeline's name in front, so the evolve was dropped.
    [Test]
    public async Task Parse_EvolvePipelineComponent()
    {
        var model = Parse(
            """
            wardley-beta
            component Database [0.40, 0.60]
            pipeline Database {
              component "File System" [0.25]
            }
            evolve "File System" 0.7
            """);

        var trend = model.Trends.Single();
        await Assert.That(trend.NodeId).IsEqualTo("Database_File System");
        await Assert.That(trend.TargetEvolution).IsEqualTo(0.7);
    }

    [Test]
    public async Task Parse_StagesNotesAnnotationsAndForces()
    {
        var model = Parse(
            """
            wardley-beta
            accTitle: Accessible title
            accDescr: Accessible description
            evolution Genesis / Concept@0.3 -> Custom Built@0.6 -> Commodity@1.0
            note "Key insight" [0.4, 0.5]
            annotations [0.10, 0.90]
            annotation 2,[0.5, 0.6] "Second"
            accelerator "AI Adoption" [0.55, 0.25]
            deaccelerator Legacy Constraints [0.15, 0.75]
            """);

        await Assert.That(string.Join("|", model.Stages)).IsEqualTo("Genesis / Concept|Custom Built|Commodity");
        await Assert.That(string.Join("|", model.StageBoundaries)).IsEqualTo("0.3|0.6|1");

        var note = model.Notes.Single();
        await Assert.That(note.Text).IsEqualTo("Key insight");
        await Assert.That(note.Visibility).IsEqualTo(0.4);
        await Assert.That(note.Evolution).IsEqualTo(0.5);

        await Assert.That(model.AnnotationsBox).IsEqualTo((0.10, 0.90));
        var annotation = model.Annotations.Single();
        await Assert.That(annotation.Number).IsEqualTo(2);
        await Assert.That(annotation.Text).IsEqualTo("Second");

        await Assert.That(model.Accelerators.Single().Name).IsEqualTo("AI Adoption");
        await Assert.That(model.Deaccelerators.Single().Name).IsEqualTo("Legacy Constraints");
        await Assert.That(model.Deaccelerators.Single().Visibility).IsEqualTo(0.15);
    }

    [Test]
    public async Task Parse_RejectsCoordinateOutOfRange()
    {
        var result = new WardleyParser().Parse(
            """
            wardley-beta
            component A [150, 0.5]
            """);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("wardley-beta\ncomponent A [0.5, 0.5]", out var type)).IsTrue();
        await Assert.That(type).IsEqualTo(DiagramType.Wardley);
        await Assert.That(Mermaid.TryDetectType("WARDLEY-BETA", out var upper)).IsTrue();
        await Assert.That(upper).IsEqualTo(DiagramType.Wardley);
    }

    static WardleyModel Parse(string input)
    {
        var result = new WardleyParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }
}
