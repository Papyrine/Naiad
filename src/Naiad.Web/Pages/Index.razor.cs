namespace Naiad.Web.Pages;

public partial class Index : IDisposable
{
    // Re-render this long after the last keystroke. Keeps a fast typist from triggering a parse on every
    // character while still feeling live.
    const int debounceMilliseconds = 250;

    // Device-pixel multiplier for the PNG export, so downloaded rasters are crisp on high-DPI displays.
    const double pngScale = 2;

    string source = DiagramSamples.Default.Source;
    DiagramRenderResult result = DiagramRenderResult.Empty;
    DiagramDocsLink? docsLink;
    string? issueUrl;
    string? userAgent;
    bool isExporting;
    RenderStats? stats;
    CancelSource? debounce;

    // Render once synchronously so the first paint already shows the default sample's diagram.
    protected override void OnInitialized() =>
        Render();

    protected override async Task OnInitializedAsync() =>
        userAgent = await JsRuntime.InvokeAsync<string?>("appInfo.userAgent");

    async Task OnInput(ChangeEventArgs args)
    {
        source = args.Value?.ToString() ?? "";

        // Coalesce bursts of keystrokes: cancel the pending render and start a fresh timer. Only the
        // last keystroke in a burst survives to call Render.
        debounce?.Cancel();
        debounce?.Dispose();
        var cancelSource = new CancelSource();
        debounce = cancelSource;

        try
        {
            await Task.Delay(debounceMilliseconds, cancelSource.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        Render();
    }

    void LoadSample(DiagramSample sample)
    {
        // A picked sample replaces the editor content and renders immediately — no debounce wait.
        debounce?.Cancel();
        source = sample.Source;
        Render();
    }

    void Render()
    {
        // Time only the parse-and-render work so the status bar can report how long Naiad took to build the SVG.
        var stopwatch = Stopwatch.StartNew();
        result = DiagramRenderer.Render(source);
        stopwatch.Stop();

        // Detect from the raw source rather than the render outcome: the opening keyword identifies the type
        // even while the body is mid-edit and not yet parseable, so the docs link stays useful through errors.
        docsLink = DiagramDocs.For(source);

        // Summarise the render for the status bar — only when a diagram was produced; an error or empty
        // source has nothing to describe.
        stats = result.Svg is { } svg
            ? new(docsLink?.Name, ReadDimensions(svg), Encoding.UTF8.GetByteCount(svg), stopwatch.Elapsed.TotalMilliseconds)
            : null;

        issueUrl = result.Unexpected is { } exception
            ? IssueLauncher.ForException("Render diagram", exception, AppInfo.Environment(userAgent), source)
            : null;
    }

    // The diagram's intrinsic pixel size lives only in the SVG's viewBox ("minX minY width height"); the root
    // element's own width is the responsive "100%". Returns null when the viewBox can't be read.
    static (double Width, double Height)? ReadDimensions(string svg)
    {
        var match = Regex.Match(svg, "viewBox='([^']*)'");
        if (!match.Success)
        {
            return null;
        }

        var parts = match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
        {
            return null;
        }

        return (width, height);
    }

    // Status-bar formatters. InvariantGlobalization fixes the decimal separator across locales.

    // One decimal place keeps a sub-millisecond render legible as e.g. "0.4 ms" instead of collapsing to "0 ms".
    static string FormatMilliseconds(double value) =>
        $"{value:0.#} ms";

    static string FormatDimensions((double Width, double Height) dimensions) =>
        $"{(int) Math.Round(dimensions.Width)} × {(int) Math.Round(dimensions.Height)}";

    static string FormatBytes(int bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{kilobytes:0.#} KB";
        }

        return $"{kilobytes / 1024d:0.#} MB";
    }

    sealed record RenderStats(string? TypeName, (double Width, double Height)? Dimensions, int SvgByteCount, double Milliseconds);

    Task DownloadSvg()
    {
        if (result.Svg is { } svg)
        {
            return FileDownloadService.DownloadTextAsync("diagram.svg", "image/svg+xml", svg);
        }

        return Task.CompletedTask;
    }

    async Task DownloadPng()
    {
        if (!result.HasSvg)
        {
            return;
        }

        isExporting = true;
        try
        {
            // Rasterize a self-contained SVG (native text, no foreignObject) in the browser's canvas, then
            // hand the resulting PNG bytes to the download. Best-effort: an export failure leaves the
            // preview untouched rather than surfacing an error.
            var exportable = DiagramRenderer.RenderForPng(source);
            var base64 = await JsRuntime.InvokeAsync<string>("diagramExport.svgToPng", exportable, pngScale);
            await FileDownloadService.DownloadAsync("diagram.png", "image/png", Convert.FromBase64String(base64));
        }
        catch
        {
            // Swallow: PNG export is a convenience over the always-available SVG download.
        }
        finally
        {
            isExporting = false;
        }
    }

    public void Dispose()
    {
        debounce?.Cancel();
        debounce?.Dispose();
    }
}
