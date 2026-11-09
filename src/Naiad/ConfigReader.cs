namespace Naiad;

/// <summary>
/// Reads the configuration Mermaid takes ahead of a diagram: the YAML of a front matter block and the
/// JSON of a <c>%%{init: ...}%%</c> directive. Values come back as maps (string keys, compared
/// ordinally), lists, text, numbers (as <see cref="double"/>), <see langword="true"/>,
/// <see langword="false"/> and <see langword="null"/>. Both readers are forgiving: configuration only
/// refines how a diagram is drawn, so what they cannot make sense of is passed over rather than
/// reported. The YAML reader covers what front matter uses: nested mappings by indentation, lists,
/// plain and quoted scalars, flow collections and comments.
/// </summary>
static class ConfigReader
{
    internal static Dictionary<string, object?> NewMap() =>
        new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the JSON of a directive. As in Mermaid, which swaps every single quote for a double one
    /// before parsing, either quote may delimit a string. Keys may also go unquoted, and a trailing comma
    /// is allowed.
    /// </summary>
    internal static object? ReadJson(string text)
    {
        var reader = new FlowReader(text);
        reader.TryReadValue(out var value);
        return value;
    }

    /// <summary>
    /// Reads a YAML document whose top level is a mapping, or <see langword="null"/> when it is not one.
    /// </summary>
    internal static Dictionary<string, object?>? ReadYaml(string text)
    {
        var lines = new List<YamlLine>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var indent = 0;
            while (indent < line.Length &&
                   line[indent] is ' ' or '\t')
            {
                indent++;
            }

            var content = line[indent..].TrimEnd();
            if (content.Length == 0 ||
                content[0] == '#')
            {
                continue;
            }

            lines.Add(new(indent, content));
        }

