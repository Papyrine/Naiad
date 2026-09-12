using Color = SixLabors.ImageSharp.Color;

/// <summary>
/// ImageSharp-backed <see cref="IRenderSurface"/>: the shared SVG walker's fills, strokes and text paint
/// into an <see cref="Image{Rgba32}"/>, then <see cref="Encode"/> writes a PNG. Each primitive's current
/// transform is applied through <see cref="DrawingOptions.Transform"/> so geometry, text and stroke
/// widths all scale with the diagram's transforms, mirroring the Skia backend.
/// </summary>
sealed class ImageSharpSurface(int width, int height, Rgba background, PngCompression compression) :
    IRenderSurface
{
    static ConcurrentDictionary<string, FontFamily> familyCache = new(StringComparer.OrdinalIgnoreCase);

    Image<Rgba32> image = new(width, height, ToPixel(background));

    public void FillPath(IReadOnlyList<SubPath> subpaths, Matrix3x2 transform, Paint paint, FillRule rule, float opacity)
    {
        var path = ToPath(subpaths);
        var brush = ToBrush(paint, opacity);
        var options = Options(transform, rule);
        image.Mutate(context => context.Paint(options, inner => inner.Fill(brush, path)));
    }

    public void StrokePath(IReadOnlyList<SubPath> subpaths, Matrix3x2 transform, Rgba color, float width, IReadOnlyList<float>? dash, float opacity)
    {
        var path = ToPath(subpaths);
        // The width stays in path units: the transform passed to Options below scales the stroke
        // outline along with the geometry, so scaling the width here too would apply it twice.
        var pen = ToPen(ToColor(color.MultiplyAlpha(opacity)), width, dash);
        var options = Options(transform, FillRule.NonZero);
        image.Mutate(context => context.Paint(options, inner => inner.Draw(pen, path)));
    }

    public void DrawText(string text, float x, float y, Matrix3x2 transform, TextStyle style)
    {
        var font = ResolveFont(style);
        var metrics = font.FontMetrics;
        var unitScale = font.Size / metrics.UnitsPerEm;
        var ascent = metrics.HorizontalMetrics.Ascender * unitScale;
        var descent = metrics.HorizontalMetrics.Descender * unitScale;

        var advance = TextMeasurer.MeasureAdvance(text, new(font));
        var penX = style.Anchor switch
        {
            TextAnchorKind.Middle => x - advance.Width / 2,
            TextAnchorKind.End => x - advance.Width,
            _ => x,
        };

        // ImageSharp lays text out from the top of the line box; convert the requested baseline into
        // that top. Descender is negative, so (ascent - descent) is the full line height.
        var top = style.Baseline switch
        {
            TextBaselineKind.Middle => y - (ascent - descent) / 2,
            TextBaselineKind.Hanging => y,
            _ => y - ascent,
        };

        var textOptions = new RichTextOptions(font)
        {
            Origin = new(penX, top),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var brush = new SolidBrush(ToColor(style.Color.MultiplyAlpha(style.Opacity)));
        var options = Options(transform, FillRule.NonZero);
        image.Mutate(context => context.Paint(options, inner => inner.DrawText(textOptions, text, brush, null)));
    }

    public void Encode(Stream stream) =>
        image.SaveAsPng(
            stream,
            new()
            {
                CompressionLevel = compression switch
                {
                    PngCompression.Fast => PngCompressionLevel.BestSpeed,
                    PngCompression.Small => PngCompressionLevel.BestCompression,
                    _ => PngCompressionLevel.Level6,
                }
            });

    static DrawingOptions Options(Matrix3x2 transform, FillRule rule) =>
        new()
        {
            Transform = new(transform),
            GraphicsOptions = new()
            {
                Antialias = true
            },
            IntersectionRule = rule == FillRule.EvenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero,
        };

    static IPath ToPath(IReadOnlyList<SubPath> subpaths)
    {
        var builder = new PathBuilder();
        foreach (var subpath in subpaths)
        {
            if (subpath.Points.Count == 0)
            {
                continue;
            }

            var points = new PointF[subpath.Points.Count];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = new(subpath.Points[i].X, subpath.Points[i].Y);
            }

            builder.StartFigure();
            builder.AddLines(points);
            if (subpath.Closed)
            {
                builder.CloseFigure();
            }
        }

        return builder.Build();
    }

    static Brush ToBrush(Paint paint, float opacity) =>
        paint switch
        {
            SolidPaint solid => new SolidBrush(ToColor(solid.Color.MultiplyAlpha(opacity))),
            LinearGradientPaint linear => new LinearGradientBrush(
                new(linear.Start.X, linear.Start.Y),
                new(linear.End.X, linear.End.Y),
                GradientRepetitionMode.None,
                Stops(linear.Stops, opacity)),
            RadialGradientPaint radial => new RadialGradientBrush(
                new(radial.Center.X, radial.Center.Y),
                radial.Radius,
                GradientRepetitionMode.None,
                Stops(radial.Stops, opacity))
        };

    static ColorStop[] Stops(IReadOnlyList<GradientStop> stops, float opacity)
    {
        var result = new ColorStop[stops.Count];
        for (var i = 0; i < stops.Count; i++)
        {
            result[i] = new(stops[i].Offset, ToColor(stops[i].Color.MultiplyAlpha(opacity)));
        }

        return result;
    }

    static Pen ToPen(Color color, float width, IReadOnlyList<float>? dash)
    {
        if (dash is {Count: > 0})
        {
            // ImageSharp's stroke pattern is expressed in multiples of the pen width.
            var pattern = new float[dash.Count];
            for (var i = 0; i < dash.Count; i++)
            {
                pattern[i] = width > 0 ? dash[i] / width : dash[i];
            }

            return new PatternPen(color, width, pattern);
        }

        return new SolidPen(color, width);
    }

    static Font ResolveFont(TextStyle style)
    {
        var family = ResolveFamily(style.FontFamilies);
        var fontStyle = (style.Bold, style.Italic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            _ => FontStyle.Regular,
        };
        return family.CreateFont(style.FontSize, fontStyle);
    }

    static FontFamily ResolveFamily(IReadOnlyList<string> families) =>
        familyCache.GetOrAdd(families.Count > 0 ? families[0] : "sans-serif", _ => Lookup(families));

    static FontFamily Lookup(IReadOnlyList<string> families)
    {
        foreach (var name in families)
        {
            if (SystemFonts.TryGet(name, out var family))
            {
                return family;
            }
        }

        foreach (var fallback in SystemFonts.Families)
        {
            return fallback;
        }

        throw new InvalidOperationException(
            "No system fonts are installed for the ImageSharp render backend to draw text. Install a font, or use the Skia backend.");
    }

    static Color ToColor(Rgba color) =>
        Color.FromPixel(ToPixel(color));

    static Rgba32 ToPixel(Rgba color) =>
        new(color.R, color.G, color.B, color.A);

    public void Dispose() =>
        image.Dispose();
}
