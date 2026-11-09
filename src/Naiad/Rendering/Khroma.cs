/// <summary>
/// The colour functions Mermaid's themes are written with, ported from the khroma library they come from,
/// so a theme's colours come out as Mermaid's do, down to how they are written: a colour changed in hue,
/// saturation or lightness is written as <c>hsl(...)</c>, one changed in red, green or blue as hex (or
/// <c>rgba(...)</c> when it is translucent or not whole), and one left unchanged as it was given.
/// </summary>
static class Khroma
{
    const int typeAll = 0;
    const int typeRgb = 1;
    const int typeHsl = 2;

    // A colour's channels, as khroma keeps them: whichever of the RGB and HSL sets it was given, with the
    // other worked out when first asked for.
    sealed class Channels
    {
        public double? R;
        public double? G;
        public double? B;
        public double? H;
        public double? S;
        public double? L;
        public double A = 1;
        public int Type = typeAll;
        public bool Changed;
        public string? Color;

        void SetType(int type)
        {
            if (Type != typeAll &&
                Type != type)
            {
                throw new InvalidOperationException("Cannot change both RGB and HSL channels at the same time");
            }

            Type = type;
        }

        void EnsureHsl()
        {
            H ??= RgbToHsl('h');
            S ??= RgbToHsl('s');
            L ??= RgbToHsl('l');
        }

        void EnsureRgb()
        {
            R ??= HslToRgb('r');
            G ??= HslToRgb('g');
            B ??= HslToRgb('b');
        }

        public double Red
        {
            get
            {
                if (Type != typeHsl &&
                    R is { } red)
                {
                    return red;
                }

                EnsureHsl();
                return HslToRgb('r');
            }
            set
            {
                SetType(typeRgb);
                Changed = true;
                R = value;
            }
        }

        public double Green
        {
            get
            {
                if (Type != typeHsl &&
                    G is { } green)
                {
                    return green;
                }

                EnsureHsl();
                return HslToRgb('g');
            }
            set
            {
                SetType(typeRgb);
                Changed = true;
                G = value;
            }
        }

        public double Blue
        {
            get
            {
                if (Type != typeHsl &&
                    B is { } blue)
                {
                    return blue;
                }

                EnsureHsl();
                return HslToRgb('b');
            }
            set
            {
                SetType(typeRgb);
                Changed = true;
                B = value;
            }
        }

        public double Hue
        {
            get
            {
                if (Type != typeRgb &&
                    H is { } hue)
                {
                    return hue;
                }

                EnsureRgb();
                return RgbToHsl('h');
            }
            set
            {
                SetType(typeHsl);
                Changed = true;
                H = value;
            }
        }

        public double Saturation
        {
            get
            {
                if (Type != typeRgb &&
                    S is { } saturation)
                {
                    return saturation;
                }

                EnsureRgb();
                return RgbToHsl('s');
            }
            set
            {
                SetType(typeHsl);
                Changed = true;
                S = value;
            }
        }

        public double Lightness
        {
            get
            {
                if (Type != typeRgb &&
                    L is { } lightness)
                {
                    return lightness;
                }

                EnsureRgb();
                return RgbToHsl('l');
            }
            set
            {
                SetType(typeHsl);
                Changed = true;
                L = value;
            }
        }

        public double Get(char channel) =>
            channel switch
            {
                'r' => Red,
                'g' => Green,
                'b' => Blue,
                'h' => Hue,
                's' => Saturation,
                'l' => Lightness,
                _ => A
            };

        public void Set(char channel, double value)
        {
            switch (channel)
            {
                case 'r':
                    Red = Math.Clamp(value, 0, 255);
                    break;
                case 'g':
                    Green = Math.Clamp(value, 0, 255);
                    break;
                case 'b':
                    Blue = Math.Clamp(value, 0, 255);
                    break;
                case 'h':
                    // As in JavaScript, the remainder keeps the sign: -60 stays -60, which reads as 300.
                    Hue = value % 360;
                    break;
                case 's':
                    Saturation = Clamp100(value);
                    break;
                case 'l':
                    Lightness = Clamp100(value);
                    break;
                default:
                    Changed = true;
                    A = Math.Clamp(value, 0, 1);
                    break;
            }
        }

