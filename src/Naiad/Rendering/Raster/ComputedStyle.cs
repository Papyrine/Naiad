/// <summary>
/// The resolved presentation properties for one element. Inherited properties (fill, stroke, font, …)
/// flow down via <see cref="CloneForChild"/>; <c>opacity</c> is reset per element since it does not
/// inherit (the walker accumulates group opacity separately). Fill/stroke are kept as their raw
/// strings because resolving them needs the surrounding context — gradient lookups and
/// <c>currentColor</c> — which the walker supplies.
/// </summary>
struct ComputedStyle
{
    // A value type so the per-element copy the walker makes (CloneForChild, once per element including the
    // many transparent group wrappers) stays on the stack instead of allocating. The explicit parameterless
    // constructor is required for the initial-value property initializers below to run on `new ComputedStyle()`.
    public ComputedStyle()
    {
    }

    // SVG initial values. The Mermaid base stylesheet overrides several of these on the root via the
    // `#naiad` rule, which the walker applies before descending.
    public string Fill { get; set; } = "#000";

    public string Stroke { get; set; } = "none";

    public double StrokeWidth { get; set; } = 1;

    public string? StrokeDasharray { get; set; }

    public double FillOpacity { get; set; } = 1;

    public double StrokeOpacity { get; set; } = 1;

    public double Opacity { get; set; } = 1;

    public string Color { get; set; } = "#000";

    public double FontSize { get; set; } = 16;

    public string FontFamily { get; set; } = "sans-serif";

    public string? FontWeight { get; set; }

    public string? FontStyle { get; set; }

    public string TextAnchor { get; set; } = "start";

    public string? DominantBaseline { get; set; }

    /// <summary>Produces the inherited base for a child: every inherited property carries over, while
    /// the non-inherited <see cref="Opacity"/> resets to its initial value.</summary>
    public readonly ComputedStyle CloneForChild()
    {
        var clone = this;
        clone.Opacity = 1;
        return clone;
    }

    public void Apply(string property, string value)
    {
        switch (property)
        {
            case "fill":
                Fill = value;
                break;
            case "stroke":
                Stroke = value;
                break;
            case "stroke-width":
                if (Length(value, FontSize) is { } width)
                {
                    StrokeWidth = width;
                }

                break;
            case "stroke-dasharray":
                StrokeDasharray = value.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : value;
                break;
            case "fill-opacity":
                if (Number(value) is { } fillOpacity)
                {
                    FillOpacity = Math.Clamp(fillOpacity, 0, 1);
                }

                break;
            case "stroke-opacity":
                if (Number(value) is { } strokeOpacity)
                {
                    StrokeOpacity = Math.Clamp(strokeOpacity, 0, 1);
                }

                break;
            case "opacity":
                if (Number(value) is { } opacity)
                {
                    Opacity = Math.Clamp(opacity, 0, 1);
                }

                break;
            case "color":
                Color = value;
                break;
            case "font-size":
                if (Length(value, FontSize) is { } fontSize)
                {
                    FontSize = fontSize;
                }

                break;
            case "font-family":
                FontFamily = value;
                break;
            case "font-weight":
                FontWeight = value;
                break;
            case "font-style":
                FontStyle = value;
                break;
            case "text-anchor":
                TextAnchor = value;
                break;
            case "dominant-baseline":
                DominantBaseline = value;
                break;
        }
    }

    static double? Number(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return null;
    }

    static double? Length(string value, double emBasis)
    {
        // Operate on a span: Trim and the unit-suffix slices below allocate no transient strings, and
        // double.TryParse has a ReadOnlySpan<char> overload, so a length parse never touches the heap.
        var text = value.AsSpan().Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2];
        }
        else if (text.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var em))
            {
                return em * emBasis;
            }

            return null;
        }
        else if (text.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pt))
            {
                return pt * 96.0 / 72;
            }

            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return null;
    }
}
