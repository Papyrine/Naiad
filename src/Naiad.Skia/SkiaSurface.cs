/// <summary>
/// Skia-backed <see cref="IRenderSurface"/>: the shared SVG walker's fills, strokes and text paint into
/// an <see cref="SKBitmap"/> via an <see cref="SKCanvas"/>, then <see cref="Encode"/> writes a PNG
/// through Skia's encoder. The current transform handed to each primitive is applied as the canvas
/// matrix, so stroke widths and font sizes scale correctly with the diagram's transforms.
/// </summary>
sealed class SkiaSurface : IRenderSurface
{
    static ConcurrentDictionary<(string, bool, bool), SKTypeface> typefaceCache = new();

    SKBitmap bitmap;
    SKCanvas canvas;
    readonly PngCompression compression;

    public SkiaSurface(int width, int height, Rgba background, PngCompression compression)
    {
        bitmap = new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        canvas = new(bitmap);
        canvas.Clear(ToColor(background));
        this.compression = compression;
    }

    public void FillPath(IReadOnlyList<SubPath> subpaths, Matrix3x2 transform, Paint paint, FillRule rule, float opacity)
    {
        using var path = ToPath(subpaths);
        path.FillType = rule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        using var skPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        ApplyPaint(skPaint, paint, opacity);

        canvas.Save();
        canvas.SetMatrix(ToMatrix(transform));
        canvas.DrawPath(path, skPaint);
        canvas.Restore();
    }

    public void StrokePath(IReadOnlyList<SubPath> subpaths, Matrix3x2 transform, Rgba color, float width, IReadOnlyList<float>? dash, float opacity)
    {
        using var path = ToPath(subpaths);
        using var skPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = ToColor(color.MultiplyAlpha(opacity)),
        };

        if (dash is {Count: > 0})
        {
            skPaint.PathEffect = SKPathEffect.CreateDash([.. dash], 0);
        }

        canvas.Save();
        canvas.SetMatrix(ToMatrix(transform));
        canvas.DrawPath(path, skPaint);
        canvas.Restore();
        skPaint.PathEffect?.Dispose();
    }

    public void DrawText(string text, float x, float y, Matrix3x2 transform, TextStyle style)
    {
        var typeface = ResolveTypeface(style);
        using var font = new SKFont(typeface, style.FontSize);
        using var skPaint = new SKPaint
        {
            IsAntialias = true,
            Color = ToColor(style.Color.MultiplyAlpha(style.Opacity)),
        };

        var width = font.MeasureText(text);
        var penX = style.Anchor switch
        {
            TextAnchorKind.Middle => x - width / 2,
            TextAnchorKind.End => x - width,
            _ => x,
        };

        var metrics = font.Metrics;
        var baseline = style.Baseline switch
        {
            TextBaselineKind.Middle => y - (metrics.Ascent + metrics.Descent) / 2,
            TextBaselineKind.Hanging => y - metrics.Ascent,
            _ => y,
        };

        canvas.Save();
        canvas.SetMatrix(ToMatrix(transform));
        // penX already carries the anchor offset, so draw left-aligned from it.
        canvas.DrawText(text, penX, baseline, SKTextAlign.Left, font, skPaint);
        canvas.Restore();
    }

    public void Encode(Stream stream)
    {
        var zlibLevel = compression switch
        {
            PngCompression.Fast => 1,
            PngCompression.Small => 9,
            _ => 6,
        };

        using var pixmap = bitmap.PeekPixels();
        using var wstream = new SKManagedWStream(stream);
        pixmap.Encode(wstream, new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zlibLevel));
    }

    static void ApplyPaint(SKPaint skPaint, Paint paint, float opacity)
    {
        if (paint is SolidPaint solid)
        {
            skPaint.Color = ToColor(solid.Color.MultiplyAlpha(opacity));
        }

        skPaint.Shader = paint switch
        {
            // The paint is the colour set above; there is no shader to layer over it.
            SolidPaint => null,
            LinearGradientPaint linear => SKShader.CreateLinearGradient(
                new(linear.Start.X, linear.Start.Y),
                new(linear.End.X, linear.End.Y),
                Colors(linear.Stops, opacity),
                Offsets(linear.Stops),
                SKShaderTileMode.Clamp),
            RadialGradientPaint radial => SKShader.CreateRadialGradient(
                new(radial.Center.X, radial.Center.Y),
                radial.Radius,
                Colors(radial.Stops, opacity),
                Offsets(radial.Stops),
                SKShaderTileMode.Clamp)
        };
    }

    static SKColor[] Colors(IReadOnlyList<GradientStop> stops, float opacity)
    {
        var colors = new SKColor[stops.Count];
        for (var i = 0; i < stops.Count; i++)
        {
            colors[i] = ToColor(stops[i].Color.MultiplyAlpha(opacity));
        }

        return colors;
    }

    static float[] Offsets(IReadOnlyList<GradientStop> stops)
    {
        var offsets = new float[stops.Count];
        for (var i = 0; i < stops.Count; i++)
        {
            offsets[i] = stops[i].Offset;
        }

        return offsets;
    }

    static SKPath ToPath(IReadOnlyList<SubPath> subpaths)
    {
        using var builder = new SKPathBuilder();
        foreach (var subpath in subpaths)
        {
            var points = subpath.Points;
            if (points.Count == 0)
            {
                continue;
            }

            builder.MoveTo(points[0].X, points[0].Y);
            for (var i = 1; i < points.Count; i++)
            {
                builder.LineTo(points[i].X, points[i].Y);
            }

            if (subpath.Closed)
            {
                builder.Close();
            }
        }

        return builder.Detach();
    }

    static SKTypeface ResolveTypeface(TextStyle style)
    {
        var family = style.FontFamilies.Count > 0 ? style.FontFamilies[0] : "sans-serif";
        return typefaceCache.GetOrAdd((family, style.Bold, style.Italic), _ => Lookup(style, family));
    }

    static SKTypeface Lookup(TextStyle style, string family)
    {
        var fontStyle = new SKFontStyle(
            style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        // Try each requested family in order; take the first that resolves to a real match, otherwise
        // let Skia fall back to its default for the first name so something always renders.
        foreach (var name in style.FontFamilies)
        {
            var candidate = SKTypeface.FromFamilyName(name, fontStyle);
            if (candidate != null &&
                string.Equals(candidate.FamilyName, name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return SKTypeface.FromFamilyName(family, fontStyle) ?? SKTypeface.Default;
    }

    static SKMatrix ToMatrix(Matrix3x2 m) =>
        new(m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1);

    static SKColor ToColor(Rgba color) =>
        new(color.R, color.G, color.B, color.A);

    public void Dispose()
    {
        canvas.Dispose();
        bitmap.Dispose();
    }
}