        double HslToRgb(char channel)
        {
            var h = H!.Value;
            var s = S!.Value;
            var l = L!.Value;
            if (s == 0)
            {
                return l * 2.55;
            }

            h /= 360;
            s /= 100;
            l /= 100;
            var q = l < .5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            return channel switch
            {
                'r' => HueToRgb(p, q, h + 1.0 / 3) * 255,
                'g' => HueToRgb(p, q, h) * 255,
                _ => HueToRgb(p, q, h - 1.0 / 3) * 255
            };
        }

        double RgbToHsl(char channel)
        {
            var r = R!.Value / 255;
            var g = G!.Value / 255;
            var b = B!.Value / 255;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var l = (max + min) / 2;
            if (channel == 'l')
            {
                return l * 100;
            }

            if (max == min)
            {
                return 0;
            }

            var d = max - min;
            var s = l > .5 ? d / (2 - max - min) : d / (max + min);
            if (channel == 's')
            {
                return s * 100;
            }

            if (max == r)
            {
                return ((g - b) / d + (g < b ? 6 : 0)) * 60;
            }

            if (max == g)
            {
                return ((b - r) / d + 2) * 60;
            }

            return ((r - g) / d + 4) * 60;
        }

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            if (t < 1.0 / 6)
            {
                return p + (q - p) * 6 * t;
            }

            if (t < 1.0 / 2)
            {
                return q;
            }

            if (t < 2.0 / 3)
            {
                return p + (q - p) * (2.0 / 3 - t) * 6;
            }

            return p;
        }

