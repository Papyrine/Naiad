namespace Naiad;

// Lays out Mermaid source: see Mermaid.Format.
static class MermaidFormatter
{
    const string indent = "    ";

    // Diagrams laid out by their blocks. The rest (mindmap, kanban, packet, sankey, architecture,
    // treemap, block, …) read structure from indentation, and are left as written.
    static readonly HashSet<DiagramType> layouts =
    [
        DiagramType.Flowchart, DiagramType.Sequence, DiagramType.Class, DiagramType.State,
        DiagramType.EntityRelationship, DiagramType.Gantt, DiagramType.UserJourney, DiagramType.Timeline,
        DiagramType.GitGraph, DiagramType.Pie, DiagramType.Requirement, DiagramType.Quadrant,
        DiagramType.XYChart, DiagramType.C4Context, DiagramType.C4Container, DiagramType.C4Component,
        DiagramType.C4Dynamic, DiagramType.C4Deployment
    ];

    // Diagrams whose `section` lines head the lines after them.
    static readonly HashSet<DiagramType> sectioned = [DiagramType.Gantt, DiagramType.UserJourney, DiagramType.Timeline];

    public static string Format(string text)
    {
        var formatted = Layout(text);
        if (formatted == text)
        {
            return formatted;
        }

        // Only white space moves, but a grammar may read it (a mis-closed block, a label left open). Text that
        // parsed is not handed back broken: it is returned as written instead.
        if (!Mermaid.Parses(formatted) &&
            Mermaid.Parses(text))
        {
            return text;
        }

        return formatted;
    }

    // The layout alone, without Format's check that it still parses.
    internal static string Layout(string text)
    {
        // Found the way rendering finds it, so what is formatted is what would render.
        if (!Mermaid.TryFindDiagram(text, out var type, out var start) ||
            !layouts.Contains(type))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + text.Length / 4);

        // Everything before the header's line: front matter as written, then directives and comments at the
        // margin. Lines inside a multi-line %%{init}%% block keep their indentation.
        var headerLine = text.AsSpan(0, start).LastIndexOfAny('\r', '\n') + 1;
        // Front matter can only open on the first line that is not blank.
        var frontMatter = FrontMatter.Possible;
        foreach (var raw in text.AsSpan(0, headerLine).EnumerateLines())
        {
            var line = Clean(raw);
            if (frontMatter == FrontMatter.Inside)
            {
                builder.Append(raw.TrimEnd()).Append('\n');
                if (line is "---")
                {
                    frontMatter = FrontMatter.Done;
                }

                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (frontMatter == FrontMatter.Possible && line is "---")
            {
                builder.Append("---\n");
                frontMatter = FrontMatter.Inside;
                continue;
            }

            frontMatter = FrontMatter.Done;
            if (line.StartsWith("%%"))
            {
                builder.Append(line).Append('\n');
            }
            else
            {
                builder.Append(raw.TrimEnd()).Append('\n');
            }
        }

        var lines = text.AsSpan(headerLine).EnumerateLines();
        lines.MoveNext();
        builder.Append(Clean(lines.Current)).Append('\n');

        var isSequence = type == DiagramType.Sequence;
        var isFlowchart = type == DiagramType.Flowchart;
        var isState = type == DiagramType.State;
        var hasSections = sectioned.Contains(type);
        var depth = 1;
        var inSection = false;
        var blank = false;
        // Inside a quoted string left open at the end of a line (a multi-line label): copied as written.
        var inString = false;

        while (lines.MoveNext())
        {
            var raw = lines.Current;
            if (inString)
            {
                builder.Append(raw.TrimEnd()).Append('\n');
                inString = OpensString(raw, true);
                continue;
            }

            var line = raw.Trim();
            if (line.Length == 0)
            {
                blank = true;
                continue;
            }

            if (blank)
            {
                builder.Append('\n');
                blank = false;
            }

            var word = FirstWord(line);
            var lineDepth = depth;
            var comment = line.StartsWith("%%");
            if (!comment)
            {
                if (word is "end" || line.StartsWith('}'))
                {
                    depth = Math.Max(1, depth - 1);
                    lineDepth = depth;
                }
                else if ((isSequence && IsSequenceDivider(word)) ||
                         (isState && line is "--"))
                {
                    lineDepth = Math.Max(1, depth - 1);
                }
                else if (hasSections && word.Equals("section", StringComparison.OrdinalIgnoreCase))
                {
                    if (inSection)
                    {
                        depth--;
                    }

                    lineDepth = depth;
                    inSection = true;
                    depth++;
                }
            }

            for (var i = 0; i < lineDepth; i++)
            {
                builder.Append(indent);
            }

            builder.Append(line).Append('\n');
            if (comment)
            {
                continue;
            }

            if (line.EndsWith('{') ||
                (isSequence && IsSequenceBlock(word)) ||
                (isFlowchart && word is "subgraph"))
            {
                depth++;
            }

            inString = OpensString(line, false);
        }

        return builder.ToString();
    }

    enum FrontMatter
    {
        Possible,
        Inside,
        Done
    }

    // A line without surrounding white space or a byte order mark.
    static CharSpan Clean(CharSpan line) =>
        line.TrimStart((char) 0xFEFF).Trim();

    // Sequence diagram blocks, closed by `end`.
    static bool IsSequenceBlock(CharSpan word) =>
        word is "loop" or "alt" or "opt" or "par" or "par_over" or "critical" or "break" or "rect" or "box";

    // The lines that divide a sequence diagram block.
    static bool IsSequenceDivider(CharSpan word) =>
        word is "else" or "and" or "option";

    // The statement keyword: up to the first space, ';' or ':'.
    static CharSpan FirstWord(CharSpan line)
    {
        var end = line.IndexOfAny(' ', '\t', ';');
        if (end >= 0)
        {
            line = line[..end];
        }

        var colon = line.IndexOf(':');
        if (colon > 0)
        {
            return line[..colon];
        }

        return line;
    }

    // Whether a double-quoted string is still open at the end of the line, given whether it started open.
    static bool OpensString(CharSpan line, bool open) =>
        open ^ (line.Count('"') % 2 == 1);
}
