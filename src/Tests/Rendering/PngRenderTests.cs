using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// Visual-regression coverage for the two PNG backends. Both drive the same shared
/// <c>SvgRasterizer</c> pipeline, so a representative spread of diagrams — exercising arcs, Béziers,
/// markers, foreignObject labels, dashes, transforms, text anchoring and the various shape primitives —
/// pins the output of the walker and both surfaces. PNGs are compared with the same fuzzy image
/// comparer the SVG suite uses (registered in Release).
/// </summary>
public class PngRenderTests
{
    // Arcs, fills, slice labels (text-anchor middle), legend swatches, title.
    const string pie =
        """
        pie title Pets
         "Dogs" : 40
         "Cats" : 30
         "Birds" : 20
        """;

    // H/V node paths, foreignObject labels, arrowhead markers, group transforms.
    const string flowchart =
        """
        flowchart LR
         A[Start] --> B[Process] --> C[End]
        """;

    // Rounded actor boxes, dashed lifelines and return arrow, markers both directions.
    const string sequence =
        """
        sequenceDiagram
         Alice->>John: Hello John
         John-->>Alice: Hi Alice
        """;

    // Bold text, compartment lines, inheritance triangle polygon.
    const string @class =
        """
        classDiagram
            class Animal {
                +String name
                +int age
                +makeSound() void
            }
            Animal <|-- Dog
        """;

    // Composite nodes, curved edges, start/end markers.
    const string state =
        """
        stateDiagram-v2
            [*] --> Still
            Still --> Moving
            Moving --> Still
            Moving --> Crash
            Crash --> [*]
        """;

    // Commit circles and colored branch lines.
    const string gitGraph =
        """
        gitGraph
            commit
            commit
            commit
        """;

    // The only diagram whose arrowhead rides a path wider than one unit, so it is what pins the
    // markerUnits="strokeWidth" scaling; also dashed drop-lines and the arc-based score faces.
    const string journey =
        """
        journey
            title My Working Day
            section Morning
                Make coffee: 5: Me
                Check emails: 3: Me
        """;

    // Inline-SVG icons embedded in foreignObject labels — the rasterizer must pull them out of the
    // label HTML and draw them next to the text rather than dropping them with the other tags.
    const string flowchartIcons =
        """
        flowchart LR
         A[sample:box Storage] --> B[sample:ring Cache]
        """;

    const string iconPack =
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

    // Icon packs are process-global and frozen after the first render, so reset before each test.
    [Before(Test)]
    public void ResetIconPacks() => IconPackRegistry.Reset();

    [Test]
    public Task SkiaPie() => VerifyPng(SkiaRenderer.RenderPng(pie, HighDpi));

    [Test]
    public Task SkiaFlowchart() => VerifyPng(SkiaRenderer.RenderPng(flowchart, HighDpi));

    [Test]
    public Task SkiaSequence() => VerifyPng(SkiaRenderer.RenderPng(sequence, HighDpi));

    [Test]
    public Task SkiaClass() => VerifyPng(SkiaRenderer.RenderPng(@class, HighDpi));

    [Test]
    public Task SkiaState() => VerifyPng(SkiaRenderer.RenderPng(state, HighDpi));

    [Test]
    public Task SkiaGitGraph() => VerifyPng(SkiaRenderer.RenderPng(gitGraph, HighDpi));

    [Test]
    public Task SkiaJourney() => VerifyPng(SkiaRenderer.RenderPng(journey, HighDpi));

    [Test]
    public Task ImageSharpPie() => VerifyPng(ImageSharpRenderer.RenderPng(pie, HighDpi));

    [Test]
    public Task ImageSharpFlowchart() => VerifyPng(ImageSharpRenderer.RenderPng(flowchart, HighDpi));

    [Test]
    public Task ImageSharpSequence() => VerifyPng(ImageSharpRenderer.RenderPng(sequence, HighDpi));

    [Test]
    public Task ImageSharpClass() => VerifyPng(ImageSharpRenderer.RenderPng(@class, HighDpi));

    [Test]
    public Task ImageSharpState() => VerifyPng(ImageSharpRenderer.RenderPng(state, HighDpi));

    [Test]
    public Task ImageSharpGitGraph() => VerifyPng(ImageSharpRenderer.RenderPng(gitGraph, HighDpi));

    [Test]
    public Task ImageSharpJourney() => VerifyPng(ImageSharpRenderer.RenderPng(journey, HighDpi));

    [Test]
    public Task SkiaFlowchartIcons()
    {
        LoadIconPack();
        return VerifyPng(SkiaRenderer.RenderPng(flowchartIcons, HighDpi));
    }

    [Test]
    public Task ImageSharpFlowchartIcons()
    {
        LoadIconPack();
        return VerifyPng(ImageSharpRenderer.RenderPng(flowchartIcons, HighDpi));
    }

    static void LoadIconPack()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(iconPack));
        IconPack.Load(stream);
    }

    [Test]
    public async Task ScaleEnlargesOutput()
    {
        const string source = "flowchart LR\n A[Start] --> B[End]";
        using var single = Image.Load<Rgba32>(SkiaRenderer.RenderPng(source));
        using var doubled = Image.Load<Rgba32>(SkiaRenderer.RenderPng(source, new()
        {
            Png =
            {
                Scale = 2
            }
        }));
        await Assert.That(doubled.Width).IsEqualTo(single.Width * 2).Within(2);
        await Assert.That(doubled.Height).IsEqualTo(single.Height * 2).Within(2);
    }

    // Render at 2x to match the device-pixel scale the SVG snapshot suite uses.
    static RenderOptions HighDpi => new()
    {
        Png =
        {
            Scale = 2
        }
    };

    static Task VerifyPng(byte[] png, [CallerFilePath] string sourceFile = "") =>
        Verify(new MemoryStream(png), extension: "png", sourceFile: sourceFile);
}
