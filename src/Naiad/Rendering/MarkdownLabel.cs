namespace Naiad;

/// <summary>
/// Mermaid's markdown strings: a label wrapped in backticks, as in <c>A["`This **is** _markdown_`"]</c>.
/// Inside one, <c>**text**</c> is bold, <c>*text*</c> and <c>_text_</c> are italic, and a line break in
/// the source is a line break in the label.
/// </summary>
static partial class MarkdownLabel
{
    public readonly record struct Run(string Text, bool Bold, bool Italic);

    public static bool IsMarkdown(string? label)
    {
        if (label is null)
        {
            return false;
        }

        var text = label.AsSpan().Trim();
        return text.Length >= 2 &&
               text[0] == '`' &&
               text[^1] == '`';
    }

    /// <summary>The label without its markers, its lines joined by <c>&lt;br/&gt;</c>: what it measures as.</summary>
    public static string Plain(string label) =>
        string.Join(
            "<br/>",
            Lines(label).Select(line => string.Concat(Runs(line).Select(_ => _.Text))));

    /// <summary>The label as HTML, with <c>&lt;strong&gt;</c> and <c>&lt;em&gt;</c> where it marked them.</summary>
    public static string Html(string label) =>
        string.Join("<br/>", Lines(label).Select(LineHtml));

    // The text between the backticks, a line at a time. Indentation is how the source was laid out, not
    // part of the label.
    static IEnumerable<string> Lines(string label)
    {
        var inner = label.Trim()[1..^1];
        return inner
            .Split('\n')
            .SelectMany(LabelLines.Split)
            .Select(_ => _.Trim())
            .Where(_ => _.Length > 0);
    }

    static string LineHtml(string line)
    {
        var html = new StringBuilder();
        foreach (var run in Runs(line))
        {
            var text = WebUtility.HtmlEncode(run.Text);
            if (run.Italic)
            {
                text = $"<em>{text}</em>";
            }

            if (run.Bold)
            {
                text = $"<strong>{text}</strong>";
            }

            html.Append(text);
        }

        return html.ToString();
    }

    /// <summary>A line split wherever its weight or slant changes.</summary>
    public static List<Run> Runs(string line)
    {
        var runs = new List<Run>();
        AddRuns(runs, line, bold: false);
        return runs;
    }

    static void AddRuns(List<Run> runs, string text, bool bold)
    {
        var last = 0;
        foreach (Match match in MarkerRegex().Matches(text))
        {
            if (match.Index > last)
            {
                runs.Add(new(text[last..match.Index], bold, false));
            }

            if (match.Groups["bold"].Success)
            {
                // Bold may hold italics: **bold and _both_**
                AddRuns(runs, match.Groups["bold"].Value, bold: true);
            }
            else
            {
                var italic = match.Groups["star"].Success ? match.Groups["star"].Value : match.Groups["under"].Value;
                runs.Add(new(italic, bold, true));
            }

            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            runs.Add(new(text[last..], bold, false));
        }
    }

    // An underscore only opens or closes italics at the edge of a word, so snake_case_names are left alone.
    [GeneratedRegex(@"\*\*(?<bold>.+?)\*\*|\*(?<star>[^*]+?)\*|(?<![\p{L}\p{N}])_(?<under>[^_]+?)_(?![\p{L}\p{N}])")]
    private static partial Regex MarkerRegex();
}
