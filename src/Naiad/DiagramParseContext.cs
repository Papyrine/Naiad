// What a diagram is parsed with. Parlot backs out of a rule that fails and keeps no record of how far it
// got, so a failed parse on its own says only that it failed. The rules that know they are giving up - a
// statement its line does not end after, a list of statements that stops short of the end of the input -
// note where, and the furthest of those places is where the failure is reported.
sealed class DiagramParseContext(string input) :
    ParseContext(new(input))
{
    int furthest;
    string? reason;

    // The diagram's accTitle and accDescr, which most grammars read only to step over. They are noted here
    // as they are read, and ParseDiagram hands them to the model. The last of each wins, as in Mermaid.
    internal string? AccessibilityTitle { get; private set; }
    internal string? AccessibilityDescription { get; private set; }

    // The shapes the grammars read these statements in, for Noting.
    internal static void Accessibility(ParseContext context, (string Keyword, TextSpan Text) line) =>
        Accessibility(context, line.Keyword, line.Text.Span);

    internal static void Accessibility(ParseContext context, (string Keyword, string Text) line) =>
        Accessibility(context, line.Keyword, line.Text);

    internal static void AccessibilityTitleLine(ParseContext context, TextSpan text) =>
        Accessibility(context, "accTitle", text.Span);

    internal static void AccessibilityDescriptionLine(ParseContext context, TextSpan text) =>
        Accessibility(context, "accDescr", text.Span);

    internal static void AccessibilityTitleLine(ParseContext context, string text) =>
        Accessibility(context, "accTitle", text);

    internal static void AccessibilityDescriptionLine(ParseContext context, string text) =>
        Accessibility(context, "accDescr", text);

    internal static void AccessibilityBlock(ParseContext context, TextSpan body) =>
        AccessibilityBlock(context, body.Span);

    internal static void AccessibilityBlock(ParseContext context, string body) =>
        AccessibilityBlock(context, body.AsSpan());

    // Notes `accTitle: x` or `accDescr: x`, given its keyword and the rest of its line, for whichever context
    // is in use. A rule that steps over other keywords too passes them through here, and they are ignored.
    internal static void Accessibility(ParseContext context, string keyword, CharSpan rest)
    {
        if (context is not DiagramParseContext diagram)
        {
            return;
        }

        var text = rest.Trim();
        if (text is [':', ..])
        {
            text = text[1..].TrimStart();
        }

        if (keyword.Equals("accTitle", StringComparison.OrdinalIgnoreCase))
        {
            diagram.AccessibilityTitle = text.ToString();
            return;
        }

        if (keyword.Equals("accDescr", StringComparison.OrdinalIgnoreCase))
        {
            diagram.AccessibilityDescription = text.ToString();
        }
    }

    // Notes the body of `accDescr { ... }`. As Mermaid reads it, each line loses its indentation, and the
    // blank lines go.
    internal static void AccessibilityBlock(ParseContext context, CharSpan body)
    {
        if (context is not DiagramParseContext diagram)
        {
            return;
        }

        var lines = new List<string>();
        foreach (var line in body.EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                lines.Add(trimmed.ToString());
            }
        }

        diagram.AccessibilityDescription = string.Join('\n', lines);
    }

    // Notes a place a rule gave up on behalf of whichever context is in use. Only a diagram's own context
    // keeps it: the shared parsers also run under a plain ParseContext.
    internal static void Failed(ParseContext context, int offset, string? why = null)
    {
        if (context is DiagramParseContext diagram)
        {
            diagram.Failed(offset, why);
        }
    }

    // A reason outranks a bare position at the same place, and anything further along outranks both.
    void Failed(int offset, string? why)
    {
        if (offset > furthest)
        {
            furthest = offset;
            reason = why;
            return;
        }

        if (offset == furthest)
        {
            reason ??= why;
        }
    }

    // Describes a failure at a known place, whatever the rules noted.
    internal string DescribeAt(int offset, string why)
    {
        furthest = offset;
        reason = why;
        return Describe();
    }

    internal string Describe()
    {
        var text = Scanner.Buffer;
        var offset = Math.Min(furthest, text.Length);

        // A rule that gave up in front of a gap had not got as far as skipping it. What it did not expect
        // is what comes after.
        while (offset < text.Length &&
               text[offset] is ' ' or '\t')
        {
            offset++;
        }

        var line = 1;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }

        return $"{reason ?? Unexpected(text.AsSpan(offset))} at line {line}, col {offset - lineStart + 1}";
    }

    // Names what was found instead: the rest of the line, cut short if it runs on.
    static string Unexpected(CharSpan rest)
    {
        if (rest.IsEmpty)
        {
            return "unexpected end of input";
        }

        var lineEnd = rest.IndexOfAny('\r', '\n');
        if (lineEnd >= 0)
        {
            rest = rest[..lineEnd];
        }

        rest = rest.TrimEnd();
        if (rest.IsEmpty)
        {
            return "unexpected end of line";
        }

        const int limit = 40;
        if (rest.Length > limit)
        {
            return $"unexpected '{rest[..limit].TrimEnd()}...'";
        }

        return $"unexpected '{rest}'";
    }
}
