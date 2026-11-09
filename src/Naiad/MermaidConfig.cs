namespace Naiad;

/// <summary>
/// The configuration a diagram was given ahead of its text: the <c>config</c> mapping of its front matter,
/// with every <c>%%{init: ...}%%</c> (or <c>%%{initialize: ...}%%</c>) directive merged over it, as
/// Mermaid merges them. Keys are as Mermaid documents them, such as <c>theme</c>,
/// <c>themeVariables.primaryColor</c> or <c>gitGraph.showBranches</c>; a value is read through one of the
/// typed accessors, which give <see langword="null"/> for a key that was not set (or not set to something
/// of that type), so the caller's own default applies.
/// </summary>
sealed class MermaidConfig
{
    public static MermaidConfig Empty { get; } = new(ConfigReader.NewMap());

    readonly Dictionary<string, object?> values;

    MermaidConfig(Dictionary<string, object?> values) =>
        this.values = values;

    /// <summary>
    /// Merges the configuration from front matter and directives. Later directives win over earlier ones,
    /// and all of them over front matter; mappings are merged key by key, anything else is replaced.
    /// </summary>
    internal static MermaidConfig Create(Dictionary<string, object?>? frontMatter, IReadOnlyList<Dictionary<string, object?>> directives)
    {
        if (frontMatter is null &&
            directives.Count == 0)
        {
            return Empty;
        }

        var merged = ConfigReader.NewMap();
        if (frontMatter is not null)
        {
            Merge(merged, frontMatter);
        }

        foreach (var directive in directives)
        {
            Merge(merged, directive);
        }

        return new(merged);
    }

    static void Merge(Dictionary<string, object?> target, Dictionary<string, object?> source)
    {
        foreach (var (key, value) in source)
        {
            if (value is Dictionary<string, object?> map)
            {
                if (target.GetValueOrDefault(key) is not Dictionary<string, object?> existing)
                {
                    existing = ConfigReader.NewMap();
                    target[key] = existing;
                }

                Merge(existing, map);
                continue;
            }

            target[key] = value;
        }
    }

    // The value at a path of keys, such as ("gitGraph", "showBranches").
    internal object? Get(params ReadOnlySpan<string> path)
    {
        object? current = values;
        foreach (var key in path)
        {
            if (current is not Dictionary<string, object?> map ||
                !map.TryGetValue(key, out current))
            {
                return null;
            }
        }

        return current;
    }

    // Text, or a number written where text was expected (`mainBranchName: 2024`).
    internal string? GetString(params ReadOnlySpan<string> path) =>
        Get(path) switch
        {
            string text => text,
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => null
        };

    // A number, or text that reads as one (`bitsPerRow: "16"`).
    internal double? GetNumber(params ReadOnlySpan<string> path) =>
        Get(path) switch
        {
            double number => number,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };

    // true or false, or the text "true" or "false".
    internal bool? GetBool(params ReadOnlySpan<string> path) =>
        Get(path) switch
        {
            bool flag => flag,
            string text when bool.TryParse(text.Trim(), out var parsed) => parsed,
            _ => null
        };

    internal Dictionary<string, object?>? GetMap(params ReadOnlySpan<string> path) =>
        Get(path) as Dictionary<string, object?>;
}
