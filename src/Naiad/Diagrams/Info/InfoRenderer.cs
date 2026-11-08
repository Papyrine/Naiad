using System.Reflection;

namespace Naiad.Diagrams.Info;

/// <summary>
/// Draws the library version on one line. Mermaid's info diagram shows Mermaid's own version; here the
/// renderer is Naiad, so it shows Naiad's, and says so, since a bare version number would read as
/// Mermaid's. The canvas and font size follow Mermaid's (400 by 100, 32px).
/// </summary>
public class InfoRenderer : IDiagramRenderer<InfoModel>
{
    const double width = 400;
    const double height = 100;
    const double fontSize = 32;
    const string textColor = "#333";

    static readonly string assemblyVersion = ReadAssemblyVersion();

    /// <summary>
    /// Replaces the version drawn, so snapshot tests do not change with every release. Tests set it and
    /// reset it to <see langword="null"/> afterwards.
    /// </summary>
    internal static string? VersionOverride { get; set; }

    internal static string Version => VersionOverride ?? assemblyVersion;

    public SvgDocument Render(InfoModel model, RenderOptions options)
    {
        var builder = new SvgBuilder();
        builder.Size(width, height);
        builder.AddText(
            width / 2,
            height / 2,
            $"Naiad v{Version}",
            anchor: "middle",
            baseline: "middle",
            fontSize: fontSize,
            fontFamily: options.FontFamily,
            fill: textColor,
            cssClass: "version");
        return builder.Build();
    }

    // The SDK appends "+<commit sha>" to the informational version; it is build metadata, not part of the
    // version a reader would recognise, so it is dropped.
    static string ReadAssemblyVersion()
    {
        var assembly = typeof(InfoRenderer).Assembly;
        var version =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "unknown";
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }
}
