static class SvgXml
{
    // The characters XML 1.0 does not allow anywhere, even as character references: all of C0 but tab, line
    // feed and carriage return, and U+FFFE and U+FFFF. Half of a surrogate pair is not a character either;
    // see AllowedAt.
    static bool IsDisallowed(char c) =>
        c < ' ' && c is not ('\t' or '\n' or '\r') ||
        c is '￾' or '￿';

    // Every character that may have to be dropped: the disallowed ones, and the surrogates, which are kept
    // only in whole pairs.
    static readonly string suspectChars =
        new(
            Enumerable.Range(0, ' ')
                .Select(_ => (char) _)
                .Where(IsDisallowed)
                .Concat(['￾', '￿'])
                .Concat(Enumerable.Range(0xD800, 0x800).Select(_ => (char) _))
                .ToArray());

    static readonly SearchValues<char> suspects = SearchValues.Create(suspectChars);

    static readonly SearchValues<char> attributeSpecials = SearchValues.Create("&<'" + suspectChars);

    // How many characters from `index` XML allows: none for a disallowed one or half a surrogate pair, two for
    // a whole pair, and otherwise one.
    static int AllowedAt(CharSpan text, int index)
    {
        var c = text[index];
        if (char.IsHighSurrogate(c))
        {
            if (index + 1 < text.Length &&
                char.IsLowSurrogate(text[index + 1]))
            {
                return 2;
            }

            return 0;
        }

        if (char.IsLowSurrogate(c) ||
            IsDisallowed(c))
        {
            return 0;
        }

        return 1;
    }

    /// <summary>
    /// Appends markup as it is, but for the characters XML does not allow, which are dropped. Markup built
    /// from user text (an HTML label) has that text encoded, but HTML encoding leaves those characters as
    /// they are, and a document holding one is not well-formed.
    /// </summary>
    public static void AppendMarkup(StringBuilder builder, string markup)
    {
        var rest = markup.AsSpan();
        var index = rest.IndexOfAny(suspects);
        if (index < 0)
        {
            builder.Append(markup);
            return;
        }

        while (index >= 0)
        {
            builder.Append(rest[..index]);
            var allowed = AllowedAt(rest, index);
            builder.Append(rest.Slice(index, allowed));
            rest = rest[(index + Math.Max(allowed, 1))..];
            index = rest.IndexOfAny(suspects);
        }

        builder.Append(rest);
    }

    /// <summary>
    /// Appends <c> name='value'</c>, escaping the value. Attribute values carry user text (colours and
    /// whole declarations from <c>style</c>/<c>classDef</c>/<c>linkStyle</c>, class names, ids), so an
    /// unescaped quote would end the attribute and let the rest of the value inject attributes or
    /// elements. The attributes are single-quoted, so only <c>&amp;</c>, <c>&lt;</c> and <c>'</c> need
    /// escaping; <c>"</c> and <c>&gt;</c> are left as they are.
    /// </summary>
    public static void AppendAttribute(this StringBuilder builder, string name, string value)
    {
        builder.Append(' ');
        builder.Append(name);
        builder.Append("='");
        AppendEscaped(builder, value, inAttribute: true);
        builder.Append('\'');
    }

    /// <summary>The value escaped for a single-quoted attribute (see <see cref="AppendAttribute"/>).</summary>
    public static string EscapeAttribute(string value)
    {
        if (value.AsSpan().IndexOfAny(attributeSpecials) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);
        AppendEscaped(builder, value, inAttribute: true);
        return builder.ToString();
    }

    /// <summary>Appends text content escaped for an element body.</summary>
    public static void AppendText(StringBuilder builder, string text) =>
        AppendEscaped(builder, text, inAttribute: false);

    static void AppendEscaped(StringBuilder builder, string text, bool inAttribute)
    {
        // Fast path: if no escaping needed, append directly without scanning twice.
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // A whole surrogate pair is one character and is kept; half of one is none, and is dropped.
            if (char.IsSurrogate(c) &&
                AllowedAt(text, i) == 2)
            {
                i++;
                continue;
            }

            var replacement = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '\'' => "&apos;",
                '>' when !inAttribute => "&gt;",
                '"' when !inAttribute => "&quot;",
                // Dropped: a document holding one is not well-formed, and no XML parser reads it.
                _ when IsDisallowed(c) || char.IsSurrogate(c) => "",
                _ => null
            };

            if (replacement is null)
            {
                continue;
            }

            if (i > start)
            {
                builder.Append(text, start, i - start);
            }

            builder.Append(replacement);
            start = i + 1;
        }

        if (start == 0)
        {
            builder.Append(text);
        }
        else if (start < text.Length)
        {
            builder.Append(text, start, text.Length - start);
        }
    }
}
