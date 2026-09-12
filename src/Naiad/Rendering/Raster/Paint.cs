/// <summary>
/// A resolved fill. The walker turns <c>fill="..."</c> (solid colour or <c>url(#id)</c> gradient
/// reference) into one of these, with gradient geometry already resolved to absolute user-space
/// coordinates, so the surfaces never have to look anything up — they just paint what they're handed.
/// </summary>
closed class Paint;

sealed class SolidPaint(Rgba color) : Paint
{
    public Rgba Color { get; } = color;
}

readonly record struct GradientStop(float Offset, Rgba Color);

sealed class LinearGradientPaint(Vector2 start, Vector2 end, IReadOnlyList<GradientStop> stops) : Paint
{
    public Vector2 Start { get; } = start;

    public Vector2 End { get; } = end;

    public IReadOnlyList<GradientStop> Stops { get; } = stops;
}

sealed class RadialGradientPaint(Vector2 center, float radius, IReadOnlyList<GradientStop> stops) : Paint
{
    public Vector2 Center { get; } = center;

    public float Radius { get; } = radius;

    public IReadOnlyList<GradientStop> Stops { get; } = stops;
}
