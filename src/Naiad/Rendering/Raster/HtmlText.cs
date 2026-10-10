/// <summary>
/// Pulls the visible text out of the little XHTML fragments Naiad emits inside
/// <c>&lt;foreignObject&gt;</c> labels (<c>&lt;div&gt;&lt;span&gt;&lt;p&gt;…&lt;/p&gt;&lt;/span&gt;&lt;/div&gt;</c>).
/// <c>&lt;br&gt;</c> becomes a line break, all other tags are stripped and the common entities are
/// decoded, so the surfaces receive plain text lines to lay out.
/// </summary>
static partial class HtmlText
{
    public static List<string> ExtractLines(string html)
    {
        // <br> → newline, then drop every remaining tag.
        var withBreaks = BreakRegex().Replace(html, "\n");
        var stripped = TagRegex().Replace(withBreaks, "");
        var decoded = Decode(stripped);

        var lines = new List<string>();
        foreach (var line in decoded.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                lines.Add(trimmed);
            }
        }

        return lines;
    }

    /// <summary>
    /// The text colour a label sets on itself, as in <c>&lt;p style="color:#fff"&gt;</c> - what a flowchart
    /// <c>style</c> or <c>classDef</c> with a <c>color</c> produces - or null when it sets none.
    /// </summary>
    public static string? InlineColor(string html)
    {
        var match = ColorRegex().Match(html);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return null;
    }

    static string Decode(string text)
    {
        if (!text.Contains('&'))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '&')
            {
                var semicolon = text.IndexOf(';', i);
                if (semicolon > i)
                {
                    var entity = text[(i + 1)..semicolon];
                    if (Entity(entity) is { } resolved)
                    {
                        builder.Append(resolved);
                        i = semicolon + 1;
                        continue;
                    }
                }
            }

            builder.Append(text[i]);
            i++;
        }

        return builder.ToString();
    }

    static string? Entity(string entity)
    {
        switch (entity)
        {
            case "amp":
                return "&";
            case "lt":
                return "<";
            case "gt":
                return ">";
            case "quot":
                return "\"";
            case "apos":
                return "'";
            case "nbsp":
                return " ";
        }

        if (entity.StartsWith('#'))
        {
            var numeric = entity[1..];
            var parsed = numeric.StartsWith('x') || numeric.StartsWith('X')
                ? int.TryParse(numeric[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : -1
                : int.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec) ? dec : -1;
            if (parsed >= 0)
            {
                return char.ConvertFromUtf32(parsed);
            }
        }

        return null;
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    // `color:` inside a style attribute, but not the tail of `background-color:`.
    [GeneratedRegex("""style\s*=\s*["'][^"']*?(?<![-\w])color\s*:\s*([^;"']+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ColorRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}
