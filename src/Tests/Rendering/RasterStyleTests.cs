using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// Styling that the PNG backends used to drop: colour names outside a short list painted nothing, and a
// label's own `color` was ignored.
public class RasterStyleTests
{
    static readonly Func<string, RenderOptions, byte[]>[] backends =
    [
        (input, options) => SkiaRenderer.RenderPng(input, options),
        (input, options) => ImageSharpRenderer.RenderPng(input, options)
    ];

    static int Count(byte[] png, Func<Rgba32, bool> match)
    {
        using var image = Image.Load<Rgba32>(png);
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (match(image[x, y]))
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Test]
    [Arguments("lightgreen", 144, 238, 144)]
    [Arguments("steelblue", 70, 130, 180)]
    [Arguments("rebeccapurple", 102, 51, 153)]
    [Arguments("LightYellow", 255, 255, 224)]
    public async Task NamedColour_Parses(string name, int red, int green, int blue)
    {
        await Assert.That(CssColor.TryParse(name, out var color)).IsTrue();
        await Assert.That((color.R, color.G, color.B, color.A))
            .IsEqualTo(((byte) red, (byte) green, (byte) blue, (byte) 255));
    }

    [Test]
    public async Task NamedColourFill_IsPainted()
    {
        const string input =
            """
            flowchart LR
                A[Alpha] --> B[Beta]
                style A fill:lightgreen,stroke:darkgreen
            """;

        foreach (var render in backends)
        {
            var png = render(input, new());

            await Assert.That(Count(png, _ => _ is { R: 144, G: 238, B: 144 })).IsGreaterThan(500);
        }
    }

    // White text on a black node, on a red canvas so the only white there can be is the label.
    [Test]
    public async Task LabelColour_IsUsedForTheText()
    {
        const string input =
            """
            flowchart LR
                A[Alpha]
                style A fill:#000,stroke:#000,color:#fff
            """;
        var options = new RenderOptions
        {
            Png = new()
            {
                Background = "red"
            }
        };

        foreach (var render in backends)
        {
            var png = render(input, options);

            await Assert.That(Count(png, _ => _ is { R: > 230, G: > 230, B: > 230 })).IsGreaterThan(20);
        }
    }

    [Test]
    [Arguments("<p style=\"color:#fff\">Alpha</p>", "#fff")]
    [Arguments("<p style='background-color: red; color: rgb(1, 2, 3);'>Alpha</p>", "rgb(1, 2, 3)")]
    public async Task InlineColour_IsReadFromTheLabel(string html, string color) =>
        await Assert.That(HtmlText.InlineColor(html)).IsEqualTo(color);

    [Test]
    [Arguments("<p>Alpha</p>")]
    [Arguments("<p style=\"background-color: red\">Alpha</p>")]
    public async Task InlineColour_IsNullWhenTheLabelSetsNone(string html) =>
        await Assert.That(HtmlText.InlineColor(html)).IsNull();

    // A round cap adds half the stroke width to each end of every dash, which closed the gaps of a dotted
    // edge under Skia. With the gaps open, a good share of the pixels along the line are background.
    [Test]
    public async Task DottedEdge_KeepsItsGaps()
    {
        const string input =
            """
            flowchart LR
                A[Alpha] -.-> B[Beta]
            """;

        var model = new FlowchartParser().Parse(input).Value;
        new Naiad.Diagrams.Flowchart.FlowchartRenderer().Render(model, RenderOptions.Default);
        // Rendered at 4x so each two-unit dash and gap covers whole pixels rather than blurring together.
        const int scale = 4;
        var points = model.Edges[0].Points;
        var padding = RenderOptions.Default.Padding;
        var y = (int) Math.Round((points[0].Y + padding) * scale);
        var from = (int) ((points[0].X + padding + 4) * scale);
        var to = (int) ((points[^1].X + padding - 14) * scale);

        var options = new RenderOptions
        {
            Png = new()
            {
                Scale = scale
            }
        };
        using var image = Image.Load<Rgba32>(SkiaRenderer.RenderPng(input, options));
        var gaps = 0;
        for (var x = from; x < to; x++)
        {
            if (image[x, y] is { R: > 200, G: > 200, B: > 200 })
            {
                gaps++;
            }
        }

        await Assert.That(to - from).IsGreaterThan(20);
        await Assert.That(gaps).IsGreaterThan((to - from) / 4);
    }
}
