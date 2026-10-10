/// <summary>
/// Parses the CSS colour syntaxes that appear in Naiad/Mermaid output — hex (<c>#rgb</c>,
/// <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>), <c>rgb()</c>/<c>rgba()</c>,
/// <c>hsl()</c>/<c>hsla()</c> and the named colours — into an <see cref="Rgba"/>. Returns false for
/// <c>none</c>, <c>currentColor</c> (resolved by the caller) and anything unrecognised, so callers can
/// treat "no concrete paint" as an ordinary outcome.
/// </summary>
static class CssColor
{
    public static bool TryParse(string? text, out Rgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value[0] == '#')
        {
            return TryParseHex(value.AsSpan(1), out color);
        }

        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgb(value, out color);
        }

        if (value.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseHsl(value, out color);
        }

        return TryParseNamed(value, out color);
    }

    static bool TryParseHex(CharSpan hex, out Rgba color)
    {
        color = default;
        switch (hex.Length)
        {
            case 3:
                // #rgb → #rrggbb
                if (Nibble(hex[0], out var r3) && Nibble(hex[1], out var g3) && Nibble(hex[2], out var b3))
                {
                    color = new((byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17), 255);
                    return true;
                }

                return false;
            case 4:
                if (Nibble(hex[0], out var r4) &&
                    Nibble(hex[1], out var g4) &&
                    Nibble(hex[2], out var b4) &&
                    Nibble(hex[3], out var a4))
                {
                    color = new((byte)(r4 * 17), (byte)(g4 * 17), (byte)(b4 * 17), (byte)(a4 * 17));
                    return true;
                }

                return false;
            case 6:
                if (Byte(hex[..2], out var r6) &&
                    Byte(hex[2..4], out var g6) &&
                    Byte(hex[4..6], out var b6))
                {
                    color = new(r6, g6, b6, 255);
                    return true;
                }

                return false;
            case 8:
                if (Byte(hex[..2], out var r8) &&
                    Byte(hex[2..4], out var g8) &&
                    Byte(hex[4..6], out var b8) &&
                    Byte(hex[6..8], out var a8))
                {
                    color = new(r8, g8, b8, a8);
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    static bool TryParseRgb(string value, out Rgba color)
    {
        color = default;
        var parts = Components(value);
        if (parts is not {Length: 3 or 4})
        {
            return false;
        }

        if (!Channel(parts[0], out var r) ||
            !Channel(parts[1], out var g) ||
            !Channel(parts[2], out var b))
        {
            return false;
        }

        var a = (byte)255;
        if (parts.Length == 4 && TryAlpha(parts[3], out var alpha))
        {
            a = alpha;
        }

        color = new(r, g, b, a);
        return true;
    }

    static bool TryParseHsl(string value, out Rgba color)
    {
        color = default;
        var parts = Components(value);
        if (parts is not {Length: 3 or 4})
        {
            return false;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ||
            !Percent(parts[1], out var s) ||
            !Percent(parts[2], out var l))
        {
            return false;
        }

        var a = (byte)255;
        if (parts.Length == 4 && TryAlpha(parts[3], out var alpha))
        {
            a = alpha;
        }

        var (r, g, b) = HslToRgb(h, s, l);
        color = new(r, g, b, a);
        return true;
    }

    // Standard HSL→RGB conversion (CSS Color 3). h in degrees, s/l in [0, 1].
    static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        h = (h % 360 + 360) % 360 / 360;
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        var r = HueToChannel(p, q, h + 1d / 3);
        var g = HueToChannel(p, q, h);
        var b = HueToChannel(p, q, h - 1d / 3);
        return (Component(r), Component(g), Component(b));
    }

    static double HueToChannel(double p, double q, double t)
    {
        t = (t % 1 + 1) % 1;
        if (t < 1d / 6)
        {
            return p + (q - p) * 6 * t;
        }

        if (t < 1d / 2)
        {
            return q;
        }

        if (t < 2d / 3)
        {
            return p + (q - p) * (2d / 3 - t) * 6;
        }

        return p;
    }

    static string[]? Components(string value)
    {
        var open = value.IndexOf('(');
        var close = value.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return null;
        }

        var inner = value[(open + 1)..close];
        // Both comma and whitespace separators occur in the wild; split on either.
        return inner.Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    static bool Channel(string text, out byte value)
    {
        value = 0;
        if (text.EndsWith('%'))
        {
            if (Percent(text, out var fraction))
            {
                value = Component(fraction);
                return true;
            }

            return false;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            value = Component(number / 255);
            return true;
        }

        return false;
    }

    static bool TryAlpha(string text, out byte value)
    {
        value = 255;
        if (text.EndsWith('%'))
        {
            if (Percent(text, out var fraction))
            {
                value = Component(fraction);
                return true;
            }

            return false;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            value = Component(number);
            return true;
        }

        return false;
    }

    static bool Percent(string text, out double fraction)
    {
        fraction = 0;
        var trimmed = text.EndsWith('%') ? text[..^1] : text;
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            fraction = text.EndsWith('%') ? number / 100 : number;
            return true;
        }

        return false;
    }

    static byte Component(double fraction) =>
        (byte)Math.Clamp((int)Math.Round(fraction * 255), 0, 255);

    static bool Nibble(char c, out int value)
    {
        if (c is >= '0' and <= '9')
        {
            value = c - '0';
            return true;
        }

        if (c is >= 'a' and <= 'f')
        {
            value = c - 'a' + 10;
            return true;
        }

        if (c is >= 'A' and <= 'F')
        {
            value = c - 'A' + 10;
            return true;
        }

        value = 0;
        return false;
    }

    static bool Byte(CharSpan hex, out byte value)
    {
        if (Nibble(hex[0], out var hi) && Nibble(hex[1], out var lo))
        {
            value = (byte)(hi * 16 + lo);
            return true;
        }

        value = 0;
        return false;
    }

    static bool TryParseNamed(string name, out Rgba color) =>
        named.TryGetValue(name, out color);

    // The CSS named colours (CSS Color Module Level 4), plus `transparent`. A flowchart `style` or
    // `classDef` may use any of them, and a name missing from here would paint nothing at all.
    static readonly Dictionary<string, Rgba> named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transparent"] = new(0, 0, 0, 0),
        ["aliceblue"] = new(240, 248, 255, 255),
        ["antiquewhite"] = new(250, 235, 215, 255),
        ["aqua"] = new(0, 255, 255, 255),
        ["aquamarine"] = new(127, 255, 212, 255),
        ["azure"] = new(240, 255, 255, 255),
        ["beige"] = new(245, 245, 220, 255),
        ["bisque"] = new(255, 228, 196, 255),
        ["black"] = new(0, 0, 0, 255),
        ["blanchedalmond"] = new(255, 235, 205, 255),
        ["blue"] = new(0, 0, 255, 255),
        ["blueviolet"] = new(138, 43, 226, 255),
        ["brown"] = new(165, 42, 42, 255),
        ["burlywood"] = new(222, 184, 135, 255),
        ["cadetblue"] = new(95, 158, 160, 255),
        ["chartreuse"] = new(127, 255, 0, 255),
        ["chocolate"] = new(210, 105, 30, 255),
        ["coral"] = new(255, 127, 80, 255),
        ["cornflowerblue"] = new(100, 149, 237, 255),
        ["cornsilk"] = new(255, 248, 220, 255),
        ["crimson"] = new(220, 20, 60, 255),
        ["cyan"] = new(0, 255, 255, 255),
        ["darkblue"] = new(0, 0, 139, 255),
        ["darkcyan"] = new(0, 139, 139, 255),
        ["darkgoldenrod"] = new(184, 134, 11, 255),
        ["darkgray"] = new(169, 169, 169, 255),
        ["darkgreen"] = new(0, 100, 0, 255),
        ["darkgrey"] = new(169, 169, 169, 255),
        ["darkkhaki"] = new(189, 183, 107, 255),
        ["darkmagenta"] = new(139, 0, 139, 255),
        ["darkolivegreen"] = new(85, 107, 47, 255),
        ["darkorange"] = new(255, 140, 0, 255),
        ["darkorchid"] = new(153, 50, 204, 255),
        ["darkred"] = new(139, 0, 0, 255),
        ["darksalmon"] = new(233, 150, 122, 255),
        ["darkseagreen"] = new(143, 188, 143, 255),
        ["darkslateblue"] = new(72, 61, 139, 255),
        ["darkslategray"] = new(47, 79, 79, 255),
        ["darkslategrey"] = new(47, 79, 79, 255),
        ["darkturquoise"] = new(0, 206, 209, 255),
        ["darkviolet"] = new(148, 0, 211, 255),
        ["deeppink"] = new(255, 20, 147, 255),
        ["deepskyblue"] = new(0, 191, 255, 255),
        ["dimgray"] = new(105, 105, 105, 255),
        ["dimgrey"] = new(105, 105, 105, 255),
        ["dodgerblue"] = new(30, 144, 255, 255),
        ["firebrick"] = new(178, 34, 34, 255),
        ["floralwhite"] = new(255, 250, 240, 255),
        ["forestgreen"] = new(34, 139, 34, 255),
        ["fuchsia"] = new(255, 0, 255, 255),
        ["gainsboro"] = new(220, 220, 220, 255),
        ["ghostwhite"] = new(248, 248, 255, 255),
        ["gold"] = new(255, 215, 0, 255),
        ["goldenrod"] = new(218, 165, 32, 255),
        ["gray"] = new(128, 128, 128, 255),
        ["green"] = new(0, 128, 0, 255),
        ["greenyellow"] = new(173, 255, 47, 255),
        ["grey"] = new(128, 128, 128, 255),
        ["honeydew"] = new(240, 255, 240, 255),
        ["hotpink"] = new(255, 105, 180, 255),
        ["indianred"] = new(205, 92, 92, 255),
        ["indigo"] = new(75, 0, 130, 255),
        ["ivory"] = new(255, 255, 240, 255),
        ["khaki"] = new(240, 230, 140, 255),
        ["lavender"] = new(230, 230, 250, 255),
        ["lavenderblush"] = new(255, 240, 245, 255),
        ["lawngreen"] = new(124, 252, 0, 255),
        ["lemonchiffon"] = new(255, 250, 205, 255),
        ["lightblue"] = new(173, 216, 230, 255),
        ["lightcoral"] = new(240, 128, 128, 255),
        ["lightcyan"] = new(224, 255, 255, 255),
        ["lightgoldenrodyellow"] = new(250, 250, 210, 255),
        ["lightgray"] = new(211, 211, 211, 255),
        ["lightgreen"] = new(144, 238, 144, 255),
        ["lightgrey"] = new(211, 211, 211, 255),
        ["lightpink"] = new(255, 182, 193, 255),
        ["lightsalmon"] = new(255, 160, 122, 255),
        ["lightseagreen"] = new(32, 178, 170, 255),
        ["lightskyblue"] = new(135, 206, 250, 255),
        ["lightslategray"] = new(119, 136, 153, 255),
        ["lightslategrey"] = new(119, 136, 153, 255),
        ["lightsteelblue"] = new(176, 196, 222, 255),
        ["lightyellow"] = new(255, 255, 224, 255),
        ["lime"] = new(0, 255, 0, 255),
        ["limegreen"] = new(50, 205, 50, 255),
        ["linen"] = new(250, 240, 230, 255),
        ["magenta"] = new(255, 0, 255, 255),
        ["maroon"] = new(128, 0, 0, 255),
        ["mediumaquamarine"] = new(102, 205, 170, 255),
        ["mediumblue"] = new(0, 0, 205, 255),
        ["mediumorchid"] = new(186, 85, 211, 255),
        ["mediumpurple"] = new(147, 112, 219, 255),
        ["mediumseagreen"] = new(60, 179, 113, 255),
        ["mediumslateblue"] = new(123, 104, 238, 255),
        ["mediumspringgreen"] = new(0, 250, 154, 255),
        ["mediumturquoise"] = new(72, 209, 204, 255),
        ["mediumvioletred"] = new(199, 21, 133, 255),
        ["midnightblue"] = new(25, 25, 112, 255),
        ["mintcream"] = new(245, 255, 250, 255),
        ["mistyrose"] = new(255, 228, 225, 255),
        ["moccasin"] = new(255, 228, 181, 255),
        ["navajowhite"] = new(255, 222, 173, 255),
        ["navy"] = new(0, 0, 128, 255),
        ["oldlace"] = new(253, 245, 230, 255),
        ["olive"] = new(128, 128, 0, 255),
        ["olivedrab"] = new(107, 142, 35, 255),
        ["orange"] = new(255, 165, 0, 255),
        ["orangered"] = new(255, 69, 0, 255),
        ["orchid"] = new(218, 112, 214, 255),
        ["palegoldenrod"] = new(238, 232, 170, 255),
        ["palegreen"] = new(152, 251, 152, 255),
        ["paleturquoise"] = new(175, 238, 238, 255),
        ["palevioletred"] = new(219, 112, 147, 255),
        ["papayawhip"] = new(255, 239, 213, 255),
        ["peachpuff"] = new(255, 218, 185, 255),
        ["peru"] = new(205, 133, 63, 255),
        ["pink"] = new(255, 192, 203, 255),
        ["plum"] = new(221, 160, 221, 255),
        ["powderblue"] = new(176, 224, 230, 255),
        ["purple"] = new(128, 0, 128, 255),
        ["rebeccapurple"] = new(102, 51, 153, 255),
        ["red"] = new(255, 0, 0, 255),
        ["rosybrown"] = new(188, 143, 143, 255),
        ["royalblue"] = new(65, 105, 225, 255),
        ["saddlebrown"] = new(139, 69, 19, 255),
        ["salmon"] = new(250, 128, 114, 255),
        ["sandybrown"] = new(244, 164, 96, 255),
        ["seagreen"] = new(46, 139, 87, 255),
        ["seashell"] = new(255, 245, 238, 255),
        ["sienna"] = new(160, 82, 45, 255),
        ["silver"] = new(192, 192, 192, 255),
        ["skyblue"] = new(135, 206, 235, 255),
        ["slateblue"] = new(106, 90, 205, 255),
        ["slategray"] = new(112, 128, 144, 255),
        ["slategrey"] = new(112, 128, 144, 255),
        ["snow"] = new(255, 250, 250, 255),
        ["springgreen"] = new(0, 255, 127, 255),
        ["steelblue"] = new(70, 130, 180, 255),
        ["tan"] = new(210, 180, 140, 255),
        ["teal"] = new(0, 128, 128, 255),
        ["thistle"] = new(216, 191, 216, 255),
        ["tomato"] = new(255, 99, 71, 255),
        ["turquoise"] = new(64, 224, 208, 255),
        ["violet"] = new(238, 130, 238, 255),
        ["wheat"] = new(245, 222, 179, 255),
        ["white"] = new(255, 255, 255, 255),
        ["whitesmoke"] = new(245, 245, 245, 255),
        ["yellow"] = new(255, 255, 0, 255),
        ["yellowgreen"] = new(154, 205, 50, 255),
    };
}
