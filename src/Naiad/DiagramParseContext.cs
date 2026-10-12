namespace Naiad;

// What a diagram is parsed with. Parlot backs out of a rule that fails and keeps no record of how far it
// got, so a failed parse on its own says only that it failed. The rules that know they are giving up - a
// statement its line does not end after, a list of statements that stops short of the end of the input -
// note where, and the furthest of those places is where the failure is reported.
sealed class DiagramParseContext(string input) :
    ParseContext(new Scanner(input))
{
    int furthest;
    string? reason;

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
