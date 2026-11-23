/// <summary>
/// Runs the layout checks (<see cref="LayoutLint"/>) over the input of every Flowchart, Class, ER and
/// State snapshot test, and holds each diagram's count of each problem to layout-lint-baseline.txt, which
/// records the problems that exist today. A count above the baseline is a regression. A count below it is
/// a fix, and fails too until the baseline comes down with it, so the baseline only ever moves towards
/// zero. Run with <c>LAYOUT_LINT_UPDATE=1</c> to rewrite it from the current counts.
/// </summary>
/// <remarks>
/// Derives from <see cref="TestBase"/> for its icon pack reset: a pack another test registered would
/// change how wide an icon label is measured, and so the layout.
/// </remarks>
public class LayoutLintTests : TestBase
{
    static readonly string baselinePath = Path.Combine(ProjectFiles.ProjectDirectory, "LayoutLint", "layout-lint-baseline.txt");

    // The folders whose snapshot tests make up the corpus, one for each diagram type the lint covers.
    static readonly string[] corpusFolders = ["Flowchart", "Class", "EntityRelationship", "State"];

    const string updateVariable = "LAYOUT_LINT_UPDATE";

    const string header =
        """
        # The layout problems each snapshot test's diagram has today, as `diagram check count`, counted by
        # LayoutLintTests. A count may only go down: after a layout fix lowers one, rewrite this file by
        # running LayoutLintTests with LAYOUT_LINT_UPDATE=1.
        """;

    [Test]
    public void CountsMatchBaseline()
    {
        var current = new Dictionary<(string Diagram, string Check), List<LayoutLint.Violation>>();
        foreach (var (name, input) in Corpus())
        {
            foreach (var check in LayoutLint.Run(DrawnLayouts.Render(input)).GroupBy(_ => _.Check))
            {
                current[(name, check.Key)] = check.ToList();
            }
        }

        if (Environment.GetEnvironmentVariable(updateVariable) == "1")
        {
            WriteBaseline(current);
            return;
        }

        var baseline = ReadBaseline();
        var rises = new StringBuilder();
        var drops = new StringBuilder();
        foreach (var key in Ordered(current.Keys.Union(baseline.Keys)))
        {
            var violations = current.GetValueOrDefault(key) ?? [];
            var allowed = baseline.GetValueOrDefault(key);
            if (violations.Count > allowed)
            {
                rises.Append($"\n  {key.Diagram} {key.Check}: {violations.Count}, baseline {allowed}");
                foreach (var violation in violations)
                {
                    rises.Append($"\n    {violation.Detail}");
                }
            }

            if (violations.Count < allowed)
            {
                drops.Append($"\n  {key.Diagram} {key.Check}: {violations.Count}, baseline {allowed}");
            }
        }

        var message = new StringBuilder();
        if (rises.Length > 0)
        {
            message.Append(
                $"Layout problems were added. Fix them, or if they are known and accepted (a new snapshot test " +
                $"that shows an existing bug, say), rerun with {updateVariable}=1 to record them:{rises}\n");
        }

        if (drops.Length > 0)
        {
            message.Append($"Layout problems were fixed. Lower the baseline to match by rerunning with {updateVariable}=1:{drops}\n");
        }

        if (message.Length > 0)
        {
            Assert.Fail(message.ToString());
        }
    }

    // An empty corpus would pass the baseline check without having checked anything.
    [Test]
    public async Task CorpusCoversEachDiagramType()
    {
        var types = new HashSet<DiagramType>();
        foreach (var (_, input) in Corpus())
        {
            if (Mermaid.TryDetectType(input, out var type))
            {
                types.Add(type);
            }
        }

        DiagramType[] expected = [DiagramType.Flowchart, DiagramType.Class, DiagramType.EntityRelationship, DiagramType.State];
        await Assert.That(types).IsEquivalentTo(expected);
    }

    // Each check is shown a layout holding exactly its own problem, and must find that and nothing else, so
    // a check that could never fire cannot hide behind a baseline of zeros.
    [Test]
    public async Task EachCheckFindsItsOwnProblem()
    {
        await Assert.That(LayoutLint.Run(CleanLayout())).IsEmpty();

        foreach (var check in LayoutLint.Checks)
        {
            var found = LayoutLint.Run(BrokenLayout(check)).Select(_ => _.Check);
            await Assert.That(string.Join(", ", found)).IsEqualTo(check);
        }
    }