        static double Clamp100(double value)
        {
            if (value >= 100)
            {
                return 100;
            }

            if (value < 0)
            {
                return 0;
            }

            return value;
        }
    }

    /// <summary>
    /// Adds to some of a colour's channels: <c>h</c>, <c>s</c> and <c>l</c>, or <c>r</c>, <c>g</c> and
    /// <c>b</c>. A change of 0 is no change.
    /// </summary>
    internal static string Adjust(string color, double h = 0, double s = 0, double l = 0, double r = 0, double g = 0, double b = 0)
    {
        var current = Parse(color);
        var changes = new List<(char, double)>();
        foreach (var (channel, amount) in new[] { ('r', r), ('g', g), ('b', b), ('h', h), ('s', s), ('l', l) })
        {
            if (amount != 0)
            {
                changes.Add((channel, current.Get(channel) + amount));
            }
        }

        var changed = Parse(color);
        foreach (var (channel, value) in changes)
        {
            changed.Set(channel, value);
        }

        return Stringify(changed);
    }

    internal static string Lighten(string color, double amount) =>
        AdjustChannel(color, 'l', amount);

    internal static string Darken(string color, double amount) =>
        AdjustChannel(color, 'l', -amount);

    static string AdjustChannel(string color, char channel, double amount)
    {
        var channels = Parse(color);
        var current = channels.Get(channel);
        var next = Math.Clamp(current + amount, 0, 100);
        if (current != next)
        {
            channels.Set(channel, next);
        }

        return Stringify(channels);
    }

    // The colour opposite on every RGB channel.
    internal static string Invert(string color)
    {
        var channels = Parse(color);
        return Rgba(255 - channels.Red, 255 - channels.Green, 255 - channels.Blue, channels.A);
    }

    internal static string Rgba(double r, double g, double b, double a)
    {
        var channels = new Channels
        {
            R = Math.Clamp(r, 0, 255),
            G = Math.Clamp(g, 0, 255),
            B = Math.Clamp(b, 0, 255),
            A = Math.Clamp(a, 0, 1)
        };
        return Stringify(channels);
    }

    // Whether the colour's relative luminance is below a half.
    internal static bool IsDark(string color)
    {
        var channels = Parse(color);
        var luminance = .2126 * ToLinear(channels.Red) + .7152 * ToLinear(channels.Green) + .0722 * ToLinear(channels.Blue);
        return Round(luminance) < .5;
    }

    // Multiplies each RGB channel, as CSS's brightness() filter does.
    internal static string Brightness(string color, double factor)
    {
        var channels = Parse(color);
        return Rgba(
            Math.Round(channels.Red * factor),
            Math.Round(channels.Green * factor),
            Math.Round(channels.Blue * factor),
            channels.A);
    }

    static double ToLinear(double channel)
    {
        var c = channel / 255;
        if (c > .03928)
        {
            return Math.Pow((c + .055) / 1.055, 2.4);
        }

        return c / 12.92;
    }

    // khroma's rounding, to ten decimal places.
    static double Round(double value) =>
        Math.Round(value * 10000000000, MidpointRounding.AwayFromZero) / 10000000000;

    static string Format(double value) =>
        Round(value).ToString("0.##########", CultureInfo.InvariantCulture);

    static string Stringify(Channels channels)
    {
        if (channels is
            {
                Changed: false,
                Color: { } color
            })
        {
            return color;
        }

        if (channels.Type == typeHsl ||
            channels.R is null)
        {
            var hsl = $"{Format(channels.Hue)}, {Format(channels.Saturation)}%, {Format(channels.Lightness)}%";
            if (channels.A < 1)
            {
                return $"hsla({hsl}, {channels.A.ToString(CultureInfo.InvariantCulture)})";
            }

            return $"hsl({hsl})";
        }

        var r = channels.Red;
        var g = channels.Green;
        var b = channels.Blue;
        if (channels.A < 1 ||
            r != Math.Floor(r) ||
            g != Math.Floor(g) ||
            b != Math.Floor(b))
        {
            if (channels.A < 1)
            {
                return $"rgba({Format(r)}, {Format(g)}, {Format(b)}, {Format(channels.A)})";
            }

            return $"rgb({Format(r)}, {Format(g)}, {Format(b)})";
        }

        return string.Create(CultureInfo.InvariantCulture, $"#{(int) r:x2}{(int) g:x2}{(int) b:x2}");
    }

    static Channels Parse(string color)
    {
        color = color.Trim();
        if (color.StartsWith('#'))
        {
            return ParseHex(color[1..], color);
        }

        var lower = color.ToLowerInvariant();
        if (lower.StartsWith("rgb", StringComparison.Ordinal) &&
            TryFunction(lower, out var rgbValues))
        {
            return new()
            {
                R = Math.Clamp(Number(rgbValues[0], 2.55), 0, 255),
                G = Math.Clamp(Number(rgbValues[1], 2.55), 0, 255),
                B = Math.Clamp(Number(rgbValues[2], 2.55), 0, 255),
                A = Alpha(rgbValues),
                Color = color
            };
        }

        if (lower.StartsWith("hsl", StringComparison.Ordinal) &&
            TryFunction(lower, out var hslValues))
        {
            return new()
            {
                H = Hue(hslValues[0]),
                S = Math.Clamp(Number(hslValues[1], 1), 0, 100),
                L = Math.Clamp(Number(hslValues[2], 1), 0, 100),
                A = Alpha(hslValues),
                Color = color
            };
        }

        if (keywords.TryGetValue(lower, out var hex))
        {
            return ParseHex(hex, color);
        }

        throw new MermaidException($"Unsupported color format: \"{color}\"");
    }

    static Channels ParseHex(string hex, string color)
    {
        if (hex.Length is not (3 or 4 or 6 or 8) ||
            !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) &&
            !long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            throw new MermaidException($"Unsupported color format: \"{color}\"");
        }

        double Digit(int index, int width)
        {
            var value = Convert.ToInt32(hex.Substring(index * width, width), 16);
            if (width == 1)
            {
                return value * 17;
            }

            return value;
        }

        var size = 2;
        if (hex.Length <= 4)
        {
            size = 1;
        }

        var alpha = 1.0;
        if (hex.Length % 4 == 0)
        {
            alpha = Digit(3, size) / 255;
        }

        return new()
        {
            R = Digit(0, size),
            G = Digit(1, size),
            B = Digit(2, size),
            A = alpha,
            Color = color
        };
    }

    // The arguments of `name(a, b, c[, d])` or `name(a b c[ / d])`.
    static bool TryFunction(string text, out string[] values)
    {
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 ||
            close < open)
        {
            values = [];
            return false;
        }

        values = text[(open + 1)..close]
            .Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Length is 3 or 4;
    }

    // A number, or a percentage scaled by the given factor.
    static double Number(string text, double percentScale)
    {
        if (text.EndsWith('%'))
        {
            return double.Parse(text[..^1], CultureInfo.InvariantCulture) * percentScale;
        }

        return double.Parse(text, CultureInfo.InvariantCulture);
    }

    static double Alpha(string[] values)
    {
        if (values.Length < 4)
        {
            return 1;
        }

        return Math.Clamp(Number(values[3], 0.01), 0, 1);
    }

    static double Hue(string text)
    {
        if (text.EndsWith("deg", StringComparison.Ordinal))
        {
            return double.Parse(text[..^3], CultureInfo.InvariantCulture);
        }

        if (text.EndsWith("turn", StringComparison.Ordinal))
        {
            return double.Parse(text[..^4], CultureInfo.InvariantCulture) * 360;
        }

        return double.Parse(text, CultureInfo.InvariantCulture);
    }

    // The CSS colour keywords.
    static readonly Dictionary<string, string> keywords = new(StringComparer.Ordinal)
    {
        ["aliceblue"] = "f0f8ff", ["antiquewhite"] = "faebd7", ["aqua"] = "00ffff", ["aquamarine"] = "7fffd4",
        ["azure"] = "f0ffff", ["beige"] = "f5f5dc", ["bisque"] = "ffe4c4", ["black"] = "000000",
        ["blanchedalmond"] = "ffebcd", ["blue"] = "0000ff", ["blueviolet"] = "8a2be2", ["brown"] = "a52a2a",
        ["burlywood"] = "deb887", ["cadetblue"] = "5f9ea0", ["chartreuse"] = "7fff00", ["chocolate"] = "d2691e",
        ["coral"] = "ff7f50", ["cornflowerblue"] = "6495ed", ["cornsilk"] = "fff8dc", ["crimson"] = "dc143c",
        ["cyan"] = "00ffff", ["darkblue"] = "00008b", ["darkcyan"] = "008b8b", ["darkgoldenrod"] = "b8860b",
        ["darkgray"] = "a9a9a9", ["darkgreen"] = "006400", ["darkgrey"] = "a9a9a9", ["darkkhaki"] = "bdb76b",
        ["darkmagenta"] = "8b008b", ["darkolivegreen"] = "556b2f", ["darkorange"] = "ff8c00", ["darkorchid"] = "9932cc",
        ["darkred"] = "8b0000", ["darksalmon"] = "e9967a", ["darkseagreen"] = "8fbc8f", ["darkslateblue"] = "483d8b",
        ["darkslategray"] = "2f4f4f", ["darkslategrey"] = "2f4f4f", ["darkturquoise"] = "00ced1", ["darkviolet"] = "9400d3",
        ["deeppink"] = "ff1493", ["deepskyblue"] = "00bfff", ["dimgray"] = "696969", ["dimgrey"] = "696969",
        ["dodgerblue"] = "1e90ff", ["firebrick"] = "b22222", ["floralwhite"] = "fffaf0", ["forestgreen"] = "228b22",
        ["fuchsia"] = "ff00ff", ["gainsboro"] = "dcdcdc", ["ghostwhite"] = "f8f8ff", ["gold"] = "ffd700",
        ["goldenrod"] = "daa520", ["gray"] = "808080", ["green"] = "008000", ["greenyellow"] = "adff2f",
        ["grey"] = "808080", ["honeydew"] = "f0fff0", ["hotpink"] = "ff69b4", ["indianred"] = "cd5c5c",
        ["indigo"] = "4b0082", ["ivory"] = "fffff0", ["khaki"] = "f0e68c", ["lavender"] = "e6e6fa",
        ["lavenderblush"] = "fff0f5", ["lawngreen"] = "7cfc00", ["lemonchiffon"] = "fffacd", ["lightblue"] = "add8e6",
        ["lightcoral"] = "f08080", ["lightcyan"] = "e0ffff", ["lightgoldenrodyellow"] = "fafad2", ["lightgray"] = "d3d3d3",
        ["lightgreen"] = "90ee90", ["lightgrey"] = "d3d3d3", ["lightpink"] = "ffb6c1", ["lightsalmon"] = "ffa07a",
        ["lightseagreen"] = "20b2aa", ["lightskyblue"] = "87cefa", ["lightslategray"] = "778899", ["lightslategrey"] = "778899",
        ["lightsteelblue"] = "b0c4de", ["lightyellow"] = "ffffe0", ["lime"] = "00ff00", ["limegreen"] = "32cd32",
        ["linen"] = "faf0e6", ["magenta"] = "ff00ff", ["maroon"] = "800000", ["mediumaquamarine"] = "66cdaa",
        ["mediumblue"] = "0000cd", ["mediumorchid"] = "ba55d3", ["mediumpurple"] = "9370db", ["mediumseagreen"] = "3cb371",
        ["mediumslateblue"] = "7b68ee", ["mediumspringgreen"] = "00fa9a", ["mediumturquoise"] = "48d1cc", ["mediumvioletred"] = "c71585",
        ["midnightblue"] = "191970", ["mintcream"] = "f5fffa", ["mistyrose"] = "ffe4e1", ["moccasin"] = "ffe4b5",
        ["navajowhite"] = "ffdead", ["navy"] = "000080", ["oldlace"] = "fdf5e6", ["olive"] = "808000",
        ["olivedrab"] = "6b8e23", ["orange"] = "ffa500", ["orangered"] = "ff4500", ["orchid"] = "da70d6",
        ["palegoldenrod"] = "eee8aa", ["palegreen"] = "98fb98", ["paleturquoise"] = "afeeee", ["palevioletred"] = "db7093",
        ["papayawhip"] = "ffefd5", ["peachpuff"] = "ffdab9", ["peru"] = "cd853f", ["pink"] = "ffc0cb",
        ["plum"] = "dda0dd", ["powderblue"] = "b0e0e6", ["purple"] = "800080", ["rebeccapurple"] = "663399",
        ["red"] = "ff0000", ["rosybrown"] = "bc8f8f", ["royalblue"] = "4169e1", ["saddlebrown"] = "8b4513",
        ["salmon"] = "fa8072", ["sandybrown"] = "f4a460", ["seagreen"] = "2e8b57", ["seashell"] = "fff5ee",
        ["sienna"] = "a0522d", ["silver"] = "c0c0c0", ["skyblue"] = "87ceeb", ["slateblue"] = "6a5acd",
        ["slategray"] = "708090", ["slategrey"] = "708090", ["snow"] = "fffafa", ["springgreen"] = "00ff7f",
        ["steelblue"] = "4682b4", ["tan"] = "d2b48c", ["teal"] = "008080", ["thistle"] = "d8bfd8",
        ["tomato"] = "ff6347", ["turquoise"] = "40e0d0", ["violet"] = "ee82ee", ["wheat"] = "f5deb3",
        ["white"] = "ffffff", ["whitesmoke"] = "f5f5f5", ["yellow"] = "ffff00", ["yellowgreen"] = "9acd32",
        ["transparent"] = "00000000"
    };
}
