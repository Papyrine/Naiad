// Code samples surfaced in the readme via MarkdownSnippets.
static class Snippets
{
    public static void Usage()
    {
        // begin-snippet: Usage
        var svg = Mermaid.Render(
            """
            flowchart LR
                A[Start] --> B[Process] --> C[End]
            """);
        // end-snippet
        Console.WriteLine(svg);
    }

    public static void RenderWithOptions(string input)
    {
        // begin-snippet: RenderOptions
        var svg = Mermaid.Render(
            input,
            new()
            {
                Padding = 20,
                FontSize = 14,
                FontFamily = "Arial, sans-serif",
                // indent the SVG markup, one element per line (default false: compact)
                PrettyPrint = true
            });
        // end-snippet
        Console.WriteLine(svg);
    }

    public static void RenderToPng(string input)
    {
        // begin-snippet: RenderToPng
        // Naiad.Skia
        var skiaPng = SkiaRenderer.RenderPng(input);
        SkiaRenderer.RenderPng(input, "diagram.png");

        // Naiad.ImageSharp
        var imageSharpPng = ImageSharpRenderer.RenderPng(input);
        ImageSharpRenderer.RenderPng(input, "diagram.png");
        // end-snippet
        Console.WriteLine(skiaPng.Length + imageSharpPng.Length);
    }

    public static void PngOptions(string input) =>
        // begin-snippet: PngOptions
        SkiaRenderer.RenderPng(
            input,
            "diagram.png",
            new()
            {
                Png =
                {
                    // 2x device-pixel scale for high-DPI output
                    Scale = 2,
                    // any CSS colour, or "transparent"
                    Background = "white",
                    // trade encode speed against file size: Fast, Balanced (default) or Small
                    Compression = PngCompression.Balanced
                }
            });
    // end-snippet

    public static void LoadIconPack()
    {
        // begin-snippet: LoadIconPack
        IconPack.Load("logos.json");

        // ...or from a stream
        using var stream = File.OpenRead("logos.json");
        IconPack.Load(stream);
        // end-snippet
    }

    public static void IconUsage()
    {
        // begin-snippet: IconUsage
        // Architecture
        Mermaid.Render(
            """
            architecture-beta
            service fn(logos:aws-lambda)[Lambda]
            service db(logos:postgresql)[Database]
            fn:R -- L:db
            """);

        // Flowchart (inline in labels)
        Mermaid.Render(
            """
            flowchart LR
                A[logos:redis Cache] --> B[logos:postgresql DB]
            """);

        // Mindmap
        Mermaid.Render(
            """
            mindmap
              Project
                Storage ::icon(logos:aws-s3)
            """);
        // end-snippet
    }

    public static void IconsInPng()
    {
        // begin-snippet: IconsInPng
        // Load packs up front, before the first render.
        IconPack.Load("logos.json");
        IconPack.Load("fontawesome.json");

        // Iconify pack icons are inline SVG, so they rasterize to PNG just like the rest of the diagram.
        var withIconifyIcon = SkiaRenderer.RenderPng(
            """
            flowchart LR
                A[logos:redis Cache] --> B[logos:postgresql DB]
            """);

        // FontAwesome glyphs have no geometry of their own, so a fa: icon only appears in PNG when a
        // pack registered under the "fa" prefix supplies it (icons keyed fa-bell, fa-car, ...).
        var withFontAwesomeIcon = SkiaRenderer.RenderPng(
            """
            flowchart LR
                A[fa:fa-bell Alerts]
            """);
        // end-snippet
        Console.WriteLine(withIconifyIcon.Length + withFontAwesomeIcon.Length);
    }
}
