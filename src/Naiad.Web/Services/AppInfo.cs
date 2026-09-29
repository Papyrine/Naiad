namespace Naiad.Web.Services;

/// <summary>Build/version facts worth attaching to a bug report.</summary>
public static class AppInfo
{
    public static string Version { get; } = ShortenSha(
        typeof(Mermaid).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Mermaid).Assembly.GetName().Version?.ToString()
        ?? "unknown");

    // The SDK suffixes the informational version with "+<40-char commit sha>"; trim it to the 7-char short
    // form git itself displays.
    static string ShortenSha(string version)
    {
        var plus = version.IndexOf('+');
        if (plus < 0)
        {
            return version;
        }

        var sha = version[(plus + 1)..];
        if (sha.Length <= 7)
        {
            return version;
        }

        return version[..(plus + 1)] + sha[..7];
    }

    /// <summary>Pre-formatted Markdown bullet lines describing the runtime, for an issue body.</summary>
    public static string Environment(string? userAgent) =>
        string.Join(
            '\n',
            $"* Naiad version: {Version}",
            $"* User agent: {userAgent}");
}
