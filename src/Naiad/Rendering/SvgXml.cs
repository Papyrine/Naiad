namespace Naiad;

static class SvgXml
{
    // The control characters XML 1.0 does not allow anywhere, even as character references: all of C0
    // but tab, line feed and carriage return.
    static bool IsDisallowed(char c) =>
        c < ' ' && c is not ('\t' or '\n' or '\r');

    static readonly SearchValues<char> attributeSpecials = SearchValues.Create(
        "&<'" + new string(Enumerable.Range(0, ' ').Select(_ => (char) _).Where(IsDisallowed).ToArray()));

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
            var replacement = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '\'' => "&apos;",
                '>' when !inAttribute => "&gt;",
                '"' when !inAttribute => "&quot;",
                // Dropped: a document holding one is not well-formed, and no XML parser reads it.
                _ when IsDisallowed(c) => "",
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