        var index = 0;
        return ReadBlock(lines, ref index, 0) as Dictionary<string, object?>;
    }

    // A line of the document, without its indentation. Blank and comment lines are left out.
    sealed record YamlLine(int Indent, string Text);

    // The block starting at the current line, if that line is indented by at least minIndent.
    static object? ReadBlock(List<YamlLine> lines, ref int index, int minIndent)
    {
        if (index >= lines.Count ||
            lines[index].Indent < minIndent)
        {
            return null;
        }

        var indent = lines[index].Indent;
        if (IsListItem(lines[index].Text))
        {
            return ReadList(lines, ref index, indent);
        }

        return ReadMap(lines, ref index, indent);
    }

    static Dictionary<string, object?> ReadMap(List<YamlLine> lines, ref int index, int indent)
    {
        var map = NewMap();
        while (index < lines.Count &&
               lines[index].Indent >= indent)
        {
            var line = lines[index];

            // Deeper lines nothing claimed (the rest of a plain scalar run over several lines) are passed
            // over, and a list item at this level is not part of a mapping.
            if (line.Indent > indent)
            {
                index++;
                continue;
            }

            if (IsListItem(line.Text))
            {
                break;
            }

            index++;
            if (TrySplitKey(line.Text, out var key, out var rest))
            {
                map[key] = ReadValue(rest, lines, ref index, indent);
            }
        }

        return map;
    }

    static List<object?> ReadList(List<YamlLine> lines, ref int index, int indent)
    {
        var list = new List<object?>();
        while (index < lines.Count &&
               lines[index].Indent == indent &&
               IsListItem(lines[index].Text))
        {
            var text = lines[index].Text;
            var content = text[1..].TrimStart();
            if (content.Length == 0)
            {
                index++;
                list.Add(ReadBlock(lines, ref index, indent + 1));
                continue;
            }

            // `- key: value` opens a mapping whose first entry is on the item's own line, indented as far
            // as the content after the dash.
            if (content[0] is not ('"' or '\'' or '[' or '{') &&
                TrySplitKey(content, out _, out _))
            {
                lines[index] = new(indent + (text.Length - content.Length), content);
                list.Add(ReadMap(lines, ref index, lines[index].Indent));
                continue;
            }

            index++;
            list.Add(ReadValue(content, lines, ref index, indent));
        }

        return list;
    }

    // The value after `key:` or `- `, which may continue on the lines below.
    static object? ReadValue(string rest, List<YamlLine> lines, ref int index, int indent)
    {
        rest = StripComment(rest);
        if (rest.Length == 0)
        {
            if (index < lines.Count &&
                lines[index].Indent > indent)
            {
                return ReadBlock(lines, ref index, indent + 1);
            }

            // A list may sit at the same indentation as the key that holds it.
            if (index < lines.Count &&
                lines[index].Indent == indent &&
                IsListItem(lines[index].Text))
            {
                return ReadList(lines, ref index, indent);
            }

            return null;
        }

        if (rest[0] is '[' or '{')
        {
            // A flow collection may run on over the deeper lines below.
            var flow = new StringBuilder(rest);
            while (!IsBalanced(flow.ToString()) &&
                   index < lines.Count &&
                   lines[index].Indent > indent)
            {
                flow.Append(' ').Append(StripComment(lines[index].Text));
                index++;
            }

            return ReadJson(flow.ToString());
        }

        if (rest[0] is '|' or '>')
        {
            var block = new List<string>();
            while (index < lines.Count &&
                   lines[index].Indent > indent)
            {
                block.Add(lines[index].Text);
                index++;
            }

            var separator = "\n";
            if (rest[0] == '>')
            {
                separator = " ";
            }

            return string.Join(separator, block);
        }

        if (rest[0] is '"' or '\'')
        {
            var reader = new FlowReader(rest);
            reader.TryReadValue(out var quoted);
            return quoted;
        }

        return ResolveScalar(rest);
    }

    static bool IsListItem(string text) =>
        text == "-" ||
        text.StartsWith("- ", StringComparison.Ordinal) ||
        text.StartsWith("-\t", StringComparison.Ordinal);

    // Splits `key: value` at the first colon followed by white space or the end of the line, outside any
    // quotes. The key may be quoted.
    static bool TrySplitKey(string text, [NotNullWhen(true)] out string? key, out string rest)
    {
        rest = "";
        if (text[0] is '"' or '\'')
        {
            var reader = new FlowReader(text);
            var quoted = reader.ReadQuoted();
            var after = text[reader.Position..].TrimStart();
            if (after.Length > 0 &&
                after[0] == ':')
            {
                key = quoted;
                rest = after[1..].Trim();
                return true;
            }

            key = null;
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ':' &&
                (i + 1 == text.Length || text[i + 1] is ' ' or '\t'))
            {
                key = text[..i].Trim();
                rest = text[(i + 1)..].Trim();
                return true;
            }
        }

        key = null;
        return false;
    }

    // Drops a trailing `# comment`: a hash at the start or after white space, outside quotes.
    static string StripComment(string text)
    {
        var quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                // Only a quote that opens a scalar starts a quoted run; an apostrophe inside a plain
                // scalar (`it's`) does not.
                if (i == 0 ||
                    text[i - 1] is ' ' or '\t' or '[' or '{' or ',' or ':')
                {
                    quote = c;
                }

                continue;
            }

            if (c == '#' &&
                (i == 0 || text[i - 1] is ' ' or '\t'))
            {
                return text[..i].TrimEnd();
            }
        }

        return text.Trim();
    }

    // Whether every bracket opened outside quotes is closed again.
    static bool IsBalanced(string text)
    {
        var depth = 0;
        var quote = '\0';
        foreach (var c in text)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '[' or '{':
                    depth++;
                    break;
                case ']' or '}':
                    depth--;
                    break;
            }
        }

        return depth <= 0;
    }

    /// <summary>
    /// What an unquoted scalar stands for, under the rules of YAML's JSON schema that Mermaid reads front
    /// matter with: <c>true</c> and <c>false</c>, <c>null</c> (or <c>~</c>, or nothing), a number, or
    /// else the text itself.
    /// </summary>
    internal static object? ResolveScalar(string text)
    {
        text = text.Trim();
        switch (text)
        {
            case "true" or "True" or "TRUE":
                return true;
            case "false" or "False" or "FALSE":
                return false;
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
        }

        if (IsNumber(text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return text;
    }

    // A decimal number as JSON writes one, with an optional sign, fraction and exponent.
    static bool IsNumber(string text)
    {
        var i = 0;
        if (i < text.Length &&
            text[i] is '-' or '+')
        {
            i++;
        }

        var digits = 0;
        while (i < text.Length &&
               char.IsAsciiDigit(text[i]))
        {
            i++;
            digits++;
        }

        if (i < text.Length &&
            text[i] == '.')
        {
            i++;
            while (i < text.Length &&
                   char.IsAsciiDigit(text[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (i < text.Length &&
            text[i] is 'e' or 'E')
        {
            i++;
            if (i < text.Length &&
                text[i] is '-' or '+')
            {
                i++;
            }

            var exponent = 0;
            while (i < text.Length &&
                   char.IsAsciiDigit(text[i]))
            {
                i++;
                exponent++;
            }

            if (exponent == 0)
            {
                return false;
            }
        }

        return i == text.Length;
    }

    /// <summary>
    /// Reads JSON, and YAML's flow collections, which are a superset of it as far as configuration goes:
    /// strings may be single-quoted and keys and values may go unquoted.
    /// </summary>
    sealed class FlowReader(string text)
    {
        public int Position { get; private set; }

        public bool TryReadValue(out object? value)
        {
            SkipSpace();
            if (Position >= text.Length)
            {
                value = null;
                return false;
            }

            switch (text[Position])
            {
                case '{':
                    value = ReadMap();
                    return true;
                case '[':
                    value = ReadList();
                    return true;
                case '"' or '\'':
                    value = ReadQuoted();
                    return true;
            }

            var start = Position;
            var bare = ReadBare(stopAtColon: false);
            if (Position == start)
            {
                value = null;
                return false;
            }

            value = ResolveScalar(bare);
            return true;
        }

        Dictionary<string, object?> ReadMap()
        {
            Position++;
            var map = NewMap();
            while (true)
            {
                SkipSpace();
                if (Position >= text.Length)
                {
                    // Never closed: keep what was read.
                    return map;
                }

                var c = text[Position];
                if (c == '}')
                {
                    Position++;
                    return map;
                }

                if (c == ',')
                {
                    Position++;
                    continue;
                }

                var start = Position;
                string key;
                if (c is '"' or '\'')
                {
                    key = ReadQuoted();
                }
                else
                {
                    key = ReadBare(stopAtColon: true).Trim();
                }

                SkipSpace();
                if (Position < text.Length &&
                    text[Position] == ':')
                {
                    Position++;
                    TryReadValue(out var value);
                    map[key] = value;
                    continue;
                }

                // Not a key and a value. Move on, at least by a character, so nothing is read forever.
                if (Position == start)
                {
                    Position++;
                }
            }
        }

        List<object?> ReadList()
        {
            Position++;
            var list = new List<object?>();
            while (true)
            {
                SkipSpace();
                if (Position >= text.Length)
                {
                    return list;
                }

                var c = text[Position];
                if (c == ']')
                {
                    Position++;
                    return list;
                }

                if (c == ',')
                {
                    Position++;
                    continue;
                }

                var start = Position;
                if (TryReadValue(out var value))
                {
                    list.Add(value);
                }

                if (Position == start)
                {
                    Position++;
                }
            }
        }

        // A quoted string, starting at its opening quote. Double quotes take JSON's backslash escapes;
        // single quotes take YAML's doubled quote.
        public string ReadQuoted()
        {
            var quote = text[Position];
            Position++;
            var builder = new StringBuilder();
            while (Position < text.Length)
            {
                var c = text[Position];
                Position++;
                if (c == quote)
                {
                    if (quote == '\'' &&
                        Position < text.Length &&
                        text[Position] == '\'')
                    {
                        builder.Append('\'');
                        Position++;
                        continue;
                    }

                    return builder.ToString();
                }

                if (c == '\\' &&
                    quote == '"' &&
                    Position < text.Length)
                {
                    var escaped = text[Position];
                    Position++;
                    switch (escaped)
                    {
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'u' when Position + 4 <= text.Length &&
                                      int.TryParse(text.AsSpan(Position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                            builder.Append((char) code);
                            Position += 4;
                            break;
                        default:
                            builder.Append(escaped);
                            break;
                    }

                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        // An unquoted run, up to what ends a value in a flow collection.
        string ReadBare(bool stopAtColon)
        {
            var start = Position;
            while (Position < text.Length)
            {
                var c = text[Position];
                if (c is ',' or '}' or ']' or '\n' ||
                    (stopAtColon && c == ':'))
                {
                    break;
                }

                Position++;
            }

            return text[start..Position];
        }

        void SkipSpace()
        {
            while (Position < text.Length &&
                   char.IsWhiteSpace(text[Position]))
            {
                Position++;
            }
        }
    }
}
