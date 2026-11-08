namespace Naiad;

// Indents serialised SVG markup, one element per line. Works on the markup rather than re-parsing it so
// attribute order, quoting and number formatting stay exactly as emitted. Elements holding text (labels,
// <style>) and <foreignObject> (HTML labels, where added whitespace would change the layout) are kept
// verbatim on a single line.
static class SvgPrettyPrinter
{
    const string indentUnit = "  ";
    static readonly SearchValues<char> tagNameEnd = SearchValues.Create(" \t\r\n/>");

    public static string Format(string markup)
    {
        var builder = new StringBuilder(markup.Length + markup.Length / 4);
        var depth = 0;
        var position = 0;
        // True straight after an opening tag, so an empty element closes on the same line.
        var afterOpen = false;

        while (position < markup.Length)
        {
            var tagStart = markup.IndexOf('<', position);
            if (tagStart < 0)
            {
                AppendText(builder, markup.AsSpan(position), depth);
                break;
            }

            if (tagStart > position)
            {
                if (AppendText(builder, markup.AsSpan(position, tagStart - position), depth))
                {
                    afterOpen = false;
                }
            }

            var tagEnd = FindTagEnd(markup, tagStart);
            var tag = markup.AsSpan(tagStart, tagEnd - tagStart);

            if (tag.StartsWith("</"))
            {
                depth = Math.Max(0, depth - 1);
                if (!afterOpen)
                {
                    NewLine(builder, depth);
                }

                builder.Append(tag);
                afterOpen = false;
            }
            else if (tag.StartsWith("<!") || tag.StartsWith("<?") || tag.EndsWith("/>"))
            {
                NewLine(builder, depth);
                builder.Append(tag);
                afterOpen = false;
            }
            else
            {
                var name = TagName(tag);
                NewLine(builder, depth);

                if (name is "foreignObject" || HasTextContent(markup, tagEnd))
                {
                    var elementEnd = FindElementEnd(markup, tagEnd, name);
                    builder.Append(markup, tagStart, elementEnd - tagStart);
                    tagEnd = elementEnd;
                    afterOpen = false;
                }
                else
                {
                    builder.Append(tag);
                    depth++;
                    afterOpen = true;
                }
            }

            position = tagEnd;
        }

        return builder.ToString();
    }

    // Whitespace between elements is dropped (the indentation replaces it); anything else is kept on its own line.
    static bool AppendText(StringBuilder builder, CharSpan text, int depth)
    {
        var trimmed = text.Trim();
        if (trimmed.IsEmpty)
        {
            return false;
        }

        NewLine(builder, depth);
        builder.Append(trimmed);
        return true;
    }

    static void NewLine(StringBuilder builder, int depth)
    {
        if (builder.Length == 0)
        {
            return;
        }

        builder.Append('\n');
        for (var i = 0; i < depth; i++)
        {
            builder.Append(indentUnit);
        }
    }

    // Index just past the '>' closing the tag at `start`, skipping any '>' inside quoted attribute values.
    static int FindTagEnd(string markup, int start)
    {
        if (string.CompareOrdinal(markup, start, "<!--", 0, 4) == 0)
        {
            return EndOf(markup, start, "-->");
        }

        if (string.CompareOrdinal(markup, start, "<![CDATA[", 0, 9) == 0)
        {
            return EndOf(markup, start, "]]>");
        }

        char? quote = null;
        for (var i = start + 1; i < markup.Length; i++)
        {
            var ch = markup[i];
            if (quote is not null)
            {
                if (ch == quote)
                {
                    quote = null;
                }
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == '>')
            {
                return i + 1;
            }
        }

        return markup.Length;
    }

    static int EndOf(string markup, int start, string terminator)
    {
        var index = markup.IndexOf(terminator, start, StringComparison.Ordinal);
        return index < 0 ? markup.Length : index + terminator.Length;
    }

    static string TagName(CharSpan tag)
    {
        var name = tag.TrimStart('<').TrimStart('/');
        var end = name.IndexOfAny(tagNameEnd);
        return (end < 0 ? name : name[..end]).ToString();
    }

    // Whether non-whitespace text follows the opening tag ending at `position` (before the next tag).
    static bool HasTextContent(string markup, int position)
    {
        for (var i = position; i < markup.Length; i++)
        {
            var ch = markup[i];
            if (ch == '<')
            {
                // A CDATA section is text too.
                return string.CompareOrdinal(markup, i, "<![CDATA[", 0, 9) == 0;
            }

            if (!char.IsWhiteSpace(ch))
            {
                return true;
            }
        }

        return false;
    }

    // Index just past the closing tag matching an element named `name` whose opening tag ends at `position`.
    static int FindElementEnd(string markup, int position, string name)
    {
        var depth = 1;
        while (position < markup.Length)
        {
            var tagStart = markup.IndexOf('<', position);
            if (tagStart < 0)
            {
                break;
            }

            var tagEnd = FindTagEnd(markup, tagStart);
            var tag = markup.AsSpan(tagStart, tagEnd - tagStart);
            if (!tag.StartsWith("<!") && !tag.StartsWith("<?") && TagName(tag) == name)
            {
                if (tag.StartsWith("</"))
                {
                    depth--;
                    if (depth == 0)
                    {
                        return tagEnd;
                    }
                }
                else if (!tag.EndsWith("/>"))
                {
                    depth++;
                }
            }

            position = tagEnd;
        }

        return markup.Length;
    }
}
