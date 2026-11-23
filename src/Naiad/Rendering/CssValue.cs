/// <summary>
/// Whether a value a diagram's source sets on its own shapes (<c>style</c>, <c>classDef</c>, <c>linkStyle</c>,
/// a sequence <c>rect</c>, a palette in its config) has the shape CSS gives that kind of value. Such values are
/// escaped wherever they are written, so none can break out of its attribute, but escaping leaves what is
/// inside it alone: <c>fill:url(https://…)</c> had a viewer fetch from a host the diagram named, and
/// <c>red;display:none</c> added declarations of its own. A value that is not one of these is dropped, and
/// the shape keeps the colour it would have had without it.
/// </summary>
static partial class CssValue
{
    /// <summary>
    /// A colour: a hex or named colour and the <c>rgb()</c>/<c>hsl()</c> forms the PNG backends draw (see
    /// <see cref="CssColor"/>), <c>none</c>, <c>currentColor</c>, CSS's other colour functions with plain numeric
    /// arguments (<c>oklch(70% 0.1 200)</c>), or a custom property a host page defines
    /// (<c>var(--accent)</c>).
    /// </summary>
    public static bool IsColor([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("currentColor", StringComparison.OrdinalIgnoreCase) ||
            CssColor.TryParse(text, out _))
        {
            return true;
        }

        return ColorFunctionRegex().IsMatch(text) ||
               CustomPropertyRegex().IsMatch(text);
    }

    /// <summary>A <c>stroke-dasharray</c>: <c>none</c>, or lengths separated by spaces or commas.</summary>
    public static bool IsDashArray([NotNullWhen(true)] string? value) =>
        value is not null &&
        DashArrayRegex().IsMatch(value.Trim());

    /// <summary>A plain number, as an opacity or a stroke width is given, with an optional <c>px</c>.</summary>
    public static bool IsNumber([NotNullWhen(true)] string? value) =>
        value is not null &&
        NumberRegex().IsMatch(value.Trim());

    // The arguments may hold numbers, units, percentages, `none` and the separators, but no parenthesis,
    // so no function nests inside, url() least of all.
    [GeneratedRegex(@"^(rgba?|hsla?|hwb|lab|lch|oklab|oklch)\([-+0-9.%a-z\s,/]*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColorFunctionRegex();

    [GeneratedRegex(@"^var\(--[a-z0-9_-]+\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CustomPropertyRegex();

    [GeneratedRegex(@"^(none|\d*\.?\d+(px|%)?([\s,]+\d*\.?\d+(px|%)?)*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DashArrayRegex();

    [GeneratedRegex(@"^\d*\.?\d+(px)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}
