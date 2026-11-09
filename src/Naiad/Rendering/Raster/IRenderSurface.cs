/// <summary>
/// The drawing sink the shared SVG walker (<see cref="SvgRasterizer"/>) paints into. Geometry arrives
/// already flattened to polyline <see cref="SubPath"/>s and styling already resolved, so a backend only
/// has to map a handful of primitives onto its own rasterizer and then <see cref="Encode"/> the result
/// as a PNG. This is the seam the alternative render packages (Naiad.Skia, Naiad.ImageSharp) build on:
/// every pixel of the parse → CSS cascade → transform → flatten → marker → label pipeline is shared, and
/// only the primitive sink and the final encode differ between them.
/// <para>
/// Each call carries the current transform so the surface can apply it natively — keeping stroke widths
/// and text sizes correctly scaled — rather than having the walker bake it into the coordinates.
/// Coordinates are otherwise in local user space (X right, Y down).
/// </para>
/// </summary>
interface IRenderSurface : IDisposable
{
    /// <summary>Fills the flattened contours under the given fill rule, transformed by <paramref name="transform"/>.</summary>
    void FillPath(IReadOnlyList<SubPath> subpaths, Matrix3x2 transform, Paint paint, FillRule rule, float opacity);

    /// <summary>Strokes the flattened contours with the given width (in local units) and optional dash pattern.</summary>
    void StrokePath(IReadOnlyList<SubPath> subpaths, Matrix3x2 transform, Rgba color, float width, IReadOnlyList<float>? dash, float opacity);

    /// <summary>
    /// Draws a single line of text anchored at (<paramref name="x"/>, <paramref name="y"/>) in local
    /// space. The surface resolves horizontal anchor and vertical baseline from <paramref name="style"/>
    /// using its own font metrics.
    /// </summary>
    void DrawText(string text, float x, float y, Matrix3x2 transform, TextStyle style);

    /// <summary>Encodes the painted surface as a PNG to <paramref name="stream"/>.</summary>
    void Encode(Stream stream);
}
