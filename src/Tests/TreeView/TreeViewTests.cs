using Naiad.Diagrams.TreeView;

public class TreeViewTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            treeView-beta
                my-project/
                    src/
                        index.js
                    package.json
                    README.md
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task QuotedLabels()
    {
        const string input =
            """
            treeView-beta
                "my project"
                    "folder with spaces"
                        "file.js"
                    'single quoted/'
            """;

        return VerifySvg(input);
    }

    // The documentation's box-drawing example: depth comes from the column of each branch character.
    [Test]
    public Task BoxDrawing()
    {
        const string input =
            """
            treeView-beta
            ├── packages/
            │   ├── mermaid/
            │   │   ├── src/
            │   │   │   ├── parser.ts
            │   │   │   └── renderer.ts
            │   │   └── package.json
            │   └── parser/
            │       └── src/
            └── README.md
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task HeavyBoxDrawing()
    {
        const string input =
            """
            treeView-beta
            ┣━━ src/
            ┃   ┗━━ main.rs
            ┃
            ┗━━ Cargo.toml
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Descriptions()
    {
        const string input =
            """
            treeView-beta
                src/
                    index.js ## app entry point
                    config.ts ## runtime configuration
                package.json ## project manifest
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Highlight()
    {
        const string input =
            """
            treeView-beta
                src/
                    App.tsx :::highlight
                    index.js
                package.json
            """;

        return VerifySvg(input);
    }

    // The documentation's combined example, with an icon from a registered pack, the built-in icons, a
    // hidden icon and one that is not registered.
    [Test]
    public Task CombinedAnnotations()
    {
        const string input =
            """
            treeView-beta
                my-project/ icon(folder)
                    src/
                        App.tsx :::highlight icon(logos:react) ## main component
                        index.js icon(file) ## entry point
                        util.js icon(none)
                    .env ## environment variables
                    Dockerfile icon(missing:docker)
                    package.json
            """;
        const string pack =
            """
            {
              "prefix": "logos",
              "width": 24,
              "height": 24,
              "icons": {
                "react": {"body": "<circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"#61dafb\"/>"}
              }
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(pack));
        IconPack.Load(stream);

        return VerifySvg(input);
    }

    [Test]
    public Task Title()
    {
        const string input =
            """
            treeView-beta
                title Project layout
                accTitle: Files
                accDescr: The files of the project
                src/
                    index.js
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task CommentsAndBlankLines()
    {
        const string input =
            """
            %% leading comment
            treeView-beta
                %% Generated files, do not edit
                src/

                    generated/
                    %% a comment between nodes
                    index.js
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task KeywordOnly()
    {
        const string input =
            """
            treeView-beta
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_Hierarchy()
    {
        var model = Parse(
            """
            treeView-beta
                my-project/
                    src/
                        index.js
                    package.json
                top.txt
            """);

        await Assert.That(Describe(model)).IsEqualTo("my-project/(src/(index.js),package.json),top.txt");
        await Assert.That(model.Nodes[0].IsDirectory).IsTrue();
        await Assert.That(model.Nodes[0].Name).IsEqualTo("my-project");
    }

    // A node goes under the nearest earlier node indented less than it, so an outdent that stops between
    // two levels still makes a child.
    [Test]
    public async Task Parse_UnevenIndentation()
    {
        var model = Parse(
            """
            treeView-beta
            a
                  b
                c
              d
            e
            """);

        await Assert.That(Describe(model)).IsEqualTo("a(b,c,d),e");
    }

    [Test]
    public async Task Parse_Annotations()
    {
        var model = Parse(
            """
            treeView-beta
                App.tsx ##   main  component   icon(x)
                b icon(logos:react) :::highlight
                c icon()
                with  spaces :::  other
                "quoted/" ## note
            """);

        var app = model.Nodes[0];
        await Assert.That(app.Name).IsEqualTo("App.tsx");
        await Assert.That(app.Description).IsEqualTo("main  component   icon(x)");

        var second = model.Nodes[1];
        await Assert.That(second.Name).IsEqualTo("b");
        await Assert.That(second.Icon).IsEqualTo("logos:react");
        await Assert.That(second.CssClass).IsEqualTo("highlight");
        await Assert.That(second.IsHighlighted).IsTrue();

        await Assert.That(model.Nodes[2].Icon).IsEqualTo("none");

        var spaced = model.Nodes[3];
        await Assert.That(spaced.Name).IsEqualTo("with  spaces");
        await Assert.That(spaced.CssClass).IsEqualTo("other");
        await Assert.That(spaced.IsHighlighted).IsFalse();

        var quoted = model.Nodes[4];
        await Assert.That(quoted.Name).IsEqualTo("quoted");
        await Assert.That(quoted.IsDirectory).IsTrue();
        await Assert.That(quoted.Description).IsEqualTo("note");
    }

    // Annotation markers only count after whitespace, so they may appear inside a name.
    [Test]
    public async Task Parse_MarkersInsideName()
    {
        var model = Parse(
            """
            treeView-beta
                a:::b
                c##d
            """);

        await Assert.That(model.Nodes.Select(_ => _.Name)).IsEquivalentTo(["a:::b", "c##d"]);
        await Assert.That(model.Nodes[0].CssClass).IsNull();
    }

    // A box-drawing character with no branch before it is part of a top-level name.
    [Test]
    public async Task Parse_BoxCharacterInsideName()
    {
        var model = Parse(
            """
            treeView-beta
            Section ─ A.txt
            └── child
            """);

        await Assert.That(Describe(model)).IsEqualTo("Section ─ A.txt(child)");
    }

    [Test]
    public async Task Parse_Title()
    {
        var model = Parse(
            """
            treeView-beta
            title My files
            accDescr {
              several
              lines
            }
            a
            """);

        await Assert.That(model.Title).IsEqualTo("My files");
        await Assert.That(Describe(model)).IsEqualTo("a");
    }

    [Test]
    public async Task Parse_BoxDrawing()
    {
        var model = Parse(
            """
            treeView-beta
            root/
            ├── src/
            │   ├── index.ts :::highlight ## entry
            │   └── utils.ts
            │
            └── package.json
            """);

        // A branch in the first column is one level down, so an unprefixed first line becomes the parent.
        await Assert.That(Describe(model)).IsEqualTo("root/(src/(index.ts,utils.ts),package.json)");
        var index = model.Nodes[0].Children[0].Children[0];
        await Assert.That(index.Description).IsEqualTo("entry");
        await Assert.That(index.CssClass).IsEqualTo("highlight");
    }

    [Test]
    public async Task Parse_BoxDrawingErrors()
    {
        var empty = new TreeViewParser().Parse("treeView-beta\n├── a\n└──   ");
        await Assert.That(empty.Success).IsFalse();
        await Assert.That(empty.Error).Contains("line 3");

        var mixed = new TreeViewParser().Parse("treeView-beta\n├── a\n    b");
        await Assert.That(mixed.Success).IsFalse();
        await Assert.That(mixed.Error).Contains("line 3");
    }

    [Test]
    public async Task Parse_ErrorReportsLine()
    {
        var result = new TreeViewParser().Parse("treeView-beta\n    a\n    b icon(bad name)\n");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error).Contains("line 3");
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("treeView-beta\n    a", out var treeView)).IsTrue();
        await Assert.That(treeView).IsEqualTo(DiagramType.TreeView);
        await Assert.That(Mermaid.TryDetectType("%% comment\nTREEVIEW-BETA\n    a", out var upper)).IsTrue();
        await Assert.That(upper).IsEqualTo(DiagramType.TreeView);
        await Assert.That(Mermaid.TryDetectType("treemap-beta\n    \"a\": 1", out var treemap)).IsTrue();
        await Assert.That(treemap).IsEqualTo(DiagramType.Treemap);
        await Assert.That(Mermaid.TryDetectType("treemap\n    \"a\": 1", out var plainTreemap)).IsTrue();
        await Assert.That(plainTreemap).IsEqualTo(DiagramType.Treemap);
    }

    static TreeViewModel Parse(string input)
    {
        var result = new TreeViewParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }

    static string Describe(TreeViewModel model) =>
        string.Join(",", model.Nodes.Select(Describe));

    static string Describe(TreeViewNode node)
    {
        var name = node.IsDirectory ? node.Name + "/" : node.Name;
        return node.Children.Count == 0
            ? name
            : $"{name}({string.Join(",", node.Children.Select(Describe))})";
    }
}
