namespace Naiad;

/// <summary>
/// Mermaid lets a label force a line break with <c>&lt;br&gt;</c>, <c>&lt;br/&gt;</c> or
/// <c>&lt;br /&gt;</c>. Renderers measure and draw labels line by line, so they share this one reading of
/// where a label breaks.
/// </summary>
static partial class LabelLines
{
    /// <summary>The label's lines, each trimmed. A label without a break is returned as its only line.</summary>
    public static string[] Split(string text)
    {
        if (!HasBreak(text))
        {
            return [text];
        }

        var lines = BreakRegex().Split(text);
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].Trim();
        }

        return lines;
    }

    public static int Count(string? text)
    {
        if (text is null ||
            !HasBreak(text))
        {
            return 1;
        }

        return BreakRegex().Count(text) + 1;
    }

    /// <summary>The character count of the longest line, which is what a label's width is measured from.</summary>
    public static int WidestLength(string text)
    {
        if (!HasBreak(text))
        {
            return text.Length;
        }

        var widest = 0;
        foreach (var line in Split(text))
        {
            widest = Math.Max(widest, line.Length);
        }

        return widest;
    }

    /// <summary>The label on a single line, for places that have room for only one.</summary>
    public static string Flatten(string text)
    {
        if (!HasBreak(text))
        {
            return text;
        }

        return string.Join(' ', Split(text));
    }

    public static bool HasBreak(string text) =>
        text.Contains('<') &&
        BreakRegex().IsMatch(text);

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();
}
