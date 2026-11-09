/// <summary>
/// Mermaid's entity codes, <c>#quot;</c>, <c>#9829;</c> and <c>#40;</c>: how a label holds a character the
/// syntax reserves, or one that is awkward to type. A name HTML knows or a decimal code point is replaced by
/// its character; anything else is left as written.
/// </summary>
static partial class EntityCodes
{
    public static string Decode(string text)
    {
        if (!text.Contains('#'))
        {
            return text;
        }

        return Pattern().Replace(
            text,
            static _ =>
            {
                var name = _.Groups[1].Value;
                if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
                {
                    // Not 0, and not a surrogate, which is half of a pair and no character by itself.
                    if (code > 0 && Rune.IsValid(code))
                    {
                        return char.ConvertFromUtf32(code);
                    }

                    return _.Value;
                }

                var decoded = WebUtility.HtmlDecode($"&{name};");
                if (decoded == $"&{name};")
                {
                    return _.Value;
                }

                return decoded;
            });
    }

    [GeneratedRegex(@"#(\w+);")]
    private static partial Regex Pattern();
}
