/// <summary>The outcome of rendering Mermaid source: SVG markup, or a parse/render error message.</summary>
public record DiagramRenderResult(string? Svg, string? Error, Exception? Unexpected)
{
    /// <summary>An empty result — no SVG and no error, shown before the first render.</summary>
    public static DiagramRenderResult Empty { get; } = new(null, null, null);

    public bool HasSvg => Svg is not null;
    public bool HasError => Error is not null;

    /// <summary>True when the error was a renderer fault rather than malformed user input.</summary>
    public bool IsUnexpected => Unexpected is not null;
}

/// <summary>
/// Wraps <see cref="Mermaid"/> for the live editor. Every render is guarded so malformed input — the
/// common case while a user is still typing — surfaces as an inline message instead of tearing down the
/// component. The PNG path re-renders with HTML elements disabled, producing a self-contained SVG
/// (native &lt;text&gt;, no foreignObject or Font Awesome <c>@import</c>) that a browser canvas can
/// rasterize cleanly and without tainting.
/// </summary>
public static class DiagramRenderer
{
    public static DiagramRenderResult Render(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return DiagramRenderResult.Empty;
        }

        try
        {
            return new(Mermaid.Render(source), null, null);
        }
        catch (MermaidException exception)
        {
            // Expected: the markup didn't parse or names an unsupported diagram. Show it, no bug prompt.
            return new(null, exception.Message, null);
        }
        catch (Exception exception)
        {
            // Unexpected: a renderer fault rather than user error — surface it and invite a bug report.
            return new(null, exception.Message, exception);
        }
    }

    /// <summary>
    /// Renders a self-contained SVG for raster export. The caller only reaches this once
    /// <see cref="Render"/> has already produced a preview, so the source is known to parse.
    /// </summary>
    public static string RenderForPng(string source) =>
        Mermaid.Render(source, new() { AllowHtmlElements = false });
}