    // The input of every snapshot test in the corpus folders, named as the test's .verified files are.
    static List<(string Name, string Input)> Corpus()
    {
        var corpus = new List<(string Name, string Input)>();
        foreach (var folder in corpusFolders)
        {
            var files = Directory.GetFiles(Path.Combine(ProjectFiles.ProjectDirectory, folder), "*Tests.cs")
                .Order(StringComparer.Ordinal);
            foreach (var file in files)
            {
                foreach (var snapshot in SnapshotInputs.Read(file))
                {
                    var name = $"{snapshot.ClassName}.{snapshot.MethodName}";
                    if (snapshot.Input is null)
                    {
                        throw new($"Could not read the input of {name} from its source, so the layout lint cannot check it");
                    }

                    corpus.Add((name, snapshot.Input));
                }
            }
        }

        return corpus;
    }

    static Dictionary<(string Diagram, string Check), int> ReadBaseline()
    {
        var baseline = new Dictionary<(string Diagram, string Check), int>();
        if (!File.Exists(baselinePath))
        {
            return baseline;
        }

        foreach (var line in File.ReadAllLines(baselinePath))
        {
            if (line.Length == 0 ||
                line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ');
            baseline[(parts[0], parts[1])] = int.Parse(parts[2], CultureInfo.InvariantCulture);
        }

        return baseline;
    }

    static void WriteBaseline(Dictionary<(string Diagram, string Check), List<LayoutLint.Violation>> current)
    {
        var builder = new StringBuilder(header).Append("\n\n");
        foreach (var key in Ordered(current.Keys))
        {
            builder.Append(CultureInfo.InvariantCulture, $"{key.Diagram} {key.Check} {current[key].Count}\n");
        }

        File.WriteAllText(baselinePath, builder.ToString());
    }

    // By diagram, then in the order the checks run.
    static IEnumerable<(string Diagram, string Check)> Ordered(IEnumerable<(string Diagram, string Check)> keys) =>
        keys
            .OrderBy(_ => _.Diagram, StringComparer.Ordinal)
            .ThenBy(_ => Array.IndexOf(LayoutLint.Checks, _.Check));

    // Two nodes in a cluster, joined by an edge from border to border, with its label between them.
    static DrawnLayout CleanLayout() =>
        new(
            new(0, 0, 200, 300),
            [
                Node("A", new(50, 20, 100, 40)),
                Node("B", new(50, 220, 100, 40))
            ],
            [new("K", new(30, 0, 140, 280))],
            [new("A", "B", [new(100, 60), new(100, 220)])],
            [new("label", new(80, 130, 40, 16))]);

    static DrawnLayout BrokenLayout(string check)
    {
        var layout = CleanLayout();
        switch (check)
        {
            case LayoutLint.ClusterOverlap:
                // Across the right side of K.
                layout.Clusters.Add(new("K2", new(120, 100, 70, 50)));
                break;
            case LayoutLint.NodeOverlap:
                layout.Nodes.Add(Node("C", new(60, 25, 40, 20)));
                break;
            case LayoutLint.EdgeThroughNode:
                // Astride the line from A to B.
                layout.Nodes.Add(Node("D", new(80, 90, 40, 20)));
                break;
            case LayoutLint.EdgeThroughCluster:
                // Inside K, astride the line from A to B, and holding neither.
                layout.Clusters.Add(new("K3", new(85, 90, 30, 30)));
                break;
            case LayoutLint.EdgeEnd:
                // Ten short of B.
                layout.Edges[0].Points[^1] = new(100, 210);
                break;
            case LayoutLint.LabelOverlap:
                layout.Labels[0] = new("label", new(80, 210, 40, 16));
                break;
            case LayoutLint.Canvas:
                // Hanging off the bottom.
                layout.Nodes.Add(Node("E", new(175, 285, 20, 20)));
                break;
        }

        return layout;
    }

    static DrawnNode Node(string id, Rect box) =>
        new(id, box, DrawnLayouts.Corners(box));
}
